# Iteration 11 — Reference-Style Links and Images

## Context

CommonMark 0.31.2 defines reference-style links (`[text][label]`) and images (`![alt][label]`) where the URL is defined elsewhere: `[label]: url "title"`. We claim CommonMark 0.31.2 support but this feature is missing. The existing inline link/image infrastructure handles rendering, hidden ranges, and click-to-open — we only need to add definition collection and reference resolution.

**Goal**: Detect `[label]: url "title"` definitions, resolve `[text][label]` links and `![alt][label]` images, using all existing rendering infrastructure.

## Design

### Reference definition collection (pre-pass)

Add a lightweight pre-pass in `Parse()` before inline parsing. Scan paragraph blocks for lines matching:
```
[label]: destination "optional title"
```

Build a `Dictionary<string, (string Url, string? Title)>` with case-insensitive label keys (per CommonMark spec, first definition wins).

### Reference link forms

| Form | Example | Display text | Lookup key |
|---|---|---|---|
| Full | `[text][label]` | text | label |
| Collapsed | `[label][]` | label | label |

**Not implementing**: Shortcut form `[label]` — too ambiguous (any `[word]` could be a false match).

### Reference image forms

| Form | Example | Alt text | Lookup key |
|---|---|---|---|
| Full | `![alt][label]` | alt | label |
| Collapsed | `![label][]` | label | label |

### How it fits existing infrastructure

- **`InlineLink`/`InlineImage` records** — unchanged. Reference links resolve to the same `(Start, Length, Text, Url, Title)` tuple. The renderer doesn't know or care it was a reference.
- **`BlockVisualMap` hidden ranges** — unchanged. For `[text][label]`, hide `[` and `][label]`. Same pattern as `[text](url)` hiding `[` and `](url)` — the math works because `link.Start + 1 + link.Text.Length` points to `]` in both cases.
- **Rendering, dimming, Ctrl+Click, tooltip** — all unchanged. `InlineStyle.Link` / `InlineStyle.Image` work the same.
- **Link editing popup** — Ctrl+K on a reference link shows the resolved URL. On confirm, replaces with inline `[text](url)` syntax (simpler than trying to update the definition).

### Definition blocks in visual mode

Keep them as paragraphs. In source mode: syntax dimming on `[label]:` part. In visual mode: hide them entirely (mark as `IsSkippedInVisual`). Most users don't want to see `[img1]: ./path/to/image.png` floating in their document.

## Phases

### Phase 1: Definition collection
Add `CollectLinkDefinitions()` in `MarkdownParser.cs`. Called in `Parse()` before the first pass (or as a separate pre-scan). Returns `Dictionary<string, (string Url, string? Title)>`.

Pattern: `^\s{0,3}\[label\]:\s+destination(\s+"title")?$`
- Label: text inside `[...]`, case-insensitive, whitespace-normalized
- Destination: bare URL or `<url>`
- Title: optional `"..."`, `'...'`, or `(...)`

Mark matched blocks as a new `BlockKind.LinkDefinition` so they can be hidden in visual mode.

**File**: `MarkdownParser.cs`

### Phase 2: Pass definitions through parse pipeline
- Add definitions dict parameter to `ParseInlines()` overloads, `MarkLinks()`, `MarkImages()`
- In `Parse()`, call `CollectLinkDefinitions()` first, pass result through

**File**: `MarkdownParser.cs`

### Phase 3: Resolve reference links in `MarkLinks()`
After failing to find `(url)` after `[text]`, check for:
- `[text][label]` — full form: look up `label` in definitions
- `[text][]` — collapsed form: look up `text` in definitions

If definition found, create `InlineLink` with resolved URL and mark as `InlineStyle.Link`.

**File**: `MarkdownParser.cs`

### Phase 4: Resolve reference images in `MarkImages()`
Same pattern as links. After failing to find `(url)` after `![alt]`, check for `[ref]` or `[]`.

**File**: `MarkdownParser.cs`

### Phase 5: Visual mode handling for definitions
- Add `BlockKind.LinkDefinition` to the enum
- Mark definition blocks with this kind in the first pass
- Add to `IsSkippedInVisual` check (like fence delimiters and table separators)
- Source mode: dim the `[label]:` syntax portion

**Files**: `MarkdownParser.cs`, `DocsCanvas.cs` (dimming), `BlockVisualMap.cs` (if needed)

### Phase 6: Tests
- Parser: definition collection, full reference link, collapsed reference link, case-insensitive lookup, undefined label (not linked), full/collapsed reference image, definition with title, multiple definitions, definition in fenced code (ignored)
- BlockVisualMap: reference link hidden ranges match inline link pattern

**Files**: `Tests/RaisinDocs.Tests/MarkdownParserTests.cs`, `Tests/RaisinDocs.Tests/BlockVisualMapTests.cs`

### Phase 7: Design doc update

**File**: `design/RaisinDocs design v01.md`

## Files to modify

- **`RaisinDocs/MarkdownParser.cs`** — `CollectLinkDefinitions()`, `BlockKind.LinkDefinition`, pipe definitions through `ParseInlines`/`MarkLinks`/`MarkImages`, resolve reference forms
- **`RaisinDocs/DocsCanvas.cs`** — syntax dimming for `LinkDefinition` blocks
- **`Tests/RaisinDocs.Tests/MarkdownParserTests.cs`** — reference link/image/definition tests
- **`Tests/RaisinDocs.Tests/BlockVisualMapTests.cs`** — reference link hidden range tests
- **`design/RaisinDocs design v01.md`** — iteration 11 entry

## Verification

1. `dotnet build RaisinDocs.slnx`
2. `dotnet test Tests/RaisinDocs.Tests/RaisinDocs.Tests.csproj` — all tests pass
3. Manual testing in TestApp:
   - `[click here][docs]` + `[docs]: https://example.com` → blue underlined link, Ctrl+Click opens
   - `![screenshot][img1]` + `[img1]: ./image.png` → image rendered
   - `[label][]` collapsed form works
   - Undefined label `[text][missing]` → stays as plain text
   - Definition line dimmed in source mode, hidden in visual mode
   - Ctrl+K on reference link → popup shows resolved URL
