using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using FactoryType = Vortice.Direct2D1.FactoryType;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using Color = System.Windows.Media.Color;

namespace RaisinDocs.TestApp;

/// <summary>
/// C3 against the real renderer: a paced presenter that replays what DocsCanvas drew.
/// </summary>
/// <remarks>
/// The earlier A/B compared the presenter against a toy WPF panel - plain unwrapped text, no
/// tables, images, colour spans, cached visuals or minimap - which does not have the problem
/// the presenter exists to solve. It managed 172 frames a second with 3% late, so there was
/// little to improve on and the comparison said nothing.
///
/// This one puts the real DocsCanvas on the WPF side, with the heavy document, and F9 switches
/// between its own scrolling and the presenter.
///
/// The presenter does not re-render the document. DocsCanvas already draws every visual line
/// into a DrawingVisual, and a DrawingVisual keeps the display list: glyph runs with the
/// indices and advances WPF resolved, geometries with their brushes, images with their
/// rectangles. The presenter replays that. So the two draw the same thing by construction
/// rather than by agreement - which is what the seam needs, and what a second implementation
/// of wrapping, block fonts, inline styles, colour spans and tables could never guarantee.
/// </remarks>
public sealed class ReplayWindow : Window
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "replay.log");

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object LogLock = new();

    internal static void Log(string message)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(LogPath,
                    $"{Clock.Elapsed.TotalSeconds,9:F3}  {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    internal static int MonitorIndex = -1;

    /// <summary>Dump the display list at a few offsets and exit. See DumpAsync.</summary>
    internal static bool Dump;

    /// <summary>
    /// Scroll the canvas at a fixed rate with the presenter off, and report WPF's own frame
    /// cadence. See AutoScroll.
    /// </summary>
    internal static bool AutoScrollTest;

    private readonly DocsEditor _editor;
    private readonly TextBlock _readout;
    private readonly ReplaySurface _surface;

    private bool _handoffEnabled = true;
    private bool _presenting;
    private bool _handingBack;
    private int _handBackTicks;
    private const int HandBackTicks = 3;

    public ReplayWindow(string path)
    {
        Title = $"{Path.GetFileName(path)} - replay presenter vs real DocsCanvas (F9)";
        Width = 1200;
        Height = 900;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _editor = new DocsEditor
        {
            ShowToolbar = false,
            ShowMinimap = false,
            Theme = DocsCanvas.EditorTheme.Dark,
        };
        if (File.Exists(path))
        {
            _editor.DocumentBasePath = Path.GetDirectoryName(Path.GetFullPath(path))!;
            _editor.SetText(File.ReadAllText(path));
        }

        // Visual mode, which is what a reader sees and what makes the replay worth testing:
        // source mode draws colour tags and table pipes as literal text, so hidden ranges,
        // rendered tables, applied colour spans and inline images - the parts most likely to
        // expose a difference between the two renderers - never appear at all.
        _editor.Canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        _editor.Canvas.SetImagePreview(DocsCanvas.ImagePreviewMode.Inline);

        _readout = new TextBlock
        {
            Foreground = Brushes.Gainsboro,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(8, 4, 8, 4),
            Text = "presenter ON (F9), F7 opaque lines, F8 text mode (Auto follows F7)",
        };

        _surface = new ReplaySurface();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_readout, 0);
        Grid.SetRow(_editor, 1);
        grid.Children.Add(_readout);
        grid.Children.Add(_editor);
        Content = grid;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath,
                $"replay log, {Path.GetFileName(path)}, {DateTime.Now:HH:mm:ss}" + Environment.NewLine);
        }
        catch (IOException) { }

        SourceInitialized += (_, _) =>
        {
            if (MonitorIndex >= 0) PlaceOnMonitor(MonitorIndex);
            UpdateLayout();
            var b = PanelBounds();
            _surface.Create(new WindowInteropHelper(this).Handle, b.x, b.y, b.w, b.h);
        };
        _editor.SizeChanged += (_, _) =>
        {
            var b = PanelBounds();
            _surface.SetBounds(b.x, b.y, b.w, b.h);
        };

        // Preview, and handled, so the canvas never sees a notch the presenter is taking. With
        // the handoff off it is not handled, and the canvas scrolls itself exactly as it does
        // in the editor - which is the thing being compared against.
        PreviewMouseWheel += (_, e) =>
        {
            if (!_handoffEnabled) return;
            if (!_presenting) TakeOver();
            _surface.Wheel(-e.Delta / 120.0);
            e.Handled = true;
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F7)
            {
                DocsCanvas.OpaqueLineVisuals = !DocsCanvas.OpaqueLineVisuals;
                Canvas.RebuildLineVisuals();
                _surface.DropTextures();
                _readout.Text = DocsCanvas.OpaqueLineVisuals
                    ? "line visuals OPAQUE (F7) - ClearType is available in the cache"
                    : "line visuals TRANSPARENT (F7) - the cache falls back to greyscale";
                Log($"F7: opaque line visuals {DocsCanvas.OpaqueLineVisuals}");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F8)
            {
                var mode = _surface.CycleTextMode();
                _readout.Text = $"text mode: {mode}  (F8 cycles, F9 toggles the presenter)";
                Log($"F8: text mode {mode}");
                e.Handled = true;
                return;
            }

            if (e.Key != Key.F9) return;
            _handoffEnabled = !_handoffEnabled;
            _surface.Stop();
            _readout.Text = _handoffEnabled
                ? "presenter ON  (F9) - replaying the canvas display list"
                : "presenter OFF (F9) - the real DocsCanvas is scrolling";
            Log($"F9: handoff {(_handoffEnabled ? "ON" : "OFF")}");
            e.Handled = true;
        };

        ContentRendered += async (_, _) =>
        {
            Log($"content height {Canvas.ContentHeight:F0}px");
            LogRefreshRate();
            CompositionTarget.Rendering += OnFrame;

            if (AutoScrollTest) StartAutoScroll();

            if (Dump)
            {
                try { await DumpAsync(); }
                catch (Exception ex) { Log($"DUMP FAILED: {ex.GetType().Name}: {ex.Message}"); Close(); }
            }
        };
        Closed += (_, _) => _surface.Destroy();
    }

    private DocsCanvas Canvas => _editor.Canvas;

    /// <summary>
    /// Writes the display list of a few lines, opaque and transparent, at offsets deep enough
    /// to be inside the report's tables.
    /// </summary>
    /// <remarks>
    /// Running in the real app rather than a test canvas, because a test canvas laid the same
    /// document out at about forty pixels wide - every table row wrapped to a single character
    /// and the table column widths came out empty, which looks exactly like the bug being
    /// chased and is not it.
    /// </remarks>
    private async Task DumpAsync()
    {
        foreach (bool opaque in new[] { false, true })
        {
            DocsCanvas.OpaqueLineVisuals = opaque;
            Canvas.RebuildLineVisuals();

            foreach (double offset in new[] { 6000.0, 12000.0 })
            {
                Canvas.ViewOffset = offset;
                Canvas.InvalidateVisual();
                Canvas.UpdateLayout();
                await Task.Delay(150);
                Canvas.PrepareLineVisualsAt(offset);

                var list = new List<DocsCanvas.LineDrawing>();
                Canvas.SnapshotLineDrawings(list);

                Log($"===== opaque={opaque} offset={offset:F0} lines={list.Count} =====");

                // The same painters, one line wide and then a whole viewport, so it is clear
                // whether the range is the problem or the call context is.
                foreach (var line in list.Where(l => l.Y >= offset && l.Y < offset + 120).Take(5))
                {
                    var sb = new System.Text.StringBuilder();
                    Describe(line.Drawing, sb, 0);
                    Log($"--- line {line.Index} y={line.Y:F0} ---{Environment.NewLine}{sb.ToString().TrimEnd()}");
                }
            }
        }

        DocsCanvas.OpaqueLineVisuals = false;
        Canvas.RebuildLineVisuals();
        Log("===== dump complete =====");
        Close();
    }

    private static void Describe(Drawing? d, System.Text.StringBuilder sb, int depth)
    {
        if (d == null) return;
        string pad = new(' ', depth * 2 + 2);

        switch (d)
        {
            case DrawingGroup g:
                sb.AppendLine($"{pad}group({g.Children.Count})" +
                              $" clip={(g.ClipGeometry as RectangleGeometry)?.Rect.ToString() ?? "none"}");
                foreach (var c in g.Children) Describe(c, sb, depth + 1);
                break;

            case GeometryDrawing geo:
                string what = geo.Geometry switch
                {
                    RectangleGeometry r => $"rect {r.Rect}",
                    LineGeometry l => $"line {l.StartPoint}->{l.EndPoint}",
                    _ => geo.Geometry?.GetType().Name ?? "null",
                };
                sb.AppendLine($"{pad}{what} fill={(geo.Brush as SolidColorBrush)?.Color.ToString() ?? "none"}" +
                              $" pen={(geo.Pen?.Brush as SolidColorBrush)?.Color.ToString() ?? "none"}");
                break;

            case GlyphRunDrawing gr:
                sb.AppendLine($"{pad}glyphs x{gr.GlyphRun?.GlyphIndices.Count ?? 0} " +
                              $"at {gr.GlyphRun?.BaselineOrigin}");
                break;

            case ImageDrawing img:
                sb.AppendLine($"{pad}image {img.Rect}");
                break;

            default:
                sb.AppendLine($"{pad}{d.GetType().Name}");
                break;
        }
    }

    public static ReplayWindow Open(string? file)
    {
        string path = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockRaisin2", "reports", "2026-08-31", "SR_report_2026-08-31_DUH768767.md");
        return new ReplayWindow(path);
    }

    // --- the gesture -----------------------------------------------------------------------

    private void TakeOver()
    {
        _presenting = true;
        _handingBack = false;
        _surface.Begin(Canvas.ViewOffset, Math.Max(0, Canvas.ContentHeight - Canvas.ActualHeight));
        PumpDrawings();
        _surface.Show();
        Log($"TAKE OVER at {Canvas.ViewOffset:F1}");
    }

    private void HandBack()
    {
        if (_handingBack) return;
        _handingBack = true;
        _presenting = false;

        double offset = _surface.Offset;
        Canvas.ViewOffset = offset;
        Canvas.InvalidateVisual();
        _handBackTicks = 0;
        Log($"HAND BACK at {offset:F1}");
    }

    /// <summary>
    /// Keeps the canvas building lines under wherever the presenter has got to, and hands the
    /// frozen display list across. Both are UI-thread work, done once a frame while a gesture
    /// runs - which is the whole of what the UI thread does during a scroll now.
    /// </summary>
    private void PumpDrawings()
    {
        Canvas.PrepareLineVisualsAt(_surface.Offset);
        _surface.PublishDrawings(Canvas);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_presenting)
        {
            var sw = Stopwatch.StartNew();
            PumpDrawings();
            _surface.NotePumpCost(sw.Elapsed.TotalMilliseconds);

            if (_surface.IsIdle) HandBack();
            return;
        }

        if (_handingBack && ++_handBackTicks >= HandBackTicks)
        {
            _surface.Hide();
            _handingBack = false;
        }

        MeasureWpfFrame();
    }

    /// <summary>Scroll offset, animated, so WPF drives the frames rather than we do.</summary>
    public static readonly DependencyProperty ScrollProbeProperty =
        DependencyProperty.Register(nameof(ScrollProbe), typeof(double), typeof(ReplayWindow),
            new PropertyMetadata(0.0, (d, e) =>
            {
                var w = (ReplayWindow)d;
                w.Canvas.ViewOffset = (double)e.NewValue;
                w.Canvas.InvalidateVisual();
            }));

    public double ScrollProbe
    {
        get => (double)GetValue(ScrollProbeProperty);
        set => SetValue(ScrollProbeProperty, value);
    }

    private Stopwatch? _autoScroll;

    /// <summary>
    /// Scrolls by animating a property, and reports how evenly WPF's frames arrive.
    /// </summary>
    /// <remarks>
    /// An animation, deliberately, and not a loop that invalidates from inside
    /// CompositionTarget.Rendering: invalidating there makes WPF schedule another pass and raise
    /// the event again, so it free-runs at 800 to 1200 a second and measures nothing. An
    /// animation is also the case WPF's interlocked presentation exists to pace, so it is the
    /// fair test of it.
    ///
    /// The canvas's own ScrollController is bypassed for the same reason in reverse: it caps
    /// repaints at DisplayRefresh.MaxFps, which is 144, and that is our ceiling rather than
    /// WPF's.
    /// </remarks>
    private void StartAutoScroll()
    {
        _handoffEnabled = false;
        _readout.Text = "auto-scroll cadence test — WPF only, animated";

        double span = Math.Min(Math.Max(0, Canvas.ContentHeight - Canvas.ActualHeight), 12000);
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = span,
            Duration = new Duration(TimeSpan.FromSeconds(8)),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };

        BeginAnimation(ScrollProbeProperty, anim);
        _autoScroll = Stopwatch.StartNew();
        Log($"AUTOSCROLL start, animating 0..{span:F0}px, presenter off");

        var stop = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(18),
        };
        stop.Tick += (_, _) => { stop.Stop(); Log("AUTOSCROLL done"); Close(); };
        stop.Start();
    }

    // --- the display this is running on -----------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public int mL, mT, mR, mB, wL, wT, wR, wB, dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    private void LogRefreshRate()
    {
        string? device = null;
        IntPtr mon = MonitorFromWindow(new WindowInteropHelper(this).Handle, 2);
        if (mon != IntPtr.Zero)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(mon, ref info)) device = info.szDevice;
        }

        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(device, -1, ref dm) && dm.dmDisplayFrequency > 0)
            Log($"display {device} {dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz " +
                $"(period {1000.0 / dm.dmDisplayFrequency:F3}ms)");
        else
            Log("display refresh rate unavailable");
    }

    private readonly List<double> _wpfGaps = new(1024);
    private readonly Stopwatch _wpfTick = Stopwatch.StartNew();
    private double _lastWpfOffset = -1;
    private int _ticks, _movingTicks;

    /// <summary>
    /// The canvas's own frame cadence while it is scrolling itself.
    /// </summary>
    /// <remarks>
    /// Only while the offset is actually moving, so an idle window does not dilute the sample.
    ///
    /// These are CompositionTarget.Rendering intervals, which is a render pass rather than a
    /// confirmed display change, so they are not strictly the same measurement as the
    /// presenter's Present gaps. Treat the WPF figure as the best case: a render that never
    /// reached the screen still counts here, and one that reached it late counts as on time.
    /// </remarks>
    /// <summary>
    /// The interval between animation updates - WPF's animation clock, in effect.
    /// </summary>
    /// <remarks>
    /// Measured between changes of the scroll offset, not between CompositionTarget.Rendering
    /// ticks. The event free-runs at 500 a second here because the property callback
    /// invalidates and WPF then schedules another pass, so its rate says nothing; how often the
    /// animated value actually advances is the pacing loop's real output.
    ///
    /// This is still a render-side measurement, not a display-side one. A frame counted here may
    /// never have reached the glass, and one that arrived late counts as on time - so treat these
    /// figures as WPF's best case.
    /// </remarks>
    private void MeasureWpfFrame()
    {
        double offset = Canvas.ViewOffset;
        if (Math.Abs(offset - _lastWpfOffset) <= 0.01) return;
        _lastWpfOffset = offset;

        double dt = _wpfTick.Elapsed.TotalMilliseconds;
        _wpfTick.Restart();
        if (dt <= 0 || dt > 500) return;

        _wpfGaps.Add(dt);
        if (_wpfGaps.Count < 300) return;

        var g = _wpfGaps.ToArray();
        Array.Sort(g);
        double med = g[g.Length / 2];
        int late = 0;
        foreach (var x in _wpfGaps) if (x > med * 1.5) late++;

        Log($"  [wpf] animation step median {med:F2}ms ({1000 / med:F0}/s), " +
            $"p99 {g[(int)(g.Length * 0.99)]:F2}ms, max {g[^1]:F2}ms, " +
            $"over 1.5x median {100.0 * late / _wpfGaps.Count:F1}%");
        _wpfGaps.Clear();
    }


    // --- placement -------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    private void PlaceOnMonitor(int index)
    {
        var all = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) => { all.Add(r); return true; }, IntPtr.Zero);
        if (index < 0 || index >= all.Count) return;

        var t = all[index];
        int w = Math.Min(1200, t.Right - t.Left - 80);
        int h = Math.Min(900, t.Bottom - t.Top - 80);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            t.Left + 40, t.Top + 40, w, h, 0x0004);
        Log($"placed on monitor {index}");
    }

    private (int x, int y, int w, int h) PanelBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = _editor.TranslatePoint(new Point(0, 0), this);
        return ((int)Math.Round(origin.X * dpi.DpiScaleX),
                (int)Math.Round(origin.Y * dpi.DpiScaleY),
                (int)Math.Round(_editor.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(_editor.ActualHeight * dpi.DpiScaleY));
    }

    // --- the surface -----------------------------------------------------------------------

    private sealed class ReplaySurface
    {
        private const int WS_CHILD = 0x40000000;
        private const int SW_HIDE = 0, SW_SHOWNA = 8;
        private const uint WM_NCHITTEST = 0x0084;
        private static readonly IntPtr HTTRANSPARENT = new(-1);

        private const double WheelDamping = 10.0;
        private const double PixelsPerNotch = 120.0;
        private const int NotchScale = 1000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int ex, string cls, string? name, int style,
            int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? n);

        private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize, style;
            [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private static WndProc? _wndProc;
        private static string? _className;

        /// <summary>
        /// A class with no background brush, and transparent to hit testing.
        /// </summary>
        /// <remarks>
        /// No brush, because the predefined "static" class paints its area pale every time the
        /// window is shown. HTTRANSPARENT, because the surface covers the canvas during a
        /// gesture and mouse messages go to the window under the pointer - without it the
        /// surface swallows the wheel notches it is meant to be presenting.
        /// </remarks>
        private static string EnsureClass()
        {
            if (_className != null) return _className;
            _wndProc = (h, m, w, l) => m == WM_NCHITTEST ? HTTRANSPARENT : DefWindowProcW(h, m, w, l);
            var c = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandleW(null),
                hbrBackground = IntPtr.Zero,
                lpszClassName = "RaisinDocsReplaySurface",
            };
            RegisterClassExW(ref c);
            return _className = c.lpszClassName;
        }

        private IntPtr _hwnd;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain1? _swapChain;
        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2d;
        private ID2D1Bitmap1? _target;
        private IDWriteFactory? _dwrite;
        private DrawingReplay? _replay;

        private Thread? _thread;
        private volatile bool _running;
        private int _bufferWidth, _bufferHeight;

        private double _offset;
        private double _velocity;
        private double _maxOffset;
        private int _wheelPending;

        /// <summary>
        /// The display list, published by the UI thread and read by the render thread. Swapped
        /// whole rather than mutated, so the render thread always has a consistent list without
        /// a lock in the frame path.
        /// </summary>
        private volatile List<DocsCanvas.LineDrawing>? _drawings;

        private readonly List<double> _gaps = new(512);
        private readonly List<double> _draws = new(512);
        private readonly List<double> _pumps = new(512);

        public bool IsShown { get; private set; }
        public double Offset => Volatile.Read(ref _offset);
        public bool IsIdle => Volatile.Read(ref _wheelPending) == 0
                              && Math.Abs(Volatile.Read(ref _velocity)) < 0.5;

        public void Begin(double offset, double maxOffset)
        {
            Volatile.Write(ref _offset, offset);
            Volatile.Write(ref _velocity, 0);
            Volatile.Write(ref _maxOffset, maxOffset);
        }

        public void Stop() { Volatile.Write(ref _velocity, 0); Interlocked.Exchange(ref _wheelPending, 0); }

        public void Wheel(double notches) =>
            Interlocked.Add(ref _wheelPending, (int)Math.Round(notches * NotchScale));

        public void PublishDrawings(DocsCanvas canvas)
        {
            var list = new List<DocsCanvas.LineDrawing>(256);
            canvas.SnapshotLineDrawings(list);
            _drawings = list;
        }

        public void NotePumpCost(double ms) { lock (_pumps) _pumps.Add(ms); }

        public void Create(IntPtr parent, int x, int y, int w, int h)
        {
            _hwnd = CreateWindowExW(0, EnsureClass(), null, WS_CHILD,
                x, y, Math.Max(1, w), Math.Max(1, h), parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "replay-presenter" };
            _thread.Start();
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            if (_hwnd != IntPtr.Zero && w > 0 && h > 0) MoveWindow(_hwnd, x, y, w, h, false);
        }

        public void Show() { if (_hwnd != IntPtr.Zero && !IsShown) { ShowWindow(_hwnd, SW_SHOWNA); IsShown = true; } }
        public void Hide() { if (_hwnd != IntPtr.Zero && IsShown) { ShowWindow(_hwnd, SW_HIDE); IsShown = false; } }

        public void Destroy()
        {
            _running = false;
            _thread?.Join(500);
            foreach (var t in _textures.Values) t.Bitmap.Dispose();
            _textures.Clear();
            _renderParams?.Dispose();
            _replay?.Dispose();
            _target?.Dispose(); _d2d?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose();
            _dwrite?.Dispose(); _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }

        private void Loop()
        {
            try { LoopCore(); }
            catch (Exception ex) { Log($"  [d2d] RENDER THREAD FAILED: {ex.GetType().Name}: {ex.Message}"); }
        }

        private void LoopCore()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                levels, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            _device = device;
            _context = context;

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();

            // Bitblt, deliberately. The flip model lets DWM promote the swapchain to an overlay
            // plane and scan it out without compositing, which on an HDR display skips the SDR
            // white level scaling WPF receives and makes the presenter visibly brighter.
            // Measured: 0 of 158 background samples wrong composed, 106 of 175 wrong promoted,
            // and the cadence is the same either way.
            _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, new SwapChainDescription1
            {
                Width = 0,
                Height = 0,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 1,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
            });

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();

            CreateTarget();
            // ClearType needs an opaque target, and WPF draws ClearType; greyscale disagrees on
            // every glyph edge.
            _replay = new DrawingReplay(_d2d, _dwrite);

            var clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;

            while (_running)
            {
                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                if (dt > 0.05) dt = 0.05;

                EnsureBufferSize();
                Advance(dt);

                var sw = Stopwatch.StartNew();
                Draw();
                sw.Stop();

                _swapChain.Present(1, PresentFlags.None);

                if (IsShown && dt > 0 && dt < 0.5)
                {
                    _gaps.Add(dt * 1000);
                    _draws.Add(sw.Elapsed.TotalMilliseconds);
                    if (_gaps.Count >= 240) Report();
                }
            }
        }

        private void Report()
        {
            var g = _gaps.ToArray(); Array.Sort(g);
            var d = _draws.ToArray(); Array.Sort(d);
            double med = g[g.Length / 2];
            int late = 0;
            foreach (var x in _gaps) if (x > med * 1.5) late++;

            double pump = 0, pumpMax = 0;
            lock (_pumps)
            {
                if (_pumps.Count > 0)
                {
                    var pp = _pumps.ToArray(); Array.Sort(pp);
                    pump = pp[pp.Length / 2];
                    pumpMax = pp[^1];
                    _pumps.Clear();
                }
            }

            Log($"  [d2d] present median {med:F2}ms ({1000 / med:F0}/s), " +
                $"p99 {g[(int)(g.Length * 0.99)]:F2}ms, over 1.5x median {100.0 * late / _gaps.Count:F1}%  |  " +
                $"replay median {d[d.Length / 2]:F2}ms, p99 {d[(int)(d.Length * 0.99)]:F2}ms  |  " +
                $"ui pump median {pump:F2}ms, max {pumpMax:F2}ms  |  " +
                $"lines {_drawings?.Count ?? 0}");

            _gaps.Clear();
            _draws.Clear();
        }

        private void EnsureBufferSize()
        {
            GetClientRect(_hwnd, out RECT rc);
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0 || (w == _bufferWidth && h == _bufferHeight)) return;

            _d2d!.Target = null;
            _target?.Dispose();
            _target = null;
            _swapChain!.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown, SwapChainFlags.None);
            CreateTarget();
            _bufferWidth = w;
            _bufferHeight = h;
        }

        private void CreateTarget()
        {
            using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
            _target = _d2d!.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Ignore),
                96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            _d2d.Target = _target;
        }

        private void Advance(double dt)
        {
            int scaled = Interlocked.Exchange(ref _wheelPending, 0);
            double v = Volatile.Read(ref _velocity);
            if (scaled != 0)
            {
                v += scaled / (double)NotchScale * PixelsPerNotch * WheelDamping;
                Volatile.Write(ref _velocity, v);
            }

            double offset = Volatile.Read(ref _offset);
            if (Math.Abs(v) > 0.5)
            {
                double decay = Math.Exp(-dt * WheelDamping);
                offset += v * (1 - decay) / WheelDamping;
                v *= decay;
            }
            else v = 0;

            Volatile.Write(ref _offset, Math.Clamp(offset, 0, Math.Max(0, Volatile.Read(ref _maxOffset))));
            Volatile.Write(ref _velocity, v);
        }

        /// <summary>One line, already drawn into a texture.</summary>
        private sealed record LineTexture(Drawing Source, ID2D1Bitmap1 Bitmap, float X, float Top);

        private readonly Dictionary<int, LineTexture> _textures = new();

        /// <summary>
        /// How many line textures may be built in one frame.
        /// </summary>
        /// <remarks>
        /// A line costs roughly 30us to draw into its texture, so a screenful is about a
        /// millisecond. The bound only matters for the first frame of a gesture, where a whole
        /// viewport arrives at once; steady scrolling brings in well under one a frame even at
        /// a fast fling.
        /// </remarks>
        private const int BuildBudget = 64;

        /// <summary>
        /// How glyphs are rasterised into the line textures.
        /// </summary>
        /// <remarks>
        /// Which of these matches WPF is not something to reason about from the documentation.
        /// ClearType is said to be unavailable on a transparent surface, which would make WPF's
        /// BitmapCache greyscale - but the cached lines plainly do not look greyscale, and the
        /// gamma DirectWrite applies by default is not necessarily the one WPF uses either. So
        /// they are switchable, and the one that matches is the one that looks matched.
        /// </remarks>
        internal enum TextMode
        {
            /// <summary>
            /// Follow the canvas. Opaque line visuals mean WPF has an opaque surface to
            /// rasterise against and uses ClearType, so the textures are opaque and ClearType
            /// too; transparent means greyscale on both sides. Matching how the other side
            /// rasterises is the only thing that makes a handoff invisible.
            /// </summary>
            Auto,
            GreyscaleDefault,
            GreyscaleGamma22,
            ClearTypeDefault,
            ClearTypeGamma22,
        }

        private volatile int _textMode;

        public TextMode Mode => (TextMode)_textMode;

        /// <summary>Switches mode and drops the textures, so they are rebuilt the new way.</summary>
        public TextMode CycleTextMode()
        {
            _textMode = (_textMode + 1) % 5;
            _dropTextures = true;
            return Mode;
        }

        private volatile bool _dropTextures;

        /// <summary>Rebuilds every texture, after the canvas has redrawn its lines.</summary>
        public void DropTextures() => _dropTextures = true;

        private void Draw()
        {
            var list = _drawings;
            double offset = Volatile.Read(ref _offset);
            double height = _d2d!.Size.Height;

            // The canvas rounds each line transform and rounds the scroll offset separately, so
            // the distance between two lines is a constant whole number. Rounding the difference
            // instead rounds every line independently and the gaps between them change from
            // frame to frame, which reads as the text jiggling.
            double scroll = Math.Round(offset);

            // Textures first: building one needs its own BeginDraw, and Direct2D does not nest
            // them.
            if (list != null) BuildTextures(list, scroll, height);

            _d2d.BeginDraw();
            // The canvas paints its own background before its children; the display list only
            // carries the lines, so the ground has to be laid here.
            _d2d.Clear(new Color4(0x1E / 255f, 0x1E / 255f, 0x1E / 255f, 1f));

            if (list != null)
            {
                foreach (var line in list)
                {
                    double y = Math.Round(line.Y) - scroll;
                    if (y > height || y < -400) continue;
                    if (!_textures.TryGetValue(line.Index, out var tex)) continue;
                    if (!ReferenceEquals(tex.Source, line.Drawing)) continue;

                    var size = tex.Bitmap.Size;
                    // Nearest neighbour, onto whole pixels: the texture holds glyphs already
                    // antialiased for this position, so resampling it would only soften them.
                    _d2d.DrawBitmap(tex.Bitmap,
                        new Vortice.Mathematics.Rect(tex.X, (float)y + tex.Top, size.Width, size.Height),
                        1f, Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor,
                        new Vortice.Mathematics.Rect(0, 0, size.Width, size.Height));
                }
            }

            _d2d.EndDraw();
        }

        private IDWriteRenderingParams? _renderParams;
        private int _paramsMode = -1;

        /// <summary>Whether line textures should be opaque, for the mode in force.</summary>
        private bool OpaqueTextures => Mode switch
        {
            TextMode.Auto => DocsCanvas.OpaqueLineVisuals,
            TextMode.ClearTypeDefault or TextMode.ClearTypeGamma22 => true,
            _ => false,
        };

        /// <summary>Sets antialiasing and gamma for the current text mode.</summary>
        private void ApplyTextMode()
        {
            var mode = Mode;

            bool clearType = mode switch
            {
                TextMode.Auto => DocsCanvas.OpaqueLineVisuals,
                TextMode.ClearTypeGamma22 or TextMode.ClearTypeDefault => true,
                _ => false,
            };

            _d2d!.TextAntialiasMode = clearType
                ? Vortice.Direct2D1.TextAntialiasMode.Cleartype
                : Vortice.Direct2D1.TextAntialiasMode.Grayscale;

            // Auto is resolved against a flag that can change, so it is never cached.
            if (mode != TextMode.Auto && _paramsMode == (int)mode) return;
            _paramsMode = (int)mode;

            _renderParams?.Dispose();
            _renderParams = null;

            if (mode is TextMode.GreyscaleGamma22 or TextMode.ClearTypeGamma22)
            {
                // Kept for comparison. The gamma sweep that produced 2.2 was run against a
                // directly drawn WPF panel on an HDR display; against the real canvas, whose
                // lines come from a transparent cache, DirectWrite's own default matches.
                _renderParams = _dwrite!.CreateCustomRenderingParams(
                    2.2f, 0f, 1.0f, Vortice.DirectWrite.PixelGeometry.Rgb,
                    Vortice.DirectWrite.RenderingMode.Natural);
            }

            _d2d.TextRenderingParams = _renderParams;
        }

        /// <summary>
        /// Draws each newly visible line into its own texture, once.
        /// </summary>
        /// <remarks>
        /// Replaying the display list every frame cost 7 to 11ms on table-heavy content and made
        /// the presenter slower than WPF. It is the same work every time, so it is done once per
        /// line and a frame becomes a few hundred blits instead.
        ///
        /// This is the same trick phase 2 used on the WPF side - render once, composite
        /// thereafter - applied on ours. The presenter was never valuable for the drawing; it is
        /// valuable for the pacing.
        /// </remarks>
        private void BuildTextures(List<DocsCanvas.LineDrawing> list, double scroll, double height)
        {
            if (_dropTextures)
            {
                _dropTextures = false;
                foreach (var t in _textures.Values) t.Bitmap.Dispose();
                _textures.Clear();
                _renderParams?.Dispose();
                _renderParams = null;
                _paramsMode = -1;
            }

            int built = 0;
            var saved = _d2d!.Target;

            foreach (var line in list)
            {
                if (built >= BuildBudget) break;

                double y = Math.Round(line.Y) - scroll;
                if (y > height || y < -400) continue;

                if (_textures.TryGetValue(line.Index, out var existing))
                {
                    if (ReferenceEquals(existing.Source, line.Drawing)) continue;
                    existing.Bitmap.Dispose();
                    _textures.Remove(line.Index);
                }

                var bounds = line.Drawing.Bounds;
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

                int w = (int)Math.Ceiling(bounds.Width) + 2;
                int h = (int)Math.Ceiling(bounds.Height) + 2;
                if (w <= 0 || h <= 0 || w > 8192 || h > 8192) continue;

                bool opaque = OpaqueTextures;

                ID2D1Bitmap1? bitmap = null;
                try
                {
                    // An opaque texture is what lets ClearType be used at all: it needs to know
                    // what is behind a glyph, and an alpha channel means it does not. It also
                    // removes the second blend - glyphs antialiased against nothing and then
                    // composited over the background - which is the extra softness transparent
                    // caching produces on both sides.
                    bitmap = _d2d.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), IntPtr.Zero, 0,
                        new BitmapProperties1(
                            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                                opaque ? Vortice.DCommon.AlphaMode.Ignore
                                       : Vortice.DCommon.AlphaMode.Premultiplied),
                            96, 96, BitmapOptions.Target));

                    _d2d.Target = bitmap;
                    _d2d.BeginDraw();
                    _d2d.Clear(opaque
                        ? new Color4(0x1E / 255f, 0x1E / 255f, 0x1E / 255f, 1f)
                        : new Color4(0f, 0f, 0f, 0f));

                    ApplyTextMode();

                    // Both offsets. Translating only vertically left the content at its
                    // original x inside the texture, and the texture was then blitted at that
                    // same x - so every indent was applied twice, and the deeper the indent the
                    // larger the error.
                    _replay!.Replay(line.Drawing, -bounds.Left, -bounds.Top);
                    _d2d.EndDraw();

                    _textures[line.Index] = new LineTexture(
                        line.Drawing, bitmap, (float)bounds.Left, (float)bounds.Top);
                    bitmap = null;
                    built++;
                }
                catch (Exception ex)
                {
                    Log($"  [d2d] texture build failed for line {line.Index}: {ex.Message}");
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }

            _d2d.Target = saved;

            // Drop textures for lines that have left the display list, so a long document does
            // not accumulate them.
            if (_textures.Count > 1200)
            {
                var live = new HashSet<int>(list.Select(l => l.Index));
                foreach (int k in _textures.Keys.Where(k => !live.Contains(k)).ToList())
                {
                    _textures[k].Bitmap.Dispose();
                    _textures.Remove(k);
                }
            }
        }
    }
}
