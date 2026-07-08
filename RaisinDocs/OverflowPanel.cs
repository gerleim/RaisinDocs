using System.Windows;
using System.Windows.Controls;

namespace RaisinDocs;

internal class OverflowPanel : Panel
{
    public static readonly DependencyProperty IsOverflowButtonProperty =
        DependencyProperty.RegisterAttached("IsOverflowButton", typeof(bool), typeof(OverflowPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetIsOverflowButton(DependencyObject d) => (bool)d.GetValue(IsOverflowButtonProperty);
    public static void SetIsOverflowButton(DependencyObject d, bool value) => d.SetValue(IsOverflowButtonProperty, value);

    private readonly HashSet<UIElement> _overflowed = new();

    internal bool HasOverflow => _overflowed.Count > 0;
    internal bool IsOverflowed(UIElement element) => _overflowed.Contains(element);

    protected override Size MeasureOverride(Size availableSize)
    {
        _overflowed.Clear();

        UIElement? chevron = null;
        double maxHeight = 0;
        double totalWidth = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);

            if (GetIsOverflowButton(child))
                chevron = child;
            else
                totalWidth += child.DesiredSize.Width;
        }

        if (double.IsPositiveInfinity(availableSize.Width) || totalWidth <= availableSize.Width)
            return new Size(totalWidth, maxHeight);

        double chevronWidth = chevron?.DesiredSize.Width ?? 0;
        double budget = availableSize.Width - chevronWidth;
        double used = 0;
        bool overflowing = false;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (GetIsOverflowButton(child)) continue;

            if (overflowing)
            {
                _overflowed.Add(child);
                continue;
            }

            if (used + child.DesiredSize.Width > budget)
            {
                overflowing = true;
                _overflowed.Add(child);
                continue;
            }

            used += child.DesiredSize.Width;
        }

        // Hide trailing separators from the visible portion
        // (separators are ~9px wide vs 34px+ for buttons)
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            if (GetIsOverflowButton(child) || _overflowed.Contains(child)) continue;
            if (child.DesiredSize.Width < 15)
                _overflowed.Add(child);
            else
                break;
        }

        return new Size(availableSize.Width, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool hasOverflow = HasOverflow;
        double x = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (GetIsOverflowButton(child))
            {
                if (hasOverflow)
                {
                    double w = child.DesiredSize.Width;
                    child.Arrange(new Rect(finalSize.Width - w, 0, w, finalSize.Height));
                }
                else
                {
                    child.Arrange(default);
                }
                continue;
            }

            if (_overflowed.Contains(child))
            {
                child.Arrange(default);
                continue;
            }

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }

        return finalSize;
    }
}
