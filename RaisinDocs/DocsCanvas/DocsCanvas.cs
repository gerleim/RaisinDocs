using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

public partial class DocsCanvas : FrameworkElement, IMinimapDataProvider
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
    private static readonly Brush _imagePlaceholderBrush;
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
    private readonly ScrollController _scroll;

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
    private InlineImage? _hoveredImage;
    private Point _hoverPosition;
    private string? _hoveredLinkUrl;
    private readonly ToolTip _linkToolTip = new()
    {
        Placement = PlacementMode.Relative,
    };

    private readonly LinkPopupController _linkPopup;
    private readonly TableRenderer _tableRenderer;
    private readonly VisualModeManager _visualModeManager;

    public enum SoftBreakMode { Relaxed, Strict }
    public enum HardBreakStyle { Backslash, TrailingSpaces }
    private SoftBreakMode _softBreak = SoftBreakMode.Relaxed;
    private HardBreakStyle _hardBreak = HardBreakStyle.Backslash;
    private bool _showWhitespace = true;
    public SoftBreakMode CurrentSoftBreak => _softBreak;
    public HardBreakStyle CurrentHardBreak => _hardBreak;
    public bool ShowWhitespace => _showWhitespace;

    public IDocsLogger? Logger { get; set; }
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

    public void PageUp() => HandlePageUp(shift: false);
    public void PageDown() => HandlePageDown(shift: false);

    public void ToggleShowWhitespace()
    {
        _showWhitespace = !_showWhitespace;
        InvalidateVisual();
    }

    public void SetShowWhitespace(bool show)
    {
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
            case Key.Left: HandleLeft(shift, ctrl); break;
            case Key.Right: HandleRight(shift, ctrl); break;
            case Key.Up: HandleUp(shift); break;
            case Key.Down: HandleDown(shift); break;
            case Key.PageUp: HandlePageUp(shift); break;
            case Key.PageDown: HandlePageDown(shift); break;
            case Key.Home: HandleHome(shift, ctrl); break;
            case Key.End: HandleEnd(shift, ctrl); break;
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
        return HandleTableEnter(out _);
    }
    internal void TestHandleEnter(bool shift = false, bool ctrl = false)
    {
        _parsedBlocks = null;
        InvalidateLayout();
        ComputeLayout();
        HandleEnter(shift, ctrl);
    }

    private readonly DispatcherTimer _undoSealTimer;
    private enum LastActionKind { None, Typing, Deleting }
    private LastActionKind _lastAction;

    public DocsCanvas()
    {
        _scroll = new ScrollController(InvalidateVisual, () => Math.Max(0, _totalContentHeight - ActualHeight));
        _linkPopup = new LinkPopupController(_doc, this);
        _tableRenderer = new TableRenderer(this);
        _visualModeManager = new VisualModeManager(this);
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
    }

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

    public void PerformCopy()
    {
        if (!_doc.HasSelection) return;
        var rect = TryGetTableRectSelection();
        string text = rect != null ? GetTableRectSelectedText(rect.Value) : _doc.GetSelectedText();
        var cfHtml = HtmlColorParser.ConvertToHtmlClipboard(text);
        if (cfHtml != null)
            ClipboardHelper.SetTextAndHtml(text, cfHtml, Logger);
        else
            ClipboardHelper.SetText(text, Logger);
    }

    public void PerformCut()
    {
        if (IsReadOnly) return;
        if (!_doc.HasSelection) return;
        SealAndStopTimer();
        var rect = TryGetTableRectSelection();
        string text = rect != null ? GetTableRectSelectedText(rect.Value) : _doc.GetSelectedText();
        ClipboardHelper.SetText(text, Logger);
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
                pasteText = HtmlColorParser.ConvertToColoredMarkdown(html);
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

    internal void InvalidateLayout()
    {
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

    private (double Width, double Height) GetImageSize(InlineImage img, double maxWidth)
    {
        var cached = _imageCache.Get(img.Url, DocumentBasePath, maxWidth);
        if (cached != null)
            return (cached.Value.Width, cached.Value.Height);
        _imageCache.RequestLoad(img.Url, DocumentBasePath, () => InvalidateLayout());
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

    internal void ComputeLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;
        _measure.EnsureMeasured(this);

        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);

        // Merge paragraph lazy continuations in the Document (logical structure per CommonMark spec)
        _doc.MergeParagraphContinuations(_parsedBlocks);

        // After merging, always rebuild parsedBlocks to reflect current block structure and content
        _parsedBlocks = MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);
        _visualMaps = null;

        // Build visual block structure for visual mode rendering
        if (IsVisual)
        {
            _visualBlockStructure = VisualBlockStructure.Build(_parsedBlocks, i => _doc.GetBlockText(i));
        }

        if (IsVisual && _visualMaps == null)
        {
            _visualMaps = new List<BlockVisualMap>(_doc.BlockCount);
            Func<int, string> getText = _doc.GetBlockText;

            // Build parent map for O(1) parent lookup during visual map computation
            var parentMap = BlockVisualMap.BuildParentMap(_parsedBlocks);

            for (int i = 0; i < _doc.BlockCount; i++)
                _visualMaps.Add(BlockVisualMap.Compute(_parsedBlocks[i], getText(i), _parsedBlocks, getText, parentMap));
        }

        ComputeLayoutCore(ActualWidth - _padding * 2);

        if (IsVisual)
            ClampCursorAwayFromHidden();
    }

    private void BuildParagraphGroups()
    {
        _blockToGroup = new Dictionary<int, ParagraphGroup>();

        // If we have VisualBlockStructure, try to use it to identify merged paragraphs
        if (_visualBlockStructure != null)
        {
            bool createdAnyGroups = false;
            for (int vi = 0; vi < _visualBlockStructure.Blocks.Count; vi++)
            {
                var vblock = _visualBlockStructure.Blocks[vi];
                if (vblock.SourceBlockIndices.Count > 1)
                {
                    EmitParagraphGroupFromVisualBlock(vblock);
                    createdAnyGroups = true;
                }
            }
            // If we created groups from VisualBlockStructure, we're done
            if (createdAnyGroups)
                return;
            // Otherwise fall through to original logic
        }

        // Original logic: detect paragraph continuations by analyzing content
        var groupBlocks = new List<int>();

        for (int bi = 0; bi <= _doc.BlockCount; bi++)
        {
            bool canContinue = false;
            if (bi < _doc.BlockCount && _parsedBlocks![bi].Kind == BlockKind.Paragraph
                && _doc.GetBlockLength(bi) > 0 && groupBlocks.Count > 0)
            {
                int prev = groupBlocks[^1];
                string prevText = _doc.GetBlockText(prev);
                var prevParsed = _parsedBlocks![prev];
                int prevContentEnd = MarkdownParser.GetContentEnd(prevText);
                bool prevHardBreak = MarkdownParser.IsTrailingHardBreak(prevParsed, prevText)
                    || (prevContentEnd >= 2 && prevText[prevContentEnd - 1] == ' ' && prevText[prevContentEnd - 2] == ' ');
                if (!prevHardBreak)
                {
                    bool hasEmptyBetween = false;
                    for (int mid = prev + 1; mid < bi; mid++)
                    {
                        if (_doc.GetBlockLength(mid) == 0) { hasEmptyBetween = true; break; }
                    }
                    canContinue = !hasEmptyBetween;
                }
            }

            if (canContinue)
            {
                groupBlocks.Add(bi);
            }
            else
            {
                if (groupBlocks.Count >= 2)
                    EmitParagraphGroup(groupBlocks);
                groupBlocks.Clear();
                if (bi < _doc.BlockCount && _parsedBlocks![bi].Kind == BlockKind.Paragraph
                    && _doc.GetBlockLength(bi) > 0)
                    groupBlocks.Add(bi);
            }
        }

        // After grouping consecutive blocks, check for single blocks that contain
        // internal newlines (merged continuations from MergeParagraphContinuations)
        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            if (_blockToGroup != null && _blockToGroup.ContainsKey(bi))
                continue;  // Already in a group

            if (_parsedBlocks![bi].Kind == BlockKind.Paragraph)
            {
                string blockText = _doc.GetBlockText(bi);
                // Skip empty blocks and blocks with consecutive newlines (merged empty blocks)
                // Only process actual text continuations like "sad\ns"
                if (blockText.Length > 0 && blockText.Contains('\n') && !blockText.Contains("\n\n"))
                {
                    Logger?.Log(DocsLogLevel.Debug, $"Continuation: Block {bi} has internal newline");
                    // This block has internal newlines - it's a merged continuation
                    // Create a group for it
                    var singleBlockGroup = new List<int> { bi };
                    EmitParagraphGroup(singleBlockGroup);
                    Logger?.Log(DocsLogLevel.Debug, $"Continuation: Created ParagraphGroup for block {bi}");
                }
            }
        }
    }

    private void EmitParagraphGroupFromVisualBlock(VisualBlock vblock)
    {
        var blockIndices = vblock.SourceBlockIndices;
        // Convert internal \n to "¶" (pilcrow only; visual space is rendered, not in text)
        var joinedText = vblock.MergedText.ToString().Replace("\n", "¶");

        // Build segments from source indices
        var segments = new JoinSegment[blockIndices.Count];
        var softBreakOffsets = new List<int>();
        int currentOffset = 0;

        for (int i = 0; i < blockIndices.Count; i++)
        {
            if (i > 0)
            {
                // Soft break marker (¶) is at the position
                softBreakOffsets.Add(currentOffset);
                currentOffset += 1; // for "¶" (1 character; replaces \n 1-to-1)
            }

            int bi = blockIndices[i];
            string text = _doc.GetBlockText(bi);
            segments[i] = new JoinSegment(bi, currentOffset, text.Length);
            currentOffset += text.Length;
        }

        // Create BlockVisualMap for the merged text
        var mergedHiddenRanges = new List<HiddenRange>();
        if (IsVisual && _visualMaps != null)
        {
            for (int i = 0; i < blockIndices.Count; i++)
            {
                var map = _visualMaps[blockIndices[i]];
                foreach (var hr in map.HiddenRanges)
                    mergedHiddenRanges.Add(new HiddenRange(hr.Start + segments[i].OffsetInJoined, hr.Length));
            }
        }
        mergedHiddenRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

        var joinedParsed = new ParsedBlock
        {
            Kind = BlockKind.Paragraph,
            Runs = vblock.Runs,
            Images = vblock.Images,
            ColorSpans = vblock.ColorSpans,
        };
        var joinedMap = new BlockVisualMap(mergedHiddenRanges,
            images: vblock.Images,
            colorSpans: vblock.ColorSpans);

        var group = new ParagraphGroup
        {
            Segments = segments,
            JoinedText = joinedText,
            JoinedMap = joinedMap,
            JoinedParsed = joinedParsed,
            SoftBreakOffsets = softBreakOffsets.ToArray(),
        };

        foreach (var seg in segments)
            _blockToGroup![seg.BlockIndex] = group;
    }

    private void EmitParagraphGroup(List<int> blockIndices)
    {
        var sb = new System.Text.StringBuilder();
        var segments = new JoinSegment[blockIndices.Count];
        var softBreakOffsets = new List<int>();
        // Map from original position to display position for offset adjustments
        var positionMaps = new List<Dictionary<int, int>>();

        for (int i = 0; i < blockIndices.Count; i++)
        {
            if (i > 0)
            {
                softBreakOffsets.Add(sb.Length);
                sb.Append("¶");  // pilcrow only (visual space is rendered, not in text)
            }
            int bi = blockIndices[i];
            string text = _doc.GetBlockText(bi);
            int startPos = sb.Length;
            var posMap = new Dictionary<int, int>();

            // Handle internal newlines in merged blocks
            if (text.Contains('\n'))
            {
                int displayPos = startPos;
                int sourcePos = 0;
                var parts = text.Split('\n');

                for (int j = 0; j < parts.Length; j++)
                {
                    if (j > 0)
                    {
                        softBreakOffsets.Add(sb.Length);
                        sb.Append("¶");  // pilcrow only (visual space is rendered, not in text)
                        sourcePos++;  // skip the \n
                        displayPos++;  // ¶ replaces \n 1-to-1
                    }

                    string part = parts[j];
                    sb.Append(part);
                    for (int k = 0; k < part.Length; k++)
                    {
                        posMap[sourcePos] = displayPos;
                        sourcePos++;
                        displayPos++;
                    }
                }

                segments[i] = new JoinSegment(bi, startPos, text.Length);
            }
            else
            {
                sb.Append(text);
                // For non-merged blocks, positions don't change
                for (int k = 0; k < text.Length; k++)
                    posMap[k] = startPos + k;
                segments[i] = new JoinSegment(bi, startPos, text.Length);
            }

            positionMaps.Add(posMap);
        }

        string joinedText = sb.ToString();

        var mergedRuns = new List<StyledRun>();
        var mergedImages = new List<InlineImage>();
        var mergedHiddenRanges = new List<HiddenRange>();
        var mergedColorSpans = new List<ColorSpan>();

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var parsed = _parsedBlocks![seg.BlockIndex];
            var map = _visualMaps![seg.BlockIndex];
            var posMap = positionMaps[i];

            foreach (var run in parsed.Runs)
            {
                var (displayStart, displayLength) = MapOffset(run.Start, run.Length, posMap, seg.OffsetInJoined);
                mergedRuns.Add(new StyledRun(displayStart, displayLength, run.Style));
            }

            if (parsed.Images != null)
            {
                foreach (var img in parsed.Images)
                {
                    var (displayStart, displayLength) = MapOffset(img.Start, img.Length, posMap, seg.OffsetInJoined);
                    mergedImages.Add(new InlineImage(
                        displayStart, displayLength, img.AltText, img.Url, img.Title));
                }
            }

            if (parsed.ColorSpans != null)
            {
                foreach (var cs in parsed.ColorSpans)
                {
                    var (displayStart, displayLength) = MapOffset(cs.Start, cs.Length, posMap, seg.OffsetInJoined);
                    mergedColorSpans.Add(new ColorSpan(
                        displayStart, displayLength, cs.Foreground, cs.Background));
                }
            }

            foreach (var hr in map.HiddenRanges)
            {
                var (displayStart, displayLength) = MapOffset(hr.Start, hr.Length, posMap, seg.OffsetInJoined);
                mergedHiddenRanges.Add(new HiddenRange(displayStart, displayLength));
            }
        }

        mergedRuns.Sort((a, b) => a.Start.CompareTo(b.Start));
        mergedHiddenRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

        var joinedParsed = new ParsedBlock
        {
            Kind = BlockKind.Paragraph,
            Runs = mergedRuns,
            Images = mergedImages.Count > 0 ? mergedImages : null,
            ColorSpans = mergedColorSpans.Count > 0 ? mergedColorSpans : null,
        };
        var joinedMap = new BlockVisualMap(mergedHiddenRanges,
            images: mergedImages.Count > 0 ? mergedImages : null,
            colorSpans: mergedColorSpans.Count > 0 ? mergedColorSpans : null);

        var group = new ParagraphGroup
        {
            Segments = segments,
            JoinedText = joinedText,
            JoinedMap = joinedMap,
            JoinedParsed = joinedParsed,
            SoftBreakOffsets = softBreakOffsets.ToArray(),
        };

        foreach (var seg in segments)
            _blockToGroup![seg.BlockIndex] = group;
    }

    private (int displayStart, int displayLength) MapOffset(int sourceStart, int sourceLength, Dictionary<int, int> posMap, int segOffset)
    {
        if (posMap.Count == 0)
            return (segOffset + sourceStart, sourceLength);

        // Get display start position
        int displayStart = posMap.ContainsKey(sourceStart) ? posMap[sourceStart] : segOffset + sourceStart;

        // Calculate display end position
        int sourceEnd = sourceStart + sourceLength - 1;  // Last source position in the range
        int displayEnd;

        if (posMap.ContainsKey(sourceEnd))
        {
            displayEnd = posMap[sourceEnd];
        }
        else if (posMap.Count > 0)
        {
            // Extrapolate from the last mapped position
            int lastMappedSource = posMap.Keys.Max();
            int lastMappedDisplay = posMap[lastMappedSource];
            int unmappedDistance = sourceEnd - lastMappedSource;
            displayEnd = lastMappedDisplay + unmappedDistance;
        }
        else
        {
            displayEnd = segOffset + sourceEnd;
        }

        int displayLength = displayEnd - displayStart + 1;
        return (displayStart, displayLength);
    }

    private void ComputeLayoutCore(double maxWidth)
    {
        _visualLines.Clear();
        _lineYPositions.Clear();
        _tableColumnWidths.Clear();
        maxWidth = Math.Max(0, maxWidth);
        _layoutMaxWidth = maxWidth;

        if (IsVisual)
        {
            _visualLineSpacings = [];
            ComputeAllTableColumnWidths(maxWidth);
            BuildParagraphGroups();
        }

        // Identify which blocks are children of containers (used to skip during iteration)
        var childBlockIndices = new HashSet<int>();
        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];
            if (parsed.Children != null)
            {
                foreach (var child in parsed.Children)
                {
                    // Find the flat index of this child
                    for (int ci = 0; ci < _doc.BlockCount; ci++)
                    {
                        if (_parsedBlocks![ci] == child)
                        {
                            childBlockIndices.Add(ci);
                            break;
                        }
                    }
                }
            }
        }

        // Process blocks, using hierarchy when available
        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];

            if (IsVisual && parsed.IsSkippedInVisual)
                continue;

            // Skip blocks that are children - they'll be processed via their parent's Children
            if (childBlockIndices.Contains(bi))
                continue;

            // Process this block and its children recursively
            ProcessBlockAndChildren(bi, parsed, maxWidth, nestingDepth: 0, parentContentCol: 0);
        }

        double y = _padding;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            int bi = _visualLines[i].BlockIndex;
            if (i > 0 && bi != _visualLines[i - 1].BlockIndex)
            {
                var curGroup = _visualLines[i].Group;
                var prevGroup = _visualLines[i - 1].Group;
                bool sameGroup = curGroup != null && prevGroup == curGroup;

                if (!sameGroup)
                {
                    bool paragraphBreak = false;
                    for (int prev = _visualLines[i - 1].BlockIndex; prev < bi && !paragraphBreak; prev++)
                    {
                        if (_doc.GetBlockLength(prev) == 0)
                            paragraphBreak = true;
                    }
                    if (paragraphBreak && _doc.GetBlockLength(_visualLines[i - 1].BlockIndex) > 0)
                        y += _paragraphGap;
                }
            }
            _lineYPositions.Add(y);
            var lineVl = _visualLines[i];
            double lineH = _measure.GetLineHeight(lineVl.BlockKind);
            if (lineVl.OverrideHeight > lineH) lineH = lineVl.OverrideHeight;
            y += lineH;
        }
        _totalContentHeight = y + _padding;

        // Compute and cache spacing for each visual line (visual mode only)
        if (IsVisual && _visualLineSpacings != null)
        {
            foreach (var vl in _visualLines)
            {
                _visualLineSpacings.Add(ComputeVisualLineSpacing(vl));
            }
        }

        _layoutVersion++;
    }

    private void ProcessBlockAndChildren(int blockIndex, ParsedBlock parsed, double maxWidth, int nestingDepth, int parentContentCol)
    {
        if (IsVisual && _blockToGroup != null && _blockToGroup.TryGetValue(blockIndex, out var group))
        {
            Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is in a ParagraphGroup");
            if (blockIndex == group.FirstBlock)
            {
                Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is FirstBlock, wrapping as joined");
                WrapSegmentJoined(group, maxWidth);
            }
            return;
        }

        Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is NOT in a ParagraphGroup");

        string text = _doc.GetBlockText(blockIndex);

        if (text.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, 0, 0, parsed.Kind)
            {
                OverrideHeight = _paragraphGap,
                NestingDepth = nestingDepth,
                ParentContentColumn = parentContentCol
            });

            // Process children of empty blocks
            if (parsed.Children != null)
            {
                int childParentCol = nestingDepth > 0 ? parentContentCol : parsed.ContentColumn;
                foreach (var child in parsed.Children)
                {
                    int childIndex = FindBlockIndex(child);
                    if (childIndex >= 0)
                        ProcessBlockAndChildren(childIndex, child, maxWidth, nestingDepth + 1, childParentCol);
                }
            }
            return;
        }

        var map = IsVisual ? _visualMaps?[blockIndex] : null;

        if (IsVisual && parsed.Kind == BlockKind.ThematicBreak)
        {
            _visualLines.Add(new VisualLine(blockIndex, 0, text.Length, parsed.Kind)
            {
                OverrideHeight = 20,
                NestingDepth = nestingDepth,
                ParentContentColumn = parentContentCol
            });
            return;
        }

        if (IsVisual && parsed.Table != null && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
        {
            _visualLines.Add(new VisualLine(blockIndex, 0, text.Length, parsed.Kind)
            {
                NestingDepth = nestingDepth,
                ParentContentColumn = parentContentCol
            });
            return;
        }

        var segments = text.Split('\n');
        int offset = 0;
        for (int s = 0; s < segments.Length; s++)
        {
            WrapSegment(blockIndex, offset, segments[s], maxWidth, parsed, map, nestingDepth, parentContentCol);
            offset += segments[s].Length + 1;
        }

        // Process children (skip paragraph continuations - they're rendered with parent)
        if (parsed.Children != null)
        {
            int childParentCol = nestingDepth > 0 ? parentContentCol : parsed.ContentColumn;
            foreach (var child in parsed.Children)
            {
                // Skip rendering paragraph lazy continuations separately
                if (parsed.Kind == BlockKind.Paragraph && child.Kind == BlockKind.Paragraph)
                    continue;

                int childIndex = FindBlockIndex(child);
                if (childIndex >= 0)
                    ProcessBlockAndChildren(childIndex, child, maxWidth, nestingDepth + 1, childParentCol);
            }
        }
    }

    private int FindBlockIndex(ParsedBlock block)
    {
        for (int i = 0; i < _doc.BlockCount; i++)
        {
            if (_parsedBlocks![i] == block)
                return i;
        }
        return -1;
    }

    private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth,
        ParsedBlock parsed, BlockVisualMap? map = null, int nestingDepth = 0, int parentContentCol = 0)
    {
        if (segment.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, startOffset, 0, parsed.Kind)
            {
                NestingDepth = nestingDepth,
                ParentContentColumn = parentContentCol
            });
            return;
        }

        double prefixWidth = 0;
        if (map?.ReplacementPrefix != null)
            prefixWidth = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

        int pos = 0;
        while (pos < segment.Length)
        {
            double lineMax = pos == 0 ? maxWidth - prefixWidth : maxWidth;
            int lineLen = FitLine(segment, pos, lineMax, parsed, map, startOffset);
            var vl = new VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind)
            {
                NestingDepth = nestingDepth,
                ParentContentColumn = parentContentCol
            };
            if (IsVisual && map?.Images != null)
            {
                double imgH = GetImageMaxLineHeight(vl, map);
                if (imgH > 0) vl = vl with { OverrideHeight = imgH };
            }
            else if (!IsVisual && _imagePreview == ImagePreviewMode.Inline && parsed.Images != null)
            {
                double imgH = GetSourceInlineImageHeight(vl, parsed.Images);
                if (imgH > 0)
                    vl = vl with { OverrideHeight = _measure.GetLineHeight(parsed.Kind) + imgH };
            }
            _visualLines.Add(vl);
            pos += lineLen;
        }
    }

    private void WrapSegmentJoined(ParagraphGroup group, double maxWidth)
    {
        string text = group.JoinedText;
        if (text.Length == 0)
        {
            _visualLines.Add(new VisualLine(group.FirstBlock, 0, 0, BlockKind.Paragraph)
                { Group = group });
            return;
        }

        int pos = 0;
        while (pos < text.Length)
        {
            int lineLen = FitLine(text, pos, maxWidth, group.JoinedParsed, group.JoinedMap);
            var (bi, _) = group.JoinedToSource(pos);
            var vl = new VisualLine(bi, pos, lineLen, BlockKind.Paragraph) { Group = group };
            if (group.JoinedMap.Images != null)
            {
                double imgH = GetImageMaxLineHeight(vl, group.JoinedMap);
                if (imgH > 0) vl = vl with { OverrideHeight = imgH };
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
            if (text[i] is ' ' or '¶') lastSpace = i;
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawOffset, ref runIdx);
            width += _measure.MeasureCharWidth(text[i], parsed.Kind, style);
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

    internal const double _tableCellPadding = 8;

    // --- Cursor ↔ visual line mapping ---

    private int CursorToVisualLineIndex()
    {
        for (int i = _visualLines.Count - 1; i >= 0; i--)
        {
            var vl = _visualLines[i];
            if (vl.Group != null)
            {
                int joined = vl.Group.SourceToJoined(_doc.CursorBlock, _doc.CursorOffset);
                if (joined >= 0 && joined >= vl.StartOffset && joined <= vl.StartOffset + vl.Length)
                {
                    if (_cursorAtLineEnd && joined == vl.StartOffset && i > 0
                        && _visualLines[i - 1].Group == vl.Group)
                        continue;
                    return i;
                }
            }
            else if (vl.BlockIndex == _doc.CursorBlock && vl.StartOffset <= _doc.CursorOffset)
            {
                if (_cursorAtLineEnd && vl.StartOffset == _doc.CursorOffset && i > 0
                    && _visualLines[i - 1].BlockIndex == vl.BlockIndex)
                    continue;
                return i;
            }
        }
        return 0;
    }

    private BlockVisualSpacing ComputeVisualLineSpacing(VisualLine vl)
    {
        if (!IsVisual || _parsedBlocks == null || _visualMaps == null || vl.BlockIndex >= _parsedBlocks.Count || vl.BlockIndex >= _visualMaps.Count)
            return new BlockVisualSpacing { ContentStartX = _padding };

        var parsed = _parsedBlocks[vl.BlockIndex];
        var map = _visualMaps[vl.BlockIndex];

        var spacing = new BlockVisualSpacing();
        double textX = _padding;

        // Add nesting indentation (block hierarchy)
        // For continuation blocks, skip this because the prefix width will serve as the indentation
        if (vl.NestingDepth > 0 && !map.IsContinuationIndent)
        {
            double charWidth = _measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);
            textX += vl.ParentContentColumn * charWidth;
        }


        // Handle markers and content positioning
        if (vl.StartOffset == 0)
        {
            if (parsed.Kind == BlockKind.Blockquote)
            {
                // Blockquote bar positioning
                var aligner = new ContentBlockAligner(textX, _measure.ListIndent);
                spacing.MarkerStartX = aligner.GetBlockquoteBarX();
                spacing.MarkerWidth = 3;
                spacing.SpacingAfterMarker = aligner.GetSpacingAfterMarker();
                spacing.ContentStartX = aligner.GetBlockquoteContentIndentX();
            }
            else if (map.ReplacementPrefix != null)
            {
                double prefixWidth = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

                if (!map.IsContinuationIndent)
                {
                    // List marker spacing structure:
                    // 1. Nesting indentation (from ListNestingLevel)
                    // 2. Fixed space before marker (2 spaces)
                    // 3. Marker (centered at MarkerStartX)
                    // 4. Fixed space after marker (SpacingAfterMarker)
                    // 5. Text content (at ContentStartX)

                    bool isListItem = parsed.Kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem or
                        BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked;

                    if (isListItem)
                    {
                        double spaceCharWidth = _measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);

                        // 1. Nesting indentation (from ListNestingLevel)
                        double nestingIndentWidth = parsed.ListNestingLevel > 0
                            ? parsed.ListNestingLevel * BlockVisualMap.SpacesPerNestingLevel * spaceCharWidth
                            : 0;

                        // 2. Fixed space before marker (2 spaces)
                        const double spacesBeforeMarker = 2;
                        double spaceBeforeMarkerWidth = spacesBeforeMarker * spaceCharWidth;

                        // Use standard marker width (checked checkbox) for all types to align centers
                        double standardMarkerWidth = _measure.MeasureReplacementPrefix("☑", parsed.Kind);

                        // 3. Marker center position
                        double markerCenterX = _padding + nestingIndentWidth + spaceBeforeMarkerWidth + (standardMarkerWidth / 2);
                        spacing.MarkerStartX = markerCenterX;

                        // 4. Fixed space after marker
                        const double spacingAfterMarker = 4.0;
                        spacing.SpacingAfterMarker = spacingAfterMarker;

                        // For ordered items, use actual marker width for proper content positioning
                        double actualMarkerWidth = prefixWidth;
                        if (parsed.Kind != BlockKind.OrderedListItem)
                        {
                            // For bullets and checkboxes, use standard width
                            actualMarkerWidth = standardMarkerWidth;
                        }

                        // 5. Text content start position
                        spacing.ContentStartX = _padding + nestingIndentWidth + spaceBeforeMarkerWidth + actualMarkerWidth + spacingAfterMarker;

                        spacing.MarkerWidth = standardMarkerWidth;
                    }
                    else
                    {
                        // Non-list markers (blockquotes, etc.)
                        double baseX = textX;
                        var aligner = new ContentBlockAligner(baseX, _measure.ListIndent);

                        if (parsed.Kind == BlockKind.Blockquote)
                        {
                            spacing.MarkerStartX = aligner.GetBlockquoteBarX();
                            spacing.MarkerWidth = 3;
                            spacing.SpacingAfterMarker = aligner.GetSpacingAfterMarker();
                            spacing.ContentStartX = aligner.GetBlockquoteContentIndentX();
                        }
                        else
                        {
                            spacing.MarkerStartX = textX;
                            spacing.MarkerWidth = prefixWidth;
                            spacing.SpacingAfterMarker = 0;
                            spacing.ContentStartX = textX + prefixWidth;
                        }
                    }
                }
                else
                {
                    // Continuation block: indent to match parent's content by using prefix width
                    // (nesting indentation is skipped for continuation blocks)
                    spacing.ContentStartX = textX + prefixWidth;
                    spacing.MarkerStartX = textX;
                    spacing.MarkerWidth = 0;
                    spacing.SpacingAfterMarker = 0;
                }

            }
            else
            {
                spacing.MarkerStartX = textX;
                spacing.MarkerWidth = 0;
                spacing.SpacingAfterMarker = 0;
                spacing.ContentStartX = textX;
            }
        }
        else
        {
            // Continuation lines - align with first line's content position
            spacing.MarkerStartX = textX;
            spacing.MarkerWidth = 0;
            spacing.SpacingAfterMarker = 0;

            if (map.ReplacementPrefix != null)
            {
                // Continuation line of a list/blockquote - indent to match first line content
                double prefixWidth = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                spacing.ContentStartX = textX + prefixWidth;
            }
            else
            {
                spacing.ContentStartX = textX;
            }
        }

        return spacing;
    }

    private double GetTextStartXForVisualLine(VisualLine vl)
    {
        if (!IsVisual || _visualLineSpacings == null || vl.BlockIndex < 0)
            return _padding;

        // Find the index of this VisualLine
        int vlIndex = -1;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (_visualLines[i] == vl)
            {
                vlIndex = i;
                break;
            }
        }

        if (vlIndex < 0 || vlIndex >= _visualLineSpacings.Count)
            return _padding;

        return _visualLineSpacings[vlIndex]?.ContentStartX ?? _padding;
    }

    private BlockVisualSpacing? GetVisualLineSpacing(VisualLine vl)
    {
        if (!IsVisual || _visualLineSpacings == null || vl.BlockIndex < 0)
            return null;

        // Find the index of this VisualLine
        int vlIndex = -1;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (_visualLines[i] == vl)
            {
                vlIndex = i;
                break;
            }
        }

        if (vlIndex < 0 || vlIndex >= _visualLineSpacings.Count)
            return null;

        return _visualLineSpacings[vlIndex];
    }

    private double CursorXInVisualLine(int vlIndex)
    {
        var vl = _visualLines[vlIndex];

        if (vl.Group != null)
        {
            int joinedOffset = vl.Group.SourceToJoined(_doc.CursorBlock, _doc.CursorOffset);
            int localOffset = Math.Clamp(joinedOffset - vl.StartOffset, 0, vl.Length);
            if (localOffset == 0) return 0;
            return MeasureJoinedRange(vl.Group, vl.StartOffset, localOffset);
        }

        int localOff = Math.Clamp(_doc.CursorOffset - vl.StartOffset, 0, vl.Length);
        var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

        var parsed = _parsedBlocks![vl.BlockIndex];
        if (IsVisual && parsed.Table != null && parsed.TableRow != null
            && _tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return CursorXInTableRow(vl.BlockIndex, parsed, colWidths, localOff);
        }

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double x = GetTextStartXForVisualLine(vl);

        // Subtract padding since we're returning cursor x relative to control left edge
        // (ContentStartX from cache already accounts for ReplacementPrefix width)
        x -= _padding;

        if (localOff == 0) return x;

        if (map == null)
        {
            string lineText = blockText.Substring(vl.StartOffset, vl.Length);
            var ft = new FormattedText(lineText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.GetBlockBaseTypeface(vl.BlockKind),
                _measure.GetBlockFontSize(vl.BlockKind), _palette.Foreground, _measure.DpiScale);
            ApplyInlineStyles(ft, vl, parsed, blockText);
            var geom = ft.BuildHighlightGeometry(new Point(0, 0), 0, localOff);
            return x + (geom != null ? geom.Bounds.Right : ft.WidthIncludingTrailingWhitespace);
        }

        int runIdx = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + localOff; i++)
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
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            x += _measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
        }
        return x;
    }

    internal double MeasureJoinedRange(ParagraphGroup group, int start, int length)
    {
        double width = MeasureRangeWidth(group.JoinedText, start, length,
            group.JoinedParsed.Runs, BlockKind.Paragraph, group.JoinedMap);

        // Add visual space width for soft breaks that fall within the range
        var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
        int runIdx = 0;
        for (int i = start; i < start + length; i++)
        {
            if (softBreaks.Contains(i) && i < group.JoinedText.Length && group.JoinedText[i] == '¶')
            {
                // Add visual space width after each pilcrow
                var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, i, ref runIdx);
                double spaceW = _measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                width += spaceW;
            }
        }

        return width;
    }

    private int HitTestInVisualLineProper(int vlIndex, double clickX)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        var parsed = _parsedBlocks![vl.BlockIndex];
        var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);

        // Account for where text actually starts on screen
        double textStartX = GetTextStartXForVisualLine(vl);

        // clickX is already adjusted by _padding, so adjust textStartX to match
        // (textStartX is in screen coordinates, so we need to remove padding to match clickX)
        double offsetFromTextStart = clickX - (textStartX - _padding);

        // Measure x position for each visible character and find closest to offsetFromTextStart
        // Start at 0 since offsetFromTextStart is already relative to where text starts
        double accum = 0;

        int runIdx = 0;
        double closestDist = double.MaxValue;
        int closestOffset = vl.StartOffset;

        for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
        {
            double charStart = accum;

            if (map != null && map.IsHidden(i))
            {
                var img = FindImageAtRawOffset(map.Images, i);
                if (img != null)
                {
                    var (imgW, _) = GetImageSize(img.Value, _layoutMaxWidth);
                    accum += imgW;
                }
                continue;
            }

            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            double charW = _measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
            double charEnd = accum + charW;

            // Check if click is closer to this char's start or end
            double distToStart = Math.Abs(offsetFromTextStart - charStart);
            double distToEnd = Math.Abs(offsetFromTextStart - charEnd);
            double minDist = Math.Min(distToStart, distToEnd);

            if (minDist < closestDist)
            {
                closestDist = minDist;
                closestOffset = i + (distToEnd < distToStart ? 1 : 0);
            }

            accum = charEnd;
        }

        return Math.Min(closestOffset, vl.StartOffset + vl.Length);
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        if (vl.Group != null)
            return HitTestInJoinedLine(vl, x);

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
            double prefixW = _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
            Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} has replacement prefix, prefixW={prefixW}, x={x}");
            if (x < prefixW)
            {
                Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Click in prefix area, returning StartOffset={vl.StartOffset}");
                return vl.StartOffset;
            }
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
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = _measure.MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
            {
                Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} matched char at offset {offset} (accum={accum}, charW={charW})");
                return offset;
            }
            accum += charW;
        }
        Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} past all chars, returning end offset {vl.StartOffset + vl.Length} (accum={accum}, x={x})");
        return vl.StartOffset + vl.Length;
    }

    private int HitTestInJoinedLine(VisualLine vl, double x)
    {
        var group = vl.Group!;
        var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
        double accum = 0;
        int runIdx = 0;

        for (int i = 0; i < vl.Length; i++)
        {
            int offset = vl.StartOffset + i;
            if (group.JoinedMap.IsHidden(offset))
            {
                var img = FindImageAtRawOffset(group.JoinedMap.Images, offset);
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
            var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, offset, ref runIdx);
            double charW = _measure.MeasureCharWidth(group.JoinedText[offset], BlockKind.Paragraph, style);

            // For soft breaks, account for visual space when hit-testing
            double testWidth = charW;
            if (softBreaks.Contains(offset) && group.JoinedText[offset] == '¶')
            {
                double spaceW = _measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                testWidth += spaceW;  // Use full visual width for hit-testing
            }

            // Check if click is in this character's area
            if (x < accum + testWidth / 2)
                return offset;

            // Advance by character width only (not visual space - that's rendering-only)
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private int HitTestVisualLine(double y)
    {
        if (_visualLines.Count == 0) return 0;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            double lineH = GetEffectiveLineHeight(_visualLines[i]);
            if (y < _lineYPositions[i] + lineH)
                return i;
        }
        return _visualLines.Count - 1;
    }

    internal void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
    {
        if (_visualLines.Count == 0) { blockIndex = 0; charOffset = 0; return; }
        double effectiveScroll = _scroll.EffectiveOffset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _visualLines[vli];
        double xForHitTest = pos.X - _padding;

        int rawOffset = IsVisual ? HitTestInVisualLineProper(vli, xForHitTest) : HitTestInVisualLine(vli, xForHitTest);

        if (vl.Group != null)
        {
            var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
            blockIndex = bi;
            charOffset = bo;
        }
        else
        {
            blockIndex = vl.BlockIndex;
            charOffset = rawOffset;
        }
        Logger?.Log(DocsLogLevel.Debug, $"HitTestToPosition: Click at ({pos.X}, {pos.Y}) -> Block {blockIndex}, Offset {charOffset}");
    }

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
            ComputeLayout();
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        _measure.EnsureMeasured(this);
        dc.DrawRectangle(_palette.Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_parsedBlocks == null)
            return;

        double effectiveScroll = Math.Round(_scroll.EffectiveOffset);
        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        DrawCodeBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        DrawColorBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        DrawInlineColorBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        if (IsVisual)
            DrawTableBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

        if (FindAndReplace.TestSearchMatchCount > 0)
            DrawSearchHighlights(dc, effectiveScroll);

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
                if (vl.Group != null)
                {
                    DrawJoinedLine(dc, vl, lineY, effectiveScroll);
                }
                else
                {
                    var parsed = _parsedBlocks[vl.BlockIndex];
                    string blockText = _doc.GetBlockText(vl.BlockIndex);
                    double fontSize = _measure.GetBlockFontSize(parsed.Kind);
                    var baseTypeface = TextMeasurer.GetBlockBaseTypeface(parsed.Kind);
                    var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

                    double textX = GetTextStartXForVisualLine(vl);

                    if (IsVisual && parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
                    {
                        DrawBlockquoteBar(dc, lineY, effectiveScroll);
                    }

                    if (IsVisual && parsed.Kind == BlockKind.ThematicBreak)
                    {
                        double ruleY = lineY - effectiveScroll + 10;
                        double ruleRight = ActualWidth - _padding;
                        dc.DrawLine(_palette.TableBorderPen, new Point(_padding, ruleY), new Point(ruleRight, ruleY));
                    }
                    else if (IsVisual && parsed.Table != null && parsed.TableRow != null)
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
                            // In source mode, only draw actual markdown syntax (bullets, numbers, etc)
                            // but NOT continuation indentation - show raw text at column 0
                            if (map.ReplacementPrefix != null && vl.StartOffset == 0 && !map.IsContinuationIndent)
                            {
                                if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                                {
                                    var spacing = GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                                            new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(lineY - effectiveScroll),
                                            parsed.Kind);
                                    }
                                }
                                else if (parsed.Kind == BlockKind.UnorderedListItem)
                                {
                                    var spacing = GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX),
                                            new AbsoluteY(lineY - effectiveScroll),
                                            parsed.Kind, parsed.ListNestingLevel);
                                    }
                                }
                                else if (parsed.Kind == BlockKind.OrderedListItem)
                                {
                                    var spacing = GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX),
                                            new AbsoluteY(lineY - effectiveScroll),
                                            map.ReplacementPrefix!, fontSize, parsed.ListNestingLevel);
                                    }
                                }
                                else
                                {
                                    var prefixFt = new FormattedText(map.ReplacementPrefix!,
                                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                        TextMeasurer.NormalTypeface, fontSize, _palette.Syntax, _measure.DpiScale);
                                    dc.DrawText(prefixFt, new Point(_padding, lineY - effectiveScroll));
                                }
                            }

                            string displayText = map.BuildDisplayString(blockText, vl.StartOffset, vl.Length);
                            if (displayText.Length > 0 || (parsed.Kind == BlockKind.HtmlBlock && parsed.CreateVisualSeparation))
                            {
                                if (displayText.Length > 0)
                                {
                                    var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                                        FlowDirection.LeftToRight, baseTypeface, fontSize,
                                        _palette.Foreground, _measure.DpiScale);
                                    ApplyInlineStylesVisual(ft, vl, parsed, map);
                                    if (parsed.Kind == BlockKind.TaskListItemChecked)
                                    {
                                        ft.SetForegroundBrush(_palette.Syntax, 0, displayText.Length);
                                        ft.SetTextDecorations(TextDecorations.Strikethrough, 0, displayText.Length);
                                    }
                                    dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));
                                }
                            }
                        }
                    }
                    else
                    {
                        string text = blockText.Substring(vl.StartOffset, vl.Length);
                        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, baseTypeface, fontSize,
                            _palette.Foreground, _measure.DpiScale);
                        ApplyInlineStyles(ft, vl, parsed, blockText);
                        dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));

                        if (_showWhitespace)
                            DrawTrailingSpaceDots(dc, vl, blockText, parsed, textX, lineY - effectiveScroll);

                        if (_imagePreview == ImagePreviewMode.Inline && parsed.Images != null)
                            DrawSourceInlineImages(dc, vl, parsed.Images, lineY, effectiveScroll);
                    }
                }
            }
        }

        if (SpellCheckEnabled)
            DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

        if (ShowPageBreaks)
            DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);

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

        Dispatcher.BeginInvoke(() =>
        {
            Minimap?.InvalidateVisual();
            ScrollStateChanged?.Invoke();
        });
    }

    private void DrawJoinedLine(DrawingContext dc, VisualLine vl,
        double lineY, double effectiveScroll)
    {
        var group = vl.Group!;
        Logger?.Log(DocsLogLevel.Debug, $"DrawJoinedLine: Rendering joined line with text '{group.JoinedText}'");

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
        InvalidateLayout();
    }
}
