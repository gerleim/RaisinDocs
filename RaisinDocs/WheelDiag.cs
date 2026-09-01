using System.Diagnostics;
using System.IO;
using System.Text;

namespace RaisinDocs;

/// <summary>
/// TEMPORARY instrumentation for the mouse-wheel scrolling investigation. Delete when done.
///
/// Writes two interleaved records to <see cref="LogPath"/>, tab separated:
///   S  ourMs  mvid  dllWriteTime  dllPath    which assembly wrote this session
///   M  ourMs  us                          one minimap OnRender pass
///   R  ourMs  totalUs bgUs textUs spellUs drawn firstVisible totalLines hits misses
///                                             one OnRender pass, split by phase
///   C  ourMs  intervalMs  targetFps           a coast starting, with the repaint target
///                                             resolved for the display the window is on
///   W  ourMs  osMs  delta  canvasHeight       a wheel notch arriving at OnMouseWheel
///   F  ourMs  dtMs  velocity  offset  painted one coast frame; painted is 1 when this
///                                             frame actually repainted, so the achieved
///                                             repaint rate can be read off directly rather
///                                             than inferred from the tick rate
///
/// The decisive comparison is <c>osMs</c> (the Windows message time, i.e. when the notch
/// physically happened) against <c>ourMs</c> (when the UI thread got round to handling it).
/// Evenly spaced osMs that arrive bunched together in ourMs means the render loop is
/// starving input delivery, and the velocity impulses land in clumps rather than evenly.
///
/// Records are buffered in memory and only written out when a coast ends, so the logging
/// itself cannot perturb the frame timing it is measuring.
/// </summary>
internal static class WheelDiag
{
    private const char TAB = '\t';

    private const int MaxBufferedLines = 2000;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StringBuilder Buffer = new();
    private static int _lines;

    internal static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RaisinDocs",
        $"wheel-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>
    /// Records which assembly wrote this session. Sessions append to one file across runs,
    /// and a process launched before a rebuild keeps the assembly it started with, so
    /// without this a stale run is indistinguishable from a fresh one in the data. The MVID
    /// changes on every compile.
    /// </summary>
    static WheelDiag()
    {
        try
        {
            var asm = typeof(WheelDiag).Assembly;
            Buffer.Append("S" + TAB).Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
                  .Append(TAB).Append(asm.ManifestModule.ModuleVersionId.ToString("N"))
                  .Append(TAB).Append(File.GetLastWriteTime(asm.Location).ToString("HH:mm:ss"))
                  .Append(TAB).Append(asm.Location)
                  .AppendLine();
            Flush();
        }
        catch { /* diagnostics must never break scrolling */ }
    }

    /// <summary>
    /// One OnRender pass, in microseconds, split by phase, plus how many visual lines were
    /// actually drawn and how many had to be skipped to reach the first visible one.
    /// </summary>
    internal static void Render(long totalUs, long bgUs, long textUs, long spellUs,
        int linesDrawn, int firstVisible, int totalLines, int hits, int misses)
    {
        Buffer.Append("R\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append(totalUs)
              .Append('\t').Append(bgUs)
              .Append('\t').Append(textUs)
              .Append('\t').Append(spellUs)
              .Append('\t').Append(linesDrawn)
              .Append('\t').Append(firstVisible)
              .Append('\t').Append(totalLines)
              .Append('\t').Append(hits)
              .Append('\t').Append(misses)
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    /// <summary>One minimap OnRender pass, in microseconds.</summary>
    internal static void Minimap(long us)
    {
        Buffer.Append("M\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append(us)
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    /// <summary>Start of a coast, recording the repaint target resolved for this gesture.</summary>
    internal static void Coast(double repaintInterval)
    {
        Buffer.Append("C\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append((repaintInterval * 1000).ToString("F2"))
              .Append('\t').Append((1.0 / repaintInterval).ToString("F0"))
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    internal static void Wheel(int delta, int osMs, double canvasHeight)
    {
        Buffer.Append("W\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append(osMs)
              .Append('\t').Append(delta)
              .Append('\t').Append(canvasHeight.ToString("F0"))
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    internal static void Frame(double dtSeconds, double velocity, double offset, bool painted)
    {
        Buffer.Append("F\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append((dtSeconds * 1000).ToString("F2"))
              .Append('\t').Append(velocity.ToString("F1"))
              .Append('\t').Append(offset.ToString("F2"))
              .Append('\t').Append(painted ? '1' : '0')
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    /// <summary>Writes the buffer out. Called when a coast ends, so no gesture is left unlogged.</summary>
    internal static void Flush()
    {
        if (Buffer.Length == 0) return;
        _lines = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, Buffer.ToString());
        }
        catch { /* diagnostics must never break scrolling */ }
        Buffer.Clear();
    }
}
