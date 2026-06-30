# Design: RaisinDocs Chrome Extension — Markdown Viewer

## Context

RaisinDocs is a WPF markdown editor control with source and visual rendering modes, multiple themes (light, dark, dark-blue), and support for GFM plus custom extensions (color tags, extended autolinks, reference links). The rendering engine (`DocsCanvas`) is tightly coupled to WPF's `DrawingContext`/`FormattedText`, but the parsing layer (`MarkdownParser`) is a pure static class operating on strings and returning plain data structures — no UI dependencies.

A Chrome extension can bring the RaisinDocs reading experience to the browser: intercept `.md` files (local or remote) and render them with the same visual style and mode toggle as the WPF control. The browser handles text layout, fonts, scrolling, and image loading natively — the extension only needs to replicate the parsing rules and CSS styling.

This is a **viewer only** — no editing, no cursor, no undo/redo, no formatting bar mutations. The scope is read-only rendering of markdown content with source/visual mode toggle and theme switching.

## What to port

### From MarkdownParser (reimplement in JavaScript)

The C# `MarkdownParser` is ~1400 lines. The JS port needs:

**Block classification** (`ClassifyBlock`):
- Headings: `#` through `######` prefix → H1–H6
- Unordered list items: `- ` or `* ` prefix
- Task list items: `- [ ] `, `- [x] `, `- [X] ` prefix → unchecked/checked
- Ordered list items: `1. ` etc.
- Fenced code blocks: `` ``` `` toggle (cross-block state)
- Block quotes: `> ` prefix
- Table rows: header, separator (with alignment detection), data rows
- Horizontal rules: `---`, `***`, `___`
- Paragraphs: everything else

**Inline parsing** (`ParseInlineStyles`):
- Bold (`**`), italic (`*`), bold-italic (`***`), strikethrough (`~~`)
- Code spans (`` ` ``)
- Inline links: `[text](url "title")`
- Reference links: `[text][ref]` with `[ref]: url "title"` definitions
- Inline images: `![alt](url "title")`
- Reference images: `![alt][ref]`
- Extended autolinks: bare `https://`, `http://`, `www.` URLs
- Color extensions: `<!-- color: fg=red bg=blue -->` block colors, `{color:red}text{/color}` inline colors

**Key parsing rules to preserve exactly**:
- Code spans suppress all other inline parsing within them
- Image parsing runs before emphasis (images inside bold/italic are parsed as images)
- Backslash escapes (`\*`, `\[`, etc.)
- GFM autolink trailing punctuation trimming and balanced-paren handling
- Table column alignment from separator row (`:---`, `:---:`, `---:`)
- Fenced code blocks suppress all inline parsing
- Reference link definition collection (bottom-of-document `[ref]: url` lines)

### From DocsCanvas visual appearance (replicate as CSS/HTML)

The WPF renderer's visual decisions, translated to CSS:

**Typography**:
- Body: Segoe UI, 16px (maps to `_fontSize = 16` in DocsCanvas)
- Headings: Segoe UI at 32/26/22/18/16/14px for H1–H6
- Code: Cascadia Mono (fallback: Consolas, monospace), 14px
- Line height: ~1.4 for body, ~1.6 for headings

**Theme palettes** (from `ThemePalette` in DocsCanvas):

| Element | Light | Dark | Dark Blue |
|---|---|---|---|
| Background | `#FFFFFF` | `#1E1E1E` | `#1B2838` |
| Foreground | `#1E1E1E` | `#D4D4D4` | `#C8D6E5` |
| Syntax dim | `#A0A0A0` | `#6A6A6A` | `#5A7A9A` |
| Code background | `#F5F5F5` | `#2D2D2D` | `#243447` |
| Link color | `#0366D6` | `#569CD6` | `#6CB4EE` |
| Heading color | foreground | `#DCDCAA` | `#FFD580` |
| Strikethrough | `#808080` | `#808080` | `#708090` |
| Table border | `#D0D0D0` | `#404040` | `#3A5068` |
| Checkbox checked | `#4EC9B0` | `#4EC9B0` | `#4EC9B0` |

