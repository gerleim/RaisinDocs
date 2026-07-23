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

public partial class DocsCanvas : FrameworkElement
{
    private const double _padding = 10;
    private const double _paragraphGap = 8;


    public enum EditorTheme { Light, Dark, DarkBlue }

    private sealed record ThemePalette(
        Brush Background, Brush Foreground, Pen CursorPen,
        Brush Selection, Brush ScrollTrack, Brush ScrollThumb,
        Brush Syntax, Brush CodeBackground,
        Brush TableBackground, Brush TableHeaderBackground, Pen TableBorderPen,
        Brush SearchMatch, Brush CurrentSearchMatch);

    private static readonly ThemePalette _lightPalette;
    private static readonly ThemePalette _darkPalette;
    private static readonly ThemePalette _darkBluePalette;
    private ThemePalette _palette = _darkPalette!;

    private static readonly Brush _checkboxCheckedBrush;
    private static readonly Brush _imagePlaceholderBrush;
    private readonly TextMeasurer _measure = new();
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

    private readonly Document _doc = new();

    private bool _cursorVisible = true;
    private bool _cursorAtLineEnd;
    private (string Marker, InlineStyle Style)? _pendingStyleOff;
    private readonly DispatcherTimer _blinkTimer;
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    private readonly record struct JoinSegment(int BlockIndex, int OffsetInJoined, int Length);

    private sealed class ParagraphGroup
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

    private record struct VisualLine(int BlockIndex, int StartOffset, int Length, BlockKind BlockKind)
    {
        public double OverrideHeight { get; init; }
        public ParagraphGroup? Group { get; init; }
    }
    private readonly List<VisualLine> _visualLines = [];
    private readonly List<double> _lineYPositions = [];
    private readonly Dictionary<TableInfo, double[]> _tableColumnWidths = new();
    private bool _layoutDirty = true;
    private double _totalContentHeight;
    private double _layoutMaxWidth;
    private readonly ScrollController _scroll;

    private List<ParsedBlock>? _parsedBlocks;
    private List<BlockVisualMap>? _visualMaps;
    private Dictionary<int, ParagraphGroup>? _blockToGroup;
    private readonly ImageCache _imageCache = new();
    private int _layoutVersion;

