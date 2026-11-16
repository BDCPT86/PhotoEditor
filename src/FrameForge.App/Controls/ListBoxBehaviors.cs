using System.Windows;
using System.Windows.Controls;

namespace FrameForge.App.Controls;

/// <summary>Receives a multi-selection list box's selected items (which WPF cannot bind directly).</summary>
public interface ISelectedItemsSink
{
    void UpdateSelection(IReadOnlyList<object> items);

    /// <summary>Raised when the view model wants the list box to select these items (e.g. "select group").</summary>
    event EventHandler<IReadOnlyList<object>>? SelectionRequested;
}

/// <summary>
/// Attached behaviours for list boxes: keep the selected item in view, and pass multi-selection
/// to a view model through <see cref="ISelectedItemsSink"/>.
/// </summary>
public static class ListBoxBehaviors
{
    public static readonly DependencyProperty SelectedItemsSinkProperty = DependencyProperty.RegisterAttached(
        "SelectedItemsSink", typeof(ISelectedItemsSink), typeof(ListBoxBehaviors),
        new PropertyMetadata(null, OnSelectedItemsSinkChanged));

    public static ISelectedItemsSink? GetSelectedItemsSink(DependencyObject element) =>
        (ISelectedItemsSink?)element.GetValue(SelectedItemsSinkProperty);

    public static void SetSelectedItemsSink(DependencyObject element, ISelectedItemsSink? value) =>
        element.SetValue(SelectedItemsSinkProperty, value);

    private static void OnSelectedItemsSinkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        listBox.SelectionChanged -= PushSelection;
        if (e.OldValue is ISelectedItemsSink oldSink)
        {
            oldSink.SelectionRequested -= ApplyRequestedSelection(listBox);
        }

        if (e.NewValue is ISelectedItemsSink sink)
        {
            listBox.SelectionChanged += PushSelection;
            sink.SelectionRequested += ApplyRequestedSelection(listBox);
            sink.UpdateSelection(listBox.SelectedItems.Cast<object>().ToList());
        }
    }

    // One handler per list box, so it can be unsubscribed.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListBox, EventHandler<IReadOnlyList<object>>> RequestHandlers = [];

    private static EventHandler<IReadOnlyList<object>> ApplyRequestedSelection(ListBox listBox) =>
        RequestHandlers.GetValue(listBox, lb => (_, items) =>
        {
            if (lb.SelectionMode == SelectionMode.Single)
            {
                lb.SelectedItem = items.Count > 0 ? items[0] : null;
                return;
            }

            if (items.Count == 0)
            {
                lb.SelectedItems.Clear();
                return;
            }

            // Set the primary item first, then add the rest. SelectedItem is two-way bound to the view model;
            // if it changed after the others were added, the binding would write it back and
            // ListBox.SelectedItem's setter would deselect everything else.
            lb.SelectedItem = items[0];
            foreach (var item in items.Skip(1))
            {
                lb.SelectedItems.Add(item);
            }

            // Keyboard focus is deliberately left where it is: selection (the group) and focus (the photo the
            // user is on) are separate. Moving focus here, during the key press that requested the selection,
            // makes the list treat it as keyboard navigation and collapse the selection to the focused item.
        });

    private static void PushSelection(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && GetSelectedItemsSink(listBox) is { } sink)
        {
            sink.UpdateSelection(listBox.SelectedItems.Cast<object>().ToList());
        }
    }

    public static readonly DependencyProperty ScrollSelectionIntoViewProperty = DependencyProperty.RegisterAttached(
        "ScrollSelectionIntoView", typeof(bool), typeof(ListBoxBehaviors),
        new PropertyMetadata(false, OnScrollSelectionIntoViewChanged));

    public static bool GetScrollSelectionIntoView(DependencyObject element) =>
        (bool)element.GetValue(ScrollSelectionIntoViewProperty);

    public static void SetScrollSelectionIntoView(DependencyObject element, bool value) =>
        element.SetValue(ScrollSelectionIntoViewProperty, value);

    private static void OnScrollSelectionIntoViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        listBox.SelectionChanged -= OnSelectionChanged;
        if ((bool)e.NewValue)
        {
            listBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } item } listBox)
        {
            listBox.ScrollIntoView(item);
        }
    }
}
