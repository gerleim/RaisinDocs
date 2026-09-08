# DocsCanvas Architecture: Phase 3 & Beyond Roadmap

**Date:** 2026-08-05, revised 2026-09-08  
**Status:** Phase 3 input/visual-mode work complete — see *Phase 3 outcome* below  
**Scope:** Identify and prioritize remaining architectural improvements

> **Read this first (2026-09-08).** Most of what this document proposed has been done.
> The three Input.cs handlers it lists as "not yet pursued" all shipped, along with four
> more. Sections below are annotated with what actually happened; the line counts in the
> original text were produced with `Get-Content | Measure-Object -Line`, which drops blank
> lines and undercounts — figures marked **(corrected)** are true `wc -l` counts.

---

## Executive Summary

After completing Phase 2 (interface refactoring), the DocsCanvas architecture is significantly improved:
- ✅ 804 internal casts eliminated
- ✅ 11 classes refactored to use specific interfaces
- ✅ 0 new test failures
- ✅ Page Up/Page Down performance restored
- ✅ Clean dependency injection established

**Current Focus:** Identify remaining opportunities for architectural improvement without disrupting the clean foundation.

---

## Current Architecture State

### What's Complete ✅
- **Phase 1:** Interface enhancements (12 focused interfaces)
- **Phase 2:** Class refactoring (11 extracted classes using specific interfaces)
- **Marker alignment:** Fixed (all three types aligned)
- **God class elimination:** ~5,500 lines in DocsCanvas (down from 8,721)
- **Loose coupling:** All extracted classes depend on specific interfaces only

### Known Good Design Patterns
- ✅ Specific interfaces for each class
- ✅ Constructor dependency injection
- ✅ Single responsibility per extracted class
- ✅ No circular dependencies
- ✅ Clean separation of concerns

### What's Deferred
- **PrintService rearchitecture:** Requires design work on unified render/print pipeline
- **TDD test failures:** Intentional - part of markdown conformance testing
- **Other projects review:** DocsEditor, Viewer, TestApp - could be assessed later

### Phase 3 outcome (2026-09-08)

**Input.cs: 1098 → 658 lines (corrected).** Seven handlers extracted, not the two or three
this document anticipated: ListFormattingHandler (247), ContextMenuHandler (168),
EditingKeysHandler (156), FormattingKeysHandler (102), HoverImageHandler (100),
IndentationHandler (98), NavigationKeysHandler (84). Eight one-line navigation wrappers
deleted. Two interfaces added — `IEditingServices` and `ISpellCheckAccess`.

**VisualMode.cs: 702 → 477 lines (corrected).** TableSelectionManager (236 lines) took the
rectangular table selection; the dead selection renderer (`DrawSelection` and everything
only it reached) was deleted.

**Formatting.cs: extraction attempted and reverted.** See the revised entry under
*Detailed Analysis* — this is now a known blocker, not an open opportunity.

**What this changed about planning.** The predictor of whether a group extracts cleanly was
not size or risk-rating but **shared mutable state**. Input.cs handlers and the table
rectangle read state and return; they moved without incident. The formatting toggles share
`_pendingStyleOff` across calls, and splitting them broke tests. Rank future candidates by
what they write, not by how many lines they are.

---

## Remaining Opportunities

### Option 1: Document Current Architecture
**Objective:** Capture the clean architecture in visual and textual form  
**Scope:**
- Interface hierarchy diagram
- Component relationship diagram
- Data flow diagram
- Updated CLAUDE.md with architecture section
- Component responsibility matrix

**Files to Update:**
- `CLAUDE.md` - Add "Architecture After Refactoring" section
- Create `design/Architecture_Diagrams.md` with visual representations
- Create `design/Component_Responsibilities.md` documenting each class's role

**Effort:** 1-2 hours  
**Priority:** HIGH - Documentation is foundational  
**ROI:** High - enables future developers to understand architecture  

**Next Step After:** Continue with other improvements knowing architecture is documented

---

### Option 2: Review Remaining Partial Files
**Objective:** Assess if Input.cs, Formatting.cs, VisualMode.cs, SourceMode.cs need improvements  
**Scope:**

#### Input.cs — ✅ **DONE.** 1098 → 658 lines (corrected)
All three proposed handlers were extracted, plus four more the original review missed
(ListFormatting, ContextMenu, Indentation, HoverImage). The risk rating "Medium (input
handling is critical path)" proved too cautious: the handlers were discrete — read the
document, do work, return — so they moved without a single regression.

