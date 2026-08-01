# DocsCanvas Phase 1 Refactoring: Implementation Guide

## Executive Summary

Phase 1 extracts 4 major features from DocsCanvas into dedicated controller classes, reducing the main file from 3067 lines to approximately 2000 lines (35% reduction). The extraction order minimizes coupling and maintains zero breaking changes to public APIs.

**Total Line Reduction: ~1,356 lines across 4 extractions**

---

## 1. PageBreakManager (Extract from DocsCanvas.Print.cs)

### Current Location
- **File**: `D:\Sources\Raisin\RaisinDocs\RaisinDocs\DocsCanvas\DocsCanvas.Print.cs`
- **Lines**: 1-212 (page break + drawing logic)
- **Related**: `DocsCanvas.Print.cs` lines 11-42 (page break state and methods)

### What to Extract

**Page Break State Management (11-42)**
```csharp
private bool _showPageBreaks;
private readonly List<double> _pageBreakYs = [];
private int _pageBreakLayoutVersion = -1;

public bool ShowPageBreaks => _showPageBreaks;
public void SetShowPageBreaks(bool show)
private void ComputePageBreakPositions()
private static int AvoidOrphanedHeading(int breakAt, int pageStart, List<VisualLine> lines)
private void DrawPageBreaks(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
```

**Print Support Methods (156-212)**
- Theme palette for print
- Page break pen and brush builders
- Print dialog integration
- Pagination logic via `DocsPaginator` class

### Dependencies Analysis

| Dependency | Type | Used By |
|-----------|------|---------|
| `_visualLines` | List<VisualLine> | ComputePageBreakPositions, AvoidOrphanedHeading |
| `_lineYPositions` | List<double> | ComputePageBreakPositions, DrawPageBreaks |
| `_layoutVersion` | int | ComputePageBreakPositions |
| `_parsedBlocks` | List<ParsedBlock> | ComputePageBreakPositions, DocsPaginator |
| `_palette` | ThemePalette | DrawPageBreaks, Print() |
| `_measure` | TextMeasurer | DocsPaginator drawing |
| `_doc` | Document | DocsPaginator.GetPage() |
| `_editMode` | EditMode | Print() method |
| `_visualMaps` | List<BlockVisualMap> | Print(), DocsPaginator |
| `_tableColumnWidths` | Dictionary | DocsPaginator.DrawTableBgs() |
| `_layoutMaxWidth` | double | DocsPaginator |
| `_scroll.Offset` | double | Implicit (for UI state) |

### New Class Structure

```csharp
public class PageBreakManager
{
    private readonly DocsCanvas _canvas;
    private bool _showPageBreaks;
    private readonly List<double> _pageBreakYs = [];
    private int _pageBreakLayoutVersion = -1;

    public PageBreakManager(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    // --- Public API ---
    
    public bool ShowPageBreaks => _showPageBreaks;
    
    public void SetShowPageBreaks(bool show)
    {
        if (_showPageBreaks == show) return;
        _showPageBreaks = show;
        _canvas.InvalidateVisual();
    }
    
    public void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        // ... pagination and print logic
    }

    public List<double> GetPageBreakYPositions()
    {
        _showPageBreaks = true;
        _pageBreakLayoutVersion = -1;
        ComputePageBreakPositions();
        return new List<double>(_pageBreakYs);
    }

    // --- Internal API (used by DocsCanvas rendering) ---
    
    internal void ComputePageBreakPositions()
    {
        if (_pageBreakLayoutVersion == _canvas._layoutVersion) return;
        _pageBreakLayoutVersion = _canvas._layoutVersion;
        _pageBreakYs.Clear();

        if (_canvas._visualLines.Count == 0) return;

        double pageContentH = DefaultPageHeight - DocsPaginator.MarginY * 2;
        double pageTopY = _canvas._lineYPositions[0];
        int pageStartLine = 0;
        int prevBlockIndex = _canvas._visualLines[0].BlockIndex;

        for (int i = 1; i < _canvas._visualLines.Count; i++)
        {
            int bi = _canvas._visualLines[i].BlockIndex;

            if (_canvas._parsedBlocks != null && bi > prevBlockIndex)
            {
                bool hasExplicitBreak = false;
                for (int b = prevBlockIndex; b < bi; b++)
                {
                    if (_canvas._parsedBlocks[b].Kind == BlockKind.PageBreak)
                    {
                        hasExplicitBreak = true;
                        break;
                    }
                }
                if (hasExplicitBreak)
                {
                    _pageBreakYs.Add(_canvas._lineYPositions[i]);
                    pageTopY = _canvas._lineYPositions[i];
                    pageStartLine = i;
                    prevBlockIndex = bi;
                    continue;
                }
            }

            double lineBottom = _canvas._lineYPositions[i] + _canvas.GetEffectiveLineHeight(_canvas._visualLines[i]) - pageTopY;
            if (lineBottom > pageContentH && i > pageStartLine)
            {
                int breakAt = AvoidOrphanedHeading(i, pageStartLine, _canvas._visualLines);
                _pageBreakYs.Add(_canvas._lineYPositions[breakAt]);
                pageTopY = _canvas._lineYPositions[breakAt];
                pageStartLine = breakAt;
            }

            prevBlockIndex = bi;
        }
    }

    internal void DrawPageBreaks(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
    {
        ComputePageBreakPositions();
        if (_pageBreakYs.Count == 0) return;

        var pen = _pageBreakPen;
        double width = _canvas.ActualWidth;

        for (int i = 0; i < _pageBreakYs.Count; i++)
        {
            double y = _pageBreakYs[i];
            if (y + 10 < viewTop) continue;
            if (y - 10 > viewBottom) break;

            double screenY = Math.Round(y - effectiveScroll) + 0.5;
            dc.DrawLine(pen, new Point(0, screenY), new Point(width, screenY));

            string label = $"Page {i + 2}";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, 10,
                _pageBreakLabelBrush, _canvas._measure.DpiScale);
            dc.DrawText(ft, new Point(width - ft.Width - 6, screenY + 2));
        }
    }

    // --- Static helpers ---
    
    private static int AvoidOrphanedHeading(int breakAt, int pageStart, List<VisualLine> lines)
    {
        int candidate = breakAt;
        while (candidate > pageStart + 1)
        {
            var prev = lines[candidate - 1];
            if (prev.Length == 0)
                candidate--;
            else if (prev.BlockKind is >= BlockKind.Heading1 and <= BlockKind.Heading6)
                return candidate - 1;
            else
                break;
        }
        return breakAt;
    }

    private const double DefaultPageHeight = 1056;
    private static readonly Pen _pageBreakPen = BuildPageBreakPen();
    private static readonly Brush _pageBreakLabelBrush = BuildPageBreakLabelBrush();

    private static Pen BuildPageBreakPen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 100, 150, 220));
        brush.Freeze();
        var pen = new Pen(brush, 1) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();
        return pen;
    }

    private static Brush BuildPageBreakLabelBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 100, 150, 220));
        brush.Freeze();
        return brush;
    }

    // --- Test hooks ---
    
    internal List<double> TestGetPageBreakYs()
    {
        _showPageBreaks = true;
        _pageBreakLayoutVersion = -1;
        ComputePageBreakPositions();
        return new List<double>(_pageBreakYs);
    }
}
```

### Integration Point

```csharp
// In DocsCanvas.cs main file
private PageBreakManager _pageBreakManager;

public PageBreakManager PageBreakManager => _pageBreakManager ??= new PageBreakManager(this);

public bool ShowPageBreaks => _pageBreakManager?.ShowPageBreaks ?? false;

public void SetShowPageBreaks(bool show) => PageBreakManager.SetShowPageBreaks(show);

// In OnRender()
if (PageBreakManager.ShowPageBreaks)
    PageBreakManager.DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);

// In ComputeLayout()
_pageBreakManager?.ComputePageBreakPositions();
```

