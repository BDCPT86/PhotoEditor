using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FrameForge.App.ViewModels;

/// <summary>
/// The photos shown in the grid. <see cref="ReplaceAll"/> swaps the contents with a single Reset on the same
/// collection, so the grid's recycling panel reuses its tiles and only rebinds them. Handing the grid a new collection
/// instead made it discard and rebuild every realised tile on each view or sort change (0.3–0.5 s; see
/// docs/benchmarks/m07-performance.md).
/// </summary>
public sealed class TileCollection : ObservableCollection<PhotoTileViewModel>
{
    public TileCollection()
    {
    }

    public TileCollection(IEnumerable<PhotoTileViewModel> tiles)
        : base(tiles)
    {
    }

    public void ReplaceAll(IEnumerable<PhotoTileViewModel> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        CheckReentrancy();
        Items.Clear();
        foreach (var tile in tiles)
        {
            Items.Add(tile);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
