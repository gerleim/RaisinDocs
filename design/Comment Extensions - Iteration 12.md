# Plan: Comment-Based Extensions (Iteration 12)

## Context

All iterations through 11 (Reference Links and Images) are complete. The editor now supports the full GFM inline formatting set: bold, italic, strikethrough, code, images, links, autolinks, and tables.

Standard Markdown has no mechanism for text coloring or theming. CSS-based approaches (`<span style="color:red">`) don't support named/themed colors and are stripped by GitHub's HTML sanitizer (XSS prevention). We need a custom extension syntax that:

- Is invisible in all standard Markdown renderers (safe degradation — content preserved, styling lost)
- Supports named, theme-defined colors (change once, update everywhere)
- Allows both inline (span-level) and block-level color application
- Is extensible for future properties without changing the grammar shape

**Solution**: HTML comment-based syntax with an `@` sigil prefix (`<!--@...-->`). Standard HTML comments are hidden by every Markdown renderer, so these extensions are invisible outside RaisinDocs. The `@` prefix distinguishes extension comments from normal user comments (`<!-- TODO -->`).

File extension remains `.md` — documents are valid Markdown with or without the extensions.

## Syntax Specification

### Theme Definition (document-level)

Defines named colors for reuse throughout the document. Placed anywhere, typically at the top:

```markdown
<!--@theme
  warning = #FF6B6B
  accent = #4ECDC4
  info = #45B7D1
  highlight = #FFFACD
  note-bg = #2A2A3E
-->
```

Rules:
- One `name = value` per line
- Values: `#RRGGBB`, `#RGB`, or CSS named colors (`red`, `blue`, `goldenrod`, etc.)
- Whitespace-flexible: spaces around `=` are optional, blank lines ignored
- Multiple `<!--@theme-->` blocks merge; later definitions win on name conflict
- Names are case-insensitive, kebab-case recommended (e.g. `note-bg`, `code-accent`)
- A theme block on a line by itself is a standalone block; inline within a paragraph is also valid

### Inline Color (span-level)

Apply foreground and/or background color to a run of text within a paragraph:

```markdown
Some text <!--@fg:warning-->warning text<!--/@fg--> and more.
Some text <!--@bg:highlight-->highlighted<!--/@bg--> and more.
Some text <!--@fg:accent bg:highlight-->both colors<!--/@--> and more.
```

Tags:
- `<!--@fg:name-->` — foreground (font) color start
- `<!--@bg:name-->` — background color start
- `<!--@fg:name bg:name-->` — both in one opener (space-separated properties)
- `<!--/@fg-->` — close foreground
- `<!--/@bg-->` — close background
- `<!--/@-->` — close all inline color spans

Literal hex values are allowed inline without a theme name:

```markdown
<!--@fg:#FF0000-->red text<!--/@fg-->
```

Nesting: inner spans override outer for the same property:

```markdown
<!--@fg:accent-->accent text <!--@fg:warning-->warning here<!--/@fg--> back to accent<!--/@fg-->
```

### Block Color (block-level)

Wrap one or more blocks (paragraphs, lists, headings, etc.) in a styled region:

```markdown
<!--@div fg:accent bg:note-bg-->
This entire paragraph is styled.

So is this one — the div spans multiple blocks.

- List items too
<!--/@div-->
```

Tags:
- `<!--@div fg:name-->` or `<!--@div bg:name-->` or `<!--@div fg:name bg:name-->` — open styled region
- `<!--/@div-->` — close styled region

Rules:
- Must be on its own line (like a fenced code delimiter)
- Nesting is allowed; inner div overrides outer for the same property
- Unclosed div at end of document is implicitly closed

### CSS Named Colors

The parser recognizes the standard CSS named colors (the 148 entries from CSS Color Level 4): `red`, `blue`, `green`, `goldenrod`, `rebeccapurple`, etc. These can be used anywhere a `#RRGGBB` value is accepted, both in theme definitions and inline:

```markdown
<!--@theme
  warning = red
  info = dodgerblue
-->
<!--@fg:goldenrod-->direct named color<!--/@fg-->
```

### Tag Summary

| Tag | Scope | Purpose |
|---|---|---|
| `<!--@theme ... -->` | Document | Define named colors |
| `<!--@fg:name-->` | Inline | Foreground color start |
| `<!--@bg:name-->` | Inline | Background color start |
| `<!--@fg:name bg:name-->` | Inline | Both colors start |
| `<!--/@fg-->` | Inline | Close foreground span |
| `<!--/@bg-->` | Inline | Close background span |
| `<!--/@-->` | Inline | Close all inline spans |
| `<!--@div ...-->` | Block | Styled block region start |
| `<!--/@div-->` | Block | Styled block region end |

## Phase 1: Theme Parser

**Goal**: Parse `<!--@theme ... -->` blocks and build a name-to-color dictionary.

