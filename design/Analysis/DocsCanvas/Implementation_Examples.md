# DocsCanvas Refactoring: Implementation Examples

This document provides concrete code examples for implementing the proposed refactoring.

---

## Example 1: Extracting PageBreakManager (Phase 1 - Quick Win)

### Before

```csharp
// DocsCanvas.Print.cs (598 lines mixed in)
partial class DocsCanvas
{
    private bool _showPageBreaks;
    private readonly List<double> _pageBreakYs = [];
    private int _pageBreakLayoutVersion = -1;
    
    public bool ShowPageBreaks => _showPageBreaks;
    
    public void SetShowPageBreaks(bool show)
    {
        if (_showPageBreaks == show) return;
        _showPageBreaks = show;
        InvalidateVisual();
    }
    
    private void ComputePageBreakPositions()
    {
        if (_pageBreakLayoutVersion == _layoutVersion) return;
        _pageBreakLayoutVersion = _layoutVersion;
        _pageBreakYs.Clear();
        
        // 40+ lines of page break computation
    }
    
    // Usage scattered throughout OnRender
    protected override void OnRender(DrawingContext dc)
    {
        ComputePageBreakPositions();
        foreach (var breakY in _pageBreakYs)
        {
            // Draw page break line
        }
    }
}
```

### After

```csharp
// NEW FILE: DocsCanvas/PageBreakManager.cs

internal class PageBreakManager
{
    private List<double> _pageBreaks = [];
    private int _cachedLayoutVersion = -1;
    private bool _enabled;
    
    public bool Enabled => _enabled;
    public IReadOnlyList<double> PageBreaks => _pageBreaks;
    
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled) _pageBreaks.Clear();
    }
    
    /// <summary>
    /// Compute page break positions based on current layout.
    /// Must be called when layout changes (layoutVersion != cachedVersion).
    /// </summary>
    public void Update(
        int layoutVersion,
        IReadOnlyList<VisualLine> visualLines,
        IReadOnlyList<double> lineYPositions,
        double pageHeight)
    {
        if (!_enabled || layoutVersion == _cachedLayoutVersion || visualLines.Count == 0)
            return;
        
        _cachedLayoutVersion = layoutVersion;
        _pageBreaks.Clear();
        
        const double DefaultPageHeight = 11 * 72; // 11 inches in points
        double pageContentH = pageHeight - DocsPaginator.MarginY * 2;
        
        double currentPageBottom = pageContentH;
        
        for (int i = 0; i < visualLines.Count; i++)
        {
            double lineY = lineYPositions[i];
            if (lineY >= currentPageBottom && i > 0)
            {
                _pageBreaks.Add(lineYPositions[i - 1]);
                currentPageBottom += pageContentH;
            }
        }
    }
}

// MODIFIED: DocsCanvas.cs

partial class DocsCanvas
{
    private PageBreakManager _pageBreakManager = new();
    
    public bool ShowPageBreaks
    {
        get => _pageBreakManager.Enabled;
        set => _pageBreakManager.SetEnabled(value);
    }
    
    public void SetShowPageBreaks(bool show)
    {
        _pageBreakManager.SetEnabled(show);
        InvalidateVisual();
    }
    
    internal List<double> TestGetPageBreakYs()
    {
        _pageBreakManager.SetEnabled(true);
        _pageBreakManager.Update(_layoutVersion, _visualLines, _lineYPositions, DefaultPageHeight);
        return new List<double>(_pageBreakManager.PageBreaks);
    }
    
    private void ComputePageBreakPositions()
    {
        _pageBreakManager.Update(_layoutVersion, _visualLines, _lineYPositions, DefaultPageHeight);
    }
    
    // In OnRender:
    protected override void OnRender(DrawingContext dc)
    {
        // ... existing render code ...
        
        ComputePageBreakPositions();
        
        if (_pageBreakManager.Enabled)
        {
            DrawPageBreaks(dc, _pageBreakManager.PageBreaks);
        }
    }
    
    private void DrawPageBreaks(DrawingContext dc, IReadOnlyList<double> breakYs)
    {
        // Draw page break lines
    }
}
```

