# Wrap table cell text in visual mode

## Context
In visual mode, a table wider than the window is cut off on the right. Every header or data row is a single `VisualLine` (LayoutEngine.cs:580-588). Its column widths are the widest cell, with no cap: `ComputeAllTableColumnWidths(maxWidth)` never reads `maxWidth` (TableRenderer.cs:34-74). The `ClipToBounds` clip hides everything past the edge. Source mode already wraps these lines normally.

**Goal:** shrink the columns to fit the window and wrap cell text onto several lines inside its cell. The row grows taller to hold them. This is the "Multi-line table cells" item in `design/RaisinDocs design v01.md:269`.

**Decisions already made:**
1. **Column widths use the browser rule.** Each column keeps at least its longest word, and the space still missing comes out of columns in proportion to (natural − min).
2. **Long words break mid-word** when even the minimum widths don't fit. Columns can then go below their longest word, down to a floor of about 3 characters.
3. **Up/Down move one wrapped line within the cell**, keeping pixel X. From the cell's first or last line they move to the adjacent row.

## Core approach
Cells wrap independently, so a row cannot be split into several VisualLines by offset ranges. The row stays **one VisualLine**, and:
- `OverrideHeight = LineCount × GetLineHeight(kind)`. This is the hook images already use. Y summation, `GetEffectiveLineHeight`, `SnappedLineHeight`, the row background, `DrawTableLines`, `HitTestVisualLine` and the rectangular table selection all honour it already.
- A new `TableRowLayout` records each cell's sub-lines as raw offsets. Every table-specific position question then answers with both an X and a sub-line.

## Steps

### 1. Layout data: new `RaisinDocs/DocsCanvas/TableRowLayout.cs`
- Define `TableRowLayout` as a plain `sealed class` nested in `partial class DocsCanvas`, like `ParagraphGroup`, so it compares by reference.
  - Fields: `int[][] LineStarts` (per cell; `[0]` = trimStart), `int[] CellEnds` (trimEnd), `int LineCount` (≥1).
  - Helpers: `GetLine(cell, k)` and `LineOf(cell, raw)`, which returns the last k whose start ≤ raw.
- Add `public TableRowLayout? TableLayout { get; init; }` to `VisualLine` (DocsCanvas.cs:242-248).
  - The record-struct equality stays a reference compare, so `GetVisualLineSpacing` and `GetTextStartXForVisualLine` still work.
  - Print gets it for free, because the paginator snapshots `_lines` (Print.cs:185).
  - `null` means one line.

