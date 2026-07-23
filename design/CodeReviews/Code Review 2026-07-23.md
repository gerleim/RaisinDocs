# Code Review — Changes since 2026-07-16

**Scope:** 71 commits (`fab1d0f..HEAD`), ~11,300 lines of production code across 56 files.
**Features reviewed:** HTML emitter (CommonMark conformance), print support, spell checking, table of contents, zoom, file change detection, list nesting, context menus, GFM extensions, drag-and-drop, recent files, session store refactor, editor/viewer menus, many parser conformance fixes.
**Tests:** 867 passed, 0 failed.

---

## Findings

### ~~C1 — Crash: `GetOrderedListPrefixLength` returns past end of string~~
- **File:** `MarkdownParser.cs:1265–1277`, triggered via `DocsCanvas.Input.cs:687–688`
- **Severity:** High
- **Status:** [x] Fixed

`GetOrderedListPrefixLength` returns `i + 2` when the ordered-list delimiter is the last character (no trailing space), even though `i + 2 > text.Length`. For `text = "1."` (length 2): `i=1`, delimiter at `text[1]`, condition `i + 1 >= text.Length` is true, returns `3`. The caller in `HandleEnter` then does `stripped.Substring(prefixLen)` → `ArgumentOutOfRangeException`.

The same unclamped value is consumed by `StripExistingListPrefix` (line 798) and `RenumberOrderedList` (line 814), so any bare `"N."` item in a renumbering chain can crash too.

**Repro:** Type `1.` as the entire line content, press Enter.

**Fix:** Only return `i + 2` when a space actually follows; when the delimiter is at end, return `i + 1`.

### ~~C2 — Crash: `StripExistingListPrefix` on bare unordered marker~~
- **File:** `DocsCanvas.Input.cs:794–803`
- **Severity:** High
- **Status:** [x] Fixed

`StripExistingListPrefix` computes `leading + 2` for `UnorderedListItem` without clamping. A single `-`, `*`, or `+` character (no space) is classified as `UnorderedListItem` by `ClassifyBlock` (the `text.Length == 1` case at MarkdownParser.cs:1192–1201), but `stripLen = 0 + 2 = 2` exceeds the 1-character block → `RemoveTextAt` throws.

This is the same class of bug fixed in commit `a8b1587` for the direct `HandleEnter` path, but `StripExistingListPrefix` was missed.

**Repro:** Type `- item-`, place cursor before the trailing `-`, press Enter. The new block is `"-"` (length 1), gets strip length 2.

**Fix:** Clamp: `Math.Min(stripLen, text.Length)` before `RemoveTextAt`.

### ~~I1 — FileChangeWatcher `Dispose` race → crash or use-after-dispose~~
- **File:** `FileChangeWatcher.cs:83–114`
- **Severity:** High
- **Status:** [x] Fixed

`Dispose()` disposes `_debounceTimer` and `_watcher` but does not set a disposed flag or unsubscribe event handlers. If `OnFileSystemChanged` fires on a ThreadPool thread concurrently with `Dispose`:
- `ScheduleCallback` can call `.Stop()`/`.Start()` on an already-disposed `Timer` → `ObjectDisposedException` on a ThreadPool thread → app crash.
- Or it can create a *new* `Timer` after the old one was disposed (since `_debounceTimer` was nulled by disposal), which fires the callback on a dead watcher.

**Fix:** Add a `_disposed` flag checked under lock in `ScheduleCallback`/`OnDebounceTimerElapsed`. Unsubscribe `Changed`/`Renamed` handlers before disposing.

### ~~I2 — Editor `ReloadFromDisk` unhandled exception → crash~~
- **File:** `RaisinDocs.Editor/MainWindow.xaml.cs:557–573`
- **Severity:** High
- **Status:** [x] Fixed

`ReloadFromDisk` has `try/finally` but no `catch`. If `File.ReadAllText` throws (file locked, permission denied, TOCTOU race with `File.Exists`), the exception propagates through `Dispatcher.Invoke` back to the ThreadPool thread from `FileChangeWatcher`'s timer callback — unhandled exception, crashing the app.

The Viewer's equivalent code (`Viewer/MainWindow.xaml.cs:113–131`) correctly wraps in `try/catch` with logging.

**Fix:** Add `catch (Exception)` with logging, matching the Viewer pattern.

