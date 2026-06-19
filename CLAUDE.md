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

### Key classes

- **DocsCanvas** — Custom `FrameworkElement` handling rendering, input, scrolling, selection, and layout. Renders text via `FormattedText`/`GlyphTypeface` with viewport culling and smooth scrolling.
- **Document** — Testable document model: `List<StringBuilder>` blocks, cursor/anchor positions, text mutations (insert, delete, paste), selection, and navigation. No UI dependencies.

### Key dependencies

- **Raisin.WPF.Base** — shared base library via project reference (SmoothScroller, layout helpers)
- **AvalonDock** — docking framework with VS2013 dark theme (test app only)
