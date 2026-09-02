using System.Windows;

namespace RaisinDocs.TestApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // THROWAWAY: --scrollproto opens the pre-buffering prototype instead of the sandbox.
        // See design/Scroll Pre-Buffering.md, phase 1. Remove with the prototype.
        Window window =
            e.Args.Contains("--scrollproto") ? new ScrollPrototypeWindow() :
            e.Args.Contains("--presenter") ? new PresenterPrototypeWindow() :
            new MainWindow();

        MainWindow = window;
        window.Show();
    }
}