*Note*: Exact values should be verified against `ThemePalette` static initializers in `DocsCanvas.cs` at implementation time — they may have drifted since this document was written.

**Visual mode rendering**:
- Headings: larger font, no `#` prefix shown
- Bold/italic/code: styled text, no `**`/`*`/`` ` `` markers shown
- Links: blue underlined text, URL hidden, clickable
- Images: rendered inline, alt text as fallback
- Lists: bullet character (`•`) replaces `- ` prefix
- Task lists: checkbox glyph (☐/☑) replaces `- [ ] ` prefix, checked items dimmed
- Tables: bordered grid with cell padding, header row bold, column alignment applied
- Fenced code: monospace block with background, language label if specified
- Block quotes: left border accent
- Color extensions: inline `{color}` spans and block `<!-- color -->` regions apply foreground/background colors
- Horizontal rules: thin line separator

**Source mode rendering**:
- Raw markdown shown as-is
- Syntax characters dimmed (gray) — `#`, `**`, `*`, `` ` ``, `- `, `| `, `[`, `](url)`, etc.
- Monospace font for entire content (Cascadia Mono / Consolas)
- Fenced code block background shading still applied

## Extension architecture

### Manifest (Manifest V3)

```
manifest.json
├── permissions: activeTab, storage
├── content_scripts: match *.md URLs, raw text content-type
├── action: popup for settings (theme, default mode)
├── background: service worker for URL interception
└── web_accessible_resources: CSS, fonts
```

### File structure

```
raisindocs-viewer/
├── manifest.json
├── background.js          — service worker: detect .md URLs, inject content script
├── content.js             — main entry: detect raw markdown, replace DOM
├── parser.js              — MarkdownParser port (block + inline parsing)
├── renderer.js            — parse tree → HTML generation
├── popup.html / popup.js  — settings UI (theme, default mode)
├── styles/
│   ├── base.css           — shared layout (container, mode toggle button)
│   ├── visual.css         — visual mode styles
│   ├── source.css         — source mode styles (monospace, dimmed syntax)
│   └── themes/
│       ├── light.css
│       ├── dark.css
│       └── dark-blue.css
└── icons/
    ├── icon-16.png
    ├── icon-48.png
    └── icon-128.png
```

### Detection and activation

The extension activates when:
1. **Direct `.md` file** — browser navigates to a URL ending in `.md` (local `file://` or remote `https://`). The browser shows raw text; the content script detects this and replaces the page content.
2. **Raw content on GitHub** — user clicks "Raw" on a GitHub `.md` file, getting `raw.githubusercontent.com` plain text.
3. **Manual activation** — user clicks the extension icon on any page to attempt markdown rendering of the page body.

Detection heuristic: if `document.contentType` is `text/plain` or `text/markdown` and the URL ends in `.md`, auto-activate. Otherwise, wait for manual trigger.

### Rendering pipeline

```
Raw markdown string
    │
    ▼
parser.js: classifyBlocks(text)
    → [ { kind: 'h1', raw: '# Title', ... }, { kind: 'paragraph', raw: '...', ... }, ... ]
    │
    ▼
parser.js: parseInlineStyles(block)
    → [ { style: 'bold', start, length }, { style: 'link', start, length, url }, ... ]
    │
    ▼
renderer.js: renderVisual(blocks) / renderSource(blocks)
    → HTML string with CSS classes for styling
    │
    ▼
content.js: inject into DOM, apply theme CSS
```

### Source mode rendering (HTML)

Source mode wraps the entire content in a `<pre>` with monospace font. Syntax characters are wrapped in `<span class="syntax-dim">` for dimming. Example:

```html
<pre class="raisindocs-source">
  <span class="syntax-dim">## </span>Heading
  <span class="syntax-dim">**</span>bold text<span class="syntax-dim">**</span>
</pre>
```

