using System.Diagnostics;

namespace DdsScope.Core.Capture;

/// <summary>
/// Counters for the four distinct ways a sample can fail to reach the capture store.
/// They are deliberately separate: middleware loss, internal queue overflow, memory
/// eviction and a deliberate capture pause mean very different things when debugging.
/// </summary>
public sealed class CaptureStatistics
{
    private long received;
    private long stored;
    private long ddsLost;
    private long queueDropped;
    private long pausedSkipped;
    private long decodeErrors;

    private long rateBaseline;
    private long rateBaselineTicks = Stopwatch.GetTimestamp();
    private double receiveRate;

    /// <summary>Samples taken off the wire, whatever happened to them afterwards.</summary>
    public long Received => Interlocked.Read(ref received);

    /// <summary>Samples appended to the capture store.</summary>
    public long Stored => Interlocked.Read(ref stored);

    /// <summary>Samples the middleware itself reported as lost (SAMPLE_LOST status).</summary>
    public long DdsLost => Interlocked.Read(ref ddsLost);

    /// <summary>Samples received but dropped because the internal queue was full.</summary>
    public long QueueDropped => Interlocked.Read(ref queueDropped);

    /// <summary>Samples received but deliberately not stored, because capture was paused.</summary>
    public long PausedSkipped => Interlocked.Read(ref pausedSkipped);

    /// <summary>Samples stored with a decode failure flag.</summary>
    public long DecodeErrors => Interlocked.Read(ref decodeErrors);

    public double ReceiveRatePerSecond => Volatile.Read(ref receiveRate);

    public void OnReceived() => Interlocked.Increment(ref received);

    public void OnStored(long delta) => Interlocked.Add(ref stored, delta);

    public void OnDdsLost(long delta) => Interlocked.Add(ref ddsLost, delta);

    public void OnQueueDropped() => Interlocked.Increment(ref queueDropped);

    public void OnPausedSkipped() => Interlocked.Increment(ref pausedSkipped);

    public void OnDecodeError() => Interlocked.Increment(ref decodeErrors);

    /// <summary>Recomputes the receive rate. Called from the UI tick, not the receive path.</summary>
    public void SampleRate()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - rateBaselineTicks) / (double)Stopwatch.Frequency;
        if (elapsed <= 0.05)
        {
            return;
        }

        var current = Received;
        Volatile.Write(ref receiveRate, (current - rateBaseline) / elapsed);
        rateBaseline = current;
        rateBaselineTicks = now;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref received, 0);
        Interlocked.Exchange(ref stored, 0);
        Interlocked.Exchange(ref ddsLost, 0);
        Interlocked.Exchange(ref queueDropped, 0);
        Interlocked.Exchange(ref pausedSkipped, 0);
        Interlocked.Exchange(ref decodeErrors, 0);
        rateBaseline = 0;
        rateBaselineTicks = Stopwatch.GetTimestamp();
        Volatile.Write(ref receiveRate, 0);
    }
}
