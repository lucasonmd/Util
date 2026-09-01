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

    public IDdsConnection Connect(DdsConnectionOptions options, ICaptureSink sink)
    {
        var connection = new RtiDdsConnection(options, sink);
        connection.Start();
        return connection;
    }

    public void Dispose()
    {
        // The factory's participants are owned by the connections themselves.
    }
}
