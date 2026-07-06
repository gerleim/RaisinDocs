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
    private TimeSpan _lastWheelFrameTime;
    private const double WheelDamping = 10.0;

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
        CompositionTarget.Rendering -= OnWheelFrame;
    }

    internal void CancelSmooth() => _smoother.Cancel();

    internal void HandleWheel(int delta)
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
            _lastWheelFrameTime = TimeSpan.Zero;
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
        if (e is not RenderingEventArgs args) return;

        double dt;
        if (_lastWheelFrameTime == TimeSpan.Zero)
        {
            _lastWheelFrameTime = args.RenderingTime;
            dt = 1.0 / 60;
        }
        else
        {
            dt = (args.RenderingTime - _lastWheelFrameTime).TotalSeconds;
            _lastWheelFrameTime = args.RenderingTime;
            if (dt <= 0 || dt >= 0.5) return;
        }

        double prevPixel = Math.Round(_offset);
        double before = _offset;
        _offset += _wheelVelocity * dt;
        Clamp();
        if (_offset != before + _wheelVelocity * dt)
            _wheelVelocity = 0;

        _wheelVelocity *= Math.Exp(-dt * WheelDamping);

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
            CompositionTarget.Rendering -= OnWheelFrame;
        }

        _invalidateVisual();
    }
}
