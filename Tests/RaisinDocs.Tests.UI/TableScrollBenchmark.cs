using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Xunit;
using Xunit.Abstractions;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// The UI-thread cost of tables while scrolling, taken apart: layout, drawing a row into its
/// line visual, the arrange that builds visuals as the viewport moves, the overlay render, and
/// the caret query a render makes while the caret is in a table.
/// </summary>
/// <remarks>
/// A baseline for design/Table Cell Wrapping.md, which must not make a scroll frame dearer. Run it
/// on the commit before a change and again after, same machine, Release:
///
///     $env:RAISINDOCS_BENCH = "1"
///     dotnet test Tests/RaisinDocs.Tests.UI -c Release --filter "FullyQualifiedName~TableScrollBenchmark"
///
/// Without RAISINDOCS_BENCH it returns at once, so the normal suite neither pays for it nor gets
/// noisy timings. Results go to the test output and to %LOCALAPPDATA%\RaisinDocs\bench.
///
/// What it cannot see: the rasterisation behind BitmapCache and the composition that follows,
/// which happen off the UI thread. capture-scroll.ps1 is the measurement for those.
/// </remarks>
public class TableScrollBenchmark
{
    private const int Width = 1200;
    private const int Height = 800;
    private const double ScrollStep = 8;   // px per frame: a brisk wheel coast at 280 Hz

    private readonly ITestOutputHelper _output;

    public TableScrollBenchmark(ITestOutputHelper output) => _output = output;

    private static bool Enabled =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAISINDOCS_BENCH"));

    private static string SamplePath(string name) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "design", "samples", name));

    [StaFact]
    public void WideTables() => Run("Wide Tables.md");

    [StaFact]
    public void FittedTables() => Run("Fitted Tables.md");

    private void Run(string sample)
    {
        if (!Enabled) return;

        var canvas = new DocsCanvas();
        canvas.SetText(File.ReadAllText(SamplePath(sample)));
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(Width, Height));
        canvas.Arrange(new Rect(0, 0, Width, Height));
        canvas.TestComputeLayout();

        var report = new StringBuilder();
        void Line(string s) { report.AppendLine(s); _output.WriteLine(s); }

        var lines = canvas.TestVisualLines;
        var tableRows = new List<int>();
        var otherLines = new List<int>();
        int paragraphLine = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].BlockKind is BlockKind.TableHeaderRow or BlockKind.TableDataRow) tableRows.Add(i);
            else
            {
                otherLines.Add(i);
                if (paragraphLine < 0 && lines[i].BlockKind == BlockKind.Paragraph && lines[i].Length > 40)
                    paragraphLine = i;
            }
        }

        Line($"{sample}  {Width}x{Height}  commit {GitHead()}  {DateTime.Now:yyyy-MM-dd HH:mm}");
        Line($"  {lines.Count} visual lines ({tableRows.Count} table rows), content height {canvas.TotalContentHeight:F0}px");

        // Warm every cache a real run would have warm: glyph widths, typefaces, the JIT.
        canvas.TestComputeLayoutAtWidth(Width - 20);
        canvas.TestComputeLayout();
        foreach (int i in tableRows) canvas.TestDrawLineContent(i);
        foreach (int i in otherLines) canvas.TestDrawLineContent(i);

        // Layout, whole document: runs on edit and resize, never on a scroll frame.
        var layout = Repeat(15, () =>
        {
            canvas.TestComputeLayoutAtWidth(Width - 20);
            canvas.TestComputeLayout();
        });
        Line($"  layout (whole doc)          {Stats(layout)}");

        // Drawing a line into its visual: what a line visual build costs on the UI thread. The
        // render cache is invalidated each pass, as a real build follows an invalidation.
        var rowDraw = new List<double>();
        var otherDraw = new List<double>();
        for (int pass = 0; pass < 5; pass++)
        {
            canvas.InvalidateRenderCache();
            foreach (int i in tableRows) rowDraw.Add(Time(() => canvas.TestDrawLineContent(i)));
            foreach (int i in otherLines) otherDraw.Add(Time(() => canvas.TestDrawLineContent(i)));
        }
        Line($"  draw line: table row        {Stats(rowDraw)}");
        Line($"  draw line: other            {Stats(otherDraw)}");

        // A scroll sweep through the whole document, one arrange per frame, building visuals
        // exactly as UpdateContentLayer does during a gesture (visible lines plus the budgeted
        // pre-render margin), then one overlay render per frame.
        canvas.InvalidateRenderCache();
        canvas.SetScrollOffsetDirect(0);
        canvas.TestUpdateContentLayer(Height);
        var arrange = new List<double>();
        var render = new List<double>();
        double max = Math.Max(0, canvas.TotalContentHeight - Height);
        for (double y = 0; y <= max; y += ScrollStep)
        {
            canvas.SetScrollOffsetDirect(y);
            arrange.Add(Time(() => canvas.TestUpdateContentLayer(Height)));
            render.Add(Time(canvas.TestRenderOnce));
        }
        Line($"  scroll frame: arrange       {Stats(arrange)}");
        Line($"  scroll frame: render        {Stats(render)}");

        // The caret query a render makes while the caret sits in a table cell, against the same
        // query on a paragraph. The canvas is never focused here, so the render above draws no
        // caret; this is that missing piece.
        var (rowBlock, rowOffset) = MidCell(canvas, tableRows[tableRows.Count / 2]);
        canvas.TestSetCursor(rowBlock, rowOffset);
        var caretTable = Repeat(2000, () => _ = canvas.TestCursorX);
        Line($"  caret X: in table cell      {Stats(caretTable)}");
        if (paragraphLine >= 0)
        {
            canvas.TestSetCursor(lines[paragraphLine].BlockIndex, lines[paragraphLine].StartOffset + 20);
            var caretPara = Repeat(2000, () => _ = canvas.TestCursorX);
            Line($"  caret X: in paragraph       {Stats(caretPara)}");
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaisinDocs", "bench");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "table-scroll.txt"), report + Environment.NewLine);
    }

    /// <summary>A block and an offset inside the second cell of a row, where the long text is.</summary>
    private static (int Block, int Offset) MidCell(DocsCanvas canvas, int vi)
    {
        int block = canvas.TestVisualLines[vi].BlockIndex;
        string text = canvas.TestGetBlockText(block);
        int second = text.IndexOf('|', text.IndexOf('|', 1) + 1);
        return (block, Math.Min(text.Length, second + 12));
    }

    private static double Time(Action work)
    {
        long t0 = Stopwatch.GetTimestamp();
        work();
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    private static List<double> Repeat(int n, Action work)
    {
        var samples = new List<double>(n);
        for (int i = 0; i < n; i++) samples.Add(Time(work));
        return samples;
    }

    private static string Stats(List<double> ms)
    {
        if (ms.Count == 0) return "no samples";
        var s = ms.OrderBy(x => x).ToList();
        double P(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        return $"n={s.Count,6}  median {P(0.5),8:F4}ms  p95 {P(0.95),8:F4}ms  max {s[^1],8:F3}ms  total {s.Sum(),9:F1}ms";
    }

    private static string GitHead()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "describe --always --dirty")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(SamplePath("x"))!,
            };
            using var p = Process.Start(psi)!;
            string head = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return head;
        }
        catch (Exception) { return "?"; }
    }
}
