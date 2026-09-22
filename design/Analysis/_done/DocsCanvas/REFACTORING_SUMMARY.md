# DocsCanvas Refactoring: Executive Summary

## The Problem: God Class Anti-Pattern

**DocsCanvas is a textbook god class** spanning **8,721 lines of code across 10 partial files**. It handles everything: rendering, layout, input, formatting, search, spell-check, printing, tables, and more. The result is:

- **Hard to maintain**: Any bug requires understanding the entire 3,067-line core file
- **Hard to test**: Most code is WPF-bound; only integration tests possible
- **Hard to extend**: New features tangle with existing state
- **Hard to onboard**: New developers must grok 8k+ lines before making changes
- **High cognitive load**: Passing 15+ parameters through 5 levels of method calls

### Line Count Breakdown

| Component | Lines | % of Total | Cohesion | Extractability |
|-----------|-------|-----------|----------|-----------------|
| **DocsCanvas.cs** | 3,067 | 35% | Very High | Keep core |
| Visual Mode | 1,725 | 20% | Medium-High | Partial extraction |
| Input Handling | 1,495 | 17% | Very High | Keep core |
| Formatting API | 712 | 8% | Medium | Keep but separate |
| Printing | 598 | 7% | **LOW** | ✓ Extract |
| SpellCheck | 426 | 5% | **LOW** | ✓ Extract |
| Find/Replace | 289 | 3% | **LOW** | ✓ Extract |
| Other (Minimap, TOC, SourceMode) | 409 | 5% | **LOW** | ✓ Extract/Interface |

---

## The Solution: Phased, Pragmatic Extraction

The refactoring is **NOT** about breaking DocsCanvas into 30 classes. It's about **separating concerns with clear seams** while keeping tightly coupled logic together.

### Key Principle: Know What Must Stay Together

✓ **Keep Together (Tier 1 - Core Rendering Engine)**
- Layout computation → Rendering → Cursor navigation
- These are interdependent by design
- Trying to separate them is premature design

✓ **Keep Together (Tier 2 - Edit Mode Logic)**
- Input handling + Document mutations
- Visual mode cursor skipping + Tables

⚠️ **Extract But Coordinate (Tier 3 - Formatting)**
- Formatting API is mostly independent
- Keep some layout awareness for reflow

✗ **Extract Completely (Tier 4 - Supplementary)**
- Find/Replace: Independent search + replace logic
- Spell Check: Service integration + error tracking
- Page Breaks: Analysis of layout output
- Minimap/TOC: Pure data pass-through

---

## Three-Phase Refactoring Plan

### Phase 1: Quick Wins (1-2 weeks, ~1,300 lines extracted)
**Risk: MINIMAL | Dependencies: Few | Testing: Easy**

Extract completely independent features with zero architectural changes:

1. **PageBreakManager** (598 lines)
   - Computes page breaks from layout
   - No rendering logic
   - No document mutations

2. **FindAndReplaceController** (289 lines)
   - Manages search state and matches
   - Delegates document mutations to DocsCanvas
   - Fires event when matches change

3. **SpellCheckController** (426 lines)
   - Service integration pattern
   - Manages spell error tracking
   - Fires event when errors change

4. **Data Provider Interfaces** (209 + 86 lines)
   - `IMinimapDataProvider` - decouple Minimap from DocsCanvas
   - `ITocDataProvider` - decouple TOC from DocsCanvas

**Result**: DocsCanvas shrinks from 3,067 → ~1,750 lines. Reduced complexity immediately.

### Phase 2: Mode-Specific Logic (2-3 weeks, ~750 lines extracted)
**Risk: MEDIUM | Dependencies: Layout-dependent | Testing: Medium**

Extract Visual Mode specific subsystems:

5. **TableRenderer** (400 lines)
   - Table drawing, column width computation
   - Separate from text rendering

6. **VisualModeManager** (200 lines)
   - Visual-mode-specific cursor skipping
   - Hidden range management

7. **LinkHandler** (150 lines)
   - Link detection, opening, hovering
   - Consolidate link logic scattered across files

**Result**: Visual Mode subsystems become independently testable. DocsCanvas.VisualMode.cs shrinks significantly.

### Phase 3: Core Refactoring (3-4 weeks, ~1,400 lines restructured)
**Risk: MEDIUM-HIGH | Dependencies: High coupling | Testing: Comprehensive**

Refactor the core rendering engine for better testability:

8. **LayoutEngine** (1,400 lines)
   - Extract layout computation logic
   - Make it independently testable
   - Keep coupling with rendering (intentional)

9. **CursorNavigationEngine** (400 lines)
   - Extract cursor positioning logic
   - Hit-testing, navigation
   - Unit testable

10. **RenderingContext** (shared state object)
    - Replace 15-parameter method calls
    - Make dependencies explicit
    - Immutable record type (zero-cost)

11. **InputDispatcher** (300 lines, optional)
    - Route input events to handlers
    - Keep WPF event handling in DocsCanvas

**Result**: Core DocsCanvas drops from 3,067 → ~1,000 lines. All major subsystems are independently testable.

---

## Expected Outcomes

