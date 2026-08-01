# DocsCanvas God Class Analysis & Refactoring Strategy

## Executive Summary

**DocsCanvas is a classic god class with ~8,721 lines of code across 10 partial class files.** It handles rendering, layout, input, formatting, search, spell-check, printing, and more. While the partial organization is helpful, the core problem remains: too many responsibilities in one class sharing tightly coupled state.

---

## Part 1: Current Architecture Breakdown

### Line Count Distribution

| File | Lines | Responsibility |
|------|-------|-----------------|
| DocsCanvas.cs | 3,067 | Core rendering, layout, theme, document access |
| DocsCanvas.VisualMode.cs | 1,725 | Table rendering/nav, visual mode cursor logic, link handling |
| DocsCanvas.Input.cs | 1,495 | Mouse/keyboard input, cursor movement, selection |
| DocsCanvas.Formatting.cs | 712 | Bold/italic/heading toggles, links, tables, reflow |
| DocsCanvas.Print.cs | 598 | Page breaks, pagination |
| DocsCanvas.SpellCheck.cs | 426 | Spell checking service integration |
| DocsCanvas.Find.cs | 289 | Find/replace, search highlighting |
| DocsCanvas.Minimap.cs | 209 | Minimap integration (mostly property exposure) |
| DocsCanvas.SourceMode.cs | 114 | Source mode inline image preview |
| DocsCanvas.Toc.cs | 86 | TOC panel integration (mostly property exposure) |
| **TOTAL** | **~8,721** | |

---

## Part 2: Responsibility Analysis

### Tier 1: Core Rendering & Layout (Primary)

**~2,500 lines in DocsCanvas.cs**

**Responsibilities:**
- **Theme/Palette**: 3 static palettes, color management (50 lines)
- **Layout Pipeline**: `ComputeLayout()`, `ComputeLayoutCore()`, `BuildParagraphGroups()`, `WrapSegment()`, `FitLine()` (~800 lines)
- **Rendering**: `OnRender()`, `DrawJoinedLine()`, style application, selection drawing (~600 lines)
- **Cursor & Navigation**: Visual line computation, hit-testing, cursor positioning (~400 lines)
- **Text Measurement**: TextMeasurer caching, character width calculation (~100 lines)
- **Scroll Management**: Scroll anchor, smooth scrolling (~150 lines)

**Cohesion**: VERY HIGH - these are deeply interdependent. Layout output feeds directly into rendering input. Cursor positioning depends on layout. Cannot easily separate.

**Usage Pattern**: This forms the "rendering engine" - everything depends on it.

---

### Tier 2: Mode-Specific Logic (Medium Primary)

#### Visual Mode Features (1,725 lines)
**Responsibilities:**
- **Table Rendering**: `DrawTableRow()`, column width computation, cell dimensions (~400 lines)
- **Table Navigation**: Hit-testing within tables, rectangular selection, cell navigation (~300 lines)
- **Visual Mode Cursor**: `SkipCursorOverHiddenRanges()`, `ClampCursorBeforeTrailingHidden()`, `SkipCursorToVisible()` (~200 lines)
- **Link Handling**: Link detection, hovering, opening (`TryOpenLinkAtClick()`, etc.) (~200 lines)
- **Task List Checkboxes**: Visual mode checkbox toggling (~100 lines)
- **Helper Methods**: Various visual mode specific utilities (~500 lines)

**Cohesion**: MEDIUM - tables are somewhat isolated, but cursor skipping is deeply tied to BlockVisualMap and layout. Link handling mixes visual rendering concerns with click logic.

**Seams**: 
- Table subsystem could extract to `TableRenderer` + `TableNavigator`
- Visual cursor skipping is a behavior layer on top of layout
- Link detection is layout-agnostic and could move to Formatting/Input

#### Source Mode Features (114 lines)
**Responsibilities:**
- Inline image preview in source mode only

