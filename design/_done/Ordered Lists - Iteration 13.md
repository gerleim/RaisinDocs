# Plan: Ordered List Items (Iteration 13)

## Context

All iterations through 12 (Comment-based color extensions) are complete. Ordered lists are a core CommonMark feature that has not been implemented — `1. item` syntax is currently rendered as a plain paragraph.

Ordered list items follow the same single-block model as unordered list items — each item is one Document block (StringBuilder). The parser detects the numbered prefix and assigns a new BlockKind. Visual mode replaces the raw prefix with a styled number, aligned with the bullet list indentation.

## CommonMark Spec Summary (§5.3)

An ordered list marker is a sequence of 1–9 digits (`0-9`) followed by `.` or `)`, then at least one space. Valid examples:

```markdown
1. First item
2. Second item
3. Third item

1) Alternative marker style
2) Also valid
```

Key rules:
- **1–9 digits max** (prevents browser overflow)
- Both `.` and `)` delimiters are valid
- The first item's number sets the list start value; subsequent items are sequential regardless of typed number
- Leading zeros are allowed (`003.` → start at 3)
- An ordered list can only interrupt a paragraph if it starts with `1`

Since RaisinDocs treats each block independently (no cross-block list grouping), we show the literal typed number rather than auto-renumbering. This matches the raw-text-is-truth philosophy — the user controls what they see.

## Phase 1: Parser — Ordered List Detection

**Goal**: Detect ordered list syntax, assign a new BlockKind value.

**New type** in `MarkdownParser.cs`:
- `BlockKind.OrderedListItem`

**Detection logic** in `ClassifyBlock()`:
- After existing checks, match `^\d{1,9}[.)]\s` — one to nine digits, followed by `.` or `)`, followed by a space
- Return `BlockKind.OrderedListItem`
- The prefix length is variable (2–11 characters: 1–9 digits + delimiter + space)

**Prefix extraction**: Add a static helper `GetOrderedListPrefixLength(string text)` that returns the prefix length (digits + delimiter + space), or 0 if not an ordered list. Used by ClassifyBlock, BlockVisualMap, and syntax dimming.

**Inline parsing**: Runs on full block text as normal. The number prefix characters are just text — inline styles start after them.

**Files**: `MarkdownParser.cs`
**Tests**: `1. item` → OrderedListItem, `1) item` → OrderedListItem, `123. item` (multi-digit), `999999999. item` (9 digits — max valid), `1234567890. item` (10 digits — paragraph, too many), `0. item` (valid, starts at 0), `1.no space` → Paragraph, inside fenced code → FencedCodeLine, leading spaces → Paragraph (no indented list items in our flat model)

## Phase 2: Source Mode Rendering

**Goal**: Show raw syntax with dimmed number prefix.

- `GetBlockFontSize()` / `GetBlockBaseTypeface()`: add `BlockKind.OrderedListItem` case returning paragraph defaults (same as `UnorderedListItem`)
- `ApplySyntaxDimming()`: dim the prefix (digits + delimiter + space) using `_palette.Syntax`. Use `GetOrderedListPrefixLength()` to determine how many characters to dim.

**Files**: `DocsCanvas.cs`

## Phase 3: BlockVisualMap — Hide Number Prefix

**Goal**: In visual mode, hide the raw markdown prefix and replace with a styled number.

- Hide first N characters (the `\d+[.)]\s` prefix) as `HiddenRange(0, N)`
- `ReplacementPrefix`: the typed number with consistent formatting — `"  1.  "` (two-space indent + number + delimiter + two spaces), keeping the user's typed number and delimiter. Built by `OrderedListPrefix`, which a continuation block also uses to measure the owner it aligns to; the two were separate builders that had drifted by a space, so a continuation under `10.` sat a space left of the text above it.
- Cursor navigation skips the hidden prefix via existing `SkipCursorOverHiddenRanges()`

**Files**: `BlockVisualMap.cs`
**Tests**: prefix hidden for `1. `, `12. `, `1) `, RawToVisual/VisualToRaw across prefix boundary, variable-length prefix (single vs multi-digit)

## Phase 4: Visual Mode Rendering ✅ — revised 2026-09-05

**Goal**: Render the number prefix with consistent styling.

The original plan for this phase asked for the number "right-aligned to a fixed indent width,
matching bullet indent". That was right about the alignment and silent about what should happen
when a number outgrows the column. What shipped keeps the first part and answers the second.

### One shared marker column

Bullets, checkboxes and ordered numbers share **one** column, so every list kind starts its text
at the same X:

```
columnWidth   = max(width("☑"), width("99."))
markerRightX  = max(padding + nesting + lead + columnWidth,   // nominal
                    leftLimit + numberWidth)                  // ordered, when it will not fit
contentStartX = markerRightX + gap
```

