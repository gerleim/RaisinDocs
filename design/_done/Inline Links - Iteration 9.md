# Plan: Inline Links (Iteration 9)

## Context

All iterations through 8 (Task List Items) are complete. Links are a core CommonMark feature that has not been implemented yet — `[text](url)` syntax is currently rendered as plain text with no styling, hidden ranges, or interactivity.

Links share the same bracket/paren structure as images (`![alt](url)`) and can reuse the existing `FindMatchingBracket` and `ParseDestinationAndTitle` helpers. The key difference is that links display their text inline (styled as a link) rather than rendering an embedded object.

## CommonMark Spec Summary

An inline link consists of a link text (sequence of inlines in square brackets) followed by a link destination and optional title in parentheses:

```markdown
[link text](https://example.com)
[link text](https://example.com "Title")
[link with **bold**](url)
```

Important parsing rules:
- Link text is delimited by `[...]` — may contain inline formatting but not other links
- `!` before `[` makes it an image, not a link — images take priority
- Link destination can be a bare URL with balanced parens, or angle-bracketed `<url>`
- Optional title in double quotes, single quotes, or parentheses
- Links cannot nest: `[foo [bar](url1)](url2)` — the inner link wins

The existing `MarkImages` method already handles all the bracket/paren parsing. `MarkLinks` will follow the identical pattern, minus the `!` prefix check.

## Phase 1: Parser — Link Detection

**Goal**: Detect `[text](url)` syntax, assign a new InlineStyle and record link metadata.

**New types** in `MarkdownParser.cs`:
- `InlineStyle.Link` — marks the full `[text](url)` span
- `InlineLink` record — `(int Start, int Length, string Text, string Url, string? Title)`

**Parsing** — new `MarkLinks()` method added to the `ParseInlines` pipeline:

```
MarkCodeSpans(text, styles);       // existing
images = MarkImages(text, styles); // existing — images consume ![
links = MarkLinks(text, styles);   // NEW — consumes [ that aren't preceded by !
MarkStrikethrough(text, styles);   // existing
MarkEmphasis(text, styles);        // existing
```

