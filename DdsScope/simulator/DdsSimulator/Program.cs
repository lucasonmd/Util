using System.Diagnostics;
using Rti.Dds.Domain;

namespace DdsSimulator;

/// <summary>
/// Stand-alone DDS publisher used to exercise DdsScope against real discovery and real
/// DynamicData when the target system is not available.
///
/// Usage: DdsSimulator [--domain N] [--rate N] [--seconds N] [--no-typecode]
///                     [--stress] [--topics N] [--writers-per-topic N]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var domainId = ArgInt(args, "--domain", 0);
        var ratePerTopic = ArgInt(args, "--rate", 200);
        var seconds = ArgInt(args, "--seconds", 0);
        var noTypeCode = args.Contains("--no-typecode", StringComparer.OrdinalIgnoreCase);
        var stress = args.Contains("--stress", StringComparer.OrdinalIgnoreCase);
        var topicCount = ArgInt(args, "--topics", 200);
        var writersPerTopic = ArgInt(args, "--writers-per-topic", 5);

        Console.WriteLine($"DdsSimulator: domain {domainId}, {ratePerTopic} samples/s per writer.");

        // --no-typecode emulates a publisher that does not advertise its type, so the
        // viewer's "Type unavailable" path can be exercised.
        Console.WriteLine("type propagation: " + (noTypeCode ? "OFF" : "ON"));
        using var participant = noTypeCode
            ? DomainParticipantFactory.Instance.CreateParticipant(domainId)
            : DomainParticipantFactory.Instance.CreateParticipant(
                domainId,
                TrafficStreams.ParticipantQosWithTypePropagation());

        var creating = Stopwatch.StartNew();
        var streams = stress
            ? TrafficStreams.CreateStress(participant, topicCount, writersPerTopic)
            : TrafficStreams.CreateAll(participant);
        creating.Stop();

        if (stress)
        {
            Console.WriteLine(
                $"stress: {topicCount:N0} topics x {writersPerTopic:N0} writers = " +
                $"{streams.Count:N0} writers, built in {creating.Elapsed.TotalSeconds:N1}s.");
        }
        else
        {
            Console.WriteLine("Topics: " + string.Join(", ", streams.Select(s => s.TopicName)));
        }

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
