using System.Diagnostics;
using System.IO;
using System.Text;

namespace RaisinDocs;

/// <summary>
/// TEMPORARY instrumentation for the mouse-wheel scrolling investigation. Delete when done.
///
/// Writes two interleaved records to <see cref="LogPath"/>, tab separated:
///   W  ourMs  osMs  delta  canvasHeight   a wheel notch arriving at OnMouseWheel
///   F  ourMs  dtMs  velocity  offset      one coast frame
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
    private const int MaxBufferedLines = 2000;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StringBuilder Buffer = new();
    private static int _lines;

    internal static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RaisinDocs",
        $"wheel-{DateTime.Now:yyyy-MM-dd}.log");

    internal static void Wheel(int delta, int osMs, double canvasHeight)
    {
        Buffer.Append("W\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append(osMs)
              .Append('\t').Append(delta)
              .Append('\t').Append(canvasHeight.ToString("F0"))
              .AppendLine();
        if (++_lines >= MaxBufferedLines) Flush();
    }

    internal static void Frame(double dtSeconds, double velocity, double offset)
    {
        Buffer.Append("F\t").Append(Clock.Elapsed.TotalMilliseconds.ToString("F1"))
              .Append('\t').Append((dtSeconds * 1000).ToString("F2"))
              .Append('\t').Append(velocity.ToString("F1"))
              .Append('\t').Append(offset.ToString("F2"))
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
