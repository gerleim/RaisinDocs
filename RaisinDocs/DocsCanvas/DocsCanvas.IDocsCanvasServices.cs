using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

/// <summary>
/// Explicit interface implementations for IDocsCanvasServices and all component interfaces.
/// This allows DocsCanvas to provide access to its internal services through a clean contract.
/// </summary>
public partial class DocsCanvas
{
    // ====== IDocumentServices ======
    Document IDocumentServices.Document => _doc;
    int IDocumentServices.BlockCount => BlockCount;
    string IDocumentServices.GetBlockText(int blockIndex) => _doc.GetBlockText(blockIndex);
    int IDocumentServices.GetBlockLength(int blockIndex) => _doc.GetBlockLength(blockIndex);
    void IDocumentServices.Undo() => _doc.Undo();
    void IDocumentServices.Redo() => _doc.Redo();
    void IDocumentServices.BeginUndoGroup() => _doc.BeginUndoGroup();
    void IDocumentServices.SealUndoGroup() => _doc.SealUndoGroup();

    // ====== ILayoutDataServices ======
    List<VisualLine> ILayoutDataServices.VisualLines => _visualLines;
    List<double> ILayoutDataServices.LineYPositions => _lineYPositions;
    List<BlockVisualSpacing>? ILayoutDataServices.VisualLineSpacings
    {
        get => _visualLineSpacings!;
        set => _visualLineSpacings = value;
    }
    double ILayoutDataServices.LayoutMaxWidth
    {
        get => _layoutMaxWidth;
        set => _layoutMaxWidth = value;
    }
    int ILayoutDataServices.LayoutVersion
    {
        get => _layoutVersion;
        set => _layoutVersion = value;
    }
    double ILayoutDataServices.TotalContentHeight
    {
        get => _totalContentHeight;
        set => _totalContentHeight = value;
    }
    bool ILayoutDataServices.LayoutDirty
    {
        get => _layoutDirty;
        set => _layoutDirty = value;
    }
    Dictionary<int, ParagraphGroup>? ILayoutDataServices.BlockToGroup
    {
        get => _blockToGroup;
        set => _blockToGroup = value;
    }
    double ILayoutDataServices.GetEffectiveLineHeight(VisualLine vl) => GetEffectiveLineHeight(vl);
    void ILayoutDataServices.InvalidateLayout() => InvalidateLayout();
    void ILayoutDataServices.ComputeLayout() => ComputeLayout();
    BlockVisualSpacing? ILayoutDataServices.GetVisualLineSpacing(VisualLine vl) => GetVisualLineSpacing(vl);

    // Test-only properties for ILayoutDataServices
    int ILayoutDataServices.TestLayoutVersion => _layoutVersion;
    int ILayoutDataServices.TestVisualLineCount => _visualLines.Count;
    List<double> ILayoutDataServices.TestLineYPositions => _lineYPositions;
    List<VisualLine> ILayoutDataServices.TestVisualLines => _visualLines;
    List<ParsedBlock>? ILayoutDataServices.TestParsedBlocks => _parsedBlocks;
    TextMeasurer ILayoutDataServices.TestMeasure => _measure;
    double ILayoutDataServices.GetEffectiveLineHeightPublic(VisualLine vl) => GetEffectiveLineHeight(vl);

    // ====== IRenderingServices ======
    ThemePalette IRenderingServices.Palette => _palette;
    TextMeasurer IRenderingServices.Measure => _measure;
    SyntaxHighlighter IRenderingServices.SyntaxHighlighter => _syntaxHighlighter;
    double IRenderingServices.ActualWidth => ActualWidth;
    double IRenderingServices.ActualHeight => ActualHeight;
    void IRenderingServices.InvalidateVisual() => InvalidateVisual();
    SolidColorBrush IRenderingServices.GetCachedBrush(byte r, byte g, byte b) => GetCachedBrush(r, g, b);
    double IRenderingServices.MeasureRangeWidth(string text, int start, int length,
        IReadOnlyList<StyledRun> runs, BlockKind blockKind, BlockVisualMap? map)
        => MeasureRangeWidth(text, start, length, runs, blockKind, map);
    double IRenderingServices.MeasureJoinedRange(ParagraphGroup group, int start, int length)
        => MeasureJoinedRange(group, start, length);

