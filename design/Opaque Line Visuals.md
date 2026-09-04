# Opaque Line Visuals

Status: **done**. All five phases complete and confirmed; the opaque fill ships. Written
2026-09-03, on branch `opaque-line-visuals`, with a branch per phase stacked on it
(`opaque-line-visuals-phase1` through `-phase4`, the last of which carries phase 5).

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
| table background and header tints | `TableRenderer.cs:80` | no - but trivially per-row |
| table borders and separators | `TableRenderer.cs:131`-`:148` | **no - but need not move down at all** |
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

## The two problems, one of which mostly dissolves

### A. Tables: lines above, tints below

`DrawTableBackgrounds` (`TableRenderer.cs:80`-`:150`) does not think in lines. It finds a
table's line range and then draws the table background over `tableY` → `tableBottom`, the
header tint over the first row, an outer border rect around the whole table, a row separator
at the top edge of every row after the first, and column separators as vertical lines from
`yTop` to `yTop + tableH`, crossing every row.

Decomposing all of that per row is real work with many ways to look subtly wrong. It is also
mostly unnecessary, because **only the fills need to be behind the text**. The lines never
cross a glyph, so they can be drawn in a pass *after* the content layer and keep their
whole-table geometry unchanged.

That splits the method in two:

- **Per-row tints, into the line visual.** A row's slice of the table background, plus the
  header tint on the first row. Both are a single `(rowY, rowH)` rect of `tableWidth` - no
  cross-row geometry, no seams.
- **The line geometry, unchanged, into the overlay.** Outer border rect, row separators,
  column separators. The code moves as it stands; only the `DrawingContext` it writes to
  changes, from `OnRender`'s to the `OverlayLayer`'s. The overlay already re-renders per
  frame in screen coordinates with `effectiveScroll` applied, exactly as this pass does
  today, so the cost is identical and no new layer is needed.

**Clearance, measured** (2026-09-03, `FormattedText.BuildGeometry().Bounds` against
`GetLineHeight`, which is `FormattedText("M").Height`):

| | line box | descender ink ends | clearance |
|---|---|---|---|
| Segoe UI 16 (body, table cells) | 21.280 | 21.032 | 0.248 |
| Segoe UI bold 16 (table header) | 21.280 | 21.024 | 0.256 |
| Cascadia Mono 14 (code) | 16.270 | 16.268 | **0.002** |

Horizontally the lines are safe outright: column widths are the widest cell's content plus
`_tableCellPadding * 2` (`TableRenderer.cs:70`, padding is 8), so a column separator is at
least 8 DIP from the nearest glyph and cell text cannot overflow into that gap.

Vertically the margin is a quarter of a pixel, and for the code font it is nothing at all.
Ink still *fits* inside the line box - see the resolved note below - but a 1 px separator
centred on a row boundary covers the half pixel above it, which is where the row above's
descenders put their antialiased tail. Drawn beneath the text today, the glyph wins there;
drawn above, an alpha-60 grey grazes it. This is half a pixel of a descender's fade and
should be invisible, but it is the one place this approach is not bit-identical. If it ever
shows, the fix is small: move just the row separators into the lower row's visual at local
`y = 0`, where they sit behind that row's own text and are still free of cross-row geometry.

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
3. **Split the table drawing.** Per-row tints into the line visual, line geometry into the
   overlay, per section A. Check the row separators against a descender-heavy table by eye.
4. **Move selection and search highlights in**, with targeted invalidation per option 1.
   Measure a drag: lines rebuilt per frame must stay in low single digits.
5. **Delete the flag.** `OnRender`'s remaining job is the canvas fill behind the paragraph
   gaps, and the overlay.

## Results so far

### Phase 1

**Confirmed 2026-09-03: the text is sharper with the fill on.** The gate is passed and
phases 2 to 5 are worth building.

Judged in the editor under `--render-diag`, flipping F8 on a stationary page, against the
F9 direct-draw path as a reference for what ClearType looked like before `337e009`.

### Noticed while comparing: the two paths space their lines differently

Flipping F9 shifts which lines sit a pixel further apart. Nothing is wrong, and phase 1 did
not cause it - it has been true since `337e009` and is only visible now that the two paths
can be switched between.

A line height of 21.28 px cannot be honoured on a whole-pixel grid, so both paths alternate
21 and 22 px gaps and average out correctly. They differ in *which* lines get the extra
pixel, because they round at different points:

| | gaps between consecutive lines (px) |
|---|---|
| cached | 21, **22**, 21, 21, 21, **22**, 21, 21, **22** |
| direct | **22**, 21, 21, 21, **22**, 21, 21, **22**, 21 |

The cached path rounds the line's origin (`RenderingContext.cs:279`) and draws the text at
local zero inside the visual; the direct path draws at the unrounded position and lets WPF
snap the glyph run, which comes out rounded about the baseline instead. Over twelve lines
they disagree on three.

Both are stable as the view scrolls, since the offset is an integer, so this is not the
spacing breathe that killed sub-pixel scrolling in `design/Sub-Pixel Scrolling.md`. It does
mean F9 is a reference for *antialiasing*, not for layout: do not use it to judge spacing.

