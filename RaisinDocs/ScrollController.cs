using System.Diagnostics;
using System.Windows.Media;
using Raisin.WPF.Base;

namespace RaisinDocs;

internal class ScrollController
{
    private readonly Action _invalidateVisual;
    private readonly Func<double> _getMaxScroll;
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
    private const double MinRepaintInterval = 1.0 / 60;

    private double _sinceRepaint;
    private double _paintedPixel;

    internal double Offset
    {
        get => _offset;
        set => _offset = value;
    }

    internal double EffectiveOffset => _offset + _smoother.Offset;

    internal ScrollController(Action invalidateVisual, Func<double> getMaxScroll)
    {
        _invalidateVisual = invalidateVisual;
        _getMaxScroll = getMaxScroll;
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
            _paintedPixel = Math.Round(_offset);
            _sinceRepaint = MinRepaintInterval; // let the first frame paint immediately
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
        double deltaPx = _wheelVelocity * dt;
        _offset += deltaPx;
        Clamp();
        if (_offset != before + deltaPx)
            _wheelVelocity = 0;

        _wheelVelocity *= Math.Exp(-dt * WheelDamping);
        WheelDiag.Frame(dt, _wheelVelocity, _offset); // TEMP instrumentation

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
            WheelDiag.Flush(); // TEMP instrumentation
        }

        // Repaint only when the image would actually differ - the renderer rounds the offset
        // to whole pixels - and at most once per display frame. The last coast frame always
        // paints, so the final resting position is never left unpainted.
        _sinceRepaint += dt;
        double pixel = Math.Round(_offset);
        if (pixel != _paintedPixel && (stop || _sinceRepaint >= MinRepaintInterval))
        {
            _paintedPixel = pixel;
            _sinceRepaint = 0;
            _invalidateVisual();
        }
    }
}
