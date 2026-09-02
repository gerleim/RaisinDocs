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

The measurement pointed at layer 2, and the module list confirmed it: milcore presents through
Direct3D 9Ex. A present rate above the display rate with drops is the signature of presenting
without waiting on a frame-latency signal, and on D3D9Ex there is no such signal to wait on -
`DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`, which the prototype used, arrived with
DXGI 1.3 and has no D3D9 counterpart.

## The free experiments, run

Both were done before anything else, and between them they found the root cause.

**Optimizations for windowed games: already on, and never applicable.**
`HKCU\Software\Microsoft\DirectX\UserGpuPreferences` reads
`DirectXUserGlobalSettings = AutoHDREnable=0;SwapEffectUpgradeEnable=1;` - the feature has been
enabled globally all along. It upgrades **DirectX 10 and 11** windowed apps from blt-model to
flip-model presentation. WPF is not one.

**HAGS: unset, at the driver default.** `HwSchMode` is absent under
`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers`. Still worth a toggle, but it changes
GPU scheduling rather than the presentation model, so it does not address the mechanism below.

**The finding was in the module list.** Running the real editor on the real document and reading
its loaded modules:

```
d3d9.dll          nvd3dumx.dll     nvldumdx.dll
wpfgfx_cor3.dll   dwrite.dll       DWMAPI.dll
```

No `dxgi.dll`. No `d3d11.dll`. No `dcomp.dll`.

**WPF's milcore renders and presents through Direct3D 9Ex.** That is the structural reason it
cannot pace. The frame latency waitable object our prototype used to hold 280 frames a second is
DXGI 1.3; D3D9Ex has no equivalent. This is not a scheduling bug inside WPF - it is the
generation of the graphics API WPF still sits on, and it also explains why a setting aimed at
DX10/11 apps could never have helped.

It is not completely without pacing primitives, in fairness: D3D9Ex gained
`D3DSWAPEFFECT_FLIPEX` in Windows 7, and `IDirect3DDevice9Ex` has `SetMaximumFrameLatency` and
`WaitForVBlank`. Whether milcore uses any of them is the next thing to read in the source, and it
decides whether A is a contained patch or a backend rewrite.

## Reading the present path

Done, and it changes the answer. The short version: WPF does try to sync to vblank, the code
that decides *when* is managed rather than native, and it was written for 60Hz.

**The swapchain is blt-model.** `d3ddevicemanager.cpp` sets `D3DSWAPEFFECT_DISCARD` (or `_COPY`
when contents must be retained), `BackBufferCount = 1`, and
`PresentationInterval = D3DPRESENT_INTERVAL_ONE`. There is no `D3DSWAPEFFECT_FLIPEX` anywhere in
the repository, and `SetMaximumFrameLatency` appears only in a COM wrapper declaration and a
fake device stub for shader generation - it is never called with a value.

**But vsync locking exists.** `CRenderTargetManager::WaitToPresent` opens with:

```cpp
// By default We don't support waiting for VSync.
*pePresentationResults = MilPresentationResults::VSyncUnsupported;
*puiRefreshRate = 60;

if (m_rgpVBlankSyncChannels.GetCount() > 0)
    IFC(WaitForDwm(...));
```

`EnableVBlankSync` - "Enables locking present calls to the vertical blank" - fills that list, via
the `MILCMD_PARTITION_SETVBLANKSYNCMODE` channel command.

**And it is on by default.** `MediaContext.EnterInterlockedPresentation` sends that command when
`MediaSystem.AnimationSmoothing && Channel.MarshalType == ChannelMarshalTypeCrossThread &&
IsClockSupported`. `s_animationSmoothing` defaults to `true`, and the registry override that could
disable it (`HKLM\Software\Microsoft\Avalon.Graphics\AnimationSmoothing`) is inside
`#if PRERELEASE`, so it is absent from shipping builds. `IsClockSupported` is only a QPC check.

### Where it actually goes wrong

