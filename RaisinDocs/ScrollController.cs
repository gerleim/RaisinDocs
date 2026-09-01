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
    private double _repaintInterval = 1.0 / 60;

    private double _sinceRepaint;
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
        WheelDiag.Flush(); // TEMP instrumentation
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
            _repaintInterval = _getRepaintInterval();
            _paintedPixel = Math.Round(_offset);
            _sinceRepaint = _repaintInterval; // let the first frame paint immediately
            WheelDiag.Coast(_repaintInterval); // TEMP instrumentation
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
                  && Math.Abs(_wheelVelocity) < WheelDamping)
        {
            _offset = prevPixel;
            stop = true;
        }

        if (stop)
        {
            _wheelVelocity = 0;
            _wheelCoasting = false;
            _wheelClock.Reset();
            CompositionTarget.Rendering -= OnWheelFrame;
        }

        // Repaint only when the image would actually differ - the renderer rounds the offset
        // to whole pixels - and at most once per display frame. The last coast frame always
        // paints, so the final resting position is never left unpainted.
        _sinceRepaint += dt;
        double pixel = Math.Round(_offset);
        bool painted = pixel != _paintedPixel && (stop || _sinceRepaint >= _repaintInterval);
        if (painted)
        {
            _paintedPixel = pixel;
            // Carry the remainder rather than zeroing. Ticks do not divide evenly into the
            // repaint interval, so zeroing rounds every repaint up to the next whole tick:
            // a 120Hz target served by ~137Hz ticks would beat down to about 68Hz. Clamped
            // to one interval so a long stall cannot bank credit for a burst of catch-up.
            _sinceRepaint = Math.Min(_sinceRepaint - _repaintInterval, _repaintInterval);
            _invalidateVisual();
        }

        WheelDiag.Frame(dt, _wheelVelocity, _offset, painted); // TEMP instrumentation
        if (stop) WheelDiag.Flush();                           // TEMP instrumentation
    }
}
