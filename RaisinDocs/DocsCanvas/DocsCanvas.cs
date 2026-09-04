using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Raisin.WPF.Base;

namespace RaisinDocs;

[Flags]
public enum ReformatActions
{
    None = 0,
    ConvertBoxTable = 1,
    MergeParagraphs = 2,
    CollapseBlankLines = 4,
    TrimWhitespace = 8,
    RenumberOrderedList = 16,
    NormalizeMarkers = 32,
}

public partial class DocsCanvas : FrameworkElement, IMinimapDataProvider, IDocsCanvasServices, ISpellCheckAccess
{
    internal const double _padding = 10;
    private const double _paragraphGap = 8;


    public enum EditorTheme { Light, Dark, DarkBlue }

    internal sealed record ThemePalette(
        Brush Background, Brush Foreground, Pen CursorPen,
        Brush Selection, Brush ScrollTrack, Brush ScrollThumb,
        Brush Syntax, Brush CodeBackground,
        Brush TableBackground, Brush TableHeaderBackground, Pen TableBorderPen,
        Brush SearchMatch, Brush CurrentSearchMatch);

    private static readonly ThemePalette _lightPalette;
    private static readonly ThemePalette _darkPalette;
    private static readonly ThemePalette _darkBluePalette;
    internal ThemePalette _palette = _darkPalette!;

    internal static readonly Brush _checkboxCheckedBrush;
    internal static readonly Brush _imagePlaceholderBrush;
    internal readonly TextMeasurer _measure = new();
    private SyntaxHighlighter _syntaxHighlighter = new(TextMateSharp.Grammars.ThemeName.DarkPlus);
    private readonly Dictionary<int, Brush> _syntaxBrushCache = new();

