# Scroll Pre-Buffering

Status: **proposed**, not started. Written 2026-09-01 after the wheel-scrolling work
(`78cea16`..`40db97a`).

## The problem this solves

Wheel scrolling is smooth at speed and steps visibly as a coast decays. Below roughly
60 px/s the view can only move in whole-pixel hops, so it updates about 38 times a second
instead of at the display rate, and the hop lands on whichever render tick comes next:

| velocity | paints | gap between paints | movement per paint |
|---|---|---|---|
| 300–800 px/s | 105 | 10.0 ms (sd 6.6) | 1px 12%, 2px 12%, … |
| 150–300 | 55 | 9.6 ms (sd 6.1) | 1px 36%, 2px 25%, … |
| 60–150 | 47 | 10.4 ms (sd 5.7) | 1px 77% |
| **20–60** | **21** | **26.1 ms (sd 11.9)** | **1px 100%** |

It is confined to the last half second of a coast, covering about 6 px, but it is the part
of the gesture the eye is most able to follow.

## Why the obvious fix does not work

Drawing at a fractional scroll offset was tried and reverted (`7988db1`, reverted by
`40db97a`). It removed the stepping — every band went to a uniform ~7 ms gap and the slow
tail went from 21 paints to 170 — and introduced something worse: **the gap between
adjacent lines visibly breathes by a pixel as the view slides.**

WPF rasterises glyph runs with horizontal sub-pixel precision, because ClearType is a
horizontal technique and the glyph cache is keyed on horizontal phase, but it positions
them **vertically on the whole-pixel grid**. At a fractional offset each line rounds
independently, so line *i* crosses a pixel boundary at a different instant from line *i+1*
and their spacing alternates between 18 and 19 px several times a second.

With an integer offset every line shifts by the same whole number, so relative spacing is
exactly constant. That is what the rounding was buying, and it is not negotiable.

`TextOptions.TextHintingMode.Animated` and turning off `SnapsToDevicePixels` were both
tried, toggled around the coast so static text kept its crispness. Neither changed the
wobble, which is consistent with the snapping living in the rasteriser rather than in
either of those.

**The constraint, stated plainly:** we cannot move live-rasterised text vertically by a
fractional amount. We can only move *already-rasterised pixels* by a fractional amount.

## Measured: caching objects reaches a ceiling, and tables are past it

Before proposing to cache pixels it is worth recording what caching *objects* achieved, since
it bounds what is left to win.

Two documents, measured with the same instrumentation, are completely different workloads:

| per render pass | trading report | rdmd design doc |
|---|---|---|
| lines drawn | 46 | 48 |
| table rows | **33.5** | 0 |
| blank lines | 3.5 | 18.2 |
| joined lines | 0 | 0 |
| line-cache hits | 1 | 31 |
| cost per line | **64.8 µs** | 30.5 µs |

On prose the per-line `FormattedText` cache works: 66 → 30.5 µs a line, and what still looks
like "bypassing the cache" is almost entirely **blank lines**, which cost nothing — they are
counted as drawn but the whole branch is guarded by `vl.Length > 0`.

On the table document three separate attempts moved nothing:

| attempt | cost per line |
|---|---|
| baseline | 64.8 µs |
| cache the per-cell `FormattedText` | 72.2 µs |
| plus clip only on overflow, plus precomputed colour backgrounds | 76.9 µs |

All within noise of each other. **A table row issues one `DrawText` per cell** — about ten a
row, ~270 a frame — where a plain line issues one. Caching what *feeds* `DrawText` cannot help
when `DrawText` is the cost, and the ~120 lines of cache lifecycle it took were reverted.

That is the argument for this design, now measured rather than predicted: a pre-rendered row
is **one blit instead of ten `DrawText` calls**, which is the only structure that reaches this
cost. Coalescing on that document still runs at 35%, so it is also the case that most needs it.

## The approach

Rasterise the glyphs once, then translate the resulting surface. The glyphs are rendered at
one fixed vertical phase, so relative spacing is locked by construction, and the surface can
be positioned sub-pixel because moving a bitmap is resampling, not re-rasterising.

The cost is that a bitmap resampled to a fractional position is slightly soft. That softness
applies uniformly to the whole surface, so it does not distort spacing — it trades a little
sharpness during motion for correct geometry, which is the opposite of the trade we have now.

## Three ways to do it in WPF

### A. One cached surface covering a scroll window

A child `ContainerVisual` holding the content, with
`CacheMode = new BitmapCache { RenderAtScale = … }` and a `TranslateTransform` for the
offset. Scrolling changes the transform; WPF re-composites without re-running the drawing.

- Content must extend beyond the viewport, or the surface runs out of pixels as it moves.
- When the offset leaves the covered range the surface must be re-rendered around the new
  position. That re-render is a full viewport-plus-overdraw draw — at the measured ~30 µs
  per line and ~150 lines, roughly 4.5 ms, i.e. a dropped frame at every boundary crossing.
- Larger overdraw makes boundary crossings rarer and the re-render more expensive. This
  trade is the main design risk in this option.

### B. Per-line cached visuals (recommended)

A `DrawingVisual` per visual line, each with its own `BitmapCache`, positioned by a
transform. Scrolling updates the transforms; a line entering the viewport gets rendered
once, on its own.

- No window, no boundary crossings, no re-render hitch. Cost is spread one line at a time,
  which is exactly the shape of the work as content scrolls in.
- Every line translates by the same fractional amount, so spacing is preserved.
- Composes with the existing per-line `FormattedText` cache in `RenderingContext`, which
  already tracks lines by index and already has an invalidation signal (`RenderVersion`,
  `163a3ff`). The natural move is to extend that cache to hold a rendered visual rather than
  a `FormattedText`.
