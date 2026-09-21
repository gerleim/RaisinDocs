using System.IO;
using System.Text.Json;

namespace RaisinDocs;

/// <summary>An unsaved document to rescue: where it came from, and how to get its text.</summary>
/// <param name="OriginalPath">The file it was opened from or last saved to, or <c>null</c> for an untitled one.</param>
/// <param name="GetText">Reads its text. Called inside the rescue, so one that throws costs only its own document.</param>
/// <param name="Baseline">The version on disk its text was based on, so a later save can tell if the file moved on.</param>
public sealed record RescuedDocument(string? OriginalPath, Func<string> GetText, DiskStamp? Baseline);

/// <summary>A document rescued by an earlier session, waiting to be offered back.</summary>
public sealed record RecoveredDocument(string RecoveryFile, string? OriginalPath, DiskStamp? Baseline, string Text);

/// <summary>
/// Keeps unsaved documents through a crash: writes them aside as the editor goes down, and finds them
/// again at the next start.
/// </summary>
/// <remarks>
/// <para>
/// <b>A crash took every unsaved document with it.</b> The editor had no handler for an unexpected
/// exception, so WPF ended the process and the unsaved text — the one thing nothing else holds — was
/// gone. Continuing after such an exception was ruled out: the editor would run in a state no one
/// planned for, and a save from there could write a damaged document over a good file. So the editor
/// logs, rescues what is unsaved here, and exits; the next start offers it back.
/// </para>
/// <para>
/// Each document is two files in the recovery folder, never anywhere near the original: its text,
/// and beside it a small <c>.origin</c> file naming where it came from. Each pair is written on its
/// own, so a document whose text cannot be read — the damage that caused the crash may be in it —
/// costs only itself. A text whose origin failed to write is still offered, as untitled.
/// </para>
/// </remarks>
public sealed class DocumentRecovery(string directory)
{
    private const string OriginExtension = ".origin";

    public string Directory { get; } = directory;

    /// <summary>Writes each document aside, and returns how many were written.</summary>
    /// <remarks>Never throws: it runs while the application is going down.</remarks>
    public int Rescue(IEnumerable<RescuedDocument> documents)
    {
        var written = 0;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var index = 0;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch
        {
            return 0;
        }

        foreach (var document in documents)
        {
            index++;
            try
            {
                var text = document.GetText();
                var name = document.OriginalPath is { } original ? Path.GetFileName(original) : "Untitled.md";
                var path = Path.Combine(Directory, $"{stamp}-{index:D3} {name}");
                if (File.Exists(path))
                    path = Path.Combine(Directory, $"{stamp}-{index:D3}-{Guid.NewGuid():N} {name}");

                File.WriteAllText(path, text);
                written++;

                try
                {
                    File.WriteAllText(path + OriginExtension, JsonSerializer.Serialize(new Origin
                    {
                        OriginalPath = document.OriginalPath,
                        LastWriteUtc = document.Baseline?.LastWriteUtc,
                        Length = document.Baseline?.Length,
                    }));
                }
                catch
                {
                    // The text is what matters; without its origin it comes back untitled.
                }
            }
            catch
            {
                // This document is lost; the others need not be.
            }
        }

        return written;
    }

    /// <summary>The documents earlier sessions rescued, oldest first.</summary>
    public IReadOnlyList<RecoveredDocument> Find()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var found = new List<RecoveredDocument>();
        foreach (var path in System.IO.Directory.GetFiles(Directory).Order(StringComparer.Ordinal))
        {
            if (path.EndsWith(OriginExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var origin = ReadOrigin(path + OriginExtension);
            DiskStamp? baseline = origin is { LastWriteUtc: { } time, Length: { } length } ? new DiskStamp(time, length) : null;
            found.Add(new RecoveredDocument(path, origin?.OriginalPath, baseline, text));
        }

        return found;
    }

    /// <summary>Removes a recovered document, once it is open again or the user has let it go.</summary>
    public void Discard(RecoveredDocument document)
    {
        File.Delete(document.RecoveryFile);
        File.Delete(document.RecoveryFile + OriginExtension);
    }

    private static Origin? ReadOrigin(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Origin>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class Origin
    {
        public string? OriginalPath { get; set; }
        public DateTime? LastWriteUtc { get; set; }
        public long? Length { get; set; }
    }
}
