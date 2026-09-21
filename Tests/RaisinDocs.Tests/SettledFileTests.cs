using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>
/// An external change is read once the program making it has finished, never half-way through.
/// </summary>
/// <remarks>
/// The first event of a save often arrives while the writer still holds the file. The editor's reload
/// read once and gave up, so the tab kept stale text unless a second event happened to follow — and
/// a later save wrote that stale text over the other program's change.
/// </remarks>
public class SettledFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rd-settled-file-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SettledFileTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "doc.md");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_file_still_being_written_is_read_once_the_writer_finishes()
    {
        File.WriteAllText(_path, "the old version");
        var writer = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes("the first half, ");
        writer.Write(bytes);
        writer.Flush();

        var read = Task.Run(() => SettledFile.Read(_path, attempts: 30, settleMs: 50), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        bytes = System.Text.Encoding.UTF8.GetBytes("and the second");
        writer.Write(bytes);
        writer.Dispose();

        var settled = await read;

        settled.Should().NotBeNull("the writer finished well inside the attempts allowed");
        settled!.Content.Should().Be("the first half, and the second");
    }

    [Fact]
    public async Task A_file_that_changes_just_after_it_is_read_is_read_again()
    {
        // A writer that opens and closes the file more than once lets a read through between its
        // writes. Only a read the file still matches a moment later is kept.
        File.WriteAllText(_path, "the version before");

        var read = Task.Run(() => SettledFile.Read(_path, attempts: 10, settleMs: 400), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        File.WriteAllText(_path, "the version after, which is longer");

        var settled = await read;

        settled!.Content.Should().Be("the version after, which is longer");
    }

    [Fact]
    public void A_file_that_never_settles_gives_nothing_rather_than_part_of_it()
    {
        File.WriteAllText(_path, "held");
        using var writer = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);

        SettledFile.Read(_path, attempts: 3, settleMs: 20).Should().BeNull();
    }

    [Fact]
    public void A_file_that_is_gone_gives_nothing()
    {
        SettledFile.Read(_path, attempts: 3, settleMs: 20).Should().BeNull();
    }

    [Fact]
    public void The_stamp_names_the_version_that_was_read()
    {
        // The editor keeps this stamp as the version its text is based on, and a save compares the
        // file against it before replacing it.
        File.WriteAllText(_path, "a version");

        var settled = SettledFile.Read(_path, attempts: 3, settleMs: 20);

        settled!.Stamp.Should().Be(DiskStamp.Of(_path));
    }

    [Fact]
    public void Another_programs_write_changes_the_stamp()
    {
        File.WriteAllText(_path, "ours");
        var ours = DiskStamp.Of(_path);

        File.WriteAllText(_path, "theirs, written after");

        DiskStamp.Of(_path).Should().NotBe(ours);
    }
}
