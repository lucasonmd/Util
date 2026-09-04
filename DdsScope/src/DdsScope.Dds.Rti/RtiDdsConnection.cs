using System.Collections.Concurrent;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;
using DdsScope.Dds.Abstractions;
using Omg.Dds.Core;
using Rti.Dds.Core;
using Rti.Dds.Core.Policy;
using Rti.Dds.Core.Status;
using DurabilityKind = Omg.Dds.Core.Policy.DurabilityKind;
using Rti.Dds.Domain;
using Rti.Dds.Subscription;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;

namespace DdsScope.Dds.Rti;

/// <summary>
/// A live connection to one DDS domain.
///
/// Two dedicated threads, no middleware listeners:
///
///   discovery thread - waits on the publication built-in reader, creates a DynamicData
///                      reader for every new topic, tracks writers going on and offline.
///   receive thread   - waits on the status conditions of all dynamic readers, takes samples
///                      in batches and pushes decoded snapshots into the capture sink.
///
/// Listeners were rejected on purpose: a listener callback runs on a middleware thread and
/// anything slow there stalls reception for every reader in the participant. A WaitSet gives
/// the same wake-up with the work on a thread we own and can bound.
/// </summary>
public sealed class RtiDdsConnection : IDdsConnection
{
    private const int ReaderMaxSamples = 8192;

    /// <summary>
    /// Space reserved for type information received from discovery. Connext rejects values
    /// above 8192 for this policy, so this is the maximum a participant may ask for.
    /// </summary>
    private const int TypeCodeMaxSerializedLength = 8192;

    /// <summary>How often the publication built-in reader is polled for discovery changes.</summary>
    private static readonly TimeSpan DiscoveryPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DdsConnectionOptions options;
    private readonly ICaptureSink sink;
    private readonly CancellationTokenSource cancellation = new();

    private readonly ConcurrentDictionary<string, TopicEntry> topics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DdsWriterInfo> writers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<InstanceHandle, WriterRef> writersByHandle = new();
    private readonly ConcurrentQueue<Action> receiveThreadWork = new();

    private DomainParticipant participant;
    private Subscriber subscriber;
    private DataReader<PublicationBuiltinTopicData> publicationReader;

    private WaitSet receiveWaitSet;
    private GuardCondition receiveWakeup;
    private Thread discoveryThread;
    private Thread receiveThread;
    private bool started;

    private readonly Dictionary<Condition, TopicEntry> entriesByCondition = new();

    private int disposed;

