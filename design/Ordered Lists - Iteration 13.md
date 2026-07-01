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
- `ReplacementPrefix`: use the typed number with consistent formatting — e.g. `"  1. "` (two-space indent + number + dot + space) to align with the bullet list's `"  • "` indent. Keep the user's typed number and delimiter.
- Cursor navigation skips the hidden prefix via existing `SkipCursorOverHiddenRanges()`

**Files**: `BlockVisualMap.cs`
**Tests**: prefix hidden for `1. `, `12. `, `1) `, RawToVisual/VisualToRaw across prefix boundary, variable-length prefix (single vs multi-digit)

## Phase 4: Visual Mode Rendering

**Goal**: Render the number prefix with consistent styling.

- The replacement prefix drawing path already handles `UnorderedListItem` (draws `•`). Add a parallel case for `OrderedListItem` that draws the number + delimiter right-aligned to a fixed indent width, matching bullet indent.
- Number color: use `_palette.Foreground` (or a subtle accent — match whatever bullet uses)
- Right-align numbers so single and multi-digit numbers line up: `1.`, `2.`, `10.` all have their `.` at the same X position

**Files**: `DocsCanvas.VisualMode.cs`, `DocsCanvas.cs`

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
