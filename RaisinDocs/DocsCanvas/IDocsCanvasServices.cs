using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

/// <summary>
/// Dependency interfaces for DocsCanvas extracted classes.
/// These interfaces define clean contracts for what each extracted class needs from DocsCanvas.
/// Enables testing, loose coupling, and clear separation of concerns.
/// All interfaces are internal - used only within DocsCanvas architecture.
/// </summary>

/// <summary>
/// Access to the Document model: text storage, cursor, selection, mutations, and undo/redo.
/// Used by: LayoutEngine, CursorNavigationEngine, FindAndReplaceController, SpellCheckController, EditingKeysHandler
/// </summary>
internal interface IDocumentServices
{
    Document Document { get; }
    int BlockCount { get; }
    string GetBlockText(int blockIndex);
    int GetBlockLength(int blockIndex);

    // Undo/Redo support
    void Undo();
    void Redo();
    void BeginUndoGroup();
    void SealUndoGroup();
}

/// <summary>
/// Access to layout results: visual lines, line positions, and cached spacing data.
/// Computed by LayoutEngine, consumed by RenderingContext and CursorNavigationEngine.
/// </summary>
internal interface ILayoutDataServices
{
    List<DocsCanvas.VisualLine> VisualLines { get; }
    List<double> LineYPositions { get; }
    List<BlockVisualSpacing>? VisualLineSpacings { get; set; }
    double LayoutMaxWidth { get; set; }
    int LayoutVersion { get; set; }
    double TotalContentHeight { get; set; }
    bool LayoutDirty { get; set; }
    Dictionary<int, DocsCanvas.ParagraphGroup>? BlockToGroup { get; set; }
    double GetEffectiveLineHeight(DocsCanvas.VisualLine vl);
    double GetTextStartXForVisualLine(DocsCanvas.VisualLine vl);
    void InvalidateLayout();
    void ComputeLayout();
    BlockVisualSpacing? GetVisualLineSpacing(DocsCanvas.VisualLine vl);

    // Test-only properties for testing and internal use by PageBreakManager
    int TestLayoutVersion { get; }
    int TestVisualLineCount { get; }
    List<double> TestLineYPositions { get; }
    List<DocsCanvas.VisualLine> TestVisualLines { get; }
    List<ParsedBlock>? TestParsedBlocks { get; }
    TextMeasurer TestMeasure { get; }
    double GetEffectiveLineHeightPublic(DocsCanvas.VisualLine vl);
}

/// <summary>
/// Rendering support: palette (colors/brushes), text measurement, caching, and line height queries.
/// Used by: RenderingContext, TableRenderer, CursorNavigationEngine, SpellCheckController, FindAndReplaceController, LayoutEngine
/// </summary>
internal interface IRenderingServices
{
    DocsCanvas.ThemePalette Palette { get; }
    TextMeasurer Measure { get; }
    SyntaxHighlighter SyntaxHighlighter { get; }
    double ActualWidth { get; }
    double ActualHeight { get; }
    void InvalidateVisual();

    /// <summary>Gets or creates a cached solid color brush.</summary>
    SolidColorBrush GetCachedBrush(byte r, byte g, byte b);

    /// <summary>Measures the width of a text range accounting for styles and visual maps.</summary>
    double MeasureRangeWidth(string text, int start, int length,
        IReadOnlyList<StyledRun> runs, BlockKind blockKind, BlockVisualMap? map);

    /// <summary>Measures the width of a range in a joined/merged paragraph group.</summary>
    double MeasureJoinedRange(DocsCanvas.ParagraphGroup group, int start, int length);
}

/// <summary>
/// Access to parsed markdown content and visual display data.
/// Set by MarkdownParser, consumed by rendering and layout.
/// </summary>
internal interface IParsedContentServices
{
    List<ParsedBlock>? ParsedBlocks { get; set; }
    List<BlockVisualMap>? VisualMaps { get; set; }
    VisualBlockStructure? VisualBlockStructure { get; set; }
}

/// <summary>
/// Visual mode specific services: hidden range navigation and visual maps.
/// Used by: VisualModeManager, CursorNavigationEngine
/// </summary>
internal interface IVisualModeServices
{
    List<BlockVisualMap>? VisualMaps { get; }
    bool IsVisual { get; }
    void SkipCursorOverHiddenRanges(bool forward);
    void ClampCursorAwayFromHidden();
    void ClampCursorBeforeTrailingHidden();
    void EnsureCursorOnVisibleBlock(bool? preferForward);
}

/// <summary>
/// Table rendering and interaction: column widths, drawing, and hit-testing.
/// Provides access to TableRenderer and table-related methods.
/// Used by: RenderingContext, CursorNavigationEngine, FindAndReplaceController, SpellCheckController
/// </summary>
internal interface ITableServices
{
    Dictionary<TableInfo, double[]> TableColumnWidths { get; }
    DocsCanvas.TableRenderer TableRenderer { get; }
    double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset);
    int HitTestInTableRow(DocsCanvas.VisualLine vl, ParsedBlock parsed, double[] colWidths, double x);
}