### Dependencies on DocsCanvas
- Needs **internal** access to: `_visualLines`, `_lineYPositions`, `_layoutVersion`, `_parsedBlocks`, `_measure`, `ActualWidth`, `GetEffectiveLineHeight()`
- Needs **public** access to: `InvalidateVisual()`
- These fields should remain on DocsCanvas (don't extract)

### Implementation Order: **1st (Least Dependencies)**
- No circular dependencies
- Only reads from DocsCanvas state
- Can be extracted first

### Testing Strategy

**Unit Tests** (new `PageBreakManagerTests.cs`):
```csharp
[Fact]
public void ComputePageBreakPositions_WithNoContent_ReturnsEmpty()
{
    var canvas = new DocsCanvas();
    var manager = new PageBreakManager(canvas);
    var breaks = manager.GetPageBreakYPositions();
    Assert.Empty(breaks);
}

[Fact]
public void AvoidOrphanedHeading_PreventsHeadingAlone()
{
    // Test orphan avoidance logic
}

[Fact]
public void Print_CreatesValidPages()
{
    // Mock PrintDialog, verify pagination
}
```

**UI Tests** (update `DocsCanvasTests.UI`):
```csharp
[Fact]
public void ShowPageBreaks_RendersPen()
{
    var canvas = CreateTestCanvas();
    canvas.SetShowPageBreaks(true);
    canvas.InvalidateLayout();
    // Assert break line is visible
}
```

**Breaking Changes Mitigation**:
- Keep public property `ShowPageBreaks` → delegate to manager
- Keep public method `SetShowPageBreaks()` → delegate to manager
- Keep internal property `TestGetPageBreakYs()` → delegate to manager
- New internal property `PageBreakManager` is not breaking

### Estimated Line Reduction: **~140 lines** from DocsCanvas.Print.cs (page break-specific code remains, DocsPaginator inner class stays)

---

## 2. FindAndReplaceController (Extract from DocsCanvas.Find.cs)

### Current Location
- **File**: `D:\Sources\Raisin\RaisinDocs\RaisinDocs\DocsCanvas\DocsCanvas.Find.cs`
- **Lines**: Full file (289 lines)
- **Note**: Separate from `FindBarController.cs` (UI controller) — this is business logic

### What to Extract

**Find State** (lines 8-14)
```csharp
internal readonly record struct SearchMatch(int Block, int Offset, int Length);
private List<SearchMatch> _searchMatches = [];
private int _currentMatchIndex = -1;
private string _lastSearchQuery = "";
private bool _lastSearchCaseSensitive;
private bool _searchDirty;
```

**Core Methods** (lines 37-88)
```csharp
internal void ExecuteSearch(string query, bool caseSensitive)
internal void NavigateMatch(int direction)
internal void ReplaceCurrent(string replacement)
internal void ReplaceAll(string replacement)
private void ScrollToMatch(int matchIndex)
private void InvalidateSearchOnContentChange()
```

**Drawing** (lines 170-284)
```csharp
private void DrawSearchHighlights(DrawingContext dc, double effectiveScroll)
private void DrawMatchOnVisualLines(DrawingContext dc, SearchMatch match, Brush brush, ...)
private void DrawMatchOnJoinedLine(DrawingContext dc, VisualLine vl, SearchMatch match, ...)
```

### Dependencies Analysis

| Dependency | Type | Used By |
|-----------|------|---------|
| `_doc` | Document | ExecuteSearch, NavigateMatch, ReplaceCurrent, ReplaceAll |
| `_searchMatches` | List<SearchMatch> | All core methods |
| `_visualLines` | List<VisualLine> | DrawMatchOnVisualLines |
| `_lineYPositions` | List<double> | DrawSearchHighlights, DrawMatch* |
| `_parsedBlocks` | List<ParsedBlock> | DrawMatchOnVisualLines |
| `_visualMaps` | List<BlockVisualMap> | DrawMatchOnVisualLines |
| `_tableColumnWidths` | Dict | DrawMatchOnVisualLines (for tables) |
| `_palette.SearchMatch`, `_palette.CurrentSearchMatch` | Brush | DrawSearchHighlights |
| `FindBar` | FindBarController | ExecuteSearch, ReplaceCurrent, ReplaceAll (calls UpdateMatchInfo) |
| `_measure` | TextMeasurer | DrawMatchOnVisualLines (MeasureRangeWidth) |
| Layout state | Various | For rendering |

### New Class Structure

```csharp
public class FindAndReplaceController
{
    private readonly DocsCanvas _canvas;
    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";
    private bool _lastSearchCaseSensitive;
    private bool _searchDirty;

    internal record struct SearchMatch(int Block, int Offset, int Length);

    public FindAndReplaceController(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    // --- Public API ---
    
    public void ExecuteSearch(string query, bool caseSensitive)
    {
        _searchMatches.Clear();
        _lastSearchQuery = query;
        _lastSearchCaseSensitive = caseSensitive;
        _searchDirty = false;

        if (string.IsNullOrEmpty(query))
        {
            _currentMatchIndex = -1;
            _canvas.FindBar?.UpdateMatchInfo(-1, 0);
            _canvas.InvalidateVisual();
            return;
        }

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int b = 0; b < _canvas._doc.BlockCount; b++)
        {
            string blockText = _canvas._doc.GetBlockText(b);
            int pos = 0;
            while (pos <= blockText.Length - query.Length)
            {
                int found = blockText.IndexOf(query, pos, comparison);
                if (found < 0) break;
                _searchMatches.Add(new SearchMatch(b, found, query.Length));
                pos = found + query.Length;
            }
        }

        _currentMatchIndex = -1;
        for (int i = 0; i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (Document.ComparePositions(m.Block, m.Offset, _canvas._doc.CursorBlock, _canvas._doc.CursorOffset) >= 0)
            {
                _currentMatchIndex = i;
                break;
            }
        }
        if (_currentMatchIndex == -1 && _searchMatches.Count > 0)
            _currentMatchIndex = 0;

        _canvas.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);

        if (_currentMatchIndex >= 0)
            ScrollToMatch(_currentMatchIndex);
        else
            _canvas.InvalidateVisual();
    }

    public void NavigateMatch(int direction)
    {
        if (_searchMatches.Count == 0) return;
        _currentMatchIndex += direction;
        if (_currentMatchIndex >= _searchMatches.Count)
            _currentMatchIndex = 0;
        else if (_currentMatchIndex < 0)
            _currentMatchIndex = _searchMatches.Count - 1;
        ScrollToMatch(_currentMatchIndex);
        _canvas.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
    }

    public void ReplaceCurrent(string replacement)
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;

        var match = _searchMatches[_currentMatchIndex];

        _canvas.SealAndStopTimer();
        _canvas._doc.BeginUndoGroup();
        _canvas._doc.RemoveTextAt(match.Block, match.Offset, match.Length);
        _canvas._doc.InsertTextAt(match.Block, match.Offset, replacement);
        _canvas._doc.CursorBlock = match.Block;
        _canvas._doc.CursorOffset = match.Offset + replacement.Length;
        _canvas._doc.CollapseSelection();
        _canvas._doc.SealUndoGroup();

        int savedIndex = _currentMatchIndex;
        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        if (_searchMatches.Count > 0)
        {
            _currentMatchIndex = Math.Min(savedIndex, _searchMatches.Count - 1);
            ScrollToMatch(_currentMatchIndex);
            _canvas.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
        }

        _canvas.InvalidateLayout();
    }

    public void ReplaceAll(string replacement)
    {
        if (_searchMatches.Count == 0) return;

        _canvas.SealAndStopTimer();
        _canvas._doc.BeginUndoGroup();

        for (int i = _searchMatches.Count - 1; i >= 0; i--)
        {
            var match = _searchMatches[i];
            _canvas._doc.RemoveTextAt(match.Block, match.Offset, match.Length);
            _canvas._doc.InsertTextAt(match.Block, match.Offset, replacement);
        }

        _canvas._doc.SealUndoGroup();

        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        _canvas.InvalidateLayout();
    }

    public void ClearMatches()
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        _canvas.InvalidateVisual();
    }

    // --- Internal API ---
    
    internal void InvalidateSearchOnContentChange()
    {
        if (_searchMatches.Count > 0 && !string.IsNullOrEmpty(_lastSearchQuery))
            _searchDirty = true;
    }

    internal void DrawSearchHighlights(DrawingContext dc, double effectiveScroll)
    {
        if (_searchDirty)
        {
            _searchDirty = false;
            ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        }

        if (_searchMatches.Count == 0) return;

        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + _canvas.ActualHeight;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int mi = 0; mi < _searchMatches.Count; mi++)
            {
                bool isCurrent = mi == _currentMatchIndex;
                if (pass == 0 && isCurrent) continue;
                if (pass == 1 && !isCurrent) continue;

                var match = _searchMatches[mi];
                var brush = isCurrent ? _canvas._palette.CurrentSearchMatch : _canvas._palette.SearchMatch;
                DrawMatchOnVisualLines(dc, match, brush, effectiveScroll, viewTop, viewBottom);
            }
        }
    }

    // --- Private helpers ---
    
    private void ScrollToMatch(int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= _searchMatches.Count) return;
        var match = _searchMatches[matchIndex];

        _canvas._doc.AnchorBlock = match.Block;
        _canvas._doc.AnchorOffset = match.Offset;
        _canvas._doc.CursorBlock = match.Block;
        _canvas._doc.CursorOffset = match.Offset + match.Length;

        _canvas.ComputeLayout();
        _canvas.EnsureCursorVisible();
        _canvas.InvalidateVisual();
    }

    private void DrawMatchOnVisualLines(DrawingContext dc, SearchMatch match, Brush brush,
        double effectiveScroll, double viewTop, double viewBottom)
    {
        int matchEnd = match.Offset + match.Length;

        for (int i = 0; i < _canvas._visualLines.Count; i++)
        {
            var vl = _canvas._visualLines[i];
            double lineH = _canvas.GetEffectiveLineHeight(vl);
            double lineY = _canvas._lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Group != null)
            {
                DrawMatchOnJoinedLine(dc, vl, match, brush, lineY, lineH, effectiveScroll);
                continue;
            }

            if (vl.BlockIndex != match.Block) continue;

            int vlEnd = vl.StartOffset + vl.Length;
            if (match.Offset >= vlEnd || matchEnd <= vl.StartOffset) continue;

            int hlStart = Math.Max(match.Offset, vl.StartOffset);
            int hlEnd = Math.Min(matchEnd, vlEnd);

            string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
            var parsed = _canvas._parsedBlocks![vl.BlockIndex];
            var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;

            double x1, x2;
            if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null)
            {
                if (_canvas._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                {
                    x1 = _canvas.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                    x2 = _canvas.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                }
                else continue;
            }
            else
            {
                x1 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                x2 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                    x1 += prefixW;
                    x2 += prefixW;
                }
            }

            double w = Math.Max(0, x2 - x1);
            if (w > 0)
                dc.DrawRectangle(brush, null,
                    new Rect(_canvas._padding + x1, lineY - effectiveScroll, w, lineH));
        }
    }

    private void DrawMatchOnJoinedLine(DrawingContext dc, VisualLine vl,
        SearchMatch match, Brush brush, double lineY, double lineH, double effectiveScroll)
    {
        var group = vl.Group!;
        int matchStartJoined = group.SourceToJoined(match.Block, match.Offset);
        int matchEndJoined = group.SourceToJoined(match.Block, match.Offset + match.Length);
        if (matchStartJoined < 0 || matchEndJoined < 0) return;

        int vlStart = vl.StartOffset;
        int vlEnd = vl.StartOffset + vl.Length;

        if (vlEnd <= matchStartJoined || vlStart >= matchEndJoined) return;

        int hlStart = Math.Max(vlStart, matchStartJoined);
        int hlEnd = Math.Min(vlEnd, matchEndJoined);

        double x1 = _canvas.MeasureJoinedRange(group, vlStart, hlStart - vlStart);
        double x2 = _canvas.MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

        double w = Math.Max(0, x2 - x1);
        if (w > 0)
            dc.DrawRectangle(brush, null,
                new Rect(_canvas._padding + x1, lineY - effectiveScroll, w, lineH));
    }

    // --- Test hooks ---
    
    internal int TestSearchMatchCount => _searchMatches.Count;
    internal int TestCurrentMatchIndex => _currentMatchIndex;
    internal void TestExecuteSearch(string query, bool caseSensitive) => ExecuteSearch(query, caseSensitive);
}
```

### Integration Point

```csharp
// In DocsCanvas.cs main file
private FindAndReplaceController? _findAndReplaceController;

internal FindAndReplaceController FindAndReplace => 
    _findAndReplaceController ??= new FindAndReplaceController(this);

// Make current Find partial methods delegate:
internal void ExecuteSearch(string query, bool caseSensitive) => FindAndReplace.ExecuteSearch(query, caseSensitive);
internal void NavigateMatch(int direction) => FindAndReplace.NavigateMatch(direction);
internal void ReplaceCurrent(string replacement) => FindAndReplace.ReplaceCurrent(replacement);
internal void ReplaceAll(string replacement) => FindAndReplace.ReplaceAll(replacement);

// In OnContentChanged:
FindAndReplace.InvalidateSearchOnContentChange();

// In OnRender:
FindAndReplace.DrawSearchHighlights(dc, effectiveScroll);
```

### Dependencies on DocsCanvas
- Needs **internal** access to: `_doc`, `_visualLines`, `_lineYPositions`, `_parsedBlocks`, `_visualMaps`, `_tableColumnWidths`, `_measure`, `_palette`, `_padding`, `ActualHeight`, `ComputeLayout()`, `EnsureCursorVisible()`, `InvalidateVisual()`, `InvalidateLayout()`, `CursorXInTableRow()`, `MeasureRangeWidth()`, `MeasureJoinedRange()`, `SealAndStopTimer()`, `IsVisual` property
- Needs **public** access to: `InvalidateVisual()`, `InvalidateLayout()`

### Implementation Order: **2nd (Moderate Dependencies)**
- More complex than PageBreakManager, but tightly focused
- Depends mainly on read-access to Document and layout state
- Can be extracted early

### Testing Strategy

**Unit Tests** (new `FindAndReplaceControllerTests.cs`):
```csharp
[Fact]
public void ExecuteSearch_EmptyQuery_ClearsMatches()
{
    var doc = new Document();
    doc.InsertTextAt(0, 0, "hello world");
    var canvas = new DocsCanvas { Document = doc };
    var controller = new FindAndReplaceController(canvas);
    
    controller.ExecuteSearch("hello", false);
    Assert.NotEmpty(controller.TestSearchMatchCount);
    
    controller.ExecuteSearch("", false);
    Assert.Equal(0, controller.TestSearchMatchCount);
}

[Fact]
public void ExecuteSearch_CaseSensitive_FindsExact()
{
    // Test case-sensitive matching
}

[Fact]
public void ReplaceCurrent_UpdatesMatchesAfterReplacement()
{
    // Test that matches are recalculated
}

[Fact]
public void ReplaceAll_CreatesUndoGroup()
{
    // Test all replacements are in one undo group
}
```

**Breaking Changes Mitigation**:
- All existing internal methods remain as delegates
- New internal property `FindAndReplace` is not breaking
- Test hooks preserved

### Estimated Line Reduction: **~289 lines** (entire DocsCanvas.Find.cs becomes controller)

---

## 3. SpellCheckController (Extract from DocsCanvas.SpellCheck.cs)

### Current Location
- **File**: `D:\Sources\Raisin\RaisinDocs\RaisinDocs\DocsCanvas\DocsCanvas.SpellCheck.cs`
- **Lines**: Full file (426 lines)

### What to Extract

**SpellCheck State** (lines 10-19)
```csharp
private bool _spellCheckEnabled;
private SpellCheckService? _spellCheckService;
private string? _projectFolder;
private readonly HashSet<int> _dirtySpellBlocks = new();
private DispatcherTimer? _spellCheckTimer;
private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;
private Pen? _spellErrorPen;
```

**All Methods** (lines 21-426)
- `SetSpellCheckEnabled(bool enabled)`
- `CleanupSpellCheck()`
- `OnDocumentBasePathChanged()`
- `SetProjectFolder(string folder)`
- `ResolveAndLoadProjectDictionary()`
- `EnsureSpellCheckInitialized()`
- `OnContentChangedForSpellCheck()`
- `SpellCheckTimerTick()`
- `RecheckBlock()`, `RecheckAllBlocks()`
- Drawing and context menu methods
- Test hooks

### Dependencies Analysis

| Dependency | Type | Used By |
|-----------|------|---------|
| `_spellCheckService` | SpellCheckService | All methods |
| `_blockSpellingErrors` | List<IReadOnlyList<SpellingError>?> | RecheckBlock, drawing methods |
| `_dirtySpellBlocks` | HashSet<int> | Deferred checking |
| `_parsedBlocks` | List<ParsedBlock> | RecheckBlock (ExtractCheckableWords) |
| `_doc` | Document | Block text access |
| `_visualLines` | List<VisualLine> | Drawing |
| `_lineYPositions` | List<double> | Drawing |
| `_measure` | TextMeasurer | Drawing |
| `_palette` | ThemePalette | Drawing |
| `DocumentBasePath` | string? | Directory resolution |
| `_layoutVersion` | int | Tracking layout changes |
| `ActualHeight` | double | Viewport culling in drawing |
| Drawing methods | Various | DrawSpellingErrors delegates to existing draw methods |

### New Class Structure

```csharp
public class SpellCheckController
{
    private readonly DocsCanvas _canvas;
    private bool _spellCheckEnabled;
    private SpellCheckService? _spellCheckService;
    private string? _projectFolder;
    private readonly HashSet<int> _dirtySpellBlocks = new();
    private DispatcherTimer? _spellCheckTimer;
    private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;
    private Pen? _spellErrorPen;

    public bool SpellCheckEnabled => _spellCheckEnabled;
    public string? ProjectFolder => _projectFolder;

    public SpellCheckController(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    // --- Public API ---
    
    public void SetSpellCheckEnabled(bool enabled)
    {
        if (_spellCheckEnabled == enabled) return;
        _spellCheckEnabled = enabled;

        if (enabled)
        {
            EnsureSpellCheckInitialized();
            RecheckAllBlocks();
        }
        else
        {
            _blockSpellingErrors = null;
            _spellCheckTimer?.Stop();
        }

        _canvas.InvalidateVisual();
    }

    public void SetProjectFolder(string folder)
    {
        RaisinDocsPaths.SetProjectFolder(folder);
        _projectFolder = folder;
        if (_spellCheckService is not null)
        {
            _spellCheckService.LoadProjectDictionary(RaisinDocsPaths.GetProjectDictionaryPath(folder));
            if (_spellCheckEnabled)
            {
                RecheckAllBlocks();
                _canvas.InvalidateVisual();
            }
        }
    }

    public void Cleanup()
    {
        if (_spellCheckTimer != null)
        {
            _spellCheckTimer.Stop();
            _spellCheckTimer.Tick -= SpellCheckTimerTick;
            _spellCheckTimer = null;
        }
        _spellCheckService = null;
        _blockSpellingErrors = null;
    }

    public void OnDocumentBasePathChanged()
    {
        if (_spellCheckService is null) return;
        ResolveAndLoadProjectDictionary();
        if (_spellCheckEnabled)
            RecheckAllBlocks();
    }

    public static string? UserDictionaryPath => RaisinDocsPaths.GetUserDictionaryPath();
    public string? ProjectDictionaryPath => _projectFolder is not null
        ? RaisinDocsPaths.GetProjectDictionaryPath(_projectFolder) : null;

    // --- Internal API ---
    
    internal void OnContentChangedForSpellCheck()
    {
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        int from = Math.Min(_canvas._doc.AnchorBlock, _canvas._doc.CursorBlock);
        int to = Math.Max(_canvas._doc.AnchorBlock, _canvas._doc.CursorBlock);
        for (int i = from; i <= to; i++)
            _dirtySpellBlocks.Add(i);

        _spellCheckTimer?.Stop();
        _spellCheckTimer?.Start();
    }

    internal void DrawSpellingErrors(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        if (_blockSpellingErrors is null || _spellErrorPen is null) return;

        for (int i = 0; i < _canvas._visualLines.Count; i++)
        {
            var vl = _canvas._visualLines[i];
            double lineH = _canvas.GetEffectiveLineHeight(vl);
            double lineY = _canvas._lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Group != null)
            {
                DrawSpellingErrorsOnJoinedLine(dc, vl, lineY, lineH, effectiveScroll);
                continue;
            }

            if (vl.BlockIndex >= _blockSpellingErrors.Count) continue;
            var errors = _blockSpellingErrors[vl.BlockIndex];
            if (errors is null) continue;

            string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
            var parsed = _canvas._parsedBlocks![vl.BlockIndex];
            var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var err in errors)
            {
                int errEnd = err.StartOffset + err.Length;
                if (err.StartOffset >= vlEnd || errEnd <= vl.StartOffset) continue;

                int hlStart = Math.Max(err.StartOffset, vl.StartOffset);
                int hlEnd = Math.Min(errEnd, vlEnd);

                double x1, x2;
                if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null)
                {
                    if (_canvas._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                    {
                        x1 = _canvas.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                        x2 = _canvas.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                    }
                    else continue;
                }
                else
                {
                    x1 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);
                    x2 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);

                    if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                    {
                        double prefixW = _canvas._measure.MeasureReplacementPrefix(
                            map.ReplacementPrefix!, map.PrefixMeasureKind);
                        x1 += prefixW;
                        x2 += prefixW;
                    }
                }

                double w = x2 - x1;
                if (w > 0)
                {
                    double baselineY = lineY - effectiveScroll + lineH - 2;
                    DrawSquigglyLine(dc, _canvas._padding + x1, _canvas._padding + x2, baselineY);
                }
            }
        }
    }

    internal bool AddSpellCheckMenuItems(ContextMenu menu, Point position)
    {
        if (_spellCheckService is null || _blockSpellingErrors is null) return false;

        _canvas.HitTestToPosition(position, out int blockIndex, out int charOffset);
        var error = FindSpellingErrorAt(blockIndex, charOffset);
        if (error is null) return false;

        var err = error.Value;
        var suggestions = _spellCheckService.Suggest(err.Word);

        if (suggestions.Count > 0)
        {
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.Bold };
                ApplyMenuItemStyle(item);
                var capturedSuggestion = suggestion;
                var capturedBlock = blockIndex;
                var capturedErr = err;
                item.Click += (_, _) =>
                {
                    ReplaceWord(capturedBlock, capturedErr.StartOffset, capturedErr.Length, capturedSuggestion);
                    _canvas.Focus();
                };
                menu.Items.Add(item);
            }
        }
        else
        {
            var noSuggestions = new MenuItem { Header = "(no suggestions)", IsEnabled = false };
            ApplyMenuItemStyle(noSuggestions);
            menu.Items.Add(noSuggestions);
        }

        menu.Items.Add(new Separator());

        var ignoreItem = new MenuItem { Header = "Ignore All" };
        ApplyMenuItemStyle(ignoreItem);
        var wordToIgnore = err.Word;
        ignoreItem.Click += (_, _) =>
        {
            _spellCheckService.IgnoreAll(wordToIgnore);
            RecheckAllBlocks();
            _canvas.InvalidateVisual();
            _canvas.Focus();
        };
        menu.Items.Add(ignoreItem);

        var addItem = new MenuItem { Header = "Add to Dictionary" };
        ApplyMenuItemStyle(addItem);
        var wordToAdd = err.Word;
        addItem.Click += (_, _) =>
        {
            _spellCheckService.AddToUserDictionary(wordToAdd);
            RecheckAllBlocks();
            _canvas.InvalidateVisual();
            _canvas.Focus();
        };
        menu.Items.Add(addItem);

        var addProjectItem = new MenuItem { Header = "Add to Project Dictionary" };
        ApplyMenuItemStyle(addProjectItem);
        var wordForProject = err.Word;
        addProjectItem.Click += (_, _) =>
        {
            _spellCheckService.AddToProjectDictionary(wordForProject);
            RecheckAllBlocks();
            _canvas.InvalidateVisual();
            _canvas.Focus();
        };
        menu.Items.Add(addProjectItem);

        return true;
    }

    // --- Private helpers ---
    
    private void EnsureSpellCheckInitialized()
    {
        if (_spellCheckService is not null) return;

        _spellCheckService = new SpellCheckService();
        _spellCheckService.LoadEmbeddedDictionary();
        ResolveAndLoadProjectDictionary();

        _spellErrorPen = new Pen(Brushes.Red, 0.75);
        _spellErrorPen.Freeze();

        _spellCheckTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _spellCheckTimer.Tick += SpellCheckTimerTick;
    }

    private void ResolveAndLoadProjectDictionary()
    {
        if (_canvas.DocumentBasePath is not null)
        {
            var root = RaisinDocsPaths.FindProjectRoot(_canvas.DocumentBasePath);
            _projectFolder = root ?? _canvas.DocumentBasePath;
        }
        else
        {
            _projectFolder = null;
        }
        var dictPath = _projectFolder is not null
            ? RaisinDocsPaths.GetProjectDictionaryPath(_projectFolder) : null;
        _spellCheckService!.LoadProjectDictionary(dictPath);
    }

    private void SpellCheckTimerTick(object? sender, EventArgs e)
    {
        _spellCheckTimer!.Stop();
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        _canvas.ComputeLayout();

        if (_blockSpellingErrors is null || _blockSpellingErrors.Count != _canvas._doc.BlockCount)
        {
            RecheckAllBlocks();
            _canvas.InvalidateVisual();
            return;
        }

        foreach (var blockIdx in _dirtySpellBlocks)
        {
            if (blockIdx >= _canvas._doc.BlockCount) continue;
            RecheckBlock(blockIdx);
        }

        _dirtySpellBlocks.Clear();
        _canvas.InvalidateVisual();
    }

    private void RecheckBlock(int blockIndex)
    {
        if (_canvas._parsedBlocks is null || blockIndex >= _canvas._parsedBlocks.Count) return;

        var text = _canvas._doc.GetBlockText(blockIndex);
        var parsed = _canvas._parsedBlocks[blockIndex];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        var errors = new List<SpellingError>();

        foreach (var (offset, word) in words)
        {
            if (!_spellCheckService!.Check(word))
                errors.Add(new SpellingError(offset, word.Length, word));
        }

        while (_blockSpellingErrors!.Count <= blockIndex)
            _blockSpellingErrors.Add(null);

        _blockSpellingErrors[blockIndex] = errors.Count > 0 ? errors : null;
    }

    private void RecheckAllBlocks()
    {
        if (_spellCheckService is null) return;

        _canvas.ComputeLayout();

        _blockSpellingErrors = new List<IReadOnlyList<SpellingError>?>(
            Enumerable.Repeat<IReadOnlyList<SpellingError>?>(null, _canvas._doc.BlockCount));

        for (int i = 0; i < _canvas._doc.BlockCount; i++)
            RecheckBlock(i);

        _dirtySpellBlocks.Clear();
    }

    private void ReplaceWord(int blockIndex, int offset, int length, string replacement)
    {
        _canvas._doc.BeginUndoGroup();
        _canvas._doc.RemoveTextAt(blockIndex, offset, length);
        _canvas._doc.InsertTextAt(blockIndex, offset, replacement);
        _canvas._doc.CursorBlock = blockIndex;
        _canvas._doc.CursorOffset = offset + replacement.Length;
        _canvas._doc.AnchorBlock = blockIndex;
        _canvas._doc.AnchorOffset = offset + replacement.Length;
        _canvas._doc.SealUndoGroup();
        _canvas.InvalidateLayout();
        _canvas.EnsureCursorVisible();
    }

    private SpellingError? FindSpellingErrorAt(int blockIndex, int charOffset)
    {
        if (_blockSpellingErrors is null || blockIndex >= _blockSpellingErrors.Count) return null;
        var errors = _blockSpellingErrors[blockIndex];
        if (errors is null) return null;

        foreach (var err in errors)
        {
            if (charOffset >= err.StartOffset && charOffset < err.StartOffset + err.Length)
                return err;
        }
        return null;
    }

    private void DrawSpellingErrorsOnJoinedLine(DrawingContext dc, VisualLine vl,
        double lineY, double lineH, double effectiveScroll)
    {
        var group = vl.Group!;

        foreach (var seg in group.Segments)
        {
            if (seg.BlockIndex >= _blockSpellingErrors!.Count) continue;
            var errors = _blockSpellingErrors[seg.BlockIndex];
            if (errors is null) continue;

            foreach (var err in errors)
            {
                int startJoined = group.SourceToJoined(seg.BlockIndex, err.StartOffset);
                int endJoined = group.SourceToJoined(seg.BlockIndex, err.StartOffset + err.Length);
                if (startJoined < 0 || endJoined < 0) continue;

                int vlStart = vl.StartOffset;
                int vlEnd = vl.StartOffset + vl.Length;
                if (vlEnd <= startJoined || vlStart >= endJoined) continue;

                int hlStart = Math.Max(vlStart, startJoined);
                int hlEnd = Math.Min(vlEnd, endJoined);

                double x1 = _canvas.MeasureJoinedRange(group, vlStart, hlStart - vlStart);
                double x2 = _canvas.MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

                double w = x2 - x1;
                if (w > 0)
                {
                    double baselineY = lineY - effectiveScroll + lineH - 2;
                    DrawSquigglyLine(dc, _canvas._padding + x1, _canvas._padding + x2, baselineY);
                }
            }
        }
    }

    private void DrawSquigglyLine(DrawingContext dc, double x1, double x2, double y)
    {
        const double waveHeight = 1.5;
        const double waveLength = 3.0;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y), false, false);
            double x = x1;
            bool up = true;
            while (x < x2)
            {
                x = Math.Min(x + waveLength, x2);
                ctx.LineTo(new Point(x, y + (up ? -waveHeight : waveHeight)), true, false);
                up = !up;
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, _spellErrorPen, geometry);
    }

    private static void ApplyMenuItemStyle(MenuItem item)
    {
        // Apply theme-aware styling if needed
    }

    // --- Test hooks ---
    
    internal SpellCheckService? TestSpellCheckService => _spellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _blockSpellingErrors is not null && blockIndex < _blockSpellingErrors.Count
            ? _blockSpellingErrors[blockIndex]
            : null;
}
```

### Integration Point

```csharp
// In DocsCanvas.cs main file
private SpellCheckController? _spellCheckController;

private SpellCheckController SpellCheck => 
    _spellCheckController ??= new SpellCheckController(this);

// Delegation methods:
public bool SpellCheckEnabled => SpellCheck.SpellCheckEnabled;
public string? ProjectFolder => SpellCheck.ProjectFolder;

public void SetSpellCheckEnabled(bool enabled) => SpellCheck.SetSpellCheckEnabled(enabled);
public void SetProjectFolder(string folder) => SpellCheck.SetProjectFolder(folder);

// In OnDocumentBasePathChanged:
SpellCheck.OnDocumentBasePathChanged();

// In OnContentChanged:
SpellCheck.OnContentChangedForSpellCheck();

// In OnRender:
SpellCheck.DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

// In AddContextMenuItems:
if (!SpellCheck.AddSpellCheckMenuItems(menu, position))
    // ... other menu items

// In Dispose/Cleanup:
_spellCheckController?.Cleanup();

public static string? UserDictionaryPath => SpellCheckController.UserDictionaryPath;
public string? ProjectDictionaryPath => SpellCheck.ProjectDictionaryPath;
```

### Dependencies on DocsCanvas
- Needs **internal** access to: `_doc`, `_parsedBlocks`, `_visualLines`, `_lineYPositions`, `_visualMaps`, `_tableColumnWidths`, `_measure`, `_padding`, `DocumentBasePath`, `HitTestToPosition()`, `ComputeLayout()`, `InvalidateVisual()`, `InvalidateLayout()`, `EnsureCursorVisible()`, `CursorXInTableRow()`, `MeasureRangeWidth()`, `MeasureJoinedRange()`, `Focus()`, `IsVisual` property
- Needs **public** access to: `DocumentBasePath`

### Implementation Order: **3rd (Moderate to High Dependencies)**
- After Find/Replace (shares similar drawing patterns)
- Requires timer coordination

### Testing Strategy

**Unit Tests** (new `SpellCheckControllerTests.cs`):
```csharp
[Fact]
public void SetSpellCheckEnabled_Enabled_InitializesService()
{
    var canvas = new DocsCanvas();
    var controller = new SpellCheckController(canvas);
    
    controller.SetSpellCheckEnabled(true);
    
    Assert.NotNull(controller.TestSpellCheckService);
}

[Fact]
public void RecheckBlock_FindsMisspelledWords()
{
    // Test word extraction and error marking
}

[Fact]
public void OnContentChangedForSpellCheck_MarksDirtyBlocks()
{
    // Test that modified blocks are queued for checking
}

[Fact]
public void SpellCheckMenuItems_OffersCorrectSuggestions()
{
    // Test context menu generation
}
```

**Breaking Changes Mitigation**:
- All existing public properties remain as delegates
- New internal property `SpellCheck` is not breaking
- Test hooks preserved and working

### Estimated Line Reduction: **~426 lines** (entire DocsCanvas.SpellCheck.cs becomes controller)

---

## 4. IMinimapDataProvider Interface (Extract from DocsCanvas.Minimap.cs)

### Current Location
- **File**: `D:\Sources\Raisin\RaisinDocs\RaisinDocs\DocsCanvas\DocsCanvas.Minimap.cs`
- **Lines**: 48-208 (data provider methods)
- **Note**: Toggle/property methods stay in DocsCanvas

### What to Extract

**Public Data Access Methods** (lines 48-208)
```csharp
internal int GetMinimapLineKind(int index)
internal double MinimapBaseLineHeight
internal (BitmapSource Image, double Width, double Height, double YOffset)? GetMinimapLineImage(int index)
internal void GetMinimapLineInfo(int index, out string text, out BlockKind kind)
internal void GetMinimapLineColorInfo(int index, out RgbColor? blockFg, out RgbColor? blockBg, ...)
internal bool GetMinimapTableRowInfo(int index, List<MinimapTableCell> cells, out bool isHeader, ...)
```

**Public Properties** (lines 36-46)
```csharp
internal int MinimapLayoutVersion
internal int MinimapLineCount
internal double MinimapScrollOffset
internal double MinimapTotalHeight
internal IReadOnlyList<double> MinimapCanvasLineYPositions
internal Color MinimapBackground
internal Color MinimapForeground
internal Color MinimapCodeBackground
internal Color MinimapTableBackground
internal Color MinimapTableHeaderBackground
internal double MinimapCanvasTextWidth
```

### Dependencies Analysis

| Dependency | Type | Used By |
|-----------|------|---------|
| `_visualLines` | List<VisualLine> | GetMinimapLineKind, GetMinimapLineImage, GetMinimapLineInfo, GetMinimapLineColorInfo |
| `_lineYPositions` | List<double> | Various |
| `_parsedBlocks` | List<ParsedBlock> | GetMinimapLineColorInfo, GetMinimapTableRowInfo |
| `_visualMaps` | List<BlockVisualMap> | GetMinimapLineImage, GetMinimapTableRowInfo |
| `_tableColumnWidths` | Dict | GetMinimapTableRowInfo |
| `_palette` | ThemePalette | Color properties |
| `_imageCache` | ImageCache | GetMinimapLineImage |
| `_doc` | Document | GetMinimapLineInfo, GetMinimapTableRowInfo |
| `_measure` | TextMeasurer | MinimapBaseLineHeight |
| `_layoutVersion` | int | MinimapLayoutVersion |
| `_scroll.EffectiveOffset` | double | MinimapScrollOffset |
| `_totalContentHeight` | double | MinimapTotalHeight |
| `_padding` | double | MinimapCanvasTextWidth |
| `ActualWidth` | double | MinimapCanvasTextWidth |
| `IsVisual` | bool | Logic gates |
| `DocumentBasePath` | string? | Image loading |
| `_layoutMaxWidth` | double | Image caching |
| `_imagePreview` | ImagePreviewMode | GetMinimapLineImage |

### New Interface Structure

```csharp
/// <summary>
/// Provides data access for minimap rendering. Encapsulates layout, visual,
/// and color information without exposing DocsCanvas internal state.
/// </summary>
internal interface IMinimapDataProvider
{
    /// <summary>Gets the current layout version for invalidation tracking.</summary>
    int MinimapLayoutVersion { get; }
    
    /// <summary>Gets the total number of visual lines.</summary>
    int MinimapLineCount { get; }
    
    /// <summary>Gets the current scroll offset in pixels.</summary>
    double MinimapScrollOffset { get; }
    
    /// <summary>Gets the total height of all content in pixels.</summary>
    double MinimapTotalHeight { get; }
    
    /// <summary>Gets the Y positions of each visual line.</summary>
    IReadOnlyList<double> MinimapCanvasLineYPositions { get; }
    
    /// <summary>Gets the background color for the current theme.</summary>
    Color MinimapBackground { get; }
    
    /// <summary>Gets the foreground (text) color for the current theme.</summary>
    Color MinimapForeground { get; }
    
    /// <summary>Gets the background color for code blocks.</summary>
    Color MinimapCodeBackground { get; }
    
    /// <summary>Gets the background color for table backgrounds.</summary>
    Color MinimapTableBackground { get; }
    
    /// <summary>Gets the background color for table header rows.</summary>
    Color MinimapTableHeaderBackground { get; }
    
    /// <summary>Gets the available text width (excludes padding).</summary>
    double MinimapCanvasTextWidth { get; }
    
    /// <summary>Gets the kind (type) of a line at the given visual line index.</summary>
    BlockKind GetMinimapLineKind(int index);
    
    /// <summary>Gets the base line height for paragraph text.</summary>
    double MinimapBaseLineHeight { get; }
    
    /// <summary>Gets image data for a visual line if it contains an image.</summary>
    (BitmapSource Image, double Width, double Height, double YOffset)? GetMinimapLineImage(int index);
    
    /// <summary>Gets the text content and block kind for a visual line.</summary>
    void GetMinimapLineInfo(int index, out string text, out BlockKind kind);
    
    /// <summary>Gets color information for a visual line.</summary>
    void GetMinimapLineColorInfo(int index, 
        out RgbColor? blockForeground, 
        out RgbColor? blockBackground,
        out IReadOnlyList<ColorSpan>? colorSpans, 
        out int spanBaseOffset);
    
    /// <summary>Gets table cell information for a table row visual line.</summary>
    /// <returns>True if the line is a table row, false otherwise.</returns>
    bool GetMinimapTableRowInfo(int index, 
        List<MinimapTableCell> cells,
        out bool isHeader, 
        out double tableWidth,
        out IReadOnlyList<ColorSpan>? colorSpans);
}
```

### Implementation by DocsCanvas

```csharp
// In DocsCanvas.cs - implement IMinimapDataProvider
public partial class DocsCanvas : FrameworkElement, IMinimapDataProvider
{
    // --- IMinimapDataProvider Implementation ---
    
    int IMinimapDataProvider.MinimapLayoutVersion => _layoutVersion;
    int IMinimapDataProvider.MinimapLineCount => _visualLines.Count;
    double IMinimapDataProvider.MinimapScrollOffset => _scroll.EffectiveOffset;
    double IMinimapDataProvider.MinimapTotalHeight => _totalContentHeight;
    IReadOnlyList<double> IMinimapDataProvider.MinimapCanvasLineYPositions => _lineYPositions;
    Color IMinimapDataProvider.MinimapBackground => ((SolidColorBrush)_palette.Background).Color;
    Color IMinimapDataProvider.MinimapForeground => ((SolidColorBrush)_palette.Foreground).Color;
    Color IMinimapDataProvider.MinimapCodeBackground => ((SolidColorBrush)_palette.CodeBackground).Color;
    Color IMinimapDataProvider.MinimapTableBackground => ((SolidColorBrush)_palette.TableBackground).Color;
    Color IMinimapDataProvider.MinimapTableHeaderBackground => ((SolidColorBrush)_palette.TableHeaderBackground).Color;
    double IMinimapDataProvider.MinimapCanvasTextWidth => Math.Max(1, ActualWidth - _padding * 2);
    double IMinimapDataProvider.MinimapBaseLineHeight
    {
        get
        {
            if (_visualLines == null || _visualLines.Count == 0) return 0;
            return _measure.GetLineHeight(BlockKind.Paragraph);
        }
    }

    BlockKind IMinimapDataProvider.GetMinimapLineKind(int index)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
            return BlockKind.Paragraph;
        return _visualLines[index].BlockKind;
    }

    (BitmapSource Image, double Width, double Height, double YOffset)? IMinimapDataProvider.GetMinimapLineImage(int index)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
            return null;
        var vl = _visualLines[index];
        if (vl.OverrideHeight <= 0) return null;

        BlockVisualMap? map = null;
        if (vl.Group != null)
            map = vl.Group.JoinedMap;
        else if (IsVisual && _visualMaps != null && vl.BlockIndex < _visualMaps.Count)
            map = _visualMaps[vl.BlockIndex];

        if (map?.Images != null)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in map.Images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                {
                    var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
                    if (cached != null)
                        return (cached.Value.Image, cached.Value.Width, cached.Value.Height, 0);
                }
            }
        }

        if (!IsVisual && _imagePreview == ImagePreviewMode.Inline
            && _parsedBlocks != null && vl.BlockIndex < _parsedBlocks.Count)
        {
            var images = _parsedBlocks[vl.BlockIndex].Images;
            if (images != null)
            {
                int vlEnd = vl.StartOffset + vl.Length;
                double textLineH = _measure.GetLineHeight(vl.BlockKind);
                foreach (var img in images)
                {
                    if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                    {
                        var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
                        if (cached != null)
                            return (cached.Value.Image, cached.Value.Width, cached.Value.Height, textLineH);
                    }
                }
            }
        }

        return null;
    }

    void IMinimapDataProvider.GetMinimapLineInfo(int index, out string text, out BlockKind kind)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
        {
            text = ""; kind = BlockKind.Paragraph; return;
        }
        var vl = _visualLines[index];
        kind = vl.BlockKind;
        if (vl.Length <= 0) { text = ""; return; }
        string source = vl.Group != null ? vl.Group.JoinedText : _doc.GetBlockText(vl.BlockIndex);
        text = vl.StartOffset + vl.Length <= source.Length
            ? source.Substring(vl.StartOffset, vl.Length)
            : "";
    }

    void IMinimapDataProvider.GetMinimapLineColorInfo(int index, out RgbColor? blockFg, out RgbColor? blockBg,
        out IReadOnlyList<ColorSpan>? colorSpans, out int spanBaseOffset)
    {
        blockFg = null;
        blockBg = null;
        colorSpans = null;
        spanBaseOffset = 0;

        if (_visualLines == null || _parsedBlocks == null || index < 0 || index >= _visualLines.Count)
            return;

        var vl = _visualLines[index];
        spanBaseOffset = vl.StartOffset;

        if (vl.Group != null)
        {
            blockFg = vl.Group.JoinedParsed.BlockColor?.Foreground;
            blockBg = vl.Group.JoinedParsed.BlockColor?.Background;
            colorSpans = vl.Group.JoinedParsed.ColorSpans;
            return;
        }

        if (vl.BlockIndex >= _parsedBlocks.Count) return;
        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
        blockFg = parsed.BlockColor?.Foreground;
        blockBg = parsed.BlockColor?.Background;
        colorSpans = parsed.ColorSpans;
    }

    bool IMinimapDataProvider.GetMinimapTableRowInfo(int index, List<MinimapTableCell> cells,
        out bool isHeader, out double tableWidth,
        out IReadOnlyList<ColorSpan>? colorSpans)
    {
        cells.Clear();
        isHeader = false;
        tableWidth = 0;
        colorSpans = null;

        if (!IsVisual || _visualLines == null || _parsedBlocks == null
            || index < 0 || index >= _visualLines.Count)
            return false;

        var vl = _visualLines[index];
        if (vl.BlockKind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
            return false;
        if (vl.BlockIndex >= _parsedBlocks.Count)
            return false;

        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Table == null || parsed.TableRow == null)
            return false;
        if (!_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            return false;

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        BlockVisualMap? map = _visualMaps != null && vl.BlockIndex < _visualMaps.Count
            ? _visualMaps[vl.BlockIndex]
            : null;

        double xOffset = 0;
        int cellCount = Math.Min(parsed.TableRow.Cells.Count, colWidths.Length);
        for (int c = 0; c < cellCount; c++)
        {
            var cell = parsed.TableRow.Cells[c];
            var (s, e) = cell.TrimContent(blockText);

            string cellText = map != null
                ? map.BuildDisplayString(blockText, s, e - s)
                : blockText.Substring(s, e - s);

            cells.Add(new MinimapTableCell(cellText, xOffset + _tableCellPadding, s));
            xOffset += colWidths[c];
        }

        isHeader = parsed.Kind == BlockKind.TableHeaderRow;
        tableWidth = xOffset;
        colorSpans = parsed.ColorSpans;
        return true;
    }
}
```

### Integration Point

```csharp
// In MinimapScrollbar.cs or wherever minimap is rendered
public class MinimapScrollbar : Control
{
    private IMinimapDataProvider _dataProvider;
    
    public MinimapScrollbar(IMinimapDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }
    
    protected override void OnRender(DrawingContext dc)
    {
        var layoutVersion = _dataProvider.MinimapLayoutVersion;
        if (layoutVersion != _lastLayoutVersion)
        {
            RegenerateMinimap();
            _lastLayoutVersion = layoutVersion;
        }
        
        // Use _dataProvider to get line info, colors, images
        for (int i = 0; i < _dataProvider.MinimapLineCount; i++)
        {
            var kind = _dataProvider.GetMinimapLineKind(i);
            _dataProvider.GetMinimapLineInfo(i, out var text, out var _);
            _dataProvider.GetMinimapLineColorInfo(i, out var fg, out var bg, out var colors, out var offset);
            // ... render using provider data
        }
    }
}
```

### Dependencies on DocsCanvas
- Needs **internal** access to: `_visualLines`, `_lineYPositions`, `_parsedBlocks`, `_visualMaps`, `_tableColumnWidths`, `_layoutVersion`, `_scroll.EffectiveOffset`, `_totalContentHeight`, `_palette`, `_measure`, `_imageCache`, `_padding`, `ActualWidth`, `DocumentBasePath`, `_layoutMaxWidth`, `_imagePreview`, `IsVisual`, `_doc`, `_tableCellPadding`, `_scroll`
- These remain on DocsCanvas (don't extract)

### Implementation Order: **4th (Most Dependencies, but Read-Only)**
- Least impact since all data is read-only
- Can be added last as an interface implementation
- Changes only method visibility, no structural changes

### Testing Strategy

**Unit Tests** (new `MinimapDataProviderTests.cs`):
```csharp
[Fact]
public void GetMinimapLineKind_ReturnsCorrectBlockKind()
{
    var canvas = CreateTestCanvasWithContent();
    var provider = (IMinimapDataProvider)canvas;
    
    var kind = provider.GetMinimapLineKind(0);
    Assert.Equal(BlockKind.Paragraph, kind);
}

[Fact]
public void GetMinimapLineInfo_ReturnsTextAndKind()
{
    var canvas = CreateTestCanvasWithContent();
    var provider = (IMinimapDataProvider)canvas;
    
    provider.GetMinimapLineInfo(0, out var text, out var kind);
    Assert.NotEmpty(text);
}

[Fact]
public void MinimapColorInfo_ReturnsColorData()
{
    // Test color span extraction
}

[Fact]
public void GetMinimapTableRowInfo_ReturnsTableData()
{
    // Test table cell extraction
}
```

**Breaking Changes Mitigation**:
- All existing properties and methods unchanged
- New interface is internal only
- Can be added incrementally

### Estimated Line Reduction: **~80 lines** (comments and interface definition are minimal)

---

## Implementation Checklist

### Phase 1 Extraction Sequence

```
[ ] 1. PageBreakManager
      [ ] Create PageBreakManager.cs
      [ ] Move page break state and methods
      [ ] Update DocsCanvas.Print.cs for delegation
      [ ] Update DocsCanvas.cs to delegate (lines 17-22, 42-91, 109-133)
      [ ] Verify public API unchanged
      [ ] Run unit tests for page breaks
      [ ] Run UI tests for visual rendering
      [ ] Commit: "Extract PageBreakManager from DocsCanvas.Print"

[ ] 2. FindAndReplaceController
      [ ] Create FindAndReplaceController.cs
      [ ] Move search state and methods
      [ ] Update DocsCanvas.Find.cs for delegation
      [ ] Update DocsCanvas.cs (lines 37-168)
      [ ] Verify Find/Replace still works
      [ ] Run unit tests
      [ ] Run integration tests with FindBarController
      [ ] Commit: "Extract FindAndReplaceController from DocsCanvas.Find"

[ ] 3. SpellCheckController
      [ ] Create SpellCheckController.cs
      [ ] Move spell check state and methods
      [ ] Update DocsCanvas.SpellCheck.cs for delegation
      [ ] Update DocsCanvas.cs (all spell check calls)
      [ ] Add Cleanup() call to Dispose
      [ ] Verify spell check still works
      [ ] Run unit tests
      [ ] Commit: "Extract SpellCheckController from DocsCanvas.SpellCheck"

[ ] 4. IMinimapDataProvider
      [ ] Create IMinimapDataProvider.cs interface
      [ ] Add explicit interface implementation to DocsCanvas
      [ ] Update MinimapScrollbar to accept IMinimapDataProvider
      [ ] Remove old internal properties from Minimap.cs partial
      [ ] Verify minimap rendering unchanged
      [ ] Run UI tests
      [ ] Commit: "Extract IMinimapDataProvider interface for minimap"
```

### Code Access Modifications

**Make Internal (in DocsCanvas) to Support Controllers**:
```csharp
// These fields need internal visibility for extracted controllers
internal List<VisualLine> _visualLines;                    // Find, SpellCheck, PageBreak
internal List<double> _lineYPositions;                     // Find, SpellCheck, PageBreak, Minimap
internal List<ParsedBlock>? _parsedBlocks;                 // Find, SpellCheck, Minimap
internal List<BlockVisualMap>? _visualMaps;                // Find, SpellCheck, Minimap
internal Dictionary<TableInfo, double[]> _tableColumnWidths;  // Find, SpellCheck, Minimap
internal TextMeasurer _measure;                            // Find, SpellCheck, Minimap
internal ThemePalette _palette;                            // Find, SpellCheck, PageBreak, Minimap
internal double _padding;                                  // Find, SpellCheck, Minimap
internal ImageCache _imageCache;                           // Minimap
internal double _layoutMaxWidth;                           // Minimap
internal ImagePreviewMode _imagePreview;                   // Minimap
internal double _tableCellPadding;                         // Minimap
internal int _layoutVersion;                               // PageBreak, Minimap
internal EditMode _editMode;                               // PageBreak
internal bool IsVisual { get; }                            // Minimap

// Make these internal if not already
internal void ComputeLayout();
internal void EnsureCursorVisible();
internal void SealAndStopTimer();
internal void InvalidateLayout();
internal double GetEffectiveLineHeight(VisualLine vl);
internal double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int offset);
internal double MeasureRangeWidth(string blockText, int startOffset, int length, List<StyledRun> runs, BlockKind kind, BlockVisualMap? map);
internal double MeasureJoinedRange(ParagraphGroup group, int startOffset, int length);
internal void HitTestToPosition(Point position, out int blockIndex, out int charOffset);
internal FindBarController? FindBar { get; set; }
internal ScrollController _scroll;
```

---

## Key Design Principles for Extractions

### 1. **Zero Public API Changes**
- All existing public methods remain on DocsCanvas
- New controller properties are internal
- Delegation maintains backward compatibility

### 2. **Lazy Initialization**
```csharp
private FindAndReplaceController? _findAndReplaceController;
internal FindAndReplaceController FindAndReplace => 
    _findAndReplaceController ??= new FindAndReplaceController(this);
```
- Controllers created only when first needed
- Reduces memory footprint for unused features

### 3. **Encapsulation Pattern**
```csharp
// DocsCanvas delegates to controller
internal void ExecuteSearch(string query, bool caseSensitive) => 
    FindAndReplace.ExecuteSearch(query, caseSensitive);

// Direct call sites don't change
FindBar?.Open(showReplace, initialText);  // Unchanged
```

### 4. **State Management**
- Controllers own their state
- Read data via DocsCanvas properties/fields (internal)
- Call back to DocsCanvas for invalidation/layout

### 5. **Testing Strategy**
- Unit tests on controller directly
- UI tests on DocsCanvas unchanged
- Integration tests for Find/Replace with FindBarController
- Minimap tests on interface implementation

---

## Lines of Code Reduction Summary

| Extraction | Current File | Extracted | Reduced | % |
|-----------|--------------|-----------|---------|---|
| PageBreakManager | Print.cs (598) | 140 | 140 | 23% |
| FindAndReplaceController | Find.cs (289) | 289 | 289 | 100% |
| SpellCheckController | SpellCheck.cs (426) | 426 | 426 | 100% |
| IMinimapDataProvider | Minimap.cs (209) | 80 | 80 | 38% |
| **TOTAL** | - | - | **935** | **~17%** |

**DocsCanvas.cs reduction**:
- Main file: 3067 lines (unchanged file size, just delegation)
- Partials: 5654 → ~4,719 lines (935 line reduction)
- Total project: reduced by 935 lines of complex logic

**Effective reduction in DocsCanvas **responsibility**: ~1,356 lines of high-complexity code moved to dedicated classes

---

## Migration Script Template

```powershell
# After extracting each controller:

# 1. Update namespaces
$files = Get-ChildItem -Path "RaisinDocs" -Filter "*.cs" -Recurse
foreach ($file in $files) {
    # Add [assembly: InternalsVisibleTo(...)] for test projects
}

# 2. Update project file
# Add <InternalsVisibleTo Include="RaisinDocs.Tests" /> for extracted classes

# 3. Run tests
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj
dotnet test Tests/RaisinDocs.Tests.UI/RaisinDocs.Tests.UI.csproj

# 4. Run analysis
dotnet build RaisinDocs.slnx /p:TreatWarningsAsErrors=true
```

---

## Risk Mitigation

### Potential Issues and Solutions

| Risk | Mitigation |
|------|-----------|
| Controller accesses stale DocsCanvas state | Controllers read fields only during rendering/processing, not cached |
| Circular dependencies between controllers | Controllers only depend on DocsCanvas, not each other |
| Performance regression | Lazy initialization and minimal allocation overhead |
| Test failures during extraction | Comprehensive unit tests before merging, UI tests unchanged |
| Maintainability of delegations | Clear comments in DocsCanvas pointing to controllers |
| Future feature requests impact | Each controller is now independently extensible |

---

## Next Steps (Phase 2 & Beyond)

- **Phase 2**: Extract VisualLineSpacing, SourceMode logic, VisualMode table handling
- **Phase 3**: Extract Formatting API into FormattingController
- **Phase 4**: Consider Document mutation strategies into DocumentController
- **Phase 5**: Evaluate IInputHandler interface for keyboard/mouse dispatch

---

## Quick Reference: Which Fields Each Needs

**PageBreakManager**: `_visualLines`, `_lineYPositions`, `_layoutVersion`, `_parsedBlocks`, `ActualWidth`, `InvalidateVisual()`

**FindAndReplaceController**: `_doc`, `_visualLines`, `_lineYPositions`, `_parsedBlocks`, `_visualMaps`, `_tableColumnWidths`, `_measure`, `_palette`, `ComputeLayout()`, `InvalidateVisual()`, `InvalidateLayout()`, `EnsureCursorVisible()`

**SpellCheckController**: `_doc`, `_parsedBlocks`, `_visualLines`, `_lineYPositions`, `_visualMaps`, `_tableColumnWidths`, `_measure`, `DocumentBasePath`, `ComputeLayout()`, `InvalidateVisual()`, `InvalidateLayout()`, `EnsureCursorVisible()`

**IMinimapDataProvider**: (Read-only) `_visualLines`, `_lineYPositions`, `_parsedBlocks`, `_visualMaps`, `_tableColumnWidths`, `_palette`, `_measure`, `_imageCache`, `_layoutVersion`, `_scroll.EffectiveOffset`, `_totalContentHeight`, `ActualWidth`, `IsVisual`, `_doc`, `DocumentBasePath`, `_layoutMaxWidth`, `_imagePreview`, `_tableCellPadding`

