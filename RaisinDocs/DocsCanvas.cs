using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Raisin.WPF.Base;

namespace RaisinDocs;

public class DocsCanvas : FrameworkElement
{
    private static readonly Typeface _normalTypeface = new("Segoe UI");
    private static readonly Typeface _boldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface _italicTypeface = new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface _boldItalicTypeface = new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface _monoTypeface = new("Cascadia Mono");
    private const double _baseFontSize = 16;
    private const double _codeFontSize = 14;
    private const double _padding = 10;
    private const double _paragraphGap = 8;
    private const double _listIndent = 20;
    private static readonly Brush _selectionBrush;
    private static readonly Pen _cursorPen;
    private const double ScrollBarWidth = 14;
    private const double ScrollBarMinThumb = 20;
    private static readonly Brush _scrollTrackBrush;
    private static readonly Brush _scrollThumbBrush;
    private static readonly Brush _syntaxBrush;
    private static readonly Brush _codeBackgroundBrush;

    private static readonly double[] _headingFontSizes = [32, 26, 22, 18, 16, 14];

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
        var syntax = new SolidColorBrush(Color.FromArgb(180, 140, 140, 140));
        syntax.Freeze();
        _syntaxBrush = syntax;
        var codeBg = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        codeBg.Freeze();
        _codeBackgroundBrush = codeBg;
    }

    private readonly Document _doc = new();

    private bool _cursorVisible = true;
    private double _dpiScale = 1.0;
    private bool _measured;
    private readonly DispatcherTimer _blinkTimer;

    private GlyphTypeface? _normalGlyph;
    private GlyphTypeface? _boldGlyph;
    private GlyphTypeface? _italicGlyph;
    private GlyphTypeface? _boldItalicGlyph;
    private GlyphTypeface? _monoGlyph;
    private readonly Dictionary<(char, int), double> _charWidthCache = new();

    private readonly Dictionary<BlockKind, double> _lineHeights = new();

    private record struct VisualLine(int BlockIndex, int StartOffset, int Length, BlockKind BlockKind);
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

    private List<ParsedBlock>? _parsedBlocks;

    private readonly DispatcherTimer _undoSealTimer;
    private enum LastActionKind { None, Typing, Deleting }
    private LastActionKind _lastAction;

    public DocsCanvas()
    {
        _smoother = new SmoothScroller(InvalidateVisual);
        Focusable = true;
        FocusVisualStyle = null;
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

        _undoSealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _undoSealTimer.Tick += (_, _) =>
        {
            _undoSealTimer.Stop();
            _doc.SealUndoGroup();
            _lastAction = LastActionKind.None;
        };

        Loaded += (_, _) => { EnsureMeasured(); Focus(); };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _blinkTimer.Start();
            else _blinkTimer.Stop();
        };
    }

    private void ResetUndoSealTimer()
    {
        _undoSealTimer.Stop();
        _undoSealTimer.Start();
    }

    private void SealAndStopTimer()
    {
        _undoSealTimer.Stop();
        _doc.SealUndoGroup();
        _lastAction = LastActionKind.None;
    }

    private void EnsureMeasured()
    {
        if (_measured) return;
        try { _dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { }

        _normalTypeface.TryGetGlyphTypeface(out _normalGlyph);
        _boldTypeface.TryGetGlyphTypeface(out _boldGlyph);
        _italicTypeface.TryGetGlyphTypeface(out _italicGlyph);
        _boldItalicTypeface.TryGetGlyphTypeface(out _boldItalicGlyph);
        _monoTypeface.TryGetGlyphTypeface(out _monoGlyph);

        foreach (BlockKind kind in Enum.GetValues<BlockKind>())
        {
            double fontSize = GetBlockFontSize(kind);
            var ft = new FormattedText("M", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GetBlockBaseTypeface(kind), fontSize,
                Brushes.Black, _dpiScale);
            _lineHeights[kind] = ft.Height;
        }

        _measured = true;
    }

    private static double GetBlockFontSize(BlockKind kind) => kind switch
    {
        BlockKind.Heading1 => _headingFontSizes[0],
        BlockKind.Heading2 => _headingFontSizes[1],
        BlockKind.Heading3 => _headingFontSizes[2],
        BlockKind.Heading4 => _headingFontSizes[3],
        BlockKind.Heading5 => _headingFontSizes[4],
        BlockKind.Heading6 => _headingFontSizes[5],
        BlockKind.FencedCodeLine => _codeFontSize,
        _ => _baseFontSize,
    };

    private static Typeface GetBlockBaseTypeface(BlockKind kind) => kind switch
    {
        BlockKind.FencedCodeLine => _monoTypeface,
        _ => _normalTypeface,
    };

    private static Typeface GetInlineTypeface(BlockKind blockKind, InlineStyle style) => blockKind switch
    {
        BlockKind.FencedCodeLine => _monoTypeface,
        _ => style switch
        {
            InlineStyle.Bold => _boldTypeface,
            InlineStyle.Italic => _italicTypeface,
            InlineStyle.BoldItalic => _boldItalicTypeface,
            InlineStyle.Code => _monoTypeface,
            _ => _normalTypeface,
        },
    };

    private GlyphTypeface? GetInlineGlyph(BlockKind blockKind, InlineStyle style) => blockKind switch
    {
        BlockKind.FencedCodeLine => _monoGlyph,
        _ => style switch
        {
            InlineStyle.Bold => _boldGlyph,
            InlineStyle.Italic => _italicGlyph,
            InlineStyle.BoldItalic => _boldItalicGlyph,
            InlineStyle.Code => _monoGlyph,
            _ => _normalGlyph,
        },
    };

    private static int GetStyleKey(BlockKind blockKind, InlineStyle style)
    {
        int fontId = blockKind == BlockKind.FencedCodeLine || style == InlineStyle.Code ? 1 : 0;
        if (style == InlineStyle.Bold) fontId = 2;
        else if (style == InlineStyle.Italic) fontId = 3;
        else if (style == InlineStyle.BoldItalic) fontId = 4;
        if (blockKind == BlockKind.FencedCodeLine) fontId = 1;
        int sizeKey = (int)GetBlockFontSize(blockKind);
        return fontId * 100 + sizeKey;
    }

    private double GetLineHeight(BlockKind kind)
    {
        return _lineHeights.TryGetValue(kind, out double h) ? h : _lineHeights[BlockKind.Paragraph];
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
        _parsedBlocks = null;
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

    private double MeasureCharWidth(char ch, BlockKind blockKind, InlineStyle style)
    {
        int key = GetStyleKey(blockKind, style);
        if (!_charWidthCache.TryGetValue((ch, key), out double w))
        {
            double fontSize = GetBlockFontSize(blockKind);
            var glyph = GetInlineGlyph(blockKind, style);
            if (glyph != null && glyph.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIndex))
            {
                w = glyph.AdvanceWidths[glyphIndex] * fontSize;
            }
            else
            {
                var typeface = GetInlineTypeface(blockKind, style);
                var ft = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    Brushes.Black, _dpiScale);
                w = ft.WidthIncludingTrailingWhitespace;
            }
            _charWidthCache[(ch, key)] = w;
        }
        return w;
    }

    private InlineStyle GetStyleAtOffset(IReadOnlyList<StyledRun> runs, int offset, ref int runHint)
    {
        while (runHint < runs.Count - 1 && offset >= runs[runHint].Start + runs[runHint].Length)
            runHint++;
        return runs[runHint].Style;
    }

    private double MeasureStringWidth(string text, int start, int length,
        IReadOnlyList<StyledRun> runs, BlockKind blockKind)
    {
        double total = 0;
        int runIdx = 0;
        for (int i = start; i < start + length; i++)
        {
            var style = GetStyleAtOffset(runs, i, ref runIdx);
            total += MeasureCharWidth(text[i], blockKind, style);
        }
        return total;
    }

    // --- Layout ---

    private void ComputeLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;

        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount);

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
            var parsed = _parsedBlocks![bi];
            var segments = text.Split('\n');
            int offset = 0;
            for (int s = 0; s < segments.Length; s++)
            {
                WrapSegment(bi, offset, segments[s], maxWidth, parsed);
                offset += segments[s].Length + 1;
            }
        }

        double y = _padding;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (i > 0 && _visualLines[i].BlockIndex != _visualLines[i - 1].BlockIndex)
                y += _paragraphGap;
            _lineYPositions.Add(y);
            y += GetLineHeight(_visualLines[i].BlockKind);
        }
        _totalContentHeight = y + _padding;
    }

    private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth,
        ParsedBlock parsed)
    {
        if (segment.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, startOffset, 0, parsed.Kind));
            return;
        }

        int pos = 0;
        while (pos < segment.Length)
        {
            int lineLen = FitLine(segment, pos, maxWidth, parsed);
            _visualLines.Add(new VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind));
            pos += lineLen;
        }
    }

    private int FitLine(string text, int start, double maxWidth, ParsedBlock parsed)
    {
        int lastSpace = -1;
        double width = 0;
        int runIdx = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == ' ') lastSpace = i;
            var style = GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            width += MeasureCharWidth(text[i], parsed.Kind, style);
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
        var parsed = _parsedBlocks![vl.BlockIndex];
        return MeasureStringWidth(blockText, vl.StartOffset, localOffset, parsed.Runs, parsed.Kind);
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        var parsed = _parsedBlocks![vl.BlockIndex];
        double accum = 0;
        int runIdx = 0;
        for (int i = 0; i < vl.Length; i++)
        {
            int offset = vl.StartOffset + i;
            var style = GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
                return offset;
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private int HitTestVisualLine(double y)
    {
        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineH = GetLineHeight(_visualLines[i].BlockKind);
            if (y < _lineYPositions[i] + lineH)
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
        double lineH = GetLineHeight(_visualLines[vli].BlockKind);
        double cursorBottom = cursorY + lineH;
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

        SealAndStopTimer();

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

        var pos = e.GetPosition(this);
        if (!IsMouseCaptured)
        {
            Cursor = IsInScrollbarArea(pos) ? Cursors.Arrow : Cursors.IBeam;
            return;
        }

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

        if (_lastAction != LastActionKind.Typing)
        {
            _doc.SealUndoGroup();
            _lastAction = LastActionKind.Typing;
        }
        _doc.BeginUndoGroup();

        if (_doc.HasSelection) _doc.DeleteSelection();

        foreach (char c in e.Text)
        {
            if (c < ' ' && c != '\t') continue;
            _doc.Insert(c);
        }
        _doc.CollapseSelection();

        ResetUndoSealTimer();
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
                SealAndStopTimer();
                _doc.BeginUndoGroup();
                if (_doc.HasSelection) _doc.DeleteSelection();
                if (shift)
                    _doc.InsertHardBreak();
                else
                    _doc.InsertParagraphBreak();
                _doc.CollapseSelection();
                _doc.SealUndoGroup();
                textChanged = true;
                break;

            case Key.Back:
                if (_lastAction != LastActionKind.Deleting)
                {
                    _doc.SealUndoGroup();
                    _lastAction = LastActionKind.Deleting;
                }
                _doc.BeginUndoGroup();
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
                ResetUndoSealTimer();
                break;

            case Key.Delete:
                if (_lastAction != LastActionKind.Deleting)
                {
                    _doc.SealUndoGroup();
                    _lastAction = LastActionKind.Deleting;
                }
                _doc.BeginUndoGroup();
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
                ResetUndoSealTimer();
                break;

            case Key.Left:
                SealAndStopTimer();
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
                SealAndStopTimer();
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
                SealAndStopTimer();
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
                SealAndStopTimer();
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
                SealAndStopTimer();
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
                SealAndStopTimer();
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
                {
                    SealAndStopTimer();
                    _doc.SelectAll();
                }
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
                    SealAndStopTimer();
                    try { Clipboard.SetText(_doc.GetSelectedText()); }
                    catch { }
                    _doc.BeginUndoGroup();
                    _doc.DeleteSelection();
                    _doc.SealUndoGroup();
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.V:
                if (ctrl)
                {
                    SealAndStopTimer();
                    try
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            _doc.BeginUndoGroup();
                            if (_doc.HasSelection) _doc.DeleteSelection();
                            _doc.Paste(text);
                            _doc.SealUndoGroup();
                            textChanged = true;
                        }
                    }
                    catch { }
                }
                else handled = false;
                break;

            case Key.Z:
                if (ctrl)
                {
                    _undoSealTimer.Stop();
                    _doc.Undo();
                    _lastAction = LastActionKind.None;
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.Y:
                if (ctrl)
                {
                    _undoSealTimer.Stop();
                    _doc.Redo();
                    _lastAction = LastActionKind.None;
                    textChanged = true;
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

        DrawCodeBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

        if (_doc.HasSelection)
            DrawSelection(dc, effectiveScroll);

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            double lineH = GetLineHeight(vl.BlockKind);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Length > 0)
            {
                string blockText = _doc.GetBlockText(vl.BlockIndex);
                string text = blockText.Substring(vl.StartOffset, vl.Length);
                var parsed = _parsedBlocks![vl.BlockIndex];
                double fontSize = GetBlockFontSize(parsed.Kind);
                var baseTypeface = GetBlockBaseTypeface(parsed.Kind);

                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, baseTypeface, fontSize,
                    Brushes.Black, _dpiScale);

                ApplyInlineStyles(ft, vl, parsed);

                dc.DrawText(ft, new Point(_padding, lineY - effectiveScroll));
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli] - effectiveScroll;
            double lineH = GetLineHeight(_visualLines[vli].BlockKind);
            dc.DrawLine(_cursorPen, new Point(cx, cy), new Point(cx, cy + lineH));
        }

        if (_scrollbarVisible)
            DrawScrollbar(dc);
    }

    private void ApplyInlineStyles(FormattedText ft, VisualLine vl, ParsedBlock parsed)
    {
        foreach (var run in parsed.Runs)
        {
            int runEnd = run.Start + run.Length;
            int vlEnd = vl.StartOffset + vl.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

            int localStart = Math.Max(0, run.Start - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, runEnd - vl.StartOffset);
            int count = localEnd - localStart;
            if (count <= 0) continue;

            if (parsed.Kind == BlockKind.FencedCodeLine) continue;

            switch (run.Style)
            {
                case InlineStyle.Bold:
                    ft.SetFontWeight(FontWeights.Bold, localStart, count);
                    break;
                case InlineStyle.Italic:
                    ft.SetFontStyle(FontStyles.Italic, localStart, count);
                    break;
                case InlineStyle.BoldItalic:
                    ft.SetFontWeight(FontWeights.Bold, localStart, count);
                    ft.SetFontStyle(FontStyles.Italic, localStart, count);
                    break;
                case InlineStyle.Code:
                    ft.SetFontFamily(_monoTypeface.FontFamily, localStart, count);
                    break;
            }
        }

        ApplySyntaxDimming(ft, vl, parsed);
    }

    private void ApplySyntaxDimming(FormattedText ft, VisualLine vl, ParsedBlock parsed)
    {
        string blockText = _doc.GetBlockText(vl.BlockIndex);
        int vlEnd = vl.StartOffset + vl.Length;

        if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
        {
            int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
            int prefixLen = hashCount + 1;
            int localStart = Math.Max(0, 0 - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, prefixLen - vl.StartOffset);
            if (localEnd > localStart)
                ft.SetForegroundBrush(_syntaxBrush, localStart, localEnd - localStart);
        }

        if (parsed.Kind == BlockKind.UnorderedListItem && vl.Length >= 2)
        {
            string vlText = blockText.Substring(vl.StartOffset, Math.Min(vl.Length, 2));
            if (vlText is "- " or "* ")
                ft.SetForegroundBrush(_syntaxBrush, 0, 2);
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

            int markerLen = run.Style switch
            {
                InlineStyle.BoldItalic => 3,
                InlineStyle.Bold => 2,
                InlineStyle.Italic => 1,
                InlineStyle.Code => CountBackticks(blockText, run.Start),
                _ => 0,
            };
            if (markerLen == 0) continue;

            DimRange(ft, vl, run.Start, markerLen);
            DimRange(ft, vl, runEnd - markerLen, markerLen);
        }
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }

    private static void DimRange(FormattedText ft, VisualLine vl, int docStart, int length)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        int localStart = Math.Max(0, docStart - vl.StartOffset);
        int localEnd = Math.Min(vl.Length, docStart + length - vl.StartOffset);
        if (localEnd > localStart)
            ft.SetForegroundBrush(_syntaxBrush, localStart, localEnd - localStart);
    }

    private void DrawCodeBlockBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        double contentWidth = _scrollbarVisible ? ActualWidth - ScrollBarWidth : ActualWidth;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.BlockKind != BlockKind.FencedCodeLine) continue;

            double lineH = GetLineHeight(vl.BlockKind);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            dc.DrawRectangle(_codeBackgroundBrush, null,
                new Rect(0, lineY - effectiveScroll, contentWidth, lineH));
        }
    }

    private void DrawSelection(DrawingContext dc, double effectiveScroll)
    {
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            double lineH = GetLineHeight(vl.BlockKind);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            int vlEnd = vl.StartOffset + vl.Length;

            bool startsBeforeSelEnd = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
            bool endsAfterSelStart = Document.ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
            if (!startsBeforeSelEnd || !endsAfterSelStart) continue;

            int hlStart = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                ? vl.StartOffset : so;
            int hlEnd = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                ? vlEnd : eo;

            string blockText = _doc.GetBlockText(vl.BlockIndex);
            var parsed = _parsedBlocks![vl.BlockIndex];
            double x1 = hlStart > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset, parsed.Runs, parsed.Kind)
                : 0;
            double x2 = hlEnd > vl.StartOffset
                ? MeasureStringWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset, parsed.Runs, parsed.Kind)
                : 0;

            bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            dc.DrawRectangle(_selectionBrush, null,
                new Rect(_padding + x1, lineY - effectiveScroll, x2 - x1, lineH));
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