#### Formatting.cs — ❌ **BLOCKED.** 634 lines (corrected, not 555)
"Extraction candidates: Unlikely" was right, but for the wrong reason. It is not that the
API surface is thin; it is that the toggles share `_pendingStyleOff`, a field remembering
that a style marker was typed and must toggle off on the next input. An extraction was
attempted and reverted after it broke `PendingBoldOff_InsideBold_TypingSplitsRun` and the
multi-block task-list toggle. **Extracting formatting requires first giving that state an
owner.** Until then this is not a mechanical move, and "Risk: Low" is wrong.

#### VisualMode.cs — ✅ **PARTLY DONE.** 702 → 477 lines (corrected, not 597)
"Mostly delegation" was not accurate. Three findings:
1. **TableSelectionManager (236 lines) extracted** — the rectangle is derived from anchor and
   cursor on every call, never stored, so it split with no state to hand over.
2. **~172 lines were dead.** `DrawSelection` lost its last caller when the content layer took
   over rendering, which made `DrawJoinedSelection` and `DrawTableRectSelection` unreachable.
   Three `VisualModeManager` forwarders were dead too — the manager calls its own internals.
3. **What remains is a fork, not delegation.** RenderingContext holds its own private copy of
   every drawing method still in the file, and `OnRenderCore` is just
   `_renderingContext.OnRender(dc)` — the screen never runs VisualMode.cs's versions. Print is
   their only live consumer, and the copies have drifted (RenderingContext's take a
   `softBreaks` parameter these do not). **Screen and print render inline styles through
   different code.** Reconciling them belongs to the deferred print rework.

#### SourceMode.cs (100 lines)
- **Current state:** Minimal, focused
- **Analysis needed:**
  - Already well-scoped
  - No extraction needed
- **Risk:** N/A

**Effort:** 30 minutes (review only)  
**Priority:** MEDIUM - Understanding might reveal hidden improvements  
**ROI:** Medium - could identify 1-2 small extraction opportunities  

**If findings:**
- Pursue further extractions from Input.cs
- Clean up any code quality issues
- Document new responsibilities

---

### Option 3: Performance Measurement
**Objective:** Quantify improvements from Phase 2 refactoring  
**Scope:**

#### Keyboard Responsiveness
- Measure Page Up/Page Down latency before/after
- Measure Left/Right arrow key responsiveness
- Measure typing responsiveness
- Method: Time from key press to visual update

#### Memory Profiling
- Profile allocation patterns before/after
- Measure casting overhead (eliminated)
- Identify remaining hotspots
- Tool: .NET Memory Profiler or built-in diagnostics

#### Rendering Performance
- Measure DrawJoinedLine performance
- Measure full-page render time
- Measure scroll performance
- Method: Stopwatch + frame timing

**Effort:** 1-2 hours  
**Priority:** MEDIUM - Validates architecture improvements  
**ROI:** Medium - Confirms performance gains, identifies future work  

**Expected Results:**
- Page Up/Page Down: Should be noticeably faster (eliminated 8-9 casts per call)
- Arrow navigation: Reduced casting overhead
- Overall: Smoother keyboard interaction

---

### Option 4: Code Simplification Pass
**Objective:** Further improve code quality and clarity  
**Scope:**
- Use /simplify skill on refactored classes
- Look for redundant patterns
- Improve variable/method naming
- Remove unnecessary complexity
- Consolidate similar logic

**Classes to Review:**
- All 11 refactored classes
- DocsCanvas.cs main file
- DocsCanvas partial files

**Effort:** 1 hour  
**Priority:** LOW - Architecture already clean  
**ROI:** Low-Medium - Incremental quality improvements  

---

### Option 5: Review Other Projects
**Objective:** Assess if other projects need similar refactoring  
**Scope:**

#### RaisinDocs.Editor
- Tabbed editor UI
- File menu (New/Open/Save)
- Session persistence
- Could have similar architectural issues

#### RaisinDocs.Viewer
- Read-only viewer
- Visual mode only
- Minimap
- Simpler than Editor, but could have patterns to extract

#### RaisinDocs.TestApp
- Development sandbox
- AvalonDock hosting
- Test environment
- Low priority

**Effort:** 1 hour (review only)  
**Priority:** LOW - Focus on DocsCanvas first  
**ROI:** Unknown - Would need analysis  

---

