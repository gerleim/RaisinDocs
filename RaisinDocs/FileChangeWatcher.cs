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
    private Timer? _debounceTimer;
    private FileChangeEvent? _pendingEvent;
    private const int DebounceMs = 500;

    public string? CurrentFilePath { get; private set; }
    public bool IsWatching { get; private set; }

    public FileChangeWatcher(Action<FileChangeEvent> onFileChanged)
    {
        _onFileChanged = onFileChanged ?? throw new ArgumentNullException(nameof(onFileChanged));
        _watcher = new FileSystemWatcher
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnFileSystemChanged;
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

    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_pendingEvent != null)
        {
            _onFileChanged(_pendingEvent);
            _pendingEvent = null;
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
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
