using System.Diagnostics;

namespace RaisinDocs;

/// <summary>
/// Records the cost of individual pieces of work during a scroll, so a stall can be attributed.
/// </summary>
/// <remarks>
/// Scroll diagnostics measured a paint interval whose median is a healthy 3.7ms but whose
/// maximum is 94 to 124ms - a tenth of a second with nothing drawn, inside a gesture lasting
/// less than a second, followed by an 81 pixel jump. That is the UI thread blocked, not a pacing
/// problem, and it also explains the compositor's own gaps: while this thread is blocked WPF
/// cannot compose either.
///
/// Which piece blocks is not something to reason about. Each candidate reports what it cost and
/// the worst of each is printed with the gesture summary.
/// </remarks>
internal static class ScrollDiag
{
    /// <summary>
    /// Off unless RAISINDOCS_SCROLL_DIAG is set or a host turns it on, so measuring costs
    /// nothing in a normal run and needs no rebuild to turn on.
    /// </summary>
    /// <remarks>
    /// Settable because an environment variable is awkward to pass from a debugger or a
    /// shortcut; DocsCanvas.ScrollDiagnostics is the public way in, and a host that sets it
    /// must do so before the canvas is constructed.
    /// </remarks>
    internal static bool Enabled { get; set; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAISINDOCS_SCROLL_DIAG"));

    private static readonly object Lock = new();
    private static readonly Dictionary<string, (double Max, double Total, int Count)> Costs = new();

    /// <summary>Times <paramref name="work"/> under <paramref name="label"/>.</summary>
    internal static void Time(string label, Action work)
    {
        if (!Enabled) { work(); return; }

        var sw = Stopwatch.StartNew();
        work();
        sw.Stop();
        Note(label, sw.Elapsed.TotalMilliseconds);
    }

    internal static void Note(string label, double ms)
    {
        if (!Enabled) return;
        lock (Lock)
        {
            Costs.TryGetValue(label, out var c);
            Costs[label] = (Math.Max(c.Max, ms), c.Total + ms, c.Count + 1);
        }
    }

    /// <summary>The worst of each label since the last call, then clears.</summary>
    internal static string Snapshot()
    {
        lock (Lock)
        {
            if (Costs.Count == 0) return "no costs recorded";

            var parts = new List<string>();
            foreach (var kv in Costs)
            {
                var (max, total, count) = kv.Value;
                parts.Add($"{kv.Key} x{count} max {max:F1}ms avg {total / Math.Max(1, count):F2}ms");
            }
            Costs.Clear();

            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts);
        }
    }
}