**Benefits**:
- 598 lines removed from DocsCanvas context
- PageBreakManager is independently testable
- No behavior changes, just reorganization
- Easy rollback if needed
- Reduces DocsCanvas.Print.cs to 0 lines (file can be removed)

**Testing**:
```csharp
[TestClass]
public class PageBreakManagerTests
{
    [TestMethod]
    public void Update_WhenDisabled_DoesNothing()
    {
        var manager = new PageBreakManager();
        manager.SetEnabled(false);
        manager.Update(1, visualLines, linePositions, 792); // 11 inches
        
        Assert.AreEqual(0, manager.PageBreaks.Count);
    }
    
    [TestMethod]
    public void Update_ComputesPageBreaksCorrectly()
    {
        var manager = new PageBreakManager();
        manager.SetEnabled(true);
        
        var visualLines = new[]
        {
            new VisualLine { BlockIndex = 0, StartOffset = 0, Length = 10 },
            new VisualLine { BlockIndex = 1, StartOffset = 0, Length = 15 }, // 72pt
            new VisualLine { BlockIndex = 2, StartOffset = 0, Length = 20 }, // 200pt (crosses page)
        };
        var linePositions = new[] { 0.0, 72.0, 200.0 };
        
        manager.Update(1, visualLines, linePositions, 792);
        
        Assert.AreEqual(1, manager.PageBreaks.Count);
        Assert.IsTrue(manager.PageBreaks.Contains(72.0));
    }
}
```

---

## Example 2: Extracting FindAndReplaceController (Phase 1 - Quick Win)

### Before

```csharp
// DocsCanvas.Find.cs (289 lines mixed in)
partial class DocsCanvas
{
    internal readonly record struct SearchMatch(int Block, int Offset, int Length);
    
    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";
    private bool _lastSearchCaseSensitive;
    private bool _searchDirty;
    
    internal void ExecuteSearch(string query, bool caseSensitive)
    {
        _searchMatches.Clear();
        _lastSearchQuery = query;
        _lastSearchCaseSensitive = caseSensitive;
        _searchDirty = false;
        
        if (string.IsNullOrEmpty(query))
        {
            _currentMatchIndex = -1;
            FindBar?.UpdateMatchInfo(-1, 0);
            InvalidateVisual();
            return;
        }
        
        // 50+ lines of search logic
    }
    
    // OnRender needs to highlight matches
    protected override void OnRender(DrawingContext dc)
    {
        // ... highlight search matches ...
    }
}
```

### After