**Cohesion**: LOW - could be part of image preview system

#### Input Handling (1,495 lines)
**Responsibilities:**
- **Mouse Events**: Click, drag, double-click, wheel (~300 lines)
- **Keyboard Events**: Key routing, dispatch (~150 lines)
- **Cursor Navigation**: Page up/down, arrow keys, word navigation (~400 lines)
- **Selection**: Selection manipulation, rectangular table selection (~300 lines)
- **Text Insertion**: Keyboard text input (~200 lines)
- **Key Handlers**: Specialized handlers for Delete, Backspace, Enter, Tab, etc. (~200 lines)

**Cohesion**: MEDIUM-HIGH - all input routing goes here, but heavily depends on cursor positioning (which is in core) and layout. Click/drag needs hit-testing from core rendering.

**Seams**:
- Could split: `InputRouter` (low-level event dispatch) vs. `NavigationController` (cursor movement)
- Selection is somewhat independent

---

### Tier 3: Editing Features (Secondary)

#### Formatting API (712 lines)
**Responsibilities:**
- Inline style toggles (Bold, Italic, Code, Strikethrough)
- Block prefix toggles (Heading, Lists, Blockquote)
- Link insertion (with UI popup)
- Color insertion (inline & block tags)
- Table insertion
- Reflow and reformatting (`ReflowAll()`, `ConvertToHardBreaks()`)
- Query methods (IsSelectionBold, etc.)

**Cohesion**: MEDIUM - many methods are independent operations on document, but some (like reflow) need layout knowledge.

**Seams**: Could extract to `FormattingEngine` or keep but separate concerns:
- Pure document mutations (toggle bold) vs. Layout-aware operations (reflow)

---

### Tier 4: Supplementary Features (Tertiary)

#### Find/Replace (289 lines)
**State**: `_searchMatches`, `_currentMatchIndex`, `_lastSearchQuery`
**Entry points**: `OpenFind()`, `CloseFind()`, `ExecuteSearch()`, `ReplaceNext()`, etc.

**Cohesion**: LOW-MEDIUM - completely independent, only needs to highlight matches during rendering.

**Seams**: ✓ GOOD CANDIDATE FOR EXTRACTION → `FindAndReplaceController`

#### Spell Check (426 lines)
**State**: `_spellCheckEnabled`, `_spellCheckService`, `_blockSpellingErrors`, timer
**Entry points**: `SetSpellCheckEnabled()`, `SetProjectFolder()`

**Cohesion**: LOW - completely independent service integration. Only touches rendering (spell error underlines).

**Seams**: ✓ GOOD CANDIDATE FOR EXTRACTION → `SpellCheckController`

#### Printing (598 lines)
**State**: `_showPageBreaks`, `_pageBreakYs`, `_pageBreakLayoutVersion`
**Entry points**: `SetShowPageBreaks()`, page break computation

**Cohesion**: LOW - page break computation is independent of rendering.

**Seams**: ✓ GOOD CANDIDATE FOR EXTRACTION → `PageBreakManager`

#### Minimap (209 lines)
**State**: Just property exposure for minimap component
**Entry points**: Properties like `MinimapCanvasLineYPositions`, `MinimapScrollOffset`

**Cohesion**: VERY LOW - pure data pass-through to external Minimap component.

**Seams**: ✓ GOOD CANDIDATE FOR EXTRACTION → `IMinimapDataProvider` interface

#### TOC (86 lines)
**State**: Just property/method delegation
**Entry points**: `ToggleToc()`, properties

**Cohesion**: VERY LOW - pure delegation to external TOC panel.

**Seams**: ✓ EASY TO EXTRACT → `ITocIntegration` interface

---

## Part 3: Shared State Analysis

### Critical Shared State (cannot be easily separated)

