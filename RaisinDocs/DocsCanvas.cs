using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

public class DocsCanvas : FrameworkElement
{
    private static readonly Typeface _typeface = new("Segoe UI");
    private const double _fontSize = 16;
    private const double _padding = 10;

    private readonly StringBuilder _text = new();
    private int _cursorPos;
    private bool _cursorVisible = true;
    private double _dpiScale = 1.0;
    private double _lineHeight;
    private bool _measured;

    private readonly DispatcherTimer _blinkTimer;

    public DocsCanvas()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
        };

        Loaded += (_, _) => { EnsureMeasured(); Focus(); };
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _blinkTimer.Start();
            else _blinkTimer.Stop();
        };
    }

    private void EnsureMeasured()
    {
        if (_measured) return;
        try { _dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { }
        var ft = new FormattedText("M", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize,
            Brushes.Black, _dpiScale);
        _lineHeight = ft.Height;
        _measured = true;
    }

    private void ResetBlink()
    {
        _cursorVisible = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        _blinkTimer.Start();
        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _blinkTimer.Stop();
        _cursorVisible = false;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var pos = e.GetPosition(this);
        int hitPos = HitTestPosition(pos);
        _cursorPos = hitPos;
        ResetBlink();
        InvalidateVisual();
    }

    private double MeasureTextWidth(string text)
    {
        if (text.Length == 0) return 0;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize,
            Brushes.Black, _dpiScale);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private int HitTestPosition(Point pos)
    {
        EnsureMeasured();
        double x = pos.X - _padding;
        if (x <= 0) return 0;

        string content = _text.ToString();
        for (int i = 0; i < content.Length; i++)
        {
            double left = MeasureTextWidth(content[..i]);
            double right = MeasureTextWidth(content[..(i + 1)]);
            double mid = (left + right) / 2;
            if (x < mid) return i;
        }
        return content.Length;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        foreach (char c in e.Text)
        {
            if (c < ' ' && c != '\t') continue;
            _text.Insert(_cursorPos, c);
            _cursorPos++;
        }

        ResetBlink();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Back:
                if (_cursorPos > 0)
                {
                    _text.Remove(_cursorPos - 1, 1);
                    _cursorPos--;
                }
                break;

            case Key.Delete:
                if (_cursorPos < _text.Length)
                    _text.Remove(_cursorPos, 1);
                break;

            case Key.Left:
                if (_cursorPos > 0)
                    _cursorPos--;
                break;

            case Key.Right:
                if (_cursorPos < _text.Length)
                    _cursorPos++;
                break;

            case Key.Home:
                _cursorPos = 0;
                break;

            case Key.End:
                _cursorPos = _text.Length;
                break;

            default:
                return;
        }

        ResetBlink();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMeasured();

        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

        string content = _text.ToString();

        if (content.Length > 0)
        {
            var ft = new FormattedText(content, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize,
                Brushes.Black, _dpiScale);
            dc.DrawText(ft, new Point(_padding, _padding));
        }

        if (_cursorVisible && IsFocused)
        {
            double cx = _padding + MeasureTextWidth(content[.._cursorPos]);
            double cy = _padding;
            var pen = new Pen(Brushes.Black, 1.5);
            dc.DrawLine(pen, new Point(cx, cy), new Point(cx, cy + _lineHeight));
        }
    }
}
