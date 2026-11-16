using System.Windows.Controls;
using System.Windows.Input;

namespace FrameForge.App.Controls;

/// <summary>
/// List box for the photo grid. WPF's spatial navigation in a wrap layout treats ← / → as "within this row",
/// so at a row's end the focus either stops or cycles back to the row's start. Photographers expect ← / → to
/// move through photos in order. This control handles ← / → (optionally with Shift) as previous/next item.
/// Shift extends a range from an anchor: the last photo reached without Shift (by arrow key or single click).
/// ↑ / ↓, Home/End, Page keys and mouse selection are left to the base list box, whose anchor is kept in sync.
/// </summary>
public sealed class PhotoGridListBox : ListBox
{
    private object? _anchor;
    private bool _moving;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key is Key.Left or Key.Right && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) && Items.Count > 0)
        {
            MoveLinear(e.Key == Key.Right ? 1 : -1, extend: modifiers == ModifierKeys.Shift);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);

        // A single selection made any other way (click, ↑/↓ without Shift, view model) becomes the new anchor.
        if (!_moving && SelectedItems.Count == 1)
        {
            _anchor = SelectedItem;
        }
    }

    private void MoveLinear(int step, bool extend)
    {
        var current = CurrentIndex();
        var target = Math.Clamp(current + step, 0, Items.Count - 1);
        var targetItem = Items[target];

        _moving = true;
        try
        {
            if (extend && SelectionMode != SelectionMode.Single)
            {
                var anchor = _anchor is null ? -1 : Items.IndexOf(_anchor);
                if (anchor < 0)
                {
                    anchor = Math.Max(current, 0);
                    _anchor = Items[anchor];
                }

                var (from, to) = anchor <= target ? (anchor, target) : (target, anchor);
                var range = new List<object>(to - from + 1);
                for (var i = from; i <= to; i++)
                {
                    range.Add(Items[i]);
                }

                SetSelectedItems(range);
            }
            else
            {
                SelectedItem = targetItem;
                _anchor = targetItem;
            }

            // Keep the base list box's anchor equal to ours, so Shift+↑/↓ and Shift+click agree.
            AnchorItem = _anchor;

            // The target may not be realised yet (virtualisation): bring it into view, then focus it.
            ScrollIntoView(targetItem);
            UpdateLayout();
            (ItemContainerGenerator.ContainerFromIndex(target) as ListBoxItem)?.Focus();
            AnchorItem = _anchor;
        }
        finally
        {
            _moving = false;
        }
    }

    /// <summary>Index of the focused tile, falling back to the selected index.</summary>
    private int CurrentIndex()
    {
        if (Keyboard.FocusedElement is ListBoxItem focused && ItemsControlFromItemContainer(focused) == this)
        {
            var index = ItemContainerGenerator.IndexFromContainer(focused);
            if (index >= 0)
            {
                return index;
            }
        }

        return SelectedIndex;
    }
}
