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

### 4 — Markdown styling (visible syntax)
- Parse inline syntax: `**bold**`, `*italic*`, `` `code` ``
- Parse block syntax: `# heading`, `- list`, ``` ``` code block ```
- Render with appropriate fonts/sizes — syntax characters still visible

### 5 — WYSIWYG mode
- Hide markdown syntax, show styled result
- Cursor navigation skips hidden syntax
- Typing at styled boundaries inserts/removes syntax appropriately
- Toggle between raw and WYSIWYG views

### Future
- Images (inline and reference)
- Tables
- Links (clickable in view mode, editable syntax in edit mode)
- Undo/redo
- Syntax highlighting in code blocks
- Drag-and-drop
