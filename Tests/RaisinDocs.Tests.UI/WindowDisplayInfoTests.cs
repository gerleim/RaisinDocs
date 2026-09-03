using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FluentAssertions;
using Raisin.WPF.Base;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Covers <see cref="WindowDisplayInfo"/> following a window between displays.
/// </summary>
/// <remarks>
/// This is what needs proving about the display-pacing work. The consequence - that a capped
/// scroll paints at the panel's rate rather than the compositor's - was measured directly:
/// 279 paints a second on a 60Hz panel before, 57 after. What that rests on is the window
/// being asked which display it is on and being told again when it moves, which is this.
///
/// Driving it through real wheel input is not an option: WPF routes WM_MOUSEWHEEL to the
/// element under the physical cursor and ignores posted coordinates, so a synthetic gesture
/// only lands when the window happens to sit under the real mouse. These tests move a window
/// instead, which needs no input at all.
///
/// They depend on the machine's monitor layout and skip themselves when it cannot support the
/// case - a single-display machine, or no two displays with different rates.
/// </remarks>
public class WindowDisplayInfoTests
{
    [StaFact]
    public void ReportsTheDisplayItIsPlacedOn()
    {
        var displays = Monitors();
        if (displays.Count < 2)
            return;   // one display: nothing to distinguish

        RunWithWindow(window =>
        {
            var info = WindowDisplayInfo.For(window);
            info.Should().NotBeNull("a shown window has an HwndSource");

            foreach (var display in displays)
            {
                PlaceInside(window, display);

                info!.Devices.Should().Contain(display.Device,
                    "the window was placed entirely inside {0}", display.Device);
                info.RefreshRate.Should().Be(display.Hz,
                    "{0} runs at {1}Hz", display.Device, display.Hz);
            }
        });
    }

    [StaFact]
    public void FollowsTheWindowToAnotherDisplay()
    {
        var displays = Monitors();
        var slow = displays.OrderBy(d => d.Hz).FirstOrDefault();
        var fast = displays.OrderByDescending(d => d.Hz).FirstOrDefault();
        if (slow is null || fast is null || slow.Hz == fast.Hz)
            return;   // needs two displays at different rates

        RunWithWindow(window =>
        {
            var info = WindowDisplayInfo.For(window)!;

            PlaceInside(window, slow);
            info.RefreshRate.Should().Be(slow.Hz);

            int seen = 0;
            info.Changed += _ => seen++;

            PlaceInside(window, fast);

            info.RefreshRate.Should().Be(fast.Hz, "the window moved to {0}", fast.Device);
            seen.Should().BeGreaterThan(0, "moving to another display raises Changed");
        });
    }

    [StaFact]
    public void SpanningTwoDisplaysTakesTheFasterRate()
    {
        var displays = Monitors();

        // Two displays sharing a vertical edge AND some rows, so a window can actually straddle
        // the seam. Sharing only an edge is not enough: two monitors can meet at a corner - here
        // one spanning y -1080..0 and another 0..1440 - where no window rect touches both.
        var pair = (from a in displays
                    from b in displays
                    where a.Right == b.Left
                          && a.Hz != b.Hz
                          && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) >= 300
                    select (a, b)).FirstOrDefault();

        if (pair.a is null)
            return;   // no adjacent pair at different rates with overlapping rows

        RunWithWindow(window =>
        {
            var info = WindowDisplayInfo.For(window)!;

            // Straddling the seam: half on each, at a height both displays cover.
            const int width = 400;
            int top = Math.Max(pair.a.Top, pair.b.Top) + 40;
            Move(window, pair.a.Right - width / 2, top, width, 200);
            Pump();

            int faster = Math.Max(pair.a.Hz, pair.b.Hz);
            info.RefreshRate.Should().Be(faster,
                "a window across {0} at {1}Hz and {2} at {3}Hz is presented on both, and pacing " +
                "to the slower one degrades the half on the faster",
                pair.a.Device, pair.a.Hz, pair.b.Device, pair.b.Hz);
        });
    }

    // --- harness ---------------------------------------------------------------------------

    /// <summary>
    /// Shows a small, unfocused, taskbar-less window for the duration of the test, so running
    /// the suite disturbs whoever is at the machine as little as possible.
    /// </summary>
    private static void RunWithWindow(Action<Window> body)
    {
        var window = new Window
        {
            Width = 400,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "WindowDisplayInfo test",
        };

        try
        {
            window.Show();
            Pump();
            body(window);
        }
        finally
        {
            window.Close();
            Pump();
        }
    }

    private static void PlaceInside(Window window, Monitor display)
    {
        Move(window, display.Left + 60, display.Top + 60, 300, 200);
        Pump();
    }

    private static void Move(Window window, int x, int y, int cx, int cy)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, IntPtr.Zero, x, y, cx, cy, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Lets the window message queue drain, so WM_WINDOWPOSCHANGED reaches the hook before the
    /// assertion reads the result.
    /// </summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class Monitor
    {
        public string Device = string.Empty;
        public int Left, Top, Right, Bottom, Hz;
    }

    private static List<Monitor> Monitors()
    {
        var found = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref RECT bounds, IntPtr _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    EnumDisplaySettings(info.szDevice, -1, ref mode);
                    found.Add(new Monitor
                    {
                        Device = info.szDevice,
                        Left = info.rcMonitor.Left,
                        Top = info.rcMonitor.Top,
                        Right = info.rcMonitor.Right,
                        Bottom = info.rcMonitor.Bottom,
                        Hz = mode.dmDisplayFrequency,
                    });
                }
                return true;
            }, IntPtr.Zero);
        return found;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}
