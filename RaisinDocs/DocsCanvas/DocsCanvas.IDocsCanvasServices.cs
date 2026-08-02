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

    // ====== ILayoutDataServices ======
    List<VisualLine> ILayoutDataServices.VisualLines => _visualLines;
    List<double> ILayoutDataServices.LineYPositions => _lineYPositions;
    List<BlockVisualSpacing>? ILayoutDataServices.VisualLineSpacings => _visualLineSpacings!;
    double ILayoutDataServices.LayoutMaxWidth => _layoutMaxWidth;
    int ILayoutDataServices.LayoutVersion => _layoutVersion;
    double ILayoutDataServices.GetEffectiveLineHeight(VisualLine vl) => GetEffectiveLineHeight(vl);
    void ILayoutDataServices.InvalidateLayout() => InvalidateLayout();
    void ILayoutDataServices.ComputeLayout() => ComputeLayout();
    BlockVisualSpacing? ILayoutDataServices.GetVisualLineSpacing(VisualLine vl) => GetVisualLineSpacing(vl);

    // ====== IRenderingServices ======
    ThemePalette IRenderingServices.Palette => _palette;
    TextMeasurer IRenderingServices.Measure => _measure;
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
    List<ParsedBlock>? IParsedContentServices.ParsedBlocks => _parsedBlocks;
    List<BlockVisualMap>? IParsedContentServices.VisualMaps => _visualMaps;
    VisualBlockStructure? IParsedContentServices.VisualBlockStructure => _visualBlockStructure;

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
    int ISearchServices.TestSearchMatchCount => 0;  // TODO: implement based on FindBar if needed

    // ====== ILoggingServices ======
    IDocsLogger? ILoggingServices.Logger => Logger;

    // ====== ICanvasOperations ======
    Dispatcher ICanvasOperations.Dispatcher => Dispatcher;
    IMinimapDataProvider? ICanvasOperations.Minimap => (IMinimapDataProvider?)Minimap;
    event Action? ICanvasOperations.ScrollStateChanged
    {
        add { ScrollStateChanged += value; }
        remove { ScrollStateChanged -= value; }
    }
    void ICanvasOperations.SealAndStopTimer() => SealAndStopTimer();
}