    public RtiDdsConnection(DdsConnectionOptions options, ICaptureSink sink)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        DomainId = options.DomainId;
    }

    public int DomainId { get; }

    public IReadOnlyCollection<DdsTopicInfo> Topics => topics.Values.Select(e => e.Info).ToArray();

    public IReadOnlyCollection<DdsWriterInfo> Writers => writers.Values.ToArray();

    public event Action<DdsTopicInfo> TopicDiscovered;

    public event Action<DdsTopicInfo> TopicUpdated;

    public event Action<DdsWriterInfo> WriterDiscovered;

    public event Action<DdsWriterInfo> WriterUpdated;

    public event Action<DdsDiagnostic> Diagnostic;

    public void Start()
    {
        // Idempotent on purpose. A second call used to create a second receive thread and a
        // second discovery thread; two threads waiting on one WaitSet is a precondition
        // violation in Connext, so the loser threw on every wait, logged an error and slept
        // 100 ms - which cost roughly two thirds of the incoming samples.
        if (started)
        {
            return;
        }

        started = true;

        // Connext 7.x ships with type_code_max_serialized_length = 0, i.e. it neither sends
        // nor stores the type information carried by discovery. Without this the tool learns
        // topic and type NAMES but never a type it can decode with.
        //
        // Note this only covers OUR side. A publisher that leaves the policy at its default
        // will still announce no type at all, and its topics show up as "Type unavailable" -
        // see the diagnostic raised in TryResolveType.
        var participantQos = DomainParticipantFactory.Instance.DefaultParticipantQos
            .WithResourceLimits(limits => limits.TypeCodeMaxSerializedLength = TypeCodeMaxSerializedLength);

        participant = DomainParticipantFactory.Instance.CreateParticipant(DomainId, participantQos);

        // A wildcard partition makes the tool see writers in any partition, which is the
        // point of a debug viewer. It costs nothing on the publisher side.
        subscriber = participant.CreateSubscriber(
            participant.DefaultSubscriberQos.WithPartition(new Partition(new[] { "*" })));

        publicationReader = participant.BuiltinSubscriber
            .LookupDataReader<PublicationBuiltinTopicData>(Subscriber.PublicationBuiltinTopicName);

        if (publicationReader == null)
        {
            throw new InvalidOperationException("The publication built-in reader is not available.");
        }

        receiveWakeup = new GuardCondition();
        receiveWaitSet = new WaitSet();
        receiveWaitSet.AttachCondition(receiveWakeup);

        receiveThread = new Thread(ReceiveLoop)
        {
            Name = "ddsscope-receive",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        receiveThread.Start();

        discoveryThread = new Thread(DiscoveryLoop)
        {
            Name = "ddsscope-discovery",
            IsBackground = true
        };
        discoveryThread.Start();
    }

    // ---------------------------------------------------------------- discovery

    /// <summary>
    /// Polls the publication built-in reader.
    ///
    /// A WaitSet on the built-in reader's StatusCondition does NOT work here: attaching it and
    /// waiting yields no wake-ups and no samples, while plain polling of the same reader
    /// returns the announcements immediately. RTI drives the built-in readers through its own
    /// internal discovery machinery, and a status condition is not a supported way to observe
    /// them. Discovery events arrive a handful at a time, so a quarter-second poll costs
    /// nothing and behaves identically across Connext versions.
    /// </summary>
    private void DiscoveryLoop()
    {
        var token = cancellation.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                DrainPublicationReader();
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Report(DiagnosticSeverity.Error, "discovery", Describe(ex));
            }

            token.WaitHandle.WaitOne(DiscoveryPollInterval);
        }
    }

    private void DrainPublicationReader()
    {
        using var samples = publicationReader.Take();
        foreach (var sample in samples)
        {
            try
            {
                if (sample.Info.ValidData)
                {
                    OnWriterDiscovered(sample.Data, sample.Info.InstanceHandle);
                }
                else
                {
                    OnWriterGone(sample.Info.InstanceHandle);
                }
            }
            catch (Exception ex)
            {
                // One malformed announcement must not stop discovery of the rest.
                Report(DiagnosticSeverity.Warning, "discovery", Describe(ex));
            }
        }
    }

    private void OnWriterDiscovered(PublicationBuiltinTopicData data, InstanceHandle handle)
    {
        var topicName = data.TopicName;
        if (string.IsNullOrEmpty(topicName))
        {
            return;
        }

        var writerId = RtiQosDescriber.FormatKey(data.Key);
        var entry = EnsureTopic(topicName, data);

        var isNew = false;
        var writer = writers.GetOrAdd(writerId, id =>
        {
            isNew = true;
            var reference = new WriterRef(id, topicName, data.TypeName, RtiQosDescriber.PublicationNameOrNull(data));
            return new DdsWriterInfo(reference, RtiQosDescriber.FormatKey(data.ParticipantKey));
        });

        writer.LastSeen = DateTime.Now;
        writer.Qos = RtiQosDescriber.Describe(data);
        writersByHandle[handle] = writer.Reference;

        var cameBackOnline = !writer.IsOnline;
        writer.IsOnline = true;

        entry.TrackWriterQos(data);
        EnsureReaderMatches(entry);

        if (isNew)
        {
            WriterDiscovered?.Invoke(writer);
        }
        else if (cameBackOnline)
        {
            WriterUpdated?.Invoke(writer);
        }
    }

    private void OnWriterGone(InstanceHandle handle)
    {
        if (!writersByHandle.TryRemove(handle, out var reference))
        {
            return;
        }

        if (!writers.TryGetValue(reference.Id, out var writer))
        {
            return;
        }

        // The writer is kept, only marked offline: its captured history is still in the store
        // and the user is very likely still looking at it.
        writer.IsOnline = false;
        writer.LastSeen = DateTime.Now;
        WriterUpdated?.Invoke(writer);
    }

    private TopicEntry EnsureTopic(string topicName, PublicationBuiltinTopicData data)
    {
        if (topics.TryGetValue(topicName, out var existing))
        {
            if (existing.Info.TypeState != TopicTypeState.Available)
            {
                TryResolveType(existing, data);
            }

            return existing;
        }

        var info = new DdsTopicInfo(topicName, data.TypeName);
        var entry = new TopicEntry(info);

        if (!topics.TryAdd(topicName, entry))
        {
            return topics[topicName];
        }

        TopicDiscovered?.Invoke(info);
        TryResolveType(entry, data);
        return entry;
    }

    /// <summary>
    /// Resolves the topic's type from discovery. A topic whose type never arrives stays in the
    /// tree as "Type unavailable" - it must not stop the other topics from being captured.
    /// </summary>
    private void TryResolveType(TopicEntry entry, PublicationBuiltinTopicData data)
    {
        if (entry.Info.TypeState == TopicTypeState.Available)
        {
            return;
        }

        DynamicType type = null;
        try
        {
            type = data.DynamicType;
        }
        catch (Exception)
        {
            type = null;
        }

        if (type == null && !string.IsNullOrEmpty(data.TypeName))
        {
            try
            {
                type = participant.GetDynamicType(data.TypeName);
            }
            catch (Exception)
            {
                type = null;
            }
        }

        if (type == null)
        {
            if (entry.Info.TypeState != TopicTypeState.Unavailable)
            {
                entry.Info.TypeState = TopicTypeState.Unavailable;
                entry.Info.TypeStateDetail =
                    "No type information was propagated for " + (data.TypeName ?? "this topic") +
                    ". The publisher must enable type propagation " +
                    "(DomainParticipant QoS: resource_limits.type_code_max_serialized_length, " +
                    "which defaults to 0 in Connext 7.x).";
                TopicUpdated?.Invoke(entry.Info);
                Report(DiagnosticSeverity.Warning, entry.Info.TopicName,
                    "Type unavailable - samples cannot be decoded. " + entry.Info.TypeStateDetail);
            }

            return;
        }

        try
        {
            entry.TypeSchema = RtiSchemaBuilder.Build(type);
            entry.DynamicType = type;
            entry.Info.Schema = entry.TypeSchema.Schema;
            entry.Info.TypeState = TopicTypeState.Available;
            entry.Info.TypeStateDetail = null;
            TopicUpdated?.Invoke(entry.Info);
        }
        catch (Exception ex)
        {
            entry.Info.TypeState = TopicTypeState.Unavailable;
            entry.Info.TypeStateDetail = "Type could not be interpreted: " + Describe(ex);
            TopicUpdated?.Invoke(entry.Info);
            Report(DiagnosticSeverity.Warning, entry.Info.TopicName, entry.Info.TypeStateDetail);
        }
    }

    /// <summary>
    /// Creates or replaces the topic's reader so its QoS still matches the writers found so far.
    ///
    /// A single fixed reader QoS cannot match every writer: a RELIABLE reader is incompatible
    /// with a BEST_EFFORT writer, and ownership kind has to match exactly. So the reader is
    /// derived from what discovery reports, and rebuilt when a new writer changes the answer.
    /// </summary>
    private void EnsureReaderMatches(TopicEntry entry)
    {
        if (entry.Info.TypeState != TopicTypeState.Available || entry.DynamicType == null)
        {
            return;
        }

        var desiredReliability = entry.HasBestEffortWriter ? ReliabilityKind.BestEffort : ReliabilityKind.Reliable;
        var desiredOwnership = entry.HasSharedWriter || !entry.HasExclusiveWriter
            ? OwnershipKind.Shared
            : OwnershipKind.Exclusive;

        if (entry.HasSharedWriter && entry.HasExclusiveWriter)
        {
            Report(DiagnosticSeverity.Warning, entry.Info.TopicName,
                "Writers disagree on OWNERSHIP; reading the SHARED ones only.");
        }

        lock (entry.Gate)
        {
            if (entry.Reader != null &&
                entry.Reliability == desiredReliability &&
                entry.Ownership == desiredOwnership)
            {
                return;
            }

            var replacing = entry.Reader != null;
            if (replacing)
            {
                DetachAndDisposeReader(entry);
                Report(DiagnosticSeverity.Info, entry.Info.TopicName,
                    $"Recreating reader as {desiredReliability}/{desiredOwnership} to match new writers.");
            }

            try
            {
                CreateReader(entry, desiredReliability, desiredOwnership);
            }
            catch (Exception ex)
            {
                Report(DiagnosticSeverity.Error, entry.Info.TopicName,
                    "Could not create a reader: " + Describe(ex));
            }
        }
    }

    private void CreateReader(TopicEntry entry, ReliabilityKind reliability, OwnershipKind ownership)
    {
        entry.Topic ??= participant.LookupTopicDescription(entry.Info.TopicName) as Topic<DynamicData>
                        ?? participant.CreateTopic(entry.Info.TopicName, entry.DynamicType);

        var qos = subscriber.DefaultDataReaderQos
            .WithReliability(policy => policy.Kind = reliability)
            .WithOwnership(policy => policy.Kind = ownership)
            // VOLATILE: never ask a publisher to resend history just because a debug tool joined.
            .WithDurability(policy => policy.Kind = DurabilityKind.Volatile)
            // KEEP_ALL plus a deep queue: bursts are buffered until the receive thread drains
            // them, rather than being overwritten before we see them.
            .WithHistory(policy => policy.Kind = HistoryKind.KeepAll)
            .WithResourceLimits(policy =>
            {
                policy.MaxSamples = ReaderMaxSamples;
                policy.MaxInstances = ResourceLimits.LengthUnlimited;
                policy.MaxSamplesPerInstance = ResourceLimits.LengthUnlimited;
            });

        var reader = subscriber.CreateDataReader(entry.Topic, qos);

        entry.Reader = reader;
        entry.Decoder = new RtiPayloadDecoder(entry.TypeSchema, options.MaxCollectionElements);
        entry.Reliability = reliability;
        entry.Ownership = ownership;

        var condition = reader.StatusCondition;
        condition.EnabledStatuses = StatusMask.DataAvailable | StatusMask.SampleLost |
                                    StatusMask.RequestedIncompatibleQos;
        entry.Condition = condition;

        // Attaching happens on the receive thread so the WaitSet is only ever touched there.
        receiveThreadWork.Enqueue(() =>
        {
            entriesByCondition[condition] = entry;
            receiveWaitSet.AttachCondition(condition);
        });
        receiveWakeup.TriggerValue = true;
    }

    private void DetachAndDisposeReader(TopicEntry entry)
    {
        var condition = entry.Condition;
        var reader = entry.Reader;

        entry.Condition = null;
        entry.Reader = null;
        entry.Decoder = null;

        receiveThreadWork.Enqueue(() =>
        {
            if (condition != null)
            {
                entriesByCondition.Remove(condition);
                try
                {
                    receiveWaitSet.DetachCondition(condition);
                }
                catch (Exception)
                {
                    // Already detached during shutdown.
                }
            }

            try
            {
                reader?.Dispose();
            }
            catch (Exception)
            {
                // Nothing useful to do while tearing a reader down.
            }
        });
        receiveWakeup.TriggerValue = true;
    }

    // ------------------------------------------------------------------ receive

    private void ReceiveLoop()
    {
        var token = cancellation.Token;
        var timeout = Duration.FromMilliseconds(100);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var active = receiveWaitSet.Wait(timeout);

                RunPendingWork();

                if (receiveWakeup.TriggerValue)
                {
                    receiveWakeup.TriggerValue = false;
                }

                foreach (var condition in active)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (entriesByCondition.TryGetValue(condition, out var entry))
                    {
                        DrainReader(entry);
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Report(DiagnosticSeverity.Error, "receive", Describe(ex));
                Thread.Sleep(100);
            }
        }

        RunPendingWork();
    }

    private void RunPendingWork()
    {
        while (receiveThreadWork.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Report(DiagnosticSeverity.Warning, "receive", Describe(ex));
            }
        }
    }

    private void DrainReader(TopicEntry entry)
    {
        var reader = entry.Reader;
        var decoder = entry.Decoder;
        if (reader == null || decoder == null)
        {
            return;
        }

        try
        {
            var changes = reader.StatusChanges;

            if ((changes & StatusMask.SampleLost) != StatusMask.None)
            {
                var lost = reader.SampleLostStatus;
                if (lost.TotalCount.Change > 0)
                {
                    sink.ReportDdsSampleLost(lost.TotalCount.Change);
                }
            }

            if ((changes & StatusMask.RequestedIncompatibleQos) != StatusMask.None)
            {
                var status = reader.RequestedIncompatibleQosStatus;
                if (status.TotalCount.Change > 0)
                {
                    Report(DiagnosticSeverity.Warning, entry.Info.TopicName,
                        "A writer was rejected as QoS-incompatible (" + status.LastPolicy?.Name + ").");
                }
            }

            TakeSamples(entry, reader, decoder);
        }
        catch (Exception ex)
        {
            // Failure is isolated to this topic; every other reader keeps running.
            Report(DiagnosticSeverity.Error, entry.Info.TopicName, Describe(ex));
        }
    }

    private void TakeSamples(TopicEntry entry, DataReader<DynamicData> reader, RtiPayloadDecoder decoder)
    {
        using var samples = reader.Take();

        var handled = 0;
        foreach (var sample in samples)
        {
            if (!sample.Info.ValidData)
            {
                continue;
            }

            var receiveTime = DateTime.Now;
            var writer = ResolveWriter(entry, sample.Info.PublicationHandle);
            var snapshot = decoder.Decode(sample.Data, out var error);

            sink.Submit(
                receiveTime,
                ToDateTime(sample.Info.SourceTimestamp),
                writer,
                snapshot,
                error == null ? CaptureFlags.None : CaptureFlags.DecodeFailed,
                error);

            if (++handled >= options.MaxSamplesPerTake)
            {
                // Give the other readers a turn; the rest is still queued in the reader.
                break;
            }
        }
    }

    /// <summary>
    /// Maps a sample back to its writer. Samples can arrive marginally before the writer's
    /// announcement, so an unknown handle produces a placeholder instead of being dropped.
    /// </summary>
    private WriterRef ResolveWriter(TopicEntry entry, InstanceHandle handle)
    {
        if (writersByHandle.TryGetValue(handle, out var writer))
        {
            return writer;
        }

        return entry.PendingWriter ??= new WriterRef(
            "unknown-" + entry.Info.TopicName,
            entry.Info.TopicName,
            entry.Info.TypeName,
            "(undiscovered writer)");
    }

    private static DateTime? ToDateTime(Time time)
    {
        if (time.Seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(time.Seconds)
            .AddTicks(time.Nanoseconds / 100)
            .LocalDateTime;
    }

    private void Report(DiagnosticSeverity severity, string scope, string message) =>
        Diagnostic?.Invoke(new DdsDiagnostic(severity, scope, message));

    /// <summary>
    /// Text for a diagnostic raised from an exception.
    ///
    /// Several Connext exceptions carry an empty Message, which reached the Log pane as a
    /// bare severity with nothing to act on. The type name is always included so a repeating
    /// failure can at least be identified.
    /// </summary>
    private static string Describe(Exception ex)
    {
        if (ex == null)
        {
            return "(no exception)";
        }

        var message = ex.Message;
        var inner = ex.InnerException;
        var detail = string.IsNullOrWhiteSpace(message)
            ? ex.GetType().FullName
            : ex.GetType().Name + ": " + message;

        return inner == null ? detail : detail + " -> " + Describe(inner);
    }

    // ----------------------------------------------------------------- shutdown

    /// <summary>
    /// Shuts down outside-in: stop discovering, stop receiving, then dispose entities. Doing
    /// it the other way round is what produces ObjectDisposedException races in DDS clients.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        cancellation.Cancel();

        if (receiveWakeup != null)
        {
            try
            {
                receiveWakeup.TriggerValue = true;
            }
            catch (Exception)
            {
                // Already torn down.
            }
        }

        discoveryThread?.Join(TimeSpan.FromSeconds(3));
        receiveThread?.Join(TimeSpan.FromSeconds(3));

        foreach (var entry in topics.Values)
        {
            SafeDispose(entry.Reader);
        }

        SafeDispose(receiveWaitSet);
        SafeDispose(receiveWakeup);
        SafeDispose(subscriber);
        SafeDispose(participant);

        cancellation.Dispose();
    }

    private static void SafeDispose(IDisposable disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception)
        {
            // Shutdown is best-effort by design.
        }
    }

    /// <summary>Per-topic state: the reader, its decoder and what its writers require.</summary>
    private sealed class TopicEntry
    {
        public TopicEntry(DdsTopicInfo info)
        {
            Info = info;
        }

        public object Gate { get; } = new();

        public DdsTopicInfo Info { get; }

        public DynamicType DynamicType { get; set; }

        public RtiTypeSchema TypeSchema { get; set; }

        public Topic<DynamicData> Topic { get; set; }

        public DataReader<DynamicData> Reader { get; set; }

        public RtiPayloadDecoder Decoder { get; set; }

        public Condition Condition { get; set; }

        public ReliabilityKind Reliability { get; set; }

        public OwnershipKind Ownership { get; set; }

        public WriterRef PendingWriter { get; set; }

        public bool HasBestEffortWriter { get; private set; }

        public bool HasExclusiveWriter { get; private set; }

        public bool HasSharedWriter { get; private set; }

        public void TrackWriterQos(PublicationBuiltinTopicData data)
        {
            if (data.Reliability?.Kind == ReliabilityKind.BestEffort)
            {
                HasBestEffortWriter = true;
            }

            if (data.Ownership?.Kind == OwnershipKind.Exclusive)
            {
                HasExclusiveWriter = true;
            }
            else
            {
                HasSharedWriter = true;
            }
        }
    }
}
