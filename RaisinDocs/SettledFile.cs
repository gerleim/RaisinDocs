using System.IO;
using System.Threading;

namespace RaisinDocs;

/// <summary>
/// Which version of a file is on disk: its last write time and its length.
/// </summary>
/// <remarks>
/// Kept by a document for the version its text was loaded from or last saved to, so a save can tell
/// whether another program has written the file since — and ask, rather than replace that work.
/// </remarks>
public readonly record struct DiskStamp(DateTime LastWriteUtc, long Length)
{
    /// <summary>The stamp of the file at <paramref name="path"/>, or <c>null</c> when there is no file.</summary>
    public static DiskStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new DiskStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A file's text, read once the program writing it has finished.</summary>
public sealed record SettledText(string Content, DiskStamp Stamp);

/// <summary>
/// Reads a file another program is writing, once that program has finished with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A change is reported while it is still being made.</b> The first event of a save often comes
/// while the writer still holds the file. Probed with a writer that holds it across two chunks, the
/// first reload was denied and only a second event, after the writer closed, brought the new text — so
/// a writer that finished inside the first event, or a burst the watcher coalesced, left the tab on
/// stale text that a later save would write over the external change.
/// </para>
/// <para>
/// So this waits. It reads with the default share mode, which is denied while a writer holds the file:
/// that refusal is what keeps a half-written file out, and widening it would load the partial text as
/// though it were the document. A read that succeeds is kept only if the file is still the same a
/// moment later, which catches a writer that opens and closes it more than once.
/// </para>
/// </remarks>
public static class SettledFile
{
    /// <summary>
    /// The file's text once it has stopped changing, or <c>null</c> if it did not settle — or went
    /// away — within <paramref name="attempts"/> tries.
    /// </summary>
    /// <remarks>Blocks for up to <paramref name="attempts"/> × <paramref name="settleMs"/>; call it off the UI thread.</remarks>
    public static SettledText? Read(string path, int attempts = 15, int settleMs = 200)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var before = DiskStamp.Of(path);
            if (before is null)
                return null;

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(settleMs);
                continue;
            }

            Thread.Sleep(settleMs);
            if (DiskStamp.Of(path) == before)
                return new SettledText(content, before.Value);
        }

        return null;
    }
}
