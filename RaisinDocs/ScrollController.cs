using System.Diagnostics;
using System.Windows.Media;
using Raisin.WPF.Base;

namespace RaisinDocs;

internal class ScrollController
{
    private readonly Action _invalidateVisual;
    private readonly Func<double> _getMaxScroll;
    private readonly Func<double> _getRepaintInterval;
    private readonly SmoothScroller _smoother;

    private double _offset;
    private double _wheelVelocity;
    private bool _wheelCoasting;
    private readonly Stopwatch _wheelClock = new();
    private const double WheelDamping = 10.0;

    /// <summary>
    /// Speed, in pixels a second, below which a coast snaps to a whole pixel and stops.
    /// </summary>
    /// <remarks>
    /// This decides how long the visibly stepped part of a coast lasts, because the renderer
    /// rounds the offset to whole pixels: the image only changes when the offset crosses a
    /// boundary, so at V pixels a second it changes V times a second however often we paint. At
    /// 10 - which is where this sat, having borrowed WheelDamping's value rather than meaning
    /// anything - the last pixel takes a tenth of a second, and the closing stretch of every
    /// gesture steps at 10 to 30 changes a second against a panel showing 280.
    ///
    /// Ending the coast sooner shortens that stretch. It does not fix the stepping, which needs
    /// sub-pixel positioning and therefore a renderer we control - see
    /// design/Sub-Pixel Scrolling.md. It only stops the scroll before the stepping becomes the
    /// thing you are looking at.
    ///
    /// Tunable without a rebuild through RAISINDOCS_SNAP_VELOCITY, for finding the point where
    /// the stop stops being noticeable.
    /// </remarks>
    private static readonly double SnapVelocity =
        double.TryParse(Environment.GetEnvironmentVariable("RAISINDOCS_SNAP_VELOCITY"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0
            ? v
            : 40.0;

    // RenderingEventArgs.RenderingTime is the composition engine's frame stamp, not a wall
    // clock: it repeats or regresses when the UI thread and the render thread desync, which
    // gets likely once OnRender approaches the frame budget (tall window = many visible
    // lines). A Stopwatch cannot do that. A frame that overran is clamped rather than
    // dropped, so slow frames cost precision, never a visible stop.
    private const double MaxFrameDelta = 0.05;

    // Repainting from inside the Rendering handler makes WPF schedule another render pass,
    // which raises Rendering again straight away: the loop free-runs as fast as the UI
    // thread can go rather than at the display rate. Measured at ~295 repaints/sec with
    // bursts to 3000Hz, each one an OnRender costing time proportional to the number of
    // visible lines. That saturates the thread, so WM_MOUSEWHEEL messages queue up and
    // Windows sums their deltas - 276 notches arrived as 83 messages, half of them carrying
    // 2 to 12 notches, each becoming one oversized velocity impulse.
    //
    // The physics still integrates on every tick and stays dt-correct; only the repaint is
    // capped, which is all the display can show anyway. The spare time lets the message
    // pump keep up so notches arrive one at a time.
    //
    // The cap is the display's own rate (see DisplayRefresh), not a fixed 60: scrolling is
    // continuous motion, so every frame the panel can show is a frame worth drawing. Read
    // once per gesture, which is cheap and picks up a monitor or mode change.

    /// <summary>The display's own interval, before any allowance for how heavy the content is.</summary>
    private double _displayInterval = 1.0 / 60;

    /// <summary>
    /// Rolling average of how many notches each wheel message carried. One is healthy.
    /// </summary>
    /// <remarks>
    /// This is the failure itself rather than a proxy for it. When the UI thread cannot keep
    /// up, WM_MOUSEWHEEL messages queue and Windows sums their deltas, so a message arrives
    /// carrying several notches at once and the coast builds in lurches. Measuring that
    /// directly means the cap works wherever the cost actually lives.
    ///
    /// It replaced a measurement of how long OnRender took. That was a reasonable proxy while
    /// OnRender did the drawing, but once lines were cached as visuals the drawing moved into
    /// the visual tree, which WPF rasterises afterwards - so the old signal read a few hundred
    /// microseconds and concluded frames were free, exactly when the real cost had moved
    /// somewhere it could not see.
    /// </remarks>
    private double _notchesPerMessage = 1.0;

    /// <summary>
    /// How much merging is tolerated before the repaint rate is eased off.
    /// </summary>
    /// <remarks>
    /// Repainting at the display's rate assumes a frame is cheap enough to draw in the time
    /// available. On heavy content it is not: a document whose lines are mostly table rows
    /// measured 3.7ms an OnRender against a 6.94ms interval, so drawing took about half of
    /// wall-clock time and the message pump fell behind. Windows then merged queued wheel
    /// notches and the impulses arrived in clumps rather than evenly, which is felt as the
    /// coast building unevenly.
    ///
    /// Measured across documents, merging stays at zero while drawing takes under about a
    /// quarter of the time, appears around a third, and is severe at a half:
    ///
    ///   load    12%  19%  22%  25%  25%  34%  49%
    ///   merged   0%   0%   0%   1%   4%   9%  27%
    ///
    /// So the interval is stretched, when needed, until drawing fits inside this share of it.
    /// Heavy content then scrolls at a lower frame rate but an even one, which is the better
    /// trade: an uneven coast is far more noticeable than a slower one.
    /// </remarks>
    private const double MergeTolerance = 1.05;

    /// <summary>Never stretch further than this, however heavy a frame is.</summary>
    private const double MaxIntervalStretch = 4.0;

    private double _paintedPixel;

    internal double Offset
    {
        get => _offset;
        set => _offset = value;
    }

    internal double EffectiveOffset => _offset + _smoother.Offset;

    internal ScrollController(Action invalidateVisual, Func<double> getMaxScroll,
        Func<double>? getRepaintInterval = null)
    {
        _invalidateVisual = invalidateVisual;
        _getMaxScroll = getMaxScroll;
        _getRepaintInterval = getRepaintInterval ?? (() => 1.0 / 60);
        _smoother = new SmoothScroller(invalidateVisual);
    }

    internal void Clamp()
    {
        double max = _getMaxScroll();
        _offset = Math.Clamp(_offset, 0, max);
    }

    internal void StopWheelCoast()
    {
        if (!_wheelCoasting) return;
        _wheelVelocity = 0;
        _wheelCoasting = false;
        _wheelClock.Reset();
        CompositionTarget.Rendering -= OnWheelFrame;
    }

    internal void CancelSmooth() => _smoother.Cancel();

    internal void HandleWheel(double delta)
    {
        if (_smoother.IsAnimating)
        {
            _offset += _smoother.Offset;
            _smoother.Cancel();
            Clamp();
        }

        _wheelVelocity -= delta * WheelDamping;

        if (!_wheelCoasting)
        {
            _wheelCoasting = true;
            _wheelClock.Restart();
            _displayInterval = _getRepaintInterval();
            _paintedPixel = Math.Round(_offset);

            CompositionTarget.Rendering += OnWheelFrame;
            _invalidateVisual();
        }
    }

    internal void SetDirect(double offset)
    {
        StopWheelCoast();
        _offset = Math.Clamp(offset, 0, _getMaxScroll());
        _smoother.Offset = 0;
        _invalidateVisual();
    }

    internal void SmoothScrollTo(double targetOffset)
    {
        StopWheelCoast();
        double oldScroll = _offset;
        _offset = Math.Clamp(targetOffset, 0, _getMaxScroll());
        double jump = _offset - oldScroll;
        _smoother.Offset -= jump;
        _smoother.Start();
        _invalidateVisual();
    }

    /// <summary>
    /// How many notches the wheel message just handled carried. One means the pump is keeping
    /// up; more means messages queued and Windows summed them.
    /// </summary>
    internal void NoteWheelNotches(double notches)
    {
        if (notches < 1) notches = 1;
        // Reacts within a few messages, so a gesture that starts merging is caught during it,
        // and recovers just as quickly once messages arrive singly again.
        _notchesPerMessage = _notchesPerMessage * 0.8 + notches * 0.2;
    }

    private void OnWheelFrame(object? sender, EventArgs e)
    {
        double dt = _wheelClock.Elapsed.TotalSeconds;
        _wheelClock.Restart();
        if (dt <= 0) dt = 1.0 / 60;
        else if (dt > MaxFrameDelta) dt = MaxFrameDelta;

        double prevPixel = Math.Round(_offset);
        double before = _offset;

        // Closed-form integral of v0*exp(-D*t) over the frame, rather than v0*dt. Moving at
        // the start-of-frame velocity for the whole interval overshoots, because the real
        // velocity decays throughout it, by D*dt/(1-exp(-D*dt)): 2% at a 5ms frame but 27%
        // at the MaxFrameDelta clamp. That made how far a notch scrolled depend on how fast
        // the machine was drawing. This form is exact at any dt, so a notch always travels
        // the same distance. The exp is the one the decay below needs anyway.
        double decay = Math.Exp(-dt * WheelDamping);
        double deltaPx = _wheelVelocity * (1 - decay) / WheelDamping;
        _offset += deltaPx;
        Clamp();
        if (_offset != before + deltaPx)
            _wheelVelocity = 0;

        _wheelVelocity *= decay;

        bool stop = Math.Abs(_wheelVelocity) < 0.5;
        if (!stop && Math.Round(_offset) != prevPixel
                  && Math.Abs(_wheelVelocity) < SnapVelocity)
        {
            // Come to rest on a whole pixel so the frame that lingers is not resampled.
            // Nearest, not the pixel this frame began on: the view moves sub-pixel now, so
            // snapping backwards would be a visible step.
            _offset = Math.Round(_offset);
            stop = true;
        }

        if (stop)
        {
            _wheelVelocity = 0;
            _wheelCoasting = false;
            _wheelClock.Reset();
            CompositionTarget.Rendering -= OnWheelFrame;
        }

        // One repaint per composition frame, identified by the frame stamp rather than by a
        // wall-clock interval.
        //
        // Repainting from inside this handler makes WPF schedule another pass and raise
        // Rendering again, so it free-runs at about 500 a second. Gating that with a
        // wall-clock interval was the mistake: the repaint lands on whichever free-running
        // tick first crosses the threshold, so it arrives within a tick of the right time -
        // about 2ms - which on a 3.571ms panel is more than half a refresh period, at a
        // different phase every frame. Each repaint then independently catches or misses a
        // composition deadline, which is the jagged scroll.
        //
        // RenderingTime is the composition engine's frame stamp. It repeats when Rendering
        // fires more than once for the same frame, which is exactly what identifies the
        // free-run duplicates. Painting only when it advances gives one repaint per frame WPF
        // actually composes, and the invalidate then drives the next one - a loop that runs at
        // the compositor's cadence instead of sliding against it.
        var stamp = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.MinValue;
        bool newFrame = stamp == TimeSpan.MinValue || stamp != _lastRenderingTime;
        _lastRenderingTime = stamp;
        DiagFrame(dt, newFrame);

        // Only when the drawn image would differ, since the renderer rounds to whole pixels.
        // This does limit the decaying tail to one paint per pixel - about 38 a second at
        // 40px/s - which is the 1px stepping that sub-pixel scrolling was meant to cure.
        double pixel = Math.Round(_offset);
        if (pixel != _paintedPixel && (stop || newFrame))
        {
            DiagPaint(pixel - _paintedPixel);
            _paintedPixel = pixel;
            _invalidateVisual();
        }

        if (stop) DiagGestureEnd();
    }

    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;

    // --- diagnostics ------------------------------------------------------------------------

    /// <summary>
    /// Records how evenly a gesture's frames and pixel steps came out, to
    /// %LOCALAPPDATA%\RaisinDocs\scroll.log.
    /// </summary>
    /// <remarks>
    /// Two different things read as jagged and they need telling apart. A skipped composition
    /// frame is the compositor not showing one we drew. An uneven pixel step is the offset
    /// crossing pixel boundaries at irregular intervals, which is what a decaying coast does
    /// naturally once the speed drops near one pixel per frame - the same whole-pixel stepping
    /// sub-pixel scrolling was meant to cure, and nothing to do with dropped frames.
    /// </remarks>
    internal static readonly bool Diagnostics = ScrollDiag.Enabled;

    private static readonly string DiagPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "scroll.log");

    private readonly List<double> _frameGaps = new(512);
    private readonly List<double> _paintGaps = new(512);
    private readonly List<double> _pixelSteps = new(512);
    private readonly Stopwatch _gestureClock = new();
    private double _sincePaint;
    private int _frames, _paints;

    private int _gc0, _gc1, _gc2;

    private void DiagFrame(double dt, bool newFrame)
    {
        if (!Diagnostics) return;
        if (!_gestureClock.IsRunning)
        {
            _gestureClock.Restart();
            _gc0 = GC.CollectionCount(0);
            _gc1 = GC.CollectionCount(1);
            _gc2 = GC.CollectionCount(2);
            ScrollDiag.Snapshot();   // discard anything from before the gesture
        }
        _frames++;
        _sincePaint += dt;
        if (newFrame && dt > 0 && dt < 0.5) _frameGaps.Add(dt * 1000);
    }

    private void DiagPaint(double step)
    {
        if (!Diagnostics) return;
        _paints++;
        if (_sincePaint > 0 && _sincePaint < 0.5) _paintGaps.Add(_sincePaint * 1000);
        _sincePaint = 0;
        _pixelSteps.Add(Math.Abs(step));
    }

    private void DiagGestureEnd()
    {
        if (!Diagnostics || _paints < 8) { DiagReset(); return; }

        double seconds = _gestureClock.Elapsed.TotalSeconds;
        string frames = Summarise(_frameGaps, "composition frame");
        string paints = Summarise(_paintGaps, "paint interval");

        // The tail is where a coast is slowest and whole-pixel stepping shows most, so it is
        // reported on its own rather than averaged into the fast part.
        int tailFrom = Math.Max(0, _paintGaps.Count - 20);
        var tail = _paintGaps.GetRange(tailFrom, _paintGaps.Count - tailFrom);
        string tailText = Summarise(tail, "tail paint interval");

        var steps = _pixelSteps.ToArray();
        Array.Sort(steps);
        int oneP = 0, multi = 0;
        foreach (var v in _pixelSteps) { if (v <= 1.0) oneP++; else multi++; }

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DiagPath)!);
            System.IO.File.AppendAllText(DiagPath,
                $"{DateTime.Now:HH:mm:ss.fff}  gesture {seconds:F2}s  " +
                $"{_frames} ticks, {_paints} paints" + Environment.NewLine +
                $"    {frames}" + Environment.NewLine +
                $"    {paints}" + Environment.NewLine +
                $"    {tailText}" + Environment.NewLine +
                $"    costs: {ScrollDiag.Snapshot()}" + Environment.NewLine +
                $"    gc during gesture: gen0 {GC.CollectionCount(0) - _gc0}, " +
                $"gen1 {GC.CollectionCount(1) - _gc1}, gen2 {GC.CollectionCount(2) - _gc2}" +
                Environment.NewLine +
                $"    pixel steps: 1px {100.0 * oneP / Math.Max(1, _pixelSteps.Count):F0}%, " +
                $"more {100.0 * multi / Math.Max(1, _pixelSteps.Count):F0}%, " +
                $"largest {(steps.Length > 0 ? steps[^1] : 0):F0}px   " +
                $"notches/message {_notchesPerMessage:F2}" + Environment.NewLine);
        }
        catch (System.IO.IOException) { }

        DiagReset();
    }

    private static string Summarise(List<double> v, string label)
    {
        if (v.Count < 4) return $"{label}: too few samples";
        var a = v.ToArray();
        Array.Sort(a);
        double med = a[a.Length / 2];
        int late = 0;
        foreach (var x in v) if (x > med * 1.5) late++;
        return $"{label} median {med:F2}ms ({1000 / med:F0}/s), " +
               $"p99 {a[(int)(a.Length * 0.99)]:F2}ms, max {a[^1]:F2}ms, " +
               $"over 1.5x median {100.0 * late / v.Count:F1}%";
    }

    private void DiagReset()
    {
        _frameGaps.Clear();
        _paintGaps.Clear();
        _pixelSteps.Clear();
        _gestureClock.Reset();
        _sincePaint = 0;
        _frames = _paints = 0;
    }
}
