# Scroll Pre-Buffering

Status: **phases 1 to 3 done and measured** on `scroll-subpixel-offset`; phase 4 unblocked. Written 2026-09-01
after the wheel-scrolling work (`78cea16`..`40db97a`); status revised 2026-09-05.

Phase 1 was prototyped and thrown away as intended. Phase 2 landed in `337e009` and then took
a long detour: caching the lines cost ClearType, and getting it back needed everything the
canvas painted beneath the text moved into the line - see `design/Opaque Line Visuals.md`.
That work finished the coverage this design's phase 3 was gated on. See *Coverage, rechecked*
below before starting it.

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
  When this was written, 18 of 47 drawn lines bypassed the cache entirely — joined lines
  (`DrawJoinedLine`), table rows and image lines — and joined lines are the common case in
  prose, so that was most of the screen. This is a gate on phase 3, not a follow-up to it.
  **It has since been met** - see *Coverage, rechecked*.

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

## Afterwards, possibly: motion blur

Once the content is a surface rather than live text, smearing it along the scroll direction is
cheap and would make a low frame rate read as continuous — the reason film at 24fps does not
look like a slideshow.

Where it helps and where it does not is worth being precise about, because it is easy to
reach for against the wrong artifact:

- **It cannot help the slow decaying tail.** Blur length is proportional to velocity, so at
  20–60 px/s there is nothing to smear, and that is exactly where the 1px stepping shows. The
  artifact that most wants fixing is the one blur structurally cannot touch.
- **It does help heavy content at speed**, where the repaint cap has lowered the frame rate and
  consecutive frames are far apart. That is the case the adaptive cap currently mitigates by
  trading frame rate for evenness; blur would make the remaining rate look better rather than
  merely even.

Two ways to draw it, once there is a cached surface:

- **Composite the surface two or three times** at intermediate offsets with reduced opacity.
  With a cached bitmap this is extra composites of a texture already on the GPU, needs no
  shader, and is trivially tunable. Crude, but likely enough.
- **A directional blur `ShaderEffect`** (HLSL ps_3_0). Correct, but WPF's built-in `BlurEffect`
  is isotropic and would blur horizontally too, which is wrong for vertical scrolling, so this
  means writing and shipping a shader.

Whichever, the blur must fall to zero as velocity does, or text at rest would be soft — and
that is also what stops it from being tried as a fix for the slow tail.

## Coverage, rechecked

Checked 2026-09-05, after `design/Opaque Line Visuals.md` finished moving everything into the
line. The phase 3 gate is met for body text, structurally rather than by a count.

**No line can bypass the cache.** `DrawLineContent` has exactly one call site, inside
`BuildLineVisual`. Joined lines, table rows, image lines, list markers and rules all reach the
screen through a `BitmapCache`'d visual because there is no other route. The 18-of-47 figure
cannot recur unless someone adds a second call site - which is the thing to watch for, rather
than the count.

**Nothing visible is deferred.** `SyncLineVisuals` builds `firstVisible..lastVisible`
unconditionally; `PreRenderBudget` throttles only the look-ahead margin.

**What still rasterises per frame**, which is the other half of the gate:

| where | what | text? |
|---|---|---|
| `OnRender` | one full-canvas background rect | no |
| overlay - `DrawTableLines` | border and separator strokes | no |
| overlay - `DrawSpellingErrors` | squiggles | no |
| overlay - caret | one `DrawLine` | no |
| overlay - hover image preview | an image, source mode only | no |
| overlay - **`DrawPageBreaks`** | **a `FormattedText` label** | **yes** |

`TableRenderer`'s `DrawText` calls are inside `DrawTableRow`, which runs within the line visual
and is built once, and in `CursorXInTableRow`, which measures rather than draws.

### Two things to settle before phase 3

- **The page-break label re-rasterises every frame** (`PageBreakManager`). At a fractional
  offset it grid-fits at a new sub-pixel phase each frame, which is exactly the wobble this
  design exists to avoid. It is a small label rather than body text, and only when
  `ShowPageBreaks` is on, but it is the one remaining thing that would breathe. Cache it or
  snap it.
