# Opaque Line Visuals

Status: **scoped**, not started. Written 2026-09-03, on branch `opaque-line-visuals`.

Restores ClearType to the editor's text, which has been greyscale antialiased since
`337e009` without anyone intending it.

## The problem

`337e009` moved every visual line into its own `DrawingVisual` with a `BitmapCache`
(`RenderingContext.cs:270`). That bought the caching win it was after - 140 displayed
frames a second against 119, cost per line from 28.9 µs to 8.6 - and quietly cost
something else.

A `BitmapCache` rasterises into an offscreen Pbgra32 surface. Pbgra32 carries alpha, and
ClearType's subpixel filter has to know what is behind a glyph to run at all, so MIL falls
back to greyscale antialiasing at DirectWrite's own gamma, instead of subpixel RGB with the
system's per-monitor ClearType gamma and contrast.

This is not confined to scrolling. `CachedLineVisuals` defaults to true
(`DocsCanvas.cs:1146`) and the cached path is the only path unless the F9 toggle is
enabled, so **all text has been greyscale since 2026-09-01**, stationary included. It reads
as thinner and softer than it did.

It was predicted before the work started - `design/Scroll Pre-Buffering.md:158` lists it as
"the single most likely reason to abandon the approach" - and then confirmed after, at
`design/Scroll Frame Pacing.md:443`, by filling each line visual with the theme background
and watching the text sharpen.

Nothing else is degrading it. Resampling was ruled out on 2026-09-03: `RenderAtScale` is
already `DpiScale` (`RenderingContext.cs:276`), and all four of this machine's monitors run
at 96 DPI, so the whole-DIP rounding at `RenderingContext.cs:162`, `:279` and `:402` is
whole-device-pixel rounding too. There is no bitmap landing off the pixel grid. The
greyscale fallback is the entire effect.

## The fix, and why it is not a flag

Give the cached surface an opaque background: fill each line visual with the theme
background before drawing its text, so DirectWrite has something to filter against.

The obstacle is z-order. Today the canvas paints in five passes:

| # | pass | where |
|---|---|---|
| 1 | full-canvas theme background fill | `RenderingContext.cs:390` |
| 2 | code, colour, inline-colour and table backgrounds | `:405`-`:410` |
| 3 | selection, then search highlights | `:413`-`:419` |
| 4 | **the text**, as child visuals | `ContentLayer` |
| 5 | squiggles, page breaks, caret, hover preview | `OverlayLayer` |

A child visual draws after the element's own content, which is what made the layering work
for free. Make the layer-4 visuals opaque and each one covers everything passes 1 to 3 drew
inside its own rect. Every one of those has to move into the line visual, in the same
order.

## Inventory: what has to move

| what | today | already per-line? |
|---|---|---|
| theme background fill | `RenderingContext.cs:390` | no - one rect for the whole canvas |
| code block backgrounds | `:1395` | **yes** |
| colour block backgrounds | `:1415` | **yes** |
| inline colour backgrounds | `:1439` | **yes** |
| selection | `:1514` | **yes** |
| search highlights | `FindAndReplaceController.cs:243` | **yes** |
| table backgrounds and borders | `TableRenderer.cs:80` | **no - whole-table geometry** |
| table rectangular selection | `RenderingContext.cs:1598` | no - whole-region |
| blockquote bar | `:1383` | already inside `DrawLineContent` |

## Three things that make this smaller than it looks

**The colours are safe.** Every brush in passes 2 and 3 is alpha over the theme background,
not opaque: selection 100/255, code background 25, table background 15, table header 30,
table border 60, search match 60-80, current match 130-160, colour spans 40, blockquote bar
80 (`DocsCanvas.cs:55`-`:83`). Alpha composited over an opaque background gives the same
pixels whether the two happen in one surface or two. Provided the fill goes down first and
the order is preserved, the result is identical - no palette retuning, no new constants.

**Five of the eight movers are already per-visual-line loops** drawing exactly
`(lineY, lineH)` rects, culled the same way, using the same `FirstLineAt` binary search.
Moving them is relocating a loop body into `DrawLineContent`, not redesigning anything.

**Lines tile exactly.** `LayoutEngine.cs:484`-`:488` advances `y += lineH` with no gap,
except `_paragraphGap` between paragraphs. Opaque line rects therefore cover the content
contiguously, and the paragraph gaps keep showing the canvas fill - the same colour they
show today.

## The two real problems

### A. Tables are whole-table geometry

`DrawTableBackgrounds` (`TableRenderer.cs:80`-`:150`) does not think in lines. It finds a
table's line range and then draws:

- the table background over `tableY` → `tableBottom`, all rows at once
- the header tint over the first row
- an outer border rect around the whole table
- a row separator at `rowY`, the **top edge** of every row after the first
- **column separators as vertical lines from `yTop` to `yTop + tableH`, crossing every row**

The column separators are the piece that genuinely cannot survive as written: a vertical
line crossing N opaque row visuals has to become N segments, one inside each row. So this
becomes `DrawTableBackgroundForRow(row)`, drawing that row's slice of the table background,
the header tint if it is the first row, its top separator if it is not, its own vertical
column-separator segments, its left and right outer edges, and the top or bottom outer edge
if it is the first or last row.

