using System.Windows;
using System.Windows.Controls;

namespace FrameForge.App.Controls;

/// <summary>Attached behaviour: keeps the selected item scrolled into view (e.g. after closing the viewer).</summary>
public static class ListBoxBehaviors
{
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
