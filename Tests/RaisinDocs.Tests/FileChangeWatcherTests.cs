using System.Collections.Concurrent;
using System.IO;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>
/// These exercise a real FileSystemWatcher against real files, so they sleep to let the
/// OS notification, the 500 ms debounce and the 1.5 s poll fallback run.
/// </summary>
public class FileChangeWatcherTests : IDisposable
{
    private const int SettleMs = 3000;

    private readonly string _dir;

    public FileChangeWatcherTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "raisindocs-fcw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string NewFile(string name = "doc.md", string content = "original\n")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static FileChangeWatcher Watch(string path, ConcurrentQueue<FileChangeEvent> events)
    {
        var watcher = new FileChangeWatcher(events.Enqueue);
        watcher.WatchFile(path);
        Thread.Sleep(200);
        return watcher;
    }

    [Fact]
    public void InPlaceWrite_ReportsModified()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        File.WriteAllText(path, "edited\n");
        Thread.Sleep(SettleMs);

        events.Should().ContainSingle()
            .Which.ChangeType.Should().Be(FileChangeType.Modified);
    }

    [Fact]
    public void AtomicReplace_ReportsModified()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        var tmp = Path.Combine(_dir, "save.tmp");
        File.WriteAllText(tmp, "replaced\n");
        File.Move(tmp, path, overwrite: true);
        Thread.Sleep(SettleMs);

        events.Should().ContainSingle()
            .Which.ChangeType.Should().Be(FileChangeType.Modified);
    }

    [Fact]
    public void DeleteThenRenameOntoTarget_ReportsModified_NotRenamed()
    {
        // The save pattern that left the viewer showing stale content: the editor writes a
        // temp file, deletes the original and renames the temp onto its name. That surfaces
        // as a rename *into* the watched path — the path did not change, the content did.
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        var tmp = Path.Combine(_dir, "save.tmp");
        File.WriteAllText(tmp, "replaced\n");
        File.Delete(path);
        File.Move(tmp, path);
        Thread.Sleep(SettleMs);

        events.Should().ContainSingle()
            .Which.ChangeType.Should().Be(FileChangeType.Modified);
        watcher.CurrentFilePath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void DeleteThenRename_DoesNotSilenceLaterChanges()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        var tmp = Path.Combine(_dir, "save.tmp");
        File.WriteAllText(tmp, "replaced\n");
        File.Delete(path);
        File.Move(tmp, path);
        Thread.Sleep(SettleMs);
        events.Clear();

        File.WriteAllText(path, "edited again\n");
        Thread.Sleep(SettleMs);

        events.Should().ContainSingle()
            .Which.ChangeType.Should().Be(FileChangeType.Modified);
    }

    [Fact]
    public void FileRenamedAway_ReportsRenamed_AndFollowsTheNewPath()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        var renamed = Path.Combine(_dir, "renamed.md");
        File.Move(path, renamed);
        Thread.Sleep(SettleMs);

        var change = events.Should().ContainSingle().Subject;
        change.ChangeType.Should().Be(FileChangeType.Renamed);
        change.FilePath.Should().Be(renamed);
        change.OldPath.Should().Be(path);
        watcher.CurrentFilePath.Should().Be(renamed);
    }

    [Fact]
    public void FileRenamedAway_ThenEdited_ReportsModifiedOnTheNewPath()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        var renamed = Path.Combine(_dir, "renamed.md");
        File.Move(path, renamed);
        Thread.Sleep(SettleMs);
        events.Clear();

        File.WriteAllText(renamed, "edited after rename\n");
        Thread.Sleep(SettleMs);

        var change = events.Should().ContainSingle().Subject;
        change.ChangeType.Should().Be(FileChangeType.Modified);
        change.FilePath.Should().Be(renamed);
    }

    [Fact]
    public void DeleteThenRecreate_ReportsModified()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        File.Delete(path);
        Thread.Sleep(300);
        File.WriteAllText(path, "recreated\n");
        Thread.Sleep(SettleMs);

        events.Should().NotBeEmpty();
        events.Should().OnlyContain(c => c.ChangeType == FileChangeType.Modified);
    }

    [Fact]
    public void CallbackThatThrows_DoesNotLoseTheChange()
    {
        // A host whose reload fails (the file was still locked, say) must be offered the
        // change again by the poll fallback rather than left showing stale content.
        var path = NewFile();
        int calls = 0;
        using var watcher = new FileChangeWatcher(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new IOException("host reload failed");
        });
        watcher.WatchFile(path);
        Thread.Sleep(200);

        File.WriteAllText(path, "edited\n");
        Thread.Sleep(SettleMs + 2000);

        calls.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Suppress_StopsCallbacks_AndResumeRebaselines()
    {
        var path = NewFile();
        var events = new ConcurrentQueue<FileChangeEvent>();
        using var watcher = Watch(path, events);

        watcher.Suppress();
        File.WriteAllText(path, "written by the host itself\n");
        Thread.Sleep(SettleMs);
        events.Should().BeEmpty();

        watcher.Resume();
        Thread.Sleep(SettleMs);
        events.Should().BeEmpty("Resume re-baselines to the host's own write");

        File.WriteAllText(path, "external edit\n");
        Thread.Sleep(SettleMs);
        events.Should().ContainSingle()
            .Which.ChangeType.Should().Be(FileChangeType.Modified);
    }
}