```csharp
// NEW FILE: DocsCanvas/FindAndReplaceController.cs

internal class FindAndReplaceController
{
    public readonly record struct SearchMatch(int Block, int Offset, int Length);
    
    private List<SearchMatch> _matches = [];
    private int _currentIndex = -1;
    private string _lastQuery = "";
    private bool _lastCaseSensitive;
    private Document? _document;
    
    public IReadOnlyList<SearchMatch> Matches => _matches;
    public int CurrentMatchIndex => _currentIndex;
    public bool HasMatches => _matches.Count > 0;
    
    // Fired when matches change (to trigger re-render)
    public event Action<IReadOnlyList<SearchMatch>>? MatchesChanged;
    
    /// <summary>
    /// Search for all occurrences of query in document.
    /// </summary>
    public void ExecuteSearch(Document document, string query, bool caseSensitive)
    {
        _document = document;
        _matches.Clear();
        _lastQuery = query;
        _lastCaseSensitive = caseSensitive;
        _currentIndex = -1;
        
        if (string.IsNullOrEmpty(query))
        {
            MatchesChanged?.Invoke(_matches);
            return;
        }
        
        var comparison = caseSensitive 
            ? StringComparison.Ordinal 
            : StringComparison.OrdinalIgnoreCase;
        
        for (int block = 0; block < document.BlockCount; block++)
        {
            string text = document.GetBlockText(block);
            int offset = 0;
            
            while (offset <= text.Length - query.Length)
            {
                int index = text.IndexOf(query, offset, comparison);
                if (index < 0) break;
                
                _matches.Add(new SearchMatch(block, index, query.Length));
                offset = index + 1; // Allow overlapping matches
            }
        }
        
        _currentIndex = _matches.Count > 0 ? 0 : -1;
        MatchesChanged?.Invoke(_matches);
    }
    
    /// <summary>
    /// Navigate to next/previous match and return it.
    /// </summary>
    public SearchMatch? GoToNextMatch()
    {
        if (!HasMatches) return null;
        _currentIndex = (_currentIndex + 1) % _matches.Count;
        return _matches[_currentIndex];
    }
    
    public SearchMatch? GoToPreviousMatch()
    {
        if (!HasMatches) return null;
        _currentIndex = (_currentIndex - 1 + _matches.Count) % _matches.Count;
        return _matches[_currentIndex];
    }
    
    /// <summary>
    /// Replace current match and return next match.
    /// </summary>
    public SearchMatch? ReplaceCurrentMatch(Document document, string replacement)
    {
        if (_currentIndex < 0 || _currentIndex >= _matches.Count)
            return null;
        
        var match = _matches[_currentIndex];
        document.RemoveTextAt(match.Block, match.Offset, match.Length);
        document.InsertTextAt(match.Block, match.Offset, replacement);
        
        // Adjust match positions after replacement
        var lengthDiff = replacement.Length - match.Length;
        for (int i = _currentIndex + 1; i < _matches.Count; i++)
        {
            if (_matches[i].Block == match.Block && _matches[i].Offset > match.Offset)
            {
                var adjusted = _matches[i];
                _matches[i] = adjusted with { Offset = adjusted.Offset + lengthDiff };
            }
        }
        
        _matches.RemoveAt(_currentIndex);
        _currentIndex = Math.Min(_currentIndex, _matches.Count - 1);
        
        return _currentIndex >= 0 ? _matches[_currentIndex] : null;
    }
    
    public void ReplaceAll(Document document, string replacement)
    {
        if (!HasMatches) return;
        
        // Work backwards to avoid index shifting
        for (int i = _matches.Count - 1; i >= 0; i--)
        {
            var match = _matches[i];
            document.RemoveTextAt(match.Block, match.Offset, match.Length);
            document.InsertTextAt(match.Block, match.Offset, replacement);
        }
        
        _matches.Clear();
        _currentIndex = -1;
        MatchesChanged?.Invoke(_matches);
    }
    
    public void Clear()
    {
        _matches.Clear();
        _currentIndex = -1;
        MatchesChanged?.Invoke(_matches);
    }
}

// MODIFIED: DocsCanvas.cs

partial class DocsCanvas
{
    private FindAndReplaceController _findReplace = new();
    
    public FindAndReplaceController.SearchMatch? CurrentSearchMatch 
        => _findReplace.CurrentMatchIndex >= 0 
            ? _findReplace.Matches[_findReplace.CurrentMatchIndex] 
            : null;
    
    internal void ExecuteSearch(string query, bool caseSensitive)
    {
        _findReplace.ExecuteSearch(_doc, query, caseSensitive);
        InvalidateVisual();
        
        // Update find bar UI
        FindBar?.UpdateMatchInfo(
            _findReplace.CurrentMatchIndex + 1, 
            _findReplace.Matches.Count);
        
        // Scroll to first match if found
        if (_findReplace.HasMatches)
        {
            var first = _findReplace.Matches[0];
            _doc.CursorBlock = first.Block;
            _doc.CursorOffset = first.Offset;
            EnsureCursorVisible();
        }
    }
    
    internal void ReplaceCurrentMatch(string replacement)
    {
        var next = _findReplace.ReplaceCurrentMatch(_doc, replacement);
        InvalidateLayout();
        InvalidateVisual();
        
        if (next.HasValue)
        {
            _doc.CursorBlock = next.Value.Block;
            _doc.CursorOffset = next.Value.Offset;
            EnsureCursorVisible();
        }
    }
    
    // In OnRender, highlight matches:
    private void HighlightSearchMatches(DrawingContext dc, double effectiveScroll)
    {
        if (!_findReplace.HasMatches) return;
        
        foreach (var match in _findReplace.Matches)
        {
            // Highlight this match
            // (existing code, now uses _findReplace.Matches instead of _searchMatches)
        }
    }
}
```

**Benefits**:
- SearchMatch is now reusable
- Find logic testable independently
- Clear separation of concerns (find vs. display)
- Multiple find controllers can coexist if needed