### ~~I2b — FileChangeWatcher misses atomic file replacements~~
- **File:** `FileChangeWatcher.cs`
- **Severity:** High
- **Status:** [x] Fixed

`FileSystemWatcher` cannot detect `File.Move` with overwrite on Windows — a known NTFS/kernel limitation (`ReadDirectoryChangesW` does not decompose atomic rename-replace into separate events for the target filename). Tools that use atomic writes (write temp → rename over original) — including Claude Code and many editors — are invisible to FSW.

**Fix:** Add a 1.5s polling timer that checks `File.GetLastWriteTimeUtc`. FSW still provides instant detection for normal writes; the poll catches atomic replacements. This is the hybrid approach used by VS Code and JetBrains IDEs.

### ~~I3 — SaveToFile triggers own-write detection~~
- **File:** `RaisinDocs.Editor/MainWindow.xaml.cs:475–484`
- **Severity:** Medium
- **Status:** [x] Fixed

`SaveToFile` calls `File.WriteAllText` (line 479) while the tab's existing `FileChangeWatcher` is still active. The watcher's `FileSystemWatcher.Changed` event fires from the app's own write, queuing a 500ms debounce callback. `SetupFileWatcher` (line 484) disposes the old watcher, but if the timer's `Elapsed` event is already queued, the callback can still fire — causing a spurious "modified by another application" dialog or a silent `ReloadFromDisk` that wipes undo history.

**Fix:** Call `_fileWatcher.StopWatching()` (or `EnableRaisingEvents = false`) before writing, then set up the new watcher after.

### ~~I4 — Watcher filter not updated after rename → detection silently stops~~
- **File:** `FileChangeWatcher.cs:35–48, 70–81`
- **Severity:** Medium
- **Status:** [x] Fixed

`_watcher.Filter` is set once in `WatchFile` and never updated after a `Renamed` event. `FileSystemWatcher.Filter` is matched against the current file name, so after a rename, subsequent `Changed` events for the new name no longer match the stale filter. File-change detection silently stops working for that tab.

**Fix:** After handling a rename, update `_watcher.Filter` to the new filename (or call `WatchFile(newPath)` to re-arm fully).

### ~~I5 — `FilePath` set on background thread without Dispatcher~~
- **File:** `RaisinDocs.Editor/MainWindow.xaml.cs:521–525`
- **Severity:** Medium
- **Status:** [x] Fixed

On a `Renamed` event, `FilePath = change.FilePath;` executes directly inside the `FileChangeWatcher` callback on a ThreadPool thread, bypassing the `Dispatcher.Invoke` that wraps the `Modified` branch. `FilePath` is read from the UI thread in `Save_Click`, `CloseTab`, `UpdateTitle`, `SaveSession`, etc.

**Fix:** Move the assignment inside `owner.Dispatcher.Invoke(...)`.

### ~~I6 — Viewer reload closure captures stale path parameter~~
- **File:** `RaisinDocs.Viewer/MainWindow.xaml.cs:104–138`
- **Severity:** Medium
- **Status:** [x] Fixed

The reload closure captures the `filePath` method parameter (line 124: `File.ReadAllText(filePath)`) instead of using `_currentFilePath`. After a `Renamed` event updates `_currentFilePath` (line 112), the closure still reads the old, now-stale path — `FileNotFoundException` on reload (caught and logged, but feature is broken).

**Fix:** Use `_currentFilePath` in the closure.

### C3 — Spell check only invalidates cursor block
- **File:** `DocsCanvas.SpellCheck.cs:97–105`
- **Severity:** Medium

`OnContentChangedForSpellCheck` only adds `_doc.CursorBlock` to `_dirtySpellBlocks`. Multi-block edits (paste, find/replace) that don't change the total block count leave other modified blocks with stale spelling-error offsets — squiggles at wrong positions or missed errors.

**Fix:** Track the actual edited block range (anchor through cursor) and mark all affected blocks dirty.

### C4 — Spell check `DispatcherTimer` never unhooked on teardown
- **File:** `DocsCanvas.SpellCheck.cs:79–95`
- **Severity:** Medium

Once spell check is enabled, the `_spellCheckTimer` is never permanently stopped or unsubscribed. If a `DocsCanvas` instance is closed (e.g. tab closed in Editor) without explicitly disabling spell check, the timer's `Tick` delegate holds a live reference to the entire `DocsCanvas` and its `Document`, `SpellCheckService`, and Hunspell dictionaries — preventing GC for the app's lifetime, compounding per closed tab.