```csharp
// Layout output - used by rendering, cursor nav, input handling
private List<VisualLine> _visualLines;
private List<double> _lineYPositions;
private double _totalContentHeight;
private int _layoutVersion;

// Document reference - used by nearly everything
private Document _doc;

// Measurement - used by layout and rendering
private TextMeasurer _measure;

// Scroll state - used by input, rendering, navigation
private ScrollController _scroll;

// Parsed blocks & visual maps - used by layout, rendering, cursor nav
private List<ParsedBlock>? _parsedBlocks;
private List<BlockVisualMap>? _visualMaps;
private Dictionary<TableInfo, double[]>? _tableColumnWidths;
```

**Coupling**: These form a "core context" that everything depends on. Separating them requires:
1. Creating a shared `RenderingContext` or `LayoutState` object that all subsystems can reference
2. OR using dependency injection to pass these into extracted components
3. OR keeping them in DocsCanvas but hiding implementation behind interfaces

### Loosely Coupled State (can be extracted)

```csharp
// Find/Replace
private List<SearchMatch> _searchMatches;
private int _currentMatchIndex;
private string _lastSearchQuery;

// Spell Check
private bool _spellCheckEnabled;
private SpellCheckService? _spellCheckService;
private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;

// Page Breaks
private List<double> _pageBreakYs;
private int _pageBreakLayoutVersion;

// Selection and input
private bool _doubleClickDrag;
private int _doubleClickBlock;
private (string Marker, InlineStyle Style)? _pendingStyleOff;
```

---

## Part 4: Dependency Patterns

### High Dependency (Core Tier)
```
Input → Layout ← Rendering
  ↓      ↓        ↑
  └─ Document ──→ Layout ─→ Rendering
  └─ Cursor Navigation ← Layout
```

**These must stay tightly integrated or be refactored as a unit.**

### Medium Dependency (Formatting & Visual Mode)
```
Formatting → Document → Layout
             ↓          ↓
          Cursor Nav  Rendering

Visual Mode → Layout + Cursor Nav + Document
```

### Low Dependency (Supplementary)
```
Find ──→ (only needs rendering callback)
  ↓
Rendering

SpellCheck ──→ (only needs rendering callback)
  ↓
Rendering

Minimap ──→ (needs property access to layout state)
Toc ──→ (needs property access to layout state)
Print ──→ (needs layout state for page breaks)
```

---

## Part 5: Proposed Refactoring Strategy

### Phase 1: Extract Completely Independent Features (Quick Wins)
**Effort**: LOW | **Risk**: MINIMAL | **Dependencies**: Few

#### 1.1 Extract `PageBreakManager`
- **Extract**: Print.cs page break logic
- **Interface**: Exposes `PageBreaks`, `ComputePageBreakPositions()`
- **Dependencies**: Needs read access to layout state (VisualLine list, line heights)
- **Impact**: DocsCanvas.Print.cs (598 lines) → external class

**Implementation**:
```csharp
internal class PageBreakManager
{
    private List<double> _pageBreaks;
    private int _layoutVersion;
    
    public void Update(IReadOnlyList<VisualLine> visualLines, 
                       IReadOnlyList<double> lineYPositions,
                       int layoutVersion, double pageHeight)
    { /* compute breaks */ }
    
    public IReadOnlyList<double> PageBreaks => _pageBreaks;
}
```

#### 1.2 Extract `FindAndReplaceController`
- **Extract**: Find.cs search logic
- **Interface**: Exposes `ExecuteSearch()`, `ReplaceNext()`, `Matches`, `CurrentMatchIndex`
- **Dependencies**: Needs document mutations, rendering callback
- **Impact**: DocsCanvas.Find.cs (289 lines) → external class

**Implementation**:
```csharp
internal class FindAndReplaceController
{
    public event Action<SearchMatch[]>? MatchesChanged;
    
    public void ExecuteSearch(ITextSource source, string query, bool caseSensitive)
    { /* find all */ }
    
    public void ReplaceNext(ITextMutator mutator) { /* replace */ }
}
```

