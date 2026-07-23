# Large File Handling

Analysis of how the current architecture handles large files and what changes would be needed to improve it.

## Current Architecture

The system is whole-document-in-memory at every layer:

- **Document** -- `SetText` splits the entire file into `List<StringBuilder>` blocks immediately.
- **MarkdownParser** -- parses every block in one pass. Needs cross-block state for fenced code, tables, link definitions, and themes.
- **Layout (`ComputeLayoutCore`)** -- word-wraps every block to produce Y positions for all visual lines. Runs on every keystroke when content changes.
- **Undo** -- `CaptureSnapshot` copies every block as `string[]`, up to 200 deep (`MaxUndoDepth`).
- **Rendering (`OnRender`)** -- the one layer that is already efficient: viewport-culled, skipping lines above and breaking once past the bottom.

## Why Partial File Reading Doesn't Fit

1. **Raw text is cheap.** A 100K-line file is ~5 MB of strings. The cost isn't reading the file -- it's what happens after (parsing, layout, undo snapshots).
2. **Parsing needs cross-block context.** Fenced code spans, table detection, link/theme definitions, and `ApplyBlockDivColors` all carry state across blocks. You can't parse block 5000 without knowing if block 4998 opened a fenced code block.
3. **Layout needs all blocks** to compute accurate Y positions and scrollbar sizing.
4. **Undo is the real memory concern.** 200 full snapshots of a 100K-line file can reach ~1 GB.

## How Real Editors Handle This

Large-file editors (VS Code, Sublime) read the whole file into memory but defer the expensive per-line work:

- **Lazy parsing** -- only parse/highlight blocks near the viewport, expand outward as the user scrolls.
- **Incremental layout** -- on edit, re-layout only the dirty range, shift everything below by the delta.
- **Diff-based undo** -- store change deltas instead of full document snapshots.

## Improvement Path (Priority Order)

### 1. Incremental Layout

On edits, re-layout only the changed block range instead of all blocks. Biggest bang for the buck since `ComputeLayoutCore` runs on every keystroke via `ComputeLayout`.

- Track a dirty block range (start/end index).
- Recompute visual lines only for dirty blocks.
- Shift Y positions of all subsequent lines by the height delta.
- Affects: `ComputeLayoutCore`, `InvalidateLayout`, `_visualLines`.

### 2. Delta-Based Undo

Store `(blockIndex, oldText, newText)` diffs instead of full document copies. Fixes memory scaling.

- Replace `DocumentSnapshot(string[] Blocks, ...)` with a list of block-level diffs.
- `RestoreSnapshot` applies diffs in reverse instead of replacing all blocks.
- Affects: `Document.CaptureSnapshot`, `RestoreSnapshot`, `DocumentSnapshot` record.

### 3. Viewport-Scoped Parsing

Parse/style only blocks near the visible range, lazily expand as the user scrolls. Hardest due to cross-block markdown state.

- Maintain a "parsed range" window around the viewport.
- On scroll, extend the parsed range incrementally.
- Fenced-code and table state must be tracked as entry state per block boundary.
- Affects: `MarkdownParser.Parse`, `ComputeLayout`, `DocsCanvas.OnRender`.

## Conclusion

Partial file reading is not the right approach. The path forward is incremental layout and delta undo -- but both are significant redesigns, not quick fixes. The current whole-document approach is fine for typical markdown files (under ~10K lines).
