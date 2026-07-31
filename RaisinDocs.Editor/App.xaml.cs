using System.Diagnostics;
using System.Windows;
using RaisinDocs;
using System.IO;

namespace RaisinDocs.Editor;

public partial class App : Application
{
    public static readonly IDocsLogger Logger = new FileLogger();
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
