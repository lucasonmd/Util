using System.Text;
using DdsScope.Dds.Abstractions;
using Rti.Dds.Topics;
using RtiDataRepresentation = Rti.Dds.Core.Policy.DataRepresentation;
using RtiPartition = Rti.Dds.Core.Policy.Partition;

namespace DdsScope.Dds.Rti;

/// <summary>
/// Renders the publication built-in topic data of a discovered writer as display rows.
///
/// Only fields that RTI Connext 7.3.0 already publishes on the publication built-in topic
/// are used, so the writer detail pane looks the same on the target deployment.
/// </summary>
internal static class RtiQosDescriber
{
    public static IReadOnlyList<DdsQosItem> Describe(in PublicationBuiltinTopicData data)
    {
        var items = new List<DdsQosItem>(24);

        Add(items, "Identity", "Topic Name", data.TopicName);
        Add(items, "Identity", "Type Name", data.TypeName);
        Add(items, "Identity", "Publication Name", PublicationNameOrNull(data));
        Add(items, "Identity", "Writer Key", FormatKey(data.Key));
        Add(items, "Identity", "Participant Key", FormatKey(data.ParticipantKey));
        Add(items, "Identity", "Virtual GUID", data.VirtualGuid.ToString());

        Add(items, "QoS", "Reliability", data.Reliability?.Kind.ToString());
        Add(items, "QoS", "Durability", data.Durability?.Kind.ToString());
        Add(items, "QoS", "Ownership", data.Ownership?.Kind.ToString());
        Add(items, "QoS", "Ownership Strength", data.OwnershipStrength?.ToString());
        Add(items, "QoS", "Deadline", data.Deadline?.Period.ToString());
        Add(items, "QoS", "Latency Budget", data.LatencyBudget?.ToString());
        Add(items, "QoS", "Liveliness", data.Liveliness?.ToString());
        Add(items, "QoS", "Destination Order", data.DestinationOrder?.ToString());
        Add(items, "QoS", "Presentation", data.Presentation?.ToString());
        Add(items, "QoS", "Lifespan", data.Lifespan?.ToString());
        Add(items, "QoS", "Disable Positive Acks", data.DisablePositiveAcks.ToString());
        Add(items, "QoS", "Partition", FormatPartition(data.Partition));
        Add(items, "QoS", "Data Representation", FormatRepresentation(data.Representation));

        Add(items, "Data", "User Data", FormatBytes(data.UserData?.Value));
        Add(items, "Data", "Topic Data", FormatBytes(data.TopicData?.Value));
        Add(items, "Data", "Group Data", FormatBytes(data.GroupData?.Value));

        Add(items, "Vendor", "Product Version", data.ProductVersion.ToString());
        Add(items, "Vendor", "RTPS Vendor Id", data.RtpsVendorId.ToString());
        Add(items, "Vendor", "RTPS Protocol Version", data.RtpsProtocolVersion.ToString());

        return items;
    }

    /// <summary>
    /// The publication name, or null when the writer did not set one. The policy itself always
    /// renders as "[Name = , RoleName = ]", which would be a useless label in the tree.
    /// </summary>
    public static string PublicationNameOrNull(in PublicationBuiltinTopicData data)
    {
        var name = data.PublicationName?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static void Add(List<DdsQosItem> items, string group, string name, string value)
    {
        items.Add(new DdsQosItem(group, name, string.IsNullOrEmpty(value) ? "-" : value));
    }

    public static string FormatKey(BuiltinTopicKey key)
    {
        try
        {
            var bytes = key.ToArray();
            var builder = new StringBuilder(bytes.Length * 2 + 3);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    builder.Append('.');
                }

                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
        catch (Exception)
        {
            return key.ToString();
        }
    }

    private static string FormatPartition(RtiPartition partition)
    {
        if (partition?.Name == null || partition.Name.Count == 0)
        {
            return "(default)";
        }

        return string.Join(", ", partition.Name);
    }

    private static string FormatRepresentation(RtiDataRepresentation representation)
    {
        if (representation?.Value == null || representation.Value.Count == 0)
        {
            return "-";
        }

        return string.Join(", ", representation.Value.Select(DescribeRepresentation));
    }

    private static string DescribeRepresentation(short id) => id switch
    {
        0 => "XCDR",
        1 => "XML",
        2 => "XCDR2",
        _ => id.ToString()
    };

    private static string FormatBytes(IReadOnlyList<byte> bytes)
    {
        if (bytes == null || bytes.Count == 0)
        {
            return "-";
        }

        var builder = new StringBuilder(bytes.Count * 2);
        var limit = Math.Min(bytes.Count, 32);
        for (var i = 0; i < limit; i++)
        {
            builder.Append(bytes[i].ToString("x2"));
        }

        if (limit < bytes.Count)
        {
            builder.Append("... (").Append(bytes.Count).Append(" bytes)");
        }

        return builder.ToString();
    }
}