where `leftLimit = padding + nesting`, `lead` is two spaces, and `gap` is 10px (widened from 4,
which was narrower than a space character and read as kerning rather than separation).

- Numbers are **right-aligned** to `markerRightX`, so `1.`, `9.` and `10.` put their delimiter on
  one X and the text after them does not move with the digit count.
- Bullets and checkboxes stay **centred** in the column, drawn on `MarkerStartX`.
- The checkbox glyph (21.970px) is wider than `99.` (20.723px), so it sets the column and sharing
  it costs bullets and checkboxes nothing — they did not move. Ordered items moved *left* onto the
  column bullets already used; their text used to start at 52.391 for `1.` and 61.016 for `12.`.

### Overflow

A number wider than the column would have to begin left of the margin. Instead its left edge
clamps at `leftLimit`, and `markerRightX` — carrying the text with it — moves right by the excess,
`gap` unchanged:

```
   9.       text column unmoved
  99.       text column unmoved
 999.       text column unmoved     (a number may reach back over the lead: 30.74px of room)
9999.       shifts right
101010.     shifts right
```

Decided **per item**, never per list. No block needs its siblings' widths, which keeps this
consistent with the single-block model described in Context above.

### Why the column has to be fixed

While it varied with the digit count, `WrapSegment` measured a list item's first line against the
prefix width even though the line started at `ContentStartX`. The difference — 12.8px for a
two-digit number — granted the line width it did not have, and anything past the 10px right
padding was cut off mid-glyph by `DocsCanvas.ClipToBounds`, with no horizontal scrolling to
recover it. A fixed column removes the discrepancy at its source.

**Number colour**: `_palette.Syntax`, matching dimmed markdown syntax rather than the foreground
the original plan suggested.

**Files**: `LayoutEngine.cs` (`ComputeListItemSpacing` holds the whole rule), `RenderingContext.cs`
and `DocsCanvas.VisualMode.cs` (`DrawOrderedListNumber`, one copy each — the print path keeps its
own until its deferred rework), `BlockVisualMap.cs` (`OrderedListPrefix`)

**Tests**: `Tests/RaisinDocs.Tests.UI/ListMarkerColumnTests.cs`

## Phase 5: Enter Key Continuation

**Goal**: Pressing Enter on an ordered list item auto-inserts the next number.

- In the Enter key handler, detect if the current block is `OrderedListItem`
- Extract the current number and delimiter, increment the number, insert `N+1. ` (or `N+1) `) as the prefix of the new block
- If the current item is empty (just the prefix, no content), remove the prefix instead (same behavior as bullet lists — pressing Enter on an empty list item exits the list)

**Files**: `DocsCanvas.cs` (keyboard handling)

## Phase 6: Toolbar and GetBlockPrefix

**Goal**: Toolbar button to insert/toggle ordered list, and teach `GetBlockPrefix` about the new prefix.

- `GetBlockPrefix()` in `Document.cs`: recognize `\d+[.)]\s` patterns as a prefix (return the matched prefix string). Must be checked before the unordered list check since `* ` is simpler.
- Add `ToggleOrderedList()` in `DocsCanvas.cs`: calls `ToggleBlockPrefixForSelection("1. ")`
- Add `_orderedListButton` in `DocsFormattingBar.cs` wired to `ToggleOrderedList()`
- Button state: checked when cursor is on an `OrderedListItem` block
- Icon: numbered list path geometry (lines with `1`, `2`, `3` or similar)
- Place the button next to the existing bullet list button

**Files**: `DocsFormattingBar.cs`, `DocsCanvas.cs`, `Document.cs`, `Themes/Generic.xaml`

## Implementation Order

```
Phase 1 (Parser)        — fully unit-testable
Phase 2 (Source)        — testable visually, depends on 1
Phase 3 (VisualMap)     — unit-testable, depends on 1
Phase 4 (Visual)        — depends on 3
Phase 5 (Enter)         — depends on 1, quality-of-life
Phase 6 (Toolbar)       — depends on all above
```

## Verification

- **Phase 1**: `dotnet test Tests/RaisinDocs.Tests/` — parser unit tests
- **Phase 2**: run TestApp, type `1. item` in source mode, verify dimming
- **Phase 3**: `dotnet test Tests/RaisinDocs.Tests/` — BlockVisualMap tests
- **Phase 4**: run TestApp, switch to Visual mode, verify number rendering and indent alignment with bullet lists
- **Phase 5**: run TestApp, press Enter at end of an ordered list item, verify next number auto-inserted; press Enter on empty item, verify prefix removed
- **Phase 6**: run TestApp, verify toolbar button inserts `1. ` prefix and toggles on/off
- **End-to-end**: ordered lists mixed with bullet lists, inline styles in list text, undo/redo, multi-digit numbers, both `.` and `)` delimiters
