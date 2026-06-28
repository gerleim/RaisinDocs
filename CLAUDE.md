# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Commits

Do not append `Co-Authored-By` trailers to commit messages.

## Build Commands

```bash
# Build
dotnet build RaisinDocs.slnx

# Test (xUnit)
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj

# Run a single test
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# UI tests
dotnet test Tests/RaisinDocs.Tests.UI/RaisinDocs.Tests.UI.csproj

# Run test app
dotnet run --project RaisinDocs.TestApp/RaisinDocs.TestApp.csproj
```

## NuGet mode (for CI / public builds)

The project uses conditional references: by default it uses `ProjectReference` to sibling Raisin libraries (local dev). Pass `-p:UseProjectReferences=false` to switch to NuGet packages instead — this is how the public repo builds without the sibling folders.

```bash
dotnet build RaisinDocs.slnx -p:UseProjectReferences=false
```

## Architecture

**WPF markdown editor control** (.NET 8, C#) built on a bare `FrameworkElement` with `OnRender`/`DrawingContext`. No RichTextBox, no FlowDocument, no WebView2.

### Projects

- **RaisinDocs** — The editor control library (DocsCanvas, Document)
- **RaisinDocs.TestApp** — WPF app hosting DocsCanvas in AvalonDock with dark theme
- **Tests/RaisinDocs.Tests** — xUnit v3 + FluentAssertions tests for the Document model
- **Tests/RaisinDocs.Tests.UI** — xUnit UI tests (DocsCanvas rendering/layout)

### Key classes

- **DocsCanvas** — Custom `FrameworkElement` handling rendering, input, scrolling, selection, and layout. Renders text via `FormattedText`/`GlyphTypeface` with viewport culling and smooth scrolling. Owns the `Document` instance and delegates all text mutations to it. Split across partial classes:
  - `DocsCanvas.cs` — core rendering (`OnRender`), layout, measurement, keyboard/mouse input
  - `DocsCanvas.VisualMode.cs` — visual-mode-only logic: cursor navigation over hidden ranges, table cell navigation/hit-testing/rendering, image rendering
  - `DocsCanvas.SourceMode.cs` — source-mode-only logic: source cursor navigation, inline image preview
- **Document** — Testable document model: `List<StringBuilder>` blocks, cursor/anchor positions, text mutations (insert, delete, paste), selection, undo/redo, and navigation. No UI dependencies — all tests target this class.
- **MarkdownParser** — Static class that classifies blocks (`BlockKind`: paragraph, H1–H6, list item, task list items, fenced code, table rows) and parses inline styles (`StyledRun`: bold, italic, bold-italic, code, link). DocsCanvas calls this to drive styled rendering; Document knows nothing about markdown.
- **BlockVisualMap** — Computes hidden ranges for visual mode (markdown syntax characters hidden from display). Used for cursor skip logic and display string building.
- **DocsEditor** — `UserControl` wrapping `DocsCanvas` + `DocsFormattingBar` into a single drop-in control. Exposes `ShowToolbar`, `Theme`, `IsDirty`, `DocumentBasePath` dependency properties, state persistence via `GetState`/`ApplyState` with `DocsEditorState`.

### Key dependencies

- **Raisin.WPF.Base** — shared base library via project reference (SmoothScroller, layout helpers)
- **AvalonDock** — docking framework with VS2013 dark theme (test app only)

### Design document

`design/RaisinDocs design v01.md` — iteration plan with completed/future milestones. Update status markers when completing iterations.
