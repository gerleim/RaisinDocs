using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Raisin.WPF.Base;

namespace RaisinDocs;

public class DocsCanvas : FrameworkElement
{
    private static readonly Typeface _typeface = new("Segoe UI");
    private const double _fontSize = 16;
    private const double _padding = 10;
    private const double _paragraphGap = 8;
    private static readonly Brush _selectionBrush;
    private static readonly Pen _cursorPen;
    private const double ScrollBarWidth = 10;
    private const double ScrollBarMinThumb = 20;
    private static readonly Brush _scrollTrackBrush;
    private static readonly Brush _scrollThumbBrush;

    static DocsCanvas()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 0, 120, 215));
        brush.Freeze();
        _selectionBrush = brush;
        var pen = new Pen(Brushes.Black, 1.5);
        pen.Freeze();
        _cursorPen = pen;
        var track = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        track.Freeze();
        _scrollTrackBrush = track;
        var thumb = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128));
        thumb.Freeze();
        _scrollThumbBrush = thumb;
    }

    private readonly Document _doc = new();

    private bool _cursorVisible = true;
    private double _dpiScale = 1.0;
    private double _lineHeight;
    private bool _measured;
    private readonly DispatcherTimer _blinkTimer;

    private GlyphTypeface? _glyphTypeface;
    private readonly Dictionary<char, double> _charWidthCache = new();

    private record struct VisualLine(int BlockIndex, int StartOffset, int Length);
    private readonly List<VisualLine> _visualLines = [];
    private readonly List<double> _lineYPositions = [];
    private bool _layoutDirty = true;
    private double _totalContentHeight;
    private double _scrollOffset;
    private bool _scrollbarVisible;
    private readonly SmoothScroller _smoother;
    private bool _isDraggingThumb;
    private double _dragStartY;
    private double _dragStartScroll;

    public DocsCanvas()
    {
        _smoother = new SmoothScroller(InvalidateVisual);
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
        };

        Loaded += (_, _) => { EnsureMeasured(); Focus(); };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _blinkTimer.Start();
            else _blinkTimer.Stop();
        };
    }

    private void EnsureMeasured()
    {
        if (_measured) return;
        try { _dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { }
        var ft = new FormattedText("M", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize,
            Brushes.Black, _dpiScale);
        _lineHeight = ft.Height;
        _typeface.TryGetGlyphTypeface(out _glyphTypeface);
        _measured = true;
    }

    private void ResetBlink()
    {
        _cursorVisible = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    private void InvalidateLayout()
    {
        _layoutDirty = true;
        InvalidateVisual();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        _blinkTimer.Start();
        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _blinkTimer.Stop();
        _cursorVisible = false;
        InvalidateVisual();
    }

    // --- Text measurement ---

    private double MeasureCharWidth(char ch)
    {
        if (!_charWidthCache.TryGetValue(ch, out double w))
        {
            if (_glyphTypeface != null &&
                _glyphTypeface.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIndex))
            {
                w = _glyphTypeface.AdvanceWidths[glyphIndex] * _fontSize;
            }
            else
            {
                var ft = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize,
                    Brushes.Black, _dpiScale);
                w = ft.WidthIncludingTrailingWhitespace;
            }
            _charWidthCache[ch] = w;
        }
        return w;
    }

    private double MeasureStringWidth(string text, int start, int length)
    {
        double total = 0;
        for (int i = start; i < start + length; i++)
            total += MeasureCharWidth(text[i]);
        return total;
    }

    // --- Layout ---

    private void ComputeLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;

        _scrollbarVisible = false;
        ComputeLayoutCore(ActualWidth - _padding * 2);
        if (_totalContentHeight > ActualHeight)
        {
            _scrollbarVisible = true;
            ComputeLayoutCore(ActualWidth - _padding * 2 - ScrollBarWidth);
        }
    }

    private void ComputeLayoutCore(double maxWidth)
    {
        _visualLines.Clear();
        _lineYPositions.Clear();
        maxWidth = Math.Max(0, maxWidth);

        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            string text = _doc.GetBlockText(bi);
            var segments = text.Split('\n');
            int offset = 0;
            for (int s = 0; s < segments.Length; s++)
            {
                WrapSegment(bi, offset, segments[s], maxWidth);
                offset += segments[s].Length + 1;
            }
        }

        double y = _padding;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (i > 0 && _visualLines[i].BlockIndex != _visualLines[i - 1].BlockIndex)
                y += _paragraphGap;
            _lineYPositions.Add(y);
            y += _lineHeight;
        }
        _totalContentHeight = y + _padding;
    }

    private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth)
    {
        if (segment.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, startOffset, 0));
            return;
        }

        int pos = 0;
        while (pos < segment.Length)
        {
            int lineLen = FitLine(segment, pos, maxWidth);
            _visualLines.Add(new VisualLine(blockIndex, startOffset + pos, lineLen));
            pos += lineLen;
        }
    }

    private int FitLine(string text, int start, double maxWidth)
    {
        int lastSpace = -1;
        double width = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == ' ') lastSpace = i;
            width += MeasureCharWidth(text[i]);
            if (width > maxWidth && i > start)
            {
                if (lastSpace >= start)
                    return lastSpace - start + 1;
                return i - start;
            }
        }
        return text.Length - start;
    }

    // --- Cursor ↔ visual line mapping ---

    private int CursorToVisualLineIndex()
    {
        for (int i = _visualLines.Count - 1; i >= 0; i--)
        {
            var vl = _visualLines[i];
            if (vl.BlockIndex == _doc.CursorBlock && vl.StartOffset <= _doc.CursorOffset)
                return i;
        }
        return 0;
    }

    private double CursorXInVisualLine(int vlIndex)
    {
        var vl = _visualLines[vlIndex];
        int localOffset = Math.Clamp(_doc.CursorOffset - vl.StartOffset, 0, vl.Length);
        if (localOffset == 0) return 0;
        string blockText = _doc.GetBlockText(vl.BlockIndex);
        return MeasureStringWidth(blockText, vl.StartOffset, localOffset);
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double accum = 0;
        for (int i = 0; i < vl.Length; i++)
        {
            double charW = MeasureCharWidth(blockText[vl.StartOffset + i]);
            if (x < accum + charW / 2)
                return vl.StartOffset + i;
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private int HitTestVisualLine(double y)
    {
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (y < _lineYPositions[i] + _lineHeight)
                return i;
        }
        return _visualLines.Count - 1;
    }

    private void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
    {
        double effectiveScroll = _scrollOffset + _smoother.Offset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        blockIndex = _visualLines[vli].BlockIndex;
        charOffset = HitTestInVisualLine(vli, pos.X - _padding);
    }

    // --- Scroll ---

    private void ClampScroll()
    {
        double maxScroll = Math.Max(0, _totalContentHeight - ActualHeight);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
    }

    private void EnsureCursorVisible()
    {
        _smoother.Cancel();
        ComputeLayout();
        int vli = CursorToVisualLineIndex();
        double cursorY = _lineYPositions[vli];
        double cursorBottom = cursorY + _lineHeight;
        if (cursorY < _scrollOffset + _padding)
            _scrollOffset = cursorY - _padding;
        else if (cursorBottom > _scrollOffset + ActualHeight - _padding)
            _scrollOffset = cursorBottom - ActualHeight + _padding;
        ClampScroll();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ComputeLayout();
        double oldScroll = _scrollOffset;
        _scrollOffset -= e.Delta;
        ClampScroll();
        double jump = _scrollOffset - oldScroll;
        if (jump != 0)
        {
            _smoother.Offset -= jump;
            _smoother.Start();
        }
        e.Handled = true;
    }

    // --- Scrollbar helpers ---

    private (double thumbY, double thumbH) GetScrollbarThumbRect()
    {
        double maxScroll = Math.Max(1, _totalContentHeight - ActualHeight);
        double trackH = ActualHeight;
        double thumbH = Math.Max(ScrollBarMinThumb, (ActualHeight / _totalContentHeight) * trackH);
        double thumbY = (_scrollOffset / maxScroll) * (trackH - thumbH);
        return (thumbY, thumbH);
    }

    private bool IsInScrollbarArea(Point pos) =>
        _scrollbarVisible && pos.X >= ActualWidth - ScrollBarWidth;

    // --- Mouse ---

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        ComputeLayout();

        var pos = e.GetPosition(this);
        if (IsInScrollbarArea(pos))
        {
            var (thumbY, thumbH) = GetScrollbarThumbRect();
            if (pos.Y >= thumbY && pos.Y <= thumbY + thumbH)
            {
                _isDraggingThumb = true;
                _dragStartY = pos.Y;
                _dragStartScroll = _scrollOffset;
                _smoother.Cancel();
            }
            else
            {
                _smoother.Cancel();
                if (pos.Y < thumbY)
                    _scrollOffset -= ActualHeight;
                else
                    _scrollOffset += ActualHeight;
                ClampScroll();
            }
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        HitTestToPosition(pos, out int block, out int offset);
        _doc.CursorBlock = block;
        _doc.CursorOffset = offset;

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _doc.CollapseSelection();

        CaptureMouse();
        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;

        var pos = e.GetPosition(this);
        if (_isDraggingThumb)
        {
            double maxScroll = Math.Max(1, _totalContentHeight - ActualHeight);
            var (_, thumbH) = GetScrollbarThumbRect();
            double trackRange = ActualHeight - thumbH;
            if (trackRange > 0)
            {
                double delta = pos.Y - _dragStartY;
                _scrollOffset = _dragStartScroll + (delta / trackRange) * maxScroll;
                ClampScroll();
                InvalidateVisual();
            }
            return;
        }

        ComputeLayout();
        HitTestToPosition(pos, out int block, out int offset);
        _doc.CursorBlock = block;
        _doc.CursorOffset = offset;

        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _isDraggingThumb = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    // --- Text input ---

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        if (_doc.HasSelection) _doc.DeleteSelection();

        foreach (char c in e.Text)
        {
            if (c < ' ' && c != '\t') continue;
            _doc.Insert(c);
        }
        _doc.CollapseSelection();

        ResetBlink();
        InvalidateLayout();
        EnsureCursorVisible();
        e.Handled = true;
    }

    // --- Keyboard ---

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool handled = true;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool textChanged = false;

        ComputeLayout();

        switch (e.Key)
        {
            case Key.Return:
                if (_doc.HasSelection) _doc.DeleteSelection();
                if (shift)
                    _doc.InsertHardBreak();
                else
                    _doc.InsertParagraphBreak();
                _doc.CollapseSelection();
                textChanged = true;
                break;

            case Key.Back:
                if (_doc.HasSelection)
                {
                    _doc.DeleteSelection();
                    textChanged = true;
                }
                else
                {
                    int prevBlock = _doc.CursorBlock;
                    int prevOffset = _doc.CursorOffset;
                    _doc.Backspace();
                    textChanged = _doc.CursorBlock != prevBlock || _doc.CursorOffset != prevOffset;
                    if (textChanged) _doc.CollapseSelection();
                }
                break;

            case Key.Delete:
                if (_doc.HasSelection)
                {
                    _doc.DeleteSelection();
                    textChanged = true;
                }
                else
                {
                    int prevBlocks = _doc.BlockCount;
                    int prevLen = _doc.GetBlockLength(_doc.CursorBlock);
                    _doc.Delete();
                    textChanged = _doc.BlockCount != prevBlocks ||
                                  _doc.GetBlockLength(_doc.CursorBlock) != prevLen;
                }
                break;

            case Key.Left:
                if (!shift && _doc.HasSelection)
                {
                    var (sb, so, _, _) = _doc.GetOrderedSelection();
                    _doc.CursorBlock = sb;
                    _doc.CursorOffset = so;
                    _doc.CollapseSelection();
                }
                else
                {
                    _doc.MoveLeft();
                    if (!shift) _doc.CollapseSelection();
                }
                break;

            case Key.Right:
                if (!shift && _doc.HasSelection)
                {
                    var (_, _, eb, eo) = _doc.GetOrderedSelection();
                    _doc.CursorBlock = eb;
                    _doc.CursorOffset = eo;
                    _doc.CollapseSelection();
                }
                else
                {
                    _doc.MoveRight();
                    if (!shift) _doc.CollapseSelection();
                }
                break;

            case Key.Up:
            {
                int vli = CursorToVisualLineIndex();
                if (vli > 0)
                {
                    double x = CursorXInVisualLine(vli);
                    vli--;
                    _doc.CursorBlock = _visualLines[vli].BlockIndex;
                    _doc.CursorOffset = HitTestInVisualLine(vli, x);
                }
                if (!shift) _doc.CollapseSelection();
                break;
            }

            case Key.Down:
            {
                int vli = CursorToVisualLineIndex();
                if (vli < _visualLines.Count - 1)
                {
                    double x = CursorXInVisualLine(vli);
                    vli++;
                    _doc.CursorBlock = _visualLines[vli].BlockIndex;
                    _doc.CursorOffset = HitTestInVisualLine(vli, x);
                }
                if (!shift) _doc.CollapseSelection();
                break;
            }

            case Key.Home:
            {
                if (ctrl)
                {
                    _doc.CursorBlock = 0;
                    _doc.CursorOffset = 0;
                }
                else
                {
                    int vli = CursorToVisualLineIndex();
                    _doc.CursorOffset = _visualLines[vli].StartOffset;
                }
                if (!shift) _doc.CollapseSelection();
                break;
            }

            case Key.End:
            {
                if (ctrl)
                {
                    _doc.CursorBlock = _doc.BlockCount - 1;
                    _doc.CursorOffset = _doc.GetBlockLength(_doc.CursorBlock);
                }
                else
                {
                    int vli = CursorToVisualLineIndex();
                    var vl = _visualLines[vli];
                    _doc.CursorOffset = vl.StartOffset + vl.Length;
                }
                if (!shift) _doc.CollapseSelection();
                break;
            }

            case Key.A:
                if (ctrl)
                    _doc.SelectAll();
                else handled = false;
                break;

            case Key.C:
                if (ctrl && _doc.HasSelection)
                {
                    try { Clipboard.SetText(_doc.GetSelectedText()); }
                    catch { }
                }
                else handled = false;
                break;

            case Key.X:
                if (ctrl && _doc.HasSelection)
                {
                    try { Clipboard.SetText(_doc.GetSelectedText()); }
                    catch { }
                    _doc.DeleteSelection();
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.V:
                if (ctrl)
                {
                    try
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (_doc.HasSelection) _doc.DeleteSelection();
                            _doc.Paste(text);
                            textChanged = true;
                        }
                    }
                    catch { }
                }
                else handled = false;
                break;

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            ResetBlink();
            if (textChanged)
                InvalidateLayout();
            else
                InvalidateVisual();
            EnsureCursorVisible();
            e.Handled = true;
        }
    }

    // --- Rendering ---

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMeasured();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

        ComputeLayout();

        double effectiveScroll = _scrollOffset + _smoother.Offset;
        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        if (_doc.HasSelection)
            DrawSelection(dc, effectiveScroll);

        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineY = _lineYPositions[i];
            if (lineY + _lineHeight < viewTop) continue;
            if (lineY > viewBottom) break;

            var vl = _visualLines[i];
            if (vl.Length > 0)
            {
                string text = _doc.GetBlockText(vl.BlockIndex).Substring(vl.StartOffset, vl.Length);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize,
                    Brushes.Black, _dpiScale);
                dc.DrawText(ft, new Point(_padding, lineY - effectiveScroll));
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli] - effectiveScroll;
            dc.DrawLine(_cursorPen, new Point(cx, cy), new Point(cx, cy + _lineHeight));
        }

        if (_scrollbarVisible)
            DrawScrollbar(dc);
    }

    private void DrawSelection(DrawingContext dc, double effectiveScroll)
    {
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineY = _lineYPositions[i];
            if (lineY + _lineHeight < viewTop) continue;
            if (lineY > viewBottom) break;

            var vl = _visualLines[i];
            int vlEnd = vl.StartOffset + vl.Length;

            bool startsBeforeSelEnd = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
            bool endsAfterSelStart = Document.ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
            if (!startsBeforeSelEnd || !endsAfterSelStart) continue;

            int hlStart = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                ? vl.StartOffset : so;
            int hlEnd = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                ? vlEnd : eo;

            string blockText = _doc.GetBlockText(vl.BlockIndex);
            double x1 = hlStart > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset)
                : 0;
            double x2 = hlEnd > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset)
                : 0;

            bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            dc.DrawRectangle(_selectionBrush, null,
                new Rect(_padding + x1, lineY - effectiveScroll, x2 - x1, _lineHeight));
        }
    }

    private void DrawScrollbar(DrawingContext dc)
    {
        double trackX = ActualWidth - ScrollBarWidth;
        dc.DrawRectangle(_scrollTrackBrush, null,
            new Rect(trackX, 0, ScrollBarWidth, ActualHeight));

        var (thumbY, thumbH) = GetScrollbarThumbRect();
        dc.DrawRectangle(_scrollThumbBrush, null,
            new Rect(trackX + 1, thumbY, ScrollBarWidth - 2, thumbH));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateLayout();
    }
}
