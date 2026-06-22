# RaisinDocs — Design

WPF markdown editor control built on a bare `FrameworkElement` with `OnRender`/`DrawingContext`, same approach as `TerminalCanvas`. No RichTextBox, no FlowDocument, no WebView2.

**Markdown specification:** [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/)

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

## Rendering

- `DrawingContext` with `FormattedText`/`GlyphRun` for text
- ClearType via DirectWrite (inherited from WPF)
- Word wrapping computed manually per block
- Cell/line height from font metrics, not fixed grid (unlike TerminalCanvas)
- Vertical layout: stack blocks with spacing (paragraph gap, heading gap, etc.)

## Iterations

### 1 — Editable plain text
- Render unstyled text in FrameworkElement via OnRender
- Blinking cursor, typing, backspace, delete
- Single block, no wrapping yet

### 2 — Multi-line with markdown newline rules
- Enter key: decide between hard break (shift+enter) and paragraph break (enter)
- Store and render paragraph breaks vs line breaks correctly
- Arrow keys, home/end navigation
- Basic word wrapping

### 3 — Selection and clipboard
- Mouse drag and shift+arrow selection
- Hit testing: screen coordinates to document position
- Copy/paste (paste as plain text initially)

### 3b — Performance and scrolling
- Layout dirty flag: skip recomputation on cursor blink, only recompute on text/size changes
- GlyphTypeface measurement cache: per-character advance width lookup instead of FormattedText allocation
- Frozen brushes and cached pen for rendering resources
- Mouse wheel scrolling with auto-scroll to keep cursor visible
- Viewport culling: only draw and hit-test visual lines within the visible region

### 3c — Scrollbar and smooth scrolling
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
- Syntax marker dimming (`#`, `- `, `**`, `*`, `` ` ``) in gray
- Subtle gray background behind fenced code blocks
- Hit testing, cursor positioning, and selection updated for mixed styles
- 34 parser tests (block classification, inline parsing, fenced code, edge cases)

### 5 — WYSIWYG mode
- Hide markdown syntax, show styled result
- Cursor navigation skips hidden syntax
- Typing at styled boundaries inserts/removes syntax appropriately
- Toggle between raw and WYSIWYG views

### 4b — Undo/redo ✅
- Snapshot-based undo (memento pattern): captures full document state (`string[]` blocks + cursor/anchor) at each undo boundary
- VS Code-style grouping with 600ms timer: continuous typing groups into one undo unit
- Group broken by: pause >600ms, cursor movement (arrow/mouse/Home/End), Enter, or switching action type (typing ↔ deleting)
- Enter, Cut, Paste each sealed as their own undo unit
- Two-stack model (`_undoStack` + `_redoStack`), new mutations clear redo
- 200-entry max undo depth
- Ctrl+Z / Ctrl+Y keyboard shortcuts
- 18 tests covering round-trip, cursor restoration, group management, redo invalidation, stack depth, compound operations

### Future
- Motion blur during smooth scroll (ghost copies offset in scroll direction, like RaisinTerminal2)
- Images (inline and reference)
- Tables
- Links (clickable in view mode, editable syntax in edit mode)
- Syntax highlighting in code blocks
- Drag-and-drop
- Toggle to show/hide hard break indicators (trailing spaces at end of lines that produce a `<br>` per CommonMark spec)
- Text expansion / autocomplete: user-defined shorthand dictionary (e.g. `t`→"the", `des`→"design") with inline ghost text shown dimmed after the cursor. Tab to accept, any other key to dismiss. Could also offer frequency-based English word completions as you type.
- C# solution symbol harvesting: scan the loaded solution for meaningful names (class names, method names, properties, enums, namespaces) and feed them into the autocomplete dictionary so documentation can reference code identifiers accurately without manual typing.
- Autocorrect: static replacement table (`Dictionary<string, string>`) of common typos (e.g. `"teh"→"the"`, `"sould"→"should"`) applied on word boundary, plus auto-correct from `ISpellChecker` top suggestion when edit distance is 1–2. Ship ~200 built-in entries, let users add their own.
- Spell checking via Windows `ISpellChecker` COM API — no bundled dictionaries, uses the OS language packs. Declare COM interfaces (`ISpellCheckerFactory`, `ISpellChecker`, `IEnumSpellingError`, `ISpellingError`) in a small interop class and check words per block, skipping markdown syntax and code spans/blocks. Render red squiggly underlines under misspelled words in `OnRender` and offer suggestions via right-click context menu.
