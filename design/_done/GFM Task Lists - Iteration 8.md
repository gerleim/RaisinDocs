# Plan: GFM Task List Items (Iteration 8)

## Context

All iterations through 7 (Tables) are complete. Task list items are the next GFM extension on the roadmap. They are a specialization of unordered list items where the first content is a checkbox marker: `- [ ] unchecked` or `- [x] checked`.

Task list items follow the same block model as regular list items — each is a single Document block (StringBuilder). The parser detects the checkbox prefix and assigns a new BlockKind. The checkbox is rendered as an interactive element in visual mode that toggles on click.

## GFM Spec Summary

A task list item is a list item whose first content (after the list marker) is `[ ]` (unchecked) or `[x]`/`[X]` (checked), followed by a space. Valid examples:

```markdown
- [ ] unchecked task
- [x] checked task
- [X] also checked
* [ ] star marker works too
```

The full raw prefix is 6 characters: `- [ ] ` or `- [x] ` (marker + space + bracket + check + bracket + space).

## Phase 1: Parser — Task List Detection

**Goal**: Detect task list syntax, assign new BlockKind values.

**New types** in `MarkdownParser.cs`:
- `BlockKind.TaskListItemUnchecked` — `- [ ] ` prefix
- `BlockKind.TaskListItemChecked` — `- [x] ` or `- [X] ` prefix

**Detection logic** in `ClassifyBlock()`:
- After matching `- ` or `* ` as a list marker, check if the remainder starts with `[ ] ` or `[x] ` / `[X] `
- If yes, return `TaskListItemUnchecked` or `TaskListItemChecked` instead of `UnorderedListItem`
- The prefix is 6 characters total (e.g. `- [x] `)

**Inline parsing**: Runs on full block text as normal. The checkbox prefix characters are just text — inline styles start after them.

**Files**: `MarkdownParser.cs`
**Tests**: basic unchecked, basic checked, `[X]` uppercase, star marker `* [ ]`, text after checkbox has inline styles, not a task list without trailing space (`- [x]word`), not a task list inside fenced code

## Phase 2: Source Mode Rendering

**Goal**: Show raw syntax with dimmed checkbox prefix.

- `GetBlockFontSize()` / `GetBlockBaseTypeface()`: add cases returning paragraph defaults (same as `UnorderedListItem`)
- `ApplySyntaxDimming()`: dim the full 6-character prefix (`- [ ] ` or `- [x] `) using `_palette.Syntax`

**Files**: `DocsCanvas.cs`

## Phase 3: BlockVisualMap — Hide Checkbox Syntax

**Goal**: In visual mode, hide the raw prefix and replace with a visual checkbox.

- Hide first 6 characters (`- [ ] ` or `- [x] `) as `HiddenRange(0, 6)`
- `ReplacementPrefix`: a short string used for measurement — the actual checkbox is drawn graphically, but the prefix reserves horizontal space (like `"  • "` does for bullets, but with checkbox width)
- Cursor navigation skips the hidden prefix via existing `SkipCursorOverHiddenRanges()`

**Files**: `BlockVisualMap.cs`
**Tests**: prefix hidden, RawToVisual/VisualToRaw across checkbox boundary, checked vs unchecked both hide 6 chars

## Phase 4: Visual Mode Rendering — Checkbox

**Goal**: Render a checkbox glyph in the prefix area.

**Checkbox rendering** (in the replacement prefix drawing path):
- Unchecked: draw an empty square (rounded rect outline)
- Checked: draw a filled square with a checkmark path inside
- Use `_palette.Foreground` for the outline, `_palette.AccentBrush` or similar for checked fill
- Checked items: render text with strikethrough and/or dimmed color (common convention)
- Size: match line height, e.g. 12–14px square

**New palette entries**: `CheckboxBorder` (Pen), `CheckboxCheckedFill` (Brush)

**Files**: `DocsCanvas.VisualMode.cs`, `DocsCanvas.cs` (palette)

## Phase 5: Click to Toggle

**Goal**: Clicking the checkbox area toggles checked/unchecked state.

**Hit testing**:
- In `OnMouseDown` (or `OnMouseLeftButtonDown`), detect if the click position falls within the checkbox area of a task list item
- The checkbox area is the replacement prefix region at the start of the first visual line of the block
- Check: is the block a `TaskListItemUnchecked` or `TaskListItemChecked`? Is the click X within the checkbox width?

**Toggle logic**:
- Modify the raw block text: replace `[ ]` with `[x]` or `[x]`/`[X]` with `[ ]` at offset 2–4
- Use `Document.ReplaceTextAt()` or direct StringBuilder manipulation
- Push an undo boundary before the toggle
- Invalidate layout after toggle

**Files**: `DocsCanvas.cs` (mouse handling), `Document.cs` (if a helper method is needed)
**Tests**: click toggles unchecked→checked, click toggles checked→unchecked, toggle pushes undo

## Phase 6: Toolbar

**Goal**: Toolbar button to insert/toggle task list item.

- Add `_taskListButton` in `DocsFormattingBar.cs` wired to a new `ToggleTaskList()` method
- `ToggleTaskList()`: uses `Document.ToggleBlockPrefix()` with `"- [ ] "` prefix
- `GetBlockPrefix()` in `Document.cs`: recognize `- [ ] ` and `- [x] ` as prefixes (currently only knows `- ` and `* `)
- Button state: checked when cursor is on a `TaskListItemUnchecked` or `TaskListItemChecked` block
- Icon: checkbox path geometry (consistent with other toolbar icons)

**Files**: `DocsFormattingBar.cs`, `DocsCanvas.cs`, `Document.cs`, `Themes/Generic.xaml`

## Implementation Order

```
Phase 1 (Parser)      — fully unit-testable
Phase 2 (Source)      — testable visually, depends on 1
Phase 3 (VisualMap)   — unit-testable, depends on 1
Phase 4 (Visual)      — depends on 3
Phase 5 (Click)       — depends on 4
Phase 6 (Toolbar)     — depends on all above
```

## Verification

- **Phase 1**: `dotnet test Tests/RaisinDocs.Tests/` — parser unit tests
- **Phase 2**: run TestApp, type task list items in source mode, verify dimming
- **Phase 3**: `dotnet test Tests/RaisinDocs.Tests/` — BlockVisualMap tests
- **Phase 4-5**: run TestApp, switch to Visual mode, verify checkbox rendering and click toggle
- **Phase 6**: run TestApp, verify toolbar button inserts and toggles task list
- **End-to-end**: test task lists mixed with regular list items, inline styles in task text, undo/redo of checkbox toggle
