# Hard line break handling

## Context

A **hard line break** is a line that ends in `\` or in two or more spaces (CommonMark). The paragraph continues on the next line, and the break shows in the rendered output. Shift+Enter inserts one; `DocsCanvas.HardBreakStyle` picks `\` (default) or two spaces.

The pieces that know about it:

- `MarkdownParser.IsTrailingHardBreak(parsed, text)` detects it. It handles the odd/even
  backslash rule, code spans, code blocks and trailing colour close tags (`GetContentEnd`).
- `MarkdownParser.GetTrailingHardBreakStart(parsed, text)` (new, see *Done*) gives the offset
  where the marker starts, or -1.
- `BlockVisualMap` hides the marker in visual mode.
- `ListFormattingHandler` adds it (Shift+Enter) and removes it (`StripTrailingHardBreak`, plain Enter).
- `Document.AddHardBreaks` / `DocsCanvas.ConvertToHardBreaks` add it to a range.

The layout merges a paragraph's lines into **one block** joined by `\n`, in both modes. Two methods share the name: `MarkdownParser.MergeParagraphContinuations` decides what merges (it sets `Children` on the parsed block), and `Document.MergeParagraphContinuations`, called from `LayoutEngine.cs:80`, does the physical join. `Document.SplitStaleContinuations` (`LayoutEngine.cs:67`) undoes a merge that no longer holds. A line that ends in a hard break is supposed to stay its own block. Many items below come from this: a check that only looks at a block's end misses a break inside a merged block.

An audit (2026-09-28) found the items below. Each is marked **verified** when the code path was
re-read by hand, or **audit** when it comes from the audit alone. A review the same day
confirmed A1 and A10 with a throwaway UI test.

## Done

- **Ctrl+B crash.** Unbolding a selection that started on the opening `**` put the anchor at -2,
  and the next Ctrl+B threw. Fixed in `ToggleInlineStyle`.
- **Style toggles treated the marker as selected text.** `**boldtext**\` + Ctrl+B gave
  `****boldtext**\**`; `boldtext\` gave `**boldtext\**`. The selection end is now clamped before
  the marker (`ClampBeforeHardBreak`), in `ToggleInlineStyle` and `SelectionHasStyle`. Both modes.

## How to test

Most of these are one-line reproductions, so each gets a failing test first, then the fix.

- **Model tests (`Tests/RaisinDocs.Tests`)** for `Document`, `MarkdownParser` and
  `HtmlBlockModelParser`: no layout needed. Preferred where the bug lives there.
- **UI tests (`Tests/RaisinDocs.Tests.UI`)** when the path goes through `DocsCanvas`: caret,
  selection, visual maps, key handlers. The pattern already used by `FormattingToggleTests`:
  `SetText`, `TestSetEditMode`, `Measure`/`Arrange`, `TestComputeLayout`, `TestSetCursor` /
  `TestSetSelection`, call the operation, assert `TestGetBlockText` and the caret.
- **Run each UI test in both modes** where the item says *both*. A `[StaTheory]` with the
  `EditMode` as data covers it without duplicating the test.
- **Break oracle.** `RaisinDocs.Html.HtmlEmitter.Render` is CommonMark-conformant, so it can say
  what a document *means*. For editing operations that shouldn't change structure (join, reflow,
  toggle, copy/paste round trip), assert that the number of `<br />` in the rendered HTML is the
  same before and after, or equals the expected count. This catches the "the `\` is still there
  but no longer a break" family without spelling out every string.

  The oracle is blind to the editor's own rule in C: it reads `foo\<!--/@fg-->` as an escaped
  `<`, not a break, so it counts zero `<br />` there whatever the editor does. Inputs with a close
  tag after the marker need a block-text assertion instead.
- **Manual check in the Editor** for rendering-only items (B7, and B2/B6 if kept), after closing any running
  instance so it isn't using a stale `RaisinDocs.dll`.

In the tables below, *Source* and *Visual* say where the bug shows. **By design** means the
behaviour there is intended, or at least defensible, and should not change without a decision.

---

## A. Clear bugs — fix

### A1. A hard break on a middle line of a paragraph is lost *(verified)*

| | |
|---|---|
| Where | `MarkdownParser.MergeParagraphContinuations`, `MarkdownParser.cs:1170-1189` |
| Modes | **Both** (the merge is physical). Visible in visual; in source the `\` is no longer at a block end, so everything below that checks the block end misses it |
| Input | `a` / `b\` / `c` |
| Now | The hard-break check (`:1167`) only looks at the first line. `b\` and `c` both merge into `a`. Visual shows `a b\ c` on one line with a visible `\` |
| Expected | Stop taking continuations after a line that ends in a hard break: `a b` merges, `c` starts a new block. CommonMark: one paragraph, `a b<br />c` |
| Also | The parser fix only prevents new merges. A break typed onto a middle line of a paragraph that is *already* merged (`\` or two spaces at the end of `b` in `a\nb\nc`) is never split: `SplitStaleContinuations` pre-filters with `AnyLineIsBlockLevel` (`Document.cs:1152`), which only fires for lines that don't classify as a paragraph, and `b\` does. The pre-filter must also fire when any line but the last ends in a hard break; `MergeStillHolds` then returns false once the parser stops at the break |
| Test | Model: parse + `Document.MergeParagraphContinuations` on the three blocks `a` / `b\` / `c`, expect blocks `a\nb\` and `c`. UI: load `a\nb\nc`, type `\` at the end of `b`, recompute layout, expect the same two blocks. UI: visual line count is 2 |
| Risk | Touches the merge that runs on every layout pass. Check existing continuation tests |

### A2. "Convert to hard breaks" misfires *(verified)*

| | |
|---|---|
| Where | `Document.AddHardBreaks` / `HasSoftBreaks`, `Document.cs:1038-1076`; `DocsCanvas.ConvertToHardBreaks`, `Formatting.cs:211-253` |
| Modes | **Both** (toolbar command) |
| Now | (a) It only compares adjacent blocks and never looks inside a merged paragraph, so on ordinary paragraphs it does nothing and the button stays disabled. (b) With no selection it runs over the whole document without checking the kind of each line: it appends `\` to headings, setext underlines, table rows including the separator, list items and thematic breaks. (c) Raw `EndsWith('\\')`: `foo\\` is treated as already broken; `foo\<!--/@fg-->` gets a second marker |
| Expected | Paragraphs: add a marker before each `\n` join inside the block, adjusting cursor and anchor offsets after each insertion. List items need both checks. The item line never merges with its continuation (both merge loops check `Kind == Paragraph`, and the item isn't one), so that join is between adjacent blocks. But lazy continuation lines keep `Kind == Paragraph` (`MarkdownParser.cs:919-927`), so `- one` / `two` / `three` leaves `two\nthree` as one merged block, whose inner joins are handled as for paragraphs. Use `IsTrailingHardBreak` for "already broken" |
| Test | Model or UI: `# T` / `para` / `| a |` / `|---|` → only the paragraph lines change. `a` / `b` / `c` → `a\` / `b\` / `c`. `- one` / `two` / `three` → `- one\` / `two\` / `three`. `foo\\` / `bar` → gets a marker. Assert on `GetText()`, not on blocks: once A1 lands, adding the markers splits a merged block into one block per line, so the block shape depends on whether A1 is in |