#### 1.3 Extract `SpellCheckController`
- **Extract**: SpellCheck.cs spell checking logic
- **Interface**: Exposes `Enable()`, `ProjectFolder`, `GetErrorsForBlock()`
- **Dependencies**: Background timer, spell checking service
- **Impact**: DocsCanvas.SpellCheck.cs (426 lines) → external class

**Implementation**:
```csharp
internal class SpellCheckController
{
    public event Action? ErrorsChanged;
    
    public void Enable(string? projectFolder) { /* init */ }
    public IReadOnlyList<SpellingError>? GetErrorsForBlock(int blockIndex) { /* */ }
}
```

#### 1.4 Create `IMinimapDataProvider` Interface
- **Extract**: Minimap.cs property exposure
- **Impact**: Decouple Minimap from DocsCanvas internals
- **Benefit**: Minimap only depends on interface, not implementation

```csharp
internal interface IMinimapDataProvider
{
    int LineCount { get; }
    double ScrollOffset { get; }
    IReadOnlyList<double> LineYPositions { get; }
    Color ForegroundColor { get; }
    // ... 8-10 more properties
}

// DocsCanvas implements this interface
public partial class DocsCanvas : IMinimapDataProvider { }
```

#### 1.5 Create `ITocDataProvider` Interface
- **Extract**: Toc.cs delegation logic
- **Similar to Minimap**: Pure data pass-through

### Phase 2: Extract Mode-Specific Logic (Medium Complexity)
**Effort**: MEDIUM | **Risk**: MEDIUM | **Dependencies**: Layout-dependent

#### 2.1 Extract `TableRenderer` & `TableNavigator`
- **Extract**: Visual mode table drawing, column width computation, cell navigation (from DocsCanvas.VisualMode.cs)
- **Keep in DocsCanvas**: Core table hit-testing, cursor clamp logic (depends on cursor position)
- **Dependencies**: Needs layout state, visual maps, document
- **Impact**: ~400 lines extracted, but still tightly coupled

**Why separate**:
- Table rendering is distinct from text rendering
- Table navigation (cell movement, selection) is distinct from text input
- Both can have dedicated test files

**Implementation**:
```csharp
internal class TableRenderer
{
    public void DrawTableRow(DrawingContext dc, VisualLine vl, ParsedBlock parsed, 
                             IReadOnlyList<VisualLine> visualLines, /* more params */);
    public double[] ComputeColumnWidths(TableInfo table, double maxWidth);
}

internal class TableNavigator
{
    public bool TryNavigateTableCell(Key key, Cursor cursor);
    public bool TryMakeRectangularSelection(/* params */);
}
```

#### 2.2 Extract `VisualModeManager`
- **Extract**: Visual mode cursor skipping, text display string building (from DocsCanvas.VisualMode.cs)
- **Interface**: Handles visual-mode-specific cursor behavior, stays in visual mode
- **Dependencies**: BlockVisualMap, Document, Layout state
- **Impact**: ~200 lines extracted

```csharp
internal class VisualModeManager
{
    public void SkipCursorOverHiddenRanges(Cursor cursor, bool forward);
    public void EnsureCursorOnVisibleBlock(Cursor cursor, IList<ParsedBlock> parsed);
    public void ClampCursorBeforeTrailingHidden(Cursor cursor, ParsedBlock parsed);
}
```

#### 2.3 Extract `LinkHandler`
- **Extract**: Link detection, opening, tooltip management (from DocsCanvas.VisualMode.cs + Input.cs)
- **Interface**: Link hit-test, open, hover tracking
- **Dependencies**: Parsed blocks, cursor
- **Impact**: ~150 lines extracted

```csharp
internal class LinkHandler
{
    public InlineLink? GetLinkAtPosition(Point pos, /* layout params */);
    public void OpenLink(string url);
    public void UpdateHover(Point pos, /* layout params */);
}
```

