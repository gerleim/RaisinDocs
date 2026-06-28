# RaisinDocs

A native WPF markdown editor control built from scratch on `FrameworkElement` with `OnRender` / `DrawingContext`. No RichTextBox, no FlowDocument, no WebView2.

Drop it into any WPF application as a single control. There is nothing else like it — Typora, Obsidian, and VS Code are all standalone Electron apps. RaisinDocs is an embeddable .NET component.

## Why

If you're building a WPF desktop application and need a markdown editor, your options today are:

- **WebView2 + a JS editor** — adds a browser runtime, cross-boundary marshalling, and a completely different programming model inside your WPF app.
- **RichTextBox / FlowDocument** — designed for rich text, not markdown. Fighting the framework to get markdown semantics right is a losing battle.
- **Plain TextBox + preview pane** — editing and preview are disconnected. No inline styling, no WYSIWYG.

RaisinDocs is none of these. It renders text directly with `FormattedText` and `GlyphTypeface`, measures and wraps words itself, and draws everything — text, images, cursor, selection, scrollbar — in a single `OnRender` pass. The result is a fast, self-contained control with no heavy dependencies.

## Features

- **Source and Visual modes** — toggle between raw markdown syntax and a WYSIWYG view that hides markers and shows styled text
- **CommonMark 0.31.2** — headings, bold, italic, bold-italic, strikethrough, inline code, fenced code blocks, bullet lists, blockquotes, hard/soft line breaks
- **GFM tables** — pipe-delimited tables with cell navigation, rectangular selection, row/column insert/delete
- **GFM task lists** — `- [ ]` / `- [x]` checkboxes with click-to-toggle in visual mode
- **Inline images** — `![alt](url)` with async loading from local files and HTTP URLs, scale-to-fit, placeholders for missing images
- **Image Preview in Source mode** — three modes (Off / Inline / On Hover) via a split button on the formatting bar
- **Undo / redo** — VS Code-style grouping with 600ms timer, Ctrl+Z / Ctrl+Y
- **Formatting bar** — toolbar control with toggle buttons for all inline and block styles, theme switch, image preview switch
- **Three themes** — `EditorTheme.Light` / `EditorTheme.Dark` / `EditorTheme.DarkBlue` with a single property
- **Smooth scrolling** — exponential decay animation, custom-drawn proportional scrollbar
- **Viewport culling** — only draws visible lines, scales to large documents
- **Full keyboard navigation** — arrow keys, Home/End, Ctrl+Home/End, Page Up/Down, word-level movement with Ctrl

## Getting started

### XAML

```xml
<Window xmlns:docs="clr-namespace:RaisinDocs;assembly=RaisinDocs">
    <docs:DocsEditor x:Name="Editor" ShowToolbar="True" />
</Window>
```

`DocsEditor` wraps the canvas and formatting bar into a single control. Set `ShowToolbar="False"` to hide the toolbar.

For custom toolbar placement, compose the parts manually instead:

```xml
<DockPanel>
    <docs:DocsFormattingBar DockPanel.Dock="Top"
                            Canvas="{Binding ElementName=Canvas}" />
    <docs:DocsCanvas x:Name="Canvas" />
</DockPanel>
```

### Code-behind

```csharp
// Load content
Editor.DocumentBasePath = @"C:\MyProject\docs";
Editor.SetText(File.ReadAllText("document.md"));

// Save content
File.WriteAllText("document.md", Editor.GetText());

// Theme
Editor.Theme = DocsCanvas.EditorTheme.Dark;

// Persist and restore editor state (theme, edit mode, image preview)
var state = Editor.GetState();
File.WriteAllText("state.json", JsonSerializer.Serialize(state));
// ... later ...
Editor.ApplyState(JsonSerializer.Deserialize<DocsEditorState>(json));
```

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+B | Toggle bold |
| Ctrl+I | Toggle italic |
| Ctrl+M | Toggle Source / Visual mode |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Shift+Enter | Hard line break (visible `\` marker) |
| Ctrl+Enter | Soft break (single newline, no marker) |
| Ctrl+X / C / V | Cut / Copy / Paste |

## Requirements

- .NET 8+
- Windows (WPF)

## Building

```bash
dotnet build RaisinDocs.slnx
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj
```

## Project structure

| Project | Description |
|---|---|
| `RaisinDocs` | The editor control library |
| `RaisinDocs.TestApp` | Sample WPF host app with dark theme |
| `Tests/RaisinDocs.Tests` | Unit tests (parser, document model, visual map) |
| `Tests/RaisinDocs.Tests.UI` | UI tests (navigation, cursor behavior) |

## License

MIT
