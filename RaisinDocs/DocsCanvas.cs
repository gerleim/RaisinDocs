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
    private const double _paragraphGap = 8;

    private readonly List<StringBuilder> _blocks = [new()];
    private int _blockIndex;
    private int _charOffset;

    private bool _cursorVisible = true;
    private double _dpiScale = 1.0;
    private double _lineHeight;
    private bool _measured;
    private readonly DispatcherTimer _blinkTimer;

    private record struct VisualLine(int BlockIndex, int StartOffset, int Length);
    private readonly List<VisualLine> _visualLines = [];
    private readonly List<double> _lineYPositions = [];

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

    private double MeasureTextWidth(string text)
    {
        if (text.Length == 0) return 0;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize,
            Brushes.Black, _dpiScale);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private void ComputeLayout()
    {
        _visualLines.Clear();
        _lineYPositions.Clear();
        double maxWidth = Math.Max(0, ActualWidth - _padding * 2);

        for (int bi = 0; bi < _blocks.Count; bi++)
        {
            string text = _blocks[bi].ToString();
            var segments = text.Split('\n');
            int offset = 0;
            for (int s = 0; s < segments.Length; s++)
            {
                WrapSegment(bi, offset, segments[s], maxWidth);
                offset += segments[s].Length + 1;
            }
        }

        double y = _padding;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (i > 0 && _visualLines[i].BlockIndex != _visualLines[i - 1].BlockIndex)
                y += _paragraphGap;
            _lineYPositions.Add(y);
            y += _lineHeight;
        }
    }

    private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth)
    {
        if (segment.Length == 0)
        {
            _visualLines.Add(new VisualLine(blockIndex, startOffset, 0));
            return;
        }

        int pos = 0;
        while (pos < segment.Length)
        {
            int lineLen = FitLine(segment, pos, maxWidth);
            _visualLines.Add(new VisualLine(blockIndex, startOffset + pos, lineLen));
            pos += lineLen;
        }
    }

    private int FitLine(string text, int start, double maxWidth)
    {
        int lastSpace = -1;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == ' ') lastSpace = i;
            double w = MeasureTextWidth(text[start..(i + 1)]);
            if (w > maxWidth && i > start)
            {
                if (lastSpace >= start)
                    return lastSpace - start + 1;
                return i - start;
            }
        }
        return text.Length - start;
    }

    private int CursorToVisualLineIndex()
    {
        for (int i = _visualLines.Count - 1; i >= 0; i--)
        {
            var vl = _visualLines[i];
            if (vl.BlockIndex == _blockIndex && vl.StartOffset <= _charOffset)
                return i;
        }
        return 0;
    }

    private double CursorXInVisualLine(int vlIndex)
    {
        var vl = _visualLines[vlIndex];
        int localOffset = Math.Clamp(_charOffset - vl.StartOffset, 0, vl.Length);
        if (localOffset == 0) return 0;
        string blockText = _blocks[vl.BlockIndex].ToString();
        return MeasureTextWidth(blockText.Substring(vl.StartOffset, localOffset));
    }

    private int HitTestInVisualLine(int vlIndex, double x)
    {
        var vl = _visualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        string blockText = _blocks[vl.BlockIndex].ToString();
        string lineText = blockText.Substring(vl.StartOffset, vl.Length);

        for (int i = 0; i < lineText.Length; i++)
        {
            double left = i > 0 ? MeasureTextWidth(lineText[..i]) : 0;
            double right = MeasureTextWidth(lineText[..(i + 1)]);
            if (x < (left + right) / 2) return vl.StartOffset + i;
        }
        return vl.StartOffset + vl.Length;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        ComputeLayout();
        var pos = e.GetPosition(this);

        int clickedVl = _visualLines.Count - 1;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (pos.Y < _lineYPositions[i] + _lineHeight)
            {
                clickedVl = i;
                break;
            }
        }

        _blockIndex = _visualLines[clickedVl].BlockIndex;
        _charOffset = HitTestInVisualLine(clickedVl, pos.X - _padding);

        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        var block = _blocks[_blockIndex];
        foreach (char c in e.Text)
        {
            if (c < ' ' && c != '\t') continue;
            block.Insert(_charOffset, c);
            _charOffset++;
        }

        ResetBlink();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool handled = true;

        ComputeLayout();

        switch (e.Key)
        {
            case Key.Return:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    _blocks[_blockIndex].Insert(_charOffset, '\n');
                    _charOffset++;
                }
                else
                {
                    var block = _blocks[_blockIndex];
                    string after = block.ToString(_charOffset, block.Length - _charOffset);
                    block.Remove(_charOffset, block.Length - _charOffset);
                    _blockIndex++;
                    _blocks.Insert(_blockIndex, new StringBuilder(after));
                    _charOffset = 0;
                }
                break;

            case Key.Back:
                if (_charOffset > 0)
                {
                    _blocks[_blockIndex].Remove(_charOffset - 1, 1);
                    _charOffset--;
                }
                else if (_blockIndex > 0)
                {
                    var prev = _blocks[_blockIndex - 1];
                    int newOffset = prev.Length;
                    prev.Append(_blocks[_blockIndex]);
                    _blocks.RemoveAt(_blockIndex);
                    _blockIndex--;
                    _charOffset = newOffset;
                }
                break;

            case Key.Delete:
                if (_charOffset < _blocks[_blockIndex].Length)
                {
                    _blocks[_blockIndex].Remove(_charOffset, 1);
                }
                else if (_blockIndex < _blocks.Count - 1)
                {
                    _blocks[_blockIndex].Append(_blocks[_blockIndex + 1]);
                    _blocks.RemoveAt(_blockIndex + 1);
                }
                break;

            case Key.Left:
                if (_charOffset > 0)
                    _charOffset--;
                else if (_blockIndex > 0)
                {
                    _blockIndex--;
                    _charOffset = _blocks[_blockIndex].Length;
                }
                break;

            case Key.Right:
                if (_charOffset < _blocks[_blockIndex].Length)
                    _charOffset++;
                else if (_blockIndex < _blocks.Count - 1)
                {
                    _blockIndex++;
                    _charOffset = 0;
                }
                break;

            case Key.Up:
            {
                int vli = CursorToVisualLineIndex();
                if (vli > 0)
                {
                    double x = CursorXInVisualLine(vli);
                    vli--;
                    _blockIndex = _visualLines[vli].BlockIndex;
                    _charOffset = HitTestInVisualLine(vli, x);
                }
                break;
            }

            case Key.Down:
            {
                int vli = CursorToVisualLineIndex();
                if (vli < _visualLines.Count - 1)
                {
                    double x = CursorXInVisualLine(vli);
                    vli++;
                    _blockIndex = _visualLines[vli].BlockIndex;
                    _charOffset = HitTestInVisualLine(vli, x);
                }
                break;
            }

            case Key.Home:
            {
                int vli = CursorToVisualLineIndex();
                _charOffset = _visualLines[vli].StartOffset;
                break;
            }

            case Key.End:
            {
                int vli = CursorToVisualLineIndex();
                var vl = _visualLines[vli];
                _charOffset = vl.StartOffset + vl.Length;
                break;
            }

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            ResetBlink();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMeasured();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

        ComputeLayout();

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.Length > 0)
            {
                string text = _blocks[vl.BlockIndex].ToString().Substring(vl.StartOffset, vl.Length);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize,
                    Brushes.Black, _dpiScale);
                dc.DrawText(ft, new Point(_padding, _lineYPositions[i]));
            }
        }

        if (_cursorVisible && IsFocused && _visualLines.Count > 0)
        {
            int vli = CursorToVisualLineIndex();
            double cx = _padding + CursorXInVisualLine(vli);
            double cy = _lineYPositions[vli];
            var pen = new Pen(Brushes.Black, 1.5);
            dc.DrawLine(pen, new Point(cx, cy), new Point(cx, cy + _lineHeight));
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
}
