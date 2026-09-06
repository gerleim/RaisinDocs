using System.Linq;
using System.Windows;

namespace RaisinDocs.TestApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // THROWAWAY: --scrollproto opens the pre-buffering prototype instead of the sandbox.
        // See design/Scroll Pre-Buffering.md, phase 1. Remove with the prototype.
        // A path opens that document read-only instead of the scratch pad. Scroll behaviour
        // depends heavily on what is being scrolled - tables, images and colour spans are what
        // make a line dear - so testing against a real document rather than whatever was last
        // left in the scratch pad is worth the one argument.
        string? file = e.Args.FirstOrDefault(a => !a.StartsWith("--"));

        // Not a WPF window at all: a bare Win32 popup owning the swapchain, which is the
        // arrangement with the best chance of independent flip. Runs, then shuts the app down.
        if (e.Args.Contains("--flipfs"))
        {
            int secs = 30;
            var sArg = e.Args.FirstOrDefault(a => a.StartsWith("--seconds="));
            if (sArg != null) int.TryParse(sArg.AsSpan("--seconds=".Length), out secs);
            FullscreenFlipWindow.Run(MonitorArg(e.Args), secs);
            Shutdown();
            return;
        }

        Window window =
            e.Args.Contains("--scrollproto") ? new ScrollPrototypeWindow() :
            e.Args.Contains("--presenter") ? new PresenterPrototypeWindow(MonitorArg(e.Args)) :
            e.Args.Contains("--textpresenter") ? TextPresenterWindow.Open(file, AutoSpeed(e.Args)) :
            e.Args.Contains("--seam") ? SeamComparisonWindow.Open(file) :
            e.Args.Contains("--replay") ? ReplayWindow.Open(file) :
            e.Args.Contains("--handoff")
                ? HandoffWindow.Open(file, e.Args.Contains("--sweep"), e.Args.Contains("--bgtest")) :
            new MainWindow(file);

        if (e.Args.Contains("--magenta")) HandoffWindow.SetDebugMagenta();
        if (e.Args.Contains("--nogesture")) HandoffWindow.NoGesture = true;
        if (e.Args.Contains("--wpfonly")) HandoffWindow.StartWithHandoffOff = true;
        if (e.Args.Contains("--bitblt")) HandoffWindow.UseBitblt = true;
        if (e.Args.Contains("--dump")) ReplayWindow.Dump = true;
        if (e.Args.Contains("--autoscroll")) ReplayWindow.AutoScrollTest = true;

        string? mon = e.Args.FirstOrDefault(a => a.StartsWith("--monitor="));
        if (mon != null && int.TryParse(mon.AsSpan("--monitor=".Length), out int rm))
            ReplayWindow.MonitorIndex = rm;

        string? monitor = e.Args.FirstOrDefault(a => a.StartsWith("--monitor="));
        if (monitor != null && int.TryParse(monitor.AsSpan("--monitor=".Length), out int m))
            HandoffWindow.MonitorIndex = m;

        MainWindow = window;
        window.Show();
    }

    /// <summary>--monitor=NAME, as a display device name fragment such as DISPLAY8.</summary>
    private static string? MonitorArg(string[] args)
        => args.FirstOrDefault(a => a.StartsWith("--monitor="))?["--monitor=".Length..];

    /// <summary>--speed=N sets the text presenter sweep speed in pixels a second.</summary>
    private static double AutoSpeed(string[] args)
    {
        string? arg = args.FirstOrDefault(a => a.StartsWith("--speed="));
        return arg != null && double.TryParse(arg.AsSpan("--speed=".Length), out double v)
            ? v : 1200;
    }
}
