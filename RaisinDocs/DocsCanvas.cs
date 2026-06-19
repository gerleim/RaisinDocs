using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

public class DocsCanvas : FrameworkElement
{
    private static readonly Typeface _typeface = new("Segoe UI");
    private const double _fontSize = 16;
    private const double _padding = 10;
    private const double _paragraphGap = 8;
    private static readonly Brush _selectionBrush;
    private static readonly Pen _cursorPen;

    static DocsCanvas()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 0, 120, 215));
        brush.Freeze();
        _selectionBrush = brush;
        var pen = new Pen(Brushes.Black, 1.5);
        pen.Freeze();
        _cursorPen = pen;
    }

    private readonly List<StringBuilder> _blocks = [new()];
    private int _blockIndex;
    private int _charOffset;
    private int _anchorBlockIndex;
    private int _anchorCharOffset;

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

    private bool HasSelection => _anchorBlockIndex != _blockIndex || _anchorCharOffset != _charOffset;

    public DocsCanvas()
    {
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

        _visualLines.Clear();
        _lineYPositions.Clear();
        double maxWidth = Math.Max(0, ActualWidth - _padding * 2);

        for (int bi = 0; bi < _blocks.Count; bi++)
        {
            string text = _blocks[bi].ToString();
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
            if (vl.BlockIndex == _blockIndex && vl.StartOffset <= _charOffset)
                return i;
        }
        return 0;
    }

    private double CursorXInVisualLine(int vlIndex)
    {
        var vl = _visualLines[vlIndex];
        int localOffset = Math.Clamp(_charOffset - vl.StartOffset, 0, vl.Length);
        if (localOffset == 0) return 0;
        string blockText = _blocks[vl.BlockIndex].ToString();
        return MeasureStringWidth(blockText, vl.StartOffset, localOffset);
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        string blockText = _blocks[vl.BlockIndex].ToString();
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
        int vli = HitTestVisualLine(pos.Y + _scrollOffset);
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
        _scrollOffset -= e.Delta;
        ClampScroll();
        InvalidateVisual();
        e.Handled = true;
    }

    // --- Selection helpers ---

    private void CollapseSelection()
    {
        _anchorBlockIndex = _blockIndex;
        _anchorCharOffset = _charOffset;
    }

    private static int ComparePositions(int block1, int offset1, int block2, int offset2)
    {
        if (block1 != block2) return block1.CompareTo(block2);
        return offset1.CompareTo(offset2);
    }

    private (int startBlock, int startOffset, int endBlock, int endOffset) GetOrderedSelection()
    {
        if (ComparePositions(_anchorBlockIndex, _anchorCharOffset, _blockIndex, _charOffset) <= 0)
            return (_anchorBlockIndex, _anchorCharOffset, _blockIndex, _charOffset);
        return (_blockIndex, _charOffset, _anchorBlockIndex, _anchorCharOffset);
    }

    private string GetSelectedText()
    {
        var (sb, so, eb, eo) = GetOrderedSelection();
        var result = new StringBuilder();
        for (int i = sb; i <= eb; i++)
        {
            if (i > sb) result.Append("\r\n");
            string blockText = _blocks[i].ToString();
            int start = (i == sb) ? so : 0;
            int end = (i == eb) ? eo : blockText.Length;
            string segment = blockText.Substring(start, end - start);
            result.Append(segment.Replace("\n", "\r\n"));
        }
        return result.ToString();
    }

    private void DeleteSelection()
    {
        var (sb, so, eb, eo) = GetOrderedSelection();
        if (sb == eb)
        {
            _blocks[sb].Remove(so, eo - so);
        }
        else
        {
            _blocks[sb].Remove(so, _blocks[sb].Length - so);
            _blocks[sb].Append(_blocks[eb].ToString().Substring(eo));
            _blocks.RemoveRange(sb + 1, eb - sb);
        }
        _blockIndex = sb;
        _charOffset = so;
        CollapseSelection();
    }

    private void PasteText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        var block = _blocks[_blockIndex];
        string afterCursor = block.ToString(_charOffset, block.Length - _charOffset);
        block.Remove(_charOffset, block.Length - _charOffset);

        block.Append(lines[0]);
        _charOffset += lines[0].Length;

        for (int i = 1; i < lines.Length; i++)
        {
            _blockIndex++;
            _blocks.Insert(_blockIndex, new StringBuilder(lines[i]));
            _charOffset = lines[i].Length;
        }

        _blocks[_blockIndex].Append(afterCursor);
        CollapseSelection();
    }

    // --- Mouse ---

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        ComputeLayout();
        HitTestToPosition(e.GetPosition(this), out _blockIndex, out _charOffset);

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            CollapseSelection();

        CaptureMouse();
        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;

        ComputeLayout();
        HitTestToPosition(e.GetPosition(this), out _blockIndex, out _charOffset);

        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    // --- Text input ---

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        if (HasSelection) DeleteSelection();

        var block = _blocks[_blockIndex];
        foreach (char c in e.Text)
        {
            if (c < ' ' && c != '\t') continue;
            block.Insert(_charOffset, c);
            _charOffset++;
        }
        CollapseSelection();

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
                if (HasSelection) DeleteSelection();
                if (shift)
                {
                    _blocks[_blockIndex].Insert(_charOffset, '\n');
                    _charOffset++;
                }
                else
                {
                    var block = _blocks[_blockIndex];
                    string after = block.ToString(_charOffset, block.Length - _charOffset);
                    block.Remove(_charOffset, block.Length - _charOffset);
                    _blockIndex++;
                    _blocks.Insert(_blockIndex, new StringBuilder(after));
                    _charOffset = 0;
                }
                CollapseSelection();
                textChanged = true;
                break;

            case Key.Back:
                if (HasSelection)
                {
                    DeleteSelection();
                    textChanged = true;
                }
                else if (_charOffset > 0)
                {
                    _blocks[_blockIndex].Remove(_charOffset - 1, 1);
                    _charOffset--;
                    CollapseSelection();
                    textChanged = true;
                }
                else if (_blockIndex > 0)
                {
                    var prev = _blocks[_blockIndex - 1];
                    int newOffset = prev.Length;
                    prev.Append(_blocks[_blockIndex]);
                    _blocks.RemoveAt(_blockIndex);
                    _blockIndex--;
                    _charOffset = newOffset;
                    CollapseSelection();
                    textChanged = true;
                }
                break;

            case Key.Delete:
                if (HasSelection)
                {
                    DeleteSelection();
                    textChanged = true;
                }
                else if (_charOffset < _blocks[_blockIndex].Length)
                {
                    _blocks[_blockIndex].Remove(_charOffset, 1);
                    textChanged = true;
                }
                else if (_blockIndex < _blocks.Count - 1)
                {
                    _blocks[_blockIndex].Append(_blocks[_blockIndex + 1]);
                    _blocks.RemoveAt(_blockIndex + 1);
                    textChanged = true;
                }
                break;

            case Key.Left:
                if (!shift && HasSelection)
                {
                    var (sb, so, _, _) = GetOrderedSelection();
                    _blockIndex = sb;
                    _charOffset = so;
                    CollapseSelection();
                }
                else
                {
                    if (_charOffset > 0)
                        _charOffset--;
                    else if (_blockIndex > 0)
                    {
                        _blockIndex--;
                        _charOffset = _blocks[_blockIndex].Length;
                    }
                    if (!shift) CollapseSelection();
                }
                break;

            case Key.Right:
                if (!shift && HasSelection)
                {
                    var (_, _, eb, eo) = GetOrderedSelection();
                    _blockIndex = eb;
                    _charOffset = eo;
                    CollapseSelection();
                }
                else
                {
                    if (_charOffset < _blocks[_blockIndex].Length)
                        _charOffset++;
                    else if (_blockIndex < _blocks.Count - 1)
                    {
                        _blockIndex++;
                        _charOffset = 0;
                    }
                    if (!shift) CollapseSelection();
                }
                break;

            case Key.Up:
            {
                int vli = CursorToVisualLineIndex();
                if (vli > 0)
                {
                    double x = CursorXInVisualLine(vli);
                    vli--;
                    _blockIndex = _visualLines[vli].BlockIndex;
                    _charOffset = HitTestInVisualLine(vli, x);
                }
                if (!shift) CollapseSelection();
                break;
            }

            case Key.Down:
            {
                int vli = CursorToVisualLineIndex();
                if (vli < _visualLines.Count - 1)
                {
                    double x = CursorXInVisualLine(vli);
                    vli++;
                    _blockIndex = _visualLines[vli].BlockIndex;
                    _charOffset = HitTestInVisualLine(vli, x);
                }
                if (!shift) CollapseSelection();
                break;
            }

            case Key.Home:
            {
                if (ctrl)
                {
                    _blockIndex = 0;
                    _charOffset = 0;
                }
                else
                {
                    int vli = CursorToVisualLineIndex();
                    _charOffset = _visualLines[vli].StartOffset;
                }
                if (!shift) CollapseSelection();
                break;
            }

            case Key.End:
            {
                if (ctrl)
                {
                    _blockIndex = _blocks.Count - 1;
                    _charOffset = _blocks[_blockIndex].Length;
                }
                else
                {
                    int vli = CursorToVisualLineIndex();
                    var vl = _visualLines[vli];
                    _charOffset = vl.StartOffset + vl.Length;
                }
                if (!shift) CollapseSelection();
                break;
            }

            case Key.A:
                if (ctrl)
                {
                    _anchorBlockIndex = 0;
                    _anchorCharOffset = 0;
                    _blockIndex = _blocks.Count - 1;
                    _charOffset = _blocks[_blockIndex].Length;
                }
                else handled = false;
                break;

            case Key.C:
                if (ctrl && HasSelection)
                {
                    try { Clipboard.SetText(GetSelectedText()); }
                    catch { }
                }
                else handled = false;
                break;

            case Key.X:
                if (ctrl && HasSelection)
                {
                    try { Clipboard.SetText(GetSelectedText()); }
                    catch { }
                    DeleteSelection();
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
                            if (HasSelection) DeleteSelection();
                            PasteText(text);
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

        double viewTop = _scrollOffset;
        double viewBottom = _scrollOffset + ActualHeight;

        if (HasSelection)
            DrawSelection(dc, viewTop, viewBottom);

        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineY = _lineYPositions[i];
            if (lineY + _lineHeight < viewTop) continue;
            if (lineY > viewBottom) break;

            var vl = _visualLines[i];
            if (vl.Length > 0)
            {
                string text = _blocks[vl.BlockIndex].ToString().Substring(vl.StartOffset, vl.Length);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize,
                    Brushes.Black, _dpiScale);
                dc.DrawText(ft, new Point(_padding, lineY - _scrollOffset));
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli] - _scrollOffset;
            dc.DrawLine(_cursorPen, new Point(cx, cy), new Point(cx, cy + _lineHeight));
        }
    }

    private void DrawSelection(DrawingContext dc, double viewTop, double viewBottom)
    {
        var (sb, so, eb, eo) = GetOrderedSelection();

        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineY = _lineYPositions[i];
            if (lineY + _lineHeight < viewTop) continue;
            if (lineY > viewBottom) break;

            var vl = _visualLines[i];
            int vlEnd = vl.StartOffset + vl.Length;

            bool startsBeforeSelEnd = ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
            bool endsAfterSelStart = ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
            if (!startsBeforeSelEnd || !endsAfterSelStart) continue;

            int hlStart = ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                ? vl.StartOffset : so;
            int hlEnd = ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                ? vlEnd : eo;

            string blockText = _blocks[vl.BlockIndex].ToString();
            double x1 = hlStart > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset)
                : 0;
            double x2 = hlEnd > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset)
                : 0;

            bool selectionContinues = ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            dc.DrawRectangle(_selectionBrush, null,
                new Rect(_padding + x1, lineY - _scrollOffset, x2 - x1, _lineHeight));
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateLayout();
    }
}