    static DocsCanvas()
    {
        _checkboxCheckedBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x7A, 0xE0));
        _checkboxCheckedBrush.Freeze();
        _imagePlaceholderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        _imagePlaceholderBrush.Freeze();

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
            tableBorder: Color.FromArgb(60, 0, 0, 0),
            searchMatch: Color.FromArgb(80, 255, 210, 0),
            currentSearchMatch: Color.FromArgb(160, 255, 165, 0));

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
            tableBorder: Color.FromArgb(60, 255, 255, 255),
            searchMatch: Color.FromArgb(60, 255, 210, 0),
            currentSearchMatch: Color.FromArgb(130, 255, 165, 0));

        _darkBluePalette = BuildPalette(
            background: Color.FromRgb(13, 17, 23),
            foreground: Color.FromRgb(212, 212, 212),
            cursor: Colors.White,
            selection: Color.FromArgb(100, 30, 60, 120),
            scrollTrack: Color.FromArgb(30, 140, 160, 255),
            scrollThumb: Color.FromArgb(120, 100, 120, 180),
            syntax: Color.FromArgb(180, 90, 100, 130),
            codeBackground: Color.FromArgb(25, 100, 140, 255),
            tableBg: Color.FromArgb(15, 100, 140, 255),
            tableHeaderBg: Color.FromArgb(30, 100, 140, 255),
            tableBorder: Color.FromArgb(60, 100, 140, 255),
            searchMatch: Color.FromArgb(60, 200, 180, 0),
            currentSearchMatch: Color.FromArgb(130, 220, 160, 0));
    }

    private static ThemePalette BuildPalette(
        Color background, Color foreground, Color cursor,
        Color selection, Color scrollTrack, Color scrollThumb,
        Color syntax, Color codeBackground,
        Color tableBg, Color tableHeaderBg, Color tableBorder,
        Color searchMatch, Color currentSearchMatch)
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
            Frozen(tableBg), Frozen(tableHeaderBg), tBorderPen,
            Frozen(searchMatch), Frozen(currentSearchMatch));

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(EditorTheme), typeof(DocsCanvas),
            new FrameworkPropertyMetadata(EditorTheme.Dark, FrameworkPropertyMetadataOptions.AffectsRender, OnThemePropertyChanged));

    public EditorTheme Theme
    {
        get => (EditorTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (DocsCanvas)d;
        canvas._palette = canvas.Theme switch
        {
            EditorTheme.Dark => _darkPalette,
            EditorTheme.DarkBlue => _darkBluePalette,
            _ => _lightPalette,
        };
        if (canvas._linkPopup.IsOpen)
            canvas._linkPopup.ApplyTheme(canvas._palette.Background, canvas._palette.Foreground, canvas._palette.Syntax, canvas._palette.CodeBackground);
        canvas.FindBar?.ApplyTheme(canvas._palette.Background, canvas._palette.Foreground, canvas._palette.Syntax, canvas._palette.CodeBackground);
        canvas.TocPanel?.ApplyTheme(canvas._palette.Background, canvas._palette.Foreground, canvas._palette.Syntax, canvas._palette.CodeBackground);
        var tmTheme = canvas.Theme == EditorTheme.Light
            ? TextMateSharp.Grammars.ThemeName.LightPlus
            : TextMateSharp.Grammars.ThemeName.DarkPlus;
        canvas._syntaxHighlighter.SetTheme(tmTheme);
        canvas._syntaxBrushCache.Clear();
        canvas.InvalidateLayout();
        canvas.Minimap?.InvalidateVisual();
        canvas.ThemeChanged?.Invoke(canvas, EventArgs.Empty);
    }

    public event EventHandler? ThemeChanged;

    private static readonly DependencyPropertyKey IsDirtyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDirty), typeof(bool), typeof(DocsCanvas),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDirtyProperty = IsDirtyPropertyKey.DependencyProperty;

    public bool IsDirty
    {
        get => (bool)GetValue(IsDirtyProperty);
        private set
        {
            if (value == IsDirty) return;
            SetValue(IsDirtyPropertyKey, value);
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? IsDirtyChanged;

    public void MarkClean()
    {
        _doc.MarkClean();
        IsDirty = false;
    }

    public bool IsReadOnly { get; set; }

    internal readonly Document _doc = new();

    private bool _cursorVisible = true;
    private bool _cursorAtLineEnd;
    bool ICanvasOperations.CursorAtLineEnd { get => _cursorAtLineEnd; set => _cursorAtLineEnd = value; }
    private (string Marker, InlineStyle Style)? _pendingStyleOff;
    private readonly DispatcherTimer _blinkTimer;
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    internal readonly record struct JoinSegment(int BlockIndex, int OffsetInJoined, int Length);

    internal sealed class ParagraphGroup
    {
        public required JoinSegment[] Segments { get; init; }
        public required string JoinedText { get; init; }
        public required BlockVisualMap JoinedMap { get; init; }
        public required ParsedBlock JoinedParsed { get; init; }
        public required int[] SoftBreakOffsets { get; init; }

        public bool ContainsBlock(int blockIndex)
        {
            foreach (var seg in Segments)
                if (seg.BlockIndex == blockIndex) return true;
            return false;
        }

        public int SourceToJoined(int blockIndex, int offset)
        {
            foreach (var seg in Segments)
            {
                if (seg.BlockIndex == blockIndex)
                    return seg.OffsetInJoined + Math.Min(offset, seg.Length);
            }
            return -1;
        }

        public (int BlockIndex, int Offset) JoinedToSource(int joinedOffset)
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                var seg = Segments[i];
                int segEnd = seg.OffsetInJoined + seg.Length;
                if (joinedOffset >= seg.OffsetInJoined && joinedOffset <= segEnd)
                    return (seg.BlockIndex, joinedOffset - seg.OffsetInJoined);
                if (i < Segments.Length - 1 && joinedOffset == segEnd + 1)
                    continue;
            }
            var last = Segments[^1];
            return (last.BlockIndex, last.Length);
        }

        public int FirstBlock => Segments[0].BlockIndex;
        public int LastBlock => Segments[^1].BlockIndex;
    }

    internal record struct VisualLine(int BlockIndex, int StartOffset, int Length, BlockKind BlockKind)
    {
        public double OverrideHeight { get; init; }
        public ParagraphGroup? Group { get; init; }
        public int NestingDepth { get; init; }
        public int ParentContentColumn { get; init; }
    }
    internal readonly List<VisualLine> _visualLines = [];
    internal readonly List<double> _lineYPositions = [];
    internal readonly Dictionary<TableInfo, double[]> _tableColumnWidths = new();
    private bool _layoutDirty = true;
    private double _totalContentHeight;
    private double _layoutMaxWidth;
    internal readonly ScrollController _scroll;

    internal List<ParsedBlock>? _parsedBlocks;
    private VisualBlockStructure? _visualBlockStructure;
    internal List<BlockVisualMap>? _visualMaps;
    private List<BlockVisualSpacing?>? _visualLineSpacings;
    private Dictionary<int, ParagraphGroup>? _blockToGroup;
    private readonly ImageCache _imageCache = new();
    private int _layoutVersion;

    public string? DocumentBasePath { get; set; }
    public int BlockCount => _doc.BlockCount;

    internal event Action? ScrollStateChanged;
    internal double ScrollOffset => _scroll.Offset;
    internal double TotalContentHeight => _totalContentHeight;

    internal void SetScrollOffsetDirect(double offset) => _scroll.SetDirect(offset);
    internal void SmoothScrollTo(double targetOffset) => _scroll.SmoothScrollTo(targetOffset);

    public enum EditMode { Source, Visual }
    private EditMode _editMode = EditMode.Source;
    public EditMode CurrentEditMode => _editMode;

    public enum ImagePreviewMode { Off, Inline, OnHover }
    private ImagePreviewMode _imagePreview = ImagePreviewMode.Off;
    public ImagePreviewMode CurrentImagePreview => _imagePreview;
    internal InlineImage? _hoveredImage;
    internal Point _hoverPosition;

    private readonly LinkHandler _linkHandler;
    private readonly LinkPopupController _linkPopup;
    private readonly TableInputHandler _tableInputHandler;
    private readonly TableRenderer _tableRenderer;
    private readonly VisualModeManager _visualModeManager;
    private readonly ColorFormattingManager _colorFormatter;
    private readonly FormattingKeysHandler _formattingKeysHandler;
    private readonly NavigationKeysHandler _navigationKeysHandler;
    private readonly EditingKeysHandler _editingKeysHandler;
    private readonly ListFormattingHandler _listFormattingHandler;
    private readonly ContextMenuHandler _contextMenuHandler;
    private readonly IndentationHandler _indentationHandler;
    private readonly HoverImageHandler _hoverImageHandler;
    private readonly LayoutEngine _layoutEngine;
    private readonly RenderingContext _renderingContext;
    private readonly CursorNavigationEngine _navigationEngine;

    public enum SoftBreakMode { Relaxed, Strict }
    public enum HardBreakStyle { Backslash, TrailingSpaces }
    private SoftBreakMode _softBreak = SoftBreakMode.Relaxed;
    private HardBreakStyle _hardBreak = HardBreakStyle.Backslash;
    private bool _showWhitespace = true;
    public SoftBreakMode CurrentSoftBreak => _softBreak;
    public HardBreakStyle CurrentHardBreak => _hardBreak;
    public bool ShowWhitespace => _showWhitespace;

    public IDocsLogger? Logger { get; set; }

    /// <summary>
    /// Records scroll cadence to %LOCALAPPDATA%\RaisinDocs\scroll.log: one line per gesture
    /// with frame and paint intervals, pixel steps and per-piece costs.
    /// </summary>
    /// <remarks>
    /// Defaults to whether RAISINDOCS_SCROLL_DIAG is set. Set it before the canvas is built -
    /// from Application.OnStartup, not a window's Loaded - because the canvas wires its
    /// counters up in its constructor.
    /// </remarks>
    public static bool ScrollDiagnostics
    {
        get => ScrollDiag.Enabled;
        set => ScrollDiag.Enabled = value;
    }

    /// <summary>
    /// Records what each stage of a layout pass costs to
    /// %LOCALAPPDATA%\RaisinDocs\layout.log: one line per twenty passes, with the average
    /// and worst of parse, structure, maps and wrapping.
    /// </summary>
    /// <remarks>
    /// Typing invalidates layout, and layout redoes every stage over the whole document, so
    /// this is what a slow keystroke is made of. Unlike the scroll log it can be turned on at
    /// any time - nothing is wired up in the constructor.
    /// </remarks>
    public static bool LayoutDiagnostics
    {
        get => LayoutDiag.Enabled;
        set => LayoutDiag.Enabled = value;
    }
    internal DocsFormattingBar? FormattingBar { get; set; }
    internal FindBarController? FindBar { get; set; }

    public event EventHandler? ContentChanged;
    public event EventHandler? FormattingChanged;
    public event EventHandler? EditModeChanged;

    public string GetText() => _doc.GetText();

    public void SetText(string text)
    {
        _doc.SetText(text);
        _doc.MarkClean();
        IsDirty = false;
        InvalidateLayout();
        OnContentChangedForSpellCheck();
    }

    public void ToggleTheme() => SetCurrentValue(ThemeProperty, Theme switch
    {
        EditorTheme.Light => EditorTheme.Dark,
        EditorTheme.Dark => EditorTheme.DarkBlue,
        _ => EditorTheme.Light,
    });

    public void ToggleEditMode() =>
        SetEditMode(_editMode == EditMode.Source ? EditMode.Visual : EditMode.Source);

    public void SetEditMode(EditMode mode)
    {
        if (_editMode == mode) return;
        SealAndStopTimer();
        _scroll.StopWheelCoast();
        _scroll.CancelSmooth();

        var anchor = ComputeScrollAnchor();

        _editMode = mode;
        InvalidateLayout();
        if (IsVisual)
        {
            ComputeLayout();
            EnsureCursorOnVisibleBlock();
            if (_parsedBlocks != null && _doc.CursorBlock < _parsedBlocks.Count
                && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
            {
                ClampCursorToTableCell();
            }
            else
            {
                SkipCursorToVisible(forward: true);
                ClampCursorBeforeTrailingHidden();
            }
            _doc.CollapseSelection();
        }
        else
        {
            ComputeLayout();
        }

        ApplyScrollAnchor(anchor);
        Minimap?.InvalidateVisual();
        EditModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private (int BlockIndex, double OffsetInViewport) ComputeScrollAnchor()
    {
        if (_visualLines.Count == 0 || _lineYPositions.Count == 0)
            return (_doc.CursorBlock, 0);

        int cursorVli = CursorToVisualLineIndex();
        double cursorY = _lineYPositions[cursorVli];
        double cursorBottom = cursorY + GetEffectiveLineHeight(_visualLines[cursorVli]);
        double viewTop = _scroll.Offset;
        double viewBottom = _scroll.Offset + ActualHeight;

        bool cursorVisible = cursorBottom > viewTop && cursorY < viewBottom;

        if (cursorVisible)
        {
            return (_doc.CursorBlock, cursorY - viewTop);
        }

        int topVli = HitTestVisualLine(viewTop);
        int topBlock = _visualLines[topVli].BlockIndex;
        double topBlockY = _lineYPositions[topVli];
        return (topBlock, topBlockY - viewTop);
    }

    private void ApplyScrollAnchor((int BlockIndex, double OffsetInViewport) anchor)
    {
        if (_visualLines.Count == 0 || _lineYPositions.Count == 0)
            return;

        int targetVli = -1;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (_visualLines[i].BlockIndex >= anchor.BlockIndex)
            {
                targetVli = i;
                break;
            }
        }
        if (targetVli < 0)
            targetVli = _visualLines.Count - 1;

        double newY = _lineYPositions[targetVli];
        _scroll.Offset = newY - anchor.OffsetInViewport;
        _scroll.Clamp();
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

    public void SetSoftBreak(SoftBreakMode mode)
    {
        if (_softBreak == mode) return;
        _softBreak = mode;
        InvalidateLayout();
    }

    public void SetHardBreak(HardBreakStyle style)
    {
        if (_hardBreak == style) return;
        _hardBreak = style;
    }

    public double ZoomLevel => _measure.ZoomFactor;

    public void SetZoom(double factor, double anchorViewportY = -1)
    {
        factor = Math.Clamp(factor, 0.5, 3.0);
        factor = Math.Round(factor, 2);
        if (Math.Abs(_measure.ZoomFactor - factor) < 0.001) return;

        ComputeLayout();

        double anchorDocY;
        if (anchorViewportY >= 0)
        {
            anchorDocY = _scroll.Offset + anchorViewportY;
        }
        else
        {
            int vli = CursorToVisualLineIndex();
            double cursorY = _lineYPositions.Count > vli ? _lineYPositions[vli] : 0;
            double viewTop = _scroll.Offset;
            double viewBottom = viewTop + ActualHeight;
            if (cursorY >= viewTop && cursorY <= viewBottom)
                anchorViewportY = cursorY - viewTop;
            else
                anchorViewportY = ActualHeight / 2;
            anchorDocY = viewTop + anchorViewportY;
        }

        double relativePos = _totalContentHeight > 0 ? anchorDocY / _totalContentHeight : 0;

        _measure.SetZoomFactor(factor);
        InvalidateLayout();
        ComputeLayout();

        double newAnchorDocY = relativePos * _totalContentHeight;
        _scroll.Offset = newAnchorDocY - anchorViewportY;
        _scroll.Clamp();
    }

    public void ZoomIn(double anchorViewportY = -1) => SetZoom(_measure.ZoomFactor + 0.1, anchorViewportY);
    public void ZoomOut(double anchorViewportY = -1) => SetZoom(_measure.ZoomFactor - 0.1, anchorViewportY);
    public void ZoomReset() => SetZoom(1.0);

    /// <summary>Fast scroll-only page up (used for global shortcuts, minimap-like speed)</summary>
    public void PageUpScroll()
    {
        ComputeLayout();
        double lineH = _visualLines.Count > 0 ? GetEffectiveLineHeight(_visualLines[0]) : 20;
        double pageAmount = Math.Max(lineH, ActualHeight - 3 * lineH);
        _scroll.Offset -= pageAmount;
        _scroll.Clamp();
        InvalidateVisual();
    }

    /// <summary>Fast scroll-only page down (used for global shortcuts, minimap-like speed)</summary>
    public void PageDownScroll()
    {
        ComputeLayout();
        double lineH = _visualLines.Count > 0 ? GetEffectiveLineHeight(_visualLines[0]) : 20;
        double pageAmount = Math.Max(lineH, ActualHeight - 3 * lineH);
        _scroll.Offset += pageAmount;
        _scroll.Clamp();
        InvalidateVisual();
    }

    /// <summary>Page up with cursor repositioning (used when canvas has focus)</summary>
    public void PageUp() => _navigationEngine.HandlePageUp(shift: false);
    /// <summary>Page down with cursor repositioning (used when canvas has focus)</summary>
    public void PageDown() => _navigationEngine.HandlePageDown(shift: false);

    public void ToggleShowWhitespace()
    {
        _showWhitespace = !_showWhitespace;
        InvalidateVisual();
    }

    public void SetShowWhitespace(bool show)
    {
        InvalidateRenderCache();
        if (_showWhitespace == show) return;
        _showWhitespace = show;
        InvalidateVisual();
    }

    private static bool IsTableRow(ParsedBlock parsed) =>
        parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow or BlockKind.TableSeparatorRow;

    internal bool IsVisual => _editMode == EditMode.Visual;

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
    internal ImageCache TestImageCache => _imageCache;
    internal void TestComputeLayout() => ComputeLayout();
    internal void TestInsert(string text)
    {
        ComputeLayout();
        foreach (char c in text)
            _doc.Insert(c);
        _doc.CollapseSelection();
        InvalidateLayout();
    }
    internal void TestTypeText(string text)
    {
        foreach (char c in text)
            InsertTextCore(c.ToString());
    }
    internal void TestNavigate(Key key, bool shift = false, bool ctrl = false)
    {
        _pendingStyleOff = null;
        ComputeLayout();
        bool textChanged = false;
        switch (key)
        {
            case Key.Left: _navigationEngine.HandleLeft(shift, ctrl); break;
            case Key.Right: _navigationEngine.HandleRight(shift, ctrl); break;
            case Key.Up: _navigationEngine.HandleUp(shift); break;
            case Key.Down: _navigationEngine.HandleDown(shift); break;
            case Key.PageUp: _navigationEngine.HandlePageUp(shift); break;
            case Key.PageDown: _navigationEngine.HandlePageDown(shift); break;
            case Key.Home: _navigationEngine.HandleHome(shift, ctrl); break;
            case Key.End: _navigationEngine.HandleEnd(shift, ctrl); break;
            case Key.Back: HandleBack(shift, out textChanged); break;
            case Key.Delete: HandleDelete(shift, out textChanged); break;
        }
        if (textChanged)
        {
            InvalidateLayout();
            if (IsVisual)
            {
                ComputeLayout();
                EnsureCursorOnVisibleBlock();
                if (_parsedBlocks != null && _doc.CursorBlock < _parsedBlocks.Count
                    && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                    ClampCursorToTableCell();
                else
                    SkipCursorToVisible(forward: true);
            }
        }
    }
    internal void TestSetSelection(int anchorBlock, int anchorOffset, int cursorBlock, int cursorOffset)
    {
        _doc.AnchorBlock = anchorBlock;
        _doc.AnchorOffset = anchorOffset;
        _doc.CursorBlock = cursorBlock;
        _doc.CursorOffset = cursorOffset;
    }
    internal int TestAnchorBlock => _doc.AnchorBlock;
    internal int TestAnchorOffset => _doc.AnchorOffset;
    internal double TestCursorX
    {
        get
        {
            ComputeLayout();
            int vli = CursorToVisualLineIndex();
            return _padding + CursorXInVisualLine(vli);
        }
    }
    internal record struct VisualBlockInfo(string RawText, string VisualText, BlockKind Kind, bool CreateVisualSeparation = false);

    internal string TestGetBlockText(int block) => _doc.GetBlockText(block);
    internal int TestBlockCount => _doc.BlockCount;

    internal VisualBlockInfo[] TestGetVisualBlockInfos()
    {
        var result = new List<VisualBlockInfo>();

        for (int i = 0; i < _doc.BlockCount; i++)
        {
            var rawText = _doc.GetBlockText(i);
            var displayText = rawText;

            // In visual mode, use the visual map to hide markdown syntax
            if (IsVisual && _visualMaps != null)
            {
                var visualMap = _visualMaps[i];
                displayText = visualMap.BuildDisplayString(rawText, 0, rawText.Length);
            }

            var kind = _parsedBlocks?[i].Kind ?? BlockKind.Paragraph;

            // In visual mode, include replacement prefix for formatting (but skip for continuations and lists)
            if (IsVisual && _visualMaps != null)
            {
                var visualMap = _visualMaps[i];
                bool isListItem = kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                               or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked;

                if (visualMap.ReplacementPrefix != null && !visualMap.IsContinuationIndent && !isListItem)
                    displayText = visualMap.ReplacementPrefix + displayText;
            }

            var createVisualSeparation = _parsedBlocks?[i].CreateVisualSeparation ?? false;

            // For merged paragraph blocks, convert soft breaks (newlines) to spaces for text extraction
            if (kind == BlockKind.Paragraph && displayText.Contains('\n'))
                displayText = displayText.Replace('\n', ' ');

            // For thematic breaks, render as normalized ---
            if (kind == BlockKind.ThematicBreak)
                displayText = " --- ";

            // Escape whitespace characters for clarity in test output
            displayText = displayText.Replace("\n", "[\\n]")
                                     .Replace("\t", "[\\t]");

            // Wrap block with type delimiter for unambiguous test output
            string blockType = GetBlockTypeLabel(kind);
            if (!string.IsNullOrWhiteSpace(displayText))
                displayText = $"[{blockType}: {displayText}]";
            else
                displayText = $"[{blockType}]";

            result.Add(new(rawText, displayText, kind, createVisualSeparation));
        }

        return result.ToArray();
    }

    private static string GetBlockTypeLabel(BlockKind kind) => kind switch
    {
        BlockKind.Paragraph => "PARA",
        BlockKind.Heading1 => "H1",
        BlockKind.Heading2 => "H2",
        BlockKind.Heading3 => "H3",
        BlockKind.Heading4 => "H4",
        BlockKind.Heading5 => "H5",
        BlockKind.Heading6 => "H6",
        BlockKind.UnorderedListItem => "LIST",
        BlockKind.OrderedListItem => "OLIST",
        BlockKind.TaskListItemUnchecked => "TASK",
        BlockKind.TaskListItemChecked => "TASK_DONE",
        BlockKind.Blockquote => "QUOTE",
        BlockKind.FencedCodeLine => "CODE",
        BlockKind.IndentedCodeLine => "CODE",
        BlockKind.ThematicBreak => "BREAK",
        BlockKind.TableHeaderRow => "TABLE_HEAD",
        BlockKind.TableSeparatorRow => "TABLE_SEP",
        BlockKind.TableDataRow => "TABLE_DATA",
        BlockKind.HtmlBlock => "HTML",
        _ => "UNKNOWN"
    };

    internal int TestGetVisualLineBlockIndex(int vi) => _visualLines[vi].BlockIndex;
    internal BlockKind TestGetVisualLineBlockKind(int vi) => _visualLines[vi].BlockKind;
    internal double TestGetLineYPosition(int vi) => _lineYPositions[vi];
    internal void TestUndo() { _doc.Undo(); InvalidateLayout(); }
    internal (int StartCol, int EndCol, int StartBlock, int EndBlock)?
        TestTryGetTableRectSelection()
    {
        ComputeLayout();
        var r = TryGetTableRectSelection();
        if (r == null) return null;
        return (r.Value.StartCol, r.Value.EndCol, r.Value.StartBlock, r.Value.EndBlock);
    }
    internal string? TestGetTableRectSelectedText()
    {
        ComputeLayout();
        var r = TryGetTableRectSelection();
        if (r == null) return null;
        return GetTableRectSelectedText(r.Value);
    }
    internal (string Text, string? Html) TestBuildClipboardPayload()
    {
        ComputeLayout();
        return BuildClipboardPayload();
    }
    internal bool TestTryPasteIntoTableCells(string pasteText)
    {
        ComputeLayout();
        _doc.BeginUndoGroup();
        bool handled = TryPasteIntoTableCells(pasteText);
        _doc.SealUndoGroup();
        InvalidateLayout();
        return handled;
    }
    internal bool TestClearTableRectCells()
    {
        ComputeLayout();
        var r = TryGetTableRectSelection();
        if (r == null) return false;
        _doc.BeginUndoGroup();
        ClearTableRectCells(r.Value);
        _doc.SealUndoGroup();
        return true;
    }
    internal bool TestHandleTableEnter()
    {
        ComputeLayout();
        return _tableInputHandler.HandleTableEnter(out _);
    }
    internal void TestHandleEnter(bool shift = false, bool ctrl = false)
    {
        _parsedBlocks = null;
        InvalidateLayout();
        ComputeLayout();
        _listFormattingHandler.HandleEnter(shift, ctrl);
    }

    private readonly DispatcherTimer _undoSealTimer;
    private LastActionKind _lastAction;

    public DocsCanvas()
    {
        ContentLayer.Transform = ContentScroll;
        _layers = new VisualCollection(this) { ContentLayer, OverlayLayer };

        _scroll = new ScrollController(InvalidateVisual, () => Math.Max(0, _totalContentHeight - ActualHeight));
        _linkHandler = new LinkHandler((INavigationServices)this, (IDocumentServices)this, (IParsedContentServices)this, (ILayoutDataServices)this, (IVisualModeServices)this, (IScrollServices)this);
        _linkPopup = new LinkPopupController(_doc, this);
        _tableInputHandler = new TableInputHandler((IDocumentServices)this, (IParsedContentServices)this, (ICanvasOperations)this);
        _tableRenderer = new TableRenderer((ITableServices)this, (IRenderingServices)this, (IDocumentServices)this, (IParsedContentServices)this, (ILayoutDataServices)this);
        _visualModeManager = new VisualModeManager((IVisualModeServices)this, (IDocumentServices)this, (IParsedContentServices)this, (ILoggingServices)this);
        _colorFormatter = new ColorFormattingManager((IDocumentServices)this, (IParsedContentServices)this, (ILayoutDataServices)this, (ICanvasOperations)this, (IScrollServices)this);
        _formattingKeysHandler = new FormattingKeysHandler(this, (ICanvasOperations)this, (IDocumentServices)this);
        _layoutEngine = new LayoutEngine(
            (ILayoutDataServices)this,
            (IDocumentServices)this,
            (IRenderingServices)this,
            (IParsedContentServices)this,
            (IVisualModeServices)this,
            (ILoggingServices)this,
            (ITableServices)this,
            (IImageServices)this,
            this);
        _renderingContext = new RenderingContext(
            (IRenderingServices)this,
            (ILayoutDataServices)this,
            (IParsedContentServices)this,
            (IDocumentServices)this,
            (IScrollServices)this,
            (ITableServices)this,
            (IVisualModeServices)this,
            (IImageServices)this,
            (ISearchServices)this,
            (ILoggingServices)this,
            (ICanvasOperations)this,
            (INavigationServices)this,
            this);
        _navigationEngine = new CursorNavigationEngine(
            (ILayoutDataServices)this,
            (IDocumentServices)this,
            (IVisualModeServices)this,
            (ITableServices)this,
            (IRenderingServices)this,
            (IParsedContentServices)this,
            (ILoggingServices)this,
            (IImageServices)this,
            (IScrollServices)this,
            (ICanvasOperations)this,
            (INavigationServices)this);
        _navigationEngine.VisualModeManager = _visualModeManager;
        _navigationKeysHandler = new NavigationKeysHandler(_navigationEngine, (IDocumentServices)this);
        _editingKeysHandler = new EditingKeysHandler((IDocumentServices)this, (IEditingServices)this);
        _listFormattingHandler = new ListFormattingHandler((IDocumentServices)this, (IParsedContentServices)this, new HardBreakStyleProvider(this));
        _contextMenuHandler = new ContextMenuHandler(this, (IDocumentServices)this, (ISpellCheckAccess)this);
        _indentationHandler = new IndentationHandler((IDocumentServices)this, (IParsedContentServices)this);
        _hoverImageHandler = new HoverImageHandler((IParsedContentServices)this, (IImageServices)this, (INavigationServices)this, (ILayoutDataServices)this, (IScrollServices)this, (IRenderingServices)this, (IDocumentServices)this, this);
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

        _doc.ContentChanged += () =>
        {
            InvalidateLayout();
            IsDirty = !_doc.IsClean;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            OnContentChangedForSpellCheck();
        };

        Loaded += (_, _) =>
        {
            _measure.EnsureMeasured(this);
            AttachDisplayInfo();
        };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _blinkTimer.Start();
            else _blinkTimer.Stop();
        };
        Unloaded += (_, _) => OnUnloaded();
    }

    private void OnUnloaded()
    {
        _blinkTimer.Stop();
        _undoSealTimer.Stop();
        CleanupSpellCheck();

        if (_displayInfo is not null)
        {
            _displayInfo.Changed -= OnDisplayChanged;
            _displayInfo = null;
        }
    }

    private WindowDisplayInfo? _displayInfo;

    /// <summary>
    /// Follows the display of the window this canvas is in, so scrolling can be paced to the
    /// panel rather than to whatever rate WPF happens to be composing at.
    /// </summary>
    /// <remarks>
    /// In Loaded rather than the constructor because the canvas has no window until then, and
    /// WindowDisplayInfo is per top-level window - a host with floating windows gets one for
    /// each, and each follows its own.
    /// </remarks>
    private void AttachDisplayInfo()
    {
        if (_displayInfo is not null)
            return;

        _displayInfo = WindowDisplayInfo.For(this);
        if (_displayInfo is null)
            return;

        _displayInfo.Changed += OnDisplayChanged;
        OnDisplayChanged(_displayInfo);
    }

    private void OnDisplayChanged(WindowDisplayInfo info) =>
        _scroll.SetDisplay(info.Devices, info.RefreshRate);

    private void ResetUndoSealTimer()
    {
        _undoSealTimer.Stop();
        _undoSealTimer.Start();
        IsDirty = !_doc.IsClean;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SealAndStopTimer()
    {
        _undoSealTimer.Stop();
        _doc.SealUndoGroup();
        _lastAction = LastActionKind.None;
    }

    public void PerformUndo()
    {
        if (IsReadOnly) return;
        _undoSealTimer.Stop();
        _doc.Undo();
        _lastAction = LastActionKind.None;
        InvalidateLayout();
    }

    public void PerformRedo()
    {
        if (IsReadOnly) return;
        _undoSealTimer.Stop();
        _doc.Redo();
        _lastAction = LastActionKind.None;
        InvalidateLayout();
    }

    /// <summary>
    /// Builds the clipboard payload for the current selection: the markdown text, plus a CF_HTML
    /// fragment when the content warrants one. Table selections produce a real &lt;table&gt; so
    /// Excel and Word paste them into separate cells.
    /// </summary>
    internal (string Text, string? Html) BuildClipboardPayload()
    {
        var rect = TryGetTableRectSelection();
        string text = rect != null ? GetTableRectSelectedText(rect.Value) : _doc.GetSelectedText();
        string? html = BuildTableClipboardHtml(rect)
                       ?? HtmlToMarkdownConverter.ConvertToHtmlClipboard(text);
        return (text, html);
    }

    private string? BuildTableClipboardHtml(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)? rect)
    {
        if (_parsedBlocks == null) return null;

        if (rect != null)
            return TableClipboardHtml.TryBuild(_parsedBlocks, _doc.GetBlockText,
                rect.Value.StartBlock, rect.Value.EndBlock, rect.Value.StartCol, rect.Value.EndCol);

        var (startBlock, _, endBlock, endOffset) = _doc.GetOrderedSelection();
        // A drag that ends at the start of the following line shouldn't pull that line in.
        if (endOffset == 0 && endBlock > startBlock) endBlock--;
        // Single-row selections are ordinary text copies, not grid copies.
        if (endBlock <= startBlock) return null;

        return TableClipboardHtml.TryBuild(_parsedBlocks, _doc.GetBlockText, startBlock, endBlock);
    }

    private void SetClipboardFromSelection()
    {
        var (text, html) = BuildClipboardPayload();
        if (html != null)
            ClipboardHelper.SetTextAndHtml(text, html, Logger);
        else
            ClipboardHelper.SetText(text, Logger);
    }

    public void PerformCopy()
    {
        if (!_doc.HasSelection) return;
        SetClipboardFromSelection();
    }

    public void PerformCut()
    {
        if (IsReadOnly) return;
        if (!_doc.HasSelection) return;
        SealAndStopTimer();
        var rect = TryGetTableRectSelection();
        SetClipboardFromSelection();
        _doc.BeginUndoGroup();
        if (rect != null)
            ClearTableRectCells(rect.Value);
        else
            _doc.DeleteSelection();
        _doc.SealUndoGroup();
        InvalidateLayout();
    }

    public void PerformPaste()
    {
        if (IsReadOnly) return;
        SealAndStopTimer();
        string? pasteText = null;
        bool inCodeBlock = _parsedBlocks != null
            && _parsedBlocks[_doc.CursorBlock].Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;
        if (!inCodeBlock)
        {
            string? html = ClipboardHelper.GetHtml(Logger);
            if (html != null)
            {
                // Use new semantic block model parser with color preservation
                var settings = new MarkdownOutputSettings { PreserveColors = true };
                pasteText = HtmlBlockModelParser.ConvertHtmlToMarkdown(html, settings);
            }
        }
        pasteText ??= ClipboardHelper.GetText(Logger);
        if (!string.IsNullOrEmpty(pasteText))
        {
            _doc.BeginUndoGroup();
            var rect = TryGetTableRectSelection();
            if (rect != null)
            {
                ClearTableRectCells(rect.Value);
                MoveCursorToRectStart(rect.Value);
            }
            else if (_doc.HasSelection)
            {
                _doc.DeleteSelection();
            }
            if (!TryPasteIntoTableCells(pasteText))
                _doc.Paste(pasteText);
            _doc.SealUndoGroup();
            InvalidateLayout();
        }
    }

    public void PerformSelectAll()
    {
        SealAndStopTimer();
        _doc.SelectAll();
        InvalidateVisual();
    }

    public void PerformFind() => OpenFind(showReplace: false);

    public void PerformFindReplace() => OpenFind(showReplace: !IsReadOnly);

    internal double GetEffectiveLineHeight(VisualLine vl)
    {
        double h = _measure.GetLineHeight(vl.BlockKind);
        return vl.OverrideHeight > h ? vl.OverrideHeight : h;
    }

    private void ResetBlink()
    {
        _cursorVisible = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    /// <summary>
    /// Bumped whenever anything that could change how a line is drawn changes. The per-line
    /// FormattedText cache in RenderingContext keys off this.
    /// </summary>
    /// <remarks>
    /// Deliberately over-eager. Dropping the cache costs one frame's rebuild on an action a
    /// reader takes occasionally - a theme switch, a zoom, toggling whitespace - whereas
    /// missing an invalidation leaves stale text on screen. Anything new that alters the
    /// text, its styling, its fonts or the palette should call
    /// <see cref="InvalidateRenderCache"/>, whether or not it also invalidates layout.
    /// </remarks>
    internal int RenderVersion { get; private set; }

    internal void InvalidateRenderCache() => RenderVersion++;

    /// <summary>
    /// Rebuilds the cached line visuals and repaints, without touching layout.
    /// </summary>
    /// <remarks>
    /// For a change that alters what a line looks like but not where anything sits - an image
    /// arriving at a size layout already reserved. A bare InvalidateVisual is not enough now
    /// that lines are cached: it would recomposite the same stale pictures.
    /// </remarks>
    internal void RedrawLinesWithImage(string url)
    {
        _renderingContext.DropLineVisualsForImage(url);
        InvalidateVisual();
    }

    // --- Hosted visual layers (see design/Scroll Pre-Buffering.md) ---

    /// <summary>
    /// Line content, rendered once per line into cached child visuals and moved as a whole by
    /// <see cref="ContentScroll"/>.
    /// </summary>
    /// <remarks>
    /// A child visual draws above the element's own OnRender content, which fixes the layer
    /// order for free: backgrounds and selection are painted by OnRender underneath, the text
    /// sits in here, and anything that has to appear over the text goes in
    /// <see cref="OverlayLayer"/>, added after this one.
    ///
    /// Scrolling moves one transform rather than redrawing every line, which is the whole
    /// point: a table row costs a composite instead of ten DrawText calls, and the offset can
    /// later be fractional without each line rounding independently.
    /// </remarks>
    /// <summary>
    /// Enables F9, which switches between drawing lines into cached visuals and drawing them
    /// straight into OnRender. Off by default; a diagnostic, not a feature.
    /// </summary>
    /// <remarks>
    /// Kept because it is the only way to compare the two paths honestly. Frame-rate figures
    /// taken from inside the process count OnRender calls, not frames the panel showed, and
    /// DwmGetCompositionTimingInfo will not report the difference. Switching paths back to
    /// back on the same document, and measuring from outside with FrameView, is what turned
    /// "I cannot tell a difference" into 140 displayed frames a second against 119.
    ///
    /// Set it from a host app while investigating; leave it alone otherwise.
    /// </remarks>
    public static bool EnableRenderPathToggle { get; set; }

    /// <summary>Which of the two drawing paths is in use. Always true unless F9 is enabled.</summary>
    internal bool CachedLineVisuals { get; private set; } = true;

    internal void ToggleCachedLineVisuals()
    {
        if (!EnableRenderPathToggle) return;
        CachedLineVisuals = !CachedLineVisuals;
        InvalidateRenderCache();   // drop the visuals either way, so neither path sees the other's
        InvalidateVisual();
    }

    /// <summary>
    /// Fills each cached line visual with the theme background before drawing its text, which
    /// is what ClearType needs to run. Phase 1 of design/Opaque Line Visuals.md.
    /// </summary>
    /// <remarks>
    /// A BitmapCache rasterises into a Pbgra32 surface, and ClearType cannot filter against a
    /// background it does not know, so every line cached since 337e009 has been greyscale
    /// antialiased. An opaque fill gives it one back.
    ///
    /// Deliberately incomplete, and off by default. Anything OnRender paints beneath the
    /// content layer - code and colour block backgrounds, table tints, selection, search
    /// highlights - is covered by the fill and disappears while this is on. Moving those in is
    /// phases 2 to 4; this phase only answers whether the text sharpens at all, which is the
    /// gate on the rest being worth building.
    /// </remarks>
    internal bool OpaqueLineVisuals { get; private set; }

    internal void ToggleOpaqueLineVisuals()
    {
        if (!EnableRenderPathToggle) return;
        OpaqueLineVisuals = !OpaqueLineVisuals;
        InvalidateRenderCache();   // the fill is baked into the bitmap, so every line rebuilds
        InvalidateVisual();
    }

    internal readonly ContainerVisual ContentLayer = new();

    /// <summary>Moves <see cref="ContentLayer"/> by the scroll offset.</summary>
    internal readonly TranslateTransform ContentScroll = new();

    /// <summary>Caret, spelling squiggles and page breaks: drawn over the text, never cached.</summary>
    internal readonly DrawingVisual OverlayLayer = new();

    // Built in the constructor, never lazily: WPF queries VisualChildrenCount during its
    // render pass, and creating the collection there would mutate the visual tree mid-render,
    // which throws "Cannot call this API during the OnRender callback".
    private VisualCollection? _layers;

    protected override int VisualChildrenCount => _layers?.Count ?? 0;

    protected override Visual GetVisualChild(int index) =>
        _layers is { } l ? l[index] : throw new ArgumentOutOfRangeException(nameof(index));


    internal void InvalidateLayout()
    {
        InvalidateRenderCache();
        _layoutDirty = true;
        _parsedBlocks = null;
        _visualBlockStructure = null;
        _visualMaps = null;
        _blockToGroup = null;
        InvalidateArrange();
        InvalidateSearchOnContentChange();
        InvalidateVisual();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        FormattingBar?.DeactivateKeyboardNavigation();
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

    internal (double Width, double Height) GetImageSize(InlineImage img, double maxWidth)
    {
        var cached = _imageCache.Get(img.Url, DocumentBasePath, maxWidth);
        if (cached != null)
            return (cached.Value.Width, cached.Value.Height);

        // Reserve the real size straight away where the header can tell us. Then the decode
        // that follows changes pixels but not layout, so it only needs a repaint - no reparse,
        // no dropped render cache, and nothing below the image moves when it appears.
        var known = _imageCache.GetPixelSize(img.Url, DocumentBasePath, maxWidth);
        _imageCache.RequestLoad(img.Url, DocumentBasePath,
            known != null ? () => RedrawLinesWithImage(img.Url) : InvalidateLayout);
        return known ?? (20, 20);
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

    internal void ComputeLayout() => _layoutEngine.ComputeLayout();

    internal void ComputeLayoutCore(double maxWidth) => _layoutEngine.ComputeLayoutCore(maxWidth);


    internal const double _tableCellPadding = 8;

    // --- Cursor ↔ visual line mapping ---

    internal int CursorToVisualLineIndex() => _navigationEngine.CursorToVisualLineIndex();

    private double GetTextStartXForVisualLine(VisualLine vl) => _layoutEngine.GetTextStartXForVisualLine(vl);

    double ILayoutDataServices.GetTextStartXForVisualLine(VisualLine vl) => GetTextStartXForVisualLine(vl);

    internal BlockVisualSpacing? GetVisualLineSpacing(VisualLine vl) => _navigationEngine.GetVisualLineSpacing(vl);

    internal double CursorXInVisualLine(int vlIndex) => _navigationEngine.CursorXInVisualLine(vlIndex);

    internal double MeasureJoinedRange(ParagraphGroup group, int start, int length)
        => _navigationEngine.MeasureJoinedRange(group, start, length);


    private int HitTestInVisualLineProper(int vlIndex, double clickX)
        => _navigationEngine.HitTestInVisualLineProper(vlIndex, clickX);

    private int HitTestInVisualLine(int vlIndex, double x)
        => _navigationEngine.HitTestInVisualLine(vlIndex, x);

    internal int HitTestInVisualLineInternal(int vlIndex, double x)
        => _navigationEngine.HitTestInVisualLine(vlIndex, x);

    private int HitTestInJoinedLine(VisualLine vl, double x)
        => _navigationEngine.HitTestInJoinedLine(vl, x);

    internal int HitTestVisualLine(double y) => _navigationEngine.HitTestVisualLine(y);

    internal void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
        => _navigationEngine.HitTestToPosition(pos, out blockIndex, out charOffset);

    // --- Scroll ---

    internal void EnsureCursorVisible()
    {
        _scroll.StopWheelCoast();
        _scroll.CancelSmooth();
        ComputeLayout();
        if (_visualLines.Count == 0) return;
        int vli = CursorToVisualLineIndex();
        double cursorY = _lineYPositions[vli];
        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
        double cursorBottom = cursorY + lineH;
        if (cursorY < _scroll.Offset + _padding)
            _scroll.Offset = Math.Max(0, cursorY - _padding);
        else if (cursorBottom > _scroll.Offset + ActualHeight - _padding)
            _scroll.Offset = cursorBottom - ActualHeight + _padding;
        _scroll.Clamp();
    }

    // --- Rendering ---

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        _measure.EnsureMeasured(this);
        if (ActualHeight > 0 && ActualWidth > 0)
        {
            ComputeLayout();
            // Before the render pass, not during it: building the cached line visuals adds
            // children, and the tree cannot be mutated while it is being rendered.
            _renderingContext.UpdateContentLayer(finalSize.Height);
        }
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Straight through when diagnostics are off. The lambda below captures dc, so handing
        // it to Time would allocate a closure and a delegate on every render - hundreds a
        // second during a scroll - for Time to discard.
        if (!ScrollDiag.Enabled)
        {
            OnRenderCore(dc);
            return;
        }

        ScrollDiag.Time("canvas-onrender", () => OnRenderCore(dc));
    }

    private void OnRenderCore(DrawingContext dc)
    {
        _renderingContext.OnRender(dc);
    }

    private void DrawJoinedLine(DrawingContext dc, VisualLine vl,
        double lineY, double effectiveScroll)
    {
        var group = vl.Group!;

        if (HasImagesOnLine(vl, group.JoinedMap))
        {
            DrawVisualLineWithImages(dc, vl, group.JoinedText, group.JoinedParsed,
                group.JoinedMap, lineY, effectiveScroll,
                _measure.GetBlockFontSize(BlockKind.Paragraph), TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph));
            return;
        }

        // Build base display string (with "¶" only, no spaces yet)
        var baseDisplay = group.JoinedMap.BuildDisplayString(group.JoinedText, vl.StartOffset, vl.Length);

        // Add visual spaces after pilcrows
        var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
        var sb = new System.Text.StringBuilder();
        int visPos = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
        {
            if (group.JoinedMap.IsHidden(i)) continue;

            // Add the visible character from base display
            if (visPos < baseDisplay.Length)
                sb.Append(baseDisplay[visPos]);

            // Add visual space after pilcrow
            if (softBreaks.Contains(i) && i < group.JoinedText.Length && group.JoinedText[i] == '¶')
                sb.Append(' ');

            visPos++;
        }

        string displayText = sb.ToString();
        if (displayText.Length == 0) return;

        double fontSize = _measure.GetBlockFontSize(BlockKind.Paragraph);
        var baseTypeface = TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph);

        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, baseTypeface, fontSize,
            _palette.Foreground, _measure.DpiScale);
        ApplyInlineStylesVisual(ft, vl, group.JoinedParsed, group.JoinedMap);

        // Color soft breaks (pilcrow + visual space)
        visPos = 0;
        int displayPos = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
        {
            if (group.JoinedMap.IsHidden(i)) continue;

            if (softBreaks.Contains(i) && displayPos < displayText.Length)
                ft.SetForegroundBrush(_palette.Syntax, displayPos, 2);  // color pilcrow + visual space

            // Advance display position (by 2 if soft break with visual space, else by 1)
            displayPos += (softBreaks.Contains(i)) ? 2 : 1;
            visPos++;
        }

        dc.DrawText(ft, new Point(_padding, lineY - effectiveScroll));
    }

    private string BuildJoinedDisplayString(ParagraphGroup group, int start, int length)
    {
        // Note: Soft break visual spaces are added in DrawJoinedLine, not here
        return group.JoinedMap.BuildDisplayString(group.JoinedText, start, length);
    }

    private void ApplyInlineStyles(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
    {
        if (parsed.SyntaxTokens != null)
        {
            ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens);
            return;
        }

        foreach (var run in parsed.Runs)
        {
            int runEnd = run.Start + run.Length;
            int vlEnd = vl.StartOffset + vl.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

            int localStart = Math.Max(0, run.Start - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, runEnd - vl.StartOffset);
            int count = localEnd - localStart;
            if (count <= 0) continue;

            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

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
                    ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, localStart, count);
                    break;
                case InlineStyle.Strikethrough:
                    ft.SetTextDecorations(TextDecorations.Strikethrough, localStart, count);
                    break;
                case InlineStyle.Link:
                    ft.SetForegroundBrush(_checkboxCheckedBrush, localStart, count);
                    ft.SetTextDecorations(TextDecorations.Underline, localStart, count);
                    break;
            }
        }

        ApplyColorSpans(ft, vl, parsed, blockText);
        ApplySyntaxDimming(ft, vl, parsed, blockText);
    }

    private void ApplySyntaxTokens(FormattedText ft, VisualLine vl, IReadOnlyList<SyntaxToken> tokens, BlockVisualMap? map = null)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var token in tokens)
        {
            int tokenEnd = token.Start + token.Length;
            if (tokenEnd <= vl.StartOffset || token.Start >= vlEnd) continue;

            int localStart;
            int count;
            if (map != null)
            {
                int rawStart = Math.Max(token.Start, vl.StartOffset);
                int rawEnd = Math.Min(tokenEnd, vlEnd);
                int vlVisualOffset = map.RawToVisual(vl.StartOffset);
                int visStart = map.RawToVisual(rawStart) - vlVisualOffset;
                int visEnd = map.RawToVisual(rawEnd) - vlVisualOffset;
                localStart = visStart;
                count = visEnd - visStart;
            }
            else
            {
                localStart = Math.Max(0, token.Start - vl.StartOffset);
                int localEnd = Math.Min(vl.Length, tokenEnd - vl.StartOffset);
                count = localEnd - localStart;
            }
            if (count <= 0) continue;

            var brush = GetSyntaxBrush(token.ForegroundArgb);
            ft.SetForegroundBrush(brush, localStart, count);
        }
    }

    private Brush GetSyntaxBrush(int argb)
    {
        if (_syntaxBrushCache.TryGetValue(argb, out var cached))
            return cached;

        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        _syntaxBrushCache[argb] = brush;
        return brush;
    }

    private void ApplyColorSpans(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
    {
        if (parsed.ColorSpans == null && parsed.BlockColor == null) return;
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;

        int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
            ? MarkdownParser.GetContentEnd(blockText) - 1
            : int.MaxValue;

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            int len = Math.Min(vl.Length, hardBreakClip - vl.StartOffset);
            if (len > 0)
                ft.SetForegroundBrush(GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, len);
        }

        if (parsed.ColorSpans != null)
        {
            foreach (var cs in parsed.ColorSpans)
            {
                int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                int vlEnd = vl.StartOffset + vl.Length;
                if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                int localStart = Math.Max(0, cs.Start - vl.StartOffset);
                int localEnd = Math.Min(vl.Length, csEnd - vl.StartOffset);
                int count = localEnd - localStart;
                if (count <= 0) continue;

                if (cs.Foreground is { } fg)
                {
                    ft.SetForegroundBrush(GetCachedBrush(fg.R, fg.G, fg.B), localStart, count);
                }
            }
        }
    }

    internal SolidColorBrush GetCachedBrush(byte r, byte g, byte b)
    {
        var color = Color.FromRgb(r, g, b);
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    private SolidColorBrush GetCachedBrush(byte a, byte r, byte g, byte b)
    {
        var color = Color.FromArgb(a, r, g, b);
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    private void DrawImagePlaceholder(DrawingContext dc, double x, double y, double w, double h, string? altText)
    {
        dc.DrawRectangle(_imagePlaceholderBrush, null, new Rect(x, y, w, h));
        if (!string.IsNullOrEmpty(altText))
        {
            var altFt = new FormattedText(altText,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                TextMeasurer.NormalTypeface, Math.Round(11 * _measure.ZoomFactor), _palette.Syntax, _measure.DpiScale);
            altFt.MaxTextWidth = Math.Max(1, w);
            altFt.MaxTextHeight = Math.Max(1, h);
            dc.DrawText(altFt, new Point(x + 2, y + 2));
        }
    }

    private void ApplySyntaxDimming(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
    {
        int vlEnd = vl.StartOffset + vl.Length;

        int ls = parsed.LeadingSpaces;

        if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
        {
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            if (stripped.Length > 0 && stripped[0] == '#')
            {
                int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
                int totalPrefix = ls + hashCount + 1;
                int localStart = Math.Max(0, 0 - vl.StartOffset);
                int localEnd = Math.Min(vl.Length, totalPrefix - vl.StartOffset);
                if (localEnd > localStart)
                    ft.SetForegroundBrush(_palette.Syntax, localStart, localEnd - localStart);
            }
        }

        if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked && vl.StartOffset == 0 && vl.Length >= ls + 6)
        {
            ft.SetForegroundBrush(_palette.Syntax, 0, ls + 6);
        }
        else if (parsed.Kind == BlockKind.UnorderedListItem && vl.StartOffset == 0 && vl.Length >= ls + 2)
        {
            ft.SetForegroundBrush(_palette.Syntax, 0, ls + 2);
        }
        else if (parsed.Kind == BlockKind.OrderedListItem && vl.StartOffset == 0)
        {
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
            if (prefixLen > 0 && vl.Length >= ls + prefixLen)
                ft.SetForegroundBrush(_palette.Syntax, 0, ls + prefixLen);
        }

        if (parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
        {
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            if (stripped.Length > 0 && stripped[0] == '>')
            {
                int dimLength = ls + 1;
                if (stripped.Length > 1 && stripped[1] == ' ')
                    dimLength += 1;
                if (vl.Length >= dimLength)
                    ft.SetForegroundBrush(_palette.Syntax, 0, dimLength);
            }
        }

        if (parsed.Kind == BlockKind.LinkDefinition)
            ft.SetForegroundBrush(_palette.Syntax, 0, vl.Length);

        if (parsed.Kind is BlockKind.ThemeDefinition or BlockKind.ColorDivOpen or BlockKind.ColorDivClose)
            ft.SetForegroundBrush(_palette.Syntax, 0, vl.Length);

        if (parsed.Kind is BlockKind.TableSeparatorRow or BlockKind.ThematicBreak or BlockKind.SetextUnderline)
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

        if (parsed.Links != null)
        {
            foreach (var link in parsed.Links)
            {
                if (link.Text == link.Url) continue;
                int linkEnd = link.Start + link.Length;
                if (linkEnd <= vl.StartOffset || link.Start >= vlEnd) continue;

                DimRange(ft, vl, link.Start, 1);
                int closeBracket = link.Start + 1 + link.Text.Length;
                DimRange(ft, vl, closeBracket, linkEnd - closeBracket);
            }
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style is InlineStyle.Normal or InlineStyle.Image or InlineStyle.Link) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

            if (run.Style is InlineStyle.Code or InlineStyle.Strikethrough)
            {
                int markerLen = run.Style == InlineStyle.Code
                    ? CountBackticks(blockText, run.Start)
                    : MarkdownParser.GetMarkerLength(run.Style);
                if (markerLen == 0) continue;

                DimRange(ft, vl, run.Start, markerLen);
                DimRange(ft, vl, runEnd - markerLen, markerLen);
            }
        }

        if (parsed.EmphasisMarkers != null)
        {
            foreach (var marker in parsed.EmphasisMarkers)
                DimRange(ft, vl, marker.Start, marker.Length);
        }

        if (MarkdownParser.IsTrailingHardBreak(parsed, blockText))
            DimRange(ft, vl, MarkdownParser.GetContentEnd(blockText) - 1, 1);

        if (parsed.Kind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine)
        {
            var tagRanges = MarkdownParser.FindInlineColorTagRanges(blockText);
            if (tagRanges != null)
            {
                foreach (var tag in tagRanges)
                    DimRange(ft, vl, tag.Start, tag.Length);
            }
        }

        if (parsed.Kind == BlockKind.HtmlBlock)
        {
            var htmlCommentRanges = MarkdownParser.FindHtmlCommentRanges(blockText);
            if (htmlCommentRanges != null)
            {
                foreach (var commentRange in htmlCommentRanges)
                    DimRange(ft, vl, commentRange.Start, commentRange.Length);
            }
        }
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }

    private void DrawTrailingSpaceDots(DrawingContext dc, VisualLine vl,
        string blockText, ParsedBlock parsed, double textX, double screenY)
    {
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
        if (vl.StartOffset + vl.Length < blockText.Length) return;

        int trailStart = blockText.Length;
        while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
        int trailCount = blockText.Length - trailStart;
        if (trailCount == 0) return;

        var measureKind = !IsVisual && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow
            ? BlockKind.Paragraph : parsed.Kind;

        double x = textX;
        int runIdx = 0;
        for (int i = vl.StartOffset; i < trailStart; i++)
        {
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            x += _measure.MeasureCharWidth(blockText[i], measureKind, style);
        }

        double spaceW = _measure.MeasureCharWidth(' ', measureKind, InlineStyle.Normal);
        double dotSize = Math.Max(2, spaceW * 0.25);
        double lineH = _measure.GetLineHeight(parsed.Kind);
        double cy = screenY + lineH / 2;

        for (int i = 0; i < trailCount; i++)
        {
            double cx = x + spaceW * (i + 0.5);
            dc.DrawEllipse(_palette.Syntax, null, new Point(cx, cy), dotSize / 2, dotSize / 2);
        }
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
        double contentWidth = ActualWidth;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.BlockKind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine) continue;

            double lineH = _measure.GetLineHeight(vl.BlockKind);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            dc.DrawRectangle(_palette.CodeBackground, null,
                new Rect(0, lineY - effectiveScroll, contentWidth, lineH));
        }
    }

    private void DrawColorBlockBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        if (_parsedBlocks == null) return;
        double contentWidth = ActualWidth;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.BlockIndex >= _parsedBlocks.Count) continue;
            var parsed = _parsedBlocks[vl.BlockIndex];
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;
            if (parsed.BlockColor?.Background is not { } bg) continue;

            double lineH = GetEffectiveLineHeight(vl);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            dc.DrawRectangle(GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                new Rect(0, lineY - effectiveScroll, contentWidth, lineH));
        }
    }

    private void DrawInlineColorBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        if (_parsedBlocks == null) return;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            double lineH = GetEffectiveLineHeight(vl);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            string blockText;
            ParsedBlock parsed;
            BlockVisualMap? map;
            IReadOnlyList<ColorSpan>? colorSpans;

            if (vl.Group != null)
            {
                var group = vl.Group;
                blockText = group.JoinedText;
                parsed = group.JoinedParsed;
                map = group.JoinedMap;
                colorSpans = map.ColorSpans;
            }
            else
            {
                if (vl.BlockIndex >= _parsedBlocks.Count) continue;
                parsed = _parsedBlocks[vl.BlockIndex];
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;
                blockText = _doc.GetBlockText(vl.BlockIndex);
                map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;
                colorSpans = IsVisual ? map?.ColorSpans : parsed.ColorSpans;
            }

            if (colorSpans == null) continue;
            if (IsVisual && parsed.Table != null && parsed.TableRow != null) continue;

            int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
                ? MarkdownParser.GetContentEnd(blockText) - 1
                : int.MaxValue;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var cs in colorSpans)
            {
                if (cs.Background == null) continue;
                int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                int rangeStart = Math.Max(cs.Start, vl.StartOffset);
                int rangeEnd = Math.Min(csEnd, vlEnd);

                double x1 = MeasureRangeWidth(blockText, vl.StartOffset, rangeStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                double x2 = MeasureRangeWidth(blockText, vl.StartOffset, rangeEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                    x1 += prefixW;
                    x2 += prefixW;
                }

                double w = x2 - x1;
                if (w <= 0) continue;

                var bg = cs.Background.Value;
                dc.DrawRectangle(GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                    new Rect(_padding + x1, lineY - effectiveScroll, w, lineH));
            }
        }
    }

    private void DrawSelection(DrawingContext dc, double effectiveScroll)
    {
        var rectSel = TryGetTableRectSelection();
        if (rectSel != null)
        {
            var r = rectSel.Value;
            DrawTableRectSelection(dc, effectiveScroll, r.StartCol, r.EndCol, r.StartBlock, r.EndBlock, r.Table);
            return;
        }

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

            if (vl.Group != null)
            {
                DrawJoinedSelection(dc, vl, lineY, lineH, effectiveScroll, sb, so, eb, eo);
                continue;
            }

            int vlEnd = vl.StartOffset + vl.Length;

            bool startsBeforeSelEnd = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
            bool endsAfterSelStart = Document.ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
            if (!startsBeforeSelEnd || !endsAfterSelStart) continue;

            int hlStart = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                ? vl.StartOffset : so;
            int hlEnd = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                ? vlEnd : eo;

            var parsed = _parsedBlocks![vl.BlockIndex];
            string blockText = _doc.GetBlockText(vl.BlockIndex);
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
                    double prefixW = _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
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

    private void DrawJoinedSelection(DrawingContext dc, VisualLine vl,
        double lineY, double lineH, double effectiveScroll,
        int sb, int so, int eb, int eo)
    {
        var group = vl.Group!;
        int selStartJoined = group.SourceToJoined(sb, so);
        int selEndJoined = group.SourceToJoined(eb, eo);
        if (selStartJoined < 0)
            selStartJoined = sb < group.FirstBlock ? 0 : group.JoinedText.Length;
        if (selEndJoined < 0)
            selEndJoined = eb > group.LastBlock ? group.JoinedText.Length : 0;

        int vlStart = vl.StartOffset;
        int vlEnd = vl.StartOffset + vl.Length;

        if (vlEnd <= selStartJoined || vlStart >= selEndJoined) return;

        int hlStart = Math.Max(vlStart, selStartJoined);
        int hlEnd = Math.Min(vlEnd, selEndJoined);

        double x1 = MeasureJoinedRange(group, vlStart, hlStart - vlStart);
        double x2 = MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

        bool selectionContinues = vlEnd < selEndJoined;
        if (selectionContinues && x2 - x1 < 4)
            x2 = x1 + 4;
        else if (selectionContinues)
            x2 += 4;

        double selW = Math.Max(0, x2 - x1);
        if (selW > 0)
            dc.DrawRectangle(_palette.Selection, null,
                new Rect(_padding + x1, lineY - effectiveScroll, selW, lineH));
    }

    internal double MeasureRangeWidth(string text, int start, int length,
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
            var style = TextMeasurer.GetStyleAtOffset(runs, i, ref runIdx);
            total += _measure.MeasureCharWidth(text[i], blockKind, style);
        }
        return total;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // A height-only change does not reflow anything, so the offset still points at the
        // same content and only needs a fresh layout.
        if (sizeInfo.WidthChanged)
            ReflowPreservingViewport();
        else
            InvalidateLayout();
    }

    /// <summary>
    /// Recomputes layout for a new width while keeping the viewport on the content the reader
    /// is looking at. A width change rewraps every line, so total content height changes and a
    /// fixed pixel scroll offset would slide the document under the reader. Anchors the same
    /// way <see cref="SetEditMode"/> and <see cref="SetZoom"/> do.
    /// </summary>
    internal void ReflowPreservingViewport()
    {
        bool canAnchor = _visualLines.Count > 0 && _lineYPositions.Count > 0;
        var anchor = canAnchor ? ComputeScrollAnchor() : default;

        InvalidateLayout();

        if (!canAnchor) return;

        ComputeLayout();
        ApplyScrollAnchor(anchor);
    }
}