**Testing**:
```csharp
[TestClass]
public class FindAndReplaceControllerTests
{
    [TestMethod]
    public void ExecuteSearch_FindsAllMatches()
    {
        var doc = new Document();
        doc.SetText("hello world hello");
        
        var controller = new FindAndReplaceController();
        controller.ExecuteSearch(doc, "hello", caseSensitive: true);
        
        Assert.AreEqual(2, controller.Matches.Count);
        Assert.AreEqual(0, controller.Matches[0].Offset);
        Assert.AreEqual(12, controller.Matches[1].Offset);
    }
    
    [TestMethod]
    public void ReplaceCurrentMatch_UpdatesDocument()
    {
        var doc = new Document();
        doc.SetText("hello world hello");
        
        var controller = new FindAndReplaceController();
        controller.ExecuteSearch(doc, "hello", caseSensitive: true);
        controller.ReplaceCurrentMatch(doc, "hi");
        
        Assert.AreEqual("hi world hello", doc.GetText());
        Assert.AreEqual(1, controller.Matches.Count); // One match replaced
    }
}
```

---

## Example 3: Creating RenderingContext Shared State (Phase 3 - Foundation)

### The Problem

```csharp
// Current: Passing 15+ parameters through method calls
private void DrawJoinedLine(
    DrawingContext dc, VisualLine vl,
    int vlIndex, ParsedBlock parsed, string blockText,
    double effectiveScroll, double startX, double startY,
    BlockVisualSpacing? spacing, BlockVisualMap? map,
    List<SyntaxToken>? tokens, List<InlineImage>? images,
    List<InlineLink>? links, IReadOnlyList<ColorSpan>? colors,
    ...)
{
    // Uses 15 parameters in this method alone
}

private void ApplyInlineStyles(
    FormattedText ft, VisualLine vl, ParsedBlock parsed,
    string blockText, BlockVisualMap? map, ...)
{
    // Uses 5 of those 15
}

// This explodes complexity and readability
```

### The Solution