### Phase 3: Refactor Core Rendering & Layout (High Complexity)
**Effort**: HIGH | **Risk**: HIGH | **Payoff**: HIGH

This is the most complex part because rendering, layout, and input are tightly coupled.

#### 3.1 Create `LayoutEngine` Class
- **Extract**: Layout computation logic into a separate, testable class
- **Current State**: `ComputeLayout()`, `ComputeLayoutCore()`, `BuildParagraphGroups()`, wrapping, fitting (~1400 lines)
- **Goal**: Make layout computation independent, testable without WPF

**Boundaries**:
- **Stays in DocsCanvas**: Viewport measurements, scroll offset calculations
- **Moves to LayoutEngine**: Block classification, line wrapping, paragraph joining, visual line generation

**Implementation**:
```csharp
internal class LayoutEngine
{
    private TextMeasurer _measure;
    private List<VisualLine> _visualLines = new();
    
    public void ComputeLayout(
        Func<int, string> getBlockText,
        int blockCount,
        double maxWidth,
        List<ParsedBlock> parsedBlocks,
        ImageCache imageCache)
    {
        // Move all computation here
    }
    
    public IReadOnlyList<VisualLine> VisualLines => _visualLines;
}
```

**Benefits**:
- Testable without OnRender/WPF
- Easier to reason about
- Can be unit tested with mock TextMeasurer
- ~1400 lines moved out of 3067-line core file

**Remaining in DocsCanvas**: Document connection, scroll state, rendering loop

#### 3.2 Create `CursorNavigationEngine`
- **Extract**: Cursor positioning, hit-testing, navigation (~400 lines)
- **Current Location**: Spread across DocsCanvas.cs + Input.cs
- **Dependencies**: VisualLines, Cursor state, Layout state, parsed blocks

**Implementation**:
```csharp
internal class CursorNavigationEngine
{
    public int CursorToVisualLineIndex(Cursor cursor, IReadOnlyList<VisualLine> visualLines);
    public int HitTestVisualLine(double y, IReadOnlyList<double> lineYPositions);
    public int HitTestToPosition(Point pos, IReadOnlyList<VisualLine> visualLines, /* more */);
}
```

#### 3.3 Create `RenderingContext` Shared State Object
- **Purpose**: Eliminate passing 15+ parameters around
- **Contains**: VisualLines, LineYPositions, LayoutState, TextMeasurer, etc.

```csharp
internal class RenderingContext
{
    public required List<VisualLine> VisualLines { get; init; }
    public required List<double> LineYPositions { get; init; }
    public required List<ParsedBlock> ParsedBlocks { get; init; }
    public required List<BlockVisualMap> VisualMaps { get; init; }
    public required TextMeasurer Measure { get; init; }
    public required ThemePalette Palette { get; init; }
    
    // Other rendering-critical state
}
```

**Benefit**: Reduces parameter chaining, makes layout/render dependencies explicit.

#### 3.4 Extract Input Routing into `InputDispatcher`
- **Extract**: `OnMouseDown()`, `OnKeyDown()`, `OnTextInput()` dispatch logic
- **Keep in DocsCanvas**: Event handler overrides (WPF requirement)
- **Move to InputDispatcher**: Business logic for handling each event

```csharp
internal class InputDispatcher
{
    public void HandleMouseDown(MouseButtonEventArgs e, /* context */);
    public void HandleMouseMove(MouseEventArgs e, /* context */);
    public void HandleKeyDown(KeyEventArgs e, /* context */);
}
```

---

## Part 6: Implementation Roadmap

### Priority 1: Quick Wins (1-2 weeks)
1. **Extract PageBreakManager** - 598 lines, ~2 days, minimal risk
2. **Extract FindAndReplaceController** - 289 lines, ~2 days, minimal risk
3. **Extract SpellCheckController** - 426 lines, ~2 days, minimal risk
4. **Create IMinimapDataProvider interface** - simplify Minimap.cs dependency, ~1 day

