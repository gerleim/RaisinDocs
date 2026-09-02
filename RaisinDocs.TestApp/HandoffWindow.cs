using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
/// C3 of design/Scroll Frame Pacing.md: the handoff, on a real gesture.
/// </summary>
/// <remarks>
/// The static comparison showed the two renderers can agree on one screenful at offset zero.
/// That is necessary and not sufficient. A gesture hands over at whatever offset the scroll
/// happens to be at, so the agreement has to hold at every sub-line position, and the swap
/// itself has to happen without a blank or repeated frame.
///
/// This window does both halves:
///
///   - On startup it sweeps the scroll offset a pixel at a time across a line and differences
///     the two renderers at each, which is the objective half. A seam that only closes at
///     offset zero would show up here as a diff that rises and falls with the offset.
///   - Then it stays open and scrolls for real: the wheel starts a gesture, the presenter takes
///     over for the duration, and WPF gets it back when the coast ends. F9 disables the
///     handoff so the same gesture can be compared against plain WPF scrolling, which is the
///     subjective half and the one that actually decides it.
///
/// Order matters at both ends, because the child HWND is always on top of WPF content:
/// taking over means drawing a frame at the current offset and only then showing the surface;
/// handing back means moving WPF and letting it paint before the surface is hidden. Getting
/// either backwards shows one frame of the wrong thing.
/// </remarks>
public sealed class HandoffWindow : Window
{
    private const double FontSize = 16;
    private const string FontFamily = "Segoe UI";
    private const double LineHeight = 22;
    private const double PaddingX = 8;

    private static readonly Color BackColor = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color TextColor = Color.FromRgb(0xDC, 0xDC, 0xDC);

