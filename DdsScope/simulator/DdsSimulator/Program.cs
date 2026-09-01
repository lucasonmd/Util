using System.Diagnostics;
using Rti.Dds.Domain;

namespace DdsSimulator;

/// <summary>
/// Stand-alone DDS publisher used to exercise DdsScope against real discovery and real
/// DynamicData when the target system is not available.
///
/// Usage: DdsSimulator [--domain N] [--rate N] [--seconds N] [--no-typecode]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var domainId = ArgInt(args, "--domain", 0);
        var ratePerTopic = ArgInt(args, "--rate", 200);
        var seconds = ArgInt(args, "--seconds", 0);
        var noTypeCode = args.Contains("--no-typecode", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"DdsSimulator: domain {domainId}, {ratePerTopic} samples/s per topic.");

        // --no-typecode emulates a publisher that does not advertise its type, so the
        // viewer's "Type unavailable" path can be exercised.
        Console.WriteLine("type propagation: " + (noTypeCode ? "OFF" : "ON"));
        using var participant = noTypeCode
            ? DomainParticipantFactory.Instance.CreateParticipant(domainId)
            : DomainParticipantFactory.Instance.CreateParticipant(
                domainId,
                TrafficStreams.ParticipantQosWithTypePropagation());

        var streams = TrafficStreams.CreateAll(participant);
        Console.WriteLine("Topics: " + string.Join(", ", streams.Select(s => s.TopicName)));
        Console.WriteLine("Publishing. Press Ctrl+C to stop.");

        var stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop = true;
        };

        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, ratePerTopic));
        var started = Stopwatch.StartNew();
        var published = 0L;
        var lastReport = TimeSpan.Zero;

        while (!stop)
        {
            foreach (var stream in streams)
            {
                stream.WriteNext();
                published++;
            }

            if (started.Elapsed - lastReport > TimeSpan.FromSeconds(2))
            {
                lastReport = started.Elapsed;
                Console.WriteLine($"  {published:N0} samples in {started.Elapsed.TotalSeconds:N0}s");
            }

            if (seconds > 0 && started.Elapsed.TotalSeconds >= seconds)
            {
                break;
            }

            Thread.Sleep(interval);
        }

        Console.WriteLine($"Stopped after {published:N0} samples.");
        return 0;
    }

    private static int ArgInt(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }
}
