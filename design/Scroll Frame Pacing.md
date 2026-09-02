# Scroll Frame Pacing

Status: **proposed**. Follows on from `Scroll Pre-Buffering.md`, which covers how lines came
to be cached as visuals. This one is about the frames reaching the screen rather than the cost
of drawing them.

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

Sub-pixel scrolling was then tried on top and **reverted**. Over live-rasterised text each
line grid-fits independently and the spacing between them visibly wriggles; over cached
bitmaps the fractional translate resamples, which softens the text and, as a coast decays and
the fraction drifts slowly, beats into a visible interference pattern. Both were worse than
the 1 px stepping they were meant to cure.

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