### Phases 2 and 3

**Confirmed 2026-09-03 by eye, and by the direct-draw path being indistinguishable from the
cached one.** Code, colour block, inline colour and table tints all survive the fill; the
table's borders and separators draw over it. Judged with F8 both ways and F9 both ways, on a
document with a fenced code block, coloured spans and a table.

Both phases changed the shipping path as well as the F8 one, since the tints moved into
`DrawLineContent`, which the cached and direct paths both call. So the regression surface was
the default render, not just the flag, and it was checked with F8 off as well as on.

UI tests: 26 of 904 failing before and after, all pre-existing and none in rendering. Model
tests: 4 of 999, likewise.

**That the two paths now look the same is the pass condition, not a reason to prefer direct
draw.** Direct draw always had ClearType, since it paints onto the already-opaque canvas -
what these phases did was bring the cached path up to it. The cached path remains
substantially faster while scrolling: 140.2 displayed fps against 119.3, and 8.6 µs a line
against 28.9 (`337e009`, measured with FrameView).

The one visible difference left between them is the line-spacing distribution described
above, which predates all of this.

### Phase 4

**Confirmed 2026-09-04.** Selection and the search highlights both draw inside the line now,
in that order, and nothing the canvas paints is under the content layer any more except the
full-canvas background fill that shows through the paragraph gaps.

Selection invalidation is targeted, per option 1 above: `DropLineVisualsForSelectionChange`
drops the union of the old and the new selection at block granularity. The rectangular table
selection carries its **columns** in the signature as well - it can be dragged sideways with
its block range unchanged, and the highlight would otherwise sit still while the drag
continued.

The search highlights went the other way and drop every cached line, because matches sit
anywhere rather than in one contiguous run and the signature is a hash - it reports that
something changed, not what. That is affordable only because a highlight change is
user-initiated, a keystroke in the find box or F3, costing one screenful; a selection changes
on every frame of a drag and could not be treated this way.

Both signatures are **derived from the state rather than maintained beside it**. A dozen sites
in `FindAndReplaceController` add to, clear or reindex the match list, and a version counter
bumped at each would leave a stale highlight baked into a cached line the first time one was
missed. Same reasoning as the syntax token cache key in `design/Typing Performance.md`.

Inverting the highlight loop - from a pass per match walking every line, to a line asking
which matches fall on it - was also less work than what it replaced: a match in another block
is now rejected on an integer compare, and a joined line rejects out-of-group matches before
`SourceToJoined` rather than after.

### Phase 5

**Done 2026-09-04.** `OpaqueLineVisuals` defaults to true, so ClearType is back for everyone.

The toggles stayed, and stopped needing a switch: F8 and F9 work in every host, the viewer
included, which is where a reader is most likely to be looking at text closely enough to care.
The badge follows the state instead - it appears when the path is not the one that ships and
goes away when the toggles come back, so a reader who has never heard of these keys never sees
one. `--render-diag` survives only to pin it on while measuring, since the honest frame-rate
figures come from a capture taken outside the process and the badge is what names the path in
it.

The direct-draw path was kept. It costs nothing now that the drawing is shared through
`DrawLineContent`, and it is the only thing that would catch a pass left half-moved.

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
- **Resolved: glyph ink does not overhang its line box**, so an opaque fill never clips the
  line above. Measured 2026-09-03: descender ink ends 0.248 DIP inside the box for Segoe UI 16
  and 0.002 for Cascadia Mono 14, and accented capitals clear the top by 2.47 and 0.27. It
  fits everywhere - but with so little room that anything drawn *on* a line boundary shares
  the boundary pixel with a descender. That is the whole of the row-separator caveat in
  section A, and it is worth re-measuring if the fonts or the line-height rule ever change.
- **Empty lines currently draw nothing.** `DrawLineContent` is guarded by `if (vl.Length > 0)`
  (`RenderingContext.cs:525`), so an empty line's visual is empty. Under the opaque scheme it
  must still paint its background, or blank lines punch holes in a code block's tint.
- **Move the drawing into `DrawLineContent`, not into `BuildLineVisual`.** Both paths already
  call `DrawLineContent` - the cached one inside the visual (`RenderingContext.cs:295`), the
  direct one from `OnRender` (`:447`) - so anything moved there serves both, and the
  `OnRender` pass it came from is then deleted rather than duplicated. Put a background next
  to the fill in `BuildLineVisual` instead and it works on the cached path while silently
  stranding F9. The fill itself is the one thing that does belong in `BuildLineVisual`: the
  direct path draws onto the already-opaque canvas and has ClearType without it.
- **The F9 path is a measurement rig, not a shipping path.** It is unreachable without
  `--render-diag`, and it is what produced the 140-vs-119 displayed-frame figure and the
  ClearType baseline phase 1 was judged against. Kept for free if the rule above is followed,
  so there is no retire-or-duplicate decision to make at phase 5 after all.
- **Do not delete the `OnRender` background passes until their replacement is shared.**
  Leaving both live double-draws the alpha tints on the direct path and darkens them; the
  cached path hides the duplicate under its opaque fill and looks fine, so this will not show
  up unless F9 is checked.
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
