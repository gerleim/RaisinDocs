# DocsCanvas Dependency Interface Refactoring Plan

## Executive Summary

**Problem:** All 11 extracted DocsCanvas classes depend on `IDocsCanvasServices` (god interface), causing 804 internal casts to DocsCanvas when accessing fields not exposed in the interface.

**Solution:** Refactor each class to depend on its specific interface (INavigationServices, ILayoutDataServices, etc.) instead of the composite interface.

**Expected Outcome:**
- ✅ Zero internal casts (804 → 0)
- ✅ Better testability (mock specific interfaces)
- ✅ Cleaner architecture (explicit dependencies)
- ✅ No public API changes
- ✅ Performance improvement
- ✅ 11-step implementation roadmap

---

## 1. INTERFACE AUDIT & CLASS MAPPING

| Class | Current Dependency | Required Primary Interfaces | Secondary Interfaces | Key Fields/Methods Accessed | Interface Gaps |
|-------|-------------------|---------------------------|----------------------|---------------------------|-----------------|
| **LayoutEngine** | IDocsCanvasServices | ILayoutDataServices, IDocumentServices | IRenderingServices, IParsedContentServices, IVisualModeServices, ILoggingServices | _layoutDirty, _measure, _parsedBlocks, _doc, _syntaxHighlighter, _visualMaps, _visualBlockStructure, _blockToGroup, ComputeLayout, ClampCursorAwayFromHidden | Need: _syntaxHighlighter, _blockToGroup, ComputeLayout access |
| **RenderingContext** | IDocsCanvasServices | IRenderingServices, ILayoutDataServices | IParsedContentServices, IVisualModeServices, IScrollServices, ITableServices, IDocumentServices, ILoggingServices, ICanvasOperations | _measure, _palette, _visualLines, _lineYPositions, _tableRenderer, _parsedBlocks, _scroll, _visualMaps, _layoutEngine, GetEffectiveLineHeight, ActualWidth, ActualHeight | Need: _layoutEngine access, _syntaxBrushCache access |
| **CursorNavigationEngine** | IDocsCanvasServices | ILayoutDataServices, IDocumentServices | IVisualModeServices, ITableServices, IRenderingServices | _visualLines, _doc, _parsedBlocks, _visualMaps, _tableColumnWidths, _cursorAtLineEnd, MeasureJoinedRange | Need: _cursorAtLineEnd management, MeasureJoinedRange |
| **VisualModeManager** | IDocsCanvasServices | IVisualModeServices, IDocumentServices | IParsedContentServices, ILoggingServices | _visualMaps, _doc, Logger, IsVisual, SkipCursorOverHiddenRanges | All needed methods/properties already in interfaces |
| **TableRenderer** | IDocsCanvasServices | ITableServices, IRenderingServices | IDocumentServices, IParsedContentServices, ILayoutDataServices | _doc, _parsedBlocks, _measure, _tableColumnWidths, _visualMaps, GetCachedBrush | All needed already available |
| **LinkHandler** | IDocsCanvasServices | INavigationServices, IDocumentServices | IParsedContentServices, ILayoutDataServices | _parsedBlocks, _doc, HitTestToPosition, ComputeLayout | All needed |
| **PageBreakManager** | IDocsCanvasServices | IRenderingServices | ILayoutDataServices (for test properties) | ActualWidth, InvalidateVisual, Test* properties | Need: Expose test properties through interface |
| **FindAndReplaceController** | IDocsCanvasServices | ISearchServices, IDocumentServices, ICanvasOperations | IRenderingServices | _doc, FindBar, InvalidateVisual, SealAndStopTimer | FindBar might need its own interface |
| **SpellCheckController** | IDocsCanvasServices | ICanvasOperations, IImageServices, IDocumentServices | IRenderingServices | DocumentBasePath, InvalidateVisual, Dispatcher, _doc | Need: DocumentBasePath, Dispatcher through interfaces |
| **TableInputHandler** | IDocsCanvasServices | IDocumentServices, IParsedContentServices | ICanvasOperations (for SealAndStopTimer) | _parsedBlocks, _doc, SealAndStopTimer | All needed |
| **ColorFormattingManager** | IDocsCanvasServices | IDocumentServices, IParsedContentServices | ILayoutDataServices, ICanvasOperations, IScrollServices | _doc, _parsedBlocks, ComputeLayout, SealAndStopTimer, InvalidateLayout, EnsureCursorVisible, RaiseFormattingChanged | Need: RaiseFormattingChanged method |