### Option 6: Create Comprehensive Refactoring Roadmap
**Objective:** Document what was done and what could be done  
**Scope:**
- Completed phases (1-2) with results
- Identified Phase 3 opportunities
- Estimated effort for each
- Prioritized list for future work
- Risk assessment for each

**Deliverables:**
- `Phase3_And_Beyond_Roadmap.md` (this document)
- Updated `CLAUDE.md` with architecture section
- Prioritized task list for future developers

**Effort:** 1 hour  
**Priority:** HIGH - Guides future work  
**ROI:** High - Enables systematic improvement  

---

## Identified Extraction Candidates

Beyond the main improvement options, these specific classes/handlers are candidates for future extraction:

### PrintService (from Print.cs - 512 lines)
**Status:** Identified, deferred pending design work  
**Current Location:** DocsCanvas.Print.cs  
**Components to Extract:**
- DocsPaginator inner class (~230 lines)
- Print() method and printing logic
- Print-specific styling and pagination

**Rationale:**
- Print rendering should work in tandem with screen rendering
- Needs unified render/print pipeline design first
- Risk: Medium (core printing functionality)

**Design Work Needed:**
- Create IRenderTarget abstraction (ScreenTarget vs PrintTarget)
- Unify RenderingContext with print pipeline
- Integrate DocsPaginator with layout engine

**Timeline:** Phase 3 (after design work)

---

### FormattingKeysHandler (from Input.cs - ~150 lines)
**Status:** ✅ **Done (2026-09).** Shipped as `FormattingKeysHandler.cs`, 102 lines. Ctrl+B/I/K;
the code-span and strikethrough shortcuts listed below were not part of it.  
**Current Location:** DocsCanvas.Input.cs, OnKeyDown() method  
**Responsibility:** Keyboard shortcuts for formatting

**Methods to Extract:**
- Ctrl+B (bold toggle)
- Ctrl+I (italic toggle)
- Ctrl+` (code toggle)
- Ctrl+~ (strikethrough toggle)
- Other formatting key combinations

**Rationale:**
- Formatting shortcuts are self-contained
- Clear separation from navigation/editing
- Would improve Input.cs readability

**Risk:** Low (formatting is isolated, well-tested)  
**Effort:** 1-2 hours  
**Priority:** Low-Medium

---

### NavigationKeysHandler (from Input.cs - ~100 lines)
**Status:** ✅ **Done (2026-09).** Shipped as `NavigationKeysHandler.cs`, 84 lines, covering the
Ctrl-modified keys. The eight unmodified wrappers (`HandleLeft`, `HandleUp`, …) were deleted
rather than moved — `OnKeyDown` calls `_navigationEngine` directly.  
**Current Location:** DocsCanvas.Input.cs, OnKeyDown() method  
**Responsibility:** Advanced cursor navigation shortcuts

**Methods to Extract:**
- Ctrl+Home (document start)
- Ctrl+End (document end)
- Ctrl+Left/Right (word navigation)
- Other navigation shortcuts

**Rationale:**
- Navigation shortcuts are well-defined
- Already partially extracted to CursorNavigationEngine
- Would consolidate related logic

**Risk:** Low (navigation is well-tested)  
**Effort:** 1-2 hours  
**Priority:** Low

---

### EditingKeysHandler (from Input.cs - ~150 lines)
**Status:** ✅ **Done (2026-09).** Shipped as `EditingKeysHandler.cs`, 156 lines. The "Risk: High"
rating did not materialise — no regressions — but the first attempt did fail to build: the class
was written as top-level while `LastActionKind` was a private enum inside DocsCanvas and
`CursorNavigationEngine` was a nested type. Fixed by moving `LastActionKind` to its own file and
routing the rest through the new `IEditingServices` interface.  
**Current Location:** DocsCanvas.Input.cs, OnKeyDown() method  
**Responsibility:** Core editing operations via keyboard

**Methods to Extract:**
- Backspace handling
- Delete handling
- Undo/Redo handling
- Undo/Redo stack management

**Rationale:**
- Editing is core functionality that deserves isolation
- Would improve separation between input dispatch and editing
- Complex dependency chain with Document

**Risk:** High (core editing path, must maintain perfect compatibility)  
**Effort:** 2-3 hours  
**Priority:** Low (only if Input.cs becomes problematic)

**Note:** This would require extensive testing to ensure no regressions

---

## Extraction Candidate Summary

| Candidate | Shipped lines | Location | Status |
|-----------|--------------|----------|--------|
| ListFormattingHandler | 247 | Input.cs | ✅ Done 2026-09 (not in original list) |
| TableSelectionManager | 236 | VisualMode.cs | ✅ Done 2026-09 (not in original list) |
| ContextMenuHandler | 168 | Input.cs | ✅ Done 2026-09 (not in original list) |
| EditingKeysHandler | 156 | Input.cs | ✅ Done 2026-09 |
| FormattingKeysHandler | 102 | Input.cs | ✅ Done 2026-09 |
| HoverImageHandler | 100 | Input.cs | ✅ Done 2026-09 (not in original list) |
| IndentationHandler | 98 | Input.cs | ✅ Done 2026-09 (not in original list) |
| NavigationKeysHandler | 84 | Input.cs | ✅ Done 2026-09 |
| PrintService | 512 | Print.cs | ⏸ Deferred — design needed, and print is deferred wholesale |
| Formatting services | — | Formatting.cs | ❌ Blocked on `_pendingStyleOff` ownership |

**Recommendation (revised 2026-09-08):**
- **Input.cs is finished.** What remains there is event plumbing; further splitting buys nothing.
- **Do not attempt Formatting.cs** as an extraction. The next step there is deciding who owns
  `_pendingStyleOff`, which is a design question, not a refactor.
- **PrintService stays deferred**, and note it has grown a second reason: VisualMode.cs and
  RenderingContext now hold diverged copies of the same drawing methods, with print reading the
  stale ones. Whoever does the print rework inherits that reconciliation.
- **Rank future candidates by what state they write**, not by line count or a risk hunch. That
  metric predicted all four outcomes above; the original risk ratings predicted none of them.

---

## Detailed Analysis: Remaining Partial Files

### Input.cs Deep Dive

**Current Size:** 989 lines  
**Responsibility:** All keyboard/mouse/text input handling

**Major Methods:**
- `OnKeyDown()` - Keyboard dispatcher (~50 lines)
- `OnTextInput()` - Text input handler (~30 lines)
- `OnMouseDown/Move/Up()` - Mouse input (~100 lines)
- `OnMouseWheel()` - Scroll handling (~30 lines)
- `Handle*Key()` - 20+ navigation handlers (~600 lines)

**Extraction Candidates:**

#### FormattingKeysHandler (~150 lines)
- Handles Ctrl+B (bold), Ctrl+I (italic), Ctrl+` (code), etc.
- Formats selection based on key combination
- Self-contained responsibility
- Risk: Low (formatting is isolated)

#### NavigationKeysHandler (~100 lines)
- Handles Ctrl+Home (doc start), Ctrl+End (doc end), etc.
- Advanced cursor positioning
- Self-contained responsibility
- Risk: Low (navigation is isolated)

#### EditingKeysHandler (~150 lines)
- Handles Backspace, Delete, Undo, Redo
- Document mutation
- Self-contained responsibility
- Risk: Medium (core editing, needs careful testing)

**Recommendation:** 
- Leave Input.cs as-is (already well-organized after TableInputHandler extraction)
- If decomposition needed, pursue it cautiously with full test coverage
- Document current structure first

---

### Formatting.cs Analysis

**Current Size:** 555 lines  
**Responsibility:** Formatting API (public methods called by apps)

**Major Methods:**
- `ToggleBold/Italic/Code/Strikethrough()` - Style toggles
- `ToggleHeading()` - Heading level cycling
- `ToggleBlockPrefixes()` - List/quote prefixes
- `ToggleFencedCode()` - Code block wrapping
- `InsertLink/Table/Color()` - Content insertion
- Formatting query properties

**Assessment:** Well-organized, clear responsibilities

**Already Extracted:**
- ColorFormattingManager (color tag handling)
- BackgroundHelper (color background removal)

**Status:** Good condition, no improvements needed  
**Recommendation:** Leave as-is

---

### VisualMode.cs Analysis

**Current Size:** 597 lines  
**Responsibility:** Visual mode specific logic

**Major Components:**
- Table rendering (delegated to TableRenderer)
- Table cell navigation (delegated to TableInputHandler)
- Visual mode cursor skipping (delegated to VisualModeManager)
- Image rendering/preview
- Visual mode specific key handling

**Assessment:** Properly delegates to extracted components

**Status:** Good condition  
**Recommendation:** Monitor for future extraction opportunities, but leave as-is

---

### SourceMode.cs Analysis

**Current Size:** 100 lines  
**Responsibility:** Source mode specific rendering

**Content:**
- Syntax highlighting
- Inline image preview in source mode
- Source mode specific text rendering

**Assessment:** Minimal, focused, no extraction needed

**Status:** Excellent  
**Recommendation:** Leave as-is

---

## Prioritized Roadmap

### Immediate (Next Session)
**Priority 1: Document Architecture** (1-2 hours)
1. Create architecture diagrams (interfaces, components, data flow)
2. Update CLAUDE.md with architecture section
3. Document component responsibilities
4. **Value:** Enables future developers, guides decisions

**Priority 2: Measure Performance** (1-2 hours)
1. Benchmark Page Up/Page Down latency
2. Measure arrow key responsiveness
3. Profile memory/allocation changes
4. **Value:** Validates improvements, identifies future optimizations

### Medium-term (Future Sessions)
**Priority 3: PrintService Rearchitecture** (Design first, then implementation)
1. Design unified render/print pipeline
2. Create IRenderTarget abstraction
3. Refactor Print.cs and RenderingContext integration
4. **Value:** Eliminates duplication, enables flexible output

**Priority 4: Input.cs Review** (If time permits)
1. Analyze for further decomposition
2. Extract handlers if warranted (FormattingKeysHandler, etc.)
3. Document current structure
4. **Value:** Further reduces class size, if beneficial

### Long-term (Future)
**Priority 5: Other Projects** (Lower urgency)
1. Review DocsEditor structure
2. Assess Viewer/TestApp for patterns
3. Apply successful patterns from DocsCanvas
4. **Value:** Consistency across codebase

**Priority 6: Code Simplification** (Ongoing)
1. Periodic /simplify reviews
2. Refactor identified patterns
3. Improve naming/clarity
4. **Value:** Incremental quality improvements

---

## Risk Assessment

### Low Risk (Safe to pursue)
- ✅ Architecture documentation (reading only)
- ✅ Performance measurement (observational)
- ✅ Code simplification (cosmetic)

### Medium Risk (Plan carefully)
- ⚠️ Input.cs decomposition (core input path)
- ⚠️ PrintService rearchitecture (needs design)

### High Risk (Defer or plan extensively)
- 🚫 TDD test fixes (intentional failures, part of development)
- 🚫 Other projects refactoring (scope creep)

---

## Success Criteria for Next Phase

### If pursuing documentation:
- ✅ Architecture diagrams created
- ✅ CLAUDE.md updated with architecture section
- ✅ Component responsibilities documented
- ✅ New developers can understand architecture from docs

### If pursuing performance measurement:
- ✅ Page Up/Page Down latency measured and compared
- ✅ Memory allocation changes quantified
- ✅ Rendering performance benchmarked
- ✅ Results published in documentation

### If pursuing Input.cs decomposition:
- ✅ Analysis complete (extraction candidates identified)
- ✅ No new test failures introduced
- ✅ New handlers properly tested
- ✅ Documentation updated

### If pursuing PrintService rearchitecture:
- ✅ Unified render/print design documented
- ✅ IRenderTarget abstraction designed
- ✅ Implementation plan created
- ✅ Zero regressions in print output

---

## Recommended Path Forward

### Suggested Sequence:
1. **Session 1:** Document Architecture (Option 2)
   - Create diagrams
   - Update CLAUDE.md
   - ~1 hour investment

2. **Session 2:** Measure Performance (Option 3)
   - Benchmark improvements
   - Validate refactoring impact
   - ~1-2 hour investment

3. **Session 3:** PrintService Design (Deferred)
   - Use Plan agent to design unified render/print
   - Document architecture decisions
   - ~1-2 hour investment

4. **Session 4:** Input.cs Analysis (Optional)
   - If performance issues identified
   - Or if another developer requests decomposition
   - ~30 minutes + implementation if needed

---

## Conclusion

Phase 2 refactoring established a clean architectural foundation. The codebase is now:
- ✅ Well-structured (specific interfaces, clear responsibilities)
- ✅ Performant (casting overhead eliminated)
- ✅ Maintainable (loose coupling, explicit dependencies)
- ✅ Testable (can mock specific interfaces)
- ✅ Extensible (easy to add new components)

**Next steps should focus on:**
1. **Documentation** - Capture architecture for future developers
2. **Measurement** - Validate improvements quantitatively
3. **Design work** - Plan PrintService rearchitecture before implementation

The foundation is solid. Future improvements should build on this, not risk destabilizing it.

---

**Status:** Ready for next phase. Awaiting decision on which opportunity to pursue first.