**Detection logic**:
- Scan for `[` where `styles[i] == InlineStyle.Normal`
- Skip if preceded by `!` (that's an image, already consumed by `MarkImages`)
- Use `FindMatchingBracket` to find `]`, then check for `(`
- Use `ParseDestinationAndTitle` to parse destination and optional title
- Mark the entire `[text](url)` range as `InlineStyle.Link`
- Store `InlineLink` with start, length, extracted text, url, title

**ParsedBlock changes**: Add `Links` property (`IReadOnlyList<InlineLink>?`) alongside existing `Images`.

**Files**: `MarkdownParser.cs`
**Tests**:
- Basic link `[text](url)` → InlineStyle.Link
- Link with title `[text](url "title")`
- Link with angle-bracket destination `[text](<url>)`
- Link text with inline bold `[**bold** text](url)` — inner styles still parsed
- Not a link: missing closing paren `[text](url`
- Not a link: image syntax `![alt](url)` stays as Image
- Not a link inside fenced code block
- Not a link inside inline code span
- Multiple links on one line
- Adjacent link and image: `[link](a) ![img](b)`
- Empty link text `[](url)`

## Phase 2: Source Mode Rendering

**Goal**: Show raw syntax with dimmed link markers and styled link text.

**Syntax dimming** in `ApplySyntaxDimming()`:
- Dim `[` (1 char at link.Start)
- Dim `](url)` or `](url "title")` — from the `]` to the closing `)` inclusive

**Inline styling** in `ApplyInlineStyles()`:
- Link text (between `[` and `]`) rendered in link color (blue) with underline
- The `InlineStyle.Link` case applies foreground brush + underline decoration to the text portion only (not the dimmed markers)

**Link color**: Use a static frozen brush (`#2B7AE0` — same blue as checked checkboxes) consistent across all themes.

**Files**: `DocsCanvas.cs`

## Phase 3: BlockVisualMap — Hide Link Syntax

**Goal**: In visual mode, hide bracket/paren syntax, show only the link text.

**Hidden ranges**:
- Hide `[` — `HiddenRange(link.Start, 1)`
- Hide `](url)` or `](url "title")` — `HiddenRange(closeBracket, link.Start + link.Length - closeBracket)` where `closeBracket = link.Start + 1 + link.Text.Length`

This keeps the link text visible and hides only the syntactic markers, following the same pattern as bold (`**`) and italic (`*`) marker hiding.

**BlockVisualMap changes**: Store `Links` list (like `Images`) for the renderer to access URL metadata for click handling.

**Files**: `BlockVisualMap.cs`
**Tests**:
- `[text](url)` — `[` and `](url)` hidden, `text` visible
- RawToVisual mapping across link boundaries
- VisualToRaw mapping across link boundaries
- Link with title — title hidden too
- Multiple links in one block

## Phase 4: Visual Mode Rendering — Link Styling

**Goal**: Render link text with blue color and underline in visual mode.

**Styling** in `ApplyInlineStylesVisual()`:
- For `InlineStyle.Link` runs, apply link color brush + underline to the visible text range
- Use `map.RawToVisual()` to map positions (same pattern as bold/italic)

**Cursor**: Standard text cursor over link text (links are editable text, not atomic elements like images). The existing cursor navigation handles hidden ranges automatically.

**Tooltip**: When hovering over link text in visual mode, show the URL as a tooltip. Track the hovered link and use `ToolTip` or draw a custom tooltip overlay.

**Files**: `DocsCanvas.cs`, `DocsCanvas.VisualMode.cs`

## Phase 5: Ctrl+Click to Open

**Goal**: Ctrl+Click on link text in visual mode opens the URL in the default browser.

**Hit testing** in `OnMouseDown`:
- If Ctrl is held and the click lands on a visual line, check if the raw position falls within any `InlineLink` range
- Look up the link's URL from `ParsedBlock.Links`
- Open via `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
- Consume the click (don't move cursor)

**Visual feedback**:
- When Ctrl is held and mouse hovers over link text, change cursor to `Cursors.Hand`
- Restore default cursor on Ctrl release or mouse leave

**Safety**: Only open `http://`, `https://`, and local file paths. Ignore other schemes.

**Files**: `DocsCanvas.cs` (mouse handling), `DocsCanvas.VisualMode.cs`

## Phase 6: Toolbar and Keyboard Shortcut

**Goal**: Ctrl+K to insert/edit a link, toolbar button.

**Keyboard shortcut** (Ctrl+K):
- If text is selected: wrap selection as `[selected text](url)` and place cursor inside the `url` placeholder
- If no selection: insert `[](url)` and place cursor between `[` and `]`
- If cursor is inside an existing link: select the full link syntax for easy editing/removal

**Toolbar button**:
- Add link icon (chain/link path geometry) to `DocsFormattingBar`
- Wire to `ToggleLink()` method
- Button state: checked when cursor is inside a link

**Files**: `DocsFormattingBar.cs`, `DocsCanvas.cs`, `Themes/Generic.xaml`

## Implementation Order

```
Phase 1 (Parser)      — fully unit-testable
Phase 2 (Source)      — visual testing, depends on 1
Phase 3 (VisualMap)   — unit-testable, depends on 1
Phase 4 (Visual)      — depends on 3
Phase 5 (Ctrl+Click)  — depends on 4
Phase 6 (Toolbar)     — depends on all above
```

## Future: Autolinks (separate iteration or Phase 7)

GFM extended autolinks auto-detect bare URLs without explicit `[text](url)` syntax:
- `https://example.com` → clickable link
- `http://example.com` → clickable link
- `www.example.com` → clickable link (prefixed with `http://`)

This would be a parser-level addition that creates `InlineLink` records for bare URLs, with the URL also serving as the display text. All rendering, hidden ranges, and click handling would reuse the link infrastructure from this iteration.

## Verification

- **Phase 1**: `dotnet test Tests/RaisinDocs.Tests/` — parser unit tests
- **Phase 2**: run TestApp, type links in source mode, verify dimming and blue text
- **Phase 3**: `dotnet test Tests/RaisinDocs.Tests/` — BlockVisualMap tests
- **Phase 4**: run TestApp, switch to Visual mode, verify blue underlined link text, tooltip
- **Phase 5**: Ctrl+Click a link, verify it opens in browser
- **Phase 6**: Ctrl+K shortcut, toolbar button
- **End-to-end**: links mixed with bold/italic text, links adjacent to images, undo/redo, copy/paste blocks containing links
