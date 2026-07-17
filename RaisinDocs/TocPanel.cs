using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RaisinDocs;

internal class TocPanel : FrameworkElement
{
    private const double ItemHeight = 26.0;
    private const double IndentPerLevel = 14.0;
    private const double LeftPadding = 10.0;
    private const double TopPadding = 6.0;
    private const double FontSize = 13.0;
    private const double H1FontSize = 14.0;

    internal DocsCanvas? Canvas { get; set; }
    internal event Action<int>? NavigateRequested;

    private List<TocEntry> _entries = new();
    private int _cachedLayoutVersion = -1;
    private int _activeHeadingBlock = -1;
    private int _hoverIndex = -1;
    private double _scrollOffset;

    private Brush _background = Brushes.Transparent;
    private Brush _foreground = Brushes.Gray;
    private Brush _activeForeground = Brushes.White;
    private Brush _activeBackground = Brushes.Transparent;
    private Brush _hoverBackground = Brushes.Transparent;
    private Pen _borderPen = new(Brushes.Gray, 1);
    private Brush _dimForeground = Brushes.Gray;

    public TocPanel()
    {
        ClipToBounds = true;
    }

    internal void ApplyTheme(Brush background, Brush foreground, Brush syntax, Brush codeBackground)
    {
        _background = background;
        _activeForeground = foreground;
        _dimForeground = syntax;

        var fgColor = ((SolidColorBrush)foreground).Color;
        var bgColor = ((SolidColorBrush)background).Color;

        _foreground = Frozen(Color.FromArgb(200, fgColor.R, fgColor.G, fgColor.B));

        bool isDark = bgColor.R < 128;
        _activeBackground = Frozen(isDark
            ? Color.FromArgb(30, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0));
        _hoverBackground = Frozen(isDark
            ? Color.FromArgb(20, 255, 255, 255)
            : Color.FromArgb(15, 0, 0, 0));

        var borderColor = ((SolidColorBrush)syntax).Color;
        var borderBrush = Frozen(Color.FromArgb(100, borderColor.R, borderColor.G, borderColor.B));
        _borderPen = new Pen(borderBrush, 1);
        _borderPen.Freeze();

        InvalidateVisual();
    }

    internal void Refresh()
    {
        if (Canvas == null) return;

        bool entriesChanged = false;
        int layoutVersion = Canvas.MinimapLayoutVersion;
        if (layoutVersion != _cachedLayoutVersion)
        {
            _cachedLayoutVersion = layoutVersion;
            _entries = Canvas.GetTocEntries();
            entriesChanged = true;
        }

        int newActive = Canvas.GetCurrentHeadingBlock();
        if (newActive != _activeHeadingBlock || entriesChanged)
        {
            _activeHeadingBlock = newActive;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(_background, null, new Rect(0, 0, w, h));
        dc.DrawLine(_borderPen, new Point(w - 0.5, 0), new Point(w - 0.5, h));

        if (_entries.Count == 0)
        {
            var emptyText = new FormattedText("No headings",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                TextMeasurer.NormalTypeface, FontSize, _dimForeground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            emptyText.MaxTextWidth = Math.Max(1, w - LeftPadding * 2);
            dc.DrawText(emptyText, new Point(LeftPadding, TopPadding));
            return;
        }

        double maxTextWidth = Math.Max(1, w - LeftPadding - IndentPerLevel * 5 - 8);

        for (int i = 0; i < _entries.Count; i++)
        {
            double y = TopPadding + i * ItemHeight - _scrollOffset;
            if (y + ItemHeight < 0) continue;
            if (y > h) break;

            var entry = _entries[i];
            bool isActive = entry.BlockIndex == _activeHeadingBlock;
            bool isHovered = i == _hoverIndex;

            if (isActive)
                dc.DrawRectangle(_activeBackground, null, new Rect(0, y, w - 1, ItemHeight));
            else if (isHovered)
                dc.DrawRectangle(_hoverBackground, null, new Rect(0, y, w - 1, ItemHeight));

            double indent = LeftPadding + (entry.HeadingLevel - 1) * IndentPerLevel;
            double fontSize = entry.HeadingLevel == 1 ? H1FontSize : FontSize;
            var typeface = entry.HeadingLevel <= 2 ? TextMeasurer.BoldTypeface : TextMeasurer.NormalTypeface;
            var brush = isActive ? _activeForeground : _foreground;

            string displayText = string.IsNullOrEmpty(entry.Text) ? "(empty heading)" : entry.Text;
            var ft = new FormattedText(displayText,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            ft.MaxTextWidth = Math.Max(1, w - indent - 8);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;

            double textY = y + (ItemHeight - ft.Height) / 2;
            dc.DrawText(ft, new Point(indent, textY));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int index = HitTestIndex(e.GetPosition(this).Y);
        if (index >= 0 && index < _entries.Count)
        {
            NavigateRequested?.Invoke(_entries[index].BlockIndex);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTestIndex(e.GetPosition(this).Y);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 && index < _entries.Count ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double totalHeight = TopPadding + _entries.Count * ItemHeight;
        double maxScroll = Math.Max(0, totalHeight - ActualHeight);
        _scrollOffset = Math.Clamp(_scrollOffset - e.Delta * 0.5, 0, maxScroll);
        InvalidateVisual();
        e.Handled = true;
    }

    private int HitTestIndex(double y)
    {
        double adjustedY = y + _scrollOffset - TopPadding;
        if (adjustedY < 0) return -1;
        return (int)(adjustedY / ItemHeight);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
