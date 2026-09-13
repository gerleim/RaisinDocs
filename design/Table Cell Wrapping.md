# Wrap table cell text in visual mode

## Context
In visual mode a table wider than the window is cut off on the right. Each header or data row is one `VisualLine` (LayoutEngine.cs:580-588), and its column widths are the widest cell with no cap: `ComputeAllTableColumnWidths(maxWidth)` never reads `maxWidth` (TableRenderer.cs:34-74). `ClipToBounds` hides the overflow. Source mode already wraps these lines.

**Goal:** shrink columns to fit the window and wrap cell text inside its cell; the row grows to hold it. This is "Multi-line table cells" in `design/RaisinDocs design v01.md:269`.

**Decisions already made:**
1. **Column widths use the browser rule.** Each column keeps at least its longest word; the shortfall comes out of columns in proportion to (natural − min).
2. **Long words break mid-word** when even the minimums don't fit, down to a floor of about 3 characters per column.
3. **Up/Down move one wrapped line within the cell**, keeping pixel X. From the cell's first or last line they move to the adjacent row.
4. **Every table row goes through `BuildCellLine`**, fitted or wrapped (step 4). One path for drawing, caret, clicks and spans, at the price of every table's widths and caret positions shifting slightly.
5. **TableRenderer owns the cell-line cache** (step 10.2). The alternative, RenderingContext, would need TableRenderer to depend back on it.

