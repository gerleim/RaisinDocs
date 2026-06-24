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
        Brush Syntax, Brush CodeBackground,
        Brush TableBackground, Brush TableHeaderBackground, Pen TableBorderPen);

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
            codeBackground: Color.FromArgb(25, 0, 0, 0),
            tableBg: Color.FromArgb(15, 0, 0, 0),
            tableHeaderBg: Color.FromArgb(30, 0, 0, 0),
            tableBorder: Color.FromArgb(60, 0, 0, 0));

        _darkPalette = BuildPalette(
            background: Color.FromRgb(30, 30, 30),
            foreground: Color.FromRgb(212, 212, 212),
            cursor: Colors.White,
            selection: Color.FromArgb(100, 38, 79, 120),
            scrollTrack: Color.FromArgb(30, 255, 255, 255),
            scrollThumb: Color.FromArgb(120, 128, 128, 128),
            syntax: Color.FromArgb(180, 110, 110, 110),
            codeBackground: Color.FromArgb(25, 255, 255, 255),
            tableBg: Color.FromArgb(15, 255, 255, 255),
            tableHeaderBg: Color.FromArgb(30, 255, 255, 255),
            tableBorder: Color.FromArgb(60, 255, 255, 255));
    }

    private static ThemePalette BuildPalette(
        Color background, Color foreground, Color cursor,
        Color selection, Color scrollTrack, Color scrollThumb,
        Color syntax, Color codeBackground,
        Color tableBg, Color tableHeaderBg, Color tableBorder)
    {
        var cursorBrush = new SolidColorBrush(cursor);
        cursorBrush.Freeze();
        var cursorPen = new Pen(cursorBrush, 1.5);
        cursorPen.Freeze();
        var tBorderPen = new Pen(Frozen(tableBorder), 1);
        tBorderPen.Freeze();

        return new ThemePalette(
            Frozen(background), Frozen(foreground), cursorPen,
            Frozen(selection), Frozen(scrollTrack), Frozen(scrollThumb),
            Frozen(syntax), Frozen(codeBackground),
            Frozen(tableBg), Frozen(tableHeaderBg), tBorderPen);

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

    private record struct VisualLine(int BlockIndex, int StartOffset, int Length, BlockKind BlockKind)
    {
        public double OverrideHeight { get; init; }
    }
    private readonly List<VisualLine> _visualLines = [];
    private readonly List<double> _lineYPositions = [];
    private readonly Dictionary<TableInfo, double[]> _tableColumnWidths = new();
    private bool _layoutDirty = true;
    private double _totalContentHeight;
    private double _layoutMaxWidth;
    private double _scrollOffset;
    private bool _scrollbarVisible;
    private readonly SmoothScroller _smoother;
    private bool _isDraggingThumb;
    private double _dragStartY;
    private double _dragStartScroll;

    private List<ParsedBlock>? _parsedBlocks;
    private List<BlockVisualMap>? _visualMaps;
    private readonly ImageCache _imageCache = new();

    public string? DocumentBasePath { get; set; }

    public enum EditMode { Source, Visual }
    private EditMode _editMode = EditMode.Source;
    public EditMode CurrentEditMode => _editMode;

    public enum ImagePreviewMode { Off, Inline, OnHover }
    private ImagePreviewMode _imagePreview = ImagePreviewMode.Off;
    public ImagePreviewMode CurrentImagePreview => _imagePreview;
    private InlineImage? _hoveredImage;
    private Point _hoverPosition;

    public event EventHandler? FormattingChanged;
    public event EventHandler? EditModeChanged;

    public string GetText() => _doc.GetText();

    public void SetText(string text)
    {
        _doc.SetText(text);
        InvalidateLayout();
    }

    public void ToggleTheme() => Theme = Theme == EditorTheme.Light ? EditorTheme.Dark : EditorTheme.Light;

    public void ToggleEditMode()
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
        EditModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CycleImagePreview()
    {
        _imagePreview = _imagePreview switch
        {
            ImagePreviewMode.Off => ImagePreviewMode.Inline,
            ImagePreviewMode.Inline => ImagePreviewMode.OnHover,
            _ => ImagePreviewMode.Off,
        };
        _hoveredImage = null;
        InvalidateLayout();
    }

    public void SetImagePreview(ImagePreviewMode mode)
    {
        if (_imagePreview == mode) return;
        _imagePreview = mode;
        _hoveredImage = null;
        InvalidateLayout();
    }

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
    internal void TestSetImagePreview(ImagePreviewMode mode)
    {
        _imagePreview = mode;
        InvalidateLayout();
    }
    internal void TestComputeLayout() => ComputeLayout();
    internal void TestInsert(string text)
    {
        ComputeLayout();
        foreach (char c in text)
            _doc.Insert(c);
        _doc.CollapseSelection();
        InvalidateLayout();
    }
    internal void TestNavigate(Key key, bool shift = false, bool ctrl = false)
    {
        ComputeLayout();
        switch (key)
        {
            case Key.Left: HandleLeft(shift, ctrl); break;
            case Key.Right: HandleRight(shift, ctrl); break;
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
        BlockKind.TableHeaderRow => style switch
        {
            InlineStyle.Italic or InlineStyle.BoldItalic => _boldItalicTypeface,
            InlineStyle.Code => _monoTypeface,
            _ => _boldTypeface,
        },
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
        BlockKind.TableHeaderRow => style switch
        {
            InlineStyle.Italic or InlineStyle.BoldItalic => _boldItalicGlyph,
            InlineStyle.Code => _monoGlyph,
            _ => _boldGlyph,
        },
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
        if (blockKind == BlockKind.TableHeaderRow && fontId == 0) fontId = 2;
        else if (blockKind == BlockKind.TableHeaderRow && fontId == 3) fontId = 4;
        int sizeKey = (int)GetBlockFontSize(blockKind);
        return fontId * 100 + sizeKey;
    }

    private double GetLineHeight(BlockKind kind)
    {
        return _lineHeights.TryGetValue(kind, out double h) ? h : _lineHeights[BlockKind.Paragraph];
    }

    private double GetEffectiveLineHeight(VisualLine vl)
    {
        double h = GetLineHeight(vl.BlockKind);
        return vl.OverrideHeight > h ? vl.OverrideHeight : h;
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

    private (double Width, double Height) GetImageSize(InlineImage img, double maxWidth)
    {
        var cached = _imageCache.Get(img.Url, DocumentBasePath, maxWidth);
        if (cached != null)
            return (cached.Value.Width, cached.Value.Height);
        _imageCache.RequestLoad(img.Url, DocumentBasePath, () => InvalidateVisual());
        return (20, 20);
    }

    private static InlineImage? FindImageAtRawOffset(IReadOnlyList<InlineImage>? images, int rawOffset)
    {
        if (images == null) return null;
        foreach (var img in images)
        {
            if (img.Start == rawOffset) return img;
            if (img.Start > rawOffset) break;
        }
        return null;
    }

    private double GetImageMaxLineHeight(VisualLine vl, BlockVisualMap? map)
    {
        if (map?.Images == null) return 0;
        double maxH = 0;
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var img in map.Images)
        {
            if (img.Start >= vl.StartOffset && img.Start < vlEnd)
            {
                var (_, h) = GetImageSize(img, _layoutMaxWidth);
                if (h > maxH) maxH = h;
            }
        }
        return maxH;
    }

    private double GetSourceInlineImageHeight(VisualLine vl, IReadOnlyList<InlineImage> images)
    {
        double totalH = 0;
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var img in images)
        {
            if (img.Start >= vl.StartOffset && img.Start < vlEnd)
            {
                var (_, h) = GetImageSize(img, _layoutMaxWidth);
                totalH += h;
            }
        }
        return totalH;
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
        _tableColumnWidths.Clear();
        maxWidth = Math.Max(0, maxWidth);
        _layoutMaxWidth = maxWidth;

        if (IsVisual)
            ComputeAllTableColumnWidths(maxWidth);

        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];

            if (IsVisual && parsed.IsSkippedInVisual)
                continue;

            string text = _doc.GetBlockText(bi);
            var map = IsVisual ? _visualMaps?[bi] : null;

            if (IsVisual && parsed.Table != null && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                _visualLines.Add(new VisualLine(bi, 0, text.Length, parsed.Kind));
                continue;
            }

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
            var lineVl = _visualLines[i];
            double lineH = GetLineHeight(lineVl.BlockKind);
            if (lineVl.OverrideHeight > lineH) lineH = lineVl.OverrideHeight;
            y += lineH;
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
            var vl = new VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind);
            if (IsVisual && map?.Images != null)
            {
                double imgH = GetImageMaxLineHeight(vl, map);
                if (imgH > 0) vl = vl with { OverrideHeight = imgH };
            }
            else if (!IsVisual && _imagePreview == ImagePreviewMode.Inline && parsed.Images != null)
            {
                double imgH = GetSourceInlineImageHeight(vl, parsed.Images);
                if (imgH > 0)
                    vl = vl with { OverrideHeight = GetLineHeight(parsed.Kind) + imgH };
            }
            _visualLines.Add(vl);
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
            int rawOffset = blockOffset + i;
            if (map != null && map.IsHidden(rawOffset))
            {
                var img = FindImageAtRawOffset(map.Images, rawOffset);
                if (img != null)
                {
                    var (imgW, _) = GetImageSize(img.Value, _layoutMaxWidth);
                    if (width + imgW > maxWidth && anyVisible && i > start)
                    {
                        if (lastSpace >= start)
                            return lastSpace - start + 1;
                        return i - start;
                    }
                    width += imgW;
                    anyVisible = true;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            if (text[i] == ' ') lastSpace = i;
            var style = GetStyleAtOffset(parsed.Runs, rawOffset, ref runIdx);
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

    private const double _tableCellPadding = 8;

    private void ComputeAllTableColumnWidths(double maxWidth)
    {
        var seen = new HashSet<TableInfo>();
        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];
            if (parsed.Table == null || parsed.TableRow == null) continue;
            if (!seen.Add(parsed.Table)) continue;

            int colCount = parsed.Table.ColumnCount;
            var widths = new double[colCount];

            for (int bj = bi; bj < _doc.BlockCount; bj++)
            {
                var p = _parsedBlocks[bj];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator || p.TableRow == null) continue;

                string text = _doc.GetBlockText(bj);
                for (int c = 0; c < Math.Min(p.TableRow.Cells.Count, colCount); c++)
                {
                    var cell = p.TableRow.Cells[c];
                    string cellText = text.Substring(cell.Start, cell.Length).Trim();
                    double w = MeasureStringWidth(cellText, p.Kind, p.Runs, cell.Start);
                    if (w > widths[c]) widths[c] = w;
                }
            }

            for (int c = 0; c < colCount; c++)
                widths[c] += _tableCellPadding * 2;

            _tableColumnWidths[parsed.Table] = widths;
        }
    }

    private double MeasureStringWidth(string text, BlockKind kind, IReadOnlyList<StyledRun> runs, int blockOffset)
    {
        double w = 0;
        int runIdx = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var style = GetStyleAtOffset(runs, blockOffset + i, ref runIdx);
            w += MeasureCharWidth(text[i], kind, style);
        }
        return w;
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

        var parsed = _parsedBlocks![vl.BlockIndex];
        if (IsVisual && parsed.Table != null && parsed.TableRow != null
            && _tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return CursorXInTableRow(vl.BlockIndex, parsed, colWidths, localOffset);
        }

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double x = 0;
        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            x += MeasureReplacementPrefix(map.ReplacementPrefix!, vl.BlockKind);

        if (localOffset == 0) return x;

        if (map == null)
        {
            string lineText = blockText.Substring(vl.StartOffset, vl.Length);
            var ft = new FormattedText(lineText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GetBlockBaseTypeface(vl.BlockKind),
                GetBlockFontSize(vl.BlockKind), _palette.Foreground, _dpiScale);
            ApplyInlineStyles(ft, vl, parsed);
            var geom = ft.BuildHighlightGeometry(new Point(0, 0), 0, localOffset);
            return x + (geom != null ? geom.Bounds.Right : ft.WidthIncludingTrailingWhitespace);
        }

        int runIdx = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + localOffset; i++)
        {
            if (map.IsHidden(i))
            {
                var img = FindImageAtRawOffset(map.Images, i);
                if (img != null)
                {
                    var (imgW, _) = GetImageSize(img.Value, _layoutMaxWidth);
                    x += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            var style = GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            x += MeasureCharWidth(blockText[i], parsed.Kind, style);
        }
        return x;
    }

    private double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = _doc.GetBlockText(blockIndex);

        // find which cell the cursor is in
        double x = 0;
        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            var cell = cells[c];
            int cellEnd = cell.Start + cell.Length;
            if (cursorOffset >= cell.Start && cursorOffset <= cellEnd)
            {
                string cellContent = blockText.Substring(cell.Start, cell.Length).Trim();
                int leadTrim = 0;
                while (cell.Start + leadTrim < cellEnd && blockText[cell.Start + leadTrim] == ' ')
                    leadTrim++;

                int offsetInContent = Math.Clamp(cursorOffset - cell.Start - leadTrim, 0, cellContent.Length);
                int runIdx = 0;
                double textW = 0;
                for (int i = 0; i < offsetInContent; i++)
                {
                    var style = GetStyleAtOffset(parsed.Runs, cell.Start + leadTrim + i, ref runIdx);
                    textW += MeasureCharWidth(cellContent[i], parsed.Kind, style);
                }

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double fullTextW = MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, cell.Start + leadTrim);
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
                    _ => 0,
                };
                return x + _tableCellPadding + alignOffset + textW;
            }
            x += colWidths[c];
        }
        return x;
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        var parsed = _parsedBlocks![vl.BlockIndex];
        if (IsVisual && parsed.Table != null && parsed.TableRow != null
            && _tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return HitTestInTableRow(vl, parsed, colWidths, x);
        }

        var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);

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
            if (map != null && map.IsHidden(offset))
            {
                var img = FindImageAtRawOffset(map.Images, offset);
                if (img != null)
                {
                    var (imgW, _) = GetImageSize(img.Value, _layoutMaxWidth);
                    if (x < accum + imgW / 2)
                        return offset;
                    accum += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            var style = GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
                return offset;
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private int HitTestInTableRow(VisualLine vl, ParsedBlock parsed, double[] colWidths, double x)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double cx = 0;

        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            if (x < cx + colWidths[c] || c == cells.Count - 1 || c == colWidths.Length - 1)
            {
                var cell = cells[c];
                string cellContent = blockText.Substring(cell.Start, cell.Length).Trim();
                int leadTrim = 0;
                while (cell.Start + leadTrim < cell.Start + cell.Length && blockText[cell.Start + leadTrim] == ' ')
                    leadTrim++;

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double fullTextW = MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, cell.Start + leadTrim);
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
                    _ => 0,
                };

                double localX = x - cx - _tableCellPadding - alignOffset;
                double accum = 0;
                int runIdx = 0;
                for (int i = 0; i < cellContent.Length; i++)
                {
                    var style = GetStyleAtOffset(parsed.Runs, cell.Start + leadTrim + i, ref runIdx);
                    double charW = MeasureCharWidth(cellContent[i], parsed.Kind, style);
                    if (localX < accum + charW / 2)
                        return cell.Start + leadTrim + i;
                    accum += charW;
                }
                return cell.Start + leadTrim + cellContent.Length;
            }
            cx += colWidths[c];
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
            double lineH = GetEffectiveLineHeight(_visualLines[i]);
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
        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
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
            UpdateHoverImage(pos);
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

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredImage != null)
        {
            _hoveredImage = null;
            InvalidateVisual();
        }
    }

    private void UpdateHoverImage(Point pos)
    {
        if (IsVisual || _imagePreview != ImagePreviewMode.OnHover || _parsedBlocks == null)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        ComputeLayout();
        double effectiveScroll = _scrollOffset + _smoother.Offset;
        double hitY = pos.Y + effectiveScroll;
        int vli = HitTestVisualLine(hitY);
        if (vli < 0 || vli >= _visualLines.Count)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        var vl = _visualLines[vli];
        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Images == null)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        int offset = HitTestInVisualLine(vli, pos.X - _padding);
        InlineImage? found = null;
        foreach (var img in parsed.Images)
        {
            if (offset >= img.Start && offset < img.Start + img.Length)
            {
                found = img;
                break;
            }
        }

        if (found != _hoveredImage)
        {
            _hoveredImage = found;
            _hoverPosition = pos;
            InvalidateVisual();
        }
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

    private void HandleLeft(bool shift, bool ctrl = false)
    {
        SealAndStopTimer();
        if (ctrl)
        {
            if (!shift && _doc.HasSelection)
            {
                var (sb, so, _, _) = _doc.GetOrderedSelection();
                _doc.CursorBlock = sb;
                _doc.CursorOffset = so;
                _doc.CollapseSelection();
            }
            else
            {
                _doc.MoveWordLeft();
            }
            if (IsVisual) SkipCursorOverHiddenRanges(forward: false);
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            if (IsVisual) HandleLeftVisual(shift);
            else HandleLeftSource(shift);
            if (!shift) _doc.CollapseSelection();
        }
    }

    private void HandleRight(bool shift, bool ctrl = false)
    {
        SealAndStopTimer();
        if (ctrl)
        {
            if (!shift && _doc.HasSelection)
            {
                var (_, _, eb, eo) = _doc.GetOrderedSelection();
                _doc.CursorBlock = eb;
                _doc.CursorOffset = eo;
                _doc.CollapseSelection();
            }
            else
            {
                _doc.MoveWordRight();
            }
            if (IsVisual) SkipCursorOverHiddenRanges(forward: true);
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            if (IsVisual) HandleRightVisual(shift);
            else HandleRightSource(shift);
            if (!shift) _doc.CollapseSelection();
        }
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

    public void InsertTable(int columns, int rows)
    {
        SealAndStopTimer();
        _doc.BeginUndoGroup();
        if (_doc.HasSelection) _doc.DeleteSelection();

        string header = "| " + string.Join(" | ", Enumerable.Range(1, columns).Select(c => $"Header {c}")) + " |";
        string separator = "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |";
        var lines = new List<string> { header, separator };
        for (int r = 0; r < rows; r++)
            lines.Add("|" + string.Concat(Enumerable.Repeat("  |", columns)));

        if (_doc.CursorOffset > 0)
            _doc.InsertParagraphBreak();

        _doc.Paste(string.Join("\n", lines));
        _doc.CursorBlock -= lines.Count - 1;
        _doc.CursorOffset = 2;
        _doc.CollapseSelection();
        _doc.SealUndoGroup();
        InvalidateLayout();
        InvalidateVisual();
        EnsureCursorVisible();
    }

    private static bool IsTableRow(ParsedBlock parsed) =>
        parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow or BlockKind.TableSeparatorRow;

    private bool HandleTableTab(bool shift, out bool textChanged)
    {
        textChanged = false;
        if (_parsedBlocks == null) return false;
        var parsed = _parsedBlocks[_doc.CursorBlock];
        if (parsed.TableRow == null || parsed.Table == null) return false;

        SealAndStopTimer();
        var cells = parsed.TableRow.Cells;
        string blockText = _doc.GetBlockText(_doc.CursorBlock);
        int colCount = parsed.Table.ColumnCount;

        // find current cell
        int curCell = -1;
        for (int c = 0; c < cells.Count; c++)
        {
            if (_doc.CursorOffset >= cells[c].Start &&
                _doc.CursorOffset <= cells[c].Start + cells[c].Length)
            { curCell = c; break; }
        }
        if (curCell < 0) curCell = 0;

        if (!shift)
        {
            if (curCell + 1 < cells.Count)
            {
                var next = cells[curCell + 1];
                MoveCursorToCell(next, blockText);
            }
            else
            {
                // move to first cell of next data row
                for (int b = _doc.CursorBlock + 1; b < _doc.BlockCount; b++)
                {
                    var p = _parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.CursorBlock = b;
                        var nextBlockText = _doc.GetBlockText(b);
                        MoveCursorToCell(p.TableRow.Cells[0], nextBlockText);
                        break;
                    }
                    break;
                }
            }
        }
        else
        {
            if (curCell > 0)
            {
                var prev = cells[curCell - 1];
                MoveCursorToCell(prev, blockText);
            }
            else
            {
                // move to last cell of previous data row
                for (int b = _doc.CursorBlock - 1; b >= 0; b--)
                {
                    var p = _parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.CursorBlock = b;
                        var prevBlockText = _doc.GetBlockText(b);
                        var lastCell = p.TableRow.Cells[^1];
                        MoveCursorToCell(lastCell, prevBlockText);
                        break;
                    }
                    break;
                }
            }
        }

        _doc.CollapseSelection();
        return true;
    }

    private void MoveCursorToCell(TableCellInfo cell, string blockText)
    {
        int start = cell.Start;
        int end = cell.Start + cell.Length;
        while (start < end && blockText[start] == ' ') start++;
        while (end > start && blockText[end - 1] == ' ') end--;
        _doc.CursorOffset = start;
        _doc.AnchorBlock = _doc.CursorBlock;
        _doc.AnchorOffset = end;
    }

    private bool HandleTableEnter(out bool textChanged)
    {
        textChanged = false;
        if (_parsedBlocks == null) return false;
        var parsed = _parsedBlocks[_doc.CursorBlock];
        if (parsed.Table == null) return false;

        int colCount = parsed.Table.ColumnCount;
        string newRow = "|" + string.Concat(Enumerable.Repeat("  |", colCount));

        _doc.BeginUndoGroup();
        if (_doc.HasSelection) _doc.DeleteSelection();

        int insertAfter = _doc.CursorBlock;
        if (parsed.Kind == BlockKind.TableHeaderRow || parsed.Kind == BlockKind.TableSeparatorRow)
        {
            // skip past separator row so new row goes after it
            for (int b = insertAfter + 1; b < _doc.BlockCount; b++)
            {
                if (_parsedBlocks[b].Kind == BlockKind.TableSeparatorRow) { insertAfter = b; continue; }
                break;
            }
        }

        _doc.CursorBlock = insertAfter;
        _doc.CursorOffset = _doc.GetBlockLength(insertAfter);
        _doc.InsertParagraphBreak();
        _doc.Paste(newRow);
        _doc.CursorOffset = 2; // position in first cell
        _doc.CollapseSelection();
        _doc.SealUndoGroup();
        textChanged = true;
        return true;
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
            case Key.Tab:
                if (HandleTableTab(shift, out textChanged))
                    break;
                handled = false;
                break;

            case Key.Return:
                SealAndStopTimer();
                if (HandleTableEnter(out textChanged))
                    break;
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
                HandleLeft(shift, ctrl);
                break;

            case Key.Right:
                HandleRight(shift, ctrl);
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
                    ToggleEditMode();
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
        if (IsVisual)
            DrawTableBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

        if (_doc.HasSelection)
            DrawSelection(dc, effectiveScroll);

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            double lineH = GetEffectiveLineHeight(vl);
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

                if (IsVisual && parsed.Table != null && parsed.TableRow != null)
                {
                    DrawTableRow(dc, vl, blockText, parsed, lineY, effectiveScroll, fontSize, baseTypeface);
                }
                else if (map != null)
                {
                    if (HasImagesOnLine(vl, map))
                    {
                        DrawVisualLineWithImages(dc, vl, blockText, parsed, map,
                            lineY, effectiveScroll, fontSize, baseTypeface);
                    }
                    else
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
                }
                else
                {
                    string text = blockText.Substring(vl.StartOffset, vl.Length);
                    var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, baseTypeface, fontSize,
                        _palette.Foreground, _dpiScale);
                    ApplyInlineStyles(ft, vl, parsed);
                    dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));

                    if (_imagePreview == ImagePreviewMode.Inline && parsed.Images != null)
                        DrawSourceInlineImages(dc, vl, parsed.Images, lineY, effectiveScroll);
                }
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli] - effectiveScroll;
            double lineH = GetEffectiveLineHeight(_visualLines[vli]);
            dc.DrawLine(_palette.CursorPen, new Point(cx, cy), new Point(cx, cy + lineH));
        }

        if (!IsVisual && _imagePreview == ImagePreviewMode.OnHover && _hoveredImage != null)
            DrawHoverImagePreview(dc);

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

        if (parsed.Kind == BlockKind.TableSeparatorRow)
        {
            ft.SetForegroundBrush(_palette.Syntax, 0, vl.Length);
        }
        else if (parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
        {
            for (int ci = vl.StartOffset; ci < vlEnd; ci++)
            {
                if (ci > 0 && blockText[ci - 1] == '\\') continue;
                if (blockText[ci] == '|')
                    DimRange(ft, vl, ci, 1);
            }
        }

        if (parsed.Images != null)
        {
            foreach (var img in parsed.Images)
            {
                int imgEnd = img.Start + img.Length;
                if (imgEnd <= vl.StartOffset || img.Start >= vlEnd) continue;

                DimRange(ft, vl, img.Start, 2);
                int closeBracket = img.Start + 2 + img.AltText.Length;
                DimRange(ft, vl, closeBracket, imgEnd - closeBracket);
            }
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
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

    private void DrawTableBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        int i = 0;
        while (i < _visualLines.Count)
        {
            var vl = _visualLines[i];
            var parsed = _parsedBlocks![vl.BlockIndex];
            if (parsed.Table == null || parsed.Kind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
            {
                i++;
                continue;
            }

            var tableInfo = parsed.Table;
            int tableStart = i;
            int tableEnd = i;
            while (tableEnd < _visualLines.Count)
            {
                var p = _parsedBlocks[_visualLines[tableEnd].BlockIndex];
                if (p.Table != tableInfo) break;
                tableEnd++;
            }

            double tableY = _lineYPositions[tableStart];
            double tableBottom = tableEnd > 0
                ? _lineYPositions[tableEnd - 1] + GetEffectiveLineHeight(_visualLines[tableEnd - 1])
                : tableY;

            if (tableBottom >= viewTop && tableY <= viewBottom
                && _tableColumnWidths.TryGetValue(tableInfo, out var colWidths))
            {
                double tableWidth = 0;
                foreach (var w in colWidths) tableWidth += w;
                double tableX = _padding;
                double yTop = tableY - effectiveScroll;
                double tableH = tableBottom - tableY;

                dc.DrawRectangle(_palette.TableBackground, null,
                    new Rect(tableX, yTop, tableWidth, tableH));

                double headerH = GetLineHeight(_visualLines[tableStart].BlockKind);
                dc.DrawRectangle(_palette.TableHeaderBackground, null,
                    new Rect(tableX, yTop, tableWidth, headerH));

                dc.DrawRectangle(null, _palette.TableBorderPen,
                    new Rect(tableX, yTop, tableWidth, tableH));

                for (int row = tableStart; row < tableEnd; row++)
                {
                    double rowY = _lineYPositions[row] - effectiveScroll;
                    if (row > tableStart)
                        dc.DrawLine(_palette.TableBorderPen,
                            new Point(tableX, rowY), new Point(tableX + tableWidth, rowY));
                }

                double cx = tableX;
                for (int c = 0; c < colWidths.Length - 1; c++)
                {
                    cx += colWidths[c];
                    dc.DrawLine(_palette.TableBorderPen,
                        new Point(cx, yTop), new Point(cx, yTop + tableH));
                }
            }

            i = tableEnd;
        }
    }

    private void DrawTableRow(DrawingContext dc, VisualLine vl, string blockText,
        ParsedBlock parsed, double lineY, double effectiveScroll,
        double fontSize, Typeface baseTypeface)
    {
        if (parsed.TableRow == null || parsed.Table == null) return;
        if (!_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths)) return;

        double x = _padding;
        double y = lineY - effectiveScroll;
        double lineH = GetLineHeight(vl.BlockKind);
        bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;

        for (int c = 0; c < Math.Min(parsed.TableRow.Cells.Count, colWidths.Length); c++)
        {
            var cell = parsed.TableRow.Cells[c];
            string cellText = blockText.Substring(cell.Start, cell.Length).Trim();
            if (cellText.Length == 0) { x += colWidths[c]; continue; }

            var cellTypeface = isHeader ? _boldTypeface : baseTypeface;
            var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, cellTypeface, fontSize,
                _palette.Foreground, _dpiScale);

            ApplyInlineStylesForCell(ft, cellText, parsed, cell, blockText);

            var align = parsed.Table.Alignments[c];
            double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
            double textX;
            if (align == ColumnAlignment.Center)
                textX = x + _tableCellPadding + Math.Max(0, (cellContentWidth - ft.Width) / 2);
            else if (align == ColumnAlignment.Right)
                textX = x + _tableCellPadding + Math.Max(0, cellContentWidth - ft.Width);
            else
                textX = x + _tableCellPadding;

            var clipRect = new Rect(x, y, colWidths[c], lineH);
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawText(ft, new Point(textX, y));
            dc.Pop();

            x += colWidths[c];
        }
    }

    private static void ApplyInlineStylesForCell(FormattedText ft, string cellText,
        ParsedBlock parsed, TableCellInfo cell, string blockText)
    {
        if (parsed.Runs.Count <= 1) return;

        int rawStart = cell.Start;
        int rawEnd = cell.Start + cell.Length;
        int leadingTrim = 0;
        while (rawStart + leadingTrim < rawEnd && blockText[rawStart + leadingTrim] == ' ')
            leadingTrim++;
        int contentStart = rawStart + leadingTrim;

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= contentStart || run.Start >= rawEnd) continue;

            int overlapStart = Math.Max(run.Start, contentStart) - contentStart;
            int overlapEnd = Math.Min(runEnd, rawEnd) - contentStart;
            int len = Math.Min(overlapEnd - overlapStart, cellText.Length - overlapStart);
            if (len <= 0 || overlapStart >= cellText.Length) continue;

            switch (run.Style)
            {
                case InlineStyle.Bold or InlineStyle.BoldItalic:
                    ft.SetFontWeight(FontWeights.Bold, overlapStart, len);
                    break;
            }
            if (run.Style is InlineStyle.Italic or InlineStyle.BoldItalic)
                ft.SetFontStyle(FontStyles.Italic, overlapStart, len);
            if (run.Style == InlineStyle.Code)
                ft.SetFontFamily(new FontFamily("Cascadia Mono,Consolas"), overlapStart, len);
            if (run.Style == InlineStyle.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough, overlapStart, len);
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
            double lineH = GetEffectiveLineHeight(vl);
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

            double x1, x2;
            if (IsVisual && parsed.Table != null && parsed.TableRow != null)
            {
                if (_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                {
                    x1 = CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                    x2 = CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                }
                else
                {
                    x1 = 0; x2 = 0;
                }
            }
            else
            {
                x1 = MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                x2 = MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = MeasureReplacementPrefix(map.ReplacementPrefix!, parsed.Kind);
                    x1 += prefixW;
                    x2 += prefixW;
                }
            }

            bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            double selW = Math.Max(0, x2 - x1);
            if (selW > 0)
                dc.DrawRectangle(_palette.Selection, null,
                    new Rect(_padding + x1, lineY - effectiveScroll, selW, lineH));
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
            if (map != null && map.IsHidden(i))
            {
                var img = FindImageAtRawOffset(map.Images, i);
                if (img != null)
                {
                    var (imgW, _) = GetImageSize(img.Value, _layoutMaxWidth);
                    total += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
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
