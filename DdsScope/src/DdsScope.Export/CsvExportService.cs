using System.Globalization;
using System.Text;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;

namespace DdsScope.Export;

public sealed class CsvExportResult
{
    public CsvExportResult(string filePath, int recordCount, TimeSpan elapsed)
    {
        FilePath = filePath;
        RecordCount = recordCount;
        Elapsed = elapsed;
    }

    public string FilePath { get; }

    public int RecordCount { get; }

    public TimeSpan Elapsed { get; }
}

/// <summary>
/// Writes captured records to CSV on a background task.
///
/// The caller passes an already materialised list taken from an immutable store snapshot,
/// so a long export never blocks, slows or interacts with the DDS receive path.
/// </summary>
public sealed class CsvExportService
{
    /// <summary>Elements of an array/sequence written into a single cell before truncating.</summary>
    public int MaxCollectionElements { get; set; } = 16;

    public static string BuildFileName(string topicName, string writerName, DateTime now)
    {
        var parts = new List<string>();
        parts.Add(Sanitize(string.IsNullOrEmpty(topicName) ? "AllTopics" : topicName));

        if (!string.IsNullOrEmpty(writerName))
        {
            parts.Add(Sanitize(writerName));
        }

        parts.Add(now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        return string.Join("_", parts) + ".csv";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);
        }

        return builder.ToString();
    }

    public Task<CsvExportResult> ExportAsync(
        IReadOnlyList<CaptureRecord> records,
        string filePath,
        IProgress<double> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (records == null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        return Task.Run(() => Export(records, filePath, progress, cancellationToken), cancellationToken);
    }

    private CsvExportResult Export(
        IReadOnlyList<CaptureRecord> records,
        string filePath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;

        // A single shared schema lets every payload field become its own column. Mixed
        // selections fall back to a summary column, since the columns would not line up.
        var schema = FindCommonSchema(records);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));

        WriteHeader(writer, schema);

        var builder = new StringBuilder(256);
        for (var i = 0; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WriteRecord(writer, builder, records[i], schema);

            if (progress != null && (i & 0x3FF) == 0)
            {
                progress.Report((double)i / records.Count);
            }
        }

        writer.Flush();
        progress?.Report(1.0);

        return new CsvExportResult(filePath, records.Count, DateTime.UtcNow - started);
    }

    private static PayloadSchema FindCommonSchema(IReadOnlyList<CaptureRecord> records)
    {
        PayloadSchema schema = null;
        for (var i = 0; i < records.Count; i++)
        {
            var candidate = records[i].Payload?.Schema;
            if (candidate == null)
            {
                continue;
            }

            if (schema == null)
            {
                schema = candidate;
            }
            else if (!ReferenceEquals(schema, candidate))
            {
                return null;
            }
        }

        return schema;
    }

    private static void WriteHeader(TextWriter writer, PayloadSchema schema)
    {
        var builder = new StringBuilder();
        builder.Append("CaptureSequence,ReceiveTime,SourceTimestamp,Topic,TypeName,Writer,WriterId,InstanceKey");

        if (schema == null)
        {
            builder.Append(",PayloadSummary");
        }
        else
        {
            foreach (var field in schema.Fields)
            {
                // Nested members keep their dotted path, so Position.X is one column.
                builder.Append(',').Append(Escape(field.Path));
            }
        }

        builder.Append(",CaptureStatus");
        writer.WriteLine(builder.ToString());
    }

    private void WriteRecord(TextWriter writer, StringBuilder builder, CaptureRecord record, PayloadSchema schema)
    {
        builder.Clear();

        builder.Append(record.Sequence.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(record.ReceiveTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(record.SourceTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
        builder.Append(Escape(record.TopicName)).Append(',');
        builder.Append(Escape(record.Writer?.TypeName)).Append(',');
        builder.Append(Escape(record.Writer?.DisplayName)).Append(',');
        builder.Append(Escape(record.Writer?.Id)).Append(',');
        builder.Append(Escape(record.InstanceKey));

        if (schema == null)
        {
            builder.Append(',').Append(Escape(record.BuildPayloadSummary(int.MaxValue)));
        }
        else
        {
            var payload = record.Payload;
            foreach (var field in schema.Fields)
            {
                builder.Append(',');
                if (payload == null || !payload.IsPresent(field.Index))
                {
                    continue;
                }

                builder.Append(field.Kind == PayloadValueKind.Collection
                    ? Escape(FormatCollection(payload.GetCollection(field.Index)))
                    : Escape(payload.FormatValue(field.Index)));
            }
        }

        builder.Append(',').Append(Escape(DescribeStatus(record)));
        writer.WriteLine(builder.ToString());
    }

    private static string DescribeStatus(CaptureRecord record)
    {
        if ((record.Flags & CaptureFlags.TypeUnavailable) != 0)
        {
            return "type unavailable";
        }

        if ((record.Flags & CaptureFlags.DecodeFailed) != 0)
        {
            return "decode failed: " + record.Error;
        }

        return "ok";
    }

    /// <summary>
    /// Collections go into one cell as <c>[a; b; c]</c>, truncated with the true length, so a
    /// large sequence cannot explode the column count or the file size.
    /// </summary>
    private string FormatCollection(CollectionValue collection)
    {
        if (collection == null)
        {
            return string.Empty;
        }

        if (collection.Items.Count == 0)
        {
            return collection.Length == 0 ? "[]" : $"[{collection.Length} items]";
        }

        var take = Math.Min(MaxCollectionElements, collection.Items.Count);
        var builder = new StringBuilder("[");
        for (var i = 0; i < take; i++)
        {
            if (i > 0)
            {
                builder.Append("; ");
            }

            builder.Append(Convert.ToString(collection.Items[i], CultureInfo.InvariantCulture));
        }

        if (take < collection.Length)
        {
            builder.Append("; ... ").Append(collection.Length).Append(" items");
        }

        return builder.Append(']').ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
