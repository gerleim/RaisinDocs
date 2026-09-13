using System.Diagnostics;
using System.Windows;
using RaisinDocs;
using System.IO;

namespace RaisinDocs.Editor;

public partial class App : Application
{
    public static readonly IDocsLogger Logger = new FileLogger();

    /// <summary>Switches the editor accepts ahead of the file to open.</summary>
    internal const string ScrollDiagSwitch = "--scroll-diag";

    /// <summary>
    /// Logs what each stage of a layout pass costs, which is what a keystroke pays for.
    /// See %LOCALAPPDATA%\RaisinDocs\layout.log.
    /// </summary>
    internal const string LayoutDiagSwitch = "--layout-diag";

    /// <summary>Open in this edit mode for this run, whatever the saved settings say.</summary>
    /// <remarks>
    /// For the scroll capture, which has to know which renderer it is measuring: a restored
    /// setting is whatever mode was last used. Not saved back - see MainWindow.SaveSession.
    /// </remarks>
    internal const string VisualSwitch = "--visual";
    internal const string SourceSwitch = "--source";

    internal static DocsCanvas.EditMode? EditModeOverride { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Here rather than in a window's Loaded: the canvas wires its scroll counters up in
        // its constructor, which runs during InitializeComponent, before Loaded fires.
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, ScrollDiagSwitch, StringComparison.OrdinalIgnoreCase))
                DocsCanvas.ScrollDiagnostics = true;
            else if (string.Equals(arg, LayoutDiagSwitch, StringComparison.OrdinalIgnoreCase))
                DocsCanvas.LayoutDiagnostics = true;
            else if (string.Equals(arg, VisualSwitch, StringComparison.OrdinalIgnoreCase))
                EditModeOverride = DocsCanvas.EditMode.Visual;
            else if (string.Equals(arg, SourceSwitch, StringComparison.OrdinalIgnoreCase))
                EditModeOverride = DocsCanvas.EditMode.Source;
        }

        base.OnStartup(e);
    }
}

internal class FileLogger : IDocsLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public bool IsDebugEnabled => true;

    public FileLogger()
    {
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaisinDocs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, $"RaisinDocs-{DateTime.Now:yyyy-MM-dd}.log");
    }

    public void Log(DocsLogLevel level, string message)
    {
        lock (_lock)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                File.AppendAllText(_logPath, line + Environment.NewLine);
                Debug.WriteLine(line);
            }
            catch { }
        }
    }
}
