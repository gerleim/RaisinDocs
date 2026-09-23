# Visual Selection Markup

Status: **done** 2026-09-23. Copy, delete and paste of a visual-mode selection keep hidden markup
balanced. Source mode is unchanged: its markers are visible, so it copies, deletes and pastes
exactly the characters selected.

## The problem

Visual mode hides markers (`**`, `*`, `~~`, backticks, link brackets and URLs, colour tags,
block prefixes), and the caret never stops inside them. So a selection's ends sit on one side or
the other of a hidden marker, depending on how the caret got there: Shift+End stops before a
closing `**`, Shift+Right steps past it. Every operation took the raw range between the ends:

| Text | Selected (as seen) | Copied before |
|---|---|---|
| `2*3=6 **x**` | whole line | `2*3=6 **x` |
| `a **b** c` | `a ` | `a **` |
| `a **b** c` | `b` | `b**` |
| `# Title *it*` | whole line | `Title *it` |
| `**whole**` | `who` | `who` - bold on screen, plain on paste |

Deletion had the same halves (`a ** c`), and pasting `**who**` inside a bold run closed the
bold instead of adding to it (`**whol**who**e**` shows "who" plain).

Editors that store formatting on the characters (Word, Google Docs, Notion, Typora, ProseMirror)
never have this: markers are written on output, around whatever was selected. Source editors that
hide syntax (Obsidian Live Preview) avoid it differently, by revealing the markers the selection
touches. RaisinDocs stores raw text like the second group but never reveals markers, like the
first - so it follows the first group's behaviour.

## The rules

All three live in `VisualSelection`, which finds each block's paired constructs
(`InlineConstruct`: emphasis and strikethrough pairs, code spans, links, colour tags) at the same
ranges `BlockVisualMap` hides. Emphasis pairing relies on `ParsedBlock.EmphasisMarkers` listing
each opener immediately before its closer.

**Copy** (`BuildCopyText`) - markers follow the selected content:
- a construct with selected visible content brings both its markers, wherever they are;
- a construct without selected content brings none, even markers inside the range;
- a selection reaching a block's first or last visible character takes the hidden markup at that
  edge: `# `, `- `, `> `, a leading `**`, a trailing `**`.

So `who` from `**whole**` copies `**who**`; `ol` from `**bold**` copies `**ol**`.

**Delete** (`PlanDeletion`, applied by `DocsCanvas.DeleteSelectedContent`) - used by Backspace,
Delete, Cut, Enter, paste over a selection and typing over one:
- a construct whose visible content is all deleted goes with its markers;
- any other construct keeps its markers, even those inside the selection;
- the caret goes where the first deleted character was.

Typing over a selection keeps the formatting of the first selected character, as Word does: the
constructs around it count as kept, so the typed text lands inside them (`a **b** c`, type over
"b" → `a **X** c`).

**Paste** (`AdaptPasteToCaret`) - a single-line paste drops its markers for formatting already in
force at the caret: bold into bold, italic into italic, strikethrough into strikethrough, a colour
into the same colour. `**who**` between "l" and "e" of `**whole**` gives `**wholwhoe**`. Code
spans and links are left alone.

## Known gaps

- **Multi-line paste inside a construct** is not adapted: splitting a bold run across lines breaks
  it whatever the pasted markers do, as Enter inside it does. Fixing that means closing and
  reopening constructs at the split, for Enter and paste alike.
- **Custom colour names** from a theme definition resolve in the document but not in pasted text,
  so a pasted tag using one is kept even inside the same colour.
- **Joined edges**: deleting across lines can butt a kept closer against a kept opener
  (`**a***d*`). CommonMark still reads it as bold then italic.

## Tables

A selection spanning two cells or two rows of a table is a rectangle (`TryGetTableRectSelection`):
copy writes whole cells as a markdown table plus an HTML `<table>`, and delete clears cells. A
selection inside one cell is ordinary text and follows the rules above, except that the line-edge
rule is skipped - a row's edges are pipes and padding, not the cell's markup.
