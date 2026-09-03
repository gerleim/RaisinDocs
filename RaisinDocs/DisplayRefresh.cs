using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RaisinDocs;

/// <summary>
/// Reports the refresh rate of the display a window is on.
/// </summary>
/// <remarks>
/// This once chose how often the scroll animation repainted. It no longer does: painting once
/// per composed frame takes its cadence from the compositor's own frame stamp, which already
/// carries the display's rate, and needs no rate of its own. What survives is the ability to
/// name the panel - so a gesture measured on one monitor can be told apart from a gesture
/// measured on another, which the diagnostic log records rather than infers.
/// </remarks>
internal static class DisplayRefresh
{
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
    internal static int GetMonitorRefreshRate(Visual? visual) => GetDisplay(visual).Hz;

    /// <summary>
    /// The device name and refresh rate of the display <paramref name="visual"/> is on.
    /// </summary>
    /// <remarks>
    /// The name matters as much as the rate. A rate alone cannot show that a window has moved
    /// to another panel, because the interesting failure - WPF continuing to pace to the
    /// display it started on - produces the old rate on the new monitor, which is
    /// indistinguishable from not having moved. The device name comes from the window handle,
    /// so it says where the window is however WPF is pacing it.
    ///
    /// An empty name means the lookup fell back to the current display device, and the rate
    /// that comes with it should not be trusted to describe this window.
    /// </remarks>
    internal static (string Device, int Hz) GetDisplay(Visual? visual)
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
            return (device ?? string.Empty, dm.dmDisplayFrequency);
        return (device ?? string.Empty, 60);
    }

}