**Fix:** Add a teardown hook (`Unloaded` or explicit `Dispose`) that stops the timer and unhooks `Tick`.

### C5 — `Print()` has no exception safety
- **File:** `DocsCanvas.Print.cs:171–204`
- **Severity:** Medium

If `dialog.PrintDocument` throws (print queue error, driver exception), the cleanup below it never runs: `_visualMaps` stays populated with stale print-only maps, `_layoutDirty` isn't set, and `InvalidateVisual()` is skipped — leaving the canvas with stale layout.

**Fix:** Wrap `dialog.PrintDocument(...)` and cleanup in `try/finally`.

### H1 — Reference links not resolved in table cells
- **File:** `HtmlEmitter.cs:868–876`
- **Severity:** Medium

`AppendTableCellContent` re-parses cell text via `MarkdownParser.Parse(_ => cellText, 1)` without passing `options.LinkDefinitions`. Reference-style links/images inside table cells render as literal text instead of links.

**Fix:** Use `MarkdownParser.ParseInlineContent(cellText, options.LinkDefinitions)`.

### H2 — Reference links not resolved in task list items
- **File:** `HtmlEmitter.cs:763–785`
- **Severity:** Medium

Same root cause as H1: task list item text is parsed with `Parse(_ => content, 1)` and no link definitions.

**Fix:** Same as H1.

### H3 — Mixed task/plain list items produce separate `<ul>` blocks
- **File:** `HtmlEmitter.cs:384–392, 763–785, 1412–1598`
- **Severity:** Medium

Task list items and plain list items are rendered by completely separate code paths. A list mixing both kinds gets split into multiple sibling `<ul>` elements instead of one continuous list. Task list rendering also has no loose/tight handling.

**Fix:** Unify task-list and plain-list item handling — treat task items as unordered items with checkbox content, sharing the same `CollectListItems`/loose-list logic.

### H4 — Heading images not resolved via reference definitions
- **File:** `HtmlEmitter.cs:417–443`
- **Severity:** Medium

Heading inline content is re-parsed without link definitions. The code backfills `Links` only when the inner parse found none (`innerBlock.Links == null`), but `Images` are never backfilled. A heading with `![alt][ref]` silently drops the image.

**Fix:** Parse heading content via `ParseInlineContent(content, options.LinkDefinitions)`.

### H5 — Escaped backslash before line break misclassified as hard break
- **File:** `HtmlEmitter.cs:1145–1182`
- **Severity:** Medium

