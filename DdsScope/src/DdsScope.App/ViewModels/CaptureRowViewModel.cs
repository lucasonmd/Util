using DdsScope.Core.Capture;

namespace DdsScope.App.ViewModels;

/// <summary>
/// One grid row.
///
/// A thin, immutable wrapper: the record itself is already the display model, and the
/// expensive strings (instance key, payload summary) are produced lazily by the record only
/// when a cell actually asks for them. Rows are never mutated after construction, so the grid
/// needs no change notification per row.
/// </summary>
public sealed class CaptureRowViewModel
{
    public CaptureRowViewModel(CaptureRecord record)
    {
        Record = record;
    }

    public CaptureRecord Record { get; }

    public long Sequence => Record.Sequence;

    public DateTime ReceiveTime => Record.ReceiveTime;

    public string Topic => Record.TopicName;

    public string Writer => Record.Writer?.DisplayName;

    public string Key => Record.InstanceKey;

    public string Summary => Record.BuildPayloadSummary();

    /// <summary>
    /// Value of one payload field, addressed by schema index.
    ///
    /// The dynamically generated grid columns bind to this indexer, so a row costs nothing
    /// extra when a topic with 20 fields is selected: cells are formatted only for the rows
    /// the grid actually realises.
    /// </summary>
    public object this[int fieldIndex]
    {
        get
        {
            var payload = Record.Payload;
            if (payload == null || (uint)fieldIndex >= (uint)payload.Schema.Fields.Count)
            {
                return null;
            }

            return payload.FormatValue(fieldIndex);
        }
    }

    public string Status => (Record.Flags & CaptureFlags.DecodeFailed) != 0
        ? "decode failed"
        : (Record.Flags & CaptureFlags.TypeUnavailable) != 0
            ? "type unavailable"
            : string.Empty;
}