**Result**: ~1,313 lines extracted, DocsCanvas drops from 3,067 to ~1,750 in main file

### Priority 2: Mode-Specific (2-3 weeks)
5. **Extract TableRenderer** - 400 lines, ~3 days, medium risk
6. **Extract VisualModeManager** - 200 lines, ~2 days, medium risk
7. **Extract LinkHandler** - 150 lines, ~1 day, low risk

**Result**: ~750 lines extracted, DocsCanvas.VisualMode.cs shrinks significantly

### Priority 3: Core Refactoring (3-4 weeks)
8. **Extract LayoutEngine** - 1,400 lines, ~5 days, HIGH RISK
   - Requires careful testing
   - Must maintain cursor behavior during refactoring
   - Start with unit tests for layout computation
9. **Extract CursorNavigationEngine** - 400 lines, ~3 days, MEDIUM-HIGH RISK
10. **Create RenderingContext** - refactor parameter passing, ~2 days, MEDIUM RISK
11. **Extract InputDispatcher** - decouple input logic from WPF event handlers, ~2 days, MEDIUM RISK

**Result**: Core DocsCanvas drops from 3,067 to ~1,000 lines

---

## Part 7: Refactoring Approach (Minimizing Risk)

### Key Principles

1. **Maintain Public API** - No breaking changes to DocsCanvas public interface
2. **Incremental Extraction** - One component at a time, with tests after each
3. **Keep Coupling Where Necessary** - Don't force separation where cohesion is high
4. **Create Interfaces** - Let extracted classes depend on ITextMutator, ITextSource, not concrete Document
5. **Test-Driven** - Extract with comprehensive tests, not refactoring-after

### Safe Extraction Pattern

```csharp
// BEFORE: All in DocsCanvas
public partial class DocsCanvas : FrameworkElement
{
    private List<SearchMatch> _searchMatches;
    private int _currentMatchIndex;
    
    internal void ExecuteSearch(string query, bool caseSensitive) { /* */ }
}

// AFTER: Extracted, but still used by DocsCanvas
public partial class DocsCanvas : FrameworkElement
{
    private FindAndReplaceController _findReplace = new();
    
    internal void ExecuteSearch(string query, bool caseSensitive)
    {
        _findReplace.ExecuteSearch(_doc, query, caseSensitive);
        InvalidateVisual();
    }
    
    // Still delegates, but implementation is elsewhere
}

internal class FindAndReplaceController
{
    public event Action<SearchMatch[]>? MatchesChanged;
    private List<SearchMatch> _matches;
    
    public void ExecuteSearch(ITextSource source, string query, bool caseSensitive)
    {
        _matches = FindMatches(source, query, caseSensitive);
        MatchesChanged?.Invoke(_matches);
    }
}
```

### Testing Strategy

1. **Phase 1 Extractions**: Add unit tests to extracted classes (FindAndReplaceController, SpellCheckController, PageBreakManager)
2. **Phase 2 Extractions**: Add UI tests for table rendering/navigation, visual mode behavior
3. **Phase 3 Extractions**: Most critical - extensive integration tests before/after LayoutEngine extraction

---

## Part 8: Architectural Benefits

### After Full Refactoring

**DocsCanvas.cs**: ~1,000 lines
- Event handlers (OnRender, OnKeyDown, OnMouseDown, etc.)
- Document access layer
- High-level orchestration
- Theme management
- Public API delegation

**Extracted Components** (~7,700 lines → dedicated, testable classes):
- LayoutEngine (1,400 lines) - Unit testable
- CursorNavigationEngine (400 lines) - Unit testable
- InputDispatcher (300 lines) - Unit testable
- TableRenderer + TableNavigator (400 lines) - UI testable
- VisualModeManager (200 lines) - Unit testable
- LinkHandler (150 lines) - Unit testable
- FindAndReplaceController (289 lines) - Unit testable
- SpellCheckController (426 lines) - Unit testable
- PageBreakManager (598 lines) - Unit testable

