using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RaisinDocs.TestApp;

/// <summary>
/// THROWAWAY prototype for option C of design/Scroll Frame Pacing.md. Delete once it has
/// answered its question.
/// </summary>
/// <remarks>
/// It answers one thing: can a flip-model swapchain paced by a latency waitable object hold an
/// even cadence, where WPF cannot?
///
/// Everything measured in the editor says the residual stutter is presentation scheduling.
/// Frames arrive on exact multiples of the refresh period - 7.00ms is two refreshes of a 280Hz
/// panel, 17.8ms is five - with a normal OnRender cost, no garbage collection, and no
/// correlation with anything we control. FrameView showed the app presenting 228 times a
/// second into a sink that changed 140 times a second, with 13% dropped: unpaced presentation.
///
/// So this presents nothing but a scrolling gradient, as simply as possible, with the pacing
/// WPF does not expose:
///   - a flip-model swapchain on a child HWND, so WPF's compositor is not in the way
///   - DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT, waiting before rendering rather
///     than blocking inside Present
///   - MaximumFrameLatency of 1, so only one frame is ever in flight
///
/// Measure it with FrameView and read MsBetweenDisplayChange. If it holds one refresh period
/// with no jumps to multiples, the ceiling is liftable and the rest of option C is worth its
/// cost. If it jumps the way WPF does, this is the machine's floor and option C dies here -
/// for a few hours rather than a few weeks.
/// </remarks>
public sealed class PresenterPrototypeWindow : Window
{
    private readonly TextBlock _readout = new()
    {
        Foreground = Brushes.Gainsboro,
        Margin = new Thickness(8, 6, 8, 6),
        FontFamily = new FontFamily("Consolas"),
    };

    private readonly SwapChainHost _host = new();

    public PresenterPrototypeWindow()
    {
        Title = "Paced presenter prototype (throwaway)";
        Width = 1200;
        Height = 800;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_readout, 0);
        Grid.SetRow(_host, 1);
        grid.Children.Add(_readout);
        grid.Children.Add(_host);
        Content = grid;

        MouseWheel += (_, e) => _host.Nudge(-e.Delta * 2.0);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Space) _host.ToggleAutoScroll();
        };

        _host.Stats += s => Dispatcher.BeginInvoke(() => _readout.Text = s);
        Closed += (_, _) => _host.Stop();
    }

    /// <summary>Hosts a child HWND that we present to ourselves, outside WPF's compositor.</summary>
    private sealed class SwapChainHost : HwndHost
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        private IntPtr _hwnd;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain2? _swapChain;
        private ID3D11RenderTargetView? _rtv;
        private Thread? _thread;
        private volatile bool _running;
        private volatile bool _auto = true;
        private double _offset;
        private double _nudge;

        public event Action<string>? Stats;

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            // "static" is a predefined class, so no window class needs registering.
            _hwnd = CreateWindowExW(0, "static", null, WS_CHILD | WS_VISIBLE,
                0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            StartRenderThread();
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Stop();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }

        public void Nudge(double px) => _nudge += px;
        public void ToggleAutoScroll() => _auto = !_auto;

        public void Stop()
        {
            _running = false;
            _thread?.Join(500);
            _rtv?.Dispose(); _swapChain?.Dispose();
            _context?.Dispose(); _device?.Dispose();
            _rtv = null; _swapChain = null; _context = null; _device = null;
        }

        private void StartRenderThread()
        {
            _running = true;
            _thread = new Thread(RenderLoop) { IsBackground = true, Name = "presenter-prototype" };
            _thread.Start();
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long last = clock.ElapsedTicks;
            var gaps = new List<double>(4096);
            double report = 0;

            while (_running)
            {
                // Block until the system is ready for another frame. This is the whole point:
                // waiting here rather than inside Present is what paces to the compositor.
                if (waitable != IntPtr.Zero)
                    WaitForSingleObject(waitable, 1000);

                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)System.Diagnostics.Stopwatch.Frequency;
                last = now;
                if (dt > 0 && dt < 0.5) gaps.Add(dt * 1000);

                if (_auto) _offset += 600 * dt;
                _offset += _nudge; _nudge = 0;

                Draw();
                _swapChain.Present(1, PresentFlags.None);

                report += dt;
                if (report >= 0.5 && gaps.Count > 20)
                {
                    report = 0;
                    Stats?.Invoke(Summarise(gaps));
                    gaps.Clear();
                }
            }
        }

        private static string Summarise(List<double> gaps)
        {
            var s = gaps.ToArray();
            Array.Sort(s);
            double med = s[s.Length / 2];
            double p99 = s[(int)(s.Length * 0.99)];
            // Anything beyond 1.5x the median is a skipped presentation window.
            int late = 0;
            foreach (var g in gaps) if (g > med * 1.5) late++;
            return $"present gap  median {med:F2}ms ({1000 / med:F0}/s)   " +
                   $"p99 {p99:F2}ms   max {s[^1]:F2}ms   " +
                   $"over 1.5x median {100.0 * late / gaps.Count:F1}%   n={gaps.Count}" +
                   "\nspace toggles auto-scroll, wheel nudges";
        }

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        private void Init()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
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
                // Flip model plus the waitable flag: the two together are what allow pacing.
                SwapEffect = SwapEffect.FlipSequential,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            };

            using var sc1 = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
            _swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            // One frame in flight, so we render against the freshest possible state.
            _swapChain.MaximumFrameLatency = 1;

            CreateTarget();
        }

        private void CreateTarget()
        {
            _rtv?.Dispose();
            using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
            _rtv = _device!.CreateRenderTargetView(backBuffer);
        }

        private void Draw()
        {
            _context!.OMSetRenderTargets(_rtv!);

            // No geometry, deliberately: what is under test is when frames reach the screen,
            // not what is in them. The whole target is cleared to a value that sweeps with the
            // offset, so uneven presentation shows as an uneven sweep, and FrameView's
            // MsBetweenDisplayChange measures it regardless.
            float phase = (float)((_offset % 480.0) / 480.0);
            float v = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
            _context.ClearRenderTargetView(_rtv!,
                new Vortice.Mathematics.Color4(0.10f + v * 0.75f, 0.12f, 0.30f - v * 0.18f, 1f));
        }
    }
}