### A3. Reflow joins across a break before a colour close tag *(verified)*

| | |
|---|---|
| Where | `IsMergeableParagraph`, `Formatting.cs:199-209` |
| Modes | **Both** |
| Input | `<!--@fg:red-->hello\<!--/@fg-->` / `bar` |
| Now | The raw `EndsWith` checks miss the break, and `Document.Reflow` joins the lines |
| Expected | Use `MarkdownParser.IsTrailingHardBreak`; the lines stay apart |
| Test | UI (needs the parsed block): reflow, assert both blocks unchanged. Not the oracle: it counts zero breaks here before and after (see *How to test*) |

### A4. Joining two lines leaves the marker mid-line *(verified)*

| | |
|---|---|
| Where | `Document.Backspace` / `Delete`, `Document.cs:305-334`, reached from `VisualModeManager.HandleBackVisual` / `HandleDeleteVisual` |
| Modes | **Visual.** Source: **by design**, the `\` is visible text and the user sees what they join |
| Input | `foo\` / `bar`, Backspace at the start of `bar` (or Delete at the visual end of `foo`) |
| Now | `foo\bar`, with a visible `\`. Before punctuation it becomes an escape: `foo\*x*` loses its italics. Trailing spaces leave a visible double space |
| Expected | In visual mode, drop the marker when joining: `foobar` |
| Test | UI, visual: both keys, both marker styles, assert `foobar` and the caret at 3 |

### A5. Plain Enter strips the wrong marker *(verified)*

| | |
|---|---|
| Where | `ListFormattingHandler.StripTrailingHardBreak`, `ListFormattingHandler.cs:79, 162-176` |
| Modes | **Both** |
| Now | (a) Enter in the middle of `foo\` removes the `\` from the end of the line, which belongs to the second half: you get `fo` / `o`, and `o` soft-joins the next line. (b) It doesn't check the kind of line: Enter at the end of `C:\dir\` inside a fenced code block deletes the backslash from the code; heading `# C:\` loses its backslash. (c) No odd/even check: `C:\\` loses one backslash |
| Expected | Strip only when the caret is at or after the marker, only when `IsTrailingHardBreak` is true (which covers code and escapes), and use `GetTrailingHardBreakStart` |
| Test | UI, both modes: the three inputs above |

