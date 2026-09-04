using DdsScope.Core.Capture;
using DdsScope.Dds.Abstractions;
using Rti.Dds.Domain;

namespace DdsScope.Dds.Rti;

/// <summary>
/// The RTI Connext implementation of <see cref="IDdsRuntime"/>.
///
/// Everything the application knows about the middleware arrives through this type, which is
/// why swapping Connext 7.7 for 7.3.0 is expected to be a package reference change plus, at
/// most, edits confined to this project.
/// </summary>
public sealed class RtiDdsRuntime : IDdsRuntime
{
    public string Description
    {
        get
        {
            var version = typeof(DomainParticipant).Assembly.GetName().Version;
            return "RTI Connext " + (version?.ToString() ?? "unknown");
        }
    }

    /// <summary>
    /// Joins the domain but does NOT start discovery: the caller has to subscribe to the
    /// discovery events first and then call <see cref="IDdsConnection.Start"/>.
    ///
    /// Starting here raced the caller. Built-in discovery delivers every writer that is
    /// already online in its first poll, so against publishers that were up before the tool
    /// those events fired with nothing attached and, since discovery does not re-announce
    /// them, the topic tree stayed empty for the whole session.
    /// </summary>
    public IDdsConnection Connect(DdsConnectionOptions options, ICaptureSink sink) =>
        new RtiDdsConnection(options, sink);

    public void Dispose()
    {
        // The factory's participants are owned by the connections themselves.
    }
}