### Visual mode rendering (HTML)

Visual mode generates semantic HTML. Example:

```html
<div class="raisindocs-visual">
  <h2>Heading</h2>
  <p><strong>bold text</strong></p>
  <ul class="task-list">
    <li><span class="checkbox checked">☑</span> <span class="task-checked">done item</span></li>
    <li><span class="checkbox">☐</span> pending item</li>
  </ul>
</div>
```

### Mode toggle

A floating button (bottom-right corner, similar to browser reader-mode toggles) switches between source and visual. The button shows the current mode icon — matching the source/visual icons from `DocsFormattingBar`. Keyboard shortcut: `Ctrl+M` (same as the WPF control).

State persists per-tab in session storage. Default mode is configurable via the popup.

### Theme switching

Theme selector in the extension popup. Choice persists in `chrome.storage.sync` (synced across devices). Applied by swapping the theme CSS class on the root container:

```html
<div class="raisindocs theme-dark-blue mode-visual">
```

### Minimap (optional, future)

The WPF control has a minimap scrollbar. A CSS-based minimap could be added later using a scaled-down rendering of the full document in a fixed sidebar — but this is out of scope for the initial version.

## Phases

### Phase 1: Parser port

Port `MarkdownParser.ClassifyBlock` and `ParseInlineStyles` to JavaScript. This is the foundation — everything else depends on correct parsing.

**Scope**:
- Block classification (all BlockKind values)
- Fenced code cross-block state tracking
- Inline style parsing: bold, italic, bold-italic, strikethrough, code spans
- Inline links, reference links, inline images, reference images
- Extended autolinks with trailing punctuation trimming
- Table cell parsing and column alignment detection
- Color extension parsing (block and inline)

**Validation**: Port a representative subset of the C# xUnit tests to a JS test runner (Vitest or plain Node assert). The parser must produce identical classifications and run boundaries for all test inputs.

### Phase 2: Visual mode renderer

Generate styled HTML from parsed blocks.

**Scope**:
- Semantic HTML output: `<h1>`–`<h6>`, `<p>`, `<strong>`, `<em>`, `<code>`, `<pre>`, `<a>`, `<img>`, `<ul>`, `<ol>`, `<li>`, `<table>`, `<blockquote>`, `<hr>`
- Task list checkboxes (visual only, not interactive — this is a viewer)
- Fenced code blocks with language class for potential syntax highlighting
- Table column alignment via CSS `text-align`
- Color extension spans with inline `style` attributes
- Link click handling (open in new tab)
- Theme CSS files matching the three WPF palettes

### Phase 3: Source mode renderer

Generate syntax-highlighted raw view.

**Scope**:
- Monospace rendering of raw text
- Syntax dimming: wrap markdown syntax characters in styled spans
- Fenced code block background
- Heading size differentiation (optional — could keep uniform monospace)

### Phase 4: Chrome extension shell

Wire parser and renderers into a working extension.

**Scope**:
- Manifest V3 configuration
- Content script: detect `.md` pages, replace DOM
- Mode toggle button (Ctrl+M shortcut)
- Popup: theme selector, default mode
- `chrome.storage.sync` for preferences
- Extension icons

### Phase 5: Polish

- Smooth mode transition (fade or instant swap)
- Print stylesheet
- GitHub-flavored URL handling (raw.githubusercontent.com)
- Local `file://` support (requires user to enable file access for the extension)
- Handle large files without blocking (chunked rendering or requestIdleCallback)

## Out of scope

- Editing, cursor, selection, undo/redo — this is a viewer
- Formatting bar — no mutations, no toolbar needed
- Minimap — possible future addition
- Syntax highlighting inside fenced code blocks — could integrate Prism.js or highlight.js later, but not in initial version
- Exporting to HTML/PDF — the browser's print function covers this adequately
- Markdown files embedded in web pages (e.g. GitHub rendered views) — only activates on raw/plain text
