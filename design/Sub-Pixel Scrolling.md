# Sub-Pixel Scrolling

Status: **planned, not started**. The third attempt. Two previous ones were reverted
(`40db97a`, `b23a387`), and this records why, what has changed since, and what would have to be
different for a third to be worth starting.

## What is left to fix

Everything else about the scroll is now measured and good. In Release, on the trading report,
280Hz panel:

| | |
|---|---|
| at speed | 276 - 279 paints a second, 4.7% of intervals over 1.5x the median |
| UI thread | 0.07 ms a frame |
| **the tail** | **49 - 87 image changes a second** |

The tail is the whole defect. As a coast decays the offset crosses whole-pixel boundaries every
11 to 20 ms, and unevenly, so the image changes 49 to 87 times a second while the panel is
showing 280. No frames are dropped - `ScrollDiag` confirms the paints are on time and the
compositor is keeping up. The picture simply does not change, because the renderer rounds the
offset to whole pixels and the offset has not moved a whole pixel yet.

Two consequences, and the second is what is actually visible:

- **Coarse.** At 60px/s the image updates 60 times a second on a 280Hz display.
- **Uneven.** Velocity decays continuously, so the interval between crossings grows smoothly
  while the crossings themselves land on integers. Steps come at 11, 13, 16, 20ms and so on -
  each individually correct, collectively read as judder.

The coast ends when velocity drops below 10px/s at a crossing, so the last step takes about
100ms. That is physics, not a stall, and it is the ~110ms maximum that appears in every gesture
summary.

## Why this is hard

Scrolling needs **vertical** sub-pixel positioning, and vertical is the hard direction.

ClearType is horizontal-only: it uses the red, green and blue subpixels of one physical pixel to
place a glyph edge more precisely along the x axis. There is no equivalent vertically. Glyph
rasterisation is also hinted vertically - baselines and x-heights are snapped to the pixel grid,
which is exactly what keeps small text crisp.

So a fractional vertical offset has to come from somewhere, and there are only three places:

1. **Turn off vertical hinting** and rasterise at the true fractional baseline. Correct motion,
   softer glyphs, and the softness varies with the fraction.
2. **Rasterise once and resample** when placing it at a fractional offset. Correct spacing,
   but the resample softens, and the softness varies with the fraction.
3. **Rasterise at several fixed vertical phases** and pick the nearest. No resampling, no
   hinting change, at the cost of memory and render work proportional to the number of phases.

Both previous attempts took one of the first two without knowing that was the choice being made.

## Attempt 1 - sub-pixel over live-rasterised text (`7988db1`, reverted `40db97a`)

Each visible line was drawn at its own fractional y.

**It failed because each line grid-fits independently.** Line A rounds its baseline down, line B
rounds up, and the *spacing between them* changes by a pixel as the fraction drifts. Reported
as "the lines jitter, the perceived distance to each other is juddering". `TextHintingMode.Animated`
and `SnapsToDevicePixels = false` were both tried and neither helped, because neither addresses
per-line rounding.

Absolute position was smooth; relative position was not, and relative position is what the eye
reads in a block of text.

## Attempt 2 - sub-pixel over cached line bitmaps (reverted `b23a387`)

Lines were rendered once into `BitmapCache`'d visuals and the whole layer translated by a
fractional amount - so one transform, no per-line rounding.

**Spacing was fixed and the text went soft.** A fractional translate resamples the cached
bitmap. Worse, as a coast decays the fraction drifts slowly through the same values, so the
resampling error beats into a visible interference pattern. Reported as "line spacing is OK,
still at some points the move feels blocky", and "antialias / subpixel rendering result in an
interference effect... a bit more blurry".

## What has changed since

Five things, and together they change the shape of the problem.

- **Lines are cached visuals moved by one transform.** The per-line grid-fitting of attempt 1
  cannot recur in that architecture.
- **The cached lines are greyscale, not ClearType.** Confirmed by filling each line visual with
  the theme background (`DocsCanvas.OpaqueLineVisuals`), which visibly sharpens the text -
  `BitmapCache` is a transparent surface, and ClearType cannot be used on one. So the text being
  resampled in attempt 2 was already softer than the editor's own un-cached text.
- **Per-row backgrounds exist now.** `DrawTableRowBackground` draws a row's fill and borders
  within the row's own bounds, which is what an opaque line needs. Selection and search
  highlights are still drawn beneath and would still have to move in.
- **Repaints are paced to the compositor's frame stamp.** There are 276 - 279 opportunities a
  second to show a different image; today the tail uses 49 to 87 of them.
- **A Direct2D presenter was built and measured.** It draws real text at 3.4us a line, held 280
  frames a second on the real document, and its output matched WPF exactly once both sides
  rasterised the same way. It was dropped because a *hybrid* is unworkable, not because it did
  not work.

## Options for a third attempt

### A. Phase-quantised line textures

Render each line at N fixed vertical phases - say 4, at quarter-pixel spacing - and blit the
nearest phase at a whole-pixel destination.