**New types** in `MarkdownParser.cs`:
- `ColorTheme` class — wraps a `Dictionary<string, Color>` with name-to-color lookup
- `ParseTheme(string[] blocks)` static method — scans all blocks for `<!--@theme` comments, parses `name = value` lines, resolves hex and named CSS colors

**Parsing logic**:
- Scan each block's text for the pattern `<!--@theme` ... `-->`
- Within the theme block, split by newlines, parse `name = value` pairs
- Resolve values via `TryParseHexColor` (handles `#RGB` and `#RRGGBB`) and `TryParseNamedColor` (CSS color table)
- Multiple theme blocks merge into one `ColorTheme`; later wins on conflict
- Invalid entries (bad color values, malformed lines) are silently skipped

**CSS named color table**: Static `Dictionary<string, Color>` with all 148 CSS Color Level 4 entries. Defined as a static field in `MarkdownParser.cs` or a separate `CssColors.cs` helper.

**Files**: `MarkdownParser.cs` (or new `CssColors.cs` for the named color table)
**Tests**:
- Single theme block with multiple entries
- `#RRGGBB` and `#RGB` hex parsing
- CSS named colors (`red`, `dodgerblue`, `rebeccapurple`)
- Case-insensitive name lookup
- Multiple theme blocks merge (later wins)
- Whitespace variations (no spaces, extra spaces, tabs)
- Invalid values skipped without error
- Empty theme block
- Theme block mixed with regular content in the same document

## Phase 2: Inline Color Parser

**Goal**: Detect `<!--@fg:name-->` / `<!--@bg:name-->` / `<!--/@...-->` tags within a block and record colored spans.

**New types** in `MarkdownParser.cs`:
- `ColorSpan` record — `(int Start, int Length, Color? Foreground, Color? Background)`
- `InlineStyle.ColoredText` enum value (or handle via separate `ColorSpans` list on `ParsedBlock`, outside the `InlineStyle` pipeline — since colored text can combine with bold/italic)

**Design decision — color as a parallel layer**:
Color is independent of bold/italic/code styling. A span can be both bold and red. Rather than adding combinatorial `InlineStyle` values, color is tracked as a separate list of `ColorSpan` entries on `ParsedBlock`, alongside the existing `StyledRun[]` array. The renderer applies both: `StyledRun` determines font weight/style, `ColorSpan` determines brush color.

**Parsing** — new `ParseColorTags()` method, called after `ParseInlines`:
- Scan for `<!--@fg:`, `<!--@bg:`, `<!--@fg:...bg:` patterns
- For each opener, find the matching closer (`<!--/@fg-->`, `<!--/@bg-->`, `<!--/@-->`)
- Resolve color name against the document's `ColorTheme`, or parse literal `#hex`
- Record `ColorSpan` with resolved `Color` values
- Mark the comment tags themselves in a `HiddenRange`-compatible way (they should not display as text)

**Interaction with existing inline styles**:
- Color tags are parsed *after* all existing inline styles (code spans, images, links, emphasis)
- Color tags inside code spans are treated as literal text (not parsed)
- Color can overlap with any other inline style — they are orthogonal layers

**Files**: `MarkdownParser.cs`
**Tests**:
- `<!--@fg:warning-->text<!--/@fg-->` with theme → ColorSpan with correct color
- `<!--@bg:highlight-->text<!--/@bg-->` → background color
- `<!--@fg:accent bg:highlight-->text<!--/@-->` → both colors
- Literal hex `<!--@fg:#FF0000-->text<!--/@fg-->` → red foreground
- Nested: inner fg overrides outer fg
- Color inside code span → not parsed (literal text)
- Color spanning bold text → both bold and colored
- Unresolved theme name → no ColorSpan (graceful skip)
- Unclosed color tag → extends to end of block
- Multiple color spans in one block

## Phase 3: Block-Level Color Parser

**Goal**: Detect `<!--@div ...-->` / `<!--/@div-->` and assign colors to enclosed blocks.

**Parsing** — two-pass, similar to fenced code and table detection:
- First pass: `ClassifyBlock` identifies blocks that are exactly `<!--@div ...-->` or `<!--/@div-->`
- Second pass: pair openers with closers, assign color properties to all blocks in between
- New `BlockKind.ColorDivOpen` and `BlockKind.ColorDivClose` (or handle without new BlockKinds by storing metadata on existing blocks)

**Block color storage**: Each `ParsedBlock` gains an optional `BlockColor` property — `(Color? Foreground, Color? Background)` — inherited from enclosing `<!--@div-->` tags. Inner divs override outer for the same property.

**Files**: `MarkdownParser.cs`
**Tests**:
- Single div wrapping paragraphs
- Div with fg only, bg only, both
- Nested divs (inner overrides outer)
- Unclosed div → implicit close at document end
- Div around headings, lists, code blocks
- Div with theme-defined colors
- Div with literal hex colors
- Empty div (no blocks inside)

## Phase 4: Source Mode Rendering

**Goal**: Show color extension comments as dimmed syntax; apply actual colors to the affected text.

