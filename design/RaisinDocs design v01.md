# RaisinDocs — Design

WPF markdown editor control built on a bare `FrameworkElement` with `OnRender`/`DrawingContext`, same approach as `TerminalCanvas`. No RichTextBox, no FlowDocument, no WebView2.

**Markdown specification:** [GitHub Flavored Markdown (GFM)](https://github.github.com/gfm/), which is built on [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/) plus extensions for tables, task lists, strikethrough, and extended autolinks.

## Document model

The internal model is a list of **blocks**, each containing **runs** (spans of styled text). The model stores raw text — markdown syntax characters are part of the content and drive styling decisions at render time.

### Newline semantics (markdown-native from the start)

Markdown newlines are not plain text newlines:

| Input | Meaning | Internal representation |
|---|---|---|
| Single newline | Soft break (rendered as space or ignored within a paragraph) | Same paragraph, continues inline |
| Two spaces + newline | Hard line break (`<br>`) | Line break within the same block |
| Blank line (two newlines) | Paragraph break | New block |

The document model must distinguish these from iteration 2 onward. A naive "one newline = one line" model would have to be torn out later.

### Keyboard mapping

| Key | Effect |
|---|---|
| Enter | Paragraph break (blank line). For standalone blocks (headings, fenced code delimiters) inserts a single break instead — blank line is redundant since the block is already visually distinct. |
| Shift+Enter | Hard line break: appends `\` (or `  `) then splits. Skipped for headings (next line is already a new paragraph). Won't duplicate an existing trailing marker. |
| Ctrl+Enter | Soft break (single newline, no marker). |

### Hard break marker rules (CommonMark §6.7)

A trailing `\` is a hard break only when:
- It is the last character on the line (not followed by spaces)
- It is NOT inside a code span or fenced code block
- The count of consecutive trailing backslashes is odd (`\\` = escaped literal, `\\\` = escaped + hard break)

## Rendering

- `DrawingContext` with `FormattedText`/`GlyphRun` for text
- ClearType via DirectWrite (inherited from WPF)
- Word wrapping computed manually per block
- Cell/line height from font metrics, not fixed grid (unlike TerminalCanvas)
- Vertical layout: stack blocks with spacing (paragraph gap, heading gap, etc.)

## Iterations

### 1 — Editable plain text ✅
- Render unstyled text in FrameworkElement via OnRender
- Blinking cursor, typing, backspace, delete
- Single block, no wrapping yet

### 2 — Multi-line with markdown newline rules ✅
- Enter key: decide between hard break (shift+enter) and paragraph break (enter)
- Store and render paragraph breaks vs line breaks correctly
- Arrow keys, home/end navigation
- Basic word wrapping

### 3 — Selection and clipboard ✅
- Mouse drag and shift+arrow selection
- Hit testing: screen coordinates to document position
- Copy/paste (paste as plain text initially)

### 3b — Performance and scrolling ✅
- Layout dirty flag: skip recomputation on cursor blink, only recompute on text/size changes
- GlyphTypeface measurement cache: per-character advance width lookup instead of FormattedText allocation
- Frozen brushes and cached pen for rendering resources
- Mouse wheel scrolling with auto-scroll to keep cursor visible
- Viewport culling: only draw and hit-test visual lines within the visible region

### 3c — Scrollbar and smooth scrolling ✅
- Smooth scrolling with exponential decay (adapted from TerminalCanvas SmoothScroll pattern)
  - CompositionTarget.Rendering drives animation frames, unhooks when idle
  - Scroll position jumps immediately; visual offset decays back to 0
  - Decay: `offset *= Math.Exp(-frameInterval * 30.0)`, stops at < 0.5px
- Custom scrollbar drawn in OnRender on right edge
  - Proportionally-sized thumb, only visible when content exceeds viewport
  - Click track to page up/down, drag thumb for direct scroll (no smooth animation)
  - Content area width reduced by scrollbar width when visible

### 4 — Markdown styling (visible syntax) ✅
- `MarkdownParser` static class: block classification (`# heading` → H1–H6, `- list`, fenced code) and inline parsing (`**bold**`, `*italic*`, `` `code` ``, `***bold italic***`)
- Cross-block fenced code detection (``` ``` ``` toggles fence state)
- Style-aware rendering via `FormattedText` range styling (SetFontWeight/SetFontStyle/SetFontFamily)
- Heading font sizes: H1=32, H2=26, H3=22, H4=18, H5=16, H6=14; code=14pt Cascadia Mono
- Variable line heights per block kind, with style-aware character measurement and word wrapping
- Syntax marker dimming (`#`, `- `, `**`, `*`, `` ` ``, trailing `\` hard break) in gray
- Subtle gray background behind fenced code blocks
- Hit testing, cursor positioning, and selection updated for mixed styles
- 34 parser tests (block classification, inline parsing, fenced code, edge cases)

### 5 — WYSIWYG mode ✅
- Hide markdown syntax, show styled result (BlockVisualMap with HiddenRange)
- Cursor navigation skips hidden syntax (SkipCursorOverHiddenRanges, CrossToPreviousBlock/CrossToNextBlock)
- Typing at styled boundaries follows Word-like behavior: End key lands inside style, Right arrow exits
- Toggle between raw (Source) and WYSIWYG (Visual) views
- Partial class split: DocsCanvas.SourceMode.cs, DocsCanvas.VisualMode.cs
- 18 UI tests (cursor skip bold/italic/code markers, cross-block boundaries, Home/End, typing at boundaries)

### 4b — Undo/redo ✅
- Snapshot-based undo (memento pattern): captures full document state (`string[]` blocks + cursor/anchor) at each undo boundary
- VS Code-style grouping with 600ms timer: continuous typing groups into one undo unit
- Group broken by: pause >600ms, cursor movement (arrow/mouse/Home/End), Enter, or switching action type (typing ↔ deleting)
- Enter, Cut, Paste each sealed as their own undo unit
- Two-stack model (`_undoStack` + `_redoStack`), new mutations clear redo
- 200-entry max undo depth
- Ctrl+Z / Ctrl+Y keyboard shortcuts
- 18 tests covering round-trip, cursor restoration, group management, redo invalidation, stack depth, compound operations

### 6 — Inline images ✅
- CommonMark `![alt](url "title")` syntax detection in MarkdownParser (InlineImage record, InlineStyle.Image lock marker)
- Parser runs MarkImages after MarkCodeSpans but before emphasis — code spans suppress images, images suppress emphasis
- Balanced bracket matching for alt text, balanced parens and angle-bracket destinations for URLs, optional quoted titles
- BlockVisualMap hides entire image syntax as HiddenRange, passes Images through for renderer
- ImageCache: async loading from local file paths and HTTP URLs, BitmapImage frozen for thread safety, scale-to-fit (native size, max content width, preserve aspect ratio), placeholder for missing/loading images
- Layout: FitLine treats images as atomic inline elements (cannot break across lines), line height accounts for image height
- Segmented rendering: DrawVisualLineWithImages splits text and images, draws each segment at correct X position
- Cursor treats images as atomic elements via existing hidden range navigation
- Hit testing and selection measurement account for image width
- 17 parser tests, 7 BlockVisualMap tests, 4 UI navigation tests

### 6b — Image Preview in Source mode ✅
- Three-way `ImagePreviewMode` enum: Off, Inline, On Hover
- **Off**: Source mode shows image syntax as dimmed text (existing behavior)
- **Inline**: images render below their syntax line; layout adds image height to the visual line's OverrideHeight
- **On Hover**: mouse hover over image syntax shows a bordered popup (max 300px wide) near the cursor, drawn as an overlay in OnRender; hit-testing detects image syntax ranges; OnMouseLeave clears the popup
- Split button on the formatting bar: left side cycles modes, right ▾ arrow opens a dropdown menu with all three options (current one checked)
- Three distinct vector icons drawn via WPF Path geometry: slashed frame (Off), mountain landscape (Inline), eye symbol (On Hover)
- No existing markdown editors (Typora, Obsidian, VS Code, Mark Text) offer an embeddable native control — RaisinDocs is unique as a reusable WPF markdown editor component

### 7 — Tables ✅
- See `design/GFM Table Support - Iteration 7.md` for detailed plan
- GFM table syntax: header row, separator row (with column alignment), data rows
- Parser detects table context across consecutive blocks (two-pass, like fenced code)
- New BlockKind values: TableHeaderRow, TableSeparatorRow, TableDataRow
- Source mode: dimmed pipe characters and separator row
- Visual mode: grid rendering with borders, cell padding, header styling, column alignment
- Tab/Shift+Tab cell navigation, Enter inserts new row
- Toolbar button to insert table template
- Rectangular cell selection with copy and clear operations

### 8 — Task list items ✅
- See `design/GFM Task Lists - Iteration 8.md` for detailed plan
- GFM task list syntax: `- [ ] unchecked`, `- [x] checked`
- Parser detects checkbox prefix, assigns TaskListItemUnchecked / TaskListItemChecked BlockKind
- Source mode: dimmed checkbox prefix
- Visual mode: rendered checkbox glyph (empty square / filled with checkmark), checked items dimmed/struck
- Click checkbox to toggle checked state, with undo support
- Toolbar button to insert/toggle task list item

### 9 — Inline links ✅
- See `design/Inline Links - Iteration 9.md` for detailed plan
- CommonMark inline link syntax: `[text](url)` with optional title
- Parser detects bracket/paren structure (reuses image parsing helpers), assigns InlineStyle.Link
- Source mode: dimmed `[` and `](url)` markers, link text in blue with underline
- Visual mode: hidden syntax markers, blue underlined link text, URL tooltip on hover
- Ctrl+Click opens URL in default browser
- Ctrl+K keyboard shortcut to insert/edit link, toolbar button
- Future follow-on: GFM extended autolinks (bare URL detection)

### GFM extensions roadmap
- ~~Strikethrough (`~~text~~`)~~ — ✅ implemented in iteration 4
- ~~Tables~~ — ✅ implemented in iteration 7
- ~~Task list items~~ — ✅ implemented in iteration 8
- Extended autolinks — bare URLs auto-linked without `<>` syntax (follow-on to iteration 9)

### Future
- ~~Links (clickable in view mode, editable syntax in edit mode)~~ — ✅ implemented in iteration 9
- Motion blur during smooth scroll (ghost copies offset in scroll direction, like RaisinTerminal2)
- Image display sizing: GFM has no sizing syntax; options include inline HTML (`<img src="url" width="300">`), Obsidian-style pipe syntax (`![alt|300](url)`), or visual drag-resize that auto-generates markup. Typora's drag-to-resize UX is the gold standard. Could also auto-downscale pasted images to reduce file size.
- Reference-style images (`![alt][ref]` with `[ref]: url "title"` definitions)
- Syntax highlighting in code blocks
- Drag-and-drop
- Toggle to show/hide hard break indicators (trailing spaces at end of lines that produce a `<br>` per CommonMark spec)
- Text expansion / autocomplete: user-defined shorthand dictionary (e.g. `t`→"the", `des`→"design") with inline ghost text shown dimmed after the cursor. Tab to accept, any other key to dismiss. Could also offer frequency-based English word completions as you type.
- C# solution symbol harvesting: scan the loaded solution for meaningful names (class names, method names, properties, enums, namespaces) and feed them into the autocomplete dictionary so documentation can reference code identifiers accurately without manual typing.
- Autocorrect: static replacement table (`Dictionary<string, string>`) of common typos (e.g. `"teh"→"the"`, `"sould"→"should"`) applied on word boundary, plus auto-correct from `ISpellChecker` top suggestion when edit distance is 1–2. Ship ~200 built-in entries, let users add their own.
- Spell checking via Windows `ISpellChecker` COM API — no bundled dictionaries, uses the OS language packs. Declare COM interfaces (`ISpellCheckerFactory`, `ISpellChecker`, `IEnumSpellingError`, `ISpellingError`) in a small interop class and check words per block, skipping markdown syntax and code spans/blocks. Render red squiggly underlines under misspelled words in `OnRender` and offer suggestions via right-click context menu.
