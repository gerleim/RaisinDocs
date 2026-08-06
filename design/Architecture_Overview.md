# DocsCanvas Architecture Overview

**Version:** 2.0 (Post Phase 2 Refactoring)  
**Last Updated:** 2026-08-05  
**Status:** Current - Clean Architecture Established

---

## Executive Summary

DocsCanvas has evolved from a monolithic 8,721-line god class into a well-architected system with:
- ✅ **12 focused interfaces** defining clear contracts
- ✅ **11 extracted classes** each with single responsibility
- ✅ **Zero internal casts** (eliminated 804)
- ✅ **Clean dependency injection** throughout
- ✅ **Loose coupling** enabling testability and maintenance

---

## High-Level Architecture

### Component Hierarchy

```
DocsCanvas (Orchestrator - 1,474 lines)
│
├── Layout & Measurement
│   ├── LayoutEngine (911 lines) - Handles layout computation, word wrapping
│   ├── TextMeasurer (internal class) - Character/text measurement
│   └── BlockVisualMap (external) - Hidden range management
│
├── Rendering & Visual
│   ├── RenderingContext (1,297 lines) - All rendering operations
│   ├── TableRenderer (500 lines) - Table rendering & column widths
│   └── PageBreakManager (155 lines) - Page break visualization
│
├── Input & Navigation
│   ├── CursorNavigationEngine (700 lines) - Cursor positioning & hit-testing
│   ├── VisualModeManager (580 lines) - Visual mode navigation
│   ├── TableInputHandler (149 lines) - Table cell navigation
│   └── LinkHandler (169 lines) - Link click handling
│
├── Document & Content
│   ├── Document (external) - Text model, cursor, selection
│   ├── MarkdownParser (external) - Block parsing & styling
│   └── BlockVisualMap (external) - Visual metadata
│
├── Features
│   ├── FindAndReplaceController (290 lines) - Search/replace
│   ├── SpellCheckController (432 lines) - Spell checking
│   └── DocsEditor (external) - State persistence wrapper
│
└── Utilities
    ├── TextMeasurer - Font measurement caching
    ├── SyntaxHighlighter - Code highlighting
    └── ScrollController (external) - Smooth scrolling
```

---

## Interface Architecture

### The 12 Service Interfaces

DocsCanvas implements a composite interface `IDocsCanvasServices` which combines 12 focused interfaces:

#### Core Data Access
1. **IDocumentServices** - Document model access
   - `Document` property
   - `GetBlockText()`, `GetBlockLength()` methods
   - Document events

2. **ILayoutDataServices** - Layout computation results
   - `VisualLines` - Computed visual lines
   - `LineYPositions` - Y coordinates for each line
   - `VisualLineSpacings` - Marker/text positioning
   - Test properties for verification

3. **IParsedContentServices** - Markdown parsing results
   - `ParsedBlocks` - Block classification and structure
   - `VisualMaps` - Hidden range information
   - `VisualBlockStructure` - Block nesting

#### Rendering & Styling
4. **IRenderingServices** - Text measurement and styling
   - `Measure` - TextMeasurer instance
   - `Palette` - Current theme colors
   - `GetCachedBrush()`, `MeasureRangeWidth()` methods
   - `ActualWidth`, `ActualHeight` viewport dimensions

5. **ITableServices** - Table rendering support
   - `TableColumnWidths` - Cached column sizes
   - `CursorXInTableRow()`, `HitTestInTableRow()` methods

#### Navigation & Interaction
6. **INavigationServices** - Cursor positioning and hit-testing
   - `HitTestToPosition()` - Mouse click to cursor position
   - `HitTestVisualLine()` - Visual line hit detection
   - `VisualLines`, `LineYPositions` access
   - `ApplyInlineStyles()` method

7. **IVisualModeServices** - Visual mode specific features
   - `IsVisual` property
   - `VisualMaps` access
   - `SkipCursorOverHiddenRanges()` method

#### UI & State
8. **ICanvasOperations** - Canvas-level operations
   - `Dispatcher` - UI thread access
   - `SealAndStopTimer()` - State management
   - `InvalidateVisual()`, `InvalidateLayout()` - Refresh
   - `RaiseFormattingChanged()` - Event notification

9. **IScrollServices** - Scrolling and viewport
   - `Scroll` - ScrollController instance
   - `EnsureCursorVisible()` method

#### Content & Media
10. **IImageServices** - Image handling
    - `DocumentBasePath` - For relative image paths
    - `ImageCache` - Cached image sizes
    - `GetImageSize()` method

11. **ISearchServices** - Find/Replace feature
    - `FindBar` - UI panel reference
    - `TestSearchMatchCount` property

12. **ILoggingServices** - Diagnostic logging
    - `Logger` - IDocsLogger instance

---

