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

        Window window =
            e.Args.Contains("--scrollproto") ? new ScrollPrototypeWindow() :
            e.Args.Contains("--presenter") ? new PresenterPrototypeWindow() :
            e.Args.Contains("--textpresenter") ? TextPresenterWindow.Open(file, AutoSpeed(e.Args)) :
            e.Args.Contains("--seam") ? SeamComparisonWindow.Open(file) :
            e.Args.Contains("--handoff") ? HandoffWindow.Open(file, e.Args.Contains("--sweep")) :
            new MainWindow(file);

        MainWindow = window;
        window.Show();
    }

    /// <summary>--speed=N sets the text presenter sweep speed in pixels a second.</summary>
    private static double AutoSpeed(string[] args)
    {
        string? arg = args.FirstOrDefault(a => a.StartsWith("--speed="));
        return arg != null && double.TryParse(arg.AsSpan("--speed=".Length), out double v)
            ? v : 1200;
    }
}
