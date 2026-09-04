# Typing Performance

Status: **done**, merged to `main` as `d2a75a1`. Written 2026-09-04.

Typing in a large document cost about 78 ms a character inside `ComputeLayout`. It now costs
3.3 ms in prose and 13.9 ms inside a fenced code block.

## The problem

Typing raises `ContentChanged`, which calls `InvalidateLayout` (`DocsCanvas.cs:868`), which
nulls the parse and marks layout dirty. `ComputeLayout` then redoes every stage over the
**whole document**: parse, the visual block structure, a `BlockVisualMap` per block, and
wrapping. None of it is incremental, and all of it runs before the character appears.

That design is defensible for a theme switch or a zoom. It is not for a keystroke.

## How it was found, including the wrong turn

Reasoning about it produced the wrong answer twice, and both times measurement corrected it.

**First wrong turn: the visual cache.** The opaque line visuals work
(`design/Opaque Line Visuals.md`) had just made cached line bitmaps wider, and the obvious
story was that typing re-rasterises every visible line. It does - `EnsureLineVisualCache`
drops the lot on any `RenderVersion` bump - but that is microseconds against what layout was
costing, and fixing it would have shaved nothing off a 78 ms keystroke.

**Second wrong turn: a synthetic benchmark.** A generated 2895-block document put
`VisualBlockStructure.Build` and the visual maps at roughly 55% of the cost, which pointed at
the visual-mode stages. On the real document they are **3%**. The harness had called
`MarkdownParser.Parse(getText, blockCount)` - the two-argument overload, which passes a null
highlighter - so it measured a parse the editor never performs.

The lesson is the reason `LayoutDiag` exists: measure the document someone is actually typing
in, not one you generated to look like it.

## What it was actually spending

`design/HTML to Markdown Semantic Block Model.md`: 1119 blocks, 1079 visual lines, 19 fenced
code blocks in `csharp`, `html` and `markdown`.

| stage | ms | share |
|---|---|---|
| **parse** | **75.9** | **97%** |
| maps | 1.2 | 1.5% |
| wrap | 1.2 | 1.5% |
| structure | 0.1 | - |
| merge, clamp | 0.0 | - |
| **total** | **78.4** | |

Splitting the parse showed where it went:

| | ms |
|---|---|
| parse without the highlighter | 5.4 |
| parse with it | **68.7** |

`ApplySyntaxHighlighting` (`MarkdownParser.cs:465`) sits in the unconditional pipeline. It
walks every block, finds every fenced block with a language, and hands its lines to TextMate -
on every parse, and therefore on every keystroke, **whether or not the caret is anywhere near
code**. There is no change detection on that path at all. The benchmark re-parsed the same
unedited text twenty times and paid 68.7 ms each time.

## The fix

**Cache a code block's tokens on its own text** (`SyntaxHighlighter.Tokenize`). A block's
tokens depend only on its language and its own lines, because TextMate's rule stack is reset
per block and nothing outside it can reach in. The key is therefore the whole of the input,
which makes the cache content-addressed and unable to go stale: an edited block is a different
key, not a wrong hit.

- `SetTheme` clears it alongside the grammar cache, since tokens carry resolved colours.
- Bounded at 256 entries and cleared wholesale on overflow. Typing inside a code block mints a
  key per keystroke, so it has to be bounded; the cap sits well above the number of blocks in a
  document, so a clear only follows a long editing run and costs one re-tokenised parse.

**Also fixed, and worth no credit here:** `ComputeLayout` re-parsed the whole document after
merging lazy paragraph continuations, unconditionally, whether or not the merge had moved
anything. `MergeParagraphContinuations` reports that now and the rebuild is conditional. On a
synthetic document that looked like half the cost. On the real one `merge` is 0.0 ms and
`re-parsed` is 0 on every logged line - this document has no lazy continuations, so the second
parse was never firing. The fix is correct and will matter on documents that use them. It did
nothing for the measurement below.

## The result

Same document, same stages, so parse is the only variable:

| stage | before | after, prose | after, inside a code block |
|---|---|---|---|
| parse | **75.9** | **0.9** | **11.5** |
| structure | 0.1 | 0.1 | 0.1 |
| maps | 1.2 | 1.1 | 1.3 |
| wrap | 1.2 | 1.2 | 1.1 |
| **total** | **78.4 ms** | **3.3 ms** | **13.9 ms** |

24x in prose, 5.6x inside a code block. The two regimes are visible in the log as they should
be: with the caret in prose all 19 blocks hit the cache; inside a fenced block that one block
re-tokenises each keystroke and the other 18 hit.

## Where the time goes now

In prose there is no dominant term left - parse, `maps` and `wrap` are all about 1 ms. That is
a reasonable place to stop.

Inside a code block parse is still 11.5 of 13.9 ms, 83%. If that becomes annoying the move is
to defer tokenising the block being edited rather than to cache it harder, since by definition
its text is new on every keystroke.

Beyond that, layout remains O(whole document) per character in every stage, which is fine at
1100 blocks now that the constant is small and would stop being fine on a much larger one.
Incremental layout - re-parsing and re-wrapping only from the edited block down - is the real
answer and has not been attempted.

## The diagnostic

`LayoutDiag` times each stage and writes one line per twenty passes to
`%LOCALAPPDATA%\RaisinDocs\layout.log`, with the average and worst of each.

    dotnet run --project RaisinDocs.Editor\RaisinDocs.Editor.csproj -- --layout-diag file.md

`DocsCanvas.LayoutDiagnostics` is the way in for a host, and unlike the scroll log it can be
switched on at any point, since nothing is wired up in the constructor.

Two decisions worth keeping if it is ever extended. It aggregates rather than logging per
pass, because a keystroke already costs too much to add a synchronous file write to it. And it
takes timestamps rather than an `Action` per stage - wrapping each stage in a lambda would
allocate a closure per stage per keystroke to measure a cost being blamed for the keystroke,
which is what the scroll diagnostics had to be rebuilt to stop doing (`9783882`).

## Things that will bite

- **`ApplySyntaxHighlighting` still walks every block on every parse.** The cache removes the
  tokenising, not the walk. It is cheap, but it is still O(document).
- **The cache key is built per block per parse**, joining the block's lines into a string. That
  is a few KB of allocation a keystroke, against 63 ms saved. If it ever shows up, a hash would
  replace it - at the cost of the collision safety the full key gives for free.
- **The first parse after a launch is dear** - 437 ms observed, from JIT and TextMate grammar
  loading. It is once per process and not worth chasing, but it will distort any measurement
  that includes the first twenty passes.
