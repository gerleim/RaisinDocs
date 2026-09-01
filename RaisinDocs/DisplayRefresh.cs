using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RaisinDocs;

/// <summary>
/// Resolves how often the scroll animation should repaint.
/// </summary>
/// <remarks>
/// We do not synchronise to vsync, and do not need to: WPF presents through DWM, which
/// composites and presents at the refresh rate itself, so no rasterisation rate we pick can
/// tear. Vsync matters here only as a ceiling. Frames rasterised faster than the compositor
/// presents are discarded, and that waste is not free — it is taken from the UI thread, which
/// runs the message pump. Overshooting it is what made Windows coalesce queued WM_MOUSEWHEEL
/// messages into single multi-notch deltas and turned smooth scrolling into bursts.
///
/// <see cref="CompositionTarget.Rendering"/> cannot be used as the clock instead: repainting
/// from inside its handler makes WPF schedule another pass and raise it again, so it free-runs
/// well past the display rate rather than tracking it.
///
/// Scrolling is continuous motion of high-contrast text, which is exactly where the eye
/// resolves frame rate, so it takes the full ceiling rather than the 60 that suits a panel of
/// static text.
/// </remarks>
internal static class DisplayRefresh
{
    /// <summary>
    /// Ceiling for the repaint target, in frames per second.
    /// </summary>
    /// <remarks>
    /// WPF's compositor is not capped at 60Hz on .NET 8 / Windows 11 — it will present at the
    /// panel's rate when the UI thread is free. It only reaches that when the thread is free,
    /// though, so past some point rasterising faster costs frames rather than gaining them.
    ///
    /// Under trial at 144. Measured at 120 the scroll held 119-122 paints/sec while moving,
    /// with 7-13% of gaps over 25ms; if that share grows here without the rate following,
    /// the ceiling is doing its job and should come back down.
    /// </remarks>
    internal const int MaxFps = 144;

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion;
        public short dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight;
        public int dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public int rcMonitorLeft, rcMonitorTop, rcMonitorRight, rcMonitorBottom;
        public int rcWorkLeft, rcWorkTop, rcWorkRight, rcWorkBottom;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    /// <summary>
    /// Refresh rate in Hz of the display <paramref name="visual"/> is on, or of the primary
    /// display when that cannot be resolved, or 60 when neither can be read.
    /// </summary>
    /// <remarks>
    /// Which display matters: a machine can easily have panels at 60, 100, 144 and 280Hz at
    /// once, so the primary display's rate says nothing about the one the window is on.
    /// Capping to a faster panel than the window occupies reintroduces exactly the overshoot
    /// this throttle exists to avoid.
    /// </remarks>
    internal static int GetMonitorRefreshRate(Visual? visual)
    {
        string? device = null;

        if (visual != null && PresentationSource.FromVisual(visual) is HwndSource source
            && source.Handle != IntPtr.Zero)
        {
            IntPtr monitor = MonitorFromWindow(source.Handle, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (GetMonitorInfo(monitor, ref info))
                    device = info.szDevice;
            }
        }

        // A null device name asks for the current display device, which is the right fallback
        // when the visual is not yet attached to a window.
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
        if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 0)
            return dm.dmDisplayFrequency;
        return 60;
    }

    /// <summary>
    /// Seconds between repaints for the display <paramref name="visual"/> is on, capped at
    /// <see cref="MaxFps"/>. Cheap enough to call once per gesture, which also picks up the
    /// window being dragged to another monitor, or a mode change.
    /// </summary>
    internal static double GetRepaintInterval(Visual? visual)
        => 1.0 / Math.Max(1, Math.Min(GetMonitorRefreshRate(visual), MaxFps));
}
