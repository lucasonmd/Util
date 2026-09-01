using System.Threading.Channels;
using DdsScope.Core.Payload;

namespace DdsScope.Core.Capture;

/// <summary>
/// Carries samples from the DDS receive threads to the capture store.
///
/// The receive side only allocates a <see cref="CaptureRecord"/> and does a non-blocking
/// channel write; everything slower - storing, memory accounting, projection to the UI -
/// happens on the single consumer task. The channel is bounded and the producer never
/// waits, so a slow consumer costs counted drops rather than back-pressure on DDS.
/// </summary>
public sealed class CapturePipeline : ICaptureSink, IAsyncDisposable
{
    private readonly Channel<CaptureRecord> channel;
    private readonly CaptureStore store;
    private readonly CaptureStatistics statistics;
    private readonly int batchSize;
    private readonly CancellationTokenSource cancellation = new();

    private Task consumer;
    private long sequence;
    private volatile bool capturePaused;

    public CapturePipeline(CaptureStore store, CaptureStatistics statistics, int queueCapacity = 200_000, int batchSize = 1024)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        this.batchSize = batchSize;

        channel = Channel.CreateBounded<CaptureRecord>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            // The producer is a DDS receive thread: it must never block or drop silently.
            // TryWrite returning false is reported as a counted queue drop instead.
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// While set, samples are still received and counted but not stored. DDS connectivity
    /// and discovery are untouched.
    /// </summary>
    public bool CapturePaused
    {
        get => capturePaused;
        set => capturePaused = value;
    }

    public CaptureStore Store => store;

    public CaptureStatistics Statistics => statistics;

    public void Start()
    {
        consumer ??= Task.Factory.StartNew(
            () => ConsumeAsync(cancellation.Token),
            cancellation.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Called on a DDS receive thread. Returns false when the sample was dropped because the
    /// internal queue is full.
    /// </summary>
    public bool Submit(
        DateTime receiveTime,
        DateTime? sourceTimestamp,
        WriterRef writer,
        PayloadSnapshot payload,
        CaptureFlags flags = CaptureFlags.None,
        string error = null)
    {
        statistics.OnReceived();

        if (capturePaused)
        {
            statistics.OnPausedSkipped();
            return true;
        }

        if ((flags & CaptureFlags.DecodeFailed) != 0)
        {
            statistics.OnDecodeError();
        }

        var record = new CaptureRecord(
            Interlocked.Increment(ref sequence),
            receiveTime,
            sourceTimestamp,
            writer,
            payload,
            flags,
            error);

        if (channel.Writer.TryWrite(record))
        {
            return true;
        }

        statistics.OnQueueDropped();
        return false;
    }

    /// <summary>Reports samples the middleware lost before we could take them.</summary>
    public void ReportDdsSampleLost(long delta) => statistics.OnDdsLost(delta);

    private async Task ConsumeAsync(CancellationToken token)
    {
        var batch = new List<CaptureRecord>(batchSize);
        var reader = channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < batchSize && reader.TryRead(out var record))
                {
                    batch.Add(record);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                store.AppendRange(batch);
                statistics.OnStored(batch.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        cancellation.Cancel();

        if (consumer != null)
        {
            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        cancellation.Dispose();
    }
}