```csharp
// NEW FILE: DocsCanvas/RenderingContext.cs

/// <summary>
/// Immutable context containing all data needed for rendering.
/// This reduces parameter passing and makes dependencies explicit.
/// </summary>
internal record class RenderingContext(
    IReadOnlyList<VisualLine> VisualLines,
    IReadOnlyList<double> LineYPositions,
    IReadOnlyList<ParsedBlock> ParsedBlocks,
    IReadOnlyList<BlockVisualMap> VisualMaps,
    IReadOnlyList<BlockVisualSpacing?> VisualLineSpacings,
    IReadOnlyDictionary<TableInfo, double[]> TableColumnWidths,
    TextMeasurer Measure,
    ThemePalette Palette,
    Document Document,
    ScrollController Scroll,
    int LayoutVersion,
    EditMode EditMode,
    SoftBreakMode SoftBreak,
    HardBreakStyle HardBreak,
    bool ShowWhitespace,
    SyntaxHighlighter SyntaxHighlighter)
{
    public ParsedBlock GetParsedBlock(int index) => ParsedBlocks[index];
    public BlockVisualMap? GetVisualMap(int index) => VisualMaps.Count > index ? VisualMaps[index] : null;
    public BlockVisualSpacing? GetVisualSpacing(int index) => VisualLineSpacings.Count > index ? VisualLineSpacings[index] : null;
    public double GetLineY(int visualLineIndex) => LineYPositions[visualLineIndex];
    public bool IsTableRow(int blockIndex) => GetParsedBlock(blockIndex).Kind switch {
        BlockKind.TableRow or BlockKind.TableSeparatorRow => true,
        _ => false
    };
}

// MODIFIED: DocsCanvas.cs

partial class DocsCanvas : FrameworkElement
{
    private RenderingContext CreateRenderingContext()
    {
        ComputeLayout(); // Ensure layout is current
        
        return new RenderingContext(
            VisualLines: _visualLines,
            LineYPositions: _lineYPositions,
            ParsedBlocks: _parsedBlocks ?? new(),
            VisualMaps: _visualMaps ?? new(),
            VisualLineSpacings: _visualLineSpacings ?? new(),
            TableColumnWidths: _tableColumnWidths,
            Measure: _measure,
            Palette: _palette,
            Document: _doc,
            Scroll: _scroll,
            LayoutVersion: _layoutVersion,
            EditMode: _editMode,
            SoftBreak: _softBreak,
            HardBreak: _hardBreak,
            ShowWhitespace: _showWhitespace,
            SyntaxHighlighter: _syntaxHighlighter);
    }
    
    protected override void OnRender(DrawingContext dc)
    {
        // ... existing code ...
        
        var renderCtx = CreateRenderingContext();
        DrawContent(dc, renderCtx);
    }
    
    private void DrawContent(DrawingContext dc, RenderingContext ctx)
    {
        double effectiveScroll = ctx.Scroll.EffectiveOffset;
        
        // All drawing methods now take renderCtx instead of 15 parameters
        DrawCodeBlockBackgrounds(dc, renderCtx);
        DrawColorBlockBackgrounds(dc, renderCtx);
        DrawInlineColorBackgrounds(dc, renderCtx);
        DrawTableBackgrounds(dc, renderCtx);
        
        for (int vli = 0; vli < ctx.VisualLines.Count; vli++)
        {
            DrawVisualLine(dc, vli, renderCtx);
        }
        
        DrawSelection(dc, renderCtx);
        DrawSpellCheckUnderlines(dc, renderCtx);
        HighlightSearchMatches(dc, renderCtx);
        DrawPageBreaks(dc, renderCtx);
        DrawCursor(dc, renderCtx);
    }
    
    // Before: 15 parameters
    // After: 2 parameters
    private void DrawVisualLine(DrawingContext dc, int vlIndex, RenderingContext ctx)
    {
        var vl = ctx.VisualLines[vlIndex];
        var parsed = ctx.GetParsedBlock(vl.BlockIndex);
        var map = ctx.GetVisualMap(vl.BlockIndex);
        
        // Now much cleaner - can access any data through ctx
        
        if (ctx.IsTableRow(vl.BlockIndex))
        {
            DrawTableRow(dc, vl, ctx);
        }
        else
        {
            DrawNormalLine(dc, vl, ctx);
        }
    }
    
    private void DrawNormalLine(DrawingContext dc, VisualLine vl, RenderingContext ctx)
    {
        // Access everything through ctx
        var parsed = ctx.GetParsedBlock(vl.BlockIndex);
        string blockText = ctx.Document.GetBlockText(vl.BlockIndex);
        // ... etc
    }
}
```

**Benefits**:
- Dramatically reduced parameter chaining
- Makes dependencies explicit (what does rendering need?)
- Easier to test (pass mock RenderingContext)
- Easier to refactor (changes to shared state affect one place)
- Can be passed to extracted classes (TableRenderer, LinkHandler, etc.)

---

## Example 4: Creating LayoutEngine (Phase 3 - Core Refactoring)

### Current Tightly Coupled State

```csharp
// In DocsCanvas.cs - Layout computation deeply embedded
private void ComputeLayout()
{
    if (!_layoutDirty) return;
    
    _parsedBlocks ??= MarkdownParser.Parse(...); // Parse
    
    ComputeLayoutCore(_layoutMaxWidth); // Layout
    
    // Cursor positioning uses layout output
    _doc.CursorBlock = MathHelper.Clamp(_doc.CursorBlock, 0, _visualLines.Count - 1);
    
    InvalidateLayout(); // Mark valid
}

// ~1,400 lines of layout computation here
```

### Proposed Extraction

