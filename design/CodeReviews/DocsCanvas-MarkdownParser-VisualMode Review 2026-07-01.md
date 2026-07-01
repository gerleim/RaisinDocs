# Code Review — DocsCanvas.cs, MarkdownParser.cs, DocsCanvas.VisualMode.cs

**Date**: 2026-07-01
**Scope**: The 3 largest source files (~5,370 lines combined), plus related files (Document.cs, BlockVisualMap.cs, ImageCache.cs, DocsEditor.xaml.cs, DocsFormattingBar.cs, DocsCanvas.SourceMode.cs)
**Findings**: 2 Critical, 5 High, 15 Medium, 12 Low

---

## CRITICAL (2)

### C1 — Layout mutation inside OnRender

- **Severity**: Critical
- **Category**: Bug / Risk
- **Location**: `DocsCanvas.cs:3103-3104`
- **What's wrong**: `Minimap?.InvalidateVisual()` and `ScrollStateChanged?.Invoke()` fire inside `OnRender`. The scroll-state subscriber (`DocsEditor.UpdateScrollBar`) sets scrollbar visibility synchronously, triggering a WPF layout pass *during* a render pass. This violates the `OnRender` contract: it must only draw, never mutate layout state or fire events that trigger layout.
- **What to do**: Post these via `Dispatcher.BeginInvoke(DispatcherPriority.Normal, ...)`, or fire `ScrollStateChanged` from every method that changes `_scrollOffset`/`_totalContentHeight` instead of from `OnRender`.

### C2 — MoveOutOfTable returns true without moving cursor

- **Severity**: Critical
- **Category**: Bug
- **Location**: `DocsCanvas.VisualMode.cs:466-497`
- **What's wrong**: `MoveOutOfTable` returns `true` on fallthrough when the table is at the document boundary. The caller thinks navigation succeeded and marks the key event handled. **The user is permanently stuck** in the last/first cell of a boundary table.
- **What to do**: Change the fallthrough `return true` at line 497 to `return false`. Same fix for the backward branch.

---

## HIGH (5)

### H1 — InsertColorWrapper cursor placement in multi-block case

- **Severity**: High
- **Category**: Bug
- **Location**: `DocsCanvas.cs:622`
- **What's wrong**: The multi-block (`sb != eb`) branch sets the cursor to `eo + (sb == eb ? opener.Length : 0)`, which always evaluates to `eo + 0`. The cursor ends up trapped inside the closing `<!--/@fg-->` tag. The single-block branch at line 613 correctly uses `eo + opener.Length`.
- **What to do**: Change to `_doc.CursorOffset = eo + closer.Length`.

### H2 — HitTestVisualLine crash on empty _visualLines

- **Severity**: High
- **Category**: Risk / Crash
- **Location**: `DocsCanvas.cs:2049-2057`
- **What's wrong**: `HitTestVisualLine` returns `_visualLines.Count - 1` when `y` is past all lines. If `_visualLines` is empty (all blocks skipped in visual mode), this returns `-1`. All callers index `_visualLines` without checking → `IndexOutOfRangeException`.
- **What to do**: Add `if (_visualLines.Count == 0) return 0;` guard. Add empty-checks in `CursorToVisualLineIndex` and `EnsureCursorVisible` too.

### H3 — ToggleInlineStyle cannot remove Bold/Italic from BoldItalic spans

- **Severity**: High
- **Category**: Bug
- **Location**: `DocsCanvas.cs:1955-2018`
- **What's wrong**: The removal loop at line 2018 does `if (run.Style != targetStyle) continue`, which skips `BoldItalic` runs because `BoldItalic != Bold`. Bold/Italic cannot be individually removed from `***bold-italic***` spans — silent no-op.
- **What to do**: Handle `BoldItalic` in the removal branch: removing Bold from `***` should convert markers to `*` (leaving italic), and vice versa.

### H4 — Table column width measurement off by leading whitespace

- **Severity**: High
- **Category**: Bug
- **Location**: `DocsCanvas.VisualMode.cs:800`
- **What's wrong**: `ComputeAllTableColumnWidths` passes `cell.Start` as `blockOffset` to `MeasureStringWidth`, but the measured string is already trimmed of leading spaces. The style lookup is off by `leadingTrim` characters → incorrect column widths for styled cell content.
- **What to do**: Pass `cell.Start + leadingTrim` as `blockOffset`.

