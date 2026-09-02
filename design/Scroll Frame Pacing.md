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

### Prove it first

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