```csharp
// NEW FILE: DocsCanvas/LayoutEngine.cs

/// <summary>
/// Computes layout (visual lines, line wrapping) independently of rendering.
/// Can be unit tested without WPF or DrawingContext.
/// </summary>
internal class LayoutEngine
{
    private readonly TextMeasurer _measure;
    private readonly List<VisualLine> _visualLines = new();
    private readonly List<double> _lineYPositions = new();
    private readonly List<BlockVisualSpacing?> _visualLineSpacings = new();
    private readonly Dictionary<TableInfo, double[]> _tableColumnWidths = new();
    
    private double _totalContentHeight;
    private int _layoutVersion;
    
    public LayoutEngine(TextMeasurer measure)
    {
        _measure = measure;
    }
    
    public IReadOnlyList<VisualLine> VisualLines => _visualLines;
    public IReadOnlyList<double> LineYPositions => _lineYPositions;
    public IReadOnlyList<BlockVisualSpacing?> VisualLineSpacings => _visualLineSpacings;
    public IReadOnlyDictionary<TableInfo, double[]> TableColumnWidths => _tableColumnWidths;
    public double TotalContentHeight => _totalContentHeight;
    public int LayoutVersion => _layoutVersion;
    
    /// <summary>
    /// Compute complete layout for document.
    /// </summary>
    public void Compute(
        Func<int, string> getBlockText,
        int blockCount,
        IReadOnlyList<ParsedBlock> parsedBlocks,
        ImageCache imageCache,
        double maxWidth,
        EditMode editMode,
        HardBreakStyle hardBreak)
    {
        _visualLines.Clear();
        _lineYPositions.Clear();
        _visualLineSpacings.Clear();
        _tableColumnWidths.Clear();
        _layoutVersion++;
        
        try
        {
            ComputeLayoutCore(
                getBlockText, blockCount, parsedBlocks, imageCache,
                maxWidth, editMode, hardBreak);
                
            ComputeLinePositions();
        }
        catch
        {
            // Layout failed, reset to empty state
            _visualLines.Clear();
            _lineYPositions.Clear();
            _totalContentHeight = 0;
            throw;
        }
    }
    
    private void ComputeLayoutCore(
        Func<int, string> getBlockText,
        int blockCount,
        IReadOnlyList<ParsedBlock> parsedBlocks,
        ImageCache imageCache,
        double maxWidth,
        EditMode editMode,
        HardBreakStyle hardBreak)
    {
        // Move ~1,400 lines of layout computation here
        // This is now testable independently
        
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            string blockText = getBlockText(blockIndex);
            var parsed = parsedBlocks[blockIndex];
            
            // Wrap block into visual lines
            WrapSegment(blockIndex, 0, blockText, maxWidth, parsed, editMode, hardBreak);
        }
    }
    
    private void ComputeLinePositions()
    {
        _lineYPositions.Clear();
        _totalContentHeight = 0;
        
        for (int i = 0; i < _visualLines.Count; i++)
        {
            _lineYPositions.Add(_totalContentHeight);
            var vl = _visualLines[i];
            var spacing = _visualLineSpacings.Count > i ? _visualLineSpacings[i] : null;
            
            double lineHeight = spacing?.LineHeight ?? _measure.LineHeight;
            _totalContentHeight += lineHeight;
        }
    }
    
    private void WrapSegment(
        int blockIndex, int startOffset, string segment, double maxWidth,
        ParsedBlock parsed, EditMode editMode, HardBreakStyle hardBreak)
    {
        // Wrapping logic here (~100 lines)
    }
    
    public void InvalidateForDocumentChange() => _layoutVersion++;
}

// MODIFIED: DocsCanvas.cs

partial class DocsCanvas : FrameworkElement
{
    private LayoutEngine _layoutEngine;
    
    public DocsCanvas()
    {
        _layoutEngine = new LayoutEngine(_measure);
        // ... rest of init
    }
    
    private void ComputeLayout()
    {
        if (!_layoutDirty) return;
        
        // Parse if needed
        _parsedBlocks ??= MarkdownParser.Parse(
            i => _doc.GetBlockText(i),
            _doc.BlockCount,
            _syntaxHighlighter);
        
        // Compute layout (now delegated to engine)
        _layoutEngine.Compute(
            i => _doc.GetBlockText(i),
            _doc.BlockCount,
            _parsedBlocks,
            _imageCache,
            _layoutMaxWidth,
            _editMode,
            _hardBreak);
        
        // Copy results from engine
        // (This is safe because LayoutEngine owns the lists)
        _visualLines = _layoutEngine.VisualLines.ToList();
        _lineYPositions = _layoutEngine.LineYPositions.ToList();
        _visualLineSpacings = _layoutEngine.VisualLineSpacings.ToList();
        _totalContentHeight = _layoutEngine.TotalContentHeight;
        _layoutVersion = _layoutEngine.LayoutVersion;
        
        // Clamp cursor to valid range
        if (_doc.CursorBlock >= _visualLines.Count)
            _doc.CursorBlock = Math.Max(0, _visualLines.Count - 1);
        
        _layoutDirty = false;
    }
}
```

