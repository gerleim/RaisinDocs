using System;
using System.IO;
using System.Timers;
using Timer = System.Timers.Timer;

namespace RaisinDocs;

/// <summary>
/// Monitors a file for external changes and notifies via callback.
/// Uses FileSystemWatcher for immediate detection of normal writes,
/// plus a polling timer to catch atomic replacements (temp file + rename)
/// that FSW cannot detect on Windows.
/// </summary>
public class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action<FileChangeEvent> _onFileChanged;
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private FileChangeEvent? _pendingEvent;
    private bool _disposed;
    private bool _suppressed;
    private DateTime _lastKnownWriteTime;
    private const int DebounceMs = 500;
    private const int PollMs = 1500;

    public string? CurrentFilePath { get; private set; }
    public bool IsWatching { get; private set; }

    public FileChangeWatcher(Action<FileChangeEvent> onFileChanged)
    {
        _onFileChanged = onFileChanged ?? throw new ArgumentNullException(nameof(onFileChanged));
        _watcher = new FileSystemWatcher
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;
    }

    public void WatchFile(string filePath)
    {
        if (_watcher == null)
            throw new ObjectDisposedException(nameof(FileChangeWatcher));

        CurrentFilePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(CurrentFilePath)!;
        var fileName = Path.GetFileName(CurrentFilePath);

        _lastKnownWriteTime = File.GetLastWriteTimeUtc(CurrentFilePath);

        _watcher.Path = directory;
        _watcher.Filter = fileName;
        _watcher.EnableRaisingEvents = true;

        _pollTimer?.Dispose();
        _pollTimer = new Timer(PollMs) { AutoReset = true };
        _pollTimer.Elapsed += OnPollTimerElapsed;
        _pollTimer.Start();

        IsWatching = true;
    }

    public void StopWatching()
    {
        if (_watcher != null)
            _watcher.EnableRaisingEvents = false;
        _pollTimer?.Stop();
        IsWatching = false;
        CurrentFilePath = null;
    }

    public void Suppress()
    {
        lock (_lock) _suppressed = true;
    }

    public void Resume()
    {
        lock (_lock)
        {
            _suppressed = false;
            _pendingEvent = null;
            if (CurrentFilePath != null && File.Exists(CurrentFilePath))
                _lastKnownWriteTime = File.GetLastWriteTimeUtc(CurrentFilePath);
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Runs on the watcher's thread: an escaping exception would tear down the host app.
        try
        {
            if (File.Exists(e.FullPath))
            {
                ScheduleCallback(new FileChangeEvent
                {
                    FilePath = e.FullPath,
                    ChangeType = FileChangeType.Modified,
                });
            }
        }
        catch
        {
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            if (!File.Exists(e.FullPath)) return;

            // A rename whose *target* is the watched file is not a rename at all from the
            // host's point of view — it is an atomic save (write a temp file, delete or
            // replace the original, rename the temp onto its name). The path is unchanged
            // and the content is new, so this has to be reported as a modification.
            // Reporting it as a rename is how an external edit went unnoticed: hosts only
            // retitle on a rename, and the debounce below still advances the polling
            // baseline, so the fallback stayed silent too.
            if (SamePath(e.FullPath, CurrentFilePath))
            {
                ScheduleCallback(new FileChangeEvent
                {
                    FilePath = e.FullPath,
                    ChangeType = FileChangeType.Modified,
                });
                return;
            }

            // The watched file itself was renamed away: follow it. Its content is unchanged.
            if (!SamePath(e.OldFullPath, CurrentFilePath)) return;

            lock (_lock)
            {
                CurrentFilePath = e.FullPath;
                if (_watcher != null)
                    _watcher.Filter = Path.GetFileName(e.FullPath);
            }

            ScheduleCallback(new FileChangeEvent
            {
                FilePath = e.FullPath,
                ChangeType = FileChangeType.Renamed,
                OldPath = e.OldFullPath,
            });
        }
        catch
        {
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The OS dropped the subscription (buffer overflow, directory went away).
        // Re-arm it so change detection does not silently degrade to polling only.
        try
        {
            var path = CurrentFilePath;
            if (_disposed || _watcher == null || path == null || !File.Exists(path)) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Path = Path.GetDirectoryName(path)!;
            _watcher.Filter = Path.GetFileName(path);
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
        }
    }

    private static bool SamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private void OnPollTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Runs on a timer thread: in .NET an exception escaping here kills the process.
        try
        {
            lock (_lock)
            {
                if (_disposed || _suppressed) return;
            }

            var path = CurrentFilePath;
            if (path == null || !File.Exists(path)) return;

            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != _lastKnownWriteTime)
            {
                ScheduleCallback(new FileChangeEvent
                {
                    FilePath = path,
                    ChangeType = FileChangeType.Modified,
                });
            }
        }
        catch
        {
        }
    }

    private void ScheduleCallback(FileChangeEvent changeEvent)
    {
        lock (_lock)
        {
            if (_disposed || _suppressed) return;

            _pendingEvent = changeEvent;

            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(DebounceMs)
                {
                    AutoReset = false,
                };
                _debounceTimer.Elapsed += OnDebounceTimerElapsed;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        FileChangeEvent? pending;
        DateTime previousWriteTime;
        lock (_lock)
        {
            if (_disposed || _suppressed) return;
            pending = _pendingEvent;
            _pendingEvent = null;
            previousWriteTime = _lastKnownWriteTime;

            if (pending != null && CurrentFilePath != null && File.Exists(CurrentFilePath))
                _lastKnownWriteTime = File.GetLastWriteTimeUtc(CurrentFilePath);
        }

        if (pending == null) return;

        // Runs on a timer thread: in .NET an exception escaping here kills the process.
        // A callback that failed also means the host never saw this change, so put the
        // baseline back and let the poll timer offer it again.
        try
        {
            _onFileChanged(pending);
        }
        catch
        {
            lock (_lock) _lastKnownWriteTime = previousWriteTime;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        if (_watcher != null)
        {
            _watcher.Changed -= OnFileSystemChanged;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
        }

        _pollTimer?.Dispose();
        _debounceTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class FileChangeEvent
{
    public string FilePath { get; set; } = "";
    public string? OldPath { get; set; }
    public FileChangeType ChangeType { get; set; }
}

public enum FileChangeType
{
    Modified,
    Renamed,
}