### H5 — FindClosingStars skips position start+1

- **Severity**: High
- **Category**: Bug
- **Location**: `MarkdownParser.cs:1024-1044`
- **What's wrong**: `FindClosingStars` double-increments `i` when skipping a too-long star run: `i = start; i++` inside the loop body, then the `for` loop does another `i++`. Position `start+1` is always skipped. A valid exact-length match starting there is missed.
- **What to do**: Replace `i = start; i++;` with `i--` to cancel the for-loop increment.

---

## MEDIUM (15)

### M1 — ComputeLayout called from OnRender

- **Severity**: Medium
- **Category**: Architecture
- **Location**: `DocsCanvas.cs:2983-3105`
- **What's wrong**: `OnRender` calls `ComputeLayout()`, which mutates `_visualLines`, `_lineYPositions`, `_tableColumnWidths` during a render pass. Layout should happen in `MeasureOverride`/`ArrangeOverride`.
- **What to do**: Move `ComputeLayout()` to the layout phase.

### M2 — O(n²) lookup in DrawJoinedLine

- **Severity**: Medium
- **Category**: Performance
- **Location**: `DocsCanvas.cs:3131-3138`
- **What's wrong**: `Array.IndexOf(group.SoftBreakOffsets, i)` inside a per-character loop in `DrawJoinedLine` → O(n²) for reflowed paragraphs.
- **What to do**: Convert `SoftBreakOffsets` to `HashSet<int>`.

### M3 — SelectionHasStyle returns true for empty selection

- **Severity**: Medium
- **Category**: Bug
- **Location**: `DocsCanvas.cs:1018-1045`
- **What's wrong**: `SelectionHasStyle` returns `true` for an empty selection / collapsed cursor in an empty block. Formatting bar shows all styles active on empty content.
- **What to do**: Track `anyRunChecked`; return `false` when no runs were inspected.

### M4 — Duplicate MeasureStringWidth overloads

- **Severity**: Medium
- **Category**: Duplication
- **Location**: `DocsCanvas.cs:1368-1379` vs `DocsCanvas.cs:1774-1784`
- **What's wrong**: Two `MeasureStringWidth` overloads with subtly different signatures doing the same work. Changes must be replicated in both.
- **What to do**: Unify into one method with default parameters.

### M5 — goto skipGap in ComputeLayoutCore

- **Severity**: Medium
- **Category**: Code Smell
- **Location**: `DocsCanvas.cs:1647-1658`
- **What's wrong**: `goto skipGap` used to skip inter-block gap calculation.
- **What to do**: Replace with a `bool addGap` flag.

### M6 — DetectTables drops ColorSpans and BlockColor

- **Severity**: Medium
- **Category**: Bug
- **Location**: `MarkdownParser.cs:356-390`
- **What's wrong**: `DetectTables` replaces blocks with new `ParsedBlock` instances but doesn't copy `ColorSpans` or `BlockColor`. Inline color tags inside table cells are silently lost.
- **What to do**: Copy `ColorSpans`/`BlockColor` to the new blocks. Better: convert `ParsedBlock` to a record for safe `with` expressions.

### M7 — ParsedBlock fragile clone-all-fields pattern

- **Severity**: Medium
- **Category**: Maintainability
- **Location**: `MarkdownParser.cs:204-240, 356-390`
- **What's wrong**: `ParsedBlock` is manually cloned field-by-field in multiple places. Already caused M6 above. Adding new fields requires updating every copy site.
- **What to do**: Convert to `record class` so `with` expressions work.

### M8 — Strikethrough delimiters not hidden in visual mode

- **Severity**: Medium
- **Category**: Bug
- **Location**: `BlockVisualMap.cs:146-153`
- **What's wrong**: `Strikethrough` falls to the `_ => 0` default — `~~` delimiters are never hidden in visual mode. Displays as `~~struck~~` with both tildes AND strikethrough formatting.
- **What to do**: Add `InlineStyle.Strikethrough => 2` to the switch.

### M9 — ImageCache thread safety