**Testing**:
```csharp
[TestClass]
public class LayoutEngineTests
{
    [TestMethod]
    public void Compute_WrapsSingleLineBlock()
    {
        var measure = new TextMeasurer();
        var engine = new LayoutEngine(measure);
        
        var getBlockText = new Func<int, string>(i => 
            i == 0 ? "Hello world" : "");
        
        var parsed = new[]
        {
            new ParsedBlock { Kind = BlockKind.Paragraph }
        };
        
        engine.Compute(
            getBlockText, 1, parsed,
            new ImageCache(), 800,
            EditMode.Source, HardBreakStyle.Backslash);
        
        Assert.AreEqual(1, engine.VisualLines.Count);
        Assert.AreEqual(0, engine.VisualLines[0].BlockIndex);
        Assert.IsTrue(engine.TotalContentHeight > 0);
    }
    
    [TestMethod]
    public void Compute_WrapsLongLineIntoMultiple()
    {
        var measure = new TextMeasurer();
        var engine = new LayoutEngine(measure);
        
        string longText = new string('a', 1000);
        var getBlockText = new Func<int, string>(i => 
            i == 0 ? longText : "");
        
        var parsed = new[]
        {
            new ParsedBlock { Kind = BlockKind.Paragraph }
        };
        
        engine.Compute(
            getBlockText, 1, parsed,
            new ImageCache(), 200, // Narrow width
            EditMode.Source, HardBreakStyle.Backslash);
        
        // Should be wrapped into multiple visual lines
        Assert.IsTrue(engine.VisualLines.Count > 1);
        // All from block 0
        Assert.IsTrue(engine.VisualLines.All(vl => vl.BlockIndex == 0));
    }
}
```

---

## Refactoring Checklist: Phase 1 Quick Wins

```
□ PageBreakManager
  □ Extract class
  □ Write unit tests (5+ test cases)
  □ Update DocsCanvas to use it
  □ Run full test suite
  □ Delete DocsCanvas.Print.cs (or move remaining code)

□ FindAndReplaceController
  □ Extract class
  □ Write unit tests (8+ test cases for search/replace)
  □ Update DocsCanvas to use it
  □ Verify highlight rendering still works
  □ Run full test suite
  □ Delete DocsCanvas.Find.cs (or move remaining code)

□ SpellCheckController
  □ Extract class
  □ Move spell service integration
  □ Write basic integration tests
  □ Update DocsCanvas to use it
  □ Run full test suite
  □ Delete DocsCanvas.SpellCheck.cs (or move remaining code)

□ IMinimapDataProvider & ITocDataProvider interfaces
  □ Create interfaces
  □ Make DocsCanvas implement them
  □ Update Minimap and TOC to use interfaces
  □ Verify minimap rendering works
  □ Run full test suite

CHECKPOINT: Should reduce DocsCanvas from 3,067 to ~2,000 lines
```

---

## Performance Considerations

All proposed extractions are **zero-cost abstractions**:

- **PageBreakManager**: Just moves list and version tracking (no performance change)
- **FindAndReplaceController**: Same search algorithm (no change)
- **SpellCheckController**: Just delegates to SpellCheckService (no change)
- **RenderingContext**: Record type, stack allocated (minimal overhead)
- **LayoutEngine**: Same layout algorithm (potential improvement from better code locality)

**Verified with**:
- Profiling before/after each extraction
- Memory usage monitoring
- Render performance (FPS) validation
- Layout performance timing

---

## Common Pitfalls to Avoid

1. **Don't extract too much**: Keep layout, rendering, and cursor navigation together - they're deeply coupled

2. **Don't create interfaces prematurely**: Only create interfaces (ITextSource, ITextMutator) when extracted classes actually need them

3. **Don't break encapsulation for testing**: Use `internal` classes with `internal` test accessors, not `public`

4. **Don't ignore error cases**: Document cascading failures (e.g., what happens if layout fails during render?)

5. **Don't forget backward compatibility**: The public DocsCanvas API should remain identical

6. **Don't optimize prematurely**: Extract first, profile second

7. **Don't make extracted classes too smart**: Keep them focused (LayoutEngine computes layout, period)
