using DdsScope.Core.Payload;

namespace DdsScope.Core.Capture;

/// <summary>
/// What a DDS adapter is allowed to do with a received sample.
///
/// Deliberately tiny: the adapter hands over an already vendor-neutral snapshot and gets
/// out of the way, so nothing on the receive thread can block on capture, filtering or UI.
/// </summary>
public interface ICaptureSink
{
    /// <summary>
    /// Hands one received sample to the capture pipeline. Returns false when the sample was
    /// dropped because the internal queue is full. Never blocks.
    /// </summary>
    bool Submit(
        DateTime receiveTime,
        DateTime? sourceTimestamp,
        WriterRef writer,
        PayloadSnapshot payload,
        CaptureFlags flags = CaptureFlags.None,
        string error = null);

    /// <summary>Reports samples the middleware itself lost before we could take them.</summary>
    void ReportDdsSampleLost(long delta);
}