- **Overlay content and cached lines will round differently.** The overlay draws in live
  coordinates each frame while the lines composite as bitmaps, so at a fractional offset a
  table border can drift a fraction against the row shading it is supposed to bound. That
  pairing did not exist when this was written - table borders moved into the overlay during
  the opaque-line work, precisely because they never cross a glyph. Either snap the overlay to
  the same grid, or accept a sub-pixel shear on table borders alone.

## Phase 3, as built

Four commits on `scroll-subpixel-offset`, 2026-09-05:

- `ef121cc` - the renderer stops rounding. `ContentScroll.Y` and the overlay both take
  `EffectiveOffset` unrounded. A line visual still sits at its own `Round(lineY)`, so the whole
  layer translating by a fraction moves every line by the identical amount and relative spacing
  stays exactly constant. That is the property the two earlier attempts could not hold, and it
  is what phase 2 bought.
- `92816fb` - the wheel coast stops gating on whole pixels. `OnWheelFrame` repainted only when
  `Math.Round(_offset)` changed, on the premise that the renderer rounded; the commit above made
  that premise false, so the first commit alone did nothing for wheel scrolling. The gate is now
  any movement past a hundredth of a pixel, and `_paintedOffset` holds the exact position so the
  log's paint-step figures measure the same thing on both gesture paths.

Two obstacles recorded above turned out not to need work. The page-break label was already
snapped - `PageBreakManager` draws at `Math.Round(y - effectiveScroll) + 0.5` - so it steps by a
whole pixel rather than re-hinting at a new sub-pixel phase. And the overlay does not shear
against the cached lines: a caret at `lineY` and a line visual at `Round(lineY)` both shift by
the same offset, so their difference is constant per line rather than drifting.

- `cd7cf24` - the coast is paced by the compositor's clock. `OnWheelFrame` read a `Stopwatch`
  restarted at the top of the handler, sixty lines before the duplicate-frame check. Repainting
  from inside the handler makes `Rendering` free-run at several hundred a second, so that clock
  timed the slivers between duplicate raises rather than the interval the panel shows. The
  offset advanced by a different amount between one *displayed* frame and the next even when
  composition was perfectly regular. `dt` now comes from the difference between frame stamps and
  a duplicate raise returns before anything moves. The closed-form integral is untouched, so a
  notch travels the same distance as before - only the clock measuring it changed.
- `0090bd3` - a minimap drag is measured at last. It maps the mouse straight onto the offset
  through `SetDirect`, so it opened no gesture and wrote no line; the `smooth` rows it was being
  compared against came from click-to-jump and the scrollbar.

### Measured

Release against Release, so no build-configuration confound - the run at 02:42 before any of
this, against 13:37 after:

| wheel gesture | before | after |
|---|---|---|
| composition median | ~3.15 ms, wandering 3.01-3.32 | **3.57 ms, every gesture** |
| composition jitter, over 1.5x median | 4.9-14.5%, avg **8.4%** | 1.2-5.9%, avg **3.1%** |
| paint interval | 7.2-8.0 ms (~135/s) | **3.57 ms (280/s)** |
| composed frames painted | 98 of 211 | **1992 of 1992** |

3.57 ms is exactly 1/280, the panel's period, to two decimals on every gesture. Before, it
free-ran at ~3.15 ms - faster than the display could show, at a phase that slid, which is the
unpaced presentation `design/Scroll Frame Pacing.md` diagnosed. Pacing off `RenderingTime`
removed it.

**Confirmed by eye, 2026-09-05.** The coast reads as good but not perfect: a little jaggedness
remains. Notch distance is unchanged, which is what the closed-form integral being untouched
predicted - it was reasoned rather than measured, so it is worth having heard.

**Then confirmed from outside the process**, with `capture-scroll.ps1` and PresentMon - 2982
frames over ten scripted gestures. **Nothing is being dropped: 0 frames of 2982**, against the
13% that `design/Scroll Frame Pacing.md` was built on. Presented and displayed are both 273/s
into a 280 Hz panel.

The jaggedness is frames held longer than one refresh: 92.9% land on exactly one, 4.1% on two,
and a 0.3% tail at four or five. Animation error - the difference between a frame's CPU delta
and its display delta, which is the metric that describes what the eye sees - has a median of
0.21-0.49 ms per gesture. Roughly half the shortfall is explained by presenting at 273/s into a
280 Hz panel; the rest is unattributed and is the next question, if it is worth one.

