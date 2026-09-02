# Rendering Direction

Status: **research**. Written after the paced-presenter work in `Scroll Frame Pacing.md` was
stopped. That work answered a narrower question than it set out to; this is about what to do
next, and it covers two options: patch WPF, or leave it.

## What we actually know

Measured on this machine, a 280Hz panel, with the real editor and a heavy document:

| | frames a second | late |
|---|---|---|
| real `DocsCanvas` | 140 | 19% |
| WPF render ticks during a scroll | 51 - 275, wildly variable | - |
| a dedicated thread calling `Present(1)` | 280 | 0 - 3% |

FrameView showed the shape of it plainly: **228 presents a second into a sink that changed 140
times a second, with 13% dropped.**

That last line is the whole diagnosis. WPF is not failing to draw - it draws more than enough.
It presents at times that do not line up with the compositor's deadlines, so half the work is
discarded. The fault is in *presentation scheduling*, not in rendering throughput.

A dedicated thread presenting with vsync holds a perfect cadence on the same machine, the same
panel and the same compositor. So the ceiling is not the hardware, not DWM's inherent behaviour
and not the GPU.

This is reported by others, not peculiar to this codebase:
[dotnet/wpf#11607](https://github.com/dotnet/wpf/discussions/11607) (gaps at exact multiples of
the refresh period), [#2294](https://github.com/dotnet/wpf/issues/2294) (VRR displays),
[#1908](https://github.com/dotnet/wpf/issues/1908) (`CompositionTarget.Rendering` slowing).

## Which layer is it in?

Three candidates, and they differ enormously in how patchable they are.

**1. WPF managed** — `MediaContext` and the render tick, in `PresentationCore`. Fully open
source C#. But it decides when to *draw*, not when to *present*, so it is unlikely to be the
culprit on its own.

**2. WPF native (milcore)** — `wpfgfx_cor3.dll`, which is where DirectX presentation happens.
**This is open source**, at `src/Microsoft.DotNet.Wpf/src/WpfGfx/` in
[dotnet/wpf](https://github.com/dotnet/wpf), with `core`, `common`, `include` and the rest. The
open-sourcing announcement ([#2554](https://github.com/dotnet/wpf/issues/2554)) confirms
`wpfgfx_cor3.dll` and `penimc_cor3.dll` were released; `PresentationNative_cor3.dll` and a
small library used by wpfgfx, `bilinearspan.lib`, were not.

**3. DWM** — closed, and not patchable. But it has settings-level levers, below.

The measurement points at layer 2. A present rate above the display rate with drops is the
signature of presenting without waiting on a frame-latency signal - exactly what our prototype
fixed by using `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT` and
`MaximumFrameLatency = 1`.

## A. Patch WPF

Feasible, with real caveats.

**Buildable?** Mostly. `bilinearspan.lib` is proprietary and comes as a prebuilt binary from
Microsoft's internal repo, so it cannot be compiled from source. It can be linked against,
but link-time code generation means the MSVC version has to match the one Microsoft's CI used
or the link fails with C1047
([#3946](https://github.com/dotnet/wpf/issues/3946)). So: a pinned toolchain, and a build that
can break when the servicing branch moves.

**Shippable?** `wpfgfx_cor3.dll` is a runtime file next to the app, so a patched build can be
deployed with it. That is also the risk: we would be shipping a forked piece of the .NET
runtime, and re-forking it on every servicing update.

**What the patch would be.** Make the present path pace to the composition deadline - a
waitable swapchain, one frame in flight - instead of presenting whenever a frame is ready.
That is the change our prototype already validated in isolation.

**Before any of that, two free experiments.** Both are reported triggers for precisely this
class of stutter, and both are a settings toggle plus a re-measure:

- **Hardware-accelerated GPU scheduling (HAGS)** — Settings → System → Display → Graphics →
  Change default graphics settings. Reported to cause choppy dragging and scrolling on some
  driver versions.
- **Optimizations for windowed games** — same page. It moves windowed apps from the legacy
  blt-model to the flip-model presentation path, which is the difference we already found
  matters enormously (see the HDR composition finding in `Scroll Frame Pacing.md`).

Neither is a fix we could ship, but either would tell us whether the fault is above or below
WPF, which is worth an hour.

## B. Leave WPF

The constraint that decides this: **this is a document editor, so text quality at 14-16px is
the product.** Any stack that renders text as signed distance fields is scale-invariant but
neither hinted nor subpixel-aware, and that is the wrong trade for body text.

| | pacing | small text | renderer swappable | notes |
|---|---|---|---|---|
| **Avalonia** | native compositor ties frames to refresh rate | ClearType/subpixel via `TextOptions.TextRenderingMode` | no | own open timing issues: [#18960](https://github.com/AvaloniaUI/Avalonia/discussions/18960), [#21213](https://github.com/AvaloniaUI/Avalonia/issues/21213) |
| **WinUI 3 / App SDK** | modern composition stack, deadline-aware | DirectWrite | no | best-supported Windows path; control maturity is the question |
| **Unity / Godot** | excellent, it is what they are for | SDF - documented as poor below ~16pt, no subpixel | n/a | wrong trade for a text editor |
| **NoesisGUI** | yours, it renders through your backend | **dropped subpixel AA in 2.0** | **yes, by design** | XAML, so closest conceptually to WPF; commercial |
| **Slint** | depends on backend | blurry at small sizes; femtovg unhinted, software renderer uses fontdue | yes | |
| **RmlUi** | yours | **pluggable font engine** - DirectWrite could be plugged in | **yes, by design** | HTML/CSS rather than XAML |
| **our own** | proven: 280/s, 0-3% late | DirectWrite/ClearType, proven to match WPF exactly | n/a | we have the canvas; we lack the chrome |

Two rows are worth dwelling on. **NoesisGUI** and **RmlUi** are both explicitly renderer-
agnostic - you supply the drawing backend - which is exactly the shape the last question asks
for. Noesis is XAML and therefore a short conceptual hop from what we have, but it dropped
subpixel antialiasing, which is the one thing we cannot give up. RmlUi keeps the door open
because its font engine is an interface: DirectWrite behind an RmlUi font engine is a real
option.

## What the abandoned work actually proved

Worth separating, because it changes what "drop this approach" means.

The presenter itself was never the problem. On the real canvas with the real document it held
268-279 frames a second against WPF's 140, with the UI thread doing 0.03-0.60ms a frame, and
its text matched WPF exactly once both sides rasterised the same way.

**What failed was the hybrid** - two renderers alternating on the same surface. Every artifact
came from the seam: stale WPF content at the handoff, backgrounds drawn under lines rather than
in them, table geometry that made sense per-table but not per-row, selection and highlights that
live outside the line visuals. None of those exist if one renderer owns the whole window.

So the honest reading is not that a paced DirectWrite presenter is unworkable. It is that
**half a renderer is unworkable**, which is a different and more encouraging conclusion.

## Suggested order

1. **Toggle HAGS and windowed-game optimizations, re-measure.** An hour, and it separates
   "WPF's fault" from "the machine's configuration".
2. **Confirm the layer.** PresentMon reports the presentation mode - hardware composed flip
   versus blt - which tells us directly whether milcore is on the modern path.
3. **Only then choose.** If it is milcore and the patch is a contained change to the present
   path, A is a few days plus a permanent maintenance tax. If not, B, and the realistic
   candidates are WinUI 3 (stay on Microsoft's stack) or our own renderer with RmlUi or our own
   controls above it.
