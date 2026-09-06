# Scroll Frame Pacing

Status: **section C is not needed for wheel scrolling. Measured, not argued.** Follows on from `Scroll Pre-Buffering.md`, which covers how lines came to be cached
as visuals. This one is about the frames reaching the screen rather than the cost of drawing
them.

> **Do not build the presenter in section C without re-reading the bottom of this note.** The
> unpaced presentation it exists to fix was measured while the wheel coast paced itself off a
> wall clock. With that fixed and re-captured with PresentMon: **0 dropped frames of 2982, and
> 92.9% of frames displayed for exactly one refresh.** The 13% drop rate this note is built on
> is gone.

## Where things stand

Lines are rendered once into `BitmapCache`'d visuals and the whole layer is moved by a single
transform (`337e009`). Measured with FrameView, which reports frames the panel actually
displayed rather than how often we drew — a distinction worth insisting on, because render
rate and frame rate were conflated for a long time before that:

| same document, same window | direct draw | cached visuals |
|---|---|---|
| cost per line | 28.9 µs | 8.6 µs |
| displayed fps (median) | 119.3 | 140.2 |
| frames hitting every refresh | 16.9% | 33.3% |
| frames at 93 fps or worse | 44.0% | 26.1% |

Sub-pixel scrolling was tried twice on top of this and reverted both times. Over
live-rasterised text each line grid-fits independently and the spacing between them visibly
wriggles; over cached bitmaps the fractional translate resamples, which softens the text and, as
a coast decays and the fraction drifts slowly, beats into a visible interference pattern. Both
were worse than the 1 px stepping they were meant to cure.

**It went in on the third attempt** (`ef121cc`, 2026-09-05), once every background, tint,
selection and highlight had moved into the line and `BuildLineVisual` was the only route a line
could take to the screen. Each visual sits at its own rounded position and the whole layer
translates beneath it, so every line moves by the identical fraction and relative spacing is
exactly constant - the property neither earlier attempt could hold. Reported as showing no
artifact. See `design/Scroll Pre-Buffering.md`.

## What is left, and what it is not

About 10% of frames arrive late. Excluded by measurement, in order:

- **Not our drawing.** `OnRender` on late frames is 503 µs against a 444 µs median, and they
  build no visuals.
- **Not cache invalidation.** `RenderVersion` moved 12 times across 1250 frames.
- **Not garbage collection.** 42 collections against 196 late frames; only 9 coincide. GC
  accounts for 5% of them.
- **Not GPU selection.** Setting the process to high performance changed nothing, and the
  machine has one GPU, so there was nothing to select.
- **The minimap was a real contributor, and is fixed.** It rebuilds its bitmap when the
  viewport leaves the line range it cached, and was being invalidated once per canvas frame at
  `DispatcherPriority.Normal`, which outranks both `Render` and `Input`. Throttled to 30Hz at
  `Background`, the 3-frame periodicity fell from 297 occurrences to 73 and late frames from
  13.9% to 9.8%.

What remains lands on **exact multiples of the refresh period**. On a 280Hz panel, a refresh
is 3.571 ms:

- median interval 7.00 ms ≈ **2 refreshes**
- late interval 17.8 ms ≈ **5 refreshes**

Frames are not arriving at arbitrary times; they are missing a composition deadline and being
picked up at a later vblank. FrameView shows the signature plainly: **228 presents a second
into a sink that changed 140 times a second, with 13% dropped.** That is unpaced presentation.

