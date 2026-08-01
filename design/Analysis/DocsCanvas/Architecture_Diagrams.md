# DocsCanvas Architecture Diagrams

## Current State: God Class with Partial Organization

```
┌─────────────────────────────────────────────────────────────────────┐
│                         DocsCanvas (8,721 lines)                     │
│  FrameworkElement → Renders, Manages all text editing concerns       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ DocsCanvas.cs (3,067 lines) - Core god class               │    │
│  ├─────────────────────────────────────────────────────────────┤    │
│  │ • Theme/Palette management (3 themes)                       │    │
│  │ • Layout computation & caching (ComputeLayout)              │    │
│  │ • Rendering pipeline (OnRender, all Draw* methods)          │    │
│  │ • Text measurement & font caching                           │    │
│  │ • Cursor positioning & navigation                           │    │
│  │ • Scroll management                                         │    │
│  │ • Edit mode state (Source/Visual toggle)                    │    │
│  │ • Shared state: _visualLines, _lineYPositions,              │    │
│  │               _parsedBlocks, _visualMaps, _scroll           │    │
│  │ • Document reference                                        │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                       │
│  ┌──────────────────────┐  ┌──────────────────────┐                 │
│  │ Input.cs (1,495)     │  │ VisualMode.cs (1,725)│                 │
│  ├──────────────────────┤  ├──────────────────────┤                 │
│  │ • Mouse handlers     │  │ • Table rendering    │                 │
│  │ • Keyboard handlers  │  │ • Table navigation   │                 │
│  │ • Cursor movement    │  │ • Visual mode cursor │                 │
│  │ • Selection logic    │  │ • Link handling      │                 │
│  │ • Text input routing │  │ • Task checkboxes    │                 │
│  └──────────────────────┘  └──────────────────────┘                 │
│                                                                       │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌───────────┐  │
│  │ Formatting.cs (712)  │  │ Print.cs (598)       │  │ Find.cs   │  │
│  ├──────────────────────┤  ├──────────────────────┤  │   (289)   │  │
│  │ • Bold/Italic/Code   │  │ • Page breaks        │  ├───────────┤  │
│  │ • Lists & Headings   │  │ • Pagination         │  │ • Search  │  │
│  │ • Links & colors     │  │ • Paginator          │  │ • Replace │  │
│  │ • Table insertion    │  │                      │  │ • Matching│  │
│  │ • Reflow             │  │                      │  └───────────┘  │
│  └──────────────────────┘  └──────────────────────┘                 │
│                                                                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐               │
│  │ SpellCheck   │  │ Minimap      │  │ TOC          │               │
│  │  (426)       │  │  (209)       │  │  (86)        │               │
│  ├──────────────┤  ├──────────────┤  ├──────────────┤               │
│  │ • Service    │  │ • Data pass  │  │ • Integration│               │
│  │ • Errors     │  │ • Properties │  │ • Toggle     │               │
│  └──────────────┘  └──────────────┘  └──────────────┘               │
│                                                                       │
│  ┌──────────────────────┐  ┌──────────────────────┐                 │
│  │ SourceMode.cs (114)  │  │ Shared Dependencies  │                 │
│  ├──────────────────────┤  ├──────────────────────┤                 │
│  │ • Image preview      │  │ • Document _doc      │                 │
│  │                      │  │ • TextMeasurer       │                 │
│  │                      │  │ • SyntaxHighlighter  │                 │
│  │                      │  │ • ScrollController   │                 │
│  │                      │  │ • ImageCache         │                 │
│  │                      │  │ • LinkPopupController│                 │
│  │                      │  │ • FormattingBar      │                 │
│  │                      │  │ • FindBar            │                 │
│  └──────────────────────┘  └──────────────────────┘                 │
│                                                                       │
│  PROBLEM: All share state, all depend on core rendering             │
│  No independent testing, high coupling, 8,721 lines in one class    │
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Dependency Web (Current - High Coupling)

```
                    Document _doc (required by almost everything)
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
    Input.cs          DocsCanvas.cs       Formatting.cs
    • Key/Mouse        • Layout             • Mutations
    • Navigation       • Rendering          • Reflow
    • Selection        • Cursor              • Links/Tables
        │               • Theme              │
        │               • Scroll          VisualMode.cs
        │                   │            • Tables
        └───────┬───────────┼────────────────┬──────────┐
                │           │                │          │
                ▼           ▼                ▼          ▼
            Cursor Movement ← Layout State    Rendering Output
            (interdependent)  (shared)      (OnRender uses Layout)
                │               │                │
                └───────────────┼────────────────┘
                        (Tight loop)

