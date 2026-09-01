using Rti.Dds.Core.Policy;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Types.Dynamic;

namespace DdsSimulator;

/// <summary>One publishing stream: a writer plus the code that fills the next sample.</summary>
public sealed class TrafficStream
{
    private readonly DataWriter<DynamicData> writer;
    private readonly Action<DynamicData> fill;
    private readonly DynamicData sample;

    internal TrafficStream(string topicName, DataWriter<DynamicData> writer, Action<DynamicData> fill)
    {
        TopicName = topicName;
        this.writer = writer;
        this.fill = fill;
        sample = writer.CreateData();
    }

    public string TopicName { get; }

    public void WriteNext()
    {
        fill(sample);
        writer.Write(sample);
    }
}

/// <summary>
/// Builds a small set of topics that resemble the target system's traffic: keyed structs of
/// primitives, a nested struct, an enum and a sequence.
/// </summary>
public static class TrafficStreams
{
    /// <summary>
    /// Participant QoS a publisher needs for its types to be visible to a debug viewer.
    ///
    /// Connext 7.x defaults type_code_max_serialized_length to 0, which means discovery
    /// carries the type NAME but no type description - so no tool can decode the payload.
    /// Real systems have to opt in the same way for a viewer to show their fields.
    /// </summary>
    public static DomainParticipantQos ParticipantQosWithTypePropagation() =>
        DomainParticipantFactory.Instance.DefaultParticipantQos
            .WithResourceLimits(limits => limits.TypeCodeMaxSerializedLength = 8192);

    public static List<TrafficStream> CreateAll(DomainParticipant participant)
    {
        var publisher = participant.CreatePublisher();
        var factory = DynamicTypeFactory.Instance;

        return new List<TrafficStream>
        {
            CreateMount(participant, publisher, factory),
            CreatePlatform(participant, publisher, factory),
            CreateTrack(participant, publisher, factory)
        };
    }

    private static TrafficStream CreateMount(DomainParticipant participant, Publisher publisher, DynamicTypeFactory factory)
    {
        var status = factory.BuildEnum()
            .WithName("MountStatus")
            .AddMember(new EnumMember("Idle", 0))
            .AddMember(new EnumMember("Slewing", 1))
            .AddMember(new EnumMember("Tracking", 2))
            .AddMember(new EnumMember("Fault", 3))
            .Create();

        var type = factory.BuildStruct()
            .WithName("C_Rotational_Mount")
            .AddMember(new StructMember("SourceID", factory.GetPrimitiveType<int>(), isKey: true))
            .AddMember(new StructMember("ReferenceID", factory.GetPrimitiveType<int>()))
            .AddMember(new StructMember("Info", factory.GetPrimitiveType<int>()))
            .AddMember(new StructMember("Azimuth", factory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("Elevation", factory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("Status", status))
            .Create();

        var topic = participant.CreateTopic("C_Rotational_Mount", type);
        var writer = publisher.CreateDataWriter(topic);
        var random = new Random(1);
        var counter = 0;

        return new TrafficStream("C_Rotational_Mount", writer, sample =>
        {
            counter++;
            sample.SetValue("SourceID", 1 + counter % 3);
            sample.SetValue("ReferenceID", counter % 17);
            sample.SetValue("Info", counter % 40);
            sample.SetValue("Azimuth", Math.Round(random.NextDouble() * 360.0, 3));
            sample.SetValue("Elevation", Math.Round(random.NextDouble() * 90.0, 3));
            sample.SetValue("Status", counter % 4);
        });
    }

    private static TrafficStream CreatePlatform(DomainParticipant participant, Publisher publisher, DynamicTypeFactory factory)
    {
        var position = factory.BuildStruct()
            .WithName("Position")
            .AddMember(new StructMember("X", factory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("Y", factory.GetPrimitiveType<double>()))
            .Create();

        var type = factory.BuildStruct()
            .WithName("C_Platform_State")
            .AddMember(new StructMember("PlatformID", factory.GetPrimitiveType<int>(), isKey: true))
            .AddMember(new StructMember("Name", factory.CreateString(32)))
            .AddMember(new StructMember("Position", position))
            .AddMember(new StructMember("Heading", factory.GetPrimitiveType<float>()))
            .Create();

        var topic = participant.CreateTopic("C_Platform_State", type);

        // Deliberately BEST_EFFORT, so the viewer has to adapt its reader QoS for this topic.
        var writer = publisher.CreateDataWriter(
            topic,
            publisher.DefaultDataWriterQos.WithReliability(p => p.Kind = ReliabilityKind.BestEffort));

        var random = new Random(2);
        var counter = 0;

        return new TrafficStream("C_Platform_State", writer, sample =>
        {
            counter++;
            var id = 10 + counter % 2;
            sample.SetValue("PlatformID", id);
            sample.SetValue("Name", "platform-" + id);
            using (var loaned = sample.LoanValue("Position"))
            {
                loaned.Data.SetValue("X", Math.Round(random.NextDouble() * 1000.0, 2));
                loaned.Data.SetValue("Y", Math.Round(random.NextDouble() * 1000.0, 2));
            }

            sample.SetValue("Heading", (float)Math.Round(random.NextDouble() * 360.0, 2));
        });
    }

    private static TrafficStream CreateTrack(DomainParticipant participant, Publisher publisher, DynamicTypeFactory factory)
    {
        var type = factory.BuildStruct()
            .WithName("C_Track_Report")
            .AddMember(new StructMember("TrackID", factory.GetPrimitiveType<int>(), isKey: true))
            .AddMember(new StructMember("Quality", factory.GetPrimitiveType<short>()))
            .AddMember(new StructMember("Samples", factory.CreateSequence(factory.GetPrimitiveType<int>(), 16)))
            .Create();

        var topic = participant.CreateTopic("C_Track_Report", type);
        var writer = publisher.CreateDataWriter(topic);
        var counter = 0;

        return new TrafficStream("C_Track_Report", writer, sample =>
        {
            counter++;
            sample.SetValue("TrackID", 100 + counter % 4);
            sample.SetValue("Quality", (short)(counter % 100));

            var points = new int[4];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = counter + i;
            }

            sample.SetAnyValue("Samples", points);
        });
    }
}
