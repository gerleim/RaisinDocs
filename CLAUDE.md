# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Commits

Do not append `Co-Authored-By` trailers to commit messages.

## Build Commands

**IMPORTANT:** Use the safe build wrapper to avoid stray process accumulation:

```bash
# Safe build (recommended - cleans up orphaned processes)
.\build-safe.ps1 -Command build

# Safe test
.\build-safe.ps1 -Command test

# Safe clean
.\build-safe.ps1 -Command clean
```

**Why the safe wrapper?** The .NET SDK spawns background compiler processes (VBCSCompiler) and worker processes that don't always exit cleanly. Over repeated builds, they accumulate and lock NuGet cache files, causing "Microsoft.WinFX.targets" copy errors. The wrapper detects and terminates orphaned processes after each build.

Legacy commands (not recommended, may accumulate stray processes):

```bash
# Build
dotnet build RaisinDocs.slnx

# Test (xUnit)
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj

# Run a single test
dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# UI tests
dotnet test Tests/RaisinDocs.Tests.UI/RaisinDocs.Tests.UI.csproj

# Run editor
dotnet run --project RaisinDocs.Editor/RaisinDocs.Editor.csproj
dotnet run --project RaisinDocs.Editor/RaisinDocs.Editor.csproj -- path/to/file.md

# Run viewer
dotnet run --project RaisinDocs.Viewer/RaisinDocs.Viewer.csproj
dotnet run --project RaisinDocs.Viewer/RaisinDocs.Viewer.csproj -- path/to/file.md

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
- **RaisinDocs.Editor** — Standalone tabbed markdown editor app. Dark theme, session persistence, File menu (New/Open/Save/SaveAs/Close). Accepts a file path as command-line argument.
- **RaisinDocs.Viewer** — Read-only markdown viewer app. DarkBlue theme, visual mode only, minimap enabled. Accepts a file path as command-line argument.
- **RaisinDocs.TestApp** — WPF app hosting DocsCanvas in AvalonDock with dark theme (development sandbox)
- **Tests/RaisinDocs.Tests** — xUnit v3 + FluentAssertions tests for the Document model
- **Tests/RaisinDocs.Tests.UI** — xUnit UI tests (DocsCanvas rendering/layout)

### Key classes

- **DocsCanvas** — Custom `FrameworkElement` handling rendering, input, scrolling, selection, and layout. Renders text via `FormattedText`/`GlyphTypeface` with viewport culling and smooth scrolling. Owns the `Document` instance and delegates all text mutations to it. Split across partial classes:
  - `DocsCanvas.cs` — core rendering (`OnRender`), layout, measurement, keyboard/mouse input
  - `DocsCanvas.Formatting.cs` — formatting API: inline style toggles, block prefix toggles, insert link/table/color, reflow, formatting query properties
  - `DocsCanvas.VisualMode.cs` — visual-mode-only logic: cursor navigation over hidden ranges, table cell navigation/hit-testing/rendering, image rendering
  - `DocsCanvas.SourceMode.cs` — source-mode-only logic: source cursor navigation, inline image preview
- **Document** — Testable document model: `List<StringBuilder>` blocks, cursor/anchor positions, text mutations (insert, delete, paste), selection, undo/redo, and navigation. No UI dependencies — all tests target this class.
- **MarkdownParser** — Static class that classifies blocks (`BlockKind`: paragraph, H1–H6, list item, task list items, fenced code, table rows) and parses inline styles (`StyledRun`: bold, italic, bold-italic, code, link). DocsCanvas calls this to drive styled rendering; Document knows nothing about markdown.
- **BlockVisualMap** — Computes hidden ranges for visual mode (markdown syntax characters hidden from display). Used for cursor skip logic and display string building.
- **DocsEditor** — `UserControl` wrapping `DocsCanvas` + `DocsFormattingBar` into a single drop-in control. Exposes `ShowToolbar`, `Theme`, `IsDirty`, `DocumentBasePath`, `ShowMinimap` dependency properties, state persistence via `GetState`/`ApplyState` with `DocsEditorState`.
- **IDocsLogger** — Logging interface for host apps. Implement and assign to `DocsCanvas.Logger` to receive warnings/errors (e.g. clipboard retry failures). Keeps the library free of logging dependencies — each host app routes to its own logging infrastructure.
- **RetryHelper** — Generic retry utility (`internal`). Configurable retries, delay, and per-retry callback. Used by `ClipboardHelper` for transient OS failures.
- **ClipboardHelper** — Wraps `Clipboard.SetText`/`GetText` with retry-on-`ExternalException` and `IDocsLogger` integration (`internal`).

### Data flow and rendering pipeline

The render pipeline is: **Document → MarkdownParser → BlockVisualMap → DocsCanvas.OnRender**.

1. **Document** stores raw text as `List<StringBuilder>` blocks (one per line). It knows nothing about markdown — only text, cursor, and undo.
2. **MarkdownParser.Parse()** is called during `ComputeLayout()`. It classifies each block into a `BlockKind` and produces `StyledRun` lists (bold, italic, code, etc.), `InlineImage`/`InlineLink` lists, and `ColorSpan` lists. A post-pass (`DetectTables`) groups adjacent pipe-delimited blocks into table structures with `TableInfo`/`TableRowInfo`.
3. **BlockVisualMap.Compute()** (visual mode only) builds hidden ranges from the parsed block — style markers (`**`, `~~`), heading prefixes, color tags, image syntax, link URL portions. It provides `BuildDisplayString` (strips hidden chars), `RawToVisual`/`VisualToRaw` (offset mapping), and `IsHidden`/`SkipHidden` (cursor navigation).
4. **DocsCanvas.OnRender** draws visible lines using `FormattedText`. In source mode it renders raw text with syntax dimming; in visual mode it uses the display string from `BlockVisualMap`. Tables, images, and selection are drawn as separate passes.

Key invariant: `Document` never depends on `MarkdownParser` or `BlockVisualMap`. All markdown awareness flows one way — from parser output into the rendering/navigation layer.

### DocsCanvas functional areas (~5400 lines across 4 partials)

The partial class is split by edit mode and by concern. All files share the same fields. The major functional areas within DocsCanvas are:

- **Layout** (`ComputeLayout`, `ComputeLayoutCore`, `WrapSegment`, `FitLine`, `BuildParagraphGroups`) — word wrapping, visual line computation, paragraph group joining for soft breaks
- **Rendering** (`OnRender`, `DrawJoinedLine`, `ApplyInlineStyles`, `ApplyColorSpans`, `ApplySyntaxDimming`, `DrawSelection`, background drawing methods) — all drawing happens here
- **Text measurement** (`MeasureCharWidth`, `MeasureStringWidth`, `MeasureRangeWidth`, `GetLineHeight`, glyph/typeface management, `_charWidthCache`)
- **Input handling** (`OnKeyDown`, `OnTextInput`, `OnMouseDown/Move/Up`, `Handle*` key dispatch methods)
- **Cursor/navigation mapping** (`CursorToVisualLineIndex`, `CursorXInVisualLine`, `HitTestVisualLine`, `HitTestToPosition`, `SetCursorFromVisualLine`)
- **Formatting API** (in Formatting.cs: `ToggleBold/Italic/Code/Strikethrough`, `ToggleInlineStyle`, `ToggleHeading`, `ToggleBlockPrefixForSelection`, `ToggleFencedCode`, `InsertLink`, `InsertTable`, formatting query properties)
- **Link popup** (in `LinkPopupController.cs`: `Show`, `Build`, `Close`, `Cancel`)
- **Table rendering/navigation** (in VisualMode.cs: `DrawTableRow`, `ComputeAllTableColumnWidths`, `CursorXInTableRow`, `HitTestInTableRow`, `HandleTableArrow`, rectangular selection)
- **Visual-mode cursor skipping** (in VisualMode.cs: `SkipCursorOverHiddenRanges`, `EnsureCursorOnVisibleBlock`, `ClampCursorBeforeTrailingHidden`)
- **Scrolling** (`ClampScroll`, `EnsureCursorVisible`, `OnMouseWheel`, smooth scroll via `SmoothScroller`)
- **Test hooks** (`internal` Test* methods/properties for UI tests)

### ParsedBlock (record class)

`ParsedBlock` is a `record class` with `init`-only properties. Use `with` expressions to create modified copies — never clone field-by-field. This prevents bugs when new properties are added.

### Host integration

Host apps configure the editor through `DocsEditor` (preferred) or `DocsCanvas` directly:

- **Settings** — `Theme` (`Light`/`Dark`/`DarkBlue`), `EditMode` (`Source`/`Visual`), `ImagePreview` (`Off`/`Inline`/`OnHover`), `SoftBreak` (`Relaxed`/`Strict`), `HardBreak` (`Backslash`/`TrailingSpaces`), `ShowWhitespace`, `ShowToolbar`, `ShowMinimap`, `DocumentBasePath`
- **State persistence** — `DocsEditor.GetState()` / `ApplyState(DocsEditorState)` serializes all settings above for save/restore
- **Logging** — Set `DocsCanvas.Logger` to an `IDocsLogger` implementation to receive library warnings/errors
- **Events** — `ContentChanged`, `IsDirtyChanged`, `ThemeChanged`, `EditModeChanged`, `FormattingChanged`

### Inline color tags

The editor supports custom color tags embedded as HTML comments (invisible in standard markdown renderers):

- **Inline**: `<!--@fg:red-->text<!--/@fg-->` — colors a span of text
- **Block div**: `<!--@div fg:red-->` / `<!--/@div-->` — colors all blocks between the tags

`MarkdownParser.ParseInlineColorTags` produces `ColorSpan` lists (stored on `ParsedBlock`). `BlockVisualMap` hides the tag syntax in visual mode. `ApplyColorSpans`/`ApplyColorSpansVisual` apply `SolidColorBrush` to `FormattedText` ranges. `HtmlColorParser` handles the HTML comment parsing and color name resolution.

### Test architecture

- **RaisinDocs.Tests** — Pure model tests. Target `Document` and `MarkdownParser` directly. No UI thread needed. Fast.
- **RaisinDocs.Tests.UI** — Test `DocsCanvas` via `internal` test hooks (`TestSetCursor`, `TestInsert`, `TestNavigate`, `TestComputeLayout`). These need a WPF dispatcher — xUnit runs them on an STA thread.
- Add new parser/document logic tests to `RaisinDocs.Tests`. Only use `RaisinDocs.Tests.UI` when testing cursor behavior, rendering, or navigation that depends on layout.

### Key dependencies

- **Raisin.WPF.Base** — shared base library via project reference (SmoothScroller, layout helpers)
- **AvalonDock** — docking framework with VS2013 dark theme (test app only)

### Design document

`design/RaisinDocs design v01.md` — iteration plan with completed/future milestones. Update status markers when completing iterations.