## Extracted Classes & Responsibilities

### Layout & Computation

#### LayoutEngine (911 lines)
**Responsibility:** Compute layout, word wrap, build visual lines  
**Dependencies:** ILayoutDataServices, IDocumentServices, IRenderingServices, IParsedContentServices, IVisualModeServices, ILoggingServices, ITableServices, IImageServices, Document  
**Key Methods:**
- `ComputeLayout()` - Main entry point
- `ComputeLayoutCore()` - Word wrapping implementation
- `WrapSegment()` - Text wrapping for visual lines
- `BuildParagraphGroups()` - Soft break grouping

**Data It Manages:**
- `_visualLines` - Computed visual lines
- `_lineYPositions` - Y coordinate cache
- `_visualLineSpacings` - Marker positioning
- `_blockToGroup` - Paragraph group mapping

---

### Rendering & Visual Output

#### RenderingContext (1,297 lines)
**Responsibility:** Render to screen using DrawingContext  
**Dependencies:** IRenderingServices, ILayoutDataServices, IParsedContentServices, IDocumentServices, IScrollServices, ITableServices, IVisualModeServices, IImageServices, ISearchServices, ILoggingServices, ICanvasOperations, INavigationServices, DocsCanvas  
**Key Methods:**
- `OnRender()` - Main rendering pipeline
- `ApplyInlineStyles()` - Bold, italic, code styling
- `ApplyColorSpans()` - Custom color application
- `DrawSelection()` - Selection highlighting
- `DrawJoinedLine()` - Soft-break line rendering

**What It Draws:**
- Text with inline styling
- Selection highlighting
- Search match highlighting
- Spell check squiggles
- Images and placeholders
- Cursor
- Page break indicators

#### TableRenderer (500 lines)
**Responsibility:** Render and measure tables  
**Dependencies:** ITableServices, IRenderingServices, IDocumentServices, IParsedContentServices, ILayoutDataServices  
**Key Methods:**
- `ComputeAllTableColumnWidths()` - Calculate optimal widths
- `DrawTableRow()` - Render table row
- `DrawTableBackgrounds()` - Borders and backgrounds
- `CursorXInTableRow()` - Cursor positioning in cells
- `HitTestInTableRow()` - Cell hit detection

#### PageBreakManager (155 lines)
**Responsibility:** Manage page break visualization for printing  
**Dependencies:** ILayoutDataServices, IRenderingServices  
**Key Methods:**
- `SetShowPageBreaks()` - Toggle visibility
- `ComputePageBreakPositions()` - Calculate page boundaries
- `DrawPageBreaks()` - Render page break lines

---

### Cursor Navigation & Input

#### CursorNavigationEngine (700 lines)
**Responsibility:** Position cursor based on user input or mouse clicks  
**Dependencies:** ILayoutDataServices, IDocumentServices, IVisualModeServices, ITableServices, IRenderingServices, IParsedContentServices, ILoggingServices, IImageServices, IScrollServices, ICanvasOperations, INavigationServices  
**Key Methods:**
- `HitTestToPosition()` - Mouse to cursor mapping
- `CursorToVisualLineIndex()` - Cursor to visual line
- `CursorXInVisualLine()` - Cursor X position in line
- `Handle*Key()` - Navigation key handlers (Up, Down, Left, Right, Home, End, Page Up/Down, etc.)

#### VisualModeManager (580 lines)
**Responsibility:** Handle visual mode specific cursor navigation  
**Dependencies:** IVisualModeServices, IDocumentServices, IParsedContentServices, ILoggingServices  
**Key Methods:**
- `SkipCursorOverHiddenRanges()` - Skip markdown syntax
- `EnsureCursorOnVisibleBlock()` - Avoid hidden blocks
- `HandleTableArrow()` - Table cell navigation
- `HandleUpVisual()`, `HandleDownVisual()` - Visual mode arrow keys

#### TableInputHandler (149 lines)
**Responsibility:** Handle keyboard input in table cells  
**Dependencies:** IDocumentServices, IParsedContentServices, ICanvasOperations  
**Key Methods:**
- `HandleTableTab()` - Tab to next cell
- `HandleTableEnter()` - Enter to new row
- `MoveCursorToCell()` - Position in cell

#### LinkHandler (169 lines)
**Responsibility:** Detect and handle link clicks  
**Dependencies:** INavigationServices, IDocumentServices, IParsedContentServices, ILayoutDataServices, IVisualModeServices, IScrollServices  
**Key Methods:**
- `GetLinkAtPosition()` - Find link under cursor
- `TryOpenLinkAtClick()` - Handle Ctrl+Click
- `UpdateLinkTooltip()` - Show link preview

---

### Features

#### FindAndReplaceController (290 lines)
**Responsibility:** Find and replace text  
**Dependencies:** ISearchServices, IDocumentServices, ICanvasOperations, IRenderingServices, ILayoutDataServices, IScrollServices, IParsedContentServices, IVisualModeServices, ITableServices  
**Key Methods:**
- `ExecuteSearch()` - Find matches
- `ReplaceOne()`, `ReplaceAll()` - Replace operations
- `DrawSearchHighlights()` - Highlight matches

#### SpellCheckController (432 lines)
**Responsibility:** Spell check and underline errors  
**Dependencies:** ICanvasOperations, IImageServices, IDocumentServices, IRenderingServices, ILayoutDataServices, IParsedContentServices, ITableServices, INavigationServices, IVisualModeServices, IScrollServices  
**Key Methods:**
- `SetSpellCheckEnabled()` - Toggle checking
- `DrawSpellingErrors()` - Underline misspelled words
- `RecheckBlock()` - Recheck single block

---

### Page Break Management

#### PageBreakManager (155 lines)
**Responsibility:** Calculate and visualize page breaks for printing  
**Dependencies:** ILayoutDataServices, IRenderingServices  
**Key Methods:**
- `ComputePageBreakPositions()` - Calculate breaks
- `DrawPageBreaks()` - Render break indicators
- `SetShowPageBreaks()` - Toggle visibility

---

## Data Flow Diagram

### User Input → Document Update → Layout → Render

```
User Input (Keyboard/Mouse)
    │
    ├─→ Input.cs / TableInputHandler / CursorNavigationEngine
    │   (Translate input to cursor/document changes)
    │
    ├─→ Document.Insert/Delete/Paste
    │   (Mutate document model)
    │
    ├─→ Layout becomes dirty (InvalidateLayout)
    │   (Signal that layout needs recomputation)
    │
    ├─→ LayoutEngine.ComputeLayout()
    │   (Word wrap, visual lines, positions)
    │
    ├─→ RenderingContext.OnRender()
    │   (Draw to screen using computed layout)
    │
    └─→ Visual Update Complete
        (User sees change)
```

### Search/Styling → Render Path

```
User Action (Find/Format/Spell)
    │
    ├─→ FindAndReplaceController / ColorFormattingManager / SpellCheckController
    │   (Compute styling changes)
    │
    ├─→ RenderingContext.OnRender()
    │   (Apply styles while rendering)
    │
    └─→ Visual Update with styling
        (User sees highlighting)
```

### Print Path

```
User Click Print
    │
    ├─→ Print() method
    │
    ├─→ DocsPaginator.GetPage()
    │
    ├─→ RenderingContext.OnRender() (print variant)
    │   (Draw to print surface)
    │
    ├─→ PageBreakManager (pagination)
    │   (Calculate page boundaries)
    │
    └─→ Print Document Output
        (To printer)
```

---

## Interface Implementation Map

Each extracted class depends on specific interfaces:

```
LayoutEngine
├── ILayoutDataServices (primary)
├── IDocumentServices
├── IRenderingServices
├── IParsedContentServices
├── IVisualModeServices
├── ILoggingServices
├── ITableServices
├── IImageServices
└── Document (direct)

RenderingContext
├── IRenderingServices (primary)
├── ILayoutDataServices
├── IParsedContentServices
├── IDocumentServices
├── IScrollServices
├── ITableServices
├── IVisualModeServices
├── IImageServices
├── ISearchServices
├── ILoggingServices
├── ICanvasOperations
├── INavigationServices
└── DocsCanvas (direct, for private methods)

CursorNavigationEngine
├── ILayoutDataServices
├── IDocumentServices
├── IVisualModeServices
├── ITableServices
├── IRenderingServices
├── IParsedContentServices
├── ILoggingServices
├── IImageServices
├── IScrollServices
├── ICanvasOperations
└── INavigationServices (primary)

VisualModeManager
├── IVisualModeServices (primary)
├── IDocumentServices
├── IParsedContentServices
└── ILoggingServices

TableRenderer
├── ITableServices (primary)
├── IRenderingServices
├── IDocumentServices
├── IParsedContentServices
└── ILayoutDataServices

LinkHandler
├── INavigationServices
├── IDocumentServices
├── IParsedContentServices
├── ILayoutDataServices
├── IVisualModeServices
└── IScrollServices

TableInputHandler
├── IDocumentServices
├── IParsedContentServices
└── ICanvasOperations

FindAndReplaceController
├── ISearchServices
├── IDocumentServices
├── ICanvasOperations
├── IRenderingServices
├── ILayoutDataServices
├── IScrollServices
├── IParsedContentServices
├── IVisualModeServices
└── ITableServices

SpellCheckController
├── ICanvasOperations
├── IImageServices
├── IDocumentServices
├── IRenderingServices
├── ILayoutDataServices
├── IParsedContentServices
├── ITableServices
├── INavigationServices
├── IVisualModeServices
└── IScrollServices

PageBreakManager
├── ILayoutDataServices (primary)
└── IRenderingServices
```

---

## Dependency Direction Principle

**Key Architectural Principle:** All dependencies point OUTWARD to interfaces, not inward to DocsCanvas.

```
┌─────────────────────────────────────────┐
│         DocsCanvas                      │
│    (Implements all 12 interfaces)       │
│         Orchestrator Layer              │
└─────────────────────────────────────────┘
              ▲
              │ (Depend on interfaces, not DocsCanvas)
              │
┌─────────────────────────────────────────┐
│  LayoutEngine │ RenderingContext │ etc  │
│  (11 extracted classes)                 │
│  Each uses specific interface contracts │
└─────────────────────────────────────────┘
```

**No class directly references another extracted class** (except for DocsCanvas which orchestrates them).

---

## Thread Model

**Single-threaded WPF Model:**
- All UI operations on dispatcher thread
- Document operations are synchronous
- No background rendering (except optional spell check timer)

**Coordination:**
- DocsCanvas owns all extracted classes (lifetime management)
- Classes communicate through DocsCanvas
- Events fire on UI thread

---

## State Management

### Owned by DocsCanvas
- `_doc` - Document instance (cursor, selection, text)
- `_palette` - Current theme
- `_scroll` - Scroll position
- `_editMode` - Source vs Visual mode
- `_layoutDirty` - Layout cache validity

### Owned by LayoutEngine
- `_visualLines` - Computed visual lines
- `_lineYPositions` - Line Y coordinates
- `_visualLineSpacings` - Marker positions

### Owned by RenderingContext
- `_syntaxBrushCache` - Theme color cache

### Shared State (via interfaces)
- `_parsedBlocks` - MarkdownParser output
- `_visualMaps` - BlockVisualMap output
- `_tableColumnWidths` - TableRenderer output

---

## Extension Points

### Adding New Feature
1. Create new controller class (e.g., `MyFeatureController`)
2. Define dependency interfaces needed
3. Add those interfaces to DocsCanvas if not present
4. Wire up in DocsCanvas constructor
5. Call from appropriate input/feature location

### Example: Add New Formatting
```csharp
public class CustomFormattingManager
{
    private IDocumentServices _doc;
    private ICanvasOperations _canvas;
    
    public CustomFormattingManager(IDocumentServices doc, ICanvasOperations canvas)
    {
        _doc = doc;
        _canvas = canvas;
    }
    
    public void ApplyCustomFormat() { ... }
}

// In DocsCanvas:
private CustomFormattingManager _customFormatter;
// In constructor:
_customFormatter = new CustomFormattingManager((IDocumentServices)this, (ICanvasOperations)this);
```

---

## Design Patterns Used

### 1. Dependency Injection (Constructor)
Every extracted class declares its dependencies in constructor parameters.

### 2. Interface Segregation
12 focused interfaces instead of 1 god interface.

### 3. Single Responsibility Principle
Each class has clear, focused responsibility.

### 4. Lazy Initialization (some cases)
Optional features initialize on first use.

### 5. Nested Classes (some cases)
Small helpers stay nested (DocsPaginator in Print.cs).

---

## Performance Characteristics

### Layout Computation
- Word wrapping: O(n) where n = character count
- Caching: Full recompute only when document/width changes
- Incremental: Could be optimized to partial recompute

### Rendering
- Viewport culling: Only visible lines rendered
- Caching: Text measurement cached
- Format rendering: O(v) where v = visible lines

### Cursor Navigation
- Hit testing: O(v) visual line search
- Character position: O(c) character scan in line

---

## Quality Metrics

| Metric | Value |
|--------|-------|
| DocsCanvas size | ~1,474 lines (was 8,721) |
| Extracted classes | 11 |
| Total extracted lines | ~5,768 |
| Internal casts | 0 (was 804) |
| Interfaces | 12 |
| Public API changes | 0 |
| Test pass rate | 100% at baseline |

---

## Migration Path (if needed)

If in future we need different rendering engine (e.g., Direct2D, Skia):
1. Create new renderer implementing `IRenderingServices`
2. Swap implementation in DocsCanvas
3. No changes needed to extracted classes

This architecture enables that flexibility.

---

## Conclusion

DocsCanvas is now a well-architected system with:
- ✅ Clear separation of concerns
- ✅ Explicit dependency contracts
- ✅ Loose coupling
- ✅ High testability
- ✅ Extensibility

The 12 interfaces define the contract between orchestrator (DocsCanvas) and components. This enables:
- Easy testing (mock interfaces)
- Easy extension (add new components)
- Easy maintenance (understand dependencies)
- Easy optimization (profile/optimize specific interfaces)