**Theme blocks**: Render as dimmed text (same treatment as fenced code delimiters or link definitions). The entire `<!--@theme ... -->` block is shown in syntax dim color.

**Inline color tags**: Dim the `<!--@fg:name-->` and `<!--/@fg-->` markers. Apply the resolved foreground/background color to the text between them via `FormattedText.SetForegroundBrush` / drawing a background rectangle.

**Block div tags**: Dim the `<!--@div ...-->` and `<!--/@div-->` lines. Apply background color as a block-level background rectangle (like fenced code blocks). Apply foreground color to all text in enclosed blocks.

**Files**: `DocsCanvas.cs`

## Phase 5: Visual Mode — Hide Extension Syntax

**Goal**: In visual mode, hide all extension comment tags and show only the styled result.

**Hidden ranges**:
- Theme blocks: entire block hidden (like link definitions — informational, not displayed)
- Inline color tags: `<!--@fg:name-->` and `<!--/@fg-->` hidden, text between them visible and colored
- Block div tags: `<!--@div ...-->` and `<!--/@div-->` lines hidden entirely

**BlockVisualMap changes**: Add `ColorSpans` to the visual map for the renderer. Process theme blocks as fully hidden blocks (zero visual height, like link definitions).

**Files**: `BlockVisualMap.cs`
**Tests**:
- Inline color tags hidden, text visible
- Theme block fully hidden
- Div open/close lines hidden
- RawToVisual / VisualToRaw mapping across color tag boundaries

## Phase 6: Visual Mode Rendering

**Goal**: Render colored text in visual mode using resolved colors.

**Inline colors**:
- Apply `SetForegroundBrush` on `FormattedText` for foreground ColorSpans
- Draw background rectangles behind text for background ColorSpans
- Colors layer on top of existing inline styles (bold red, italic highlighted, etc.)

**Block colors**:
- Draw block-level background rectangle spanning the full content width for `bg` divs
- Apply foreground brush to all text in blocks enclosed by `fg` divs

**Theme awareness**: If the editor's `Theme` property changes (light/dark), theme colors remain as defined by the document — they are author-controlled, not editor-theme-dependent.

**Files**: `DocsCanvas.cs`, `DocsCanvas.VisualMode.cs`

## Phase 7: Toolbar and Editing Support

**Goal**: UI for inserting and editing color extensions.

**Color picker** (deferred — keep simple for initial release):
- Toolbar dropdown button with a small palette of common colors
- Inserts `<!--@fg:colorname-->` / `<!--/@fg-->` around selection
- If a theme is defined, show theme color names in the palette

**Editing behavior**:
- Typing inside a color span inherits the span's color
- Backspace/delete at color tag boundaries: standard behavior (delete the comment character by character)
- Undo/redo: color tags are regular text, existing undo system handles them

**Files**: `DocsFormattingBar.cs`, `DocsCanvas.cs`

## Implementation Order

```
Phase 1 (Theme parser)       — fully unit-testable, no UI
Phase 2 (Inline color parser) — unit-testable, depends on 1
Phase 3 (Block color parser)  — unit-testable, depends on 1
Phase 4 (Source rendering)    — visual testing, depends on 2+3
Phase 5 (Visual map)          — unit-testable, depends on 2+3
Phase 6 (Visual rendering)    — depends on 5
Phase 7 (Toolbar)             — depends on all above
```

Phases 2 and 3 can be developed in parallel since they are independent parsing concerns that both depend only on Phase 1.

## Verification

- **Phase 1**: `dotnet test Tests/RaisinDocs.Tests/` — theme parsing tests
- **Phase 2**: `dotnet test Tests/RaisinDocs.Tests/` — inline color span tests
- **Phase 3**: `dotnet test Tests/RaisinDocs.Tests/` — block div tests
- **Phase 4**: run TestApp, type color extensions in source mode, verify dimming and color application
- **Phase 5**: `dotnet test Tests/RaisinDocs.Tests/` — BlockVisualMap color tests
- **Phase 6**: run TestApp, switch to Visual mode, verify colored text, block backgrounds
- **Phase 7**: toolbar color picker, insert around selection
- **End-to-end**: colored text mixed with bold/italic/links, color spans crossing emphasis boundaries, undo/redo, copy/paste blocks with color tags, document with no theme (literal hex only), document with theme (named colors only)

## Future Extensions (same `<!--@...-->` syntax)

The comment extension grammar is designed to accommodate future properties without structural changes:

| Property | Syntax example | Purpose |
|---|---|---|
| Font size | `<!--@size:18-->` | Override font size for a span |
| Border | `<!--@border:accent-->` | Colored border around a block |
| Padding | `<!--@pad:8-->` | Block padding in pixels |
| Named styles | `<!--@style warning-->` | Bundle of properties defined once |
| Alignment | `<!--@align:center-->` | Block text alignment |
| Opacity | `<!--@opacity:0.5-->` | Semi-transparent text or blocks |

These would follow the same pattern: `<!--@property:value-->` opener, `<!--/@property-->` closer, invisible in standard renderers.
