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
- **CommonMark 0.31.2** — headings, bold, italic, bold-italic, strikethrough, inline code, fenced code blocks, bullet lists, blockquotes
- **Inline images** — `![alt](url)` with async loading from local files and HTTP URLs, scale-to-fit, placeholders for missing images
- **Image Preview in Source mode** — three modes (Off / Inline / On Hover) via a split button on the formatting bar
- **Undo / redo** — VS Code-style grouping with 600ms timer, Ctrl+Z / Ctrl+Y
- **Formatting bar** — toolbar control with toggle buttons for all inline and block styles, theme switch, image preview switch
- **Light and dark themes** — `EditorTheme.Light` / `EditorTheme.Dark` with a single property
- **Smooth scrolling** — exponential decay animation, custom-drawn proportional scrollbar
- **Viewport culling** — only draws visible lines, scales to large documents
- **Full keyboard navigation** — arrow keys, Home/End, Ctrl+Home/End, Page Up/Down, word-level movement with Ctrl

## Getting started

### XAML

```xml
<Window xmlns:docs="clr-namespace:RaisinDocs;assembly=RaisinDocs">
    <DockPanel>
        <docs:DocsFormattingBar DockPanel.Dock="Top"
                                Canvas="{Binding ElementName=Editor}"
                                Background="#2D2D2D"
                                Foreground="#D4D4D4" />
        <docs:DocsCanvas x:Name="Editor" />
    </DockPanel>
</Window>
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
```

The formatting bar is optional — `DocsCanvas` works standalone. Bind the bar's `Canvas` property to your canvas instance and it picks up all formatting state automatically.

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+B | Toggle bold |
| Ctrl+I | Toggle italic |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+X / C / V | Cut / Copy / Paste |
| Tab | Toggle Source / Visual mode |

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