### A6. Shift+Enter inside code, and after an escaped backslash *(verified)*

| | |
|---|---|
| Where | `ListFormattingHandler.HandleEnter`, `ListFormattingHandler.cs:43-54` |
| Modes | **Both** |
| Now | The kind comes from `ClassifyBlock` on one line, which has no fence context, so Shift+Enter inside a fenced code block inserts `\` into the code. `EndsWith(marker)` treats `C:\\` as already broken and adds nothing |
| Expected | Inside code: a plain newline. Otherwise: decide with `IsTrailingHardBreak` on the text before the caret |
| Test | UI, both modes: Shift+Enter inside a fence; Shift+Enter after `C:\\` gives `C:\\\` |

### A7. Visual copy picks up the hidden `\` *(verified)*

| | |
|---|---|
| Where | `VisualSelection.BlockSelectionText`, `VisualSelection.cs:147-148` |
| Modes | **Visual only.** Source copy is raw text |
| Now | A selection reaching the last visible character takes the hidden markup after it, which includes the marker. Copying `oo` from `foo\` gives `oo\`; pasting mid-line shows a `\`, pasting at a line end adds a break the user never saw |
| Expected | Stop before the last selected block's marker. Markers between selected lines stay (they are real breaks inside the selection). Clamp only the break marker: other trailing markup still comes along (`ReachingTheLastVisibleCharacter_TakesTrailingMarkup` must keep passing, and `**x**\` copies as `**x**`). `BlockSelectionText` can't do this alone: `BuildCopyText` (`VisualSelection.cs:106-124`) calls it for every selected block, passing middle blocks whole, and it cannot tell the last block from the others. Give it a flag (e.g. `keepTrailingBreak`, true for every block but the last), and when the flag is false clamp before the whole-block early return (`from == 0 && to == text.Length`), which returns `foo\` too |
| Test | Model (`VisualSelectionTests` in `Tests/RaisinDocs.Tests`, next to the existing trailing-markup tests; `VisualSelection` is static and needs no canvas): select `oo` in `foo\`, assert the copy text is `oo`. Selecting all of `foo` gives `foo`. Both go through the existing one-block `Copy` helper. A two-line selection keeps the inner `\`: that goes through `BuildCopyText` and needs a new multi-block helper |

### A8. HTML to markdown puts breaks in the wrong places *(verified: a; audit: b)*

| | |
|---|---|
| Where | `HtmlBlockModelParser.FormatParagraph`, `:1018-1051`; `FormatInlineSegments`, `:1211-1219` |
| Modes | **Both** (paste of HTML, import) |
| Now | (a) A trailing `<br>` (`<p>Line<br></p>`, common from browsers and mail) gives `Line\` as the paragraph's last line, which renders a literal `\`. (b) Headings and list items ignore `<br>`: `<li>one<br>two</li>` gives `- onetwo` |
| Expected | (a) Drop a break that ends the paragraph. (b) List items: a break plus the continuation indent. Headings can't hold a break: a space |
| Test | Model: converter tests for each input |

### A9. Text inserted after the hidden marker *(audit; caret paths not re-verified)*

| | |
|---|---|
| Where | Caret: Ctrl+Right (`CursorNavigationEngine.cs:594-601`), Up/Down with the goal x past the line end, Ctrl+End. Insertions: paste (`InsertPastedText`, `DocsCanvas.cs:1234-1250`), Tab, insert link (`LinkPopupController.cs:65-80, 170`), colour (`ColorFormattingManager.cs:131-136`), Ctrl+B/I/\`/~~ with no selection (`Formatting.cs:544-553`) |
| Modes | **Visual.** Source: **by design**, the caret sits after a visible `\` because the user put it there |
| Now | Only click, drag, typing, End and mode switch call `ClampCursorBeforeTrailingHidden`. From a caret after the `\`: paste gives `foo\pasted`; Tab `foo\    `; link `foo\[t](u)`; colour `foo\<!--@fg:red--><!--/@fg-->` (no longer a break); Ctrl+B `foo\****` |
| Expected | Fix the cause, not each insertion: every visual-mode caret move ends before the trailing hidden marker. A selection-based link must also clamp its end (`[foo\](url)` escapes the bracket) |
| Test | UI, visual: for each navigation key, assert the caret offset is before the marker. One insertion test per operation to lock it in |

### A10. Shift+Enter at a soft break makes a paragraph break *(verified by a probe test)*

| | |
|---|---|
| Where | `ListFormattingHandler.HandleEnter`, `ListFormattingHandler.cs:43-54`; `Document.InsertParagraphBreak` |
| Modes | **Both** |
| Input | `asd` / `123`, which loads as one merged block `asd\n123`; caret after `asd`, Shift+Enter |
| Now | Saves as `asd\` / *(blank)* / `123`. `InsertParagraphBreak` splits before the `\n`, so the second block starts with it and the old soft break becomes a blank line: a paragraph break, with a literal `\` at the end of the first paragraph. Any paragraph written over several source lines hits this |
| Expected | `asd\` / `123`: the new block boundary replaces the soft break, so drop the leading `\n` of the second half. That is the block shape A1 produces. Do it in the Shift+Enter branch of `HandleEnter`, not in `InsertParagraphBreak`. Plain Enter calls `InsertParagraphBreak` twice (`ListFormattingHandler.cs:149-151`), so it would still make a paragraph break without the `\n`, but the `\n` is what gives it the empty caret line: today it saves as `asd` / *(blank)* / *(empty caret line)* / `123`, with the caret on its own line. Dropping the `\n` there would leave the caret at the start of `123` instead |
| Test | UI, both modes, both marker styles: the input above, assert `GetText()` and the caret at the start of `123`. Plain Enter at the same spot is already locked by `EnterKeyTests.Enter_AtEndOfLine_WithNextLine_InsertsNewParagraph`, which must keep passing |

---

## B. Likely bugs — confirm, then fix

### B1. Reformat's trim creates hard breaks *(verified)*

| | |
|---|---|
| Where | `Document.TrimLine`, `Document.cs:839-840` |
| Modes | **Both** |
| Now | `foo\ ` trims to `foo\`: a literal backslash followed by a space becomes a break. Tabs count as trailing whitespace, so `foo\t\t` becomes `foo  `, a break where there was none (CommonMark only counts spaces) |
| Confirm | Oracle: render before and after, compare `<br />` counts |
| Expected | Keep one space after a lone `\` (or drop both), and count only spaces toward the two-space break |

### B2. Visual mode hides `\` where it isn't a break *(audit)*

| | |
|---|---|
| Where | `IsTrailingHardBreak` is local to one block and only excludes code; `BlockVisualMap.cs:345-362` hides on its say-so |
| Modes | **Visual only** |
| Now | The `\` is hidden in ATX/setext headings, on the last line of a paragraph or list item (followed by a blank line, another block or end of document), and in a table row's last cell. CommonMark shows a literal `\` in all of these |
| Confirm | Manual: type each case and compare with `HtmlEmitter` output |
| Decision | Match CommonMark (show it) or keep hiding it as the editor's own convention. Showing it tells the user their file will look wrong elsewhere |

### B3. Heading/prefix toggles leave a stray marker *(audit)*

| | |
|---|---|
| Where | `Document.ToggleBlockPrefix`, `Document.cs:338-363`; `ToggleBlockPrefixForSelection`, `Formatting.cs:406-452` |
| Modes | **Both** |
| Now | Making `foo\` (followed by `bar`) a heading gives `# foo\`. Making the line after `foo\` a list, heading or quote leaves `foo\` as the last line of its paragraph |
| Expected | Heading: drop the marker. Previous line: drop its marker once it is the last line of its paragraph |
| Confirm | Depends on B2: if a paragraph-final `\` is shown, the leftover is visible and clearly wrong |

### B4. HTML paste ignores the hard-break setting *(verified)*

| | |
|---|---|
| Where | `DocsCanvas.cs:1217`, `DocsCanvas.Input.cs:480` |
| Modes | **Both** |
| Now | Both build `new MarkdownOutputSettings { PreserveColors = true }`, so HTML paste always writes `\`, even with `HardBreak = TrailingSpaces` |
| Expected | Pass `HardBreak = _hardBreak` |
| Test | UI: set `TrailingSpaces`, paste `a<br>b`, expect `a  ` / `b` |

### B5. Double-click past the end selects the hidden `\` *(audit)*

| | |
|---|---|
| Where | `HitTestInVisualLine` returns the raw length; `Document.GetWordBoundaries` (`Document.cs:166`) clamps to `len-1`, the marker |
| Modes | **Visual.** Source: the `\` is visible, selecting it is normal |
| Now | Typing then deletes the break. Applies to any trailing hidden markup |
| Test | UI, visual: double-click right of `foo\`, assert the selection is `foo` |

### B6. Emphasis across a Shift+Enter shows literal markers *(audit)*

| | |
|---|---|
| Where | Hard-break lines are never merged, so emphasis spanning one is never paired |
| Modes | **Visual** (rendering). Source shows the markers anyway |
| Now | Shift+Enter inside `**fo|o**` gives `**fo\` / `o**`; visual mode shows the `**`. CommonMark renders it bold |
| Expected | Either pair emphasis across hard breaks, or have Shift+Enter close and reopen the style (`**fo**\` / `**o**`). The second is local and much cheaper |

### B7. Background clip in source mode is one character short *(audit)*

| | |
|---|---|
| Where | `RenderingContext.cs:1936-1937` clips at `GetContentEnd - 1` |
| Modes | **Source** (visual hides the spaces) |
| Now | With a trailing-spaces break, the background covers all but one of the spaces |
| Expected | Clip at `GetTrailingHardBreakStart` |
| Also | The same `GetContentEnd - 1` clip is at `RenderingContext.cs:1333` (foreground, which does no harm on spaces) and `DocsCanvas.cs:1735` (print, deferred). Change `1333` with it so the two live sites agree; leave print |

---

## C. Decisions, not bugs

- **`foo\<!--/@fg-->` counts as a hard break.** That is the editor's rule (`GetContentEnd`, with a
  test), but in CommonMark `\<` escapes the `<`, so other renderers show the tag text and no
  break. Every path that inserts a close tag after the marker produces it (`InsertColorWrapper`
  `:111`, `BackgroundHelper.cs:301/309`). The portable form is `foo<!--/@fg-->\`. Inserting the
  close tag *before* the marker would avoid the question; changing the rule would break existing
  documents.
- **Source mode.** A4, A7, A9 and B5 are marked *by design* in source mode. If source mode should
  also protect the marker, that's a separate decision.

## Suggested order

1. **A9 (caret clamp).** One navigation fix that removes most of the visual-mode insertion bugs.
2. **A1 with A10.** The merge is where many items start, and A10's correct result is the block
   shape A1 produces. A5(a) and A2 behave differently depending on block shape, so their tests
   should be written against the post-A1 structure.
3. **A4, A5, A6, A7.** Everyday editing; small, local fixes.
4. **A3, B4, B7.** One-liners.
5. **A8.** HTML conversion, self-contained.
6. **A2.** Depends on how A1 lands.
7. **B1, B2, B3, B6** once the B2 decision is made.

Out of scope: print (deferred wholesale rework).

## Open questions

1. **B2:** should visual mode show a `\` that CommonMark doesn't treat as a line break?
2. **Source mode:** should it also protect the line-break marker in the four items marked *by design* (A4, A7, A9, B5)?