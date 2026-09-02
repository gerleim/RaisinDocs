using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
// Direct2D, DirectWrite, Direct3D and WPF all define these names. Aliases rather than
// full qualification at every use, so the drawing code stays readable.
using FactoryType = Vortice.Direct2D1.FactoryType;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using Color = System.Windows.Media.Color;

namespace RaisinDocs.TestApp;

/// <summary>
/// C2 of design/Scroll Frame Pacing.md: the presenter surface, drawing real text.
/// </summary>
/// <remarks>
/// The prototype (<see cref="PresenterPrototypeWindow"/>) established that a flip-model
/// swapchain paced by a latency waitable object holds one refresh cleanly where WPF quantises
/// to multiples of it. It presented a colour sweep, so it proved the cadence and nothing else.
///
/// The other half was where the pixels come from, and measurement closed the door on WPF
/// supplying them: RenderTargetBitmap rasterises in software, at ~350us per cached line visual
/// against 8.6us to composite the same line live, flat in area because the cost is the tree and
/// not the pixels. It is software by design - dotnet/wpf#9021 asks for a hardware path and is
/// open - so there is no version of that approach that gets cheaper.
///
/// So the text is drawn here instead, with Direct2D and DirectWrite, straight into the
/// swapchain back buffer on the GPU. No capture step at all.
///
/// What this stage has to answer, before the seam and the ring buffer are worth building:
///   - what a line costs to draw through DirectWrite, against 8.6us to composite a cached one
///   - whether the cadence survives real drawing, or only survived an empty clear
///   - how much of the cost is laying text out rather than drawing it, which decides whether
///     the text layouts need the same caching the FormattedTexts needed
/// </remarks>
public sealed class TextPresenterWindow : Window
{
    private readonly TextBlock _readout = new()
    {
        Foreground = Brushes.Gainsboro,
        Margin = new Thickness(8, 6, 8, 6),
        FontFamily = new FontFamily("Consolas"),
    };

    private readonly TextSurface _surface;

    public TextPresenterWindow(string[] lines, string title, double autoSpeed)
    {
        _surface = new TextSurface(lines, autoSpeed);

        Title = $"{title} - paced text presenter (C2)";
        Width = 1200;
        Height = 900;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_readout, 0);
        Grid.SetRow(_surface, 1);
        grid.Children.Add(_readout);
        grid.Children.Add(_surface);
        Content = grid;

        // A wheel notch here means what it means in the canvas, so the feel is comparable.
        MouseWheel += (_, e) => _surface.Wheel(e.Delta / 120.0);
        _surface.Stats += s => Dispatcher.BeginInvoke(() => _readout.Text = s);
        Closed += (_, _) => _surface.Stop();
    }

    /// <summary>Reads the document the sandbox would open, so measurements are comparable.</summary>
    /// <param name="autoSpeed">
    /// Sweep speed in pixels a second. The interesting variable: at 1200 the layout cache is
    /// barely touched, and a fast fling is where building text layouts could put cost back on
    /// the frame. Raise it to find out rather than assume.
    /// </param>
    public static TextPresenterWindow Open(string? file, double autoSpeed = 1200)
    {
        string path = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockRaisin2", "reports", "2026-08-31", "SR_report_2026-08-31_DUH768767.md");

        string[] lines = File.Exists(path)
            ? File.ReadAllLines(path)
            : Enumerable.Range(0, 3000)
                .Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog, 0123456789.")
                .ToArray();

        return new TextPresenterWindow(lines,
            File.Exists(path) ? Path.GetFileName(path) : "generated", autoSpeed);
    }

    /// <summary>A child HWND we present to ourselves, outside the WPF compositor.</summary>
    private sealed class TextSurface : HwndHost
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;

        // Matched to the canvas so the numbers can be compared with the WPF path.
        private const float FontSize = 14f;
        private const float LineHeight = 20f;
        private const float PaddingX = 8f;

        // The same physics as ScrollController, so a fling covers the same ground.
        private const double WheelDamping = 10.0;
        private const double PixelsPerNotch = 120.0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        private readonly string[] _lines;

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
        private ID2D1SolidColorBrush? _text;

        /// <summary>
        /// One text layout per line, built on demand. The WPF path needed exactly this for
        /// FormattedText - laying text out is the dear half, drawing it is cheap - so the
        /// question is whether DirectWrite behaves the same way. Counted, not assumed.
        /// </summary>
        private readonly Dictionary<int, IDWriteTextLayout> _layouts = new();

        private Thread? _thread;
        private volatile bool _running;
        private double _offset;
        private double _velocity;
        private int _wheelPending;

        /// <summary>
        /// Sweeps the document continuously so the surface is always drawing new lines. A
        /// coast from a wheel notch decays within a second, which is too short a sample and
        /// too dependent on when it was taken; this holds a constant speed instead.
        /// </summary>
        private bool _auto = true;
        private int _autoDirection = 1;
        private readonly double _autoSpeed;

        public event Action<string>? Stats;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaisinDocs", "presenter.log");

        public TextSurface(string[] lines, double autoSpeed)
        {
            _lines = lines;
            _autoSpeed = autoSpeed;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.WriteAllText(LogPath,
                    $"paced text presenter, {lines.Length} lines, {DateTime.Now:HH:mm:ss}\n");
            }
            catch (IOException) { }
        }

        private void Report(string s)
        {
            Stats?.Invoke(s);
            try { File.AppendAllText(LogPath, s.Replace("\n", " | ") + "\n"); }
            catch (IOException) { }
        }

        public void Wheel(double notches)
        {
            _auto = false;                       // a hand on the wheel takes over
            Interlocked.Add(ref _wheelPending, (int)-notches);
        }

        public void ToggleAuto() => _auto = !_auto;

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            _hwnd = CreateWindowExW(0, "static", null, WS_CHILD | WS_VISIBLE,
                0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _running = true;
            _thread = new Thread(RenderLoop) { IsBackground = true, Name = "text-presenter" };
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
            _text?.Dispose(); _format?.Dispose(); _dwrite?.Dispose();
            _target?.Dispose(); _d2d?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose();
            _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _device = null;
        }

        private void Init()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            // BgraSupport is required before Direct2D will share the device.
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                levels, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            _device = device;
            _context = context;

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();

            var desc = new SwapChainDescription1
            {
                Width = 0,
                Height = 0,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            };

            using var sc1 = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
            _swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            _swapChain.MaximumFrameLatency = 1;

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
            _format = _dwrite.CreateTextFormat("Consolas",
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                FontSize);

            CreateTarget();
            _text = _d2d.CreateSolidColorBrush(new Color4(0.86f, 0.86f, 0.86f, 1f));
        }

        private void CreateTarget()
        {
            _target?.Dispose();
            using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
            var props = new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw);
            _target = _d2d!.CreateBitmapFromDxgiSurface(surface, props);
            _d2d.Target = _target;
        }

        private void RenderLoop()
        {
            try { Init(); }
            catch (Exception ex)
            {
                Stats?.Invoke($"init failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var waitable = _swapChain!.FrameLatencyWaitableObject;
            var clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;

            var gaps = new List<double>(4096);
            var draws = new List<double>(4096);
            int built = 0, linesDrawn = 0, frames = 0;
            double report = 0;

            while (_running)
            {
                if (waitable != IntPtr.Zero) WaitForSingleObject(waitable, 1000);

                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                if (dt > 0 && dt < 0.5) gaps.Add(dt * 1000);
                if (dt > 0.05) dt = 0.05;

                Advance(dt);

                var sw = Stopwatch.StartNew();
                (int b, int n) = Draw();
                sw.Stop();
                draws.Add(sw.Elapsed.TotalMilliseconds);
                built += b;
                linesDrawn += n;
                frames++;

                _swapChain.Present(1, PresentFlags.None);

                report += dt;
                if (report >= 0.5 && gaps.Count > 20)
                {
                    report = 0;
                    Report(Summarise(gaps, draws, built, linesDrawn, frames));
                    gaps.Clear(); draws.Clear();
                    built = 0; linesDrawn = 0; frames = 0;
                }
            }
        }

        /// <summary>Closed-form decay, as ScrollController does it, so a coast matches.</summary>
        private void Advance(double dt)
        {
            int notches = Interlocked.Exchange(ref _wheelPending, 0);
            if (notches != 0) _velocity += notches * PixelsPerNotch * WheelDamping;

            double max = Math.Max(0, _lines.Length * LineHeight - _d2d!.Size.Height);

            if (_auto)
            {
                _offset += _autoSpeed * dt * _autoDirection;
                if (_offset >= max) { _offset = max; _autoDirection = -1; }
                else if (_offset <= 0) { _offset = 0; _autoDirection = 1; }
                return;
            }

            if (Math.Abs(_velocity) > 0.5)
            {
                double decay = Math.Exp(-dt * WheelDamping);
                _offset += _velocity * (1 - decay) / WheelDamping;
                _velocity *= decay;
            }
            else _velocity = 0;

            _offset = Math.Clamp(_offset, 0, max);
        }

        /// <summary>Draws the visible lines. Returns layouts built, and lines drawn.</summary>
        private (int built, int drawn) Draw()
        {
            var size = _d2d!.Size;
            _d2d.BeginDraw();
            _d2d.Clear(new Color4(0.12f, 0.12f, 0.12f, 1f));

            int first = Math.Max(0, (int)(_offset / LineHeight));
            int lastLine = Math.Min(_lines.Length - 1, (int)((_offset + size.Height) / LineHeight));
            int built = 0, drawn = 0;

            for (int i = first; i <= lastLine; i++)
            {
                if (!_layouts.TryGetValue(i, out var layout))
                {
                    layout = _dwrite!.CreateTextLayout(_lines[i], _format!, size.Width - PaddingX * 2, LineHeight);
                    _layouts[i] = layout;
                    built++;
                }

                // Whole pixels, as the WPF path does, so the comparison is like for like.
                float y = (float)Math.Round(i * LineHeight - _offset);
                _d2d.DrawTextLayout(new System.Numerics.Vector2(PaddingX, y), layout, _text!);
                drawn++;
            }

            _d2d.EndDraw();
            TrimLayouts(first, lastLine);
            return (built, drawn);
        }

        /// <summary>Keeps a margin either side, as the line visual cache does.</summary>
        private void TrimLayouts(int first, int last)
        {
            const int window = 400;
            if (_layouts.Count <= window * 2) return;
            var drop = _layouts.Keys.Where(k => k < first - window || k > last + window).ToList();
            foreach (int k in drop) { _layouts[k].Dispose(); _layouts.Remove(k); }
        }

        private static string Summarise(List<double> gaps, List<double> draws,
                                        int built, int linesDrawn, int frames)
        {
            var g = gaps.ToArray(); Array.Sort(g);
            var d = draws.ToArray(); Array.Sort(d);
            double med = g[g.Length / 2];
            int late = 0;
            foreach (var x in gaps) if (x > med * 1.5) late++;

            double perLineUs = linesDrawn > 0 ? draws.Sum() * 1000.0 / linesDrawn : 0;

            return $"present  median {med:F2}ms ({1000 / med:F0}/s)   p99 {g[(int)(g.Length * 0.99)]:F2}ms   " +
                   $"over 1.5x median {100.0 * late / gaps.Count:F1}%\n" +
                   $"draw     median {d[d.Length / 2]:F2}ms   p99 {d[(int)(d.Length * 0.99)]:F2}ms   " +
                   $"max {d[^1]:F2}ms\n" +
                   $"per line {perLineUs:F1}us  (WPF cached visual composite: 8.6us)   " +
                   $"lines/frame {(double)linesDrawn / frames:F0}   " +
                   $"layouts built/frame {(double)built / frames:F2}\n" +
                   $"wheel to scroll";
        }
    }
}