A line ending in `\\` (escaped backslash = literal backslash) followed by a soft break is misclassified as a hard break. The code checks if the preceding character is `\` without verifying whether that backslash was itself the target of a backslash escape. The literal backslash is also dropped from output.

**Fix:** Check whether the `\` was consumed by `MarkBackslashEscapes` before treating it as a hard-break marker.

### H6 — `StripInlineMarkdown` drops all `*`/`_` in image alt text
- **File:** `HtmlEmitter.cs:1986–1989`
- **Severity:** Medium

Used to build `<img>` `alt` text. Every `*` and `_` character is unconditionally discarded, regardless of whether they're emphasis delimiters or literal punctuation. `![a * b_c](x.png)` produces `alt="a  bc"`.

**Fix:** Only elide matched emphasis delimiters, preserving literal occurrences.

### M1 — O(n²) backward scan in `DetectSetextHeadings`
- **File:** `MarkdownParser.cs:686–697`
- **Severity:** Low (performance)

For every `Paragraph` block, an inner loop walks backward through all preceding blocks to determine `inContainerScope`. A long unbroken paragraph spanning N consecutive lines makes this O(N²).

**Fix:** Track `inContainerScope` incrementally in the forward loop.

### M2 — `GetContentColumnForMarker` hardcodes marker widths
- **File:** `MarkdownParser.cs:895–906` vs correct `GetContentColumn` at `1214–1246`
- **Severity:** Low

`GetContentColumnForMarker` hardcodes marker width as 2 for unordered items instead of scanning actual trailing whitespace (as `GetContentColumn` does). Lists with extra indentation after the marker get the wrong content column in nested-list detection.

**Fix:** Share the whitespace-scanning logic from `GetContentColumn`.

### M3 — `DetectListNesting` drops theme dictionary
- **File:** `MarkdownParser.cs:781–782, 825`
- **Severity:** Low

When an `IndentedCodeLine` is reclassified as a nested list item, `ParseInlineColorTags(text, null)` is called with `null` theme. The `DetectListNesting` method signature never receives `theme` despite it being in scope at the call site (line 380). Theme-defined custom colors on reclassified lines silently fail to resolve.

**Fix:** Thread `theme` through `DetectListNesting`.

### M4 — Spell check word extraction includes leading/trailing quotes
- **File:** `MarkdownParser.cs:3291–3292`
- **Severity:** Low

`IsWordChar` treats `'`/`'` as word characters unconditionally. For `'hello'` (single-quoted word), the extracted token is `'hello'` (8 chars including quotes), which fails dictionary lookup — producing a spurious misspelling squiggle on correctly-spelled words.

**Fix:** Only treat `'`/`'` as a word character when interior to a word (preceded and followed by a letter/digit).

### H7 — `&& false` dead code in `ExpandTabs`
- **File:** `HtmlEmitter.cs:121`
- **Severity:** Low (code quality)

`if (isMarker && !(ch is '>' or '-' or '*' or '+' && false))` — the `&& false` makes the parenthesized expression always false. The condition reduces to just `if (isMarker)`. Reads as leftover/incomplete logic.

**Fix:** Simplify to `if (isMarker)`.

### H8 — `GetStyleAt` linear scan per character
- **File:** `HtmlEmitter.cs:1216–1224`
- **Severity:** Low (performance)

`GetStyleAt` does a `foreach` over `runs` for every character position in `AppendInlineHtml`, giving O(text × runs) per paragraph. Noticeable on paragraphs with many inline style runs.

**Fix:** Keep a monotonically-advancing index into `runs` since they're position-ordered.

### M5 — `CollectDefinitions` called twice in `Parse` overload
- **File:** `MarkdownParser.cs:144–152`
- **Severity:** Low (performance)

The `Parse(getBlockText, blockCount, out linkDefinitions)` overload calls `Parse(...)` internally (which calls `CollectDefinitions`), then calls `CollectDefinitions` again explicitly to return the definitions. Double full-document scan.

**Fix:** Have the inner `Parse` optionally return the definitions it already collected.

### I7 — SyntaxHighlighter re-tokenize doesn't update `ruleStack`
- **File:** `SyntaxHighlighter.cs:39–67`
- **Severity:** Low

When a non-empty line yields zero tokens, it's retokenized with `null` rule stack to recover from corruption. But `ruleStack` (carried to the next line) is never updated to the fresh result — the "corrupted" state keeps propagating forward.

**Fix:** Assign the re-tokenize's returned rule stack to `ruleStack`.

---

## Notes — code quality observations (no action needed)

- **HTML emitter** architecture is well-structured: clear separation between block dispatch, inline rendering, and entity resolution. Entity table is data-only, no logic issues.
- **Print support** (`DocsPaginator`): clean per-page state swap with proper `try/finally` inside `GetPage`. The outer `Print()` method is the only gap (C5 above).
- **TocPanel**: clean extraction, runs on UI thread, no resource or threading issues.
- **MinimapScrollbar**: viewport/image-thumbnail code and `ImageCache.RequestLoad`/`TestInject` run exclusively on the UI thread with no resource-leak issues.
- **Document.cs `SplitInlineColorDivs`** change from `closeBlock == i && closeStart == openEnd` to `closeBlock == i` is a correct fix — it now skips any same-line inline color span, not just empty ones.
- **BlockVisualMap.cs** removing `Strikethrough` from code-marker-hiding is correct — strikethrough markers are now handled via `parsed.EmphasisMarkers`.
- **No scheme allow-list for link URLs** (HtmlEmitter): `javascript:` URIs pass through to emitted `href`/`src`. This matches CommonMark reference behavior, but is worth noting if HTML output is ever rendered in a WebView.
- **Pre-existing**: unterminated `<!--@theme` block causes the rest of the document to be swallowed as hidden `ThemeDefinition` blocks (MarkdownParser `Parse` main loop, lines 257–277, missing a bounds guard). Outside this review's diff, flagged for awareness only.