/// <summary>
/// Cursor navigation support: hit-testing, visual line mapping, and navigation queries.
/// Used by: LinkHandler, FindAndReplaceController, SpellCheckController, RenderingContext
/// </summary>
internal interface INavigationServices
{
    List<DocsCanvas.VisualLine> VisualLines { get; }
    List<double> LineYPositions { get; }
    void HitTestToPosition(Point pos, out int blockIndex, out int charOffset);
    int HitTestVisualLine(double y);
    void ApplyInlineStyles(System.Windows.Media.FormattedText ft, DocsCanvas.VisualLine vl, ParsedBlock parsed, string blockText);
}

/// <summary>
/// Scrolling and viewport management.
/// Used by: CursorNavigationEngine, RenderingContext
/// </summary>
internal interface IScrollServices
{
    ScrollController Scroll { get; }
    void EnsureCursorVisible();
}

/// <summary>
/// Image rendering and caching support.
/// Used by: LayoutEngine, RenderingContext, CursorNavigationEngine
/// </summary>
internal interface IImageServices
{
    DocsCanvas.ImagePreviewMode ImagePreview { get; }
    ImageCache ImageCache { get; }
    string? DocumentBasePath { get; }
    (double Width, double Height) GetImageSize(InlineImage img, double maxWidth);
}

/// <summary>
/// Search and highlighting support for find/replace functionality.
/// Used by: RenderingContext, FindAndReplaceController
/// </summary>
internal interface ISearchServices
{
    FindBarController? FindBar { get; }

    /// <summary>
    /// True when a search is active with at least one match, so the highlight pass is worth running.
    /// Must not force construction of the find/replace controller.
    /// </summary>
    bool HasSearchHighlights { get; }
}

/// <summary>
/// Logging support for diagnostics and debugging.
/// Used by: LayoutEngine, CursorNavigationEngine, VisualModeManager, RenderingContext
/// </summary>
internal interface ILoggingServices
{
    IDocsLogger? Logger { get; }
}

/// <summary>
/// Editing operations: backspace, delete, undo, redo, and undo grouping management.
/// Provides access to editing-specific functionality and state management.
/// Used by: EditingKeysHandler
/// </summary>
internal interface IEditingServices
{
    /// <summary>Gets whether the current edit mode is visual (vs source).</summary>
    bool IsVisual { get; }

    /// <summary>Gets the last action kind for undo grouping purposes.</summary>
    LastActionKind LastAction { get; set; }

    /// <summary>Tries to get a rectangular table selection (if in a table with Shift+Arrow drag).</summary>
    (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)? TryGetTableRectSelection();

    /// <summary>Clears (deletes content of) table cells in a rectangular range.</summary>
    void ClearTableRectCells((int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect);

    /// <summary>Handles backspace in visual mode. Returns true if text was changed.</summary>
    bool HandleBackVisual();

    /// <summary>Handles backspace in source mode. Returns true if text was changed.</summary>
    bool HandleBackSource();

    /// <summary>Handles delete in visual mode. Returns true if text was changed.</summary>
    bool HandleDeleteVisual();

    /// <summary>Handles delete in source mode. Returns true if text was changed.</summary>
    bool HandleDeleteSource();

    /// <summary>Resets the undo seal timer to allow grouping of consecutive editing actions.</summary>
    void ResetUndoSealTimer();

    /// <summary>Stops the undo seal timer to finalize the current undo group.</summary>
    void StopUndoSealTimer();
}

/// <summary>
/// Core canvas operations: state management, rendering control, UI components.
/// Used by: RenderingContext, FindAndReplaceController, and extracted classes for operations
/// </summary>
internal interface ICanvasOperations
{
    System.Windows.Threading.Dispatcher Dispatcher { get; }
    IMinimapDataProvider? Minimap { get; }
    bool CursorAtLineEnd { get; set; }
    event System.Action? ScrollStateChanged;
    void SealAndStopTimer();
    void RaiseFormattingChanged();
    void StyleMenuItem(System.Windows.Controls.MenuItem item);
    void FocusCanvas();
}

/// <summary>
/// Composite interface grouping all DocsCanvas services.
/// Implementations should provide access to all core services needed by extracted classes.
/// Used as primary dependency by most classes (LayoutEngine, RenderingContext, etc.).
/// </summary>
internal interface IDocsCanvasServices :
    IDocumentServices,
    ILayoutDataServices,
    IRenderingServices,
    IParsedContentServices,
    IVisualModeServices,
    ITableServices,
    INavigationServices,
    IScrollServices,
    IImageServices,
    ISearchServices,
    ILoggingServices,
    IEditingServices,
    ICanvasOperations
{
    // Composite interface - combines all specialized service interfaces
}
