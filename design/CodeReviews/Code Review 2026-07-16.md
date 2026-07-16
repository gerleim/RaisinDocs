# Code Review — Changes since 2026-07-12

**Scope:** 18 commits (`cb90615..HEAD`), ~3,900 lines of production code across 17 files.
**Features reviewed:** Tab indent/outdent (iter 18), Enter auto-continuation (iter 18), angle-bracket autolinks (iter 17), formatting bar keyboard nav (Alt/F6), Find & Replace (iter 20), syntax highlighting (iter 21), formatting extraction refactor, minimap icon states, misc bug fixes.
**Build:** Clean (0 warnings). **Tests:** 767 passed, 0 failed.

---

## Findings

### ~~C1 — SplitInlineColorDivs drops text after same-block close tag~~
- **Status:** [x] False positive

`FindInlineColorCloseStart` (MarkdownParser.cs:1731) requires `close + 3 == text.Length` — the close tag must be at the very end of the block text. Trailing text after the close tag causes the function to return -1, so the same-block match scenario described here cannot occur.

### ~~C2 — GetFenceInfo uses full info string as language~~
- **File:** `MarkdownParser.cs:878–879`
- **Severity:** Low — affects uncommon syntax
- **Status:** [x] Fixed

`GetFenceInfo` returns the entire trimmed info string as the language identifier. Per CommonMark §4.5, only the first word of the info string is typically used as the language. A fence like `` ```csharp highlight `` would produce language `"csharp highlight"`, which won't match in `MapLanguageToExtension` and silently skip highlighting.

Fix: `var lang = infoString.Trim().Split(' ', 2)[0];`.

### ~~C3 — SyntaxHighlighter bare catch blocks~~
- **File:** `SyntaxHighlighter.cs:128, 140`
- **Severity:** Low — defensive but overly broad
- **Status:** [x] Fixed

`GetGrammar` uses bare `catch { }` which swallows all exception types including `OutOfMemoryException`. Changed to `catch (Exception)` to let fatal CLR exceptions propagate.

### ~~L1 — Minor allocation in GetIndentStep~~
- **File:** `DocsCanvas.Input.cs:804`
- **Severity:** Low — unnecessary intermediate allocation
- **Status:** [x] Fixed

`text.AsSpan().TrimStart().ToString()` creates an intermediate `ReadOnlySpan<char>` before calling `.ToString()`. Simplified to `text.TrimStart()`.

---

## Test coverage gaps

### T1 — Enter auto-continuation for lists/blockquotes
- **Status:** [ ] Open

`HandleEnter` auto-continuation logic for bullet lists, task lists, and blockquotes (`DocsCanvas.Input.cs:581–612`) has no test coverage. Key scenarios:
- Pressing Enter on `- item` should produce `- ` on new line
- Pressing Enter on `- [ ] task` should produce `- [ ] ` on new line
- Pressing Enter on empty prefix (e.g., `- ` with no content) should clear the prefix
- Pressing Enter on `> quote` should produce `> ` on new line
- Indented list items should preserve indentation

### ~~T2 — SplitInlineColorDivs same-block close with trailing text~~
- **Status:** [x] N/A — C1 was a false positive

---

## Notes — code quality observations (no action needed)

- **Formatting extraction refactor** (`DocsCanvas.cs` → `DocsCanvas.Formatting.cs`): Clean mechanical move. `ToggleBlockPrefixForSelection` now correctly adjusts selection to cover full modified lines — an improvement over the original.
- **ToggleOrderedList** rewritten to number lines sequentially and strip numbered prefixes on toggle-off — clear improvement over the old `ToggleBlockPrefixForSelection("1. ")`.
- **Find & Replace** architecture: Clean separation between `FindBarController` (UI) and `DocsCanvas.Find.cs` (search/replace logic). Debounced search, proper undo grouping, reverse-order ReplaceAll.
- **Syntax highlighting**: Smart caching (grammar cache, brush cache), proper tokenizer state threading for multi-line constructs, timeouts on tokenization.
- **Keyboard navigation**: Proper focus management with `Focusable` toggling, Home/End/Tab/Escape support, Ctrl+Z/Y forwarding, overflow-aware button skipping.
- **GetContentColumn refactor**: Correctly implements CommonMark §5.3 list item content column rules with proper whitespace-after-marker counting.