### The premise this started from was wrong

The investigation began from an observation that a minimap drag is smooth at any speed while
the wheel is not, and the inference that the pipeline must therefore be able to keep up and the
wheel must be doing something wrong. The conclusion was right. The inference was not.

With all three gestures measured the same way:

| gesture | composition | jitter | paints | composed frames painted |
|---|---|---|---|---|
| **wheel** | 3.57 ms | **3.1%** | 280/s | 100% |
| smooth | 3.57 ms | 12.2% | 280/s | 100% |
| **direct** (drag) | 5.29 ms | **20.6%** | 187/s | 40-78% |

The drag measures **worst** of the three. It paints when a mouse message arrives, not when a
frame is composed, and mouse reports are neither synchronised to vblank nor delivered at 280 Hz,
so a fifth to three fifths of composed frames get no paint and the interval inherits the input
sampling. It *looks* smooth because the content is slaved to the hand: position is a direct
function of the mouse, so there is no cadence for the eye to catch, and any unevenness is the
observer's own.

**Confirmed externally on 2026-09-05**, with both gestures captured in the same run on a quiet
machine, driven by a scripted sweep so the two are comparable:

| | wheel (10 gestures) | minimap drag (3) |
|---|---|---|
| presented, and displayed | 256-279/s | **108-110/s** |
| display interval | **3.57 ms** - exactly 1/280, on every gesture | **7.15 ms** - two refreshes |
| intervals over 1.5x median | 1.1-9.0%, and 1.1-2.5% on the long scrolls | 24-38% |
| animation error, median | **0.22-0.33 ms** | **3.73-7.12 ms** |
| dropped | 0.0% | 0.0% |

The drag carries roughly **twenty times** the wheel's animation error and holds every frame for
two refreshes. Nothing is dropped in either, so this is pacing rather than loss: the drag is
bound by the rate mouse messages arrive, which is about 110/s and unrelated to the panel.

So the observation this investigation started from - a drag is smooth at any speed and the wheel
is not - is exactly inverted by measurement. The wheel is now the best-paced input in the editor
and the drag the worst.

What survives of the original argument is the weaker and more useful half: rendering cost is not
the bottleneck. `canvas-onrender` averages about 0.05 ms throughout.

Two caveats on those figures. The `smooth` gestures were all 0.26-0.51 s and `direct` has two
samples, so a couple of late frames dominate their percentages; the wheel figures come from
0.57-7.46 s runs. And one drag showed composition itself at 7.01 ms, so WPF appears to compose
at half rate when paints are sparse - part of `direct`'s jitter is that rather than anything
here.

### What this can and cannot fix

**It addresses the slow tail only** - the 20-60 px/s band in the table above, where the view
could previously move only in whole-pixel hops.

**It does nothing for uneven scrolling at speed.** That is a different fault with a different
cause, measured in `design/Scroll Frame Pacing.md`: WPF presents unpaced, 228 presents a second
into a sink changing 140 times a second, 13% dropped, with late intervals landing at 2 and 5
refreshes instead of 1. It is a known WPF issue rather than anything in this codebase
([dotnet/wpf#11607](https://github.com/dotnet/wpf/discussions/11607) reports the same signature,
and finds `BitmapCache` and `UseLayoutRounding` equally ineffective against it). The fix
explored for it is the paced presenter in section C of that note.

The arithmetic makes the separation plain: at speed the view moves more than a pixel per frame,
so `Math.Round(_offset)` changed every frame and the old gate already passed every frame.
Removing it cannot change anything above about 60 px/s. **Phase 3 should not be credited with
smoothing fast scrolling, and should not be blamed if fast scrolling stays uneven.**

A minimap drag is not the yardstick it looks like, for the reason above: it is not animated at
all. Judge the wheel against the log, not against a drag.

## Alternatives rejected

- **Retained `DrawingGroup` plus translate.** Does not avoid re-rasterisation; WPF re-renders
  the drawing each frame.
- **Ending the coast earlier**, so it never enters the steppy regime. Cheap, but the
  remaining distance would have to go somewhere, and jumping it is worse than stepping it.
- **`TextFormattingMode.Display`.** Snaps harder, not less.