    private static readonly string OutDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "seam");

    private readonly string[] _lines;
    private readonly WpfTextPanel _wpf;
    private readonly PresenterSurface _d2d;
    private readonly TextBlock _readout;

    /// <summary>Whether the presenter takes over at all. F9 toggles it, for the A/B.</summary>
    private bool _handoffEnabled = true;

    /// <summary>Scroll position while WPF owns it. The presenter owns it during a gesture.</summary>
    private double _offset;
    private double _velocity;
    private bool _presenting;
    private bool _handingBack;

    private const double WheelDamping = 10.0;
    private const double PixelsPerNotch = 120.0;

    /// <summary>Whether to run the offset sweep before handing the window over.</summary>
    private readonly bool _sweep;

    public HandoffWindow(string[] lines, bool sweep)
    {
        _lines = lines;
        _sweep = sweep;
        Title = "Handoff A/B (C3) - wheel to scroll, F9 toggles the presenter";
        Width = 1000;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(BackColor);

        _wpf = new WpfTextPanel(lines);
        _d2d = new PresenterSurface(lines) { Visibility = Visibility.Collapsed };
        _readout = new TextBlock
        {
            Foreground = Brushes.Gainsboro,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(8, 4, 8, 4),
            Text = "presenter ON (F9 to toggle)",
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_readout, 0);
        var host = new Grid();
        host.Children.Add(_wpf);
        host.Children.Add(_d2d);
        Grid.SetRow(host, 1);
        grid.Children.Add(_readout);
        grid.Children.Add(host);
        Content = grid;

        MouseWheel += OnWheel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F9) return;
            _handoffEnabled = !_handoffEnabled;
            _readout.Text = _handoffEnabled
                ? "presenter ON  (F9 to toggle) - scrolling hands over to Direct2D"
                : "presenter OFF (F9 to toggle) - scrolling stays in WPF";
            e.Handled = true;
        };

        ContentRendered += async (_, _) =>
        {
            if (!_sweep)
            {
                // Straight to the A/B. The sweep depends on screen capture, which depends on
                // nothing covering the window - not a thing to make someone wait through.
                CompositionTarget.Rendering += OnFrame;
                return;
            }

            try { await SweepAsync(); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "handoff.txt"),
                    $"FAILED: {ex.GetType().Name}: {ex.Message}"
                    + Environment.NewLine + Environment.NewLine + ex.StackTrace);
            }
        };

        Closed += (_, _) => _d2d.Stop();
    }

    public static HandoffWindow Open(string? file, bool sweep)
    {
        string path = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockRaisin2", "reports", "2026-08-31", "SR_report_2026-08-31_DUH768767.md");

        string[] lines = (File.Exists(path)
                ? File.ReadAllLines(path)
                : Enumerable.Range(0, 2000)
                    .Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog.")
                    .ToArray())
            .Where(l => l.Trim().Length > 0)
            .ToArray();

        return new HandoffWindow(lines, sweep);
    }

    // --- the gesture ----------------------------------------------------------------------

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        double notches = -e.Delta / 120.0;

        if (!_handoffEnabled)
        {
            _velocity += notches * PixelsPerNotch * WheelDamping;
            return;
        }

        if (!_presenting) TakeOver();
        _d2d.Wheel(notches);
    }

    /// <summary>
    /// Draw at the current offset first, show second. The surface is a child window and sits
    /// over the WPF content, so showing it before it holds the right pixels would put one
    /// frame of stale content - or an empty buffer - on screen.
    /// </summary>
    private void TakeOver()
    {
        _d2d.SetOffset(_offset);
        _presenting = true;
        _handingBack = false;
        _d2d.WaitForFrame();
        _d2d.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Move WPF first, let it paint, hide the surface last - the mirror of taking over. WPF
    /// paints on its own schedule, so this waits a render pass rather than assuming.
    /// </summary>
    private void HandBack()
    {
        // _presenting has to be cleared here, not in the callback below. It is the flag that
        // stops OnFrame calling this again, and OnFrame runs every frame: leaving it set until
        // an asynchronous callback ran queued one hand-back per frame, each re-invalidating WPF
        // and re-hiding the surface.
        if (_handingBack) return;
        _handingBack = true;
        _presenting = false;

        _offset = _d2d.Offset;
        _wpf.Offset = _offset;
        _wpf.InvalidateVisual();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // A new gesture may have started while this was queued, in which case the surface
            // is wanted after all.
            if (!_presenting) _d2d.Visibility = Visibility.Collapsed;
            _handingBack = false;
        });
    }

    /// <summary>WPF's own scrolling, used when the presenter is switched off.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_presenting)
        {
            if (_d2d.IsIdle) HandBack();
            return;
        }

        if (Math.Abs(_velocity) < 0.5) { _velocity = 0; return; }

        double dt = 1.0 / 144;
        double decay = Math.Exp(-dt * WheelDamping);
        _offset += _velocity * (1 - decay) / WheelDamping;
        _velocity *= decay;
        _offset = Math.Max(0, Math.Min(_offset, _lines.Length * LineHeight - _wpf.ActualHeight));

        _wpf.Offset = _offset;
        _wpf.InvalidateVisual();
    }

    // --- the objective half ---------------------------------------------------------------

    /// <summary>
    /// Differences the two renderers at every sub-line scroll offset. Agreement at offset zero
    /// says nothing about a gesture, which hands over wherever it happens to be.
    /// </summary>
    private async Task SweepAsync()
    {
        // CopyFromScreen reads the desktop, so it returns whatever is on screen at those
        // coordinates - including another window sitting over this one. Without this the sweep
        // silently measures the wrong pixels, which it did: backgrounds came back as 102,98,97
        // and 63,62,62 where the theme is 30,30,30.
        Topmost = true;
        Activate();
        await Task.Delay(300);

        Directory.CreateDirectory(OutDir);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"handoff sweep, line height {LineHeight}, {FontFamily} {FontSize}");
        report.AppendLine();
        report.AppendLine("offset   mean abs   >8      >64    max");

        double worstMean = 0, worstOver8 = 0;
        int worstOffset = 0;

        for (int off = 0; off < (int)LineHeight + 2; off++)
        {
            _wpf.Offset = off;
            _wpf.InvalidateVisual();
            _d2d.SetOffset(off);

            _d2d.Visibility = Visibility.Collapsed;
            using var wpf = await CaptureWhenVisibleAsync();

            _d2d.Visibility = Visibility.Visible;
            using var d2d = await CaptureWhenVisibleAsync();

            // Keep one pair mid-line, so the residual can be looked at rather than inferred
            // from a percentage.
            if (off == 11)
            {
                wpf.Save(Path.Combine(OutDir, "handoff_wpf.png"), System.Drawing.Imaging.ImageFormat.Png);
                d2d.Save(Path.Combine(OutDir, "handoff_d2d.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var m = Measure(wpf, d2d);

            // If the captured background is not the theme colour, something was over the
            // window and the numbers mean nothing. Say so rather than reporting them.
            var bg = wpf.GetPixel(wpf.Width - 12, wpf.Height / 2);
            bool occluded = Math.Abs(bg.R - BackColor.R) > 6 || Math.Abs(bg.G - BackColor.G) > 6;
            if (occluded) report.AppendLine($"{off,5}   OCCLUDED - captured background " +
                                            $"{bg.R},{bg.G},{bg.B}, expected " +
                                            $"{BackColor.R},{BackColor.G},{BackColor.B}");
            if (!occluded)
                report.AppendLine($"{off,5}   {m.meanAbs,8:F2}   {m.over8,5:F2}%  {m.over64,5:F2}%  {m.max,4}");

            if (!occluded && m.meanAbs > worstMean)
            {
                worstMean = m.meanAbs;
                worstOver8 = m.over8;
                worstOffset = off;
            }
        }

        report.AppendLine();
        report.AppendLine($"worst offset {worstOffset}: mean {worstMean:F2}, {worstOver8:F2}% differing by >8");
        report.AppendLine("(offset 0 alone measured mean 0.54 / 3.16% in the static comparison)");

        _d2d.Visibility = Visibility.Collapsed;
        _wpf.Offset = 0;
        _offset = 0;
        _wpf.InvalidateVisual();
        Topmost = false;

        File.WriteAllText(Path.Combine(OutDir, "handoff.txt"), report.ToString());
        _readout.Text = $"sweep done - worst offset {worstOffset}, {worstOver8:F2}% differing. " +
                        "Wheel to scroll, F9 toggles the presenter.";

        // Only now: while it is subscribed, nothing below Render priority gets a turn.
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>
    /// Waits for the change to reach the screen. A plain delay rather than an idle-priority
    /// yield: the capture reads the composed desktop, so what matters is that DWM has
    /// presented, and idle priority is starved by anything animating.
    /// </summary>
    private static async Task SettleAsync() => await Task.Delay(200);

    /// <summary>
    /// Captures, and retries while something is covering the window. The desktop is shared
    /// with whatever else is running, so a capture is only worth keeping once the background
    /// reads as the theme colour.
    /// </summary>
    private async Task<System.Drawing.Bitmap> CaptureWhenVisibleAsync()
    {
        System.Drawing.Bitmap? shot = null;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0)
            {
                Topmost = false;
                Topmost = true;
                Activate();
            }

            await SettleAsync();
            shot?.Dispose();
            shot = Capture();

            var bg = shot.GetPixel(shot.Width - 12, shot.Height / 2);
            if (Math.Abs(bg.R - BackColor.R) <= 6 && Math.Abs(bg.G - BackColor.G) <= 6)
                return shot;
        }

        return shot!;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>The scrolling area only, so the readout above it is not compared.</summary>
    private System.Drawing.Bitmap Capture()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetClientRect(hwnd, out RECT r);
        var origin = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(hwnd, ref origin);

        var top = _wpf.TranslatePoint(new Point(0, 0), this);
        int skip = (int)Math.Round(top.Y);

        int w = r.Right - r.Left, h = r.Bottom - r.Top - skip;
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(origin.X, origin.Y + skip, 0, 0, new System.Drawing.Size(w, h),
            System.Drawing.CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static (double meanAbs, double over8, double over64, int max) Measure(
        System.Drawing.Bitmap a, System.Drawing.Bitmap b)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        byte[] pa = ToBytes(a, w, h), pb = ToBytes(b, w, h);

        long sum = 0, channels = 0, over8 = 0, over64 = 0, pixels = 0;
        int max = 0;

        for (int y = 4; y < h - 4; y++)
        for (int x = 4; x < w - 4; x++)
        {
            int i = (y * w + x) * 4;
            int dr = Math.Abs(pa[i] - pb[i]);
            int dg = Math.Abs(pa[i + 1] - pb[i + 1]);
            int db = Math.Abs(pa[i + 2] - pb[i + 2]);
            int d = Math.Max(dr, Math.Max(dg, db));

            sum += dr + dg + db;
            channels += 3;
            pixels++;
            if (d > 8) over8++;
            if (d > 64) over64++;
            if (d > max) max = d;
        }

        return ((double)sum / channels, 100.0 * over8 / pixels, 100.0 * over64 / pixels, max);
    }

    private static byte[] ToBytes(System.Drawing.Bitmap bmp, int w, int h)
    {
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * w * 4, w * 4);
        bmp.UnlockBits(data);
        return bytes;
    }

    // --- the two renderers -----------------------------------------------------------------

    private sealed class WpfTextPanel : FrameworkElement
    {
        private readonly string[] _lines;
        public double Offset { get; set; }

        public WpfTextPanel(string[] lines) => _lines = lines;

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(new SolidColorBrush(BackColor), null,
                new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));

            var typeface = new Typeface(FontFamily);
            var brush = new SolidColorBrush(TextColor);

            int first = Math.Max(0, (int)(Offset / LineHeight));
            int last = Math.Min(_lines.Length - 1, (int)((Offset + ActualHeight) / LineHeight));

            for (int i = first; i <= last; i++)
            {
                var ft = new FormattedText(_lines[i], CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, FontSize, brush, dpi);
                dc.DrawText(ft, new Point(PaddingX, Math.Round(i * LineHeight - Offset)));
            }
        }
    }

    /// <summary>The paced Direct2D presenter, settled to the parameters the seam sweep found.</summary>
    private sealed class PresenterSurface : HwndHost
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX cls);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? name);

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        // Held for the lifetime of the process: the class keeps a pointer to this delegate, and
        // letting it be collected would leave the window calling into freed memory.
        private static WndProc? _wndProc;
        private static string? _className;

        /// <summary>
        /// A window class with no background brush.
        /// </summary>
        /// <remarks>
        /// The predefined "static" class carries a light background brush, so every time the
        /// surface was shown Windows painted that area pale before Direct2D drew anything -
        /// which is visible as the background flashing bright at the end of a gesture. With a
        /// null brush nothing is erased and the swapchain's own pixels are all that appear.
        /// </remarks>
        private static string EnsureWindowClass()
        {
            if (_className != null) return _className;

            _wndProc = DefWindowProcW;
            var cls = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandleW(null),
                hbrBackground = IntPtr.Zero,
                lpszClassName = "RaisinDocsPresenterSurface",
            };

            RegisterClassExW(ref cls);
            return _className = cls.lpszClassName;
        }

        private readonly string[] _lines;
        private readonly Dictionary<int, IDWriteTextLayout> _layouts = new();

        private IntPtr _hwnd;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain2? _swapChain;
        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2d;
        private ID2D1Bitmap1? _target;
        private IDWriteFactory? _dwrite;
        private IDWriteTextFormat? _format;
        private ID2D1SolidColorBrush? _brush;

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        private Thread? _thread;
        private volatile bool _running;
        private long _frames;
        private int _bufferWidth, _bufferHeight;
        private double _offset;
        private double _velocity;
        private int _wheelPending;

        public PresenterSurface(string[] lines) => _lines = lines;

        public double Offset => Volatile.Read(ref _offset);
        public bool IsIdle => Math.Abs(Volatile.Read(ref _velocity)) < 0.5;

        public void SetOffset(double offset)
        {
            Volatile.Write(ref _offset, offset);
            Volatile.Write(ref _velocity, 0);
        }

        public void Wheel(double notches) => Interlocked.Add(ref _wheelPending, (int)notches);

        /// <summary>Blocks until the surface has drawn a frame, so it is safe to show.</summary>
        public void WaitForFrame()
        {
            // Bounded tightly: while the surface is hidden its swapchain may be occluded and
            // stop advancing altogether, and this runs on the UI thread. Showing one stale
            // frame is a far smaller fault than freezing the gesture that is starting.
            long start = Interlocked.Read(ref _frames);
            var sw = Stopwatch.StartNew();
            while (Interlocked.Read(ref _frames) <= start + 1 && sw.ElapsedMilliseconds < 25)
                Thread.Sleep(1);
        }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            _hwnd = CreateWindowExW(0, EnsureWindowClass(), null, WS_CHILD | WS_VISIBLE,
                0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "handoff-presenter" };
            _thread.Start();
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Stop();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(500);
            foreach (var l in _layouts.Values) l.Dispose();
            _layouts.Clear();
            _brush?.Dispose(); _format?.Dispose(); _dwrite?.Dispose();
            _target?.Dispose(); _d2d?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose();
            _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _device = null;
        }

        private void Loop()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                levels, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            _device = device;
            _context = context;

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();

            using (var sc1 = factory.CreateSwapChainForHwnd(_device, _hwnd, new SwapChainDescription1
            {
                Width = 0,
                Height = 0,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            }))
            {
                _swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            }
            _swapChain.MaximumFrameLatency = 1;

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
            _format = _dwrite.CreateTextFormat(FontFamily,
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                (float)FontSize);

            CreateTarget();
            _d2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Cleartype;

            // The settings the seam sweep landed on: mean difference 0.54 of 255 against WPF.
            _d2d.TextRenderingParams = _dwrite.CreateCustomRenderingParams(
                2.2f, 0f, 1.0f, Vortice.DirectWrite.PixelGeometry.Rgb,
                Vortice.DirectWrite.RenderingMode.Natural);

            _brush = _d2d.CreateSolidColorBrush(ToColor4(TextColor));

            var waitable = _swapChain.FrameLatencyWaitableObject;
            var clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;

            while (_running)
            {
                // A short timeout, so a hidden or occluded swapchain cannot park this thread:
                // it has to keep drawing at the current offset, ready to be shown.
                if (waitable != IntPtr.Zero) WaitForSingleObject(waitable, 100);

                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                if (dt > 0.05) dt = 0.05;

                EnsureBufferSize();
                Advance(dt);
                Draw();
                _swapChain.Present(1, PresentFlags.None);
                Interlocked.Increment(ref _frames);
            }
        }

        /// <summary>
        /// Keeps the swapchain the size of its window. The surface starts collapsed, so its
        /// HWND is created at a placeholder size and the swapchain is built to match; without
        /// this, showing it later stretches that buffer over the real area and the presenter
        /// draws something quite different from what WPF drew.
        /// </summary>
        private void EnsureBufferSize()
        {
            GetClientRect(_hwnd, out RECT rc);
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0 || (w == _bufferWidth && h == _bufferHeight)) return;

            // The target holds a reference to the back buffer, so it has to go first.
            _d2d!.Target = null;
            _target?.Dispose();
            _target = null;

            _swapChain!.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown,
                SwapChainFlags.FrameLatencyWaitableObject);

            CreateTarget();
            _bufferWidth = w;
            _bufferHeight = h;
        }

        private void CreateTarget()
        {
            using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
            // AlphaMode.Ignore: ClearType needs an opaque target, and without it Direct2D
            // falls back to greyscale and disagrees with WPF on every glyph edge.
            _target = _d2d!.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Ignore),
                96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            _d2d.Target = _target;
        }

        private void Advance(double dt)
        {
            int notches = Interlocked.Exchange(ref _wheelPending, 0);
            double v = Volatile.Read(ref _velocity);
            if (notches != 0) v += notches * PixelsPerNotch * WheelDamping;

            double offset = Volatile.Read(ref _offset);
            if (Math.Abs(v) > 0.5)
            {
                double decay = Math.Exp(-dt * WheelDamping);
                offset += v * (1 - decay) / WheelDamping;
                v *= decay;
            }
            else v = 0;

            double max = Math.Max(0, _lines.Length * LineHeight - _d2d!.Size.Height);
            Volatile.Write(ref _offset, Math.Clamp(offset, 0, max));
            Volatile.Write(ref _velocity, v);
        }

        private void Draw()
        {
            var size = _d2d!.Size;
            double offset = Volatile.Read(ref _offset);

            _d2d.BeginDraw();
            _d2d.Clear(ToColor4(BackColor));

            int first = Math.Max(0, (int)(offset / LineHeight));
            int last = Math.Min(_lines.Length - 1, (int)((offset + size.Height) / LineHeight));

            for (int i = first; i <= last; i++)
            {
                if (!_layouts.TryGetValue(i, out var layout))
                {
                    // No wrapping: DirectWrite wraps inside a text layout by default, but
                    // the canvas wraps in LayoutEngine and hands the renderer visual lines that
                    // are already final. Wrapping again here re-flows text WPF left alone, and
                    // the two modes then disagree about where lines break.
                    layout = _dwrite!.CreateTextLayout(
                        _lines[i], _format!, size.Width - (float)PaddingX * 2, (float)LineHeight);
                    layout.WordWrapping = Vortice.DirectWrite.WordWrapping.NoWrap;
                    _layouts[i] = layout;
                }

                _d2d.DrawTextLayout(
                    new System.Numerics.Vector2((float)PaddingX,
                        (float)Math.Round(i * LineHeight - offset)),
                    layout, _brush!);
            }

            _d2d.EndDraw();
            Trim(first, last);
        }

        private void Trim(int first, int last)
        {
            const int window = 400;
            if (_layouts.Count <= window * 2) return;
            foreach (int k in _layouts.Keys.Where(k => k < first - window || k > last + window).ToList())
            {
                _layouts[k].Dispose();
                _layouts.Remove(k);
            }
        }

        private static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }
}
