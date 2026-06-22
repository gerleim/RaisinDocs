using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Raisin.WPF.Base;

namespace RaisinDocs;

public partial class DocsCanvas : FrameworkElement
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
    private const double ScrollBarWidth = 14;
    private const double ScrollBarMinThumb = 20;

    public enum EditorTheme { Light, Dark }

    private sealed record ThemePalette(
        Brush Background, Brush Foreground, Pen CursorPen,
        Brush Selection, Brush ScrollTrack, Brush ScrollThumb,
        Brush Syntax, Brush CodeBackground);

    private static readonly ThemePalette _lightPalette;
    private static readonly ThemePalette _darkPalette;
    private ThemePalette _palette = _lightPalette!;

    private static readonly double[] _headingFontSizes = [32, 26, 22, 18, 16, 14];

    static DocsCanvas()
    {
        _lightPalette = BuildPalette(
            background: Colors.White,
            foreground: Colors.Black,
            cursor: Colors.Black,
            selection: Color.FromArgb(100, 0, 120, 215),
            scrollTrack: Color.FromArgb(30, 0, 0, 0),
            scrollThumb: Color.FromArgb(120, 128, 128, 128),
            syntax: Color.FromArgb(180, 140, 140, 140),
            codeBackground: Color.FromArgb(25, 0, 0, 0));

        _darkPalette = BuildPalette(
            background: Color.FromRgb(30, 30, 30),
            foreground: Color.FromRgb(212, 212, 212),
            cursor: Colors.White,
            selection: Color.FromArgb(100, 38, 79, 120),
            scrollTrack: Color.FromArgb(30, 255, 255, 255),
            scrollThumb: Color.FromArgb(120, 128, 128, 128),
            syntax: Color.FromArgb(180, 110, 110, 110),
            codeBackground: Color.FromArgb(25, 255, 255, 255));
    }

    private static ThemePalette BuildPalette(
        Color background, Color foreground, Color cursor,
        Color selection, Color scrollTrack, Color scrollThumb,
        Color syntax, Color codeBackground)
    {
        var cursorBrush = new SolidColorBrush(cursor);
        cursorBrush.Freeze();
        var cursorPen = new Pen(cursorBrush, 1.5);
        cursorPen.Freeze();

        return new ThemePalette(
            Frozen(background), Frozen(foreground), cursorPen,
            Frozen(selection), Frozen(scrollTrack), Frozen(scrollThumb),
            Frozen(syntax), Frozen(codeBackground));

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(EditorTheme), typeof(DocsCanvas),
            new FrameworkPropertyMetadata(EditorTheme.Light, FrameworkPropertyMetadataOptions.AffectsRender, OnThemePropertyChanged));

    public EditorTheme Theme
    {
        get => (EditorTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (DocsCanvas)d;
        canvas._palette = canvas.Theme == EditorTheme.Dark ? _darkPalette : _lightPalette;
        canvas.ThemeChanged?.Invoke(canvas, EventArgs.Empty);
    }

    public event EventHandler? ThemeChanged;

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
    private List<BlockVisualMap>? _visualMaps;

    public enum EditMode { Source, Visual }
    private EditMode _editMode = EditMode.Source;
    public EditMode CurrentEditMode => _editMode;

    public event EventHandler? FormattingChanged;

    public string GetText() => _doc.GetText();

    public void SetText(string text)
    {
        _doc.SetText(text);
        InvalidateLayout();
    }

    public void ToggleTheme() => Theme = Theme == EditorTheme.Light ? EditorTheme.Dark : EditorTheme.Light;

    // --- Public formatting API ---

    public void ToggleBold()
    {
        SealAndStopTimer();
        ToggleInlineStyle("**", InlineStyle.Bold);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleItalic()
    {
        SealAndStopTimer();
        ToggleInlineStyle("*", InlineStyle.Italic);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleCodeSpan()
    {
        SealAndStopTimer();
        ToggleInlineStyle("`", InlineStyle.Code);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleStrikethrough()
    {
        SealAndStopTimer();
        ToggleInlineStyle("~~", InlineStyle.Strikethrough);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleHeading(int level)
    {
        if (level < 1 || level > 6) return;
        ToggleBlockPrefixForSelection(new string('#', level) + " ");
    }

    public void ToggleBulletList()
    {
        ToggleBlockPrefixForSelection("- ");
    }

    public void ToggleBlockquote()
    {
        ToggleBlockPrefixForSelection("> ");
    }

    public void ToggleFencedCode()
    {
        SealAndStopTimer();
        var (sb, _, eb, _) = _doc.HasSelection
            ? _doc.GetOrderedSelection()
            : (_doc.CursorBlock, 0, _doc.CursorBlock, 0);

        ComputeLayout();

        bool allFenced = true;
        for (int b = sb; b <= eb; b++)
        {
            if (_parsedBlocks![b].Kind != BlockKind.FencedCodeLine)
            {
                allFenced = false;
                break;
            }
        }

        _doc.BeginUndoGroup();

        if (allFenced)
        {
            int openDelim = -1;
            for (int b = sb; b >= 0; b--)
            {
                if (_parsedBlocks![b].IsFenceDelimiter) { openDelim = b; break; }
            }
            int closeDelim = -1;
            for (int b = eb; b < _doc.BlockCount; b++)
            {
                if (_parsedBlocks![b].IsFenceDelimiter) { closeDelim = b; break; }
            }

            if (openDelim >= 0 && closeDelim >= 0)
            {
                _doc.RemoveBlockAt(closeDelim);
                _doc.RemoveBlockAt(openDelim);
            }
        }
        else
        {
            _doc.InsertBlockAt(eb + 1, "```");
            _doc.InsertBlockAt(sb, "```");
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    private void ToggleBlockPrefixForSelection(string prefix)
    {
        SealAndStopTimer();
        var (sb, _, eb, _) = _doc.HasSelection
            ? _doc.GetOrderedSelection()
            : (_doc.CursorBlock, 0, _doc.CursorBlock, 0);

        bool allHavePrefix = true;
        for (int b = sb; b <= eb; b++)
        {
            if (!_doc.GetBlockText(b).StartsWith(prefix))
            {
                allHavePrefix = false;
                break;
            }
        }

        _doc.BeginUndoGroup();

        if (allHavePrefix)
        {
            for (int b = sb; b <= eb; b++)
                _doc.ToggleBlockPrefix(b, prefix);
        }
        else
        {
            for (int b = sb; b <= eb; b++)
            {
                if (!_doc.GetBlockText(b).StartsWith(prefix))
                    _doc.ToggleBlockPrefix(b, prefix);
            }
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    // --- Formatting query properties ---

    public BlockKind CurrentBlockKind
    {
        get
        {
            if (!_measured) return BlockKind.Paragraph;
            ComputeLayout();
            return _parsedBlocks![_doc.CursorBlock].Kind;
        }
    }

    public bool SelectionIsBold => SelectionHasStyle(InlineStyle.Bold);
    public bool SelectionIsItalic => SelectionHasStyle(InlineStyle.Italic);
    public bool SelectionIsCode => SelectionHasStyle(InlineStyle.Code);
    public bool SelectionIsStrikethrough => SelectionHasStyle(InlineStyle.Strikethrough);

    private bool SelectionHasStyle(InlineStyle targetStyle)
    {
        if (!_measured || !_doc.HasSelection) return false;
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        so = Math.Min(so, _doc.GetBlockLength(sb));
        eo = Math.Min(eo, _doc.GetBlockLength(eb));

        ComputeLayout();

        for (int b = sb; b <= eb; b++)
        {
            int blockSelStart = (b == sb) ? so : 0;
            int blockSelEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (blockSelStart >= blockSelEnd) continue;

            var parsed = _parsedBlocks![b];
            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                if (runEnd <= blockSelStart || run.Start >= blockSelEnd) continue;
                if (run.Style != targetStyle && run.Style != InlineStyle.BoldItalic)
                    return false;
                if (run.Style == InlineStyle.BoldItalic &&
                    targetStyle != InlineStyle.Bold && targetStyle != InlineStyle.Italic)
                    return false;
            }
        }
        return true;
    }

    private void RaiseFormattingChanged()
    {
        FormattingChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsVisual => _editMode == EditMode.Visual;

    internal int TestCursorBlock => _doc.CursorBlock;
    internal int TestCursorOffset => _doc.CursorOffset;
    internal void TestSetCursor(int block, int offset)
    {
        _doc.CursorBlock = block;
        _doc.CursorOffset = offset;
        _doc.CollapseSelection();
    }
    internal void TestSetEditMode(EditMode mode)
    {
        _editMode = mode;
        InvalidateLayout();
    }
    internal void TestComputeLayout() => ComputeLayout();
    internal void TestNavigate(Key key, bool shift = false, bool ctrl = false)
    {
        ComputeLayout();
        switch (key)
        {
            case Key.Left: HandleLeft(shift); break;
            case Key.Right: HandleRight(shift); break;
            case Key.Up: HandleUp(shift); break;
            case Key.Down: HandleDown(shift); break;
            case Key.Home: HandleHome(shift, ctrl); break;
            case Key.End: HandleEnd(shift, ctrl); break;
        }
    }

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
                _palette.Foreground, _dpiScale);
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
        _visualMaps = null;
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
                    _palette.Foreground, _dpiScale);
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

    private double MeasureReplacementPrefix(string prefix, BlockKind blockKind)
    {
        double total = 0;
        for (int i = 0; i < prefix.Length; i++)
            total += MeasureCharWidth(prefix[i], blockKind, InlineStyle.Normal);
        return total;
    }

    // --- Layout ---

    private void ComputeLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;

        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount);

        if (IsVisual && _visualMaps == null)
        {
            _visualMaps = new List<BlockVisualMap>(_doc.BlockCount);
            for (int i = 0; i < _doc.BlockCount; i++)
                _visualMaps.Add(BlockVisualMap.Compute(_parsedBlocks[i], _doc.GetBlockText(i)));
        }

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
            var parsed = _parsedBlocks![bi];

            if (IsVisual && parsed.IsFenceDelimiter)
                continue;

            string text = _doc.GetBlockText(bi);
            var map = IsVisual ? _visualMaps?[bi] : null;
            var segments = text.Split('\n');
            int offset = 0;
            for (int s = 0; s < segments.Length; s++)
            {
                WrapSegment(bi, offset, segments[s], maxWidth, parsed, map);
                offset += segments[s].Length + 1;
            }
        }

        double y = _padding;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            int bi = _visualLines[i].BlockIndex;
            if (i > 0 && bi != _visualLines[i - 1].BlockIndex)
            {
                bool paragraphBreak = false;
                for (int prev = _visualLines[i - 1].BlockIndex; prev < bi && !paragraphBreak; prev++)
                {
                    if (_doc.GetBlockLength(prev) == 0)
                        paragraphBreak = true;
                    else if (_doc.GetBlockText(prev).EndsWith("  "))
                        paragraphBreak = true;
                }
                if (paragraphBreak)
                    y += _paragraphGap;
            }
            _lineYPositions.Add(y);
            y += GetLineHeight(_visualLines[i].BlockKind);
        }
        _totalContentHeight = y + _padding;
    }

    private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth,
        ParsedBlock parsed, BlockVisualMap? map = null)
    {
        if (segment.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, startOffset, 0, parsed.Kind));
            return;
        }

        double prefixWidth = 0;
        if (map?.ReplacementPrefix != null)
            prefixWidth = MeasureReplacementPrefix(map.ReplacementPrefix, parsed.Kind);

        int pos = 0;
        while (pos < segment.Length)
        {
            double lineMax = pos == 0 ? maxWidth - prefixWidth : maxWidth;
            int lineLen = FitLine(segment, pos, lineMax, parsed, map, startOffset);
            _visualLines.Add(new VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind));
            pos += lineLen;
        }
    }

    private int FitLine(string text, int start, double maxWidth, ParsedBlock parsed,
        BlockVisualMap? map = null, int blockOffset = 0)
    {
        int lastSpace = -1;
        double width = 0;
        int runIdx = 0;
        bool anyVisible = false;
        for (int i = start; i < text.Length; i++)
        {
            if (map != null && map.IsHidden(blockOffset + i)) continue;
            if (text[i] == ' ') lastSpace = i;
            var style = GetStyleAtOffset(parsed.Runs, blockOffset + i, ref runIdx);
            width += MeasureCharWidth(text[i], parsed.Kind, style);
            anyVisible = true;
            if (width > maxWidth && anyVisible && i > start)
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
        var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double x = 0;
        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            x += MeasureReplacementPrefix(map.ReplacementPrefix!, vl.BlockKind);

        if (localOffset == 0) return x;

        var parsed = _parsedBlocks![vl.BlockIndex];
        int runIdx = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + localOffset; i++)
        {
            if (map != null && map.IsHidden(i)) continue;
            var style = GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            x += MeasureCharWidth(blockText[i], parsed.Kind, style);
        }
        return x;
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);
        var parsed = _parsedBlocks![vl.BlockIndex];
        double accum = 0;

        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            double prefixW = MeasureReplacementPrefix(map.ReplacementPrefix!, vl.BlockKind);
            if (x < prefixW) return vl.StartOffset;
            accum = prefixW;
        }

        int runIdx = 0;
        for (int i = 0; i < vl.Length; i++)
        {
            int offset = vl.StartOffset + i;
            if (map != null && map.IsHidden(offset)) continue;
            var style = GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
                return offset;
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private void ToggleInlineStyle(string marker, InlineStyle targetStyle)
    {
        if (!_doc.HasSelection) return;

        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        so = Math.Min(so, _doc.GetBlockLength(sb));
        eo = Math.Min(eo, _doc.GetBlockLength(eb));

        ComputeLayout();

        int markerLen = marker.Length;
        int styleMarkerLen = targetStyle switch
        {
            InlineStyle.Bold => 2,
            InlineStyle.Italic => 1,
            InlineStyle.BoldItalic => 3,
            InlineStyle.Code => 1,
            InlineStyle.Strikethrough => 2,
            _ => 0,
        };

        bool allStyled = true;
        for (int b = sb; b <= eb; b++)
        {
            int bStart = (b == sb) ? so : 0;
            int bEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (bStart >= bEnd) continue;

            var parsed = _parsedBlocks![b];
            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                if (runEnd <= bStart || run.Start >= bEnd) continue;

                int contentStart = run.Start + styleMarkerLen;
                int contentEnd = runEnd - styleMarkerLen;
                int overlapStart = Math.Max(bStart, contentStart);
                int overlapEnd = Math.Min(bEnd, contentEnd);
                if (overlapStart < overlapEnd && run.Style == targetStyle) continue;

                allStyled = false;
                break;
            }
            if (!allStyled) break;
        }

        _doc.BeginUndoGroup();

        int newSo = so, newEo = eo;

        for (int b = eb; b >= sb; b--)
        {
            int bStart = (b == sb) ? so : 0;
            int bEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (bStart >= bEnd) continue;

            if (allStyled)
            {
                var parsed = _parsedBlocks![b];
                foreach (var run in parsed.Runs)
                {
                    int runEnd = run.Start + run.Length;
                    if (runEnd <= bStart || run.Start >= bEnd) continue;
                    if (run.Style != targetStyle) continue;

                    _doc.RemoveTextAt(b, runEnd - markerLen, markerLen);
                    _doc.RemoveTextAt(b, run.Start, markerLen);

                    if (b == eb)
                    {
                        newEo = eo - markerLen;
                        if (eo > runEnd - markerLen) newEo -= markerLen;
                    }
                    if (b == sb)
                        newSo = so - markerLen;
                    break;
                }
            }
            else
            {
                _doc.InsertTextAt(b, bEnd, marker);
                _doc.InsertTextAt(b, bStart, marker);
                if (b == sb) newSo = so + markerLen;
                if (b == eb) newEo = eo + markerLen;
            }
        }

        _doc.AnchorBlock = sb;
        _doc.AnchorOffset = newSo;
        _doc.CursorBlock = eb;
        _doc.CursorOffset = newEo;
        _doc.SealUndoGroup();
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

        if (e.ClickCount == 2)
        {
            _doc.SelectWord(block, offset);
            CaptureMouse();
            ResetBlink();
            InvalidateVisual();
            return;
        }

        _doc.CursorBlock = block;
        _doc.CursorOffset = offset;
        SkipCursorOverHiddenRanges(forward: true);

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _doc.CollapseSelection();

        CaptureMouse();
        ResetBlink();
        InvalidateVisual();
        RaiseFormattingChanged();
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
        SkipCursorOverHiddenRanges(forward: true);

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

    // --- Key handlers (Source / Visual dispatch) ---

    private void HandleBack(bool shift, out bool textChanged)
    {
        textChanged = false;
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
        else if (IsVisual) textChanged = HandleBackVisual();
        else textChanged = HandleBackSource();
        ResetUndoSealTimer();
    }

    private void HandleDelete(bool shift, out bool textChanged)
    {
        textChanged = false;
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
        else if (IsVisual) textChanged = HandleDeleteVisual();
        else textChanged = HandleDeleteSource();
        ResetUndoSealTimer();
    }

    private void HandleLeft(bool shift)
    {
        SealAndStopTimer();
        if (IsVisual) HandleLeftVisual(shift);
        else HandleLeftSource(shift);
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleRight(bool shift)
    {
        SealAndStopTimer();
        if (IsVisual) HandleRightVisual(shift);
        else HandleRightSource(shift);
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleHome(bool shift, bool ctrl)
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
        if (IsVisual) HandleHomeVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleEnd(bool shift, bool ctrl)
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
        if (IsVisual) HandleEndVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleUp(bool shift)
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
        if (IsVisual) HandleUpVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleDown(bool shift)
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
        if (IsVisual) HandleDownVisual();
        if (!shift) _doc.CollapseSelection();
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
                _doc.InsertParagraphBreak();
                _doc.CollapseSelection();
                _doc.SealUndoGroup();
                textChanged = true;
                break;

            case Key.Back:
                HandleBack(shift, out textChanged);
                break;

            case Key.Delete:
                HandleDelete(shift, out textChanged);
                break;

            case Key.Left:
                HandleLeft(shift);
                break;

            case Key.Right:
                HandleRight(shift);
                break;

            case Key.Up:
                HandleUp(shift);
                break;

            case Key.Down:
                HandleDown(shift);
                break;

            case Key.Home:
                HandleHome(shift, ctrl);
                break;

            case Key.End:
                HandleEnd(shift, ctrl);
                break;

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
                // cursor skip deferred to after InvalidateLayout below
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

            case Key.B:
                if (ctrl) ToggleBold();
                else handled = false;
                break;

            case Key.I:
                if (ctrl) ToggleItalic();
                else handled = false;
                break;

            case Key.M:
                if (ctrl)
                {
                    SealAndStopTimer();
                    _editMode = _editMode == EditMode.Source ? EditMode.Visual : EditMode.Source;
                    InvalidateLayout();
                    if (IsVisual)
                    {
                        ComputeLayout();
                        EnsureCursorOnVisibleBlock();
                        SkipCursorToVisible(forward: true);
                    }
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
            {
                InvalidateLayout();
                if (IsVisual)
                {
                    ComputeLayout();
                    EnsureCursorOnVisibleBlock();
                    SkipCursorToVisible(forward: true);
                }
            }
            else
                InvalidateVisual();
            EnsureCursorVisible();
            e.Handled = true;
            RaiseFormattingChanged();
        }
    }

    // --- Rendering ---

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMeasured();
        dc.DrawRectangle(_palette.Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

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
                var parsed = _parsedBlocks![vl.BlockIndex];
                double fontSize = GetBlockFontSize(parsed.Kind);
                var baseTypeface = GetBlockBaseTypeface(parsed.Kind);
                var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

                double textX = _padding;

                if (map != null)
                {
                    if (map.ReplacementPrefix != null && vl.StartOffset == 0)
                    {
                        var prefixFt = new FormattedText(map.ReplacementPrefix!,
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            _normalTypeface, fontSize, _palette.Syntax, _dpiScale);
                        dc.DrawText(prefixFt, new Point(_padding, lineY - effectiveScroll));
                        textX += MeasureReplacementPrefix(map.ReplacementPrefix!, parsed.Kind);
                    }

                    string displayText = map.BuildDisplayString(blockText, vl.StartOffset, vl.Length);
                    if (displayText.Length > 0)
                    {
                        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, baseTypeface, fontSize,
                            _palette.Foreground, _dpiScale);
                        ApplyInlineStylesVisual(ft, vl, parsed, map);
                        dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));
                    }
                }
                else
                {
                    string text = blockText.Substring(vl.StartOffset, vl.Length);
                    var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, baseTypeface, fontSize,
                        _palette.Foreground, _dpiScale);
                    ApplyInlineStyles(ft, vl, parsed);
                    dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));
                }
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli] - effectiveScroll;
            double lineH = GetLineHeight(_visualLines[vli].BlockKind);
            dc.DrawLine(_palette.CursorPen, new Point(cx, cy), new Point(cx, cy + lineH));
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
                case InlineStyle.Strikethrough:
                    ft.SetTextDecorations(TextDecorations.Strikethrough, localStart, count);
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
                ft.SetForegroundBrush(_palette.Syntax, localStart, localEnd - localStart);
        }

        if (parsed.Kind == BlockKind.UnorderedListItem && vl.Length >= 2)
        {
            string vlText = blockText.Substring(vl.StartOffset, Math.Min(vl.Length, 2));
            if (vlText is "- " or "* ")
                ft.SetForegroundBrush(_palette.Syntax, 0, 2);
        }

        if (parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0 && vl.Length >= 2)
            ft.SetForegroundBrush(_palette.Syntax, 0, 2);

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
                InlineStyle.Strikethrough => 2,
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

    private void DimRange(FormattedText ft, VisualLine vl, int docStart, int length)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        int localStart = Math.Max(0, docStart - vl.StartOffset);
        int localEnd = Math.Min(vl.Length, docStart + length - vl.StartOffset);
        if (localEnd > localStart)
            ft.SetForegroundBrush(_palette.Syntax, localStart, localEnd - localStart);
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

            dc.DrawRectangle(_palette.CodeBackground, null,
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
            var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

            double x1 = MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                parsed.Runs, parsed.Kind, map);
            double x2 = MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                parsed.Runs, parsed.Kind, map);

            if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            {
                double prefixW = MeasureReplacementPrefix(map.ReplacementPrefix!, parsed.Kind);
                x1 += prefixW;
                x2 += prefixW;
            }

            bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            dc.DrawRectangle(_palette.Selection, null,
                new Rect(_padding + x1, lineY - effectiveScroll, x2 - x1, lineH));
        }
    }

    private double MeasureRangeWidth(string text, int start, int length,
        IReadOnlyList<StyledRun> runs, BlockKind blockKind, BlockVisualMap? map)
    {
        if (length <= 0) return 0;
        double total = 0;
        int runIdx = 0;
        for (int i = start; i < start + length; i++)
        {
            if (map != null && map.IsHidden(i)) continue;
            var style = GetStyleAtOffset(runs, i, ref runIdx);
            total += MeasureCharWidth(text[i], blockKind, style);
        }
        return total;
    }

    private void DrawScrollbar(DrawingContext dc)
    {
        double trackX = ActualWidth - ScrollBarWidth;
        dc.DrawRectangle(_palette.ScrollTrack, null,
            new Rect(trackX, 0, ScrollBarWidth, ActualHeight));

        var (thumbY, thumbH) = GetScrollbarThumbRect();
        dc.DrawRectangle(_palette.ScrollThumb, null,
            new Rect(trackX + 1, thumbY, ScrollBarWidth - 2, thumbH));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateLayout();
    }
}
