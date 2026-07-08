# Plan: Indentation Awareness (Iteration 19)

## Context

RaisinDocs currently classifies each block independently — leading whitespace is ignored, and there is no concept of "content column" or continuation. This works for the common case but deviates from CommonMark in several areas where indentation is load-bearing.

This iteration adds indentation awareness to the parser's two-pass pipeline, bringing RaisinDocs into compliance with CommonMark's indentation rules.

## CommonMark Indentation Rules Summary

Indentation matters in five areas of the spec:

### 1. List continuation (§5.2–5.3)

A list item's **content column** = marker width + spaces after marker. Continuation lines indented to this column remain part of the list item.

```
1. Content here      content column = 3 (marker "1." = 2 + space = 3)
   Continuation      3 spaces → same item

10. Content here     content column = 4 (marker "10." = 3 + space = 4)
    Continuation     4 spaces → same item

- Content            content column = 2 (marker "-" = 1 + space = 2)
  Continuation       2 spaces → same item
```

**Lazy continuation**: paragraph continuation lines may omit the indentation entirely:
```
1. First paragraph
still part of item 1 (lazy continuation — no blank line, no new block starter)
```

**Multi-paragraph items**: a blank line followed by an indented paragraph continues the item:
```
1. First paragraph.

   Second paragraph — still item 1 (indented to content column after blank line).

2. Item 2.
```

**What ends continuation**: a blank line followed by non-indented content, or a line that starts a new block-level structure (heading, fence, blockquote marker, list marker, thematic break).

### 2. Indented code blocks (§4.4)

Lines indented 4+ spaces (and not inside a list/container or directly after a paragraph) become indented code. Already planned as iteration 16 — but interacts with list indentation (4 spaces inside a list item with content column 3 means 7 total spaces for code).

### 3. Block-level prefix tolerance (§4.1–4.3)

ATX headings, setext headings, thematic breaks, fenced code openings, blockquote markers, and list markers may be preceded by **0–3 spaces** and are still recognized. Four spaces makes them indented code instead.

Currently RaisinDocs requires these at column 0. This tolerance needs adding.

### 4. Blockquote continuation (§5.1)

Lazy continuation applies to blockquotes too — paragraph text after `> ` may omit the `>` on continuation lines:
```
> First line
still part of the blockquote (lazy continuation)
```

### 5. Nested containers

Lists inside blockquotes, lists inside lists, blockquotes inside lists — all determined by indentation relative to the content column. Deep nesting is uncommon but the spec requires it.

### 6. Tab character handling (§2.2)

Tabs in source text are structurally equivalent to spaces up to the next **tab stop** (every 4 columns, 0-indexed: 0, 4, 8, 12…). The number of spaces a tab represents depends on its column position:

```
Column 0: tab → 4 spaces (advances to column 4)
Column 1: tab → 3 spaces (advances to column 4)
Column 2: tab → 2 spaces (advances to column 4)
Column 3: tab → 1 space  (advances to column 4)
```

This matters for indentation awareness because a tab at the start of a line counts as 4 spaces of indent — enough for indented code, enough for continuation of most list items. A tab after a list marker may partially or fully satisfy the required space.

**Structural vs literal**: tab expansion applies only to structural indentation (determining block kind, continuation, nesting). Inside code blocks and fenced code, tabs are preserved literally and rendered at tab-stop width. This distinction is already natural in the two-pass model: `ClassifyBlock()` and `DetectContinuations()` expand tabs for structural decisions; inline content rendering preserves them.

**Editor convention**: all major markdown editors (VS Code, Obsidian, Typora, MarkText) insert spaces when the user presses Tab, not tab characters. RaisinDocs follows this convention (iteration 18). However, the parser must still correctly handle tab characters in pasted or imported content.

**Implementation**: add a `ExpandTabsForStructure(string line)` helper that returns a column count or expanded string for the leading whitespace portion only. Used by `ClassifyBlock()` and `DetectContinuations()` — no changes to how tabs render in code blocks.

## Design Approach

### ParsedBlock additions

```csharp
public class ParsedBlock
{
    // ... existing fields ...

    // New: indentation context
    public int ContentColumn { get; init; }      // content column for container blocks (lists, blockquotes)
    public int NestingDepth { get; init; }        // 0 = top-level, 1+ = nested inside container
    public bool IsLazyContinuation { get; init; } // true if this block is a lazy continuation line
    public bool IsIndentedContinuation { get; init; } // true if continuation via indentation after blank line
    public int OwnerBlock { get; init; } = -1;    // block index of the list item / blockquote this continues
}
```

### Two-pass detection: DetectContinuations

Add `DetectContinuations()` to `MarkdownParser.Parse()`, called after `DetectTables()`. This pass scans consecutive blocks and reclassifies continuation lines.