### Metrics (After Full Refactoring)

| Metric | Before | After | Change |
|--------|--------|-------|--------|
| **DocsCanvas.cs lines** | 3,067 | ~1,000 | -67% |
| **Total lines** | 8,721 | ~8,500 | -2% |
| **Unit-testable classes** | 0 | 8+ | +800% |
| **Methods per class** | ~200 | ~30-50 | -75% |
| **Cyclomatic complexity** | Very High | Medium | ~50% ↓ |
| **Test execution time** | Slow (UI) | Fast (Unit) | ~10x ↑ |

### Qualitative Improvements

**Maintainability**: ⭐⭐⭐⭐ (High)
- Easy to find and fix bugs in specific subsystems
- Clear responsibility boundaries
- Self-documenting code structure

**Testability**: ⭐⭐⭐⭐ (High)
- 70-80% of tests are fast unit tests
- No WPF boilerplate for testing layout/input
- Test failures point directly to root cause

**Extensibility**: ⭐⭐⭐⭐ (High)
- New rendering features don't touch input handling
- New input modes don't touch rendering
- New supplementary features (Find 2.0, SpellCheck Pro) are isolated

**Onboarding**: ⭐⭐⭐ (Medium)
- New developers start with single component (~400 lines)
- Not everything is necessary to understand first
- Clear dependency graph shows what connects where

---

## Safety Guarantees

### Public API Stability
✓ **Zero breaking changes** to DocsCanvas public interface
- All methods remain public
- All properties remain unchanged
- No event signature changes

### Behavioral Stability
✓ **No rendering changes** - extracted classes use same algorithms
✓ **No performance regressions** - extractions are zero-cost or improvements
✓ **No input handling changes** - keyboard/mouse behavior identical

### Risk Mitigation
✓ Extensive unit tests written for each extracted class
✓ Existing integration tests remain valid
✓ Performance profiling before/after each phase
✓ Code review checkpoints between phases
✓ Can roll back any phase independently

---

## Implementation Order

### Quick Win Sequence (Reduces Risk)

1. Start with **PageBreakManager** (598 lines)
   - Simplest extraction
   - Zero impact on core
   - Tests are straightforward

2. Move to **IMinimapDataProvider** interface
   - Decouple Minimap before refactoring core
   - Reduces methods exposed from DocsCanvas

3. Extract **FindAndReplaceController** (289 lines)
   - Good candidate for unit testing
   - Search logic is independent

4. Extract **SpellCheckController** (426 lines)
   - Service integration pattern is clear
   - No rendering coupling

**Checkpoint**: DocsCanvas now ~1,750 lines, 1,313 lines extracted, high confidence

5. Extract **TableRenderer** (400 lines)
   - Visual mode specific
   - Good starting point for Phase 2

6. Extract **LinkHandler** (150 lines)
   - Consolidates scattered link logic
   - Medium coupling

7. Extract **VisualModeManager** (200 lines)
   - Visual mode cursor behavior
   - Some layout coupling

**Checkpoint**: Visual mode is now 600+ lines of extracted code, testable

8. Extract **LayoutEngine** (1,400 lines)
   - Most complex step
   - Requires careful testing
   - Very high confidence before starting

9. Create **RenderingContext** (shared state)
   - Consolidate 15+ parameter lists
   - Improve readability

10. Extract **CursorNavigationEngine** (400 lines)
    - Clean separation of hit-testing and navigation
    - Good test coverage

11. Extract **InputDispatcher** (optional, 300 lines)
    - Only if Phase 3 success justifies it

---

## Testing Strategy

### Phase 1 Testing (Quick Wins)
```
PageBreakManager
  ✓ Unit tests: 5-10 cases (page position computation)
  ✓ Integration test: Page breaks render correctly
  
FindAndReplaceController
  ✓ Unit tests: 10+ cases (search, replace, navigation)
  ✓ Integration test: Highlighting works
  
SpellCheckController
  ✓ Unit tests: 5-8 cases (enable/disable, error tracking)
  ✓ Integration test: Underlines render correctly
```

### Phase 3 Testing (Core Refactoring)
```
LayoutEngine
  ✓ Unit tests: 20+ cases (wrapping, positioning, etc.)
  ✓ Regression tests: Compare output with original
  ✓ Performance tests: Layout time unchanged
  
CursorNavigationEngine
  ✓ Unit tests: 15+ cases (hit-testing, navigation)
  ✓ Integration tests: Cursor behavior identical
  
RenderingContext
  ✓ Compile-time validation (record type)
  ✓ Integration tests: Parameter passing works
```

**Before/After Testing**:
- Preserve all existing UI tests
- Add unit tests for each extracted class
- Regression tests to ensure behavior unchanged
- Performance benchmarks for critical paths

---

## Timeline & Resource Estimate

### Phase 1: Quick Wins
- **Duration**: 1-2 weeks
- **Team size**: 1 developer
- **Risk level**: MINIMAL
- **Confidence**: VERY HIGH
- **Effort**: ~80 hours

### Phase 2: Mode-Specific Logic
- **Duration**: 2-3 weeks
- **Team size**: 1 developer
- **Risk level**: MEDIUM
- **Confidence**: HIGH
- **Effort**: ~120 hours

### Phase 3: Core Refactoring
- **Duration**: 3-4 weeks
- **Team size**: 1-2 developers (pair on LayoutEngine)
- **Risk level**: MEDIUM-HIGH
- **Confidence**: MEDIUM-HIGH
- **Effort**: ~200 hours

**Total**: ~6 weeks, ~400 hours, best done by one developer with code review checkpoints

---

## Decision Framework

### When to Start Refactoring

**Start Phase 1 now if:**
- ✓ New features are being added frequently (need stability)
- ✓ Bugs are hard to trace to root causes (need clarity)
- ✓ Team is growing (need onboarding efficiency)
- ✓ Tests are slow (need unit testability)

**Start Phase 2/3 only if:**
- ✓ Phase 1 completed and stable
- ✓ Team has good test coverage
- ✓ No critical features in flight

### Abort Criteria

**Rollback immediately if:**
- Public API breaks (shouldn't happen)
- Rendering behavior changes (should not happen)
- Performance regresses >5% (investigate, not expected)
- Existing tests fail on same code (refactoring failed)

**Pause and reassess if:**
- Phase taking >50% longer than estimated (complexity underestimated)
- Team confidence drops (may indicate seams are wrong)

---

## Success Criteria

✅ **Phase 1 Success**:
- 1,300+ lines extracted
- DocsCanvas drops to ~1,750 lines
- All existing tests pass
- No public API changes
- No performance regression

✅ **Phase 2 Success**:
- Additional 750+ lines extracted
- Visual Mode fully abstracted
- Visual Mode can be tested independently
- All existing tests pass
- Cursor navigation behavior unchanged

✅ **Phase 3 Success**:
- LayoutEngine is independently testable
- Layout can be tested without WPF
- RenderingContext reduces method parameters
- DocsCanvas core drops to ~1,000 lines
- All existing tests pass
- No performance regression

✅ **Overall Success**:
- 89% of code extracted into focused, testable classes
- Core DocsCanvas is clear and maintainable
- New developers can understand subsystems independently
- Bug fix time reduced (can isolate issues faster)
- Feature development accelerated (less cognitive load)

---

## Next Steps

1. **Review** this analysis with the team
2. **Prioritize** which phase to start with
3. **Plan** Phase 1 tasks (PageBreakManager first)
4. **Create** branch for Phase 1 work
5. **Write** unit tests before extracting (TDD approach)
6. **Extract** one component at a time
7. **Profile** before and after each extraction
8. **Review** code and tests thoroughly
9. **Merge** to main when all tests pass
10. **Repeat** for next component

---

## Reference Documents

See accompanying files for detailed information:

- **DocsCanvas_Analysis.md**: Complete problem analysis, dependency patterns, refactoring details
- **Architecture_Diagrams.md**: Visual representations of current vs. target architecture
- **Implementation_Examples.md**: Concrete code examples for each extraction, test cases, migration patterns

---

## Questions & Discussion

**Q: Why not split into smaller pieces?**
A: Layout, rendering, and cursor navigation are tightly coupled. Splitting them requires a shared context object and leads to more complex code, not less. Better to keep them together and improve their internal organization.

**Q: What if Phase 3 is too risky?**
A: Stop at Phase 2. DocsCanvas drops to 1,000 lines anyway. Phase 3 (LayoutEngine extraction) gives 70% of the benefit but 80% of the complexity. Phase 1-2 alone are transformative.

**Q: What about performance?**
A: All extractions maintain the same algorithm. RenderingContext is a record type (stack-allocated). LayoutEngine should perform the same or better due to better code locality. Expected: No measurable performance impact.

**Q: Can we do this incrementally without disrupting active development?**
A: Yes! Each phase is isolated and backward compatible. Develop on separate branch, merge when complete and tested. Phase 1 can be done in parallel with normal feature work.

**Q: Who should do this refactoring?**
A: A developer who:
- Understands the codebase well
- Is comfortable with WPF rendering
- Can write comprehensive tests
- Has 6-8 weeks available
- Can explain architecture clearly

**Q: Will this break anything?**
A: No, if done properly:
- Tests provide safety net
- Public API unchanged
- Behavior unchanged
- Performance unchanged
- Risk increases only in Phase 3, which is optional

---

## Conclusion

**DocsCanvas exhibits classic god class symptoms**, but the solution isn't to atomize it into 30 classes. The solution is to:

1. **Extract truly independent features** (25% of code) with no risk
2. **Organize mode-specific logic** (15% of code) into clean subsystems
3. **Improve core rendering** (60% of code) without breaking cohesion

**The payoff is significant**:
- 67% reduction in main file size
- 8+ independently testable classes
- Clear architectural boundaries
- Much easier to maintain and extend
- Better onboarding for new developers

**The approach is pragmatic**:
- Starts with quick wins to build confidence
- Phases allow stopping at any point
- Zero public API breaks
- Extensive testing at each step
- Clear rollback strategy

**This is implementable**: 6 weeks, ~400 hours, 1 developer. Worth the investment for improved maintainability and team velocity.
