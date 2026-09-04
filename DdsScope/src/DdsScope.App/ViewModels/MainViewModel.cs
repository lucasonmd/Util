using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using DdsScope.App.Infrastructure;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;
using DdsScope.Dds.Abstractions;
using DdsScope.Dds.Rti;
using DdsScope.Export;
using DdsScope.Filtering;

namespace DdsScope.App.ViewModels;

public sealed class MemoryLimitOption
{
    public MemoryLimitOption(string caption, long bytes)
    {
        Caption = caption;
        Bytes = bytes;
    }

    public string Caption { get; }

    public long Bytes { get; }

    public override string ToString() => Caption;
}

/// <summary>
/// Application state and the bridge between the capture pipeline and the views.
///
/// Two rules shape everything here:
///   - the DDS threads never touch this object; they only call into the capture sink, and
///     discovery events are queued and drained on the UI tick;
///   - the grid is refreshed on a timer in batches, never per sample.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxDisplayedRows = 20_000;
    private static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(100);

    private readonly Dispatcher dispatcher;
    private readonly CaptureStore store;
    private readonly CaptureStatistics statistics = new();
    private readonly CapturePipeline pipeline;
    private readonly IDdsRuntime runtime = new RtiDdsRuntime();
    private readonly CsvExportService exporter = new();
    private readonly DispatcherTimer timer;

    private readonly ConcurrentQueue<Action> uiWork = new();
    private readonly Dictionary<string, TopicNodeViewModel> topicNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopicNodeViewModel> writerNodes = new(StringComparer.Ordinal);

    private IDdsConnection connection;

    private CaptureQuery query = CaptureQuery.MatchAll;
    private DisplayFilter compiledFilter = DisplayFilter.PassAll;
    private long lastProjectedSequence;
    private bool queryDirty;
    private int rebuildGeneration;

    private int domainId;
    private bool isConnected;
    private bool isBusy;
    private bool viewPaused;
    private bool capturePaused;
    private bool liveFollow = true;
    private int pendingNewCount;
    private string displayFilterText = string.Empty;
    private string filterError;
    private string quickSearchText = string.Empty;
    private string statusMessage = "Not connected.";
    private CaptureRowViewModel selectedRow;
    private TopicNodeViewModel selectedNode;
    private MemoryLimitOption memoryLimit;

    public MainViewModel()
    {
        dispatcher = Dispatcher.CurrentDispatcher;

        MemoryLimits = new[]
        {
            new MemoryLimitOption("256 MB", 256L * 1024 * 1024),
            new MemoryLimitOption("512 MB", 512L * 1024 * 1024),
            new MemoryLimitOption("1 GB", 1024L * 1024 * 1024),
            new MemoryLimitOption("2 GB", 2048L * 1024 * 1024),
            new MemoryLimitOption("4 GB", 4096L * 1024 * 1024)
        };
        memoryLimit = MemoryLimits[2];

        store = new CaptureStore(memoryLimit.Bytes);
        pipeline = new CapturePipeline(store, statistics);
        pipeline.Start();

        ConnectCommand = new RelayCommand(Connect, () => !isConnected && !isBusy);
        DisconnectCommand = new RelayCommand(Disconnect, () => isConnected);
        ClearCommand = new RelayCommand(ClearCapture);
        JumpToLatestCommand = new RelayCommand(JumpToLatest);
        SaveCsvCommand = new RelayCommand(SaveCsv, () => !isBusy);
        ClearFilterCommand = new RelayCommand(() => DisplayFilterText = string.Empty);
        SelectAllTopicsCommand = new RelayCommand(() => SelectedNode = null);

        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UiInterval };
        timer.Tick += (_, _) => OnUiTick();
        timer.Start();
    }

    // ------------------------------------------------------------- collections

    public CaptureRowCollection Rows { get; } = new();

    public ObservableCollection<TopicNodeViewModel> TopicTree { get; } = new();

    public ObservableCollection<SampleDetailNode> SampleDetail { get; } = new();

    public ObservableCollection<DdsQosItem> WriterDetail { get; } = new();

    public ObservableCollection<DdsDiagnostic> Diagnostics { get; } = new();

    public IReadOnlyList<MemoryLimitOption> MemoryLimits { get; }

    // ---------------------------------------------------------------- commands

    public RelayCommand ConnectCommand { get; }

    public RelayCommand DisconnectCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand JumpToLatestCommand { get; }

    public RelayCommand SaveCsvCommand { get; }

    public RelayCommand ClearFilterCommand { get; }

    public RelayCommand SelectAllTopicsCommand { get; }

    /// <summary>Raised when the grid should rebuild its columns for a new payload schema.</summary>
    public event Action<PayloadSchema> ColumnsChanged;

    /// <summary>Asks the view for a CSV destination. Returns null when the user cancels.</summary>
    public Func<string, string> RequestSavePath { get; set; }

    // -------------------------------------------------------------- properties

    public int DomainId
    {
        get => domainId;
        set => Set(ref domainId, value);
    }

    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (Set(ref isConnected, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Freezes the grid only. Reception and capture keep running, so resuming shows the
    /// samples that arrived meanwhile.
    /// </summary>
    public bool ViewPaused
    {
        get => viewPaused;
        set
        {
            if (!Set(ref viewPaused, value))
            {
                return;
            }

            if (!value)
            {
                JumpToLatest();
            }
        }
    }

    /// <summary>
    /// Stops storing samples. The domain stays joined, discovery keeps running and skipped
    /// samples are counted separately from every other kind of loss.
    /// </summary>
    public bool CapturePaused
    {
        get => capturePaused;
        set
        {
            if (Set(ref capturePaused, value))
            {
                pipeline.CapturePaused = value;
            }
        }
    }

    /// <summary>
    /// While off, no row is added to the bound collection at all - which is what guarantees
    /// the viewport and the selected row cannot move while new samples arrive.
    /// </summary>
    public bool LiveFollow
    {
        get => liveFollow;
        set
        {
            if (!Set(ref liveFollow, value))
            {
                return;
            }

            if (value)
            {
                JumpToLatest();
            }
        }
    }

    public int PendingNewCount
    {
        get => pendingNewCount;
        private set
        {
            if (Set(ref pendingNewCount, value))
            {
                Raise(nameof(PendingBannerText));
                Raise(nameof(HasPendingRows));
            }
        }
    }

    public bool HasPendingRows => pendingNewCount > 0;

    public string PendingBannerText =>
        pendingNewCount > 0 ? $"↑ {pendingNewCount:N0} new samples" : string.Empty;

    public string DisplayFilterText
    {
        get => displayFilterText;
        set
        {
            if (!Set(ref displayFilterText, value))
            {
                return;
            }

            if (DisplayFilter.TryCompile(value, out var filter, out var error))
            {
                compiledFilter = filter;
                FilterError = null;
                queryDirty = true;
            }
            else
            {
                // Capture is untouched by a bad filter; the last good one stays in effect.
                FilterError = error;
            }
        }
    }

    public string FilterError
    {
        get => filterError;
        private set
        {
            if (Set(ref filterError, value))
            {
                Raise(nameof(HasFilterError));
            }
        }
    }

    public bool HasFilterError => !string.IsNullOrEmpty(filterError);

    public string QuickSearchText
    {
        get => quickSearchText;
        set
        {
            if (Set(ref quickSearchText, value))
            {
                queryDirty = true;
            }
        }
    }

    public CaptureRowViewModel SelectedRow
    {
        get => selectedRow;
        set
        {
            if (!Set(ref selectedRow, value))
            {
                return;
            }

            SampleDetail.Clear();
            foreach (var node in SampleDetailNode.Build(value?.Record))
            {
                SampleDetail.Add(node);
            }
        }
    }

    public TopicNodeViewModel SelectedNode
    {
        get => selectedNode;
        set
        {
            if (!Set(ref selectedNode, value))
            {
                return;
            }

            ApplySelection();
        }
    }

    public MemoryLimitOption MemoryLimit
    {
        get => memoryLimit;
        set
        {
            if (Set(ref memoryLimit, value) && value != null)
            {
                store.MaxBytes = value.Bytes;
                Raise(nameof(MemoryText));
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public string RuntimeDescription => runtime.Description;

    // ------------------------------------------------------------- status bar

    public int TopicCount => topicNodes.Count;

    public int WriterCount => writerNodes.Count;

    public string RateText => statistics.ReceiveRatePerSecond.ToString("N0", CultureInfo.CurrentCulture) + "/s";

    public string MemoryText =>
        $"{store.EstimatedBytes / (1024.0 * 1024.0):N0} MB / {memoryLimit.Bytes / (1024 * 1024):N0} MB";

    public string StoredText => store.Count.ToString("N0", CultureInfo.CurrentCulture);

    public string DdsLostText => statistics.DdsLost.ToString("N0", CultureInfo.CurrentCulture);

    public string QueueDropText => statistics.QueueDropped.ToString("N0", CultureInfo.CurrentCulture);

    public string EvictedText => store.EvictedCount.ToString("N0", CultureInfo.CurrentCulture);

    public string PausedSkippedText => statistics.PausedSkipped.ToString("N0", CultureInfo.CurrentCulture);

    // --------------------------------------------------------------- lifecycle

    private void Connect()
    {
        if (isConnected)
        {
            return;
        }

        try
        {
            isBusy = true;
            ConnectCommand.RaiseCanExecuteChanged();

            var options = new DdsConnectionOptions { DomainId = DomainId };
            connection = runtime.Connect(options, pipeline);

            connection.TopicDiscovered += OnTopicDiscovered;
            connection.TopicUpdated += OnTopicUpdated;
            connection.WriterDiscovered += OnWriterDiscovered;
            connection.WriterUpdated += OnWriterUpdated;
            connection.Diagnostic += OnDiagnostic;

            IsConnected = true;
            StatusMessage = $"Connected to domain {DomainId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Connect failed: " + ex.Message;
            connection = null;
        }
        finally
        {
            isBusy = false;
            ConnectCommand.RaiseCanExecuteChanged();
        }
    }

    private void Disconnect()
    {
        if (connection == null)
        {
            return;
        }

        connection.TopicDiscovered -= OnTopicDiscovered;
        connection.TopicUpdated -= OnTopicUpdated;
        connection.WriterDiscovered -= OnWriterDiscovered;
        connection.WriterUpdated -= OnWriterUpdated;
        connection.Diagnostic -= OnDiagnostic;

        connection.Dispose();
        connection = null;

        IsConnected = false;
        StatusMessage = "Disconnected. Captured samples are still available.";
    }

    private void ClearCapture()
    {
        store.Clear();
        Rows.Clear();
        PendingNewCount = 0;
        SelectedRow = null;
        StatusMessage = "Capture cleared.";
    }

    // -------------------------------------------------- discovery event bridge

    private void OnTopicDiscovered(DdsTopicInfo topic) => uiWork.Enqueue(() => AddTopicNode(topic));

    private void OnTopicUpdated(DdsTopicInfo topic) => uiWork.Enqueue(() =>
    {
        if (topicNodes.TryGetValue(topic.TopicName, out var node))
        {
            node.RefreshFromTopic();
        }
        else
        {
            AddTopicNode(topic);
        }

        if (selectedNode != null && selectedNode.IsTopic && selectedNode.TopicName == topic.TopicName)
        {
            ColumnsChanged?.Invoke(topic.Schema);
        }
    });

    private void OnWriterDiscovered(DdsWriterInfo writer) => uiWork.Enqueue(() => AddWriterNode(writer));

    private void OnWriterUpdated(DdsWriterInfo writer) => uiWork.Enqueue(() =>
    {
        if (writerNodes.TryGetValue(writer.Id, out var node))
        {
            node.RefreshFromWriter();
        }
    });

    private void OnDiagnostic(DdsDiagnostic diagnostic) => uiWork.Enqueue(() =>
    {
        Diagnostics.Insert(0, diagnostic);
        while (Diagnostics.Count > 500)
        {
            Diagnostics.RemoveAt(Diagnostics.Count - 1);
        }

        if (diagnostic.Severity != DiagnosticSeverity.Info)
        {
            StatusMessage = diagnostic.ToString();
        }
    });

    private TopicNodeViewModel AddTopicNode(DdsTopicInfo topic)
    {
        if (topicNodes.TryGetValue(topic.TopicName, out var existing))
        {
            existing.RefreshFromTopic();
            return existing;
        }

        var node = TopicNodeViewModel.ForTopic(topic);
        topicNodes[topic.TopicName] = node;

        // Keep the tree alphabetical so a busy domain stays readable.
        var index = 0;
        while (index < TopicTree.Count &&
               string.CompareOrdinal(TopicTree[index].Caption, node.Caption) < 0)
        {
            index++;
        }

        TopicTree.Insert(index, node);
        Raise(nameof(TopicCount));
        return node;
    }

    private void AddWriterNode(DdsWriterInfo writer)
    {
        if (writerNodes.ContainsKey(writer.Id))
        {
            return;
        }

        if (!topicNodes.TryGetValue(writer.TopicName, out var topicNode))
        {
            topicNode = AddTopicNode(new DdsTopicInfo(writer.TopicName, writer.TypeName));
        }

        var node = TopicNodeViewModel.ForWriter(writer);
        writerNodes[writer.Id] = node;
        topicNode.Children.Add(node);
        Raise(nameof(WriterCount));
    }

    // -------------------------------------------------------------- projection

    private void OnUiTick()
    {
        while (uiWork.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                StatusMessage = "UI update failed: " + ex.Message;
            }
        }

        statistics.SampleRate();
        RaiseStatusBar();

        if (queryDirty)
        {
            queryDirty = false;
            RebuildRows();
            return;
        }

        if (!viewPaused)
        {
            ProjectNewRows();
        }
    }

    private void RaiseStatusBar()
    {
        Raise(nameof(RateText));
        Raise(nameof(MemoryText));
        Raise(nameof(StoredText));
        Raise(nameof(DdsLostText));
        Raise(nameof(QueueDropText));
        Raise(nameof(EvictedText));
        Raise(nameof(PausedSkippedText));
    }

    /// <summary>
    /// Adds only the records captured since the last tick. Steady-state cost is proportional
    /// to the new samples, not to the size of the capture store.
    /// </summary>
    private void ProjectNewRows()
    {
        var snapshot = store.Snapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        List<CaptureRowViewModel> fresh = null;
        var newest = lastProjectedSequence;
        var scanned = 0;

        foreach (var record in snapshot.Reversed())
        {
            if (record.Sequence <= lastProjectedSequence)
            {
                break;
            }

            if (scanned++ == 0)
            {
                newest = record.Sequence;
            }

            if (!query.Matches(record))
            {
                continue;
            }

            (fresh ??= new List<CaptureRowViewModel>()).Add(new CaptureRowViewModel(record));
        }

        lastProjectedSequence = newest;

        if (fresh == null)
        {
            return;
        }

        if (!liveFollow)
        {
            PendingNewCount += fresh.Count;
            return;
        }

        // fresh is newest-first; the collection batches the notifications for a large burst.
        Rows.PrependAndTrim(fresh, MaxDisplayedRows);
    }

    private void JumpToLatest()
    {
        liveFollow = true;
        Raise(nameof(LiveFollow));
        RebuildRows();
    }

    /// <summary>
    /// Re-runs the whole query over the store. Used when the filter, search or selection
    /// changes - not on the sample path.
    /// </summary>
    private void RebuildRows()
    {
        query = new CaptureQuery(BuildSelectionFilter(), compiledFilter, quickSearchText);

        var snapshot = store.Snapshot();
        var generation = ++rebuildGeneration;
        var localQuery = query;

        Task.Run(() =>
        {
            var rows = new List<CaptureRowViewModel>(Math.Min(snapshot.Count, MaxDisplayedRows));
            long newest = 0;
            var first = true;

            foreach (var record in snapshot.Reversed())
            {
                if (first)
                {
                    newest = record.Sequence;
                    first = false;
                }

                if (!localQuery.Matches(record))
                {
                    continue;
                }

                rows.Add(new CaptureRowViewModel(record));
                if (rows.Count >= MaxDisplayedRows)
                {
                    break;
                }
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != rebuildGeneration)
                {
                    // A newer rebuild already started; this result is stale.
                    return;
                }

                Rows.ResetTo(rows);

                lastProjectedSequence = Math.Max(lastProjectedSequence, newest);
                PendingNewCount = 0;
            }));
        });
    }

    private SelectionFilter BuildSelectionFilter()
    {
        if (selectedNode == null)
        {
            return SelectionFilter.All;
        }

        return selectedNode.IsTopic
            ? new SelectionFilter(selectedNode.TopicName, null)
            : new SelectionFilter(selectedNode.TopicName, selectedNode.WriterId);
    }

    private void ApplySelection()
    {
        WriterDetail.Clear();
        if (selectedNode?.Writer != null)
        {
            foreach (var item in selectedNode.Writer.Qos)
            {
                WriterDetail.Add(item);
            }
        }

        // With a single topic selected the grid can show that type's fields as real columns.
        var schema = selectedNode?.Topic?.Schema;
        if (schema == null && selectedNode?.Writer != null &&
            topicNodes.TryGetValue(selectedNode.TopicName, out var topicNode))
        {
            schema = topicNode.Topic?.Schema;
        }

        ColumnsChanged?.Invoke(schema);

        // Selection is a direct click, so it refreshes now rather than on the next tick:
        // the scan runs off the UI thread anyway, and a deferred rebuild made the grid look
        // frozen for up to one tick after every click. Typed filters stay coalesced.
        queryDirty = false;
        RebuildRows();
    }

    /// <summary>Appends a term to the display filter, used by the payload context menu.</summary>
    public void AppendFilterTerm(string term)
    {
        DisplayFilterText = DisplayFilter.AndWith(displayFilterText, term);
    }

    // ------------------------------------------------------------------ export

    private void SaveCsv()
    {
        var suggested = CsvExportService.BuildFileName(
            selectedNode?.TopicName,
            selectedNode?.Writer?.DisplayName,
            DateTime.Now);

        var path = RequestSavePath?.Invoke(suggested);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Snapshot and filter here, on the UI thread, so the exported set is exactly what the
        // user is looking at; the file itself is written on a background task.
        var snapshot = store.Snapshot();
        var localQuery = query;
        var records = new List<CaptureRecord>();
        foreach (var record in snapshot.InOrder())
        {
            if (localQuery.Matches(record))
            {
                records.Add(record);
            }
        }

        if (records.Count == 0)
        {
            StatusMessage = "Nothing to export for the current selection and filter.";
            return;
        }

        isBusy = true;
        SaveCsvCommand.RaiseCanExecuteChanged();
        StatusMessage = $"Exporting {records.Count:N0} samples...";

        _ = ExportAsync(records, path);
    }

    private async Task ExportAsync(IReadOnlyList<CaptureRecord> records, string path)
    {
        try
        {
            var result = await exporter.ExportAsync(records, path).ConfigureAwait(true);
            StatusMessage =
                $"Exported {result.RecordCount:N0} samples to {Path.GetFileName(result.FilePath)} " +
                $"in {result.Elapsed.TotalSeconds:N1}s.";
        }
        catch (Exception ex)
        {
            // An export failure is contained: capture never noticed it happened.
            StatusMessage = "Export failed: " + ex.Message;
        }
        finally
        {
            isBusy = false;
            SaveCsvCommand.RaiseCanExecuteChanged();
        }
    }

    // ---------------------------------------------------------------- shutdown

    public void Dispose()
    {
        timer.Stop();
        Disconnect();
        pipeline.DisposeAsync().AsTask().GetAwaiter().GetResult();
        runtime.Dispose();
    }
}
