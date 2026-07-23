using System;
using System.IO;
using System.Timers;
using Timer = System.Timers.Timer;

namespace RaisinDocs;

/// <summary>
/// Monitors a file for external changes and notifies via callback.
/// Handles debouncing of rapid file system events.
/// </summary>
public class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action<FileChangeEvent> _onFileChanged;
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private FileChangeEvent? _pendingEvent;
    private bool _disposed;
    private bool _suppressed;
    private const int DebounceMs = 500;

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
    }

    public void WatchFile(string filePath)
    {
        if (_watcher == null)
            throw new ObjectDisposedException(nameof(FileChangeWatcher));

        CurrentFilePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(CurrentFilePath)!;
        var fileName = Path.GetFileName(CurrentFilePath);

        _watcher.Path = directory;
        _watcher.Filter = fileName;
        _watcher.EnableRaisingEvents = true;
        IsWatching = true;
    }

    public void StopWatching()
    {
        if (_watcher != null)
            _watcher.EnableRaisingEvents = false;
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
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
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

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (File.Exists(e.FullPath))
        {
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
        lock (_lock)
        {
            if (_disposed || _suppressed) return;
            pending = _pendingEvent;
            _pendingEvent = null;
        }

        if (pending != null)
            _onFileChanged(pending);
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
            _watcher.Dispose();
        }

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
