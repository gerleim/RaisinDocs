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
    /// Turns on F9 (cached visuals against direct draw) and F10 (opaque line visuals), and
    /// the badge that says which is active. See design/Opaque Line Visuals.md.
    /// </summary>
    internal const string RenderDiagSwitch = "--render-diag";

    protected override void OnStartup(StartupEventArgs e)
    {
        // Here rather than in a window's Loaded: the canvas wires its scroll counters up in
        // its constructor, which runs during InitializeComponent, before Loaded fires.
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, ScrollDiagSwitch, StringComparison.OrdinalIgnoreCase))
                DocsCanvas.ScrollDiagnostics = true;
            else if (string.Equals(arg, RenderDiagSwitch, StringComparison.OrdinalIgnoreCase))
                DocsCanvas.EnableRenderPathToggle = true;
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
