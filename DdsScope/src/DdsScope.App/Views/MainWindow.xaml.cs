using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DdsScope.App.ViewModels;
using DdsScope.Core.Payload;
using DevExpress.Xpf.Grid;
using System.Windows.Data;

namespace DdsScope.App.Views;

public partial class MainWindow : Window
{
    private const string PayloadFieldPrefix = "pf";

    private readonly MainViewModel viewModel = new();
    private PayloadSchema currentSchema;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.ColumnsChanged += RebuildColumns;
        viewModel.RequestSavePath = AskForCsvPath;

        // Any deliberate interaction with the grid stops live follow, so the row the user is
        // reading stays exactly where it is.
        CaptureGrid.PreviewMouseWheel += (_, _) => viewModel.LiveFollow = false;
        CaptureGrid.PreviewMouseLeftButtonDown += (_, _) => viewModel.LiveFollow = false;
        CaptureGrid.PreviewKeyDown += OnGridKeyDown;

        RebuildColumns(null);

        if (App.StartupDomainId.HasValue)
        {
            viewModel.DomainId = App.StartupDomainId.Value;
        }

        if (App.StartupAutoConnect)
        {
            Loaded += (_, _) => viewModel.ConnectCommand.Execute(null);
        }

        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            viewModel.LiveFollow = false;
        }
    }

    /// <summary>
    /// Rebuilds the capture grid columns.
    ///
    /// With a single topic selected the payload fields become real columns; the all-topics
    /// view falls back to key plus a payload summary, because the schemas would not line up.
    /// </summary>
    private void RebuildColumns(PayloadSchema schema)
    {
        // Selecting another writer of the same topic, or a topic whose type was already
        // resolved, hands back the same schema instance. Rebuilding the columns for it would
        // throw away the user's column widths and order for no change in content.
        if (ReferenceEquals(currentSchema, schema) && CaptureGrid.Columns.Count > 0)
        {
            return;
        }

        currentSchema = schema;
        CaptureGrid.Columns.Clear();

        CaptureGrid.Columns.Add(new GridColumn
        {
            FieldName = nameof(CaptureRowViewModel.Sequence),
            Header = "Seq",
            Width = 70
        });

        var time = new GridColumn
        {
            FieldName = nameof(CaptureRowViewModel.ReceiveTime),
            Header = "Receive Time",
            Width = 110
        };
        time.EditSettings = new DevExpress.Xpf.Editors.Settings.TextEditSettings
        {
            DisplayFormat = "HH:mm:ss.fff"
        };
        CaptureGrid.Columns.Add(time);

        if (schema == null)
        {
            CaptureGrid.Columns.Add(new GridColumn
            {
                FieldName = nameof(CaptureRowViewModel.Topic),
                Header = "Topic",
                Width = 160
            });
            CaptureGrid.Columns.Add(new GridColumn
            {
                FieldName = nameof(CaptureRowViewModel.Writer),
                Header = "Writer",
                Width = 130
            });
            CaptureGrid.Columns.Add(new GridColumn
            {
                FieldName = nameof(CaptureRowViewModel.Key),
                Header = "Key",
                Width = 120
            });
            CaptureGrid.Columns.Add(new GridColumn
            {
                FieldName = nameof(CaptureRowViewModel.Summary),
                Header = "Payload",
                Width = 320
            });
            return;
        }

        CaptureGrid.Columns.Add(new GridColumn
        {
            FieldName = nameof(CaptureRowViewModel.Writer),
            Header = "Writer",
            Width = 130
        });

        foreach (var field in schema.Fields)
        {
            // Bound to the row's indexer, so a cell is formatted only when the grid realises
            // it. Column sorting, filtering, reordering and hiding keep working as usual.
            CaptureGrid.Columns.Add(new GridColumn
            {
                FieldName = PayloadFieldPrefix + field.Index.ToString(CultureInfo.InvariantCulture),
                Header = field.Path,
                Width = 110,
                ReadOnly = true,
                Binding = new Binding("[" + field.Index.ToString(CultureInfo.InvariantCulture) + "]")
                {
                    Mode = BindingMode.OneWay
                }
            });
        }
    }

    private string AskForCsvPath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            Title = "Export captured samples"
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnFilterEqualsClick(object sender, RoutedEventArgs e) => AppendFilterFromSelection("==");

    private void OnFilterNotEqualsClick(object sender, RoutedEventArgs e) => AppendFilterFromSelection("!=");

    /// <summary>
    /// Turns the selected payload node into a filter term and ANDs it onto whatever the user
    /// already typed, leaving their expression intact.
    /// </summary>
    private void AppendFilterFromSelection(string op)
    {
        if (DetailTree.SelectedItem is not SampleDetailNode node || node.FilterPath == null)
        {
            return;
        }

        var value = node.Value ?? string.Empty;

        // Enum cells read "Label (3)"; filtering on the label is what the user means.
        var parenthesis = value.IndexOf(" (", StringComparison.Ordinal);
        if (parenthesis > 0 && value.EndsWith(")", StringComparison.Ordinal))
        {
            value = value[..parenthesis];
        }

        var literal = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? value
            : "\"" + value.Replace("\"", "\\\"") + "\"";

        viewModel.AppendFilterTerm($"{node.FilterPath} {op} {literal}");
    }
}