**Out of scope:** `\` hard breaks inside cells, and vertical alignment of shorter cells — cells are top-aligned. Both are in the v01 item and stay open there.

## Core approach
Cells wrap independently, so a row can't be split into VisualLines by offset ranges. The row stays **one VisualLine**, and:
- `OverrideHeight = LineCount × GetLineHeight(kind)` — the hook images use. Y summation, `GetEffectiveLineHeight`, `SnappedLineHeight`, the row background, `DrawTableLines`, `HitTestVisualLine` and the rectangular table selection already honour it.
- A new `TableRowLayout` holds each cell's sub-lines as raw offsets, so every table position query returns an X and a sub-line.

`baseH` below means `GetLineHeight(kind)`, the height of one sub-line.

## Steps

### 1. Layout data: new `RaisinDocs/DocsCanvas/TableRowLayout.cs`
- `TableRowLayout`: a plain `sealed class` nested in `partial class DocsCanvas`, like `ParagraphGroup`, so it compares by reference.
  - Fields: `int[][] LineStarts` (per cell; `[0]` = trimStart), `int[] CellEnds` (trimEnd), `int LineCount` (≥1).
  - An empty cell (trimStart == trimEnd) still has `LineStarts = [trimStart]`.
  - Helpers: `GetLine(cell, k)`; `LineOf(cell, raw)` = last k whose start ≤ raw, or 0 when raw is in the padding before trimStart.
- Add `public TableRowLayout? TableLayout { get; init; }` to `VisualLine` (DocsCanvas.cs:242-248). `null` means every cell is one line, `[trimStart, trimEnd)`.
  - Record-struct equality stays a reference compare, so `GetVisualLineSpacing` and `GetTextStartXForVisualLine` are unaffected.
  - Print gets it for free: the paginator snapshots `_lines` (Print.cs:185).

### 2. Column widths: `TableRenderer.ComputeAllTableColumnWidths`
- **Natural width** per cell: `_rendering.MeasureRangeWidth(text, s, e-s, p.Runs, p.Kind, map)`, which skips hidden characters and gets bold from the header kind. It replaces `MeasureStringWidth` on the display string, which applied raw run offsets to display text; wrapping must measure the same way. This applies to every table, so a table that already fits can come out a little wider or narrower than today.
- **Minimum width:** the widest word, split at `' '` only (FitLine's break point), measured the same way.
- Add `2*_tableCellPadding` to both.
- **Tiers**, with `avail = maxWidth`:
  - **Tier 1:** `avail <= 0 || sumNat <= avail` → natural.
  - **Tier 2:** `sumMin <= avail` → `min + (nat-min)*(avail-sumMin)/(sumNat-sumMin)`.
  - **Tier 3:** `floor = Math.Min(min, 3*charW('0', header) + 2*pad)`. Width is `floor` if `sumFloor >= avail`, else `floor + (min-floor)*(avail-sumFloor)/(sumMin-sumFloor)`. When `sumFloor >= avail` the table still overflows and is clipped, as today.

### 3. Wrapping: LayoutEngine
- Give `FitLine` (LayoutEngine.cs:703) an optional `int end = -1` bound: the loop runs to `end` or `text.Length`, and so does the fall-through return (`end - start`, not `text.Length - start`). Existing callers are unchanged.
- Add `BuildTableRowLayout(blockIndex, text, parsed, map, colWidths)` next to FitLine:
  - Per cell, repeatedly call `FitLine(text, pos, colWidth - 2*pad + 0.01, parsed, map, 0, null, e)`. The 0.01 stops a cell at exactly natural width wrapping on float error.
  - Trailing spaces hang, as in paragraphs.
- In the table branch (:580-588), set `TableLayout`, and `OverrideHeight` when `LineCount > 1`. Only tables that had to shrink get a layout (see step 10).

### 4. One cell-line primitive for drawing and positioning: TableRenderer
Private `BuildCellLine(blockText, parsed, map, column, colWidths, ls, le)` → `(FormattedText, AlignX, double[] Stops)`:
- **Text:** display string of `[ls,le)`, bold typeface for the header, `ApplyInlineStylesForCell` (already works on sub-ranges).
- **Alignment:** from this sub-line's `ft.Width`, so centre/right apply per sub-line.
- **Caret stops:** `RenderingContext.BuildCaretStops` (:692, make it `internal static`), falling back to `BuildHighlightGeometry` when it returns null. Computed on first query, not when the line is drawn (see step 10.2).

Drawing, caret X, hit-testing and spans use it for **every** table row, fitted or wrapped — a fitted row is just one sub-line per cell. It replaces the advance-sum hit test (:355-411) and the `MeasureRangeWidth` colour backgrounds (:251-252). The caret, clicks and glyphs then can't drift apart.

### 5. Drawing: `DrawTableRow` (TableRenderer.cs:193-267)
- **Clip:** one per cell, `colWidth × LineCount*baseH`.
- **Text:** sub-line k at `y + k*baseH`, top-aligned in the cell.
- **Colour-span backgrounds:** intersect each span with each sub-line; X range from that line's highlight geometry.

### 6. Position and hit-test APIs
In TableRenderer, forwarded through `ITableServices` (IDocsCanvasServices.cs:119-125), `DocsCanvas.IDocsCanvasServices.cs:108-111` and `DocsCanvas.VisualMode.cs:109-113`:
- **`PositionInTableRow(vl, parsed, colWidths, offset)`** → `TableCaretPos(X, Column, SubLine)`. Replaces `CursorXInTableRow`.
- **`HitTestInTableRow(vl, parsed, colWidths, x, localY)`:** column by X, sub-line `floor(localY/baseH)`, then `HitTestTableCellLine`. Clamp as a double before converting to `int`: step 8 passes `double.MaxValue`, which converts to `int.MinValue` and would clamp to the top line.
- **`HitTestTableCellLine(vl, parsed, colWidths, column, subLine, x)`:**
  - Clamp `subLine` to **this cell's** line count, not the row's, so a click below a short cell's text lands on its last line.
  - Offset at the midpoint between caret stops.
  - On a non-last sub-line, clamp to that line's last visible character, so a click past the end of line k stays on k instead of landing at the start of k+1 (which would make Up look stuck).
  - This is stricter than paragraphs. `HitTestInVisualLineProper` returns the line end (CursorNavigationEngine.cs:301), which is the next line's start, and the caret shows there; only End reaches the end of a wrapped line, through `CursorAtLineEnd` (:643). Home/End stay row-wide in a table, so after a **mid-word** break (tier 3) the offset after the line's last character can't be reached from that line. It still shows as the start of k+1. Accepted.
- **`RangeSpansInTableRow(vl, parsed, colWidths, start, end, List<LineSpan>)`:** one `LineSpan(X1, X2, SubLine, RowLineCount, Continues)` per sub-line the range crosses, in every cell it overlaps. Side effect: a fully selected row no longer paints a 4px sliver (offsets 0 and len sit on the pipes).

In CursorNavigationEngine and `INavigationServices`:
- **`PositionInVisualLine(vli, offset)`** → `(X, Top, Height)`: `(X, SubLine*baseH, baseH)` for a table row, `(XInVisualLine, 0, effectiveH)` otherwise.
- **`CaretBox(vli)`**: the same, for the caret's offset.
- **`GetRangeSpans(vli, start, end, list)`**: a non-table line gets exactly one span, so it behaves as before.
- **`XInVisualLine`** keeps its signature; its table branch uses `.X`.
- **`HitTestInVisualLineProper` / `HitTestInVisualLine`** get `double localY = 0`.
- **`HitTestToPosition`** (:439-460) passes `pos.Y + scroll - LineYPositions[vli]`.

### 7. Callers to update
| Caller | Change |
|---|---|
| RenderingContext.cs:841-848, caret | Draw from `CaretBox`: top offset, one sub-line tall |
| RenderingContext.cs:1873-1885, `DrawSelectionForLine` | Loop over spans. Single-line row keeps `(y, bgH)`; otherwise top `Round(k*baseH)`, bottom `Round((k+1)*baseH)` or `bgH` on the last line. Shared helper on `LineSpan`. |
| FindAndReplaceController.cs:325-330 | Loop over spans. Highlights draw inside the line visual with `(y, bgH)` like the selection, so top and height come from the same `LineSpan` helper |
| SpellCheckController.cs:240-248 | Loop over spans; squiggle Y = `lineY - effectiveScroll + (k+1)*baseH - 2` |
| DocsCanvas.cs:1350-1353, `EnsureCursorVisible` | Scroll to the caret's sub-line box |
| DocsCanvas.Formatting.cs:283-287, link popup | Y = `lineY + Top + Height + 4` |
| LinkHandler.cs:105-111, tooltip | Under the hovered sub-line |

Every other caller of `XInVisualLine`, `CursorXInVisualLine` and the hit tests was checked and needs no change:
- `DrawInlineColorBackground` returns early for table rows (RenderingContext.cs:1805).
- The joined-line paths (RenderingContext.cs:1928, FindAndReplaceController.cs:391, SpellCheckController.cs:277) never see a table row.
- `HoverImageHandler` (:73) runs in source mode only.
- The `HitTestToPosition` callers (Input.cs:49/87/140, LinkHandler.cs:45/78, SpellCheckController.cs:330) get the local Y inside it.
- `VerticalGoalX` (CursorNavigationEngine.cs:665) takes the goal X from `CursorXInVisualLine`, which is what moving up and down inside a cell needs.
- `TestCursorX` (DocsCanvas.cs:658) returns X only; `TestCursorY` covers the sub-line.
- `TestLineVisibleOffsetXs` (Print.cs:56) still returns X only. That is fine for its existing tests, but it can't tell sub-lines apart.

### 8. Up/Down and PageUp/PageDown: CursorNavigationEngine.cs:674-742
- **`TryMoveWithinTableCell(vli, goalX, dir)`:** when the row has more than one line and `SubLine+dir` is inside the cell, set `CursorOffset = HitTestTableCellLine(column, SubLine+dir, goalX)`. The caret stays in the cell even if goal X is outside it.
- **Otherwise** the existing `SetCursorFromVisualLine(vli±1, x, localY)`: down passes `0` (top sub-line), up passes `double.MaxValue` (bottom sub-line of the cell under X).
- **PageUp/PageDown:** take `cursorY` and the step from the caret's Top/Height, so a tall row doesn't shrink the page; pass local Y to `SetCursorFromVisualLine`.
- **Home/End** stay row-wide.

### 9. Minimap and print
- **Minimap:**
  - `MinimapTableCell` (Integrations.cs:13) gets `LineIndex`.
  - `GetMinimapTableRowInfo` emits one entry per sub-line, plus `out int lineCount`.
  - MinimapScrollbar.cs:333-361 uses `subH = lineH/lineCount` for glyph scale and each entry's Y.
  - `GetMinimapLineImage` (:281) returns null when `vl.TableLayout != null`. This is required, not defensive. When a cell holds an image, the method returns that image with YOffset 0 (Integrations.cs:289-298), and MinimapScrollbar.cs:323-324 then `continue`s, which skips the row's text.
- **Print, minimal only** (print rework is deferred): the header tint at Print.cs:384 uses the paginator's `GetLineHeight(i)`. Nothing else in print changes, including the VisualMode.cs drawing copies. Print reaches `DrawTableRow` through the same forwarder (Print.cs:443) and so prints wrapped rows, but must not touch the cell-line cache (step 10.2). A row taller than a page is clipped at the page edge.

### 10. Scrolling performance must not regress
Scrolling is closely measured (`design/Scroll Frame Pacing.md`). **Rule: a pure scroll does no more UI-thread work than today.** A wrapped row may cost more to *build*, never more per scroll frame.

Per scroll frame today:
- **Layout** doesn't run; `ComputeLayoutCore` needs `LayoutDirty` (edit, resize, zoom, mode).
- **Arrange** runs `UpdateContentLayer` (RenderingContext.cs:157): `FirstLineAt` binary search, builds missing lines (all visible, plus up to `PreRenderBudget` = 6 of the 120-line margin), sets `ContentScroll.Y`.
- **`OnRender`** paints the overlay — `DrawTableLines`, squiggles, page breaks, caret — **every frame**, so all of it must be cheap.

Where cost could creep in:

1. **Tables that fit are no slower.** `ComputeAllTableColumnWidths` records which tables shrank (tier 2/3). Rows of fitted tables get `TableLayout = null` and no `OverrideHeight`, so layout, heights and pre-render cost are unchanged. They do go through `BuildCellLine` like wrapped rows (step 4), and the cache below makes that cheaper per frame than today's `CursorXInTableRow`.
2. **No FormattedText per frame for the caret.** Today `CursorXInTableRow` builds a FormattedText and highlight geometry every render while the caret is in a table; `BuildCaretStops` would add a DrawingGroup and glyph walk on top. So:
   - **Owner:** TableRenderer holds the cache next to `BuildCellLine`. RenderingContext trims it with `TrimLineFtCache` (same window, RenderingContext.cs:589).
   - **Checked on every lookup.** TableRenderer compares the cache's `RenderVersion` and `LayoutVersion` against the current values at each lookup, and resets on a mismatch, not only when `EnsureLineFtCache` runs. That method runs only from arrange, render and `GetLineText`, and a table position query reaches none of them: `XInVisualLine` returns from its table branch (CursorNavigationEngine.cs:148-152) before `LaidOutX`. A query after printing and before the next arrange would otherwise be served the print-width entry. The check is two integer compares.
   - **`RenderVersion` goes on an interface.** Today it exists only on DocsCanvas (DocsCanvas.cs:1149), and TableRenderer takes its dependencies through service interfaces. Expose it on `IRenderingServices`. `LayoutVersion` is already on `ILayoutDataServices` (IDocsCanvasServices.cs:41).
   - **Per visual line:** for each cell and sub-line, the FormattedText, `AlignX`, and `Stops`.
   - **Keyed on `RenderVersion` and `LayoutVersion`.** The print paginator runs `canvas.ComputeLayoutCore(_contentWidth)` over the canvas's own `_visualLines` and column widths, then only sets `_layoutDirty` (Print.cs:183-189); `RenderVersion` does not move. Without `LayoutVersion` in the key, a query before the next arrange would store print-width entries under the screen's `RenderVersion`, and they would outlive the restore. `ComputeLayoutCore` bumps `LayoutVersion` (LayoutEngine.cs:528), so the cost is one rebuild after printing.
   - **Filled by the screen path only.** `DrawTableRow` gets an optional cache-slot argument that RenderingContext.cs:942 passes for line i. Print.cs:443 passes none, so its print widths and its own line indexes never reach the cache, and Print.cs itself is unchanged.
   - **A query miss builds and stores the entry.** Some queries ask about rows that have not been drawn: `EnsureCursorVisible` after a jump, a PageDown landing beyond the pre-render margin, Up/Down into a row not built yet. Queries only come from the screen, so filling on miss is safe given the key above.
   - **Stops are lazy.** Building a row stores the FormattedText and `AlignX`; `BuildCaretStops` runs on the first caret, squiggle or span query for that sub-line, as `_lineStops` does. Row builds stay no dearer than today.
   - **Rebuilds reuse it.** A row redrawn after a selection change takes its FormattedTexts from the cache.
   - Net: the caret in a table gets cheaper than today.
3. **Squiggles** (`DrawSpellingErrors`, every frame) read the same cache.
4. **Build cost counts against the pre-render budget.** When `SyncLineVisuals` (:130-135) builds a wrapped row it spends `max(1, TableLayout.LineCount)` of the budget, keeping pre-render cost flat whatever the row height.
5. **Minimal DrawText calls.** One `DrawText` per non-empty sub-line and one clip per cell. DrawText is a table row's cost (RenderingContext.cs:56-59), but it's paid once at rasterisation, and the bitmap is the same area the text would take as paragraph lines.
6. **No whole-document overlay work.** New overlay code only touches `FirstLineAt(viewTop)..viewBottom`. `DrawTableLines` (TableRenderer.cs:124) already scans from line 0 each frame; wrapping doesn't worsen it, and fixing it is a separate change.
7. **Selection changes** make `DropLineVisualsForSelectionChange` rebuild touched rows, and tall rows cost more — on a keypress, not a scroll frame. Pure scrolling never changes the selection signature.
8. **Layout cost matches paragraphs.** `BuildTableRowLayout` and the width pass run only for shrunk tables, on cached `MeasureCharWidth` widths — the same cost model as `FitLine` over the same text, on edit and resize only.

**How to check it:**
- **UI test guard:** with a wrapped table, scroll via `SetScrollOffsetDirect` and run arrange and render within already-built lines. Assert no line visuals are built and the caret row's cell-line cache is hit, not rebuilt (new `TestCellLineCacheBuilds` counter).
- **Measured before/after** on a table-heavy document narrowed so its tables wrap:
  - `.\capture-scroll.ps1 -Automated -Release -File "<wide-table doc>" -Monitor DISPLAY6 -Size 1200x800`, then `analyse-scroll.ps1`.
  - Compare `canvas-onrender` (ScrollDiag), intervals over 1.5× and animation error p50 against the parent commit, same document and window.
  - **Bar:** no measurable change in `canvas-onrender`; intervals over 1.5× within the recorded run-to-run spread (about 1–2%).
  - **Conditions:** quiet machine, `dotnet build-server shutdown` first, Release both sides. The capture drives the wheel — ask before running it.

### Baseline, before any wrapping code (2026-09-13)
Taken on `c9097f0`, which adds only the measuring. Tables are still clipped here, so every row is one line in both documents.

**Setup:**
- **Instrumentation.** The scroll log now also times `canvas-arrange` (`UpdateContentLayer`, where line visuals are built) and each build, split into `line-build-table` and `line-build`. Before this commit only `canvas-onrender` was timed.
- **Documents.** `design/samples/Wide Tables.md` and `Fitted Tables.md`, generated by `make-table-samples.py`: 80 sections, each with two paragraphs and an 11-row, 4-column table. Wide cells run to about 300 characters; fitted cells to about 20.
- **Benchmark.** `Tests/RaisinDocs.Tests.UI/TableScrollBenchmark.cs`, Release, canvas 1200×800, no window. Run it with `RAISINDOCS_BENCH=1`; it appends to `%LOCALAPPDATA%\RaisinDocs\bench\table-scroll.txt`.
  - It measures UI-thread work only. `BitmapCache` rasterisation and composition happen off that thread and are not in it.
  - The scroll sweep steps 8px per frame through the whole document, one arrange and one overlay render per frame.
  - The working tree held another session's uncommitted edits to `CursorNavigationEngine.cs` and `VisualModeManager.cs`, so the runs report `c9097f0-dirty`.

**Results.** Three runs. Medians repeat within a few percent unless a range is shown. Layout is given as a mean, because its median swings between 36 and 70 ms from run to run while the total holds steady.

| | Wide Tables | Fitted Tables |
|---|---|---|
| visual lines (table rows) | 1604 (880) | 1604 (880) |
| layout, whole document, mean | 59–62 ms | 10 ms |
| draw one table row, median / p95 | 0.388 / 0.58 ms | 0.087 / 0.32 ms |
| draw one other line, median / p95 | 0.022 / 0.07 ms | 0.022 / 0.07 ms |
| scroll frame arrange, median / p95 / max | 0.009 / 0.43 / 7.0 ms | 0.005 / 0.14 / 4.4 ms |
| scroll frame render, median / p95 | 0.063 / 0.079 ms | 0.0095 / 0.015 ms |
| caret X with the caret in a table cell, median | 0.068 ms | 0.061 ms |
| caret X with the caret in a paragraph, median | 0.032 ms | 0.032 ms |

**What stands out:**
- **A wide row is already 4.5× dearer to draw than a fitted one** while clipped. Each cell builds one long `FormattedText` whether or not it is visible.
- **The caret costs about twice as much in a table cell as in a paragraph, every frame.** That is `CursorXInTableRow` building a `FormattedText` per call. Step 10.2's cache is aimed at exactly this.
- **Overlay render is 6.6× dearer for wide tables** at the same line count and content height. That was not expected and is not yet explained: the only code in the overlay that knows about tables is `DrawTableLines`, and it draws the same number of lines for both documents. The wheel capture below shows no such difference in the real app (`canvas-onrender` 0.03 ms for both), so compare this row before and after the change, not against the capture.
- **Arrange p95 tracks row draw cost.** Most frames build nothing; the ones that do build up to 6 lines.

**Wheel capture.** The only measurement that includes rasterisation and composition.
- **Setup:** `capture-scroll.ps1 -Automated -Release -Mode Visual -Monitor DISPLAY1 -Size 1200x800`, on the 280 Hz primary (3.57 ms budget). `dotnet build-server shutdown` ran first, and both `.meta` files read `quiet: yes`, `interfered: none detected`, and `mode: visual requested, ran in visual`.
- **Commits:** taken on `72209e1`. The captures are `scroll-20260913-212703.csv` (wide) and `scroll-20260913-213334.csv` (fitted), in `%LOCALAPPDATA%\RaisinDocs\captures`.
- **A capture before this one was discarded.** Every capture had been opening its file in source mode, and a restored setting would not have fixed that. `11fc557` and `72209e1` now make the capture pass the mode explicitly and record the mode it ran in. An earlier wide run is also left out: it ran beside 16 build processes from another session.

Gesture-level results, three passes each. A wheel "flick" is 1, 3 or 10 notches (0.33–0.70 s); "sustained" is 30 notches (2.2 s):

| | Wide Tables | Fitted Tables |
|---|---|---|
| wheel flicks: frames displayed/s | 259–279 | 248–274 |
| wheel flicks: intervals over 1.5× | 3.3–6.1% | 3.0–10.7% |
| wheel flicks: animation error median / p95 | 0.16–0.29 / 1.1–3.6 ms | 0.20–0.29 / 0.9–6.8 ms |
| wheel sustained: frames displayed/s | 274–278 | 274–276 |
| wheel sustained: intervals over 1.5× | 1.0–2.2% | 1.8–2.3% |
| wheel sustained: animation error median / p95 | 0.20–0.22 / 1.3–1.6 ms | 0.19–0.23 / 1.3–1.8 ms |
| never displayed | 0–2.6% | 0–3.9% |
| **minimap drag: frames displayed/s** | **44–77** | **112–116** |
| **minimap drag: display interval** | **14.25 ms** | **7.15 ms** |
| minimap drag: animation error median | 3.5–6.0 ms | 3.3–3.5 ms |

In-app costs from the same runs, as logged per gesture:

| | Wide Tables | Fitted Tables |
|---|---|---|
| `line-build-table` avg, sustained wheel | 0.61–0.63 ms | 0.39–0.45 ms |
| `line-build` (other lines) avg | 0.15–0.18 ms | 0.16–0.18 ms |
| `canvas-arrange` avg / max, sustained wheel | 0.03–0.10 / 0.9–1.2 ms | 0.02–0.08 / 0.6–5.3 ms |
| `canvas-arrange` avg / max, minimap drag | 6.6–6.8 / 9–14 ms | 2.6–2.8 / 3.5–6.0 ms |
| `canvas-onrender` avg | 0.03 ms | 0.03 ms |
| `minimap-rebuild` avg / max | 4.4–6.7 / 15 ms | 1.5–2.3 / 4.5 ms |

**What this says:**
- **The wheel doesn't care about table width today.** Both documents hold 3.57 ms at one refresh per frame, with the same spread. The flick differences are within run-to-run noise: the fitted run's worst flick is the first gesture of its run, where caches are coldest.
- **The minimap drag already does.** Every drag builds about 1040 line visuals (≈574 table rows), about 26 per move, because a drag jumps a screen at a time. Wide rows cost 0.41 ms each against 0.13 ms fitted, so a move costs 6.7 ms against 2.7 ms. That takes the drag from two refreshes per frame to four, and from 114/s to about 75/s, **before any wrapping**.
- **Wrapping will land mostly on the drag.** A wrapped row is taller, so a screen holds fewer rows, but each row draws more sub-lines. The per-screen DrawText count is what moves, and the drag rebuilds whole screens. Step 10.4's weighted pre-render budget does nothing for this case, because the visible lines are always built in full.
- **The minimap's own rebuild is 3× dearer for wide tables** (4.4–6.7 ms against 1.5–2.3 ms). It renders each cell's full text, even though the canvas clips it.
- **In-process overlay difference doesn't show in the app.** The benchmark measured overlay render as 6.6× dearer for wide tables, but `canvas-onrender` averages 0.03 ms for both in the real app. The benchmark figure is likely an artefact of rendering with no window. It is not a scroll cost.

## Tests: new `Tests/RaisinDocs.Tests.UI/TableCellWrapTests.cs`
- **New hooks:**
  - `TestCursorY`, `TestCaretHeight`.
  - `TestCursorXNoLayout`: `CursorXInVisualLine` without the `ComputeLayout()` that `TestCursorX` runs first (DocsCanvas.cs:656).
  - `TestTableRowLineCount(vi)`, `TestTableColumnWidths(block)`.
  - `TestSelectionRects(vi)` / `TestSearchMatchRects(vi)`, returning every rect a line paints in the brush. The existing `TestSelectionRect` / `TestSearchMatchRect` (Print.cs:39-44) return one, through `FindRectPaintedWith`, so they need a collecting variant of it (RenderingContext.cs:532); they are not a rename.
- **Setup:** explicit width, as in `SelectionHighlightAlignmentTests.MakeCanvasAt` (:187-198).

Cases:
1. **800px:** natural widths, `LineCount == 1` everywhere, Y positions unchanged.
2. **~400px:** column widths sum to ≤ avail; each row is `LineCount*baseH` tall.
3. **Tier 2:** every column ≥ its longest word; no sub-line starts mid-word.
4. **Tier 3, ~150px:** floors respected; mid-word breaks occur.
5. **Hidden markers:** `**bold**` cells wrap like plain text of the same visible width; header glyphs stay inside their column.
6. **Alignment:** right and centre align per sub-line.
7. **Caret:** `TestCursorY` steps by baseH exactly at `LineStarts[k]`; `TestCaretHeight == baseH`.
8. **Click round-trip:** clicking at the caret's X/Y returns the same offset for every visible offset; a click past the end of a non-last sub-line stays on it.
9. **Up/Down:** Down moves within the cell, and from the last sub-line to the next row; Up from the row below lands on the bottom sub-line; Up from sub-line 0 goes to the previous row; goal X is kept.
10. **Spans:** Shift+Down in a cell gives one selection rect per sub-line; a search match across a wrap gives two; a fully selected row gives rects in every cell.
11. **PageDown** past tall rows moves at least `ActualHeight - 3*baseH`.
12. **Minimap:** row info carries sub-lines and a line count.
13. **Resize:** reflowing from 400 to 800 restores `LineCount == 1`.
14. **Print path:** `TestComputeLayoutAtWidth(narrow)` gives wrapped rows.
15. **Regression:** TableCursorTests, TableEditingTests, SelectionHighlightAlignmentTests and VerticalGoalColumnTests pass. No test calls `CursorXInTableRow`, but the comments at TableCursorTests.cs:95 (char-by-char advances, no kerning) and :105 name it and are updated. That file has uncommitted changes from the other session, so the line numbers may move.
16. **Sub-line clamps:** Up from the row below into a row whose cell under X is shorter than the row lands on that cell's last line; a click below a short cell's text does the same.
17. **Print leaves the cache alone:** put the caret in a column whose X moves when the table shrinks.
    - Record `TestCursorX` at screen width.
    - Call `InvalidateRenderCache()` (DocsCanvas.cs:1151). It bumps `RenderVersion` only, with no relayout, so the entry the recording just stored is gone. Without this, the next read finds that screen entry and never takes the miss path this test exists for.
    - `TestComputeLayoutAtWidth(narrow)`, then read `TestCursorXNoLayout`. It must differ from the screen value, which proves the miss path ran against the print lines. `TestCursorX` can't be used here: its `ComputeLayout()` sees `_layoutDirty` and lays out at screen width before querying, so the test would pass with or without `LayoutVersion` in the key.
    - Read `TestCursorX` again. It must match the recorded screen value. Its `ComputeLayout()` bumps `LayoutVersion` but not `RenderVersion`, so without `LayoutVersion` in the key, or without the per-lookup check, it returns the print-width entry and the test fails here.
18. **Empty cell** in a wrapped row: caret, click and Up/Down work on it.

## Verification
- `.\build-safe.ps1 -Command build`, then `.\build-safe.ps1 -Command test`.
- **Manual:** open a file with a 6-column table in the Editor (`dotnet run --project RaisinDocs.Editor/RaisinDocs.Editor.csproj -- file.md`), with long prose, a long unbroken identifier, `**bold**`/`` `code` ``, a colour span, and right- and centre-aligned columns (the AvalonDock doc from the screenshot works). Make sure no stale Editor holds the DLL. Then:
  - Narrow the window through all three tiers.
  - Click on different sub-lines.
  - Up/Down and Shift+Up/Down.
  - Select a whole row.
  - Search for a word that crosses a wrap.
  - Misspell a word on a cell's second line.
  - Check the minimap in the Viewer.
  - Print to PDF; check the header tint and wrapped rows.
- Mark multi-line table cells done in `design/RaisinDocs design v01.md:269`.

## Implementation order
1. Steps 1–3, the text-and-alignment part of step 4 (`BuildCellLine` without stops), and step 5 (layout, widths, drawing) → tests 1–6. Drawing depends on `BuildCellLine`, so it can't wait for stage 2.
2. The rest of step 4 (caret stops), steps 6 and 7, caret/popups only → tests 7–8, 17–18.
3. Step 8 → tests 9, 11 and 16.
4. Step 7 spans (selection, search, squiggles) → test 10.
5. Step 9 → tests 12 and 14.
6. Step 10: the fitted-table fast path lands with stage 1 and the cell-line cache with stage 2, so no stage runs slower. The weighted pre-render budget and the scroll capture come last.

Commit per stage, staging named files only; another session also commits to this repo.
