using System.Diagnostics;
using System.IO;
using System.Text;

namespace RaisinDocs;

/// <summary>
/// TEMPORARY probe for phase 2 of design/Scroll Pre-Buffering.md. Delete once it has said
/// whether caching lines as visuals actually made a frame cheaper.
///
/// One line per OnRender:  ourMs  onRenderUs  linesDrawn  visualsBuilt  renderVersion
///
/// visualsBuilt is the count that had to be rasterised this frame. Scrolling should reveal
/// only a line or two at a time, so a number near linesDrawn means the cache is being thrown
/// away - most likely RenderVersion moving, which drops every visual.
/// </summary>
internal static class Phase2Diag
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StringBuilder Buffer = new();
    private static int _lines;

    internal static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RaisinDocs", $"phase2-{DateTime.Now:yyyy-MM-dd-HHmm}.log");

    internal static void Frame(long onRenderUs, int linesDrawn, int visualsBuilt, int renderVersion,
        double repaintMs, double displayMs)
    {
        // Gen0/1/2 collection counts, so a late frame can be checked against a collection
        // rather than guessed at. Reading them is a few field loads.
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        Buffer.Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append(onRenderUs)
              .Append('\t').Append(linesDrawn)
              .Append('\t').Append(visualsBuilt)
              .Append('\t').Append(renderVersion)
              .Append('\t').Append(repaintMs.ToString("F2"))
              .Append('\t').Append(displayMs.ToString("F2"))
              .Append('\t').Append(g0)
              .Append('\t').Append(g1)
              .Append('\t').Append(g2)
              .AppendLine();
        if (++_lines >= 400) Flush();
    }

    internal static void Flush()
    {
        if (Buffer.Length == 0) return;
        _lines = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, Buffer.ToString());
        }
        catch { }
        Buffer.Clear();
    }
}