The waiting is not done by blocking on a vblank signal. It is done in managed code, by estimating
when the next vblank will be and setting a `DispatcherTimer`:

```csharp
// we add 1ms to the estimated time because are estimated time make not be perfectly accurate
// and its more important that wake up after the vblank than right on it.
long earliestWakeupTicks = currentTicks + TicksUntilNextVsync(currentTicks)
                         + TimeSpan.TicksPerMillisecond;
```

...with a fallback of `TimeSpan.FromMilliseconds(17)` when the refresh rate is unknown, and a
render rate chosen as:

```csharp
_adjustedRefreshRate = FindNextPrime(displayRefreshRate + 5);
```

Three assumptions, all reasonable in 2006 and all wrong at 280Hz:

- **The +1 ms fudge.** At 60Hz a refresh period is 16.7 ms and 1 ms is a 6% safety margin. At
  280Hz the period is 3.571 ms and the same constant is **28% of it**.
- **A `DispatcherTimer` at `Render` priority** as the wake-up mechanism. Its resolution and queue
  latency are around a millisecond at best - again, noise at 16.7 ms, and a large fraction of a
  period at 3.571 ms. Any overshoot lands in the *next* period, which is exactly the
  refresh-multiple gaps we measured.
- **Deliberately off-cadence.** `FindNextPrime(refresh + 5)` targets 293 for a 280Hz display, on
  purpose, to avoid beating against the panel when tearing is possible. It is the opposite of
  pacing.

So the earlier framing - "WPF cannot pace because D3D9Ex has no waitable object" - is not the
whole story and is not the actionable part. WPF paces; it paces with a timer and a constant that
were sized for a 60Hz world.

**And that code is managed.** `MediaContext.cs` and `MediaSystem.cs` are C# in `PresentationCore`,
not native milcore. No `bilinearspan.lib`, no pinned MSVC, no LTCG. That makes A very much cheaper
than the earlier estimate - but it is still a forked runtime assembly, and the roadmap gives no
sign of an upstream fix to converge with.

## Testing the 60Hz theory, and refuting it

The prediction was that WPF's pacing constants are sized for a 16.7ms period and fall apart at
3.571ms, so the stutter should vanish on a slower panel. Measured by animating the scroll offset
and timing the interval between animation steps, on three panels of this machine:

| panel | period | WPF animation step | late |
|---|---|---|---|
| 100Hz | 10.000 ms | 3.83 ms — **261/s** | 7.7 - 12.7% |
| 144Hz | 6.944 ms | 3.73 ms — **268/s** | 6.0 - 20.0% |
| 280Hz | 3.571 ms | 3.57 ms — **280/s** | **0.3 - 4.7%** |

**The theory is wrong.** The 280Hz panel has the cleanest cadence of the three by a wide margin.

What the numbers show instead is that **the animation clock runs at about 280 a second on every
monitor** - the primary display's rate, whichever panel the window is on. On the 100Hz and 144Hz
panels that is 2.6x and 1.9x faster than the display can show, and those are exactly the panels
where frames arrive unevenly.

That matches `_adjustedRefreshRate = FindNextPrime(displayRefreshRate + 5)` operating on one
global refresh rate rather than a per-window one. On a mixed-refresh desktop, every window that
is not on the primary display is paced to the wrong clock by construction.

**Two limits on this result.** It measures the render side: the interval between animation steps,
not frames confirmed on the glass. On the 280Hz panel the clock ticks a clean 280 a second while
FrameView counted 140 reaching the display out of 228 presented - so the loss there happens after
the animation tick, in the blt-model present path, and this test cannot see it. Closing that gap
needs FrameView or PresentMon, not instrumentation inside the process.

## Open: the window changing display

Not addressed, and it needs to be. Two separate faults, one WPF's and one ours.

**WPF paces every window to one display.** Measured by animating a scroll and timing the
interval between animation steps, with the window placed on each panel in turn:

