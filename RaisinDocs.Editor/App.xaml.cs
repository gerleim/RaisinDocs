using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RaisinDocs;
using System.IO;

namespace RaisinDocs.Editor;

public partial class App : Application
{
    public static readonly IDocsLogger Logger = new FileLogger();

    /// <summary>Where unsaved documents go when the editor crashes, until the next start offers them back.</summary>
    internal static readonly string RecoveryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaisinDocs", "recovery");

    private static int _crashing;

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

    /// <summary>
    /// The other settings a scroll capture has to pin, for the same reason as the edit mode.
    /// </summary>
    /// <remarks>
    /// Zoom decides how large the text is and so how many lines a wrapped table row takes; the
    /// table of contents and the minimap sit beside the canvas and take width from it. Restored
    /// from the saved settings, any of them makes a capture measure a different document from the
    /// last one - which is how a zoom level left over from testing got into a before-and-after
    /// comparison. Written <c>--zoom=1.0</c>, <c>--toc=off</c>, <c>--minimap=on</c>.
    /// </remarks>
    internal const string ZoomSwitch = "--zoom=";
    internal const string TocSwitch = "--toc=";
    internal const string MinimapSwitch = "--minimap=";

    internal static double? ZoomOverride { get; private set; }
    internal static bool? TocOverride { get; private set; }
    internal static bool? MinimapOverride { get; private set; }

    private static bool? OnOff(string arg, string prefix) =>
        arg.Substring(prefix.Length).ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        };

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
            else if (arg.StartsWith(ZoomSwitch, StringComparison.OrdinalIgnoreCase)
                     && double.TryParse(arg.AsSpan(ZoomSwitch.Length), System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out double zoom))
                ZoomOverride = zoom;
            else if (arg.StartsWith(TocSwitch, StringComparison.OrdinalIgnoreCase))
                TocOverride = OnOff(arg, TocSwitch);
            else if (arg.StartsWith(MinimapSwitch, StringComparison.OrdinalIgnoreCase))
                MinimapOverride = OnOff(arg, MinimapSwitch);
        }

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Crash(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Crash(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Log(DocsLogLevel.Error, $"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    /// <summary>
    /// An unexpected exception: log it, keep what is unsaved, say so, and exit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There was no handler, so WPF ended the editor and every unsaved document went with it.</b>
    /// Continuing is ruled out on purpose: after an exception no one planned for, the editor may hold
    /// a damaged document, and a save from there would write it over a good file. So it does not try.
    /// </para>
    /// <para>
    /// The rescue runs on the UI thread, which owns the editors. From the dispatcher it is already
    /// there; from another thread it is asked over with a time limit, since that thread may be the one
    /// in trouble — and the process ends when this returns whatever happens, so the rescue is the whole
    /// of what can be done. Everything here is guarded: a handler that throws saves nothing. A second
    /// exception during the first is ignored.
    /// </para>
    /// </remarks>
    private static void Crash(Exception? exception)
    {
        if (Interlocked.Exchange(ref _crashing, 1) == 1)
            return;

        Logger.Log(DocsLogLevel.Error, $"Unexpected error, closing: {exception}");

        int unsaved = 0, kept = 0;
        try
        {
            var dispatcher = Current?.Dispatcher;
            if (dispatcher is not null)
            {
                (int Unsaved, int Kept) Rescue()
                {
                    if (Current.MainWindow is not MainWindow window)
                        return (0, 0);
                    var documents = window.UnsavedDocuments();
                    return (documents.Count, new DocumentRecovery(RecoveryDirectory).Rescue(documents));
                }

                (unsaved, kept) = dispatcher.CheckAccess()
                    ? Rescue()
                    : dispatcher.Invoke(Rescue, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            Logger.Log(DocsLogLevel.Error, $"Could not rescue unsaved documents: {ex.Message}");
        }

        Logger.Log(DocsLogLevel.Error, $"Rescued {kept} of {unsaved} unsaved documents to {RecoveryDirectory}.");

        var outcome = unsaved == 0
            ? "No document had unsaved changes."
            : kept == unsaved
                ? $"Your {unsaved} unsaved document{(unsaved == 1 ? " was" : "s were")} kept, and will be offered back when RaisinDocs next starts."
                : $"{kept} of {unsaved} unsaved documents were kept, and will be offered back when RaisinDocs next starts.";
        try
        {
            MessageBox.Show($"RaisinDocs hit an unexpected error and has to close.\n\n{outcome}\n\n{exception?.Message}",
                "RaisinDocs Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
        }

        Environment.Exit(1);
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