**Algorithm**:
```
For each block i:
  If block[i] is a list item (ordered/unordered/task):
    Compute contentColumn = marker width + spaces after marker
    Scan forward from i+1:
      If block[j] is blank: mark as potential gap, continue scanning
      If block[j] starts with contentColumn spaces of indent:
        → reclassify as ListContinuation (indented continuation)
        → strip the leading spaces for inline parsing
      If block[j] is a Paragraph and no blank line precedes it:
        → reclassify as ListContinuation (lazy continuation)
      Otherwise: stop scanning, list item ends

  Similar logic for blockquotes (content column = 2 for "> ")
```

### New BlockKind values

```csharp
ListContinuation,       // continuation line of a list item (lazy or indented)
BlockquoteContinuation, // lazy continuation of a blockquote
```

Alternatively, keep the `Paragraph` kind but set `IsLazyContinuation = true` and `OwnerBlock = <index>`. The choice depends on whether rendering needs to branch on kind or just on the flags. Using flags is more flexible — the continuation line could itself be a heading, code block, etc. inside the list item.

**Recommendation**: Use `OwnerBlock` + flags rather than new BlockKind values. The continuation line's actual kind (paragraph, heading, code) still matters for rendering. The flags tell the renderer "this belongs to the list item at OwnerBlock, indent accordingly."

### Rendering changes

**Visual mode — indentation**:
- Continuation lines get a left indent matching the owner block's content column width (measured in pixels via `MeasureCharWidth`)
- This stacks with the replacement prefix: if the owner is a bullet list item with `"  • "` prefix, the continuation line gets the same left margin

