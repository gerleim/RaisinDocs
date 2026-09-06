using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RaisinDocs.TestApp;

/// <summary>
/// A borderless top-level window covering one whole display, presenting a flip-model swapchain.
/// Throwaway; it exists to answer one question.
/// </summary>
/// <remarks>
/// <b>The question.</b> A windowed swapchain is composited by DWM — PresentMon reports
/// <c>Composed: Flip</c> — and DWM composes on a single clock derived from the primary display. That
/// was measured: a swapchain created on and correctly bound to a 60Hz panel still presented at
/// 280/s, the primary's rate, and reached the same accuracy floor as WPF.
///
/// The escape from that is <b>independent flip</b>, where DWM hands the display plane to the
/// swapchain and stops compositing it. Then presentation is paced by that output's own vblank.
/// Windows grants it to a borderless window covering an entire output with nothing drawn over it —
/// it is how fullscreen-borderless games get their pacing.
///
/// So this is deliberately not a WPF window hosting a child HWND. A child HWND has its parent's
/// content behind it, which is composition, and a negative result would not distinguish "independent
/// flip is unavailable here" from "this arrangement disqualified itself". A bare Win32 popup owning
/// the swapchain is the configuration with the best chance, so a negative from it means something.
///
/// <b>Reading the result.</b> PresentMon's <c>PresentMode</c> column is the answer:
/// <c>Hardware: Independent Flip</c> means DWM let go, and the present gap should then be the
/// panel's refresh period rather than the primary's. <c>Composed: Flip</c> means it did not.
///
/// <b>It closes itself.</b> A borderless window covering a whole monitor with no chrome is a good way
/// to trap whoever is at the machine, so it exits on Escape, on a click, and on a timeout regardless.
/// </remarks>
internal static class FullscreenFlipWindow
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WM_DESTROY = 0x0002, WM_KEYDOWN = 0x0100, WM_LBUTTONDOWN = 0x0201, WM_CLOSE = 0x0010;
    private const int VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public int cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(
        int exStyle, string cls, string? name, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessageW(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(string? name);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public uint dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "presenter-binding.log");

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  [fullscreen] {line}{Environment.NewLine}");
        }
        catch { }
    }

    private static volatile bool _running;
    private static WndProc? _proc;   // kept alive; a collected delegate is a crash on the next message

    /// <summary>Runs the test on a named display for a fixed time, then exits.</summary>
    internal static void Run(string? monitor, int seconds)
    {
        if (!FindDisplay(monitor ?? "DISPLAY", out var name, out int x, out int y, out int w, out int h))
        {
            Log($"no display matching '{monitor}'");
            return;
        }

        _proc = StaticWndProc;
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = "RaisinFlipTest",
        };
        RegisterClassExW(ref cls);

        // The whole output, not the working area: covering the taskbar is part of what qualifies a
        // window for independent flip.
        IntPtr hwnd = CreateWindowExW(0, "RaisinFlipTest", null, WS_POPUP | WS_VISIBLE,
            x, y, w, h, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) { Log("CreateWindowEx failed"); return; }

        Log($"window    : {name} {w}x{h} at {x},{y}");

        _running = true;
        var render = new Thread(() => RenderLoop(hwnd)) { IsBackground = true, Name = "flip-test" };
        render.Start();

        var deadline = Stopwatch.StartNew();
        while (_running && deadline.Elapsed.TotalSeconds < seconds)
        {
            while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            Thread.Sleep(5);
        }

        _running = false;
        render.Join(1000);
        DestroyWindow(hwnd);
        Log("closed");
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_KEYDOWN when wParam.ToInt32() == VK_ESCAPE:
            case WM_LBUTTONDOWN:
            case WM_CLOSE:
                _running = false;
                return IntPtr.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void RenderLoop(IntPtr hwnd)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        IDXGISwapChain2? swapChain = null;
        ID3D11RenderTargetView? rtv = null;

        try
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                levels, out device, out context).CheckError();

            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
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
                // Opaque, explicitly. Transparency disqualifies a swapchain from independent flip.
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            };

            using var sc1 = factory.CreateSwapChainForHwnd(device, hwnd, desc);
            swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            swapChain.MaximumFrameLatency = 1;

            try
            {
                using var output = swapChain.GetContainingOutput();
                Log($"bound to  : {output.Description.DeviceName}");
            }
            catch (Exception ex) { Log($"bound to  : (failed: {ex.GetType().Name})"); }

            using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            rtv = device.CreateRenderTargetView(backBuffer);

            var waitable = swapChain.FrameLatencyWaitableObject;
            var clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;
            var gaps = new System.Collections.Generic.List<double>(8192);
            double offset = 0, report = 0;

            while (_running)
            {
                if (waitable != IntPtr.Zero) WaitForSingleObject(waitable, 1000);

                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                if (dt > 0 && dt < 0.5) gaps.Add(dt * 1000);

                offset += 600 * dt;
                float phase = (float)((offset % 480.0) / 480.0);
                float v = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;

                context.OMSetRenderTargets(rtv);
                context.ClearRenderTargetView(rtv,
                    new Vortice.Mathematics.Color4(0.10f + v * 0.75f, 0.12f, 0.30f - v * 0.18f, 1f));
                swapChain.Present(1, PresentFlags.None);

                report += dt;
                if (report >= 2.0 && gaps.Count > 20)
                {
                    report = 0;
                    gaps.Sort();
                    double med = gaps[gaps.Count / 2];
                    Log($"present gap median {med:F3}ms ({1000 / med:F0}/s)  min {gaps[0]:F3}  max {gaps[^1]:F3}  n={gaps.Count}");
                    gaps.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            Log($"render loop failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            rtv?.Dispose(); swapChain?.Dispose(); context?.Dispose(); device?.Dispose();
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    private static bool FindDisplay(string fragment, out string name, out int x, out int y, out int w, out int h)
    {
        name = string.Empty; x = y = w = h = 0;
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & 0x1) == 0) continue;
            if (!dd.DeviceName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) continue;

            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(dd.DeviceName, -1, ref dm)) continue;

            name = dd.DeviceName;
            x = dm.dmPositionX; y = dm.dmPositionY;
            w = (int)dm.dmPelsWidth; h = (int)dm.dmPelsHeight;
            return true;
        }
        return false;
    }
}