- Costs a visual per line and one bitmap each. Bounded the same way the existing cache is —
  drop entries beyond a window either side of the viewport.

### C. Manual `RenderTargetBitmap` per line

Same shape as B but we own the bitmap and call `dc.DrawImage` at a fractional Y.

- Full control over resampling and over when rendering happens.
- `RenderTargetBitmap` rendering is software and comparatively slow to produce, and we would
  be reimplementing what `BitmapCache` already does on the GPU.
- Worth keeping in mind only if `BitmapCache` turns out not to behave as documented.

**Recommendation: B**, falling back to A if per-line visuals prove too heavy.

## What must stay live

Not everything can go in the cache. Anything that changes independently of the scroll offset
has to be drawn on top, at the same translated offset:

| layer | cached? | why |
|---|---|---|
| code/colour block backgrounds | yes | content-derived, scroll-locked |
| text glyphs, list markers, rules, images | yes | the expensive part, and the point of the exercise |
| selection highlight | **no** | changes with the caret, and is drawn *under* the text |
| search highlights | **no** | changes with the query |
| caret | **no** | blinks |
| spelling squiggles | probably yes | content-derived, but arrives asynchronously — needs its own invalidation |

Selection sitting *under* the text means the cached surface must be transparent rather than
painting its own background. Layer order becomes: background → selection and search
highlights → cached content → caret and squiggles.

## Things that will bite

- **`RenderAtScale` must track DPI and zoom.** If it does not, the cached bitmap is resampled
  and text goes soft permanently rather than only while moving. Zoom already bumps
  `RenderVersion`, so the hook exists; DPI changes need a `DpiChanged` hook that does not
  exist yet.
- **`BitmapCache` and ClearType.** A cached surface with transparency generally cannot use
  ClearType, so text will fall back to greyscale antialiasing. This is a **visible change to
  how all text looks**, not only while scrolling, and is the single most likely reason to
  abandon the approach. Worth building a throwaway prototype to look at before committing to
  anything.
- **WPF's timing engine runs on the UI thread**, unlike WinUI. This removes rasterisation
  cost but does not move animation off that thread, so it does not by itself fix the
  irregular `CompositionTarget.Rendering` tick (p10 1.66 ms, median 2.93, p90 7.11, max 25.1).
- **Memory.** One bitmap per cached line at DPI × zoom. Needs the same windowing the
  `FormattedText` cache already uses.
- **Table rows are the case that matters, not prose.** They are where the cost is (33.5 of 46
  drawn lines on the report, 64.8 µs a line) and the one place object caching demonstrably
  cannot help. A design that handles prose beautifully and leaves tables re-rendering per
  frame would miss the point entirely.
- **Coverage has to be total before the offset goes fractional.** Any line type left out is
  re-rasterised at a new sub-pixel phase every frame, which is precisely the wobble this
  design exists to avoid — so an uncached table row would visibly breathe against its cached
  neighbours. Partial coverage looks *worse* than the integer-offset status quo, not better.
  Today 18 of 47 drawn lines bypass the `FormattedText` cache entirely — joined lines
  (`DrawJoinedLine`), table rows and image lines — and joined lines are the common case in
  prose, so that is most of the screen. This is a gate on phase 3, not a follow-up to it.

## Phased plan

Each phase is separately measurable with the existing `WheelDiag` instrumentation, and
separately revertible.

1. **Prototype the look, throw it away.** One `DrawingVisual` with `BitmapCache` showing a
   page of representative text, translated by a fractional offset. The only question is
   whether the text is acceptable — greyscale antialiasing, resampling softness in motion,
   crispness at rest. **If the answer is no, stop here**; nothing further is worth building.
2. **Per-line visuals for every line type, integer offset.** Convert the render loop to
   per-line cached visuals — plain lines, joined lines, table rows, image lines, list markers,
   rules — without changing scroll behaviour. Coverage must be complete here, for the reason
   above: phase 3 is only safe once nothing re-rasterises per frame. Confirms no regression in
   cost, spacing or the test suite while the offset is still integral. Measure: cost per line,
   render load, coalescing, and that the count of lines bypassing the cache is zero.
3. **Enable the fractional offset.** The payoff step, gated on phase 2 reaching full coverage.
   Measure the same speed-band table as above: the slow tail should look like every other
   band, and spacing must stay constant.
4. **Revisit extrapolation.** Projecting the offset to the predicted presentation time was
   written and discarded along with the sub-pixel work; it only makes sense once motion is
   genuinely sub-pixel. See below.

## Related, not required

Once motion is sub-pixel, the sampling question returns: DWM presents at vblank regardless
of when we paint, so a frame shows the position as it was when painted, held until the next
refresh. Painting and presenting drift, so evenly spaced paints land at unevenly spaced
positions.

The physics is closed-form (`v₀·(1−e^(−D·Δ))/D`), so projecting the offset to the predicted
presentation instant is exact and costs one `exp`. A refresh period is a serviceable
assumption; `DwmGetCompositionTimingInfo` gives `qpcVBlank` and `qpcRefreshPeriod` for the
exact figure, plus `cFramesDropped`/`cFramesMissed`, which would answer definitively whether
frames reach the screen. Note the struct must match the OS's expected `cbSize` exactly — a
hand-transcribed version failed with 0x88980090.

This is worth nothing while the offset is integral, which is why it was discarded rather
than kept.

## Alternatives rejected

- **Retained `DrawingGroup` plus translate.** Does not avoid re-rasterisation; WPF re-renders
  the drawing each frame.
- **Ending the coast earlier**, so it never enters the steppy regime. Cheap, but the
  remaining distance would have to go somewhere, and jumping it is worse than stepping it.
- **`TextFormattingMode.Display`.** Snaps harder, not less.
