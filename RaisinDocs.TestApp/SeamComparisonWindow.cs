using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
/// C3 of design/Scroll Frame Pacing.md, first question: can the seam be invisible at all?
/// </summary>
/// <remarks>
/// The presenter takes over at the start of a gesture and hands back at the end. Both handoffs
/// have to be pixel-exact, or every scroll begins and ends with a visible pop - which would be
/// worse than the stutter the whole exercise exists to cure.
///
/// So before building any handoff machinery, find out whether the two paths can agree. The
/// same lines, the same font at the same size, at the same positions, drawn once by WPF and
/// once by Direct2D, and the results differenced.
///
/// Both captures come from the composed desktop rather than from either renderer, because that
/// is what the eye actually sees and it is the only measurement that includes DWM. Capturing
/// WPF through RenderTargetBitmap would compare against software rasterisation that never
/// reaches the screen - the same trap that made the first capture-cost numbers meaningless.
///
/// A constant offset between the two is worth knowing about and easy to correct, so the diff
/// is also computed over a small search of shifts. What cannot be corrected is a difference in
/// how the glyphs themselves are rasterised.
/// </remarks>
public sealed class SeamComparisonWindow : Window
{
    private const double FontSize = 16;       // TextMeasurer.BaseFontSize
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
    private readonly D2DTextSurface _d2d;