### 2. Column widths: `TableRenderer.ComputeAllTableColumnWidths`
- **Natural width** per cell: `_rendering.MeasureRangeWidth(text, s, e-s, p.Runs, p.Kind, map)`. It skips hidden characters, and the header kind gives bold. This replaces the display-string plus `MeasureStringWidth` call, which passed raw offsets for display text. That change is needed so wrap decisions match measurement.
- **Minimum width:** the widest word, splitting at `' '` only (FitLine's break point) and measured the same way.
- Add `2*_tableCellPadding` to both natural and minimum.
- **Choose widths by tier**, with `avail = maxWidth`:
  - **Tier 1:** `avail <= 0 || sumNat <= avail` → natural. Unchanged from today.
  - **Tier 2:** `sumMin <= avail` → `min + (nat-min)*(avail-sumMin)/(sumNat-sumMin)`.
  - **Tier 3:** `floor = Math.Min(min, 3*charW('0', header) + 2*pad)`. Width is `floor` if `sumFloor >= avail`, else `floor + (min-floor)*(avail-sumFloor)/(sumMin-sumFloor)`.

### 3. Wrapping: LayoutEngine
- Give `FitLine` (LayoutEngine.cs:703) an optional `int end = -1` bound; the loop runs to `end` or `text.Length`. Existing callers are unchanged.
- Add `BuildTableRowLayout(blockIndex, text, parsed, map, colWidths)` next to FitLine:
  - For each cell, repeatedly call `FitLine(text, pos, colWidth - 2*pad + 0.01, parsed, map, 0, null, e)`.
  - The 0.01 tolerance keeps the widest cell at natural width from wrapping on float error.
  - Trailing spaces hang, as they do in paragraphs.
- In the table branch (:580-588), set `TableLayout` and, when `LineCount > 1`, `OverrideHeight`. Only rows of tables that had to shrink get a layout. A table that fits keeps `TableLayout = null` (see step 10).

### 4. One cell-line primitive, used by drawing and positioning: TableRenderer
Add private `BuildCellLine(blockText, parsed, map, column, colWidths, ls, le)`, which returns `(FormattedText, AlignX, double[] Stops)`:
- **Text and styling:** display string of `[ls,le)`, bold typeface for the header, and `ApplyInlineStylesForCell` (which already works on sub-ranges).
- **Alignment:** from this sub-line's `ft.Width`, so centre/right alignment applies per sub-line.
- **Caret stops:** from `RenderingContext.BuildCaretStops` (:692, change it to `internal static`). If that gives none, fall back to `BuildHighlightGeometry`.

Drawing, caret X, hit-testing and spans all go through this one primitive. It replaces:
- the advance-sum hit test (:355-411)
- the `MeasureRangeWidth` colour backgrounds (:251-252)

With one primitive the caret, clicks and drawn glyphs cannot drift apart.

### 5. Drawing: `DrawTableRow` (TableRenderer.cs:193-267)
- **Clip:** one per cell, `colWidth × LineCount*lineH`.
- **Text:** each sub-line k is drawn at `y + k*lineH`, aligned to the top of the cell.
- **Colour-span backgrounds:** intersect each span with each sub-line, and take the X range from that line's FormattedText highlight geometry.

### 6. Position and hit-test APIs
In TableRenderer, forwarded through `ITableServices` (IDocsCanvasServices.cs:119-125), `DocsCanvas.IDocsCanvasServices.cs:108-111` and `DocsCanvas.VisualMode.cs:109-113`:
- **`PositionInTableRow(vl, parsed, colWidths, offset)`** → `TableCaretPos(X, Column, SubLine)`. Replaces `CursorXInTableRow`.
- **`HitTestInTableRow(vl, parsed, colWidths, x, localY)`:**
  - Pick the column by X, and the sub-line as `clamp(floor(localY/baseH))`.
  - Delegate to `HitTestTableCellLine`.
- **`HitTestTableCellLine(vl, parsed, colWidths, column, subLine, x)`:**
  - Pick the offset at the midpoint between caret stops.
  - On a sub-line that is not the last one, clamp to that line's last visible character. That way a caret past the end of line k stays on k and doesn't jump to the start of k+1. Without the clamp, Up would appear stuck.
- **`RangeSpansInTableRow(vl, parsed, colWidths, start, end, List<LineSpan>)`:**
  - `LineSpan(X1, X2, SubLine, RowLineCount, Continues)`, one for every sub-line the range crosses, in every cell it overlaps.
  - Fixes as a side effect: a fully selected row currently paints only a 4px sliver, because offsets 0 and len sit on the pipes.

In CursorNavigationEngine and `INavigationServices`:
- **`PositionInVisualLine(vli, offset)`** → `(X, Top, Height)`.
  - Table row: `(X, SubLine*baseH, baseH)`.
  - Any other line: `(XInVisualLine, 0, effectiveH)`.
- **`CaretBox(vli)`**: the same box, for the caret's own offset.
- **`GetRangeSpans(vli, start, end, list)`**: a line that is not a table gets exactly one span, so its behaviour is unchanged.
- **`XInVisualLine`** keeps its signature; its table branch uses `.X`.
- **`HitTestInVisualLineProper` / `HitTestInVisualLine`** get `double localY = 0`.
- **`HitTestToPosition`** (:439-460) passes `pos.Y + scroll - LineYPositions[vli]`.

### 7. Callers to update
| Caller | Change |
|---|---|
| RenderingContext.cs:841-848, caret | Draw from `CaretBox`: top offset, one sub-line tall |
| RenderingContext.cs:1873-1885, `DrawSelectionForLine` | Loop over the spans. A single-line row keeps `(y, bgH)`. Otherwise `top = Round(k*baseH)`; height runs to `Round((k+1)*baseH)`, or to `bgH` on the last line. Put this in a shared helper on `LineSpan`. |
| FindAndReplaceController.cs:325-330 | Loop over the spans |
| SpellCheckController.cs:240-248 | Loop over the spans; squiggle Y = `lineY + (k+1)*baseH - 2` |
| DocsCanvas.cs:1350-1353, `EnsureCursorVisible` | Scroll to the caret's sub-line box |
| DocsCanvas.Formatting.cs:283-287, link popup | Y = `lineY + Top + Height + 4` |
| LinkHandler.cs:105-111, tooltip | Place under the hovered sub-line |

### 8. Up/Down and PageUp/PageDown: CursorNavigationEngine.cs:674-742
- **`TryMoveWithinTableCell(vli, goalX, dir)`:**
  - Applies when the row has more than one line and `SubLine+dir` is inside the cell.
  - Sets `CursorOffset = HitTestTableCellLine(column, SubLine+dir, goalX)`, so the caret stays in the cell even if the goal X falls outside it.
- **Otherwise, existing path:** `SetCursorFromVisualLine(vli±1, x, localY)` with the new `localY` parameter.
  - Moving down passes `0` (top sub-line).
  - Moving up passes `double.MaxValue`, which clamps to the bottom sub-line of the cell under X.
- **PageUp/PageDown:**
  - Use the caret's Top/Height for `cursorY` and the step, so a tall row doesn't shrink the page.
  - Pass the local Y into `SetCursorFromVisualLine`.
- **Home/End** stay row-wide, as today.

### 9. Minimap and print
- **Minimap:**
  - `MinimapTableCell` (Integrations.cs:13) gets `LineIndex`.
  - `GetMinimapTableRowInfo` emits one entry per sub-line, plus `out int lineCount`.
  - MinimapScrollbar.cs:333-361 uses `subH = lineH/lineCount` for the glyph scale and the Y of each cell entry.
  - `GetMinimapLineImage` (:281) returns null when `vl.TableLayout != null`, so a tall row is not treated as an image line.
- **Print, minimal only** (print is due for a rework, see memory): at Print.cs:384 the header tint uses the paginator's effective `GetLineHeight(i)`. Nothing else in the print path changes, and the VisualMode.cs drawing copies stay as they are.

### 10. Scrolling performance must not regress
Scrolling is already measured closely (`design/Scroll Frame Pacing.md`). **The rule for this feature: a pure scroll does no more UI-thread work than it does today.** Wrapping may make a row cost more to *build*, but it must not add anything to a scroll frame.

What happens per scroll frame today:
- **Layout doesn't run.** `ComputeLayoutCore` only runs when `LayoutDirty` is set: edits, resize, zoom, mode.
- **Arrange runs.** `UpdateContentLayer` (RenderingContext.cs:157) finds the visible range with the binary search in `FirstLineAt`. It builds only lines that are missing: all visible ones, plus up to `PreRenderBudget` = 6 lines of the 120-line margin. Then it sets `ContentScroll.Y`.
- **`OnRender` runs.** It paints the overlay: `DrawTableLines`, spelling squiggles, page breaks and the caret. **These run every painted frame**, so anything they call has to be cheap.

Where this design could add per-frame cost, and how each is handled:

1. **Tables that fit pay nothing.** `ComputeAllTableColumnWidths` records which tables had to shrink (tier 2 or 3). A row in a table that fits gets `TableLayout = null` and no `OverrideHeight`, so every path runs exactly as it does today. Most documents never touch the new code.
2. **The caret needs no FormattedText per frame.** Today `CursorXInTableRow` builds a FormattedText and a highlight geometry on every render while the caret is in a table. `CaretBox` → `BuildCellLine` would add `BuildCaretStops` on top (a DrawingGroup plus a glyph walk), which is dearer. So:
   - Cache the cell-line results (`AlignX` and `Stops` per cell and sub-line) per visual line.
   - Key the cache on `RenderVersion`, and trim it with the same window as `_lineFt`/`_lineStops` (`TrimLineFtCache`, RenderingContext.cs:589).
   - Fill it while `DrawTableRow` builds the line visual, which constructs those FormattedTexts anyway. A later caret, squiggle or span lookup is then an array read.
   - As a result the caret costs less per frame in a table than it does now.
3. **Squiggles use the same cache.** `DrawSpellingErrors` runs every frame, and its table spans read cached stops instead of rebuilding text.
4. **Build cost counts against the pre-render budget.** `PreRenderBudget` counts lines, so a 10-line row counts as one. When `SyncLineVisuals` (:130-135) builds a wrapped row, it takes `max(1, TableLayout.LineCount)` from the budget. Pre-rendering then stays near its current per-frame cost whatever the row height.
5. **No more DrawText calls per row than needed.** Each cell issues one `DrawText` per non-empty sub-line, and each cell gets one clip, not one per sub-line. The line-visual comment (RenderingContext.cs:56-59) notes that DrawText is the cost of a table row. It is paid once when the row is rasterised, not per frame, and a wrapped row's bitmap is the same area as the paragraph lines the text would otherwise fill.
6. **No overlay work over the whole document.** New overlay code only touches lines inside `FirstLineAt(viewTop)..viewBottom`.
   - **Already O(n), left alone:** `DrawTableLines` (TableRenderer.cs:124) scans from line 0 on every frame. Wrapping doesn't make this worse, and fixing it is a separate change. Noted here, not bundled.
7. **Selection changes rebuild only what changed.** `DropLineVisualsForSelectionChange` drops the touched rows, and a tall row costs more to rebuild. That happens on a keypress, not a scroll frame, so it is acceptable. Pure scrolling never changes the selection signature.
8. **Layout work stays in line with paragraphs.** `BuildTableRowLayout` and the width pass run only for shrunk tables, using cached per-character widths (`MeasureCharWidth`), the same cost model as `FitLine` on the same amount of paragraph text. This runs on edit and resize, not on scroll.

**How to check it:**
- **Unit-level guard** (UI test): scroll a canvas holding a wrapped table by setting `SetScrollOffsetDirect` and running arrange and render, with the view kept inside already-built lines. Assert that no line visuals are built and that the cell-line cache for the caret row is hit, not rebuilt. Add a `TestCellLineCacheBuilds` counter.
- **Measured, before and after:** capture on a table-heavy document narrowed so its tables wrap, using the existing harness:
  - `.\capture-scroll.ps1 -Automated -Release -File "<wide-table doc>" -Monitor DISPLAY6 -Size 1200x800`
  - then `analyse-scroll.ps1`.
  - Compare `canvas-onrender` (ScrollDiag), intervals over 1.5× and animation error p50 against the same document and window on the parent commit.
  - **The bar:** no measurable change in `canvas-onrender`, and intervals over 1.5× within the run-to-run spread already recorded in that note (about 1–2%).
  - **Conditions:** quiet machine, `dotnet build-server shutdown` first, Release on both sides. The capture drives the wheel, so ask before running it.

## Tests: new `Tests/RaisinDocs.Tests.UI/TableCellWrapTests.cs`
- **Test hooks:** `TestCursorY`, `TestCaretHeight`, `TestTableRowLineCount(vi)`, `TestTableColumnWidths(block)`, plus list forms of the painted-rect hooks (`TestSelectionRects` / `TestSearchMatchRects`).
- **Canvas setup:** at an explicit width, as in `SelectionHighlightAlignmentTests.MakeCanvasAt` (:187-198).

Cases:
1. **800px:** natural widths, `LineCount == 1` everywhere, Y positions unchanged.
2. **About 400px:** the column widths sum to at most avail, and each row is `LineCount*baseH` tall.
3. **Tier 2:** every column is at least its longest word, and no sub-line starts mid-word.
4. **Tier 3 at about 150px:** floors are respected, and mid-word breaks occur.
5. **Hidden markers:** `**bold**` cells wrap like plain text of the same visible width. Header glyphs stay inside their column.
6. **Alignment:** right- and centre-aligned cells align per sub-line.
7. **Caret:** `TestCursorY` steps by baseH exactly at `LineStarts[k]`, and `TestCaretHeight == baseH`.
8. **Click round-trip:** clicking at the caret's X/Y returns the same offset for every visible offset. A click past the end of a non-last sub-line stays on that line.
9. **Up/Down:**
   - Down moves within the cell, and from the last sub-line to the next row.
   - Up from the row below lands on the bottom sub-line.
   - Up from sub-line 0 goes to the previous row.
   - The goal X is preserved.
10. **Spans:**
    - Shift+Down inside a cell gives one selection rect per sub-line.
    - A search match that crosses a wrap gives two rects.
    - A fully selected row gives rects in every cell.
11. **PageDown** past tall rows moves at least `ActualHeight - 3*baseH`.
12. **Minimap:** row info carries sub-lines and a line count.
13. **Resize:** reflowing from 400 to 800 restores `LineCount == 1`.
14. **Print path:** `TestComputeLayoutAtWidth(narrow)` gives wrapped rows.
15. **Regression:** TableCursorTests, TableEditingTests, SelectionHighlightAlignmentTests and VerticalGoalColumnTests all pass. Call sites of `CursorXInTableRow` in tests get updated to the new API.

## Verification
- `.\build-safe.ps1 -Command build`, then `.\build-safe.ps1 -Command test`.
- **Manual check:** open a markdown file with a 6-column table in the Editor (`dotnet run --project RaisinDocs.Editor/RaisinDocs.Editor.csproj -- file.md`). The table should contain long prose, a long unbroken identifier, `**bold**`/`` `code` ``, a colour span, and right- and centre-aligned columns. The AvalonDock doc from the screenshot works well. Make sure no stale Editor instance is holding the DLL. Then:
  - Narrow the window through all three width tiers.
  - Click on different sub-lines.
  - Use Up/Down and Shift+Up/Down.
  - Select a whole row.
  - Search for a word that crosses a wrap.
  - Check a misspelling on a cell's second line.
  - Check the minimap in the Viewer.
  - Print to PDF and check the header tint and wrapped rows.
- Update `design/RaisinDocs design v01.md:269` to mark multi-line table cells as done.

## Implementation order
1. Steps 1–3 and 5 (layout, widths, drawing) → tests 1–6.
2. Steps 4, 6 and 7, caret/popups only → tests 7–8.
3. Step 8 → tests 9 and 11.
4. Step 7 spans (selection, search, squiggles) → test 10.
5. Step 9 → tests 12 and 14.
6. Step 10: the fitted-table fast path goes in with stage 1, and the cell-line cache with stage 2, so no stage ever runs slower. The weighted pre-render budget and the scroll capture come last, before and after.

Commit per stage, staging named files only; another session also commits to this repo.
