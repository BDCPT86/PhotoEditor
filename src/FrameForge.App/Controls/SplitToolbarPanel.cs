using System.Windows;
using System.Windows.Controls;

namespace FrameForge.App.Controls;

/// <summary>
/// A toolbar of two groups: the first child on the left, the second on the right. When both don't fit on one row
/// (for example with a side panel open), the right group moves to a second row, still right-aligned, instead of being
/// clipped.
/// </summary>
public sealed class SplitToolbarPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SplitToolbarPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(SplitToolbarPanel),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The smallest space between the groups on one row.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The space between the rows when the groups are stacked.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    private bool Stacked(double width) =>
        InternalChildren.Count == 2
        && InternalChildren[0].DesiredSize.Width + Gap + InternalChildren[1].DesiredSize.Width > width;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
        }

        if (InternalChildren.Count != 2)
        {
            return InternalChildren.Count == 1 ? InternalChildren[0].DesiredSize : default;
        }

        var left = InternalChildren[0].DesiredSize;
        var right = InternalChildren[1].DesiredSize;
        return Stacked(availableSize.Width)
            ? new Size(Math.Max(left.Width, right.Width), left.Height + RowGap + right.Height)
            : new Size(left.Width + Gap + right.Width, Math.Max(left.Height, right.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var left = InternalChildren[0];
        if (InternalChildren.Count == 1)
        {
            left.Arrange(new Rect(new Point(0, 0), left.DesiredSize));
            return finalSize;
        }

        var right = InternalChildren[1];
        if (Stacked(finalSize.Width))
        {
            left.Arrange(new Rect(0, 0, Math.Min(left.DesiredSize.Width, finalSize.Width), left.DesiredSize.Height));
            var y = left.DesiredSize.Height + RowGap;
            right.Arrange(new Rect(Math.Max(0, finalSize.Width - right.DesiredSize.Width), y, right.DesiredSize.Width, right.DesiredSize.Height));
        }
        else
        {
            var rowHeight = Math.Max(left.DesiredSize.Height, right.DesiredSize.Height);
            left.Arrange(new Rect(0, 0, left.DesiredSize.Width, rowHeight));
            right.Arrange(new Rect(finalSize.Width - right.DesiredSize.Width, 0, right.DesiredSize.Width, rowHeight));
        }

        return finalSize;
    }
}