    public string? DocumentBasePath { get; set; }

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
    internal string TestGetBlockText(int block) => _doc.GetBlockText(block);
    internal int TestBlockCount => _doc.BlockCount;
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
    }

    private void ResetUndoSealTimer()
    {
        _undoSealTimer.Stop();
        _undoSealTimer.Start();
        IsDirty = !_doc.IsClean;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SealAndStopTimer()
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

    private double GetEffectiveLineHeight(VisualLine vl)
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
        _visualMaps = null;
        _blockToGroup = null;
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

    private void ComputeLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;
        _measure.EnsureMeasured(this);

        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);

        if (IsVisual && _visualMaps == null)
        {
            _visualMaps = new List<BlockVisualMap>(_doc.BlockCount);
            Func<int, string> getText = _doc.GetBlockText;
            for (int i = 0; i < _doc.BlockCount; i++)
                _visualMaps.Add(BlockVisualMap.Compute(_parsedBlocks[i], getText(i), _parsedBlocks, getText));
        }

        ComputeLayoutCore(ActualWidth - _padding * 2);
    }

    private void BuildParagraphGroups()
    {
        _blockToGroup = new Dictionary<int, ParagraphGroup>();
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
    }

    private void EmitParagraphGroup(List<int> blockIndices)
    {
        var sb = new System.Text.StringBuilder();
        var segments = new JoinSegment[blockIndices.Count];
        var softBreakOffsets = new List<int>();

        for (int i = 0; i < blockIndices.Count; i++)
        {
            if (i > 0)
            {
                softBreakOffsets.Add(sb.Length);
                sb.Append('¶');
            }
            int bi = blockIndices[i];
            string text = _doc.GetBlockText(bi);
            segments[i] = new JoinSegment(bi, sb.Length, text.Length);
            sb.Append(text);
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

            foreach (var run in parsed.Runs)
                mergedRuns.Add(new StyledRun(run.Start + seg.OffsetInJoined, run.Length, run.Style));

            if (parsed.Images != null)
            {
                foreach (var img in parsed.Images)
                    mergedImages.Add(new InlineImage(
                        img.Start + seg.OffsetInJoined, img.Length, img.AltText, img.Url, img.Title));
            }

            if (parsed.ColorSpans != null)
            {
                foreach (var cs in parsed.ColorSpans)
                    mergedColorSpans.Add(new ColorSpan(
                        cs.Start + seg.OffsetInJoined, cs.Length, cs.Foreground, cs.Background));
            }

            foreach (var hr in map.HiddenRanges)
                mergedHiddenRanges.Add(new HiddenRange(hr.Start + seg.OffsetInJoined, hr.Length));
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

    private void ComputeLayoutCore(double maxWidth)
    {
        _visualLines.Clear();
        _lineYPositions.Clear();
        _tableColumnWidths.Clear();
        maxWidth = Math.Max(0, maxWidth);
        _layoutMaxWidth = maxWidth;

        if (IsVisual)
        {
            ComputeAllTableColumnWidths(maxWidth);
            BuildParagraphGroups();
        }

        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];

            if (IsVisual && parsed.IsSkippedInVisual)
                continue;

            if (IsVisual && _blockToGroup != null && _blockToGroup.TryGetValue(bi, out var group))
            {
                if (bi == group.FirstBlock)
                    WrapSegmentJoined(group, maxWidth);
                continue;
            }

            string text = _doc.GetBlockText(bi);

            if (text.Length == 0)
            {
                if (IsVisual && parsed.OwnerBlock >= 0)
                    continue;
                _visualLines.Add(new VisualLine(bi, 0, 0, parsed.Kind) { OverrideHeight = _paragraphGap });
                continue;
            }

            var map = IsVisual ? _visualMaps?[bi] : null;

            if (IsVisual && parsed.Kind == BlockKind.ThematicBreak)
            {
                _visualLines.Add(new VisualLine(bi, 0, text.Length, parsed.Kind) { OverrideHeight = 20 });
                continue;
            }

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
        _layoutVersion++;
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
            prefixWidth = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

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

    private const double _tableCellPadding = 8;

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
        double x = 0;
        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);

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

    private double MeasureJoinedRange(ParagraphGroup group, int start, int length)
    {
        return MeasureRangeWidth(group.JoinedText, start, length,
            group.JoinedParsed.Runs, BlockKind.Paragraph, group.JoinedMap);
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
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = _measure.MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
                return offset;
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    private int HitTestInJoinedLine(VisualLine vl, double x)
    {
        var group = vl.Group!;
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
            if (x < accum + charW / 2)
                return offset;
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

    private void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
    {
        if (_visualLines.Count == 0) { blockIndex = 0; charOffset = 0; return; }
        double effectiveScroll = _scroll.EffectiveOffset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _visualLines[vli];
        int rawOffset = HitTestInVisualLine(vli, pos.X - _padding);
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
            _scroll.Offset = cursorY - _padding;
        else if (cursorBottom > _scroll.Offset + ActualHeight - _padding)
            _scroll.Offset = cursorBottom - ActualHeight + _padding;
        _scroll.Clamp();
    }

    // --- Rendering ---

    protected override void OnRender(DrawingContext dc)
    {
        _measure.EnsureMeasured(this);
        dc.DrawRectangle(_palette.Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Mutating layout state here violates WPF's OnRender contract (should only draw, not mutate).
        // Correct placement would be MeasureOverride/ArrangeOverride, but this is stable because
        // ComputeLayout is idempotent and completes before any drawing calls.
        ComputeLayout();

        double effectiveScroll = Math.Round(_scroll.EffectiveOffset);
        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        DrawCodeBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        DrawColorBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        DrawInlineColorBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
        if (IsVisual)
            DrawTableBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

        if (_searchMatches.Count > 0)
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
                    string blockText = _doc.GetBlockText(vl.BlockIndex);
                    var parsed = _parsedBlocks![vl.BlockIndex];
                    double fontSize = _measure.GetBlockFontSize(parsed.Kind);
                    var baseTypeface = TextMeasurer.GetBlockBaseTypeface(parsed.Kind);
                    var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;

                    double textX = _padding;

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
                            if (map.ReplacementPrefix != null && vl.StartOffset == 0)
                            {
                                if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                                {
                                    double nestOff = _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind)
                                        - _measure.ListIndent;
                                    textX += DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                                        _padding, lineY - effectiveScroll, parsed.Kind, nestOff);
                                }
                                else if (parsed.Kind == BlockKind.UnorderedListItem)
                                {
                                    double nestOff = _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind)
                                        - TextMeasurer.ListIndent;
                                    textX += DrawListBullet(dc, _padding, lineY - effectiveScroll,
                                        parsed.Kind, parsed.ListNestingLevel, nestOff);
                                }
                                else if (parsed.Kind == BlockKind.OrderedListItem)
                                {
                                    textX += DrawOrderedListNumber(dc, _padding, lineY - effectiveScroll,
                                        map.ReplacementPrefix!, fontSize, parsed.ListNestingLevel);
                                }
                                else if (map.IsContinuationIndent)
                                {
                                    textX += _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                                }
                                else
                                {
                                    var prefixFt = new FormattedText(map.ReplacementPrefix!,
                                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                        TextMeasurer.NormalTypeface, fontSize, _palette.Syntax, _measure.DpiScale);
                                    dc.DrawText(prefixFt, new Point(_padding, lineY - effectiveScroll));
                                    textX += _measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                                }
                            }

                            string displayText = map.BuildDisplayString(blockText, vl.StartOffset, vl.Length);
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

        if (_spellCheckEnabled)
            DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

        if (_showPageBreaks)
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

        if (HasImagesOnLine(vl, group.JoinedMap))
        {
            DrawVisualLineWithImages(dc, vl, group.JoinedText, group.JoinedParsed,
                group.JoinedMap, lineY, effectiveScroll,
                _measure.GetBlockFontSize(BlockKind.Paragraph), TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph));
            return;
        }

        string displayText = BuildJoinedDisplayString(group, vl.StartOffset, vl.Length);
        if (displayText.Length == 0) return;

        double fontSize = _measure.GetBlockFontSize(BlockKind.Paragraph);
        var baseTypeface = TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph);

        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, baseTypeface, fontSize,
            _palette.Foreground, _measure.DpiScale);
        ApplyInlineStylesVisual(ft, vl, group.JoinedParsed, group.JoinedMap);

        var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
        int visPos = 0;
        for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
        {
            if (group.JoinedMap.IsHidden(i)) continue;
            if (softBreaks.Contains(i) && visPos < displayText.Length)
                ft.SetForegroundBrush(_palette.Syntax, visPos, 1);
            visPos++;
        }

        dc.DrawText(ft, new Point(_padding, lineY - effectiveScroll));
    }

    private string BuildJoinedDisplayString(ParagraphGroup group, int start, int length)
    {
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

    private void ApplySyntaxTokens(FormattedText ft, VisualLine vl, IReadOnlyList<SyntaxToken> tokens)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var token in tokens)
        {
            int tokenEnd = token.Start + token.Length;
            if (tokenEnd <= vl.StartOffset || token.Start >= vlEnd) continue;

            int localStart = Math.Max(0, token.Start - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, tokenEnd - vl.StartOffset);
            int count = localEnd - localStart;
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

    private SolidColorBrush GetCachedBrush(byte r, byte g, byte b)
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

        if (parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0 && vl.Length >= ls + 2)
            ft.SetForegroundBrush(_palette.Syntax, 0, ls + 2);

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