| panel | period | WPF animation step |
|---|---|---|
| 100Hz | 10.000 ms | 3.83 ms - 261/s |
| 144Hz | 6.944 ms | 3.73 ms - 268/s |
| 280Hz | 3.571 ms | 3.57 ms - 280/s |

About 280 a second everywhere, which is the primary display's rate. On the 100Hz and 144Hz
panels that is 2.6x and 1.9x more frames than the panel can show, and those are exactly the
panels where the animation step came out uneven - 7.7 to 20% late, against 0.3 to 4.7% on the
280Hz one. It matches `FindNextPrime(displayRefreshRate + 5)` operating on one global refresh
rate rather than a per-window one.

**And our own per-monitor interval is currently unused.** `DisplayRefresh.GetRepaintInterval`
resolves the rate of the display the window is actually on, and `ScrollController` reads it once
per gesture into `_displayInterval` - but since repaints were re-paced onto the compositor's
frame stamp, nothing reads that field. The scroll now paints once per composed frame whatever
the panel is, so on a non-primary display it inherits WPF's wrong clock directly.

What has to be decided and then handled:

- **Starting on a non-primary display.** The rate is resolved per gesture, so this is already
  approximately right for `_displayInterval` - and irrelevant while nothing reads it.
- **Moving the window between displays**, including mid-gesture. Nothing watches for it. The
  right trigger is the window's monitor changing (`WM_DISPLAYCHANGE`, or a move that crosses a
  monitor boundary), not a timer.
- **Whether to reinstate a cap at all.** The frame stamp is the right pacing signal when it
  tracks the panel. When it does not - any non-primary display here - a cap keyed to the
  window's own monitor would stop us painting three times more often than the panel can show.
  That is the same waste that caused wheel-message coalescing originally, so it is not
  cosmetic.

The measurement to make first is cheap: run a gesture on the 100Hz panel with `ScrollDiag`
enabled and compare the paint interval against the panel's 10 ms period. If it comes out near
3 ms, we are painting three frames for every one shown.

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

**What the patch would be**, and this is where the D3D9Ex finding bites. The prototype paced by
waiting on a DXGI frame latency object, and there is none in D3D9Ex to wait on. The nearest
equivalents are `D3DSWAPEFFECT_FLIPEX` with `SetMaximumFrameLatency(1)`, and possibly
`WaitForVBlank`. If milcore already uses FlipEx, the patch is small. If it presents the older
way, the patch grows towards replacing milcore's presentation layer.

**And note what it buys.** Even a successful patch leaves the editor on a 2006 graphics API,
maintained by us, re-forked on every servicing update. That is the strongest argument for B -
not that A cannot work, but that it spends real effort to stay where we are.

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

1. ~~Toggle HAGS and windowed-game optimizations~~ — **done**. The windowed-game optimization
   was already on and never applied; HAGS remains untested but addresses the wrong mechanism.
   The module list answered the question they were meant to answer.
2. ~~Confirm the layer~~ — **done**. It is milcore, and it is on Direct3D 9Ex.
3. ~~Read milcore's present path~~ — **done**. It is blt-model D3D9Ex with no FlipEx, but the
   pacing decision is not there. It is in managed `PresentationCore`, in a `DispatcherTimer`
   estimate with a hardcoded 1 ms margin and a deliberately off-cadence prime render rate.
4. **Decide.** A is now much cheaper than it looked - a managed assembly, not a native one - and
   the change is small and legible: the wake-up margin, the timer, and the prime-rate choice. It
   is still a forked runtime assembly with no upstream convergence, and it leaves the editor on a
   blt-model D3D9Ex path afterwards.

   B remains what it was: the presenter already held 280 frames a second on the real document with
   text matching WPF exactly, and everything that went wrong came from sharing a surface rather
   than from the presenter.

   Worth testing before either: **does the fault scale with refresh rate?** Every constant above is
   sized for 16.7 ms. Running the editor on the 100Hz or 144Hz panel and re-measuring costs
   minutes, and if the pacing is clean there, that confirms the diagnosis exactly and tells us
   what a patch would have to fix.