---

## 2. INTERFACE ENHANCEMENT ANALYSIS

### Missing Methods/Properties to Add

**ILayoutDataServices enhancements:**
- Test properties for testing (TestLayoutVersion, TestVisualLineCount, TestLineYPositions, TestVisualLines, TestParsedBlocks, TestMeasure)

**ICanvasOperations enhancements:**
- Add `void RaiseFormattingChanged()` method for ColorFormattingManager

**Document access:**
- All classes need Document reference (already in IDocumentServices)

**Internal Helper Access:**
- LayoutEngine uses _syntaxHighlighter - add to IRenderingServices
- LayoutEngine uses _blockToGroup - needs interface property
- RenderingContext uses _layoutEngine - needs access to helper methods

---

## 3. REFACTORING STRATEGY

### Approach: Composite + Specific Interfaces

Instead of forcing each class to use only one interface, use:
1. **Primary interface** (the most critical one)
2. **Secondary interfaces** as needed through constructor injection
3. **Avoid** using the composite IDocsCanvasServices

**Example - Current (bad):**
```csharp
public LayoutEngine(IDocsCanvasServices services)
```

**Example - Refactored (good):**
```csharp
public LayoutEngine(ILayoutDataServices layout, IDocumentServices doc, 
                   IRenderingServices rendering, IParsedContentServices content,
                   IVisualModeServices visual, ILoggingServices logging)
```

### Constructor Injection Pattern

Each class constructor should be updated to accept specific interfaces instead of the composite interface.

---

## 4. STEP-BY-STEP IMPLEMENTATION ROADMAP

### Phase 1: Interface Preparation (No Breaking Changes)

1. Add missing methods to interfaces (RaiseFormattingChanged to ICanvasOperations)
2. Add test property accessors to ILayoutDataServices
3. Update DocsCanvas.IDocsCanvasServices.cs to expose new interface members
4. Verify all interfaces have needed members
5. **Build checkpoint:** All interfaces complete, DocsCanvas implements them

### Phase 2: Refactor Classes (One by One, Testing After Each)

**Order (dependency-based - refactor dependencies first):**

#### 1. VisualModeManager (Simplest)
- Change: `IDocsCanvasServices services` → `IVisualModeServices visual, IDocumentServices doc, ILoggingServices logging`
- Casts to remove: All `((DocsCanvas)_services)` → Use specific interface methods
- Build & test after completion

#### 2. PageBreakManager
- Change: Add IRenderingServices for ActualWidth
- Casts to remove: `((DocsCanvas)_services).TestLayoutVersion` → `layout.TestLayoutVersion`
- Build & test after completion

#### 3. LinkHandler
- Change: `INavigationServices nav, IDocumentServices doc, IParsedContentServices content, ILayoutDataServices layout`
- Casts to remove: Casting for `_parsedBlocks`, `HitTestToPosition`, `ComputeLayout`
- Build & test after completion

#### 4. TableInputHandler
- Change: `IDocumentServices doc, IParsedContentServices content, ICanvasOperations canvas`
- Casts to remove: Direct `_parsedBlocks`, `_doc` access through interfaces
- Build & test after completion

#### 5. ColorFormattingManager
- Change: `IDocumentServices doc, IParsedContentServices content, ILayoutDataServices layout, ICanvasOperations canvas, IScrollServices scroll`
- Casts to remove: Direct field access, RaiseFormattingChanged through canvas interface
- Build & test after completion

#### 6. TableRenderer
- Change: `ITableServices table, IRenderingServices rendering, IDocumentServices doc, IParsedContentServices content, ILayoutDataServices layout`
- Casts to remove: Field access through interfaces
- Build & test after completion

#### 7. FindAndReplaceController
- Change: `ISearchServices search, IDocumentServices doc, ICanvasOperations canvas, IRenderingServices rendering`
- Casts to remove: FindBar access, InvalidateVisual through rendering, SealAndStopTimer through canvas
- Build & test after completion

