namespace DdsScope.Core.Capture;

/// <summary>
/// An immutable view over the capture store, safe to enumerate from any thread while the
/// capture thread keeps appending.
/// </summary>
public sealed class CaptureSnapshotView
{
    private readonly CaptureRecord[][] chunks;
    private readonly int lastChunkCount;

    internal CaptureSnapshotView(CaptureRecord[][] chunks, int lastChunkCount, int count)
    {
        this.chunks = chunks;
        this.lastChunkCount = lastChunkCount;
        Count = count;
    }

    public static readonly CaptureSnapshotView Empty = new(Array.Empty<CaptureRecord[]>(), 0, 0);

    public int Count { get; }

    /// <summary>Oldest first.</summary>
    public IEnumerable<CaptureRecord> InOrder()
    {
        for (var c = 0; c < chunks.Length; c++)
        {
            var chunk = chunks[c];
            var end = c == chunks.Length - 1 ? lastChunkCount : chunk.Length;
            for (var i = 0; i < end; i++)
            {
                yield return chunk[i];
            }
        }
    }

    /// <summary>Newest first - the order the capture grid displays.</summary>
    public IEnumerable<CaptureRecord> Reversed()
    {
        for (var c = chunks.Length - 1; c >= 0; c--)
        {
            var chunk = chunks[c];
            var end = c == chunks.Length - 1 ? lastChunkCount : chunk.Length;
            for (var i = end - 1; i >= 0; i--)
            {
                yield return chunk[i];
            }
        }
    }
}

/// <summary>
/// Memory-bounded, append-only store of captured samples.
///
/// Records are appended in receive order and never replaced: a repeated instance key
/// produces a new record, because this is a capture log and not a state cache. When the
/// configured budget is exceeded the oldest chunk is dropped whole, which keeps eviction
/// O(1) instead of walking records one at a time.
/// </summary>
public sealed class CaptureStore
{
    public const int ChunkSize = 4096;

    private readonly object gate = new();
    private readonly List<CaptureRecord[]> chunks = new();
    private readonly List<long> chunkBytes = new();

    private int lastChunkCount;
    private long estimatedBytes;
    private long evictedCount;
    private int count;

    public CaptureStore(long maxBytes)
    {
        MaxBytes = maxBytes;
    }

    /// <summary>Memory budget in bytes. Changing it takes effect on the next append.</summary>
    public long MaxBytes { get; set; }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    public long EstimatedBytes
    {
        get
        {
            lock (gate)
            {
                return estimatedBytes;
            }
        }
    }

    /// <summary>Total records dropped to stay inside the memory budget.</summary>
    public long EvictedCount
    {
        get
        {
            lock (gate)
            {
                return evictedCount;
            }
        }
    }

    public void Append(CaptureRecord record)
    {
        lock (gate)
        {
            AppendLocked(record);
            EvictWhileOverBudget();
        }
    }

    /// <summary>Appends a batch under a single lock acquisition.</summary>
    public void AppendRange(IReadOnlyList<CaptureRecord> records)
    {
        lock (gate)
        {
            for (var i = 0; i < records.Count; i++)
            {
                AppendLocked(records[i]);
            }

            EvictWhileOverBudget();
        }
    }

    private void AppendLocked(CaptureRecord record)
    {
        if (chunks.Count == 0 || lastChunkCount == ChunkSize)
        {
            chunks.Add(new CaptureRecord[ChunkSize]);
            chunkBytes.Add(0);
            lastChunkCount = 0;
        }

        var lastIndex = chunks.Count - 1;
        chunks[lastIndex][lastChunkCount++] = record;
        chunkBytes[lastIndex] += record.EstimatedBytes;
        estimatedBytes += record.EstimatedBytes;
        count++;
    }

    private void EvictWhileOverBudget()
    {
        // The chunk being filled is never evicted, so a very small budget degrades to
        // "keep the newest chunk" instead of throwing away what was just captured.
        while (estimatedBytes > MaxBytes && chunks.Count > 1)
        {
            estimatedBytes -= chunkBytes[0];
            count -= chunks[0].Length;
            evictedCount += chunks[0].Length;
            chunks.RemoveAt(0);
            chunkBytes.RemoveAt(0);
        }
    }

    public CaptureSnapshotView Snapshot()
    {
        lock (gate)
        {
            if (count == 0)
            {
                return CaptureSnapshotView.Empty;
            }

            return new CaptureSnapshotView(chunks.ToArray(), lastChunkCount, count);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            chunks.Clear();
            chunkBytes.Clear();
            lastChunkCount = 0;
            estimatedBytes = 0;
            count = 0;
        }
    }
}