This is the largest single piece and the one with the most ways to look subtly wrong:
separators doubled or missing where rows join, column lines a pixel out between adjacent
rows, the outer border broken at the seams.

One caveat to check rather than assume: a `_paragraphGap` inside a table would leave an
untinted stripe under per-row decomposition where the whole-table rect covers it today.
Tables should not contain empty blocks, so this is probably moot - but it is a test, not an
assumption.

### B. Selection and search highlights are view state

This is the decision the design actually turns on.

Today changing the selection costs one `InvalidateVisual` and one cheap `OnRender` pass.
The cached text is never touched. Bake the highlight into the bitmap and every line the
selection covers has to be rebuilt, because its picture now contains the highlight.

**Option 1 - bake it in, invalidate per line.** Keeps today's appearance exactly. The
machinery already exists: `DropLineVisualsForImage` (`RenderingContext.cs:232`) does
targeted per-line invalidation without touching layout, and `RedrawLinesWithImage`
(`DocsCanvas.cs:1108`) is the calling pattern. What is needed is the same shape keyed on
the selection's symmetric difference, so dragging down one line rebuilds one or two lines a
frame rather than the screenful. At the measured 8.6 µs a line, a worst case of Ctrl+A over
a full screen is ~430 µs against a 3.57 ms budget at 280 Hz - affordable as a one-off, and
the incremental drag case is a rounding error.

**Option 2 - leave selection in the overlay, above the text.** No invalidation problem, and
it is a two-line change. But alpha-100 blue over the glyphs tints the text instead of
sitting behind it, which is a visible change to how selection looks - working directly
against the fidelity this whole exercise is for.

**Recommendation: option 1**, with option 2 held as the fallback if the invalidation proves
harder to get right than the estimate suggests.

## Phased plan

Each phase is separately revertible and leaves the build shippable behind the flag. Only
phase 5 commits.

1. **Prove it, cheaply.** Add `OpaqueLineVisuals` behind a flag beside
   `EnableRenderPathToggle`: fill each line rect with `Palette.Background` before drawing
   its text, and change nothing else. Tables, selection and highlights will visibly break -
   that is expected and not yet interesting. The only question is whether the text sharpens.
   **If ClearType does not come back, stop here**; the premise is wrong and nothing below is
   worth building. This is the same discipline phase 1 of pre-buffering used, and it is
   cheap for the same reason.
2. **Move the per-line backgrounds in.** Canvas fill, code blocks, colour blocks, inline
   colour. All four are already per-line loops. After this, prose and code render correctly
   and only tables and selection are wrong. Measure cost per line: this adds one
   `DrawRectangle` to a bitmap that is built once, so it should not move.
3. **Decompose the table backgrounds.** `DrawTableBackgroundForRow`, per section A.
4. **Move selection and search highlights in**, with targeted invalidation per option 1.
   Measure a drag: lines rebuilt per frame must stay in low single digits.
5. **Delete the flag.** `OnRender`'s remaining job is the canvas fill behind the paragraph
   gaps, and the overlay.

## Things that will bite

- **You cannot assert ClearType from a `RenderTargetBitmap` test.** RTB is itself a Pbgra32
  transparent surface, so it renders greyscale whatever the line visuals do. Pixel tests can
  verify the *geometry* of phases 2 to 4 - seams, separators, background coverage - which is
  exactly what those phases need. Confirming ClearType itself is by eye, or by an external
  screen capture, against the F9 direct-draw path.
- **RTB tests must force an arrange first.** `UpdateContentLayer` runs from
  `ArrangeOverride`, deliberately (`RenderingContext.cs:148`), and adding children mid-render
  is illegal; the note there records that under RTB the adds silently did not take and the
  canvas laid out 2101 lines and drew none. Any new pixel test has to arrange before it
  renders.
- **Empty lines currently draw nothing.** `DrawLineContent` is guarded by `if (vl.Length > 0)`
  (`RenderingContext.cs:525`), so an empty line's visual is empty. Under the opaque scheme it
  must still paint its background, or blank lines punch holes in a code block's tint.
- **The F9 direct-draw path draws its backgrounds from `OnRender`.** It is the only honest
  way to compare the two paths, and it is what produced the 140-vs-119 measurement. Either it
  keeps its own copies of the moved code or it retires with the flag. Prefer keeping both
  through phase 4 and deciding at phase 5.
- **Print has its own duplicate of the table border drawing** (`DocsCanvas.Print.cs:359`-`:407`,
  against `_printPalette`). Printing does not go through line visuals and is unaffected, but
  the table decomposition must not be refactored in a way that leaves the two copies to
  drift.
- **The minimap renders its own `RenderTargetBitmap`** (`MinimapScrollbar.cs:697`, `:765`)
  and does not use line visuals. Unaffected.
- **Theme switching bumps `RenderVersion`**, which drops every line visual, so the opaque
  fill picks up the new background for free. No new invalidation needed there.

## What this does not do

Nothing for scroll smoothness. The fractional-offset ambition in
`design/Scroll Pre-Buffering.md` phase 3 is a separate question, blocked on its own grounds,
and neither helped nor hurt by this.