#### 8. SpellCheckController
- Change: `ICanvasOperations canvas, IImageServices images, IDocumentServices doc, IRenderingServices rendering`
- Casts to remove: Dispatcher, InvalidateVisual, DocumentBasePath through interfaces
- Build & test after completion

#### 9. CursorNavigationEngine (Complex - 158 casts)
- Change: `ILayoutDataServices layout, IDocumentServices doc, IVisualModeServices visual, ITableServices table, IRenderingServices rendering`
- Casts to remove: `_visualLines`, `_doc`, `_parsedBlocks`, `_visualMaps`, `_tableColumnWidths` access through interfaces
- Special: _cursorAtLineEnd is internal state - needs property in IDocumentServices or custom getter
- Build & test after completion

#### 10. RenderingContext (Complex - 142 casts)
- Change: `IRenderingServices rendering, ILayoutDataServices layout, IParsedContentServices content, IVisualModeServices visual, IScrollServices scroll, ITableServices table, IDocumentServices doc, ILoggingServices logging, ICanvasOperations canvas, LayoutEngine layoutEngine`
- Casts to remove: All field access through specific interfaces
- Special: _layoutEngine.MeasureJoinedRange() - needs layout engine interface or expose through rendering
- Build & test after completion

#### 11. LayoutEngine (Most Complex - 200+ casts)
- Change: `ILayoutDataServices layout, IDocumentServices doc, IRenderingServices rendering, IParsedContentServices content, IVisualModeServices visual, ILoggingServices logging, Document document`
- Casts to remove: All field casts through interfaces
- Special: _syntaxHighlighter, _blockToGroup management, ComputeLayout direct field mutations
- Build & test after completion

### Phase 3: Update DocsCanvas Constructor
- Update the 11 instantiation lines to pass specific interfaces instead of `(IDocsCanvasServices)this`
- Example:
  ```csharp
  _visualModeManager = new VisualModeManager((IVisualModeServices)this, (IDocumentServices)this, (ILoggingServices)this);
  ```

### Phase 4: Cleanup & Verification
1. Remove any now-unused using directives
2. Verify build with 0 errors
3. Run full test suite
4. Verify no regression in editor behavior

---

## 5. BUILD VERIFICATION STRATEGY

After each class refactoring:

1. **Syntax Check:** `dotnet build` - 0 errors
2. **Cast Verification:**
   ```bash
   grep -c "((DocsCanvas)_services)" [ClassName].cs
   ```
   Should go to 0 after refactoring
3. **Total Cast Count:**
   ```bash
   grep -c "((DocsCanvas)" RaisinDocs/DocsCanvas/*.cs
   ```
   Should decrement by ~73 per class (804 / 11 ≈ 73 per class)
4. **Test Run:** Run unit tests to verify no functional regression
5. **Manual Verification:** Open editor app, verify no visual regression

---

## 6. RISK ASSESSMENT

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Breaking changes to public API | **Low** | High | All changes are internal to DocsCanvas - no public API changes |
| Circular dependency creation | **Low** | High | Carefully review constructor parameters for cycles |
| Performance regression | **Low** | Medium | Interfaces add minimal overhead; measure if concerned |
| Missed cast locations | **Low** | Medium | Use grep to verify all 804 casts are replaced |
| Test coverage gaps | **Medium** | Medium | Run full test suite after each refactoring phase |
| Incremental build breaks | **Low** | High | Build after EACH class refactoring, fix immediately |
| Document access patterns | **Medium** | Medium | Document should be directly injected where needed |

**Mitigation Strategy:**
- Commit frequently (after each class)
- Build and test after each change
- Use git bisect if regression occurs
- Keep git history clean for easy rollback

---

## 7. SUCCESS CRITERIA CHECKLIST

Before considering refactoring complete:

| Criterion | Method | Expected Result |
|-----------|--------|-----------------|
| All 11 classes updated | Code review of each class | All constructors changed to use specific interfaces |
| Zero casting | `grep -c "((DocsCanvas)" RaisinDocs/DocsCanvas/*.cs` | Result = **0** |
| Build succeeds | `dotnet build` | **0 errors** |
| Tests pass | `dotnet test` | All tests passing at baseline level |
| Editor runs | Manual run | App opens, can edit, renders correctly |
| No performance regression | Manual testing | Scrolling, typing, visual mode are responsive |
| Code review approval | Architecture review | Plan and implementation approved |
| No public API changes | API compatibility check | All new interfaces are `internal` |
| Total cast count | Grep verification | 0 remaining `((DocsCanvas)...)` casts |

---

## 8. SPECIFIC INTERFACE ENHANCEMENTS REQUIRED

### Add to ILayoutDataServices:
```csharp
// Test properties for PageBreakManager and testing
int TestLayoutVersion { get; }
int TestVisualLineCount { get; }
List<double> TestLineYPositions { get; }
List<DocsCanvas.VisualLine> TestVisualLines { get; }
List<ParsedBlock>? TestParsedBlocks { get; }
TextMeasurer TestMeasure { get; }
double GetEffectiveLineHeightPublic(DocsCanvas.VisualLine vl);
```

### Add to ICanvasOperations:
```csharp
// Formatting notification for ColorFormattingManager
void RaiseFormattingChanged();
```

### Consider for IRenderingServices:
```csharp
// For LayoutEngine syntax highlighting
SyntaxHighlighter SyntaxHighlighter { get; }

// For LayoutEngine visual state tracking
Dictionary<int, DocsCanvas.ParagraphGroup> BlockToGroup { get; set; }
```

---

## 9. IMPLEMENTATION ORDER RATIONALE

1. **VisualModeManager first:** Minimal dependencies, acts as confidence builder
2. **PageBreakManager second:** Still simple, introduces test property pattern
3. **LinkHandler, TableInputHandler:** Single/dual interface dependencies
4. **ColorFormattingManager, TableRenderer, FindAndReplaceController:** Moderate complexity
5. **SpellCheckController:** Canvas operations pattern
6. **CursorNavigationEngine:** Moderate-to-complex, affects navigation
7. **RenderingContext:** Complex with many dependencies
8. **LayoutEngine:** Most complex (200+ casts), do last so support structures are in place

This order ensures:
- Early builds show progress
- Pattern establishment before complex classes
- Interface enhancements tested early
- Core dependencies resolved before complex consumers

---

## 10. PERFORMANCE IMPACT

### Expected Improvements:
- **Page Up/Page Down:** Currently 8-9 casts per call → 0 casts (immediate responsiveness gain)
- **Left/Right Arrow:** Currently 32 casts per call → 0 casts
- **DrawJoinedLine:** Currently 20+ casts per line rendered → 0 casts
- **General:** Reduced type-checking overhead in hot paths

### Testing Method:
1. Measure keyboard responsiveness before refactoring
2. Measure after refactoring
3. Expected: Noticeable improvement in Page Up/Page Down and arrow key navigation

---

## 11. CRITICAL FILES FOR IMPLEMENTATION

- `RaisinDocs/DocsCanvas/IDocsCanvasServices.cs` - Interface definitions
- `RaisinDocs/DocsCanvas/DocsCanvas.IDocsCanvasServices.cs` - Explicit interface implementations
- `RaisinDocs/DocsCanvas/DocsCanvas.cs` - Main class constructor updates
- All 11 extracted class files - Constructor and field updates

---

## 12. IMPLEMENTATION TIMELINE

- **Phase 1 (Interface Prep):** 30 minutes - 1 hour
- **Phase 2 (Class Refactoring):** 3-4 hours (11 classes × 15-20 min average)
- **Phase 3 (Constructor Update):** 30 minutes
- **Phase 4 (Cleanup & Verification):** 30 minutes - 1 hour
- **Total:** 5-7 hours of focused development

---

## Next Steps

1. Review and approve this plan
2. Execute Phase 1 (interface enhancements)
3. Execute Phase 2-4 following the step-by-step roadmap
4. Verify all success criteria are met
5. Commit with message: "Refactor DocsCanvas classes to use specific interfaces instead of god interface"

---

**Document Version:** 1.0  
**Date:** 2026-08-03  
**Status:** Ready for Implementation