- **Severity**: Medium
- **Category**: Risk / Thread Safety
- **Location**: `ImageCache.cs:12-58`
- **What's wrong**: `_cache` and `_pending` are plain `Dictionary` accessed from both UI and threadpool threads. The `else` branch (dispatcher null) at lines 52-57 mutates both dictionaries on a threadpool thread without synchronization.
- **What to do**: Use `ConcurrentDictionary`, or ensure all mutations marshal to the dispatcher.

### M10 — HttpClient has no timeout

- **Severity**: Medium
- **Category**: Risk
- **Location**: `ImageCache.cs:13`
- **What's wrong**: Static `HttpClient` has no timeout. Slow servers block threadpool threads indefinitely.
- **What to do**: Set `_http.Timeout = TimeSpan.FromSeconds(15)`.

### M11 — Brush allocations every render frame

- **Severity**: Medium
- **Category**: Performance
- **Location**: `DocsCanvas.cs:3198-3419`
- **What's wrong**: New `SolidColorBrush` objects created and frozen every render frame for color spans. Dozens per frame at 60fps = significant GC pressure.
- **What to do**: Cache brushes by `Color` in a `Dictionary<Color, Brush>`.

### M12 — Redundant GetBlockText allocations in render path

- **Severity**: Medium
- **Category**: Performance
- **Location**: `DocsCanvas.cs:3228`
- **What's wrong**: `ApplySyntaxDimming` calls `_doc.GetBlockText()` (new string allocation) redundantly — caller already has `blockText`. Same for `DrawTrailingSpaceDots`.
- **What to do**: Pass `blockText` as a parameter.

### M13 — _parsedBlocks accessed without bounds check

- **Severity**: Medium
- **Category**: Risk
- **Location**: `DocsCanvas.VisualMode.cs:231-237`
- **What's wrong**: `_parsedBlocks[_doc.CursorBlock - 1]` accessed without bounds-checking against `_parsedBlocks.Count`. Stale list → `ArgumentOutOfRangeException`.
- **What to do**: Add `if (_doc.CursorBlock >= _parsedBlocks.Count) return;` guard.

### M14 — HandleTableArrow fallthrough returns true

- **Severity**: Medium
- **Category**: Bug
- **Location**: `DocsCanvas.VisualMode.cs:347-419`
- **What's wrong**: `HandleTableArrow` returns `true` on fallthrough when cursor is in trailing whitespace/pipes past all cells. Cursor stuck, event marked handled.
- **What to do**: Clamp to nearest cell boundary, or return `false`.

### M15 — Null-forgiving access on map.Images

- **Severity**: Medium
- **Category**: Risk
- **Location**: `DocsCanvas.VisualMode.cs:1124`
- **What's wrong**: `map.Images!` null-forgiving access in `DrawVisualLineWithImages`. NRE if called without `HasImagesOnLine` gate.
- **What to do**: Add `if (map.Images == null) return;` guard.

---

## LOW (12)

### L1 — God class architecture

- **Severity**: Low
- **Category**: Architecture
- **Location**: `DocsCanvas.cs` (entire file, ~4800 lines across 3 partials)
- **What's wrong**: `DocsCanvas` handles rendering, input, layout, formatting, scroll, tables, images, link popups, and test hooks. The partial-class split is not by abstraction — all three files directly access the same fields.
- **What to do**: Long-term: extract `LinkPopupController`, `SelectionManager`, `FormattingCommands`, `LayoutEngine`, `ScrollController`. Short-term: move `Test*` members into `DocsCanvas.TestHooks.cs`.

### L2 — Unbounded _charWidthCache

- **Severity**: Low
- **Category**: Risk
- **Location**: `DocsCanvas.cs:167`
- **What's wrong**: `_charWidthCache` grows without bound. No eviction policy.
- **What to do**: Add a size cap (e.g., 4096) or clear on theme change.

### L3 — Silent catch on GetDpi

- **Severity**: Low
- **Category**: Suspicious
- **Location**: `DocsCanvas.cs:1201-1203`
- **What's wrong**: `try { GetDpi } catch { }` silently swallows all exceptions — wrong DPI scale with no diagnostic.
- **What to do**: Log the exception or check `IsLoaded` before calling `GetDpi`.

### L4 — GetStyleKey loses bold/italic for FencedCodeLine