No resampling, no hinting change, no per-line rounding: each phase is a genuine rasterisation at
that sub-pixel offset. Motion granularity becomes 1/N of a pixel, so the tail would update at
the paint rate rather than the crossing rate.

Costs N times the line texture memory and N times the rasterisation work per line. At the
measured 0.07ms a frame there is room, but the memory is real on a long document and the
rasterisation is not free when lines stream in during a fling.

**Not expressible in WPF.** `BitmapCache` gives no control over the phase at which a visual is
rasterised. This needs a renderer we control.

### B. Sub-pixel in a Direct2D presenter

DirectWrite positions glyph runs at fractional baseline origins natively, and
`DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC` exists precisely so that sub-pixel positioning stays
symmetric rather than favouring one direction. Drawing fresh each frame at the true fractional
offset avoids both failure modes outright - no per-line rounding, because there is no rounding,
and no resampling, because nothing is being resampled.

Measured cost from the presenter work: 3.4us a line, about 0.15ms a frame for a screenful.

**Requires the canvas to be drawn by the presenter, always** - not the hybrid that was abandoned.
That is the larger decision recorded in `Rendering Direction.md`, and this would be one of its
benefits rather than a project of its own.

### C. Vertical hinting off, in WPF

`TextFormattingMode.Display` versus `Ideal`, and the text rendering options, control how much
WPF grid-fits. Setting the whole canvas to the least-hinted mode and translating fractionally
would give smooth motion at the cost of permanently softer text, scrolling or not.

Cheap to try and easy to revert. Likely rejected on appearance, but it costs an hour and it
settles empirically how much of the crispness comes from vertical hinting.

### D. End the coast earlier

Not a fix. The stepping is visible below roughly 30px/s; the coast currently runs to 10px/s.
Raising the stop threshold shortens the visibly-stepped phase at the cost of the scroll settling
a little sooner and a little more abruptly.

Worth measuring as a baseline for how much of the complaint is the last half second, because if
most of it is, D is minutes of work against weeks.

## Related, and separate

**Frame delivery when the window changes display** is a different defect, tracked in
`Rendering Direction.md`. It matters here only because both are about which clock the scroll
runs on: this document is about the offset moving in whole pixels, that one about frames
arriving at the wrong rate on a non-primary panel.

**Greyscale antialiasing, which nobody chose.** Worth stating plainly because the question comes
up whenever text quality does: we do not set a text rendering mode anywhere. There is no
`TextRenderingMode`, `TextFormattingMode`, `TextHintingMode` or `TextOptions` in the library or
in either host app. WPF's default is ClearType.

But the editor is not getting ClearType. ClearType needs to know what is behind a glyph, so it
cannot be used on a transparent surface, and a `BitmapCache` is one - so every line cached since
phase 2 is greyscale antialiased. Three independent things agree:

- Filling each line visual with the theme background first
  (`DocsCanvas.OpaqueLineVisuals`) makes the text visibly sharper.
- The Direct2D presenter only matched WPF's output at greyscale; at ClearType it was sharper
  than the thing it was standing in for.
- Nothing in the codebase asks for greyscale, so it cannot be deliberate.

So the editor's text has been greyscale since phase 2, by side effect, and nobody decided it.
That is worth deciding rather than inheriting. Restoring ClearType means making each line visual
opaque, which means moving everything currently drawn beneath a line into it - the row
separators and per-row backgrounds are already done, selection and search highlights are not.

It bears on this document because option C proposes going further in the other direction, and
turning vertical hinting off on text that is already greyscale would be a second reduction in
crispness on top of one we never intended.

## How we would know it worked

`ScrollDiag` already reports the right numbers, and the acceptance test is one line of its
output.

- **Tail paint interval should approach the composition frame interval.** Today: tail median 11
  - 20ms against a composition frame of about 3.0ms. Success is a tail median near 3.6ms, and
  the "over 1.5x median" figure in the tail down from 20 - 45% to the 5% seen at speed.
- **No new softness.** The seam comparison harness in `RaisinDocs.TestApp --seam` already
  differences two renderings pixel by pixel. Comparing a sub-pixel build against the current one
  at a whole-pixel offset must come out near identical; any broad difference is the blur that
  killed attempt 2.
- **No beat.** Attempt 2's interference appeared as the fraction drifted slowly. A slow coast
  held near constant velocity for several seconds is the test, and it has to be watched rather
  than measured.

## Suggested order

1. **D, as a baseline.** Minutes. Establishes how much of the complaint lives in the last half
   second of a gesture, which bounds what the rest is worth.
2. **C, as an experiment.** An hour. Settles how much crispness vertical hinting is actually
   buying, which decides whether A and B are solving a real constraint or an imagined one.
3. **Then A or B, and only as part of the larger renderer decision.** Neither is worth doing on
   its own: A needs a renderer WPF cannot provide, and B is a benefit of owning the canvas
   rather than a reason to.