    public SeamComparisonWindow(string[] lines)
    {
        _lines = lines;
        Title = "Seam comparison (C3)";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(BackColor);

        _wpf = new WpfTextPanel(lines);
        _d2d = new D2DTextSurface(lines);

        var grid = new Grid();
        grid.Children.Add(_wpf);
        grid.Children.Add(_d2d);
        Content = grid;

        // async void by way of an event handler: without a catch here a failure vanishes and
        // the previous run's report is left in place looking like a fresh result.
        ContentRendered += async (_, _) =>
        {
            try { await RunAsync(); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "report.txt"),
                    $"FAILED: {ex.GetType().Name}: {ex.Message}"
                    + Environment.NewLine + Environment.NewLine + ex.StackTrace);
                Close();
            }
        };
    }

    public static SeamComparisonWindow Open(string? file)
    {
        string path = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockRaisin2", "reports", "2026-08-31", "SR_report_2026-08-31_DUH768767.md");

        // Prose lines only. Blank lines and table pipes would compare mostly background, which
        // agrees trivially and would flatter the result.
        string[] lines = (File.Exists(path)
                ? File.ReadAllLines(path)
                : new[] { "The quick brown fox jumps over the lazy dog." })
            .Where(l => l.Trim().Length > 30 && !l.Contains('|'))
            .Take(24)
            .ToArray();

        return new SeamComparisonWindow(lines.Length > 0
            ? lines
            : Enumerable.Range(0, 24)
                .Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog, 0123456789.")
                .ToArray());
    }

    private async Task RunAsync()
    {
        Directory.CreateDirectory(OutDir);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"seam comparison, {_lines.Length} lines, {FontFamily} {FontSize}");
        report.AppendLine($"dpi scale {VisualTreeHelper.GetDpi(this).DpiScaleX:F2}");
        report.AppendLine();

        // WPF once: it is the target, and it does not vary.
        _d2d.Visibility = Visibility.Collapsed;
        await SettleAsync();
        using var wpf = Capture();
        wpf.Save(Path.Combine(OutDir, "wpf.png"), System.Drawing.Imaging.ImageFormat.Png);
        report.AppendLine($"captured {wpf.Width}x{wpf.Height} from the composed desktop");
        report.AppendLine();

        _d2d.Visibility = Visibility.Visible;
        await SettleAsync();

        // Sweep the rendering params rather than guess them. Gamma and contrast change glyph
        // weight, the rendering mode changes how outlines are fitted to the pixel grid, and
        // WPF's choices are not documented anywhere we can just read off.
        var modes = new[]
        {
            Vortice.DirectWrite.RenderingMode.Natural,
            Vortice.DirectWrite.RenderingMode.NaturalSymmetric,
        };

        (double diff, double over8, string label) best = (double.MaxValue, 0, "none");
        var results = new List<(double diff, double over8, string label)>();

        foreach (var mode in modes)
        foreach (float gamma in new[] { 2.0f, 2.2f, 2.4f, 2.6f, 2.8f, 3.0f })
        foreach (float level in new[] { 0.7f, 0.85f, 1.0f })
        {
            _d2d.SetRenderingParams(gamma, 0f, level, mode);
            await SettleAsync();
            using var shot = Capture();

            (double meanAbs, double over8) = Measure(wpf, shot);
            string label = $"{mode,-16} gamma {gamma:F2}  level {level:F2}";
            results.Add((meanAbs, over8, label));
            if (meanAbs < best.diff) best = (meanAbs, over8, label);
        }

        _d2d.UseMonitorRenderingParams();
        await SettleAsync();
        using (var monitorShot = Capture())
        {
            (double meanAbs, double over8) = Measure(wpf, monitorShot);
            results.Add((meanAbs, over8, "MONITOR system params"));
            if (meanAbs < best.diff) best = (meanAbs, over8, "MONITOR system params");
        }

        results.Sort((x, y) => x.diff.CompareTo(y.diff));
        report.AppendLine("rendering params, closest first:");
        report.AppendLine("  mean abs   >8    setting");
        foreach (var r in results.Take(10))
            report.AppendLine($"  {r.diff,6:F2}   {r.over8,5:F2}%  {r.label}");
        report.AppendLine();
        report.AppendLine($"worst of {results.Count}: {results[^1].diff:F2} ({results[^1].label})");
        report.AppendLine();

        // Keep the best, and write the full picture for it.
        if (best.label.StartsWith("MONITOR"))
        {
            _d2d.UseMonitorRenderingParams();
        }
        else
        {
            var parts = best.label.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _d2d.SetRenderingParams(float.Parse(parts[2]), 0f, float.Parse(parts[4]),
                Enum.Parse<Vortice.DirectWrite.RenderingMode>(parts[0]));
        }
        await SettleAsync();
        using var bestShot = Capture();
        bestShot.Save(Path.Combine(OutDir, "d2d.png"), System.Drawing.Imaging.ImageFormat.Png);

        report.AppendLine($"best: {best.label}");
        Compare(wpf, bestShot, report);

        File.WriteAllText(Path.Combine(OutDir, "report.txt"), report.ToString());
        Close();
    }

    /// <summary>Mean absolute difference, and the share of pixels differing visibly.</summary>
    private static (double meanAbs, double over8) Measure(System.Drawing.Bitmap a,
                                                          System.Drawing.Bitmap b)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        byte[] pa = ToBytes(a, w, h), pb = ToBytes(b, w, h);

        long sum = 0, channels = 0, over8 = 0, pixels = 0;

        for (int y = 4; y < h - 4; y++)
        for (int x = 4; x < w - 4; x++)
        {
            int i = (y * w + x) * 4;
            int dr = Math.Abs(pa[i] - pb[i]);
            int dg = Math.Abs(pa[i + 1] - pb[i + 1]);
            int db = Math.Abs(pa[i + 2] - pb[i + 2]);

            sum += dr + dg + db;
            channels += 3;
            pixels++;
            if (Math.Max(dr, Math.Max(dg, db)) > 8) over8++;
        }

        return ((double)sum / channels, 100.0 * over8 / pixels);
    }

    /// <summary>Lets several frames reach the screen before capturing.</summary>
    private static async Task SettleAsync()
    {
        for (int i = 0; i < 6; i++)
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
    }

    // --- capture -------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>
    /// The client area as the desktop shows it. Physical pixels throughout - GetClientRect and
    /// ClientToScreen both work in them - so no DPI conversion is involved.
    /// </summary>
    private System.Drawing.Bitmap Capture()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetClientRect(hwnd, out RECT r);
        var origin = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(hwnd, ref origin);

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(origin.X, origin.Y, 0, 0, new System.Drawing.Size(w, h),
            System.Drawing.CopyPixelOperation.SourceCopy);
        return bmp;
    }

    // --- comparison ----------------------------------------------------------------------

    private static void Compare(System.Drawing.Bitmap a, System.Drawing.Bitmap b,
                                System.Text.StringBuilder report)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        byte[] pa = ToBytes(a, w, h), pb = ToBytes(b, w, h);

        // A whole-pixel offset between the two would swamp everything else and is trivially
        // corrected by moving the surface, so find the best alignment before judging the rest.
        int bestDx = 0, bestDy = 0;
        double bestMean = double.MaxValue;
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            double mean = MeanAbs(pa, pb, w, h, dx, dy);
            if (mean < bestMean) { bestMean = mean; bestDx = dx; bestDy = dy; }
        }

        report.AppendLine($"best alignment: dx={bestDx} dy={bestDy}   (0,0 means they already line up)");
        report.AppendLine($"mean abs difference at (0,0): {MeanAbs(pa, pb, w, h, 0, 0):F2} / 255");
        report.AppendLine($"mean abs difference aligned : {bestMean:F2} / 255");
        report.AppendLine();

        Histogram(pa, pb, w, h, bestDx, bestDy, report);
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

    private static double MeanAbs(byte[] a, byte[] b, int w, int h, int dx, int dy)
    {
        long sum = 0;
        long n = 0;
        for (int y = 4; y < h - 4; y++)
        {
            int sy = y + dy;
            if (sy < 0 || sy >= h) continue;
            for (int x = 4; x < w - 4; x++)
            {
                int sx = x + dx;
                if (sx < 0 || sx >= w) continue;
                int ia = (y * w + x) * 4, ib = (sy * w + sx) * 4;
                sum += Math.Abs(a[ia] - b[ib]) + Math.Abs(a[ia + 1] - b[ib + 1]) + Math.Abs(a[ia + 2] - b[ib + 2]);
                n += 3;
            }
        }
        return n == 0 ? double.MaxValue : (double)sum / n;
    }

    /// <summary>
    /// How many pixels differ, and by how much. The mean hides the shape of the disagreement:
    /// a faint difference over every glyph edge reads very differently from a few pixels being
    /// wildly wrong, and only the second is visible as a pop.
    /// </summary>
    private static void Histogram(byte[] a, byte[] b, int w, int h, int dx, int dy,
                                  System.Text.StringBuilder report)
    {
        long total = 0, over2 = 0, over8 = 0, over32 = 0, over64 = 0, max = 0;

        for (int y = 4; y < h - 4; y++)
        {
            int sy = y + dy;
            if (sy < 0 || sy >= h) continue;
            for (int x = 4; x < w - 4; x++)
            {
                int sx = x + dx;
                if (sx < 0 || sx >= w) continue;
                int ia = (y * w + x) * 4, ib = (sy * w + sx) * 4;
                int d = Math.Max(Math.Abs(a[ia] - b[ib]),
                        Math.Max(Math.Abs(a[ia + 1] - b[ib + 1]), Math.Abs(a[ia + 2] - b[ib + 2])));
                total++;
                if (d > 2) over2++;
                if (d > 8) over8++;
                if (d > 32) over32++;
                if (d > 64) over64++;
                if (d > max) max = d;
            }
        }

        report.AppendLine($"pixels compared      {total}");
        report.AppendLine($"differing by >2      {100.0 * over2 / total,6:F2}%");
        report.AppendLine($"differing by >8      {100.0 * over8 / total,6:F2}%   (roughly the visible threshold)");
        report.AppendLine($"differing by >32     {100.0 * over32 / total,6:F2}%");
        report.AppendLine($"differing by >64     {100.0 * over64 / total,6:F2}%   (a glyph in a different place)");
        report.AppendLine($"largest difference   {max}");
    }

    // --- the two renderers ---------------------------------------------------------------

    /// <summary>Draws the lines the way DocsCanvas does: FormattedText, one per line.</summary>
    private sealed class WpfTextPanel : FrameworkElement
    {
        private readonly string[] _lines;

        public WpfTextPanel(string[] lines) => _lines = lines;

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(new SolidColorBrush(BackColor), null,
                new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));

            var typeface = new Typeface(FontFamily);
            var brush = new SolidColorBrush(TextColor);

            for (int i = 0; i < _lines.Length; i++)
            {
                var ft = new FormattedText(_lines[i], CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, FontSize, brush, dpi);
                dc.DrawText(ft, new Point(PaddingX, Math.Round(i * LineHeight)));
            }
        }
    }

    /// <summary>The same lines through Direct2D and DirectWrite, at the same positions.</summary>
    private sealed class D2DTextSurface : HwndHost
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        private readonly string[] _lines;
        private IntPtr _hwnd;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain1? _swapChain;
        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2d;
        private ID2D1Bitmap1? _target;
        private IDWriteFactory? _dwrite;
        private IDWriteTextFormat? _format;
        private ID2D1SolidColorBrush? _brush;
        private Thread? _thread;
        private volatile bool _running;

        // ClearType is only the first of the settings that have to agree. Gamma, contrast and
        // the rendering mode all change how a glyph is rasterised, and DirectWrite's defaults
        // are not WPF's. Applied on the render thread, which owns the device context.
        private volatile bool _paramsDirty;
        private volatile bool _useMonitorParams;
        private float _gamma = 1.8f, _contrast = 0.5f, _clearTypeLevel = 1f;
        private Vortice.DirectWrite.RenderingMode _mode = Vortice.DirectWrite.RenderingMode.Default;

        public D2DTextSurface(string[] lines) => _lines = lines;

        public void SetRenderingParams(float gamma, float contrast, float clearTypeLevel,
                                       Vortice.DirectWrite.RenderingMode mode)
        {
            _gamma = gamma; _contrast = contrast; _clearTypeLevel = clearTypeLevel; _mode = mode;
            _useMonitorParams = false;
            _paramsDirty = true;
        }

        /// <summary>
        /// The system's own ClearType settings for this monitor - gamma, contrast and level as
        /// the user tuned them. This is where WPF gets its text appearance from, so it should
        /// agree by construction rather than by a fitted constant.
        /// </summary>
        public void UseMonitorRenderingParams()
        {
            _useMonitorParams = true;
            _paramsDirty = true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            _hwnd = CreateWindowExW(0, "static", null, WS_CHILD | WS_VISIBLE,
                0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "seam-d2d" };
            _thread.Start();
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _running = false;
            _thread?.Join(500);
            _brush?.Dispose(); _format?.Dispose(); _dwrite?.Dispose();
            _target?.Dispose(); _d2d?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose();
            _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
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

            _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, new SwapChainDescription1
            {
                Width = 0,
                Height = 0,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
            });

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
            _format = _dwrite.CreateTextFormat(FontFamily,
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                (float)FontSize);

            using (var surface = _swapChain.GetBuffer<IDXGISurface>(0))
            {
                _target = _d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Ignore),
                    96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            }
            _d2d.Target = _target;
            // WPF draws text with ClearType, so anything else disagrees on every glyph edge.
            _d2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Cleartype;
            _brush = _d2d.CreateSolidColorBrush(ToColor4(TextColor));

            while (_running)
            {
                if (_paramsDirty)
                {
                    _paramsDirty = false;
                    var prev = _d2d.TextRenderingParams;
                    _d2d.TextRenderingParams = _useMonitorParams
                        ? _dwrite.CreateMonitorRenderingParams(MonitorFromWindow(_hwnd, 2))
                        : _dwrite.CreateCustomRenderingParams(
                            _gamma, _contrast, _clearTypeLevel,
                            Vortice.DirectWrite.PixelGeometry.Rgb, _mode);
                    prev?.Dispose();
                }

                Draw();
                _swapChain.Present(1, PresentFlags.None);
            }
        }

        private void Draw()
        {
            var size = _d2d!.Size;
            _d2d.BeginDraw();
            _d2d.Clear(ToColor4(BackColor));

            for (int i = 0; i < _lines.Length; i++)
            {
                using var layout = _dwrite!.CreateTextLayout(
                    _lines[i], _format!, size.Width - (float)PaddingX * 2, (float)LineHeight);
                _d2d.DrawTextLayout(
                    new System.Numerics.Vector2((float)PaddingX, (float)Math.Round(i * LineHeight)),
                    layout, _brush!);
            }

            _d2d.EndDraw();
        }

        private static Color4 ToColor4(Color c)
            => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }
}
