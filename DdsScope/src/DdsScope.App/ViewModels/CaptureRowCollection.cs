using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DdsScope.App.ViewModels;

/// <summary>
/// The grid's row collection, mutated in batches.
///
/// ObservableCollection raises one CollectionChanged per item, and the grid does a unit of
/// work per notification - not per row. Replacing 15k rows one Add at a time costs about
/// 130 ms of notification handling before the grid has drawn anything, while the same rows
/// applied under a single Reset cost about 2 ms. Every bulk change therefore goes through
/// one of the two methods here.
/// </summary>
public sealed class CaptureRowCollection : ObservableCollection<CaptureRowViewModel>
{
    /// <summary>
    /// Bursts at or below this size keep per-item notifications. They are cheap at this
    /// scale and, unlike a Reset, they leave the grid's selection and scroll offset alone.
    ///
    /// Per-item costs roughly 9 us a row, so a burst this size is about 2 ms of notification
    /// handling inside a 100 ms tick. It is set high on purpose: a Reset makes the grid
    /// re-realise every visible row, and ten of those a second is what the eye reads as the
    /// view stuttering rather than scrolling.
    /// </summary>
    private const int ResetThreshold = 256;

    /// <summary>Replaces every row. Used when the query changes, never on the sample path.</summary>
    public void ResetTo(IReadOnlyList<CaptureRowViewModel> rows)
    {
        Items.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            Items.Add(rows[i]);
        }

        RaiseReset();
    }

    /// <summary>
    /// Puts <paramref name="fresh"/> (newest first) on top and trims the tail to
    /// <paramref name="max"/>.
    /// </summary>
    public void PrependAndTrim(IReadOnlyList<CaptureRowViewModel> fresh, int max)
    {
        var trim = Math.Max(0, Count + fresh.Count - max);

        // Only the insertions decide. Counting the trim here meant that once the grid had
        // filled to `max` - where trim equals fresh.Count forever after - the effective
        // threshold halved, and a steady few hundred samples a second put every single tick
        // through a Reset. The trimmed rows are at the far end of the list, off-screen while
        // live follow holds the viewport at the top, so they never justify that.
        if (fresh.Count <= ResetThreshold)
        {
            // Inserting from the back leaves the newest sample on top.
            for (var i = fresh.Count - 1; i >= 0; i--)
            {
                Insert(0, fresh[i]);
            }

            while (Count > max)
            {
                RemoveAt(Count - 1);
            }

            return;
        }

        for (var i = fresh.Count - 1; i >= 0; i--)
        {
            Items.Insert(0, fresh[i]);
        }

        while (Items.Count > max)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