**Visual mode — spacing**:
- No paragraph gap between a list item and its lazy continuation (they're one logical paragraph)
- Paragraph gap between a list item and its indented continuation after a blank line (they're separate paragraphs within the same item)
- No paragraph gap between a list item and its indented continuation without a blank line

**Source mode — no change**: raw text is shown as-is. The indentation is visible and correct in the source.

### 0–3 space prefix tolerance

Update `ClassifyBlock()` to strip up to 3 leading spaces before checking for block markers. Track the stripped count for content column calculations.

```csharp
internal static BlockKind ClassifyBlock(string text, out int leadingSpaces)
{
    leadingSpaces = 0;
    while (leadingSpaces < 3 && leadingSpaces < text.Length && text[leadingSpaces] == ' ')
        leadingSpaces++;
    // classify text.Substring(leadingSpaces) as before
    // if leadingSpaces == 4+, return Paragraph (indented code candidate)
}
```

This is a signature change — callers that don't care about leading spaces use an overload or discard the out parameter.

## Phases

### Phase 1: Content column computation ✅

**Goal**: Compute and store the content column for list items and blockquotes.

- Add `ContentColumn` property to `ParsedBlock`
- For ordered list items: `GetOrderedListPrefixLength()` already returns the right value
- For unordered list items: 2 (`- ` or `* `)
- For task list items: 6 (`- [ ] ` or `- [x] `)
- For blockquotes: 2 (`> `)
- Store on the ParsedBlock during first-pass classification

**Files**: `MarkdownParser.cs`
**Tests**: verify content column values for each list type, multi-digit ordered lists

### Phase 2: Lazy continuation detection ✅

**Goal**: Detect and mark lazy continuation lines (no blank line, no indent, paragraph text continues a list/blockquote).

- Add `DetectContinuations()` two-pass method
- Scan forward from each list item / blockquote
- A Paragraph block immediately following (no blank line between) that doesn't start a new block structure → mark as lazy continuation
- Set `OwnerBlock` and `IsLazyContinuation` on the ParsedBlock
- Stop at: blank lines, headings, fences, list markers, blockquote markers, thematic breaks

**Files**: `MarkdownParser.cs`
**Tests**: `"1. text", "continuation"` → second block has OwnerBlock=0 and IsLazyContinuation=true; `"1. text", "", "not continuation"` → second block is blank, third is independent paragraph; `"1. text", "# heading"` → heading is not continuation

### Phase 3: Indented continuation detection ✅

**Goal**: Detect continuation via indentation after blank lines.

- Extend `DetectContinuations()` to handle: blank line → indented line pattern
- A blank line followed by a line indented to the content column → mark as indented continuation
- Set `OwnerBlock` and `IsIndentedContinuation`
- Multiple blank-line + indented-content cycles continue the same item

**Files**: `MarkdownParser.cs`
**Tests**: `"1. text", "", "   continuation"` (3-space indent for `1. `) → indented continuation; `"10. text", "", "    continuation"` (4-space indent for `10. `) → indented continuation; `"1. text", "", "continuation"` (no indent after blank) → not continuation

### Phase 4: Visual mode rendering — continuation indentation ✅

**Goal**: Render continuation lines with proper indentation in visual mode.

- In `ComputeLayout` / line rendering, check `IsLazyContinuation` or `IsIndentedContinuation`
- Apply left indent matching the owner block's visual content column
- For indented continuations, strip the leading spaces from the display text (they're structural, not content) — add as `HiddenRange` in `BlockVisualMap`

**Files**: `DocsCanvas.cs`, `DocsCanvas.VisualMode.cs`, `BlockVisualMap.cs`

### Phase 5: Visual mode rendering — spacing adjustments ✅

**Goal**: Correct paragraph spacing for continuation lines.

- Lazy continuation: suppress paragraph gap between owner and continuation (they're one paragraph)
- Indented continuation after blank line: show paragraph gap (they're separate paragraphs within the same item, but still indented)
- Tight vs loose lists: if items within a list are separated by blank lines, add paragraph gap between items (loose); otherwise no gap (tight)

**Files**: `DocsCanvas.cs` (layout Y-position computation)

### Phase 6: 0–3 space prefix tolerance

**Goal**: Recognize block markers preceded by up to 3 spaces.

- Update `ClassifyBlock()` to strip 0–3 leading spaces
- `   # heading` → still a heading (3 spaces)
- `    # heading` → indented code, not a heading (4 spaces)
- Same for list markers, blockquote markers, thematic breaks, fence openings
- Careful: 4+ spaces must not match — that's indented code territory

**Files**: `MarkdownParser.cs`
**Tests**: `"   # heading"` → Heading1; `"    # heading"` → Paragraph; `"  - item"` → UnorderedListItem; `"   > quote"` → Blockquote

### Phase 7: Tab character structural expansion ✅

**Goal**: Correctly interpret tab characters as structural indentation per CommonMark §2.2.

- Add `ExpandLeadingTabs(string line)` helper returning the effective column count of leading whitespace (spaces = 1 column each, tabs = advance to next multiple of 4)
- Use in `ClassifyBlock()`: a leading tab counts as 4 spaces → indented code candidate; tab + `#` at column 4 → indented code, not heading
- Use in `DetectContinuations()`: a tab may satisfy the content column indent requirement
- Do **not** expand tabs inside code block content — preserve literally for rendering
- Do **not** insert tab characters from the editor (iteration 18 inserts spaces) — this phase handles tabs in existing/imported content only

**Files**: `MarkdownParser.cs`
**Tests**: `"\t# heading"` → Paragraph (tab = 4 spaces, indented code); `"\ttext"` → indented code candidate (4 spaces); tab after `- ` counts correctly for continuation; mixed tabs and spaces expand correctly

### Phase 8: Source mode — indentation hints (optional)

**Goal**: Subtle visual indicator in source mode showing which lines are continuations.

- Optional: draw a thin vertical line at the content column position for continuation lines
- Or: subtle background tint on continuation lines matching their owner
- This is a polish item, not required for spec compliance

## Implementation Order

```
Phase 1 (Content column)    — data model, unit-testable               ✅
Phase 2 (Lazy continuation) — two-pass detection, unit-testable       ✅
Phase 3 (Indented cont.)    — extends phase 2, unit-testable          ✅
Phase 4 (Visual indent)     — rendering, depends on 2+3              ✅
Phase 5 (Spacing)           — rendering, depends on 4                ✅
Phase 6 (Prefix tolerance)  — parser, independent of 2–5
Phase 7 (Tab expansion)     — parser, independent of 2–5, pairs with phase 6  ✅
Phase 8 (Source hints)       — optional polish
```

Phases 1–3 are pure parser work with no rendering changes — fully testable in isolation.
Phases 6–7 are independent parser work and can be done in parallel with 2–5.

## Scope and Limitations

This iteration covers:
- Single-level list continuation (lazy and indented)
- Single-level blockquote continuation
- 0–3 space prefix tolerance
- Tight/loose list spacing

This iteration does **not** cover:
- **Nested containers** (lists inside lists, lists inside blockquotes) — requires recursive indentation tracking, a follow-up iteration
- **Indented code blocks** — already planned as iteration 16, but the interaction with list content columns should be noted (code inside a list item requires content column + 4 spaces)

## Verification

- **Phases 1–3**: `dotnet test Tests/RaisinDocs.Tests/` — parser unit tests
- **Phases 4–5**: run TestApp, type list items with continuation lines in both modes, verify indentation and spacing
- **Phase 6**: `dotnet test` — parser tests for prefix-tolerant classification
- **End-to-end**: compare rendering of CommonMark spec examples §5.2–5.3 against expected output; test ordered and unordered list continuation, multi-paragraph items, tight/loose lists, blockquote lazy continuation