Find.cs, SpellCheck.cs, Print.cs ──→ all only used during rendering
Minimap.cs, Toc.cs ──→ passive data consumers
```

---

## Proposed Post-Refactoring Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                      DocsCanvas (1,000 lines)                         │
│                       Orchestration & API                             │
├──────────────────────────────────────────────────────────────────────┤
│ • Event handlers (OnRender, OnKeyDown, OnMouseDown, etc.)             │
│ • WPF integration layer                                               │
│ • Public API delegation                                               │
│ • Theme management                                                    │
│ • Document reference                                                  │
│ • High-level state orchestration                                      │
└──────────────────────────────────────────────────────────────────────┘
                    ▲
                    │ delegates to/orchestrates
                    │
        ┌───────────┼───────────┬──────────────┐
        │           │           │              │
        ▼           ▼           ▼              ▼

    ┌─────────┐  ┌──────────┐  ┌────────────┐  ┌──────────────┐
    │ Layout  │  │ Cursor   │  │  Input     │  │ TextMeasurer │
    │ Engine  │  │ Nav      │  │ Dispatcher │  │              │
    │ (1,400) │  │ (400)    │  │ (300)      │  │ (unchanged)  │
    └────┬────┘  └────┬─────┘  └────────────┘  └──────────────┘
         │            │              │
         └────────────┼──────────────┘
              (shared context)
                      ▲
                      │ produces
                      ▼
        ┌──────────────────────────┐
        │ RenderingContext         │
        │ • VisualLines            │
        │ • LineYPositions         │
        │ • ParsedBlocks           │
        │ • VisualMaps             │
        │ • LayoutVersion          │
        └──────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                   Optional Components (Pluggable)               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │ Table        │  │ Visual Mode  │  │ Link         │           │
│  │ Renderer     │  │ Manager      │  │ Handler      │           │
│  │ (400)        │  │ (200)        │  │ (150)        │           │
│  └──────────────┘  └──────────────┘  └──────────────┘           │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │ Find &       │  │ SpellCheck   │  │ PageBreak    │           │
│  │ Replace      │  │ Controller   │  │ Manager      │           │
│  │ (289)        │  │ (426)        │  │ (598)        │           │
│  └──────────────┘  └──────────────┘  └──────────────┘           │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Interfaces for Minimap & TOC data access                │  │
│  │ • IMinimapDataProvider                                  │  │
│  │ • ITocDataProvider                                      │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Extraction Priority Pyramid

```
                              PHASE 3
                         Core Refactoring
                           ▲ HIGH RISK
                          │ HIGH PAYOFF
                    ┌─────┴─────┐
                    │ LayoutEng  │
                    │ CursorNav  │
                    │ InputDisp   │
                    │ Rendering  │
                    │ Context    │
                    └─────┬─────┘
                          ▲
                    PHASE 2
                  Mode-Specific
                    ▲ MEDIUM RISK
                   │ MEDIUM PAYOFF
              ┌────┴─────┐
              │ Table    │
              │ VisualMgr│
              │ LinkHndlr│
              └────┬─────┘
                   ▲
                PHASE 1
              Quick Wins
              ▲ LOW RISK
             │ GOOD PAYOFF
         ┌───┴────┐
         │ PageBrk│
         │ Find   │
         │ Spell  │
         │ Minimap│
         └────────┘

Time: 4-6 weeks
Lines extracted: 7,721 (89% of total)
Lines remaining in core: 1,000 (11% of total)
Testability improvement: Massive
Maintainability gain: High
```

---

## Data Flow Simplification

### Current State
```
Input Events → DocsCanvas.*
  │                  │
  ├─→ Cursor Nav ────┼─→ Layout Compute ─→ _visualLines
  │                  │                           │
  ├─→ Document Mut ──┼──→ InvalidateLayout ──────┤
  │                  │                           │
  └─→ Text Editing ──┼───────────────────────────┼─→ OnRender
                     │                           │
                 _parsedBlocks ─→ VisualMaps ────┼──→ DrawingContext
                 _scroll ────────────────────────┘

Problem: All paths cross through DocsCanvas, hard to test individually
```

### Post-Refactoring
```
Input Events
    │
    ├─→ InputDispatcher ──→ Document mutations
    │
    ├─→ CursorNavigationEngine ──→ Cursor state
    │
    └─→ DocsCanvas ──→ [orchestrate] ──→ InvalidateLayout
                          │
                          ▼
                   LayoutEngine
                       │ produces
                       ▼
                RenderingContext
                   (VisualLines,
                    ParsedBlocks,
                    VisualMaps)
                       │
                       ▼
                  OnRender
                  (DrawingContext)