- **Severity**: Low
- **Category**: Bug
- **Location**: `DocsCanvas.cs:1279-1289`
- **What's wrong**: `GetStyleKey` unconditionally resets `fontId=1` for `FencedCodeLine`, losing bold/italic distinction in the cache key.
- **What to do**: Short-circuit at the top: `if (blockKind == FencedCodeLine) return 1 * 100 + (int)GetBlockFontSize(blockKind);`.

### L5 — IsFenceLine overly permissive

- **Severity**: Low
- **Category**: Risk
- **Location**: `MarkdownParser.cs:502-506`
- **What's wrong**: Only checks `trimmed.StartsWith("```")` — backticks in info string pass, no fence-length tracking.
- **What to do**: Verify no backticks after the initial run; track opening fence backtick count.

### L6 — MarkEmphasis diverges from CommonMark

- **Severity**: Low
- **Category**: Suspicious
- **Location**: `MarkdownParser.cs:962-1021`
- **What's wrong**: `MarkEmphasis` with `>=3` stars uses greedy matching, diverging from CommonMark delimiter-run algorithm.
- **What to do**: Document as known limitation, or implement delimiter run stack algorithm.

### L7 — Duplicated HTML comment detection logic

- **Severity**: Low
- **Category**: Duplication
- **Location**: `MarkdownParser.cs:1162-1257` vs `1282-1313`
- **What's wrong**: `ParseInlineColorTags` and `FindInlineColorTagRanges` duplicate HTML comment detection logic.
- **What to do**: Extract a shared helper that yields `(start, end, isOpener, body)` tuples.

### L8 — Link definition title doesn't handle escaped delimiters

- **Severity**: Low
- **Category**: Bug
- **Location**: `MarkdownParser.cs:320-322`
- **What's wrong**: `IndexOf(qClose, titleStart)` doesn't handle backslash-escaped delimiters in link definition titles.
- **What to do**: Replace `IndexOf` with a loop that skips `\`-escaped characters.

### L9 — SplitLines negates span parameter benefit

- **Severity**: Low
- **Category**: Code Smell
- **Location**: `MarkdownParser.cs:1376-1392`
- **What's wrong**: Takes `ReadOnlySpan<char>` but immediately calls `.ToString()`.
- **What to do**: Change parameter to `string` (honest API) or use span-based slicing throughout.

### L10 — Three full scans before main parse

- **Severity**: Low
- **Category**: Architecture
- **Location**: `MarkdownParser.cs:86-89`
- **What's wrong**: `CollectLinkDefinitions`, `CollectThemeDefinitions`, and the main loop each independently scan all blocks.
- **What to do**: Combine the two pre-scans into a single pass.

### L11 — Image placeholder rendering duplicated 3 times

- **Severity**: Low
- **Category**: Duplication
- **Location**: `VisualMode.cs:1141-1153`, `SourceMode.cs:28-39`, `SourceMode.cs:72-83`
- **What's wrong**: Placeholder brush create/freeze/draw code repeated three times. Brush recreated each frame.
- **What to do**: Extract `DrawImagePlaceholder` helper; make the placeholder brush a static frozen field.

### L12 — Cell-trimming logic repeated ~9 times

- **Severity**: Low
- **Category**: Duplication
- **Location**: `VisualMode.cs:358, 439, 458, 704, 746, 896, 933, 973, 984`
- **What's wrong**: `while (s < e && blockText[s] == ' ') s++; while (e > s && blockText[e-1] == ' ') e--;` pattern appears ~9 times across table code.
- **What to do**: Extract `(int TrimStart, int TrimEnd) TrimCellRange(string blockText, TableCellInfo cell)`.

---

## Test Coverage Gaps

The test suite is solid for common cases but has notable gaps that would expose several findings above:

1. No tests for emphasis with >3 opening stars (e.g., `****text****`) — would expose H5
2. No tests for `FindClosingStars` with mismatched star run lengths — would expose H5
3. No tests for colored text inside table cells — would expose M6
4. No tests for strikethrough in visual mode (BlockVisualMap) — would expose M8
5. No tests for link definition titles with escaped delimiters — would expose L8
6. No tests for `IsFenceLine` with backticks in the info string
7. No tests for `ParseTableCells` with degenerate inputs (empty string, single pipe, no pipes)
8. No tests for empty `_visualLines` scenarios — would expose H2
9. No tests for toggling Bold off from `***bold-italic***` — would expose H3