### Key Improvements

| Aspect | Before | After |
|--------|--------|-------|
| **Lines in main class** | 3,067 | ~1,000 |
| **Total lines** | 8,721 | 8,500 (refactoring cost) |
| **Unit-testable components** | 0 (WPF-bound) | 8 classes |
| **Partial files** | 10 | 8 (consolidate after extraction) |
| **Clear responsibilities** | 10 mixed | 15+ separated |
| **Maintenance difficulty** | High | Medium |
| **Onboarding time** | Very high | High |

---

## Part 9: Seams for Natural Separation

### Natural Boundaries (No Forcing Required)

1. **Find & Replace** ✓
   - Completely independent logic
   - Only touches rendering via callbacks
   - Current 289 lines

2. **Spell Check** ✓
   - Service integration pattern
   - Independent state machine
   - Current 426 lines

3. **Page Breaks** ✓
   - Operates on layout output only
   - No mutation, just analysis
   - Current 598 lines

4. **Minimap Integration** ✓
   - Pure data pass-through
   - No behavior coupling
   - Current 209 lines

### Loose Seams (Requires Some Refactoring)

5. **Table Rendering & Navigation**
   - Tables are isolated in visual mode
   - Hit-testing is somewhat coupled
   - Moderate extraction effort

6. **Visual Mode Cursor**
   - Depends on BlockVisualMap
   - Encodes visual mode semantics
   - Good seam for behavior extraction

7. **Link Handling**
   - Spread across multiple files
   - Depends on layout state
   - Could consolidate into single handler

### Strong Coupling (Not Recommended to Separate)

8. **Layout + Rendering + Cursor Navigation**
   - Interdependent by design
   - Separating requires shared context object
   - Better to keep as core unit, but make LayoutEngine independent

---

## Part 10: Migration Plan with Minimal Disruption

### Step 1: Establish Interfaces (Day 1)
```csharp
// New interfaces for dependency injection
public interface ITextSource { string GetText(); }
public interface ITextMutator { void InsertText(int block, int offset, string text); }
public interface ILayoutDataProvider { IReadOnlyList<VisualLine> VisualLines { get; } }
```

### Step 2: Extract Lowest Coupling Classes (Week 1)
- PageBreakManager
- FindAndReplaceController
- SpellCheckController
- Create Minimap & TOC data provider interfaces

### Step 3: Extract Medium Coupling (Week 2-3)
- TableRenderer
- LinkHandler
- VisualModeManager
- Consolidate partial files after extraction

### Step 4: Extract Core (Week 4-5)
- LayoutEngine (with extensive testing)
- CursorNavigationEngine
- Create RenderingContext shared state
- Measure reduction in DocsCanvas coupling

### Step 5: Cleanup (Week 6)
- InputDispatcher if needed
- Consolidate remaining partials
- Update documentation
- Profile performance (extraction shouldn't impact it)

---

## Conclusion

**DocsCanvas is a textbook god class**, but it's not beyond redemption. The solution isn't to break it into 30 classes (premature design), but to:

1. **Extract completely independent features** (25% of code) with no risk
2. **Extract mode-specific logic** (15% of code) with clear seams
3. **Refactor core rendering/layout** (60% of code) as a cohesive unit with better internal structure
4. **Keep high-cohesion components together** rather than force artificial separation

**Target outcome**: 
- DocsCanvas drops from 3,067 to ~1,000 lines
- 8 new, independently testable classes
- Core rendering engine remains tight but better organized
- Easier to maintain, extend, and onboard new developers

**Time estimate**: 4-6 weeks with comprehensive testing

**Risk level**: MEDIUM (only high during LayoutEngine extraction; other phases are low risk)
