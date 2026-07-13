# Code Review — Changes since 2026-07-01

**Date**: 2026-07-12
**Scope**: 97 commits, ~11,200 lines added across 39 files. Key areas: DocsCanvas.Input.cs (new partial), Document.cs, DocsCanvas.VisualMode.cs, HtmlColorParser.cs, DocsFormattingBar.cs, RetryHelper.cs, iteration 19 (indentation awareness), iteration 16 (indented code blocks), ordered list renumbering, table paste, toolbar overflow.
**Findings**: 6 Confirmed Correctness, 1 Plausible Correctness, 3 Confirmed Cleanup

---

## CRITICAL (1)

### ~~C1 — HandleEnter crashes on indented ordered list items~~ FIXED

- **Severity**: Critical
- **Category**: Bug / Crash
- **Location**: `DocsCanvas.Input.cs:569` (same root cause at lines 558, 560)
- **What's wrong**: `GetOrderedListPrefixLength` returns 0 for text with leading spaces (e.g. `"  1. item"`) because it expects the raw text to start with a digit. `HandleEnter` then calls `blockText.Substring(0, prefixLen - 2)` which evaluates to `Substring(0, -2)` and throws `ArgumentOutOfRangeException`.
- **Repro**: Create an indented ordered list item (1–3 leading spaces) and press Enter.

---

## HIGH (4)

### ~~H1 — SplitInlineColorDivs leaves orphaned close tag on same-line pairs~~ FIXED

- **Severity**: High
- **Category**: Bug
- **Location**: `Document.cs:573`
- **What's wrong**: When open and close color tags are on the same line, line 573 overwrites the close-tag removal done at line 564. The `after` variable (text from openEnd onward) still includes the close tag. Result: the converted output contains `content<!--/@fg-->` with an orphaned close tag that corrupts subsequent color rendering.
- **Repro**: Select text containing a same-line inline color pair like `<!--@fg:red-->content<!--/@fg-->` and trigger Reflow.

### ~~H2 — Reflow operates on stale block range after SplitInlineColorDivs~~ FIXED

- **Severity**: High
- **Category**: Bug
- **Location**: `DocsCanvas.cs:500`
- **What's wrong**: After `SplitInlineColorDivs` inserts new blocks (increasing block count), `eb` is not updated to the new end block. The subsequent Reflow merge-paragraphs pass does not cover the content block that was split out, so the paragraph remains unwrapped.
- **Repro**: Select text containing inline color tags and invoke Reformat. The paragraph stays unwrapped despite the reformat action.

### ~~H3 — TryPasteIntoTableCells cursor offset wrong after multi-cell paste~~ FIXED

- **Severity**: High
- **Category**: Bug
- **Location**: `DocsCanvas.VisualMode.cs:766`
- **What's wrong**: The right-to-left loop captures `lastOffset` from the rightmost cell first. When left cells are then modified with different-length replacements, all right-side positions shift, but `lastOffset` is never updated. The cursor ends up at a wrong position — potentially inside a pipe delimiter or in adjacent cell content.
- **Repro**: Paste pipe-delimited table rows where replacement cell content differs in length from original cells.

### ~~H4 — GetBlockPrefix doesn't strip whitespace before ordered-list detection~~ FIXED

- **Severity**: High
- **Category**: Bug
- **Location**: `Document.cs:341`
- **What's wrong**: `GetBlockPrefix` does not strip leading whitespace before calling `GetOrderedListPrefixLength`. Indented ordered list items like `"  1. text"` aren't recognized, so `ToggleBlockPrefix` prepends `1. ` producing `"1.   1. text"` — a corrupted double-prefix line.
- **Repro**: Select an indented ordered list item and click the ordered-list toggle button.

---

## MEDIUM (2)

### ~~M1 — DecodeEntity truncates Unicode codepoints above U+FFFF~~ FIXED

- **Severity**: Medium
- **Category**: Bug
- **Location**: `HtmlColorParser.cs:327`
- **What's wrong**: `(char)codePoint` truncates 32-bit values to 16 bits. Numeric character references above U+FFFF (e.g. `&#128512;` for U+1F600 grinning face emoji) produce a wrong character (U+F600) instead of the intended emoji. Should use `char.ConvertFromUtf32()` or emit a surrogate pair.
- **Repro**: Paste HTML from clipboard containing a numeric character reference above U+FFFF.

### ~~M2 — RenumberOrderedList silently fails for indented items~~ FIXED

- **Severity**: Medium
- **Category**: Bug
- **Location**: `DocsCanvas.Input.cs:611`
- **What's wrong**: Same root cause as C1/H4: `GetOrderedListPrefixLength` returns 0 for indented lines, causing immediate `break` in the renumber loop. Following ordered list items are not renumbered after pressing Enter.
- **Repro**: Press Enter in an ordered list where subsequent items have leading indentation. Following items retain stale numbers.

---

## LOW — Cleanup (3)

### L1 — RetryHelper blocks UI thread with Thread.Sleep — SKIPPED

- **Severity**: Low
- **Category**: Performance
- **Location**: `RetryHelper.cs:17`
- **What's wrong**: `ClipboardHelper` is called from `OnKeyDown` (Ctrl+C/V/X) on the WPF dispatcher thread. If another app holds the clipboard, `RetryHelper` sleeps 100ms × 2 retries = 200ms of UI freeze. A `DispatcherTimer` or async retry would keep the UI responsive.
- **Skip reason**: Async retry adds ~30 lines of state management (timer field, attempt counter, extracted paste method, re-entrant edge cases) to avoid a 200ms worst-case freeze that requires active clipboard contention from another process. Not worth the complexity.

### ~~L2 — SolidColorBrush allocated per colored line per frame in OnRender~~ FIXED

- **Severity**: Low
- **Category**: Performance / GC pressure
- **Location**: `DocsCanvas.cs:2358` (also line 2434)
- **What's wrong**: `DrawBlockColorBackgrounds` creates `new SolidColorBrush(Color.FromArgb(40, ...))` per visible colored line on every render frame. With many colored blocks, this generates GC pressure during scrolling. `GetCachedBrush` already exists for foreground colors — the same pattern with an ARGB key would eliminate these allocations.

### L3 — Overflow menu handlers double-trigger UpdateButtonStates

- **Severity**: Low
- **Category**: Redundant work
- **Location**: `DocsFormattingBar.cs:433`
- **What's wrong**: Each overflow menu handler calls `Canvas?.ToggleBold()` (which raises `FormattingChanged` → `OnFormattingChanged` → `UpdateButtonStates`) then also calls `UpdateButtonStates()` explicitly. The expensive `GetReformatActions` and `CanConvertToHardBreaks` logic runs twice per toolbar click.

---

## Common Root Cause

Findings C1, H4, and M2 share the same root cause: **`GetOrderedListPrefixLength` does not handle leading whitespace**. The iteration-19 indentation awareness work introduced 0–3 space prefixes for block markers, but `GetOrderedListPrefixLength` still expects text to start with a digit. A single fix (stripping leading whitespace before the prefix-length check, or making the method whitespace-aware) would resolve all three.

---

## Review Stats

| Metric | Value |
|--------|-------|
| Effort level | High |
| Finder agents | 4 |
| Candidates found | 22 |
| Verifier agents | 21 |
| Confirmed | 10 |
| Refuted | 3 |
| Reported | 10 |
