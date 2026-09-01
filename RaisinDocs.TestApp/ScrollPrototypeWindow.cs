using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RaisinDocs.TestApp;

/// <summary>
/// THROWAWAY prototype for phase 1 of design/Scroll Pre-Buffering.md. Delete once it has
/// answered its question.
/// </summary>
/// <remarks>
/// It answers one thing only: is text drawn through a BitmapCache acceptable to look at?
/// Everything else in that design depends on the answer, because a cached surface with
/// transparency generally cannot use ClearType, so text falls back to greyscale antialiasing -
/// a change to how all text looks, not only while scrolling. If that is not acceptable the
/// design stops here and no further work is worth doing.
///
/// Three columns, the same text in each:
///   live      what the editor does today - DrawText straight to the DrawingContext
///   cached    the same drawing inside a BitmapCache'd visual, whole-pixel offset
///   cached+   the same, at a fractional offset, which is the point of the exercise
///
/// The comparisons that matter: live against cached while both are still, for crispness; and
/// cached against cached+ while scrolling, for whether the fractional offset moves smoothly
/// and whether line spacing stays put.
/// </remarks>
public sealed class ScrollPrototypeWindow : Window
{
    private readonly LineHost _live;
    private readonly LineHost _cached;
    private readonly LineHost _cachedFrac;
    private readonly TextBlock _readout = new() { Margin = new Thickness(8, 4, 8, 4) };

    private double _offset;
    private double _velocity;
    private bool _running;

    public ScrollPrototypeWindow()
    {
        Title = "Scroll pre-buffering prototype (throwaway)";
        Width = 1400;
        Height = 900;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _live = new LineHost(cached: false);
        _cached = new LineHost(cached: true);
        _cachedFrac = new LineHost(cached: true);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());

        AddHeader(grid, 0, "live — DrawText (ClearType today)");
        AddHeader(grid, 1, "cached — BitmapCache, whole-pixel offset");
        AddHeader(grid, 2, "cached+ — BitmapCache, FRACTIONAL offset");

        Grid.SetRow(_readout, 1);
        Grid.SetColumnSpan(_readout, 3);
        _readout.Foreground = Brushes.Gainsboro;
        grid.Children.Add(_readout);

        Place(grid, _live, 0);
        Place(grid, _cached, 1);
        Place(grid, _cachedFrac, 2);
        Content = grid;

        CompositionTarget.Rendering += OnFrame;
        MouseWheel += (_, e) => _velocity -= e.Delta * 10.0;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Space) { _offset = 0; _velocity = 0; Redraw(); }
        };
        Redraw();
        UpdateReadout();
    }

    private static void AddHeader(Grid g, int col, string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(8, 6, 8, 6),
            FontWeight = FontWeights.Bold,
        };
        Grid.SetRow(tb, 0);
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private static void Place(Grid g, UIElement e, int col)
    {
        Grid.SetRow(e, 2);
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private void UpdateReadout() =>
        _readout.Text = $"wheel to scroll, space to reset   |   offset {_offset:F2}   " +
                        $"velocity {_velocity:F0} px/s   |   middle column rounds the offset, " +
                        $"right column does not";

    private void OnFrame(object? sender, EventArgs e)
    {
        if (Math.Abs(_velocity) < 0.5)
        {
            if (_running) { _running = false; UpdateReadout(); }
            return;
        }
        _running = true;

        const double dt = 1.0 / 120, damping = 10.0;
        double decay = Math.Exp(-dt * damping);
        _offset += _velocity * (1 - decay) / damping;
        _velocity *= decay;

        Redraw();
        UpdateReadout();
    }

    private void Redraw()
    {
        _live.SetOffset(Math.Round(_offset));
        _cached.SetOffset(Math.Round(_offset));
        _cachedFrac.SetOffset(_offset);
    }

    /// <summary>Draws the sample text, optionally through a BitmapCache'd child visual.</summary>
    private sealed class LineHost : FrameworkElement
    {
        private const int LineCount = 400;

        private readonly bool _cached;
        private readonly VisualCollection _children;
        private readonly DrawingVisual? _content;
        private readonly TranslateTransform _translate = new();
        private double _offset;

        // Deliberately fractional, as in the real layout: line Y positions accumulate
        // FormattedText.Height, which is never a whole number.
        private const double LineHeight = 18.6133;

        public LineHost(bool cached)
        {
            _cached = cached;
            _children = new VisualCollection(this);

            if (cached)
            {
                _content = new DrawingVisual
                {
                    // RenderAtScale must track DPI and zoom in the real thing, or the bitmap is
                    // resampled and text is soft permanently rather than only while moving.
                    CacheMode = new BitmapCache { RenderAtScale = 1.0, SnapsToDevicePixels = false },
                    Transform = _translate,
                };
                _children.Add(_content);
                DrawContent();
            }
        }

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];

        public void SetOffset(double offset)
        {
            _offset = offset;
            if (_cached) _translate.Y = -offset;   // move the already-rasterised pixels
            else InvalidateVisual();               // re-rasterise at the new position
        }

        private void DrawContent()
        {
            using var dc = _content!.RenderOpen();
            // Tall enough that the prototype never scrolls off it; the real design has to
            // handle running out, which is the main cost of the single-surface variant.
            Draw(dc, 0, LineCount);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null,
                new Rect(0, 0, ActualWidth, ActualHeight));
            if (!_cached) Draw(dc, _offset, LineCount);
        }

        private static void Draw(DrawingContext dc, double offset, int lines)
        {
            var face = new Typeface("Consolas");
            var brush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
            brush.Freeze();

            for (int i = 0; i < lines; i++)
            {
                double y = i * LineHeight - offset;
                if (y < -LineHeight || y > 900) continue;
                var ft = new FormattedText(
                    $"{i:D3}  The quick brown fox jumps over the lazy dog — 0123456789 iIlL1 mM",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 14, brush,
                    VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);
                if (i % 5 == 0) ft.SetFontWeight(FontWeights.Bold, 5, 9);
                dc.DrawText(ft, new Point(10, y));
            }
        }
    }
}