Benefits:
• LayoutEngine testable without UI
• CursorNavigationEngine testable independently
• InputDispatcher testable
• RenderingContext is data, not behavior
• OnRender becomes simpler (just draws)
```

---

## Testing Pyramid Before/After

### BEFORE: God Class (8,721 lines)

```
                UI Tests
              (Integration)
                    ▲
                   /│\
                  / │ \  - Must test entire DocsCanvas
                 /  │  \ - Slow, fragile, hard to isolate issues
                /   │   \
               ┌─────────┐
               │ Unit    │ Mostly empty - hard to unit test
               │ Tests   │ WPF binding, huge dependencies
               └─────────┘
```

**Reality**: 
- Most tests are UI tests
- Hard to test individual concerns (layout, input, rendering separately)
- Single bug in any module requires full integration test
- Slow feedback loop

### AFTER: Separated Components

```
                 UI Tests (Integration)
                  (20-30% of tests)
                    ▲
                   /│\
        ┌──────────┼─┴──────────┐
        │          │            │
   Layout Tests  Cursor Tests  Input Tests
   (Unit)        (Unit)         (Unit)
   (Fast)        (Fast)         (Fast)
        │          │            │
        └──────────┼────────────┘
                   ▼
            RenderingContext
            (Data structure
             - minimal testing)
```

**Benefits**:
- 70-80% tests are unit tests (fast)
- Layout engine tested independently
- Input handling tested independently
- Cursor navigation tested independently
- UI tests only for integration scenarios
- Fast feedback loop (minutes → seconds)
- Easier to find root cause of bugs

---

## Cohesion Levels: Current vs. Target

```
CURRENT STATE:

DocsCanvas.cs (3,067 lines)
├─ Tier 1: Theme (50 lines) ─────────────────────┐ LOW
├─ Tier 2: Layout (1,400 lines) ─────────────────┤ VERY HIGH
├─ Tier 3: Rendering (600 lines) ────────────────┤ VERY HIGH
├─ Tier 4: Cursor Nav (400 lines) ───────────────┤ HIGH
├─ Tier 5: Text Measure (100 lines) ─────────────┤ HIGH
├─ Tier 6: Scroll (150 lines) ────────────────────┤ MEDIUM
└─ Tier 7: Doc Access (300 lines) ───────────────┘ HIGH


INPUT.CS (1,495 lines) ────────────────────────────┐
  └─ VERY HIGH cohesion (all input-related)       │
VisualMode.CS (1,725 lines) ──────────────────────┤ HIGH
  ├─ Table rendering (400 lines) ───────────────┐ cohesion
  ├─ Table navigation (300 lines) ───────────────┤ (within
  ├─ Visual cursor (200 lines) ───────────────────┤ file)
  └─ Link handling (200 lines) ────────────────────┘
Formatting.CS (712 lines) ────────────────────────┐
  └─ MEDIUM-HIGH cohesion (formatting ops)       │
Find.CS (289 lines) ───────────────────────────────┤ LOW
  └─ LOW cohesion (search-specific)              │ (to
Print.CS (598 lines) ──────────────────────────────┤ DocsCanvas)
  └─ LOW cohesion (print-specific)               │
SpellCheck.CS (426 lines) ────────────────────────┘
  └─ LOW cohesion (spell service-specific)


================== AFTER REFACTORING ==================

Core DocsCanvas (1,000 lines) ──────────────────────┐
  └─ VERY HIGH cohesion (orchestration only)      │
                                                   │
LayoutEngine (1,400 lines) ────────────────────────┤ VERY
  └─ VERY HIGH cohesion (layout only)             │ HIGH
                                                   │ cohesion
CursorNavigationEngine (400 lines) ────────────────┤ (within
  └─ VERY HIGH cohesion (cursor only)             │ each
                                                   │ class)
InputDispatcher (300 lines) ───────────────────────┤
  └─ VERY HIGH cohesion (input routing only)      │
                                                   │
TableRenderer (400 lines) ─────────────────────────┤ HIGH
  └─ VERY HIGH cohesion (table rendering only)    │
                                                   │
FindAndReplaceController (289 lines) ──────────────┤
  └─ VERY HIGH cohesion (search only)             │
                                                   │
SpellCheckController (426 lines) ──────────────────┤
  └─ VERY HIGH cohesion (spell check only)        │
                                                   │
PageBreakManager (598 lines) ──────────────────────┘
  └─ VERY HIGH cohesion (pagination only)
```

**Result**: Every class has HIGH or VERY HIGH internal cohesion
