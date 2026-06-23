# Plan: GFM Table Support (Iteration 7)

## Context

All iterations through 6b are complete. Tables are listed as a Future item in the design doc. CommonMark 0.31.2 doesn't include tables, but GFM tables are ubiquitous in practice. The implementation follows the same multi-block pattern as fenced code blocks: parser detects table context across consecutive Document blocks, assigns new BlockKind values, and rendering/editing branch on those kinds.

Tables are a multi-block construct where each row is a separate Document block (StringBuilder). The parser must detect header + separator + data rows as a group, compute column metadata, and pass it through to layout and rendering.

## Phase 1: Parser — Table Detection and Metadata

**Goal**: Detect GFM table syntax, assign new BlockKind values, emit cell boundary and alignment metadata.

**New types** in `MarkdownParser.cs`:
- `BlockKind.TableHeaderRow`, `BlockKind.TableSeparatorRow`, `BlockKind.TableDataRow`
- `enum ColumnAlignment { Left, Center, Right }`
- `record struct TableCellInfo(int Start, int Length)` — cell position within raw block text
- `class TableRowInfo { IReadOnlyList<TableCellInfo> Cells }` — per-row cell boundaries
- `class TableInfo { int ColumnCount, IReadOnlyList<ColumnAlignment> Alignments }` — shared across all rows of one table
- `ParsedBlock` gets: `TableRowInfo? TableRow`, `TableInfo? Table`, `bool IsTableSeparator`

**Detection algorithm** — two-pass in `Parse()`:
1. Pass 1: existing logic (fenced code, block classification, inlines) — unchanged
2. Pass 2: scan result for table sequences. For each Paragraph block containing `|`, check if the next block is a valid separator row (`|:?-+:?|`). If yes, reclassify header + separator + subsequent pipe-rows as table blocks. Blocks inside fenced code are never reclassified.

**Cell parsing helpers**:
- `ParseTableRow(text)` — split on unescaped `|`, trim leading/trailing pipes, record each cell's Start/Length
- `ParseSeparatorRow(text)` — validate pattern, extract ColumnAlignment from colon positions

**Inline parsing**: runs on full row text as normal (pipes don't interfere with emphasis). Cell boundaries inform layout, not parsing.

**Files**: `MarkdownParser.cs`
**Tests**: basic table, alignment detection (left/center/right), inline styles in cells, escaped `\|`, table inside fenced code (not detected), table followed by paragraph, minimum 1-column table, missing leading/trailing pipes, cell Start/Length correctness

## Phase 2: Source Mode Rendering

**Goal**: Once parser produces table BlockKinds, source mode shows raw syntax with dimmed pipes and separator.

- `GetBlockFontSize()` / `GetBlockBaseTypeface()`: add cases returning paragraph defaults
- `ApplySyntaxDimming()`: dim all unescaped `|` in header/data rows; dim entire separator row
- Inline styles within cells still render (bold text in a cell appears bold)

**Files**: `DocsCanvas.cs` (switch statements in font selection, ApplySyntaxDimming)

## Phase 3: Layout — Column Width Computation

**Goal**: Compute column widths across all rows, lay out table rows as single non-wrapping visual lines.

- `ComputeTableColumnWidths()`: iterate all rows of a table, measure cell text widths, take max per column, add cell padding (~12px each side)
- In `ComputeLayoutCore()`: table rows skip `WrapSegment()`, produce exactly one VisualLine each. Separator rows skipped in Visual mode (like fence delimiters).
- Store column widths on the shared `TableInfo` object (computed lazily during layout)
- Wide tables that exceed viewport: clip (horizontal scroll is a future enhancement)

**Files**: `DocsCanvas.cs` (layout section)

## Phase 4: BlockVisualMap — Hide Pipe Syntax

**Goal**: In Visual mode, pipe delimiters become hidden ranges for cursor navigation.

- Separator rows: `IsTableSeparator` flag, skipped in layout (like `IsFenceDelimiter`)
- Header/data rows: hide leading pipe, trailing pipe, and interior pipes as `HiddenRange`
- `EnsureCursorOnVisibleBlock()`: extend to skip separator rows

Note: even though Visual mode uses a dedicated table rendering path (not `BuildDisplayString`), the hidden ranges are still needed for cursor skip logic.

**Files**: `BlockVisualMap.cs`, `DocsCanvas.VisualMode.cs`
**Tests**: pipes hidden, RawToVisual/VisualToRaw across cell boundaries, separator row skipped

## Phase 5: Visual Mode Rendering — Table Grid

**Goal**: Render tables as proper grids with borders, cell padding, header styling.

**Table backgrounds** (analogous to `DrawCodeBlockBackgrounds()`):
- Light background for entire table area
- Distinct header row background
- Horizontal rules between rows, vertical rules between columns, outer border

**Cell content rendering**:
- For each table VisualLine, iterate cells from `TableRowInfo`
- Extract cell text, create `FormattedText`, apply inline styles for runs overlapping cell range
- Align text per `ColumnAlignment` (left/center/right)
- Clip to cell bounds with `dc.PushClip()`
- Header row text rendered bold

**New palette entries**: `TableBorder` (Pen), `TableHeaderBackground` (Brush), `TableBackground` (Brush)

**Files**: `DocsCanvas.cs` (OnRender, palette)

## Phase 6: Cursor and Editing

**Goal**: Cursor navigates correctly within table cells; Tab moves between cells.

- **Pipe skipping**: existing `SkipCursorOverHiddenRanges()` handles this via Phase 4 hidden ranges
- **Tab**: in table row, move to next cell; at last cell, move to first cell of next row; Shift+Tab reverses
- **Enter**: insert new data row pre-populated with correct number of empty cells (`| | | |`)
- **Backspace at row start / Delete at row end**: prevent merging table rows (like fence delimiter boundary protection)
- **Separator skipping**: up/down arrow skips separator rows (extend `EnsureCursorOnVisibleBlock()`)

**Files**: `DocsCanvas.cs` (OnKeyDown), `DocsCanvas.VisualMode.cs`
**Tests**: Tab/Shift+Tab navigation, arrow keys skip pipes, separator row skipped, boundary protection

## Phase 7: Toolbar and Insert Table

**Goal**: Toolbar button to insert a table template.

- `InsertTable(columns, rows)` on DocsCanvas: inserts header + separator + data row blocks
- Toolbar button with grid icon, click inserts 3x2 table
- Button state: checked when cursor inside table

**Files**: `DocsCanvas.cs`, `DocsFormattingBar.cs`, `Generic.xaml`

## Implementation Order

```
Phase 1 (Parser)     ── fully unit-testable
Phase 2 (Source)     ── testable visually, depends on 1
Phase 3 (Layout)     ── depends on 1
Phase 4 (VisualMap)  ── depends on 1
Phase 5 (Visual)     ── depends on 3, 4
Phase 6 (Cursor)     ── depends on 4
Phase 7 (Toolbar)    ── depends on all above
```

## Verification

- **Phase 1**: `dotnet test Tests/RaisinDocs.Tests/` — parser unit tests
- **Phase 2-3**: run TestApp, type a table in source mode, verify dimming and layout
- **Phase 4**: `dotnet test Tests/RaisinDocs.Tests/` — BlockVisualMap tests
- **Phase 5**: run TestApp, switch to Visual mode, verify grid rendering
- **Phase 6**: `dotnet test Tests/RaisinDocs.Tests.UI/` — cursor navigation tests
- **End-to-end**: test markdown with tables of various sizes, alignments, inline styles in cells, tables adjacent to other block types