This is a known WPF issue, not something peculiar to this codebase — see
[dotnet/wpf#11607](https://github.com/dotnet/wpf/discussions/11607), where the same symptom is
reported as gaps of 20.83/27.78/34.72 ms where 6.94 ms was expected at 144Hz, and where
`BitmapCache` and `UseLayoutRounding` were likewise found ineffective against it.

---

## A. Pre-render ahead of the viewport

**Small, clearly correct, do first.**

`SyncLineVisuals` creates a visual only for lines between `firstVisible` and `lastVisible`, so
a line is rasterised at the moment it scrolls into view — inside a scroll frame. The ±400 line
window governs only when visuals are *dropped*, not when they are made.

Rasterise a margin above and below the viewport instead, off the critical path, so a scroll
never rasterises anything.

The measured cost today is small — a mean of 0.30 visuals built per frame — but 100 of 911
late frames did build one, so it is a real if minor contributor. The change is to widen the
loop bounds and do the work when idle rather than during a gesture.

## B. Scroll by transform alone

**Bigger, structurally right, do second.**

Every painted frame still calls `InvalidateVisual` and runs `OnRender` — about 500 µs — purely
to redraw backgrounds, selection, search highlights and the caret at the new offset. The text
moves by transform; everything else is redrawn from scratch.

During a scroll none of that content changes. Only its position does.

Move each of them into its own layer under the same scroll transform, invalidated only when
its own content changes. A pure scroll then becomes *setting `ContentScroll.Y` and nothing
else*: no `InvalidateVisual`, no `OnRender`, no UI-thread work at all. WPF composites layers
it has already rasterised.

Layer order is already established by phase 2 and does not change: the element's own
`OnRender` paints underneath, `ContentLayer` holds the text, `OverlayLayer` sits above.
Backgrounds and selection need to join the scrolling set without ending up over the text.

The same fix is available for the minimap: it rebuilds when the viewport leaves its cached
range, so rendering the whole document's thumbnail once would remove rebuilds entirely,
bounded by memory on a long document.

**Neither A nor B can fix the refresh-multiple quantisation.** They remove *our* contribution
to it, which is worth doing on its own terms and may well reduce it. They cannot make DWM
present on a fixed cadence.

---

## C. A paced presenter, for scrolling only (optional)

The only mechanism that addresses the quantisation directly. Worth understanding before
committing to it, and worth proving before building it.

### Why WPF cannot do it

Three hops, two of them scheduling points: the UI thread builds the visual tree, WPF's render
thread rasterises and presents to its own surface, and DWM composites and presents at vblank.
The quantisation happens at the last one — a frame misses a composition deadline and waits for
a later vblank. But it is WPF's doing that we miss them, because it presents when it chooses
rather than pacing to the deadline, and it exposes none of the machinery for doing better:

- **`DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`** — signals when the system is ready
  for a new frame, so rendering begins at the right moment rather than blocking inside
  `Present`. Works with `CreateSwapChainForComposition`, i.e. for a DWM-composited window.
- **`SetMaximumFrameLatency(1)`** — one frame in flight.
- **Windows 11's composition swapchain API** — explicitly deadline-aware.

Windows Terminal, a text renderer with the same problem shape, uses this route.

### The insight that makes it affordable

It does **not** require reimplementing the renderer. During a scroll the content does not
change — only its position. So this is a *presenter*, not a renderer: WPF still rasterises the
lines, and we only take over how those existing pixels reach the screen while a gesture is
running.

1. Scroll starts: capture the content region (viewport plus margin) into a texture. The cached
   line visuals are already exactly that content.
2. A child surface with a flip-model swapchain blits the texture at the scroll offset, paced
   by a waitable object — one present per vblank instead of 228 into a 140 sink.
3. Scroll ends: hide it; WPF paints the final position.

A useful side effect: at 280Hz, whole-pixel scrolling is smooth down to 280 px/s, because
there are 280 steps a second to spend. The 1 px stepping is only objectionable because we
deliver about 140 of them. Fixing the cadence may remove the motivation for sub-pixel
scrolling altogether — and with it the resampling blur and the wriggling spacing that made
both variants of it unusable.

### What stays hard

- **The seam.** The handoff at both ends must be pixel-exact and flash-free. A visible pop at
  the start and end of every gesture would be worse than the stutter it cures.
- **Running off the texture.** A fast fling covers more than any sensible margin, so new
  strips must be rendered into it as the scroll proceeds — a scrolling ring buffer. This is
  the real complexity, and it puts rasterisation back on the critical path unless kept ahead.
- **Airspace.** The find bar and link popup overlay the canvas and would be hidden while the
  surface is up. Probably acceptable, since they are rarely open mid-fling, but it is a
  behaviour change.
- `D3DImage` does not help: the surface goes back to WPF's compositor, so presentation is at
  its mercy again.

### The prototype answered yes

Built and measured (`RaisinDocs.TestApp --presenter`). A child HWND, a flip-model swapchain,
`FRAME_LATENCY_WAITABLE_OBJECT`, `MaximumFrameLatency = 1`, presenting nothing but a colour
sweep so that only the timing is under test:

```
present gap   median 3.57ms (280/s)   p99 3.72ms   max 3.78ms   over 1.5x median 0.0%
```

Exactly one refresh of the 280Hz panel, a 4% spread, and **not one jump to a multiple**.
Against the editor on the same machine, same panel, same compositor: 7.00ms median, with 19%
of frames at 17.8ms - two refreshes and five.

**So the ceiling is liftable.** The quantisation is not the hardware, not DWM's inherent
behaviour, and not the GPU. It is WPF presenting when it chooses rather than to a deadline, and
a paced swapchain does not have that problem.

What that buys, if built: an even 280 in place of 140 with a fifth of frames late. It also very
likely removes the argument for sub-pixel scrolling, since at 280 steps a second whole-pixel
motion is smooth down to 280 px/s - and with it the resampling blur and the wriggling spacing
that made both attempts at sub-pixel unusable.

What it still costs is unchanged and unmeasured: the seam at both ends of a gesture, the ring
buffer for running off the texture, airspace while the surface is up, and the interop itself.
Those are the things to weigh now that the payoff is known rather than guessed.

### But the pixels cannot come from WPF

The prototype settled the pacing. It left the other half open: where the presenter gets its
pixels. The design assumes `RenderTargetBitmap` over the cached line visuals, since those are
already exactly the content being scrolled. Measured on the trading report, 2894 lines, a
1000x1000 viewport:

| capture | cost |
|---|---|
| canvas, 1x viewport | 20.8 ms |
| canvas, 2x viewport | 20.2 ms |
| canvas, 3x viewport | 20.6 ms |
| `ContentLayer` alone, 3x viewport | 18.0 ms |
| canvas, 1x viewport, direct draw (no cached children) | 12.9 ms |

**The cost is flat in area.** Tripling the bitmap changes nothing, so this is not pixel work -
it is re-rasterising the visual tree, and the same 51 line visuals are the whole bill whatever
size bitmap they are drawn into. `ContentLayer` on its own costs 18.0 ms with no `OnRender`
involved, which puts it at **~350 us per cached line visual against 8.6 us to composite the
same line live**, a factor of 40.

The reason is that `RenderTargetBitmap` rasterises in software, through WIC, with no GPU. That
makes `BitmapCache` a liability rather than a help there: 20.8 ms with cached children against
12.9 ms drawing directly. The cache that made live compositing fast makes capture slow.

Three consequences, and they compound:

1. A capture at the start of a gesture costs **four to six refreshes** - a visible hitch at the
   start of every scroll, which is the defect the whole exercise exists to remove.
2. A larger margin is not free. Flat-in-area is only flat because the same visuals are being
   drawn; three viewports of real content is three times the visuals, so about 50 ms.
3. `RenderTargetBitmap.Render` has visual thread affinity, so **every capture is UI-thread
   time**, competing with the scroll it is meant to smooth. It cannot be moved to the
   presenter thread.

So "WPF rasterises, we only present" does not survive contact with measurement. The pacing
works; there is no cheap way to feed it. Feeding it means drawing the text into the texture
ourselves with Direct2D/DirectWrite on the presenter thread - which needs the styled-run,
table and image pipeline duplicated off the UI thread, and is the second renderer this whole
approach existed to avoid.

That is the decision now on the table: pay for a second text renderer for the scrolling case,
or keep the gains from A and B and accept WPF's cadence.

### C2: the presenter draws text, and it is cheap

`RaisinDocs.TestApp --textpresenter [--speed=N]`. Direct2D and DirectWrite drawing the report's
lines straight into the paced swapchain, no capture anywhere. Whole viewport redrawn every
frame at the exact offset:

```
present  median 3.57ms (280/s)  p99 3.64ms  over 1.5x median 0.0%
draw     median 0.14ms  p99 0.23ms  max 0.29ms
per line 3.4us   lines/frame 43   layouts built/frame 0.21
```

| drawing one line | cost |
|---|---|
| WPF, live `OnRender` | 28.9 us |
| WPF, compositing a cached `BitmapCache` visual | 8.6 us |
| **DirectWrite, drawn fresh** | **3.4 us** |

**Drawing every line from scratch is cheaper than WPF compositing bitmaps it had already
rasterised.** A frame costs 0.14 ms of a 3.57 ms budget - 4%, with 25x headroom - and the
cadence is the prototype's, unchanged by doing real work: 280 a second, not one frame late.

Held at 6000 px/s and at 20000 px/s, far faster than any real fling. Layout construction, the
one thing a fast scroll adds, rises to 3.6 layouts a frame at 20000 px/s and changes nothing:
draw stays at 0.18 ms.

**This removes the ring buffer.** C4 existed because a fling outruns any captured texture and
new strips would have to be rendered into it. There is no texture to run off: every frame is
drawn fresh at its own offset, so a fling is the same work as a crawl. The seam and airspace
remain; the hardest piece of the design does not.

It also reopens sub-pixel scrolling, which was abandoned twice. Both failures were WPF's: over
live text each line grid-fits independently so the spacing wriggles, and over cached bitmaps a
fractional translate resamples and blurs. Neither applies to one Direct2D pass placing all
lines at fractional offsets. Unproven, but worth retrying once the seam works.

What C2 has **not** covered, and what the cost will grow by: this is one text format, no word
wrap, and none of the tables, images, colour spans, selection or search highlights the canvas
draws. The headroom is large but the pipeline is not yet real.

### C3: the seam can be invisible

`RaisinDocs.TestApp --seam`. The same 24 lines, Segoe UI 16, drawn once by WPF and once by
Direct2D at the same positions, both captured from the composed desktop - not through
RenderTargetBitmap, which would compare against software rasterisation that never reaches the
screen.

**The two paths already agree on where glyphs go.** Best alignment over a +/-3px search is
dx=0, dy=0: DirectWrite and WPF place the lines identically, with no offset to correct.

They disagreed on how glyphs are *rasterised*, and it was one setting. WPF draws ClearType;
Direct2D was drawing greyscale, because ClearType needs an opaque target and the swapchain
bitmap had been created with a premultiplied alpha channel. Measured on glyph edge pixels:
99.6% carried colour fringing on the WPF side, 0% on ours.

`AlphaMode.Ignore` plus `TextAntialiasMode.Cleartype`, then a sweep of rendering mode, gamma,
contrast and ClearType level against the WPF capture:

| | mean abs diff | pixels differing by >8 | by >64 | max |
|---|---|---|---|---|
| greyscale (default) | 2.79 | 8.73% | 4.12% | 139 |
| ClearType | 0.80 | 7.74% | 0.12% | 67 |
| ClearType, Natural, gamma 2.2 | **0.54** | **3.16%** | **0.12%** | **67** |

Gamma plateaus at 2.2 - identical results through 3.0 - and contrast changes nothing at all.
`NaturalSymmetric` is worse than `Natural`. The system's own per-monitor ClearType parameters
ranked mid-pack rather than best, so WPF is not simply using those either.

Amplifying the residual 6x shows faint outlines around glyph edges and nothing else: no shifted
glyphs, no doubled edges, no structural disagreement. What is left is a small difference in
antialiasing weight.

**Treat the exact figures above as indicative, not settled.** Two faults were found in the
harness afterwards:

- **DirectWrite was re-wrapping the text.** `CreateTextLayout` takes a maximum width and wraps
  inside it by default, while the `FormattedText` it was being compared against had no
  `MaxTextWidth` and did not wrap at all. Any line long enough to wrap had its tail clipped,
  because the layout was given a maximum height of one line. `WordWrapping.NoWrap` is also the
  correct setting for the presenter proper: the canvas wraps in `LayoutEngine` and hands the
  renderer visual lines that are already final, so wrapping again would re-flow finished text.
- **The capture reads the desktop.** `CopyFromScreen` returns whatever is on screen at those
  coordinates, so anything covering the window is measured instead. It shows up as a near
  uniform shift - close to 100% of pixels differing a little and almost none differing a lot -
  and it is not reliably avoidable while sharing a desktop. The sweep now checks the captured
  background against the theme colour and refuses to report numbers when they disagree.

The qualitative findings hold: alignment is exact, ClearType versus greyscale was the whole
disagreement, and the amplified difference image shows glyph edges rather than displaced text.
The decimals do not, and the A/B on a real gesture is what settles it anyway.

**So the seam is workable and C3 proceeds as planned.** The always-on presenter, which would
have dissolved the seam by never handing back, is not forced on us.

Two things this does not yet cover. It is one font at one size in one colour, unwrapped and
unstyled - bold, italic, code, colour spans and tables all still have to agree. And a static
diff cannot say whether a single-frame change of that magnitude is perceptible at the moment of
handoff; that needs an A/B on a real gesture.

### C3, in practice: three faults, all in the handoff

Built as `RaisinDocs.TestApp --handoff` (F9 toggles the presenter, `--bgtest` drives its own
gesture and samples its own pixels, `--monitor=N` picks a display). Each fault was found by
instrumenting rather than reasoning, after several wrong guesses each time.

**The text jumped back for a few frames at the end of every gesture.** WPF is not updated while
the presenter owns the scroll, so it still shows wherever the gesture began - the log reads
"WPF was showing 0.0, 120px stale" every time. Hiding the surface from a queued callback
uncovered WPF before its new frame had been presented. The surface now waits three
`CompositionTarget.Rendering` ticks, which are real render passes rather than a queue priority.

**The surface swallowed the wheel.** It covers the canvas during a gesture, so the pointer is
over it rather than over WPF, and mouse messages go to the window under the pointer. Measured
with synthetic wheel input: 2 of 6 notches arrived with the presenter engaged, against 6 of 6
without it, so a spin travelled far less and felt slower. Returning `HTTRANSPARENT` from
`WM_NCHITTEST` makes hit testing skip the surface entirely. **A presenter must be input
transparent** - it is not only a thing to look at.

**It must not be an `HwndHost`.** WPF excludes a hosted window's region from its own rendering
for as long as the host is in the visual tree, so hiding the child left a hole showing whatever
was behind the window. Parenting the child window directly means WPF renders its whole client
area and the surface simply covers part of it.

With those fixed, five rapid notches give 600px in 1.11s through WPF and 600px in 1.13s through
the presenter, and the handoff itself is reported as a sub-pixel difference on an SDR display.

### The colour management problem

On an HDR display the two renderers do not agree, and it is not fixable in the renderer.

Measured with the same build and the same injected gesture on three displays:

| display | colour space | background samples above threshold |
|---|---|---|
| SDR, 8 bit, 270 nits | `RgbFullG22NoneP709` | 0 of 158 |
| **HDR, 10 bit, 604 nits** | `RgbFullG2084NoneP2020` | **96 of 151** |
| SDR, 8 bit, 270 nits | `RgbFullG22NoneP709` | 0 of 152 |

On both SDR displays every background sample reads the theme colour exactly. On the HDR display
the presenter reads about 72 and WPF about 99 where both should read 30: WPF goes through the
SDR-in-HDR composition path and the swapchain does not, so the same nominal colour arrives at a
different luminance. Saturated colours are untouched - magenta comes back as exactly 255,0,255 -
which is the signature of a luminance mapping rather than a rendering fault. Declaring the
swapchain `RgbFullG22NoneP709` succeeds and changes nothing.

**Found: the flip model was bypassing composition.** DWM can promote a flip-model swapchain to
a hardware overlay plane or DirectFlip and scan it out without compositing it at all. That is
the performance feature the flip model is famous for, and on an HDR display it is exactly the
wrong thing: composition is where SDR content gets its white level scaled to the display's SDR
content brightness, so a bypassed swapchain skips the conversion every other window receives.

Bitblt cannot be promoted - it always goes through composition - which makes it the test. Same
build, same injected gesture, same HDR display, one line different:

| swap effect | background samples above threshold |
|---|---|
| `FlipSequential` | 106 of 175 (presenter 72, WPF 117) |
| `Discard` (bitblt) | **0 of 158 - every sample exactly 30** |

**And the cadence survives it.** Sustained over thirteen consecutive samples on the 280Hz
panel: median 3.57 to 3.64ms, 277 to 280 presents a second, 0.0 to 1.0% late. Against the flip
model's 3.57ms and 0.0 to 1.3%, and against WPF's 7.00ms with 19% late.

So the pacing never depended on bypassing the compositor. `Present(1, ...)` on a dedicated
thread is what holds the cadence; WPF cannot do that because its presentation is driven from
the UI thread. Composition costs about a percent of frames and buys pixel-exact agreement with
WPF on every display type.

The production form of this is a composition swapchain -
`CreateSwapChainForComposition` with a DirectComposition visual, which is composed by design and
still supports the frame latency waitable object. Bitblt proves the principle; that is the
shape to build.

What remains is that the ClearType and gamma sweep was fitted on an HDR display against a target
that was already being transformed, so those numbers should be taken again now that both
renderers are composed alike.

### C3 against the real renderer: the presenter replays the canvas display list

`RaisinDocs.TestApp --replay --monitor=N`. The real DocsCanvas with the trading report in
visual mode, F9 switching between its own scrolling and the presenter.

**The presenter does not render the document.** DocsCanvas already draws each visual line into
a DrawingVisual, and a DrawingVisual keeps the display list: glyph runs carrying the indices and
advances WPF resolved, geometries with their brushes, images with their rectangles. The
presenter replays that, so the two draw the same thing by construction. Reproducing wrapping,
block fonts, inline styles, colour spans, tables and images a second time would have made every
one of them a place the two could disagree.

The list is cloned and frozen per line before it crosses to the render thread, and the canvas is
asked to keep building lines under wherever the presenter has reached
(`PrepareLineVisualsAt`), since the canvas itself does not move during a gesture.

**Replaying every frame was far too dear**: 7 to 11ms on table-heavy content, which made the
presenter slower than WPF. It is the same work each frame, so each line is now drawn once into
a GPU texture and a frame is a few hundred blits - phase 2's trick applied on our side of the
fence. That is legitimate only because nothing moves sub-pixel: glyphs sit on a whole pixel
grid, horizontal position is fixed per line, and scrolling is whole-pixel, so a pre-rendered
line is exactly valid at every offset it will be drawn at.

| | per frame | rate |
|---|---|---|
| replay every frame | 7 - 11 ms | 95 - 132/s |
| **texture cache** | **0.29 - 0.57 ms** | **268 - 279/s** |

Sustained over twelve gestures on the real document: present median 3.58 to 3.73ms, 2.5 to 7.1%
late, UI thread 0.03 to 0.60ms a frame. Against the real canvas at 140/s with 19% late.

Three faults were found by looking rather than reasoning, and all three were mine: the display
list was replayed at `Round(y - offset)` where the canvas rounds the line and the offset
separately, so line spacing changed every frame and the text jiggled; the texture was
translated vertically but not horizontally, so every indent was applied twice; and images were
not drawn at all.

### ClearType is missing from the editor's own cache

The last visible difference was antialiasing, and chasing it found something about the shipping
renderer rather than the presenter.

ClearType needs to know what is behind a glyph, so it cannot be used on a transparent surface -
and a `BitmapCache` is one. **Every line the editor has cached since phase 2 has been greyscale
antialiased rather than ClearType.** Confirmed by filling each line visual with the theme
background first (`DocsCanvas.OpaqueLineVisuals`): the text visibly sharpens.

That also settles the seam. Both sides render through a transparent cache, so both are
greyscale at DirectWrite's own gamma, and at that setting the presenter and the canvas produce
the same text - with no fitted constant. The gamma 2.2 the earlier sweep produced was fitted
against a directly drawn panel on an HDR display, and does not apply here.

Making lines opaque is a real improvement and a real piece of work, not a flag. Anything drawn
at or under a line boundary is covered by the next line's background: table row separators
disappear, and selection, search highlights and the code and colour block backgrounds would go
the same way. Restoring ClearType means moving all of those into the line visuals.

### How it was proved

Phase 1 of the pre-buffering work paid for itself by answering one question cheaply before
anything was built on it. Do the same here.

A throwaway window: flip-model swapchain, latency waitable object,
`SetMaximumFrameLatency(1)`, a static bitmap scrolling past. Measure with FrameView.

**The only question: does it hold an even cadence, with no refresh-multiple jumps?**

If it cannot, the idea dies for a few hours' work rather than a few weeks'. If it can, the
measured gap between that and what WPF delivers is the size of the prize, and worth weighing
against the seam, the ring buffer and the airspace before going further.

## Order

1. **A**, then **B**. Small, correct, and they may make C unnecessary.
2. Re-measure. If the residual no longer bothers a reader, stop.
3. Only then the **C prototype**, and only build the real thing if the prototype holds cadence.

## Update, 2026-09-05: the wheel was pacing itself off a wall clock

The figures this note is built on - 228 presents a second into a sink changing 140 times a
second, 13% dropped, late intervals at 2 and 5 refreshes - were all taken from wheel gestures.
They were real. They were not only WPF's doing.

`OnWheelFrame` read a `Stopwatch` and restarted it at the top of the handler, sixty lines
before the duplicate-frame check. Repainting from inside that handler makes `Rendering`
free-run at several hundred a second, so the clock was timing the slivers between duplicate
raises rather than the interval the panel shows. The coast therefore advanced by a different
amount between one displayed frame and the next **even when composition was perfectly
regular**, and asked for repaints at a phase that slid against the compositor. Fixed in
`cd7cf24` by taking `dt` from `RenderingTime` and returning early on duplicate raises.

Release against Release, same document, same panel:

| wheel gesture | before | after |
|---|---|---|
| composition median | ~3.15 ms, wandering | **3.57 ms = 1/280, every gesture** |
| jitter, over 1.5x median | avg **8.4%** | avg **3.1%** |
| paint interval | ~135/s | **280/s** |
| composed frames painted | 98 of 211 | **1992 of 1992** |

The cadence is now locked to the panel period rather than free-running past it, which is the
symptom section C exists to cure. 3.1% is not 0%, so something is still being missed - but the
gap between that and a presenter project spanning C1, C2 and C3 is very different from the gap
that justified proposing one.

### What this does not overturn

- The prototype measurements in C1-C3 stand. A paced swapchain does present at 3.57 ms with 0%
  over 1.5x median, and the ClearType and colour findings along the way were worth having -
  `### ClearType is missing from the editor's own cache` is what set off
  `design/Opaque Line Visuals.md`.
- [dotnet/wpf#11607](https://github.com/dotnet/wpf/discussions/11607) is still a real report of
  the same class of problem from someone else's codebase.
- Nothing here has been re-measured with FrameView from outside the process, which is what
  produced the 228-into-140 figure. The in-process log says the cadence is locked; confirming
  that the panel agrees needs the external tool.

### What to do before building the presenter

Re-run the FrameView capture that produced the original figure, against the fixed coast. If
presents now match the sink, section C is solving a problem that has largely gone. If they do
not, the remaining gap is measured rather than assumed, and the case is a real one.

## Re-captured, 2026-09-05: the drops are gone

The re-capture this note asked for, done. `capture-scroll.ps1` drives a repeatable wheel sweep
and runs PresentMon against the editor; `analyse-scroll.ps1` slices the result per gesture by
QPC. Release build, 2982 frames, ten gestures, 280Hz panel.

| | this note, before | re-captured |
|---|---|---|
| presented | 228/s | **273/s** |
| displayed | 140/s | **273/s** |
| dropped | **13%** | **0.0%** - 0 of 2982 |

Not an artefact of reading the wrong column: `DisplayedTime` is populated on all 2982 rows and
never `NA`, and `PresentMode` is `Composed: Copy with GPU GDI`, the expected WPF path.

**What is left**, as display intervals against the 3.571 ms refresh, counting only frames inside
a gesture - idle settle time between gestures otherwise inflates the long tail:

| held for | with a video app running | machine quiet |
|---|---|---|
| 1 refresh | 94.8% | **98.0%** |
| 2 refreshes | 4.1% | **1.3%** |
| 3 refreshes | 0.4% | 0.2% |
| 4+ refreshes | 0.7% | 0.5% |

**Two thirds of the double-holds were another application.** The first capture was taken with a
video-heavy app in the background; closing it took two-refresh holds from 4.1% to 1.3% and
tightened animation error's p95 from 1.5-4.4 ms to 1.07-1.88 ms. Per-gesture jitter fell from
1.8-11.9% to 1.0-6.9%, and on the 2.24 s sustained scrolls it is 1.0-1.8%.

Nothing was dropped in either run, so that finding did not depend on machine state. The
histogram did, which is worth remembering before quoting one.

**On a quiet machine 98% of frames land on exactly one refresh at 280 Hz.** What remains is
about 2% held an extra refresh, with zero dropped.

Animation error agrees: median 0.21-0.49 ms per gesture, p95 1.5-4.4 ms. The motion matches the
time each frame was actually on screen. The large maxima in the per-gesture output are at
gesture boundaries, where the previous-frame delta spans an idle gap; they are an artefact of
slicing rather than stutter.

### What this means for section C

The presenter was proposed because WPF presented 228 times a second into a sink that changed
140 times, discarding 13%. That is no longer what happens. **Do not build it to fix a drop rate
of zero.**

What remains on a quiet machine is about 2% of frames held an extra refresh. We present at
roughly 276/s into a 280 Hz panel, and that shortfall alone accounts for most of it. There is
not obviously anything left to chase.

**Machine state has to be part of a capture's record.** The difference between a busy and a
quiet machine here was larger than the difference any code change in this work made. It happened
three times: a video-heavy app once, and leftover build processes twice - twenty-eight of them
holding 3.3 GB after an afternoon of builds, kept alive by MSBuild node reuse and surviving an
attempt to clear them. `dotnet build-server shutdown` is what retires those.

`capture-scroll.ps1` counts them before launching and says so now, and keeps PresentMon's own
output beside the capture, because lost ETW events are reported there and nowhere else - the
noisy run lost 661, a clean one loses single digits. `analyse-scroll.ps1` repeats the warning,
scaled against the capture, since a capture is read weeks later by someone who no longer
remembers what was running.

**The final figures**, quiet machine, scripted sweep, 3114 frames: every wheel gesture presented
and displayed 256-279/s at a 3.57 ms interval - exactly 1/280 - with 0.0% dropped and 1.1-2.5%
of intervals running long during sustained scrolling. Animation error median 0.22-0.33 ms.

The same run measured a minimap drag for the first time: 108-110/s, a 7.15 ms interval, and an
animation error median of 3.73-7.12 ms. The drag is bound by the rate mouse messages arrive and
carries about twenty times the wheel's error, which inverts the observation this whole
investigation began from. See `design/Scroll Pre-Buffering.md`.

What still stands from C1-C3: the prototype numbers, the ClearType finding that set off
`design/Opaque Line Visuals.md`, and [dotnet/wpf#11607](https://github.com/dotnet/wpf/discussions/11607)
as someone else's report of the same class of problem. What does not stand is the motivation.

## The baseline set: other refresh rates, other window sizes

**Run in full, 2026-09-05.** The refresh axis found a fault; the size axis is clean. Both below.

Everything measured so far was taken in one configuration: whatever size the editor happened to
open at, on the 280 Hz primary. Two reasons that is not enough.

**Proof across refresh rates.** WPF has behaved oddly at different rates before, and every
conclusion in this note rests on captures from a single panel. "0.0% dropped" and "98% on one
refresh" are claims about this machine at 280 Hz until they have been seen somewhere else. A
280 Hz panel is also the *easy* case in one specific way and the hard case in another: the budget
is only 3.57 ms, but the cost of missing it is one cheap refresh. At 60 Hz the budget is 16.67 ms
- nearly five times as forgiving - but a miss costs 16.67 ms, which is visible.

**A baseline to compare against later.** This is the reason that does not need the first one to
find anything. A future change to the render path is judged against numbers taken before it, and
those numbers have to exist across the range the app will actually run on, recorded well enough
to be trusted weeks later. A baseline's value is precisely that it changes nothing now.

### Two axes, varied one at a time

The three 1920x1080 panels differ only in refresh rate, so they are the refresh sweep at a
constant window size. Size varies on the primary alone, at a constant refresh rate. Nothing
moves both at once - a capture that changed size and rate together could not be attributed to
either.

| | `\.\DISPLAY8` | `\.\DISPLAY7` | `\.\DISPLAY9` | `\.\DISPLAY6` |
|---|---|---|---|---|
| resolution | 1920x1080 | 1920x1080 | 1920x1080 | 2560x1440 |
| refresh | 60 Hz | 100 Hz | 144 Hz | 280 Hz |
| frame budget | 16.67 ms | 10.00 ms | 6.94 ms | 3.57 ms |

Window height is the multiplier on per-frame work, because it sets how many lines have to be
produced. 1920x1032 is the full working area of a 1920x1080 panel under Windows 11 with the
taskbar in place, and it is the widest and tallest window the refresh sweep can hold constant
across all three.

### Running it

```powershell
# Refresh sweep - size held at the working area of each 1920x1080 panel
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY8 -Maximise
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY7 -Maximise
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY9 -Maximise

# Size comparison - one panel, one refresh rate, three heights
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY6 -Size 1200x800
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY6 -Size 1920x1032
.\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY6 -Maximise
```

Each capture writes a `.meta` file beside the CSV recording the display, its refresh rate, the
window rectangle, the document *and its length*, and whether the machine was quiet. The document
is part of the configuration - it sets how many lines exist to scroll through - and this note is
one that grows, so the line count is recorded rather than the name alone. `analyse-scroll.ps1` prints it
back before the numbers. That is not bookkeeping for its own sake: the difference between a busy
and a quiet machine was larger than the difference any code change in this work made, so a
capture without its conditions cannot be compared with anything.

**Take them in one sitting on a quiet machine**, for the same reason. A sweep assembled from runs
taken days apart measures the days as much as the panels.

### What to record

| display | Hz | window | presented/s | never shown | 1-refresh | animation error p50 |
|---|---|---|---|---|---|---|
| DISPLAY8 | 60 | 1920x1032 | 63-73 | **34.9%** | 16.67 / 33.32 ms | **12.9 ms** |
| DISPLAY7 | 100 | 1920x1032 | 91-101 | not recomputed | 12.2-20.1 ms | **11.8 ms** |
| DISPLAY9 | 144 | 1920x1032 | 132-141 | **35.4%** | 6.95 ms | **4.0 ms** |
| DISPLAY6 | 280 | 2560x1392 | 236-273 | **2.6%** | 3.57 ms | **0.78 ms** |

The "never shown" column replaces a "dropped" one that read 0.0% everywhere. It was not measured -
see the correction below.

**What would be interesting.** Presented per second tracking the refresh rate on each panel, and
the unshown share staying low. Anything else is the finding: presented rate *not* following the panel
means the pacing is locked to something other than the compositor; drops appearing only at low
refresh rates means the work is not the constraint, the wait is; a size that degrades at constant
refresh rate means per-frame cost scales with visible lines more steeply than it should.

~~**The likeliest outcome is that it all confirms what is already here**~~ - it did not. The first
cell run found the fault below, which had been invisible on the primary.

### Found on the first cell: the composition clock follows the primary, not the panel

939 frames, quiet machine, Release, editor filling the working area of the 60 Hz panel.

| | DISPLAY6, 280 Hz | DISPLAY8, 60 Hz |
|---|---|---|
| dropped | 0.0% | **0.0%** |
| intervals over 1.5x median | 1.1-2.5% | **29-44%** |
| animation error, median | 0.22-0.33 ms | **12.9 ms** |
| animation error as a share of the frame budget | 8% | **77%** |

Nothing is dropped at 60 Hz either, so that finding travels. Nothing else does.

The app's own log says why:

```
wheel gesture 2.22s on \.\DISPLAY8 60Hz   263 ticks, 133 paints
    composition frame median  7.13ms (140/s)
    paint interval      median 17.85ms (56/s)
```

**`CompositionTarget.Rendering` fires at 140 Hz while the window is displayed at 60 Hz.** 140 is
half of 280, the primary's rate - the composition clock is derived from the primary display and
not from the panel the window is actually on. The editor correctly identifies the panel it is on;
WPF does not pace to it.

The consequence is a beat rather than a shortfall. 16.67 ms is not a multiple of 7.13 ms, so a
paint that wants to land once per display refresh has to take either two compositor ticks
(14.28 ms) or three (21.42 ms). It alternates between them: 133 paints in 2.22 s is 59.9/s, which
is exactly the panel rate, while the *median* interval is 17.85 ms - about two and a half ticks.
**The frame count is right and the frame times are wrong**, by roughly +-3.5 ms, systematically.

That is the mechanism. The size of the resulting animation error - 12.9 ms, most of a frame - is
larger than +-3.5 ms of jitter alone accounts for, so the beat is demonstrated and the full
arithmetic of the magnitude is not yet.

**What this does not overturn.** Zero dropped frames holds on both panels. The wall-clock pacing
bug found on 2026-09-05 was real and its fix was real. What has to be narrowed is the headline:
"98% of frames land on exactly one refresh" is a result *for a window on the primary display*,
where the composition grid and the display period are the same 3.57 ms and everything therefore
locks. It was never a claim about the app on any monitor, and it read like one.

**Why this was invisible until now.** Every previous capture was taken on the primary. The one
configuration where the composition clock happens to equal the display clock is the one that was
always measured. This is the case reason 1 was written for.

### The sweep, completed: severity tracks how badly the two clocks fit

All three 1920x1080 panels, quiet machine, Release, window filling the working area. Nothing
dropped anywhere.

| panel | display period | composition grid the app saw | display interval, median | animation error p50 | as a share of budget |
|---|---|---|---|---|---|
| DISPLAY6, 280 Hz (primary) | 3.57 ms | 3.57 ms | 3.57 ms | 0.22-0.33 ms | **8%** |
| DISPLAY9, 144 Hz | 6.94 ms | 3.57 ms | **6.95 ms** | ~4.0 ms | **58%** |
| DISPLAY8, 60 Hz | 16.67 ms | 7.13 ms | 16.67 / 33.32 ms | 12.9 ms | **77%** |
| DISPLAY7, 100 Hz | 10.00 ms | 7.13 ms | 12.2-20.1 ms | 11.8 ms | **117%** |

**The composition grid is the primary's, on every panel.** 3.57 ms is DISPLAY6's period and
7.13 ms is exactly two of them; the app never once saw a tick at 10.00 ms or 16.67 ms, the periods
of the panels it was actually being displayed on. It identifies the panel correctly in its own log
and paces to a clock belonging to a different one.

**How badly that hurts depends on arithmetic, and the ordering confirms the mechanism.** Painting
on a grid of 3.57 ms into a panel of period P, the mismatch is how far P sits from a whole number
of ticks:

| panel | P / 3.57 ms | distance from a whole tick | measured error / budget |
|---|---|---|---|
| 280 Hz | 1.00 | 0% | 8% |
| 144 Hz | 1.94 | 6% | 58% |
| 60 Hz | 4.67 | 33% | 77% |
| 100 Hz | 2.80 | 20%, and the grid halves to 7.13 ms here - 1.40 ticks, 40% out | 117% |

The measured severity orders exactly as the fit does. That is the mechanism confirmed, on three
panels, rather than a coincidence read off one.

**At 100 Hz the animation error exceeds a whole frame period.** It is the worst fit of the four
and the only rate where the median display interval never once equals the panel's period.

### Where the prediction was wrong, and what that adds

144 Hz was predicted to "look like the primary". It does not. It locks its *display interval* -
6.95 ms on all nine gestures, against a wandering 12-20 ms at 100 Hz - and still carries 4.0 ms of
animation error, twelve times the primary's.

Both are explained by the same number. The app paints every second composition tick, 7.14 ms, into
a panel whose period is 6.94 ms. Each paint is 0.2 ms late, which is far too little to change
which refresh a frame lands on - so intervals look perfectly locked - but it accumulates, slipping
a whole frame roughly every 35, and animation error measures exactly that drift. **A locked
interval is not the same as correct timing**, and 144 Hz is the case that separates the two.

That reading of the 144 Hz residual follows from the mechanism rather than being separately
measured; the ordering across four panels is the part that is established.

### What this does and does not change

**Zero dropped frames survives everywhere** - four panels, four refresh rates, 0.0% throughout.
The wall-clock pacing bug found earlier was real and its fix was real.

**The headline was too broad.** "98% of frames land on exactly one refresh" is a result for a
window on the primary, where the grid and the panel are the same 3.57 ms and everything therefore
locks. Every capture before today was taken in that one configuration, which is the only one where
the fault is invisible.

**What would fix it** is pacing to the panel the window is on rather than to whatever clock
`CompositionTarget.Rendering` offers. That was investigated on 2026-09-06 and the answer is that
the application cannot do it. See below.

### Investigated: the app cannot fix this from the paint side

The obvious suspicion was our own paint scheduling, and the raw capture seemed to support it. On
the 60 Hz panel, presents are bimodal - p10 3.07 ms, median 10.55, p90 42.57, against a locked
3.57 ms on the primary - and **438 of 939 presents are under 6 ms, 63% of them immediately after
one over 20 ms**. A 16.67 ms paint gate cannot emit a 3 ms present, so the gate looked like the
culprit: it took the first composition tick at or after the ideal time and carried the remainder,
and a long tick - p99 on a secondary panel is 21-28 ms - leaves most of a period banked, making
the next tick 7 ms later immediately due.

That reasoning was wrong, and the experiment is what showed it. The gate was changed to paint on
the tick *nearest* the ideal time, bounding the carry at half a tick so arrears cannot build.
Re-measured on the same panel:

| 60 Hz panel | before | nearest-tick gate |
|---|---|---|
| animation error p50 | 12.62 ms | 12.87 ms |
| intervals over 1.5x | 38.3% | 45.0% |
| presents under 6 ms | 47% | **50%** |

Nothing moved, and the burst pattern the change was designed to remove got marginally worse. The
primary was unaffected either way (0.60-0.69 ms error, 0.8-2.0% long), which is the expected
no-op there since its tick period and display period are the same.

**Why it could not have worked.** Our own paint log for the same run says the paints were regular
all along:

```
wheel gesture 0.51s on \.\DISPLAY8 60Hz   65 ticks, 30 paints
    composition frame median  7.14ms (140/s)  over 1.5x median 32.3%
    paint interval      median 17.85ms  (56/s)  over 1.5x median  6.7%
```

We paint every 17.85 ms with 6.7% of intervals running long, while the compositor ticks at 32%
irregularity and half the presents are under 6 ms. **Presents are not our paints.** WPF's render
thread presents the swapchain on its own clock whether or not the content changed, so the paint
gate decides when the *content* updates and has no influence on when a frame is presented. There
was never a lever here.

That is why the two clocks cannot be reconciled from inside the application: the content changes
at the panel's rate, the presents happen at the primary's, and the panel displays whichever
present is latest at each vblank - carrying content that is up to a full content-period stale. The
mismatch is structural.

**What is left, none of it cheap.** Presenting through a path we control rather than WPF's - which
is section C of this note, retired on measurement for the primary and never evaluated for this
case. Or waiting on the target output's vblank via DXGI and driving composition from that, which
WPF gives no way to do. Both are large, and the fault only appears on a secondary monitor whose
refresh rate is not a divisor of the primary's.

**Recommendation: leave it.** It is measured, understood, attributable to WPF rather than to this
code, and bounded - nothing is dropped on any panel, and the effect is invisible on the primary,
where the application spends nearly all its time. The value of the investigation is knowing which
of the three candidate causes it was, and that the paint gate is not it.

### The size axis: flat, then a step at full screen

Three window sizes on the primary, refresh held at 280 Hz. Wheel gestures only - the minimap drag
is a different path and is discussed separately above.

| window | pixels | animation error p50 | intervals over 1.5x, sustained scrolls | presented/s |
|---|---|---|---|---|
| 1200x800 | 0.96 M | 0.26 ms | 1.6-3.6% | 263-276 |
| 1920x1032 | 1.98 M | 0.28 ms | 1.1-3.2% | 239-277 |
| 2560x1392 | 3.56 M | **0.78 ms** | 2.9-9.4% | 236-273 |

Nothing dropped at any size, and the display interval is 3.57 ms throughout.

**Doubling the window costs nothing; the third step costs 3x.** 1200x800 to 1920x1032 more than
doubles the pixels and moves animation error by 0.02 ms, which is noise. Going to the full working
area triples it. Whatever this scales with, it is not pixel area over this range - three points do
not determine the curve, and no attempt is made here to fit one.

**It is not our rendering.** `canvas-onrender` averages 0.00-0.01 ms per pass at every size, the
same as it does in the small window. The extra cost is below us: more and larger cached line
visuals for WPF to rasterise and composite. That is worth knowing before anyone optimises the
render callback in response to these numbers.

**In absolute terms this axis is fine.** 0.78 ms against a 3.57 ms budget is 22%, at the largest
window the machine has, on the fastest panel. Compare the refresh axis above, where the same
metric reaches 117% of budget. **Size is a minor effect and refresh rate is a major one** - which
is the ranking worth taking away, and the opposite of what a per-frame cost model would predict.

One app-side cost was large enough to name: `minimap-rebuild` ran 2-13 ms, and 13.3 ms is nearly
four frames at 280 Hz. **Investigated and fixed** - the minimap's bitmap cache invalidated itself
on a single line of movement, so it rebuilt about fifty times per drag instead of once. The same
sweep records 3 rebuilds where it recorded 73, and none during a drag.

Re-measured on the same window and panel. The middle column is a capture taken with build
processes running and is kept only to show why it is not quoted:

| maximised, 280 Hz | before (quiet) | after (contaminated) | after (quiet) |
|---|---|---|---|
| rebuilds across the sweep | 73 | 3 | **8** |
| wheel animation error p50 | 0.69-0.89 ms | 0.70-1.00 ms | **0.60-0.67 ms** |
| sustained intervals over 1.5x | 2.9-9.4% | 3.3-4.6% | **1.0-1.6%** |
| minimap drag animation error p50 | 7.88-10.55 ms | 8.46-11.23 ms | 9.19-10.58 ms |
| dropped | 0.0% | 0.0% | 0.0% |

**The drag is unchanged and the sustained wheel scroll is about three times cleaner.**

The drag result is the expected one: it runs at 108-110/s because that is how fast mouse messages
arrive, not because rendering cannot keep up, so rebuilds during it were absorbed in slack that
existed anyway.

The wheel result was not expected, and is mechanically coherent. The 2.2 s sustained gestures were
precisely the ones carrying `minimap-rebuild x50` before - roughly 150 ms of glyph rasterisation
inside a 2200 ms gesture, on the same thread, now one or two rebuilds. Their share of long
intervals falls from 2.9-9.4% to 1.0-1.6%.

### Pinned down: two clean pairs

The pre-fix binary was rebuilt from the parent of the fix commit and re-measured, so both sides
have two clean captures on the same window, panel and document. Sustained 2.2 s wheel gestures -
the ones that carried 50 rebuilds each - three per run:

| | intervals over 1.5x | animation error p50 |
|---|---|---|
| pre-fix, run A | 9.4 / 2.9 / 4.6% | 0.80 / 0.72 / 0.75 ms |
| pre-fix, run B | 9.7 / 1.2 / 1.3% | 0.73 / 0.64 / 0.64 ms |
| post-fix, run A | 1.5 / 1.1 / 1.0% | 0.62 / 0.61 / 0.62 ms |
| post-fix, run B | 1.8 / 2.2 / 1.2% | 0.64 / 0.65 / 0.63 ms |

**What the fix removes is the tail, not the median.** Pre-fix, one gesture per run ran at 9.4-9.7%
while the others sat between 1.2% and 4.6%. Post-fix, no gesture in either run exceeds 2.2%. The
pre-fix best case and the post-fix typical case are the same thing - the difference is entirely
that the bad gesture stops happening.

Both bad gestures were the **first** sustained scroll of their run, which is where the minimap has
furthest to travel from a cold cache. That is where fifty rebuilds hurt and where one does not.

Medians across the six samples each way: 3.75% to 1.35% of intervals running long, and 0.73 ms to
0.63 ms of animation error. The animation-error ranges barely overlap (0.64-0.80 against
0.61-0.65), so that 0.1 ms is consistent - and it is 3% of a 3.57 ms budget, which is small.

**So: "about three times cleaner" holds for the median and understates what actually changed.**
The honest claim is that the worst gesture in a session went from ~9.5% to ~2%, and that an
already-good typical gesture improved slightly.

Two earlier versions of this passage were wrong in opposite directions - "changed nothing that can
be seen", written from a capture with build processes running, and then a 3x claim from a single
pair. Four clean captures is what it took.

### Superseded: what the remaining cells were expected to test

The sweep is no longer confirmation. If the composition clock is pinned near 140 Hz regardless of
the panel, each remaining rate beats against it differently and predictably:

| panel | display period | ticks per refresh at a ~7.13 ms grid | expected |
|---|---|---|---|
| 100 Hz | 10.00 ms | 1.40 | strong beat, worst of the three |
| 144 Hz | 6.94 ms | 0.97 | nearly locked - should look like the primary |
| 280 Hz | 3.57 ms | 0.50 | locked (measured) |

If 144 Hz comes back clean and 100 Hz comes back bad, the grid explanation is confirmed and the
fault is fully characterised as a mismatch between two clocks rather than a cost problem. If
144 Hz is also bad, the explanation is wrong and the composition rate has to be read directly on
each panel before anything else is concluded.

The first thing to check on each remaining cell is the logged `composition frame median`, not the
capture: it says what clock WPF chose, which is the independent variable this has turned into.

## Correction, 2026-09-06: every "0.0% dropped" in this note was fabricated

`analyse-scroll.ps1` decided whether a present had been displayed by looking for a `DisplayedTime`
column (PresentMon 2.x) or a `Dropped` one (1.x). **The 2.5.1 binary this harness pins emits
neither.** It has `MsBetweenDisplayChange`, which is `NA` exactly when a present was never shown.
With no column matched, the function returned `true` - "nothing to go on, assume it was shown" -
so every capture taken with the pinned tool reported a dropped share of 0.0% unconditionally.

That is not a conservative default. It reports the good answer when it knows nothing, and it did
so in every capture in this note taken after the move to 2.5.1. The function now throws instead.

**What the figure actually is**, recomputed from `MsBetweenDisplayChange`:

| capture | reported | actual, never shown |
|---|---|---|
| 280 Hz primary, full screen | 0.0% | **2.6%** |
| 144 Hz | 0.0% | **35.4%** |
| 60 Hz | 0.0% | **34.9%** |

**What survives.** The claim that matters for the primary - that the 13% drop rate this note was
built on is gone - survives: 13% to 2.6% is still the finding, and every other primary-panel
metric was measured correctly. The claim that "nothing is dropped on any panel", used to argue the
composition-clock fault was bounded, does not: **a third of our presents on a secondary panel were
never shown.**

The irony is that `get-presentmon.ps1` pins the tool version with a comment explaining that
PresentMon renames columns between majors and an unpinned tool would silently change what a
harness reads. The tool was pinned. The reader was not updated to match it, and the mismatch
failed silently in the one direction nobody checks.

## The throttle was the fault, not WPF

`ScrollController` capped repaints at the panel's refresh rate - `_sinceDisplayFrame >=
_displayPeriod` - on the reasoning that the panel cannot show more, so composing more is waste.
That reasoning is wrong whenever WPF's composition clock is faster than the panel, which is every
non-primary display here.

WPF presents on its own clock whether or not we invalidated. Throttling content updates to 60/s
while presents happen at 280/s means the panel picks up, at each vblank, a present carrying
content of an age set by our accumulator's phase rather than by the vblank - stale by up to a full
16.67 ms period. Not throttling means every present carries fresh content, and whatever the panel
samples is at most one composition tick old.

Measured on DISPLAY8, quiet machine, same window and document:

| 60 Hz panel | throttle on | throttle off |
|---|---|---|
| animation error p50 | 12.87 ms | **1.27 ms** |
| as a share of budget | 77% | **8%** |
| intervals over 1.5x, sustained | 41-45% | **0.7-1.5%** |
| display interval | 16.67 / 33.32 ms | **16.67 ms** |
| presented | 63-73/s | 268-279/s |
| never shown | 34.9% | 74.6% |

**A tenth of the animation error, and it now matches the primary's 8% of budget.** The unshown
share triples, which is the mechanism rather than a cost: presenting far more often than the panel
refreshes is what guarantees a fresh frame at every vblank.

**The throttle's original justification no longer holds.** It was added because painting at 320-357
a second "collected 46 gen0, 20 gen1 and 17 gen2 collections" in 9.55 seconds. Re-measured now:
**zero collections of any generation, with or without the throttle**, and `canvas-onrender` costs
0.01 ms across 137 passes instead of 30. That justification was measured before per-line cached
visuals, when every frame re-rasterised the world.

**This also settles the presenter question.** The paced-presenter prototype was measured on the
same panel for comparison: animation error median 1.20 ms, display interval 16.67 ms, 77.2% never
shown - the same result the editor now reaches by deleting three lines. The presenter is not
needed for this fault, and section C stays retired.

**The one open cost is power.** Compositing 280 times a second to feed a 60 Hz panel is wasted
work, which matters on a laptop even when it costs 0.01 ms a pass. A rate limit tied to something
other than the display period - or applied only on battery - would be the place to look, and
nothing here has measured it.

### The vblank phase is obtainable, so the wasteful version is not the end of it

Removing the throttle buys correct output by brute force: paint on every composition tick so the
content is never more than one tick old whenever the panel samples it. It costs about four times
the presents - 65 a second against 275 during gestures on a 60Hz panel - of which roughly three
quarters are never shown.

The cheaper version needs to know when the panel's vblanks are, so one paint per refresh can be
aimed at the tick just before one. Probed on 2026-09-06:

| display | VidPnSourceId | measured interval | actual | spread |
|---|---|---|---|---|
| DISPLAY7 | 1 | 10.000 ms = 100.0 Hz | 100 Hz | 9.883-10.119 |
| DISPLAY8 | 2 | 16.665 ms = 60.0 Hz | 60 Hz | 16.524-16.823 |
| DISPLAY9 | 3 | 6.944 ms = 144.0 Hz | 144 Hz | 6.858-7.021 |
| DISPLAY6 | 0 | 12.607 ms = 79.3 Hz | **280 Hz** | 9.930-16.732 |

`D3DKMTOpenAdapterFromHdc` on a DC for the display name, then `D3DKMTCreateDevice`, then
`D3DKMTWaitForVerticalBlankEvent` on the returned adapter, device and source. **Exact on the three
panels that need it**, with min and max inside 1% of the median - so the phase is predictable, not
merely the rate.

DISPLAY6 reports nonsense, and the shape of the nonsense explains itself: 12.6 ms is about 3.5
refreshes of 3.57 ms, ranging over roughly 2.8 to 4.7 of them. The waiting thread cannot be
rescheduled every 3.57 ms, so it misses vblanks and times multiples. That is a thread-wakeup
limit rather than an API one, and it falls on the one panel where the phase is not needed: on the
primary the composition clock already equals the refresh, so every tick is the right tick.

`DwmGetCompositionTimingInfo` is not a route. It fails with `0x88980090` even given a real window
handle.

**Two mistakes worth recording, because both produced confident wrong numbers rather than errors.**
`LUID` is two 4-byte fields and aligns to 4; declaring it as a single `long` forced 8-byte
alignment and pushed `VidPnSourceId` past the end of the struct, so every display reported source
0. And `D3DKMTWaitForVerticalBlankEvent` needs a real device handle from `D3DKMTCreateDevice` -
passing 0 returns success and waits on nothing in particular. Together they reported a plausible
79 Hz for all four displays.

**What this makes possible, and what is still unproven.** A thread waiting on the target panel's
vblank gives the phase; the paint then goes on the composition tick immediately before the next
vblank, at one paint per refresh instead of one per tick. Content stays at most one tick stale for
a quarter of the work. Unproven: that cross-thread signalling does not add jitter of its own, and
that the phase stays stable enough to aim at over a long gesture. Neither is measured, and the
current brute-force version is correct meanwhile.
