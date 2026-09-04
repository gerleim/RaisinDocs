using System.Diagnostics;

namespace RaisinDocs;

/// <summary>
/// Records what each stage of a layout pass costs, so a slow keystroke can be attributed.
/// </summary>
/// <remarks>
/// Typing invalidates layout, and layout redoes every stage for the whole document: parse,
/// the visual block structure, a visual map per block, then wrapping. A synthetic 2895-block
/// document measured roughly 100 to 165 ms a keystroke across two runs of the same harness,
/// which is too wide a spread to design against and was not the reader's own document
/// anyway. This measures the real one.
///
/// Aggregated rather than logged per pass. A keystroke already costs too much to add a
/// synchronous file write to it, and a line per character would bury the shape in noise;
/// one summary every <see cref="RunsPerLine"/> passes costs nothing and reads better.
/// </remarks>
internal static class LayoutDiag
{
    /// <summary>
    /// Off unless RAISINDOCS_LAYOUT_DIAG is set or a host turns it on, so measuring costs
    /// nothing in a normal run and needs no rebuild to turn on.
    /// </summary>
    internal static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
                Log($"layout diagnostics enabled - {Environment.ProcessPath}");
        }
    }

    private static bool _enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAISINDOCS_LAYOUT_DIAG"));

    /// <summary>Where the layout log is written.</summary>
    internal static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "layout.log");

    /// <summary>How many layout passes are folded into one logged line.</summary>
    private const int RunsPerLine = 20;

    internal static void Log(string text)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            System.IO.File.AppendAllText(LogPath,
                $"{DateTime.Now:HH:mm:ss.fff}  {text}{Environment.NewLine}");
        }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly object Lock = new();
    private static readonly Dictionary<string, (double Max, double Total)> Stages = new();
    private static readonly List<string> Order = [];
    private static int _runs;
    private static int _reparses;

    /// <summary>
    /// Notes what <paramref name="label"/> cost since <paramref name="since"/>, and returns
    /// now, so a caller can chain one stage straight into the next.
    /// </summary>
    /// <remarks>
    /// Timestamps rather than a lambda per stage. Wrapping each stage in an Action would
    /// allocate one closure per stage per keystroke to measure a cost that is being blamed
    /// for the keystroke, which is the mistake the scroll diagnostics had to be rebuilt to
    /// avoid.
    /// </remarks>
    internal static long Mark(string label, long since)
    {
        long now = Stopwatch.GetTimestamp();
        if (!_enabled) return now;

        double ms = (now - since) * 1000.0 / Stopwatch.Frequency;
        lock (Lock)
        {
            if (!Stages.TryGetValue(label, out var s)) Order.Add(label);
            Stages[label] = (Math.Max(s.Max, ms), s.Total + ms);
        }
        return now;
    }

    /// <summary>Counts a pass where the merge forced a second parse.</summary>
    internal static void NoteReparse()
    {
        if (!_enabled) return;
        lock (Lock) { _reparses++; }
    }

    /// <summary>
    /// Ends one layout pass, and logs a summary once enough have accumulated.
    /// </summary>
    internal static void EndPass(int blockCount, int visualLineCount)
    {
        if (!_enabled) return;

        lock (Lock)
        {
            if (++_runs < RunsPerLine) return;

            var parts = new List<string>();
            double total = 0;
            foreach (var label in Order)
            {
                var (max, sum) = Stages[label];
                total += sum / _runs;
                parts.Add($"{label} {sum / _runs:F1}/{max:F1}");
            }

            Log($"{blockCount} blocks, {visualLineCount} lines, {_runs} passes, "
                + $"{_reparses} re-parsed | avg/max ms: {string.Join("  ", parts)} "
                + $"| total avg {total:F1}ms");

            Stages.Clear();
            Order.Clear();
            _runs = 0;
            _reparses = 0;
        }
    }
}
