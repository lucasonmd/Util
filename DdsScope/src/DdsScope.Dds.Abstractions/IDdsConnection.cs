using DdsScope.Core.Capture;

namespace DdsScope.Dds.Abstractions;

/// <summary>Settings a connection is opened with.</summary>
public sealed class DdsConnectionOptions
{
    public int DomainId { get; set; }

    /// <summary>
    /// Upper bound on array/sequence elements copied per sample. Longer collections are
    /// recorded by length only, so one oversized field cannot stall the receive thread.
    /// </summary>
    public int MaxCollectionElements { get; set; } = 64;

    /// <summary>
    /// Maximum samples taken from one reader per wake-up. Bounds the time spent on a single
    /// noisy topic before the receive loop services the others.
    /// </summary>
    public int MaxSamplesPerTake { get; set; } = 256;
}

/// <summary>
/// A live connection to one DDS domain: discovery plus automatic readers for every
/// discovered topic.
///
/// Connection lifetime and capture lifetime are separate. Pausing capture leaves this
/// object - and all its discovery state - untouched.
/// </summary>
public interface IDdsConnection : IDisposable
{
    int DomainId { get; }

    /// <summary>Topics discovered so far. Safe to read from the UI thread.</summary>
    IReadOnlyCollection<DdsTopicInfo> Topics { get; }

    /// <summary>Writers discovered so far, including ones that have gone offline.</summary>
    IReadOnlyCollection<DdsWriterInfo> Writers { get; }

    /// <summary>Raised on a background thread when a topic is first seen.</summary>
    event Action<DdsTopicInfo> TopicDiscovered;

    /// <summary>Raised on a background thread when a topic's type state changes.</summary>
    event Action<DdsTopicInfo> TopicUpdated;

    /// <summary>Raised on a background thread when a writer is first seen.</summary>
    event Action<DdsWriterInfo> WriterDiscovered;

    /// <summary>Raised on a background thread when a writer goes offline or comes back.</summary>
    event Action<DdsWriterInfo> WriterUpdated;

    /// <summary>Raised on a background thread for isolated, non-fatal failures.</summary>
    event Action<DdsDiagnostic> Diagnostic;

    /// <summary>Starts discovery and sample reception.</summary>
    void Start();
}

/// <summary>Entry point of a DDS vendor adapter.</summary>
public interface IDdsRuntime : IDisposable
{
    /// <summary>Name and version of the underlying middleware, for the status bar.</summary>
    string Description { get; }

    /// <summary>
    /// Joins a domain. Received samples are pushed into <paramref name="sink"/> from the
    /// adapter's own receive threads.
    /// </summary>
    IDdsConnection Connect(DdsConnectionOptions options, ICaptureSink sink);
}