    // ====== IParsedContentServices ======
    List<ParsedBlock>? IParsedContentServices.ParsedBlocks
    {
        get => _parsedBlocks;
        set => _parsedBlocks = value;
    }
    List<BlockVisualMap>? IParsedContentServices.VisualMaps
    {
        get => _visualMaps;
        set => _visualMaps = value;
    }
    VisualBlockStructure? IParsedContentServices.VisualBlockStructure
    {
        get => _visualBlockStructure;
        set => _visualBlockStructure = value;
    }

    // ====== IVisualModeServices ======
    List<BlockVisualMap>? IVisualModeServices.VisualMaps => _visualMaps;
    bool IVisualModeServices.IsVisual => IsVisual;
    void IVisualModeServices.SkipCursorOverHiddenRanges(bool forward) => SkipCursorOverHiddenRanges(forward);
    void IVisualModeServices.ClampCursorAwayFromHidden() => ClampCursorAwayFromHidden();
    void IVisualModeServices.ClampCursorBeforeTrailingHidden() => ClampCursorBeforeTrailingHidden();
    void IVisualModeServices.EnsureCursorOnVisibleBlock(bool? preferForward) => EnsureCursorOnVisibleBlock(preferForward);

    // ====== ITableServices ======
    Dictionary<TableInfo, double[]> ITableServices.TableColumnWidths => _tableColumnWidths;
    TableRenderer ITableServices.TableRenderer => _tableRenderer;
    double ITableServices.CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
        => CursorXInTableRow(blockIndex, parsed, colWidths, cursorOffset);
    int ITableServices.HitTestInTableRow(VisualLine vl, ParsedBlock parsed, double[] colWidths, double x)
        => HitTestInTableRow(vl, parsed, colWidths, x);

    // ====== INavigationServices ======
    List<VisualLine> INavigationServices.VisualLines => _visualLines;
    List<double> INavigationServices.LineYPositions => _lineYPositions;
    void INavigationServices.HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
        => HitTestToPosition(pos, out blockIndex, out charOffset);
    int INavigationServices.HitTestVisualLine(double y) => HitTestVisualLine(y);
    void INavigationServices.ApplyInlineStyles(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        => ApplyInlineStyles(ft, vl, parsed, blockText);

    // ====== IScrollServices ======
    ScrollController IScrollServices.Scroll => _scroll;
    void IScrollServices.EnsureCursorVisible() => EnsureCursorVisible();

    // ====== IImageServices ======
    ImagePreviewMode IImageServices.ImagePreview => CurrentImagePreview;
    ImageCache IImageServices.ImageCache => _imageCache;
    string? IImageServices.DocumentBasePath => DocumentBasePath;
    (double Width, double Height) IImageServices.GetImageSize(InlineImage img, double maxWidth) => GetImageSize(img, maxWidth);

    // ====== ISearchServices ======
    FindBarController? ISearchServices.FindBar => FindBar;
    // Reads the controller only if it already exists - never force the lazy FindAndReplace
    // property, which would allocate on the first render of every canvas.
    bool ISearchServices.HasSearchHighlights => _findAndReplaceController?.HasHighlights ?? false;

    // ====== ILoggingServices ======
    IDocsLogger? ILoggingServices.Logger => Logger;

    // ====== IEditingServices ======
    bool IEditingServices.IsVisual => IsVisual;
    LastActionKind IEditingServices.LastAction
    {
        get => _lastAction;
        set => _lastAction = value;
    }
    (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)? IEditingServices.TryGetTableRectSelection() => TryGetTableRectSelection();
    void IEditingServices.ClearTableRectCells((int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect) => ClearTableRectCells(rect);
    bool IEditingServices.HandleBackVisual() => HandleBackVisual();
    bool IEditingServices.HandleBackSource() => HandleBackSource();
    bool IEditingServices.HandleDeleteVisual() => HandleDeleteVisual();
    bool IEditingServices.HandleDeleteSource() => HandleDeleteSource();
    void IEditingServices.ResetUndoSealTimer() => ResetUndoSealTimer();
    void IEditingServices.StopUndoSealTimer() => _undoSealTimer.Stop();

    // ====== ICanvasOperations ======
    Dispatcher ICanvasOperations.Dispatcher => Dispatcher;
    IMinimapDataProvider? ICanvasOperations.Minimap => (IMinimapDataProvider?)Minimap;
    event Action? ICanvasOperations.ScrollStateChanged
    {
        add { ScrollStateChanged += value; }
        remove { ScrollStateChanged -= value; }
    }
    void ICanvasOperations.SealAndStopTimer() => SealAndStopTimer();
    void ICanvasOperations.RaiseFormattingChanged() => FormattingChanged?.Invoke(this, EventArgs.Empty);
}
