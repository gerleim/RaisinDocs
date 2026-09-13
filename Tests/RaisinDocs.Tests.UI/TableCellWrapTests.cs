using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// A table wider than the window shrinks its columns to fit and wraps the text inside its cells,
/// making the row taller. See design/Table Cell Wrapping.md.
/// </summary>
public class TableCellWrapTests
{
    private const int CanvasHeight = 600;
    private const double Eps = 0.001;

    private static DocsCanvas MakeCanvasAt(string text, int width)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(width, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, width, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    private static List<int> TableRows(DocsCanvas canvas)
    {
        var rows = new List<int>();
        for (int i = 0; i < canvas.TestVisualLines.Count; i++)
            if (canvas.TestVisualLines[i].BlockKind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
                rows.Add(i);
        return rows;
    }

    private static double LineHeight(DocsCanvas canvas, int vi)
        => canvas.TestMeasure.GetLineHeight(canvas.TestVisualLines[vi].BlockKind);

    private static string LongProse(int words)
    {
        var vocab = "layout anchorable pane document floating window serializer content title selected focused".Split(' ');
        return string.Join(" ", Enumerable.Range(0, words).Select(i => vocab[(i * 7) % vocab.Length]));
    }

    /// <summary>The left edge of every column, then the table's right edge.</summary>
    private static double[] ColumnEdges(double[] widths)
    {
        var edges = new double[widths.Length + 1];
        edges[0] = DocsCanvas._padding;
        for (int c = 0; c < widths.Length; c++) edges[c + 1] = edges[c] + widths[c];
        return edges;
    }

    /// <summary>A glyph run's visible extent: its left edge and the right edge of its last non-space character.</summary>
    private static (double Left, double Right)? InkExtent((Point Origin, string Text, double[] Edges) run)
    {
        int last = run.Text.Length - 1;
        while (last >= 0 && run.Text[last] == ' ') last--;
        if (last < 0) return null;
        return (run.Edges[0], run.Edges[last + 1]);
    }

    // --- 1. A table that fits is untouched ---

    [StaFact]
    public void FittingTable_KeepsNaturalWidths_AndOneLinePerRow()
    {
        const string md = "| Name | Value |\n|---|---|\n| alpha | one two three |\n| beta | four five |";
        var narrow = MakeCanvasAt(md, 800);
        var wide = MakeCanvasAt(md, 4000);

        int block = narrow.TestVisualLines[TableRows(narrow)[0]].BlockIndex;
        narrow.TestTableColumnWidths(block).Should().Equal(wide.TestTableColumnWidths(block),
            "a table that fits at 800px gets exactly the widths it gets with room to spare");

        var rows = TableRows(narrow);
        foreach (int vi in rows)
        {
            narrow.TestVisualLines[vi].TableLayout.Should().BeNull("a fitting table takes no wrap pass");
            narrow.TestVisualLines[vi].OverrideHeight.Should().Be(0);
        }
        for (int r = 0; r + 1 < rows.Count; r++)
            (narrow.TestGetLineYPosition(rows[r + 1]) - narrow.TestGetLineYPosition(rows[r]))
                .Should().BeApproximately(LineHeight(narrow, rows[r]), Eps);
    }

    // --- 2. A wide table fits the window and its rows grow ---

    [StaFact]
    public void WideTable_ColumnsFitTheWindow_AndRowsAreAsTallAsTheirLines()
    {
        string md = $"| Element | Contents |\n|---|---|\n| RootPanel | {LongProse(60)} |\n| Hidden | {LongProse(20)} |\n| Last | short |";
        const int width = 400;
        var canvas = MakeCanvasAt(md, width);

        var rows = TableRows(canvas);
        var widths = canvas.TestTableColumnWidths(canvas.TestVisualLines[rows[0]].BlockIndex)!;
        widths.Sum().Should().BeLessThanOrEqualTo(width - 2 * DocsCanvas._padding + Eps);

        canvas.TestVisualLines.Where(vl => vl.TableLayout != null).Should().NotBeEmpty();
        canvas.TestVisualLines[rows[1]].TableLayout!.LineCount.Should().BeGreaterThan(1);

        for (int r = 0; r + 1 < rows.Count; r++)
        {
            int vi = rows[r];
            int lines = canvas.TestVisualLines[vi].TableLayout?.LineCount ?? 1;
            (canvas.TestGetLineYPosition(rows[r + 1]) - canvas.TestGetLineYPosition(vi))
                .Should().BeApproximately(lines * LineHeight(canvas, vi), Eps,
                    $"row {r} holds {lines} line(s) of text");
        }
    }

    // --- 3. Tier 2: every column keeps its longest word ---

    [StaFact]
    public void MinimumsFit_NoColumnBreaksAWord_AndAOneWordColumnKeepsItsWidth()
    {
        string md = $"| Element | Contents |\n|---|---|\n| RootPanel | {LongProse(60)} |\n| FloatingWindows | {LongProse(25)} |";
        var canvas = MakeCanvasAt(md, 500);
        var roomy = MakeCanvasAt(md, 4000);

        var rows = TableRows(canvas);
        int block = canvas.TestVisualLines[rows[0]].BlockIndex;
        canvas.TestTableColumnWidths(block)![0].Should().BeApproximately(roomy.TestTableColumnWidths(block)![0], Eps,
            "a column of single words has a minimum equal to its natural width, so gives nothing up");

        foreach (int vi in rows)
        {
            var layout = canvas.TestVisualLines[vi].TableLayout!;
            string text = canvas.TestGetBlockText(canvas.TestVisualLines[vi].BlockIndex);
            layout.CellLineCount(0).Should().Be(1);
            for (int c = 0; c < layout.CellCount; c++)
                for (int k = 1; k < layout.CellLineCount(c); k++)
                    text[layout.LineStarts[c][k] - 1].Should().Be(' ', "with every longest word fitting, lines break only after a space");
        }
    }

    // --- 4. Tier 3: long words break, down to a floor ---

    [StaFact]
    public void MinimumsDoNotFit_LongWordsBreakMidWord_AndColumnsKeepTheirFloor()
    {
        const string md = "| A | B |\n|---|---|\n| Supercalifragilisticexpialidocious | Antidisestablishmentarianism |";
        const int width = 250;
        var canvas = MakeCanvasAt(md, width);
        var roomy = MakeCanvasAt(md, 4000);

        var rows = TableRows(canvas);
        int block = canvas.TestVisualLines[rows[0]].BlockIndex;
        var widths = canvas.TestTableColumnWidths(block)!;
        var natural = roomy.TestTableColumnWidths(block)!;

        double floor = 3 * canvas.TestMeasure.MeasureCharWidth('0', BlockKind.TableHeaderRow, InlineStyle.Normal)
                       + 2 * DocsCanvas._tableCellPadding;
        for (int c = 0; c < widths.Length; c++)
            widths[c].Should().BeGreaterThanOrEqualTo(Math.Min(floor, natural[c]) - Eps);
        widths.Sum().Should().BeLessThanOrEqualTo(width - 2 * DocsCanvas._padding + Eps);

        var data = canvas.TestVisualLines[rows[1]];
        string text = canvas.TestGetBlockText(data.BlockIndex);
        var layout = data.TableLayout!;
        bool midWord = false;
        for (int c = 0; c < layout.CellCount; c++)
            for (int k = 1; k < layout.CellLineCount(c); k++)
                midWord |= char.IsLetter(text[layout.LineStarts[c][k] - 1]);
        midWord.Should().BeTrue("a word wider than its column has to break inside itself");
    }

    // --- 5. Hidden markers and bold headers are measured as drawn ---

    [StaFact]
    public void WrappedText_WithHiddenMarkersAndBoldHeaders_StaysInsideItsColumn()
    {
        string md = $"| Element with a long header | Contents under a header just as long |\n|---|---|\n" +
                    $"| **RootPanel** `code` | **{LongProse(12)}** and `{LongProse(3)}` then {LongProse(20)} |";
        var canvas = MakeCanvasAt(md, 420);

        var rows = TableRows(canvas);
        var widths = canvas.TestTableColumnWidths(canvas.TestVisualLines[rows[0]].BlockIndex)!;
        var edges = ColumnEdges(widths);
        canvas.TestVisualLines[rows[0]].TableLayout!.LineCount.Should().BeGreaterThan(1, "the header wraps too");

        foreach (int vi in rows)
        {
            foreach (var run in canvas.TestLineGlyphRuns(vi))
            {
                if (InkExtent(run) is not { } ink) continue;
                int c = Array.FindLastIndex(edges, e => e <= run.Origin.X + Eps);
                c.Should().BeInRange(0, widths.Length - 1);
                ink.Right.Should().BeLessThanOrEqualTo(edges[c + 1] - DocsCanvas._tableCellPadding + 0.5,
                    $"'{run.Text}' is measured the way it is drawn, bold and hidden markup included");
            }
        }
    }

    // --- 6. Alignment applies to each line of a cell ---

    [StaTheory]
    [InlineData("---:")]
    [InlineData(":---:")]
    public void WrappedLines_AreAlignedOneByOne(string separator)
    {
        string md = $"| Key | Value |\n|---|{separator}|\n| k | {LongProse(40)} |";
        var canvas = MakeCanvasAt(md, 400);

        var rows = TableRows(canvas);
        var widths = canvas.TestTableColumnWidths(canvas.TestVisualLines[rows[0]].BlockIndex)!;
        var edges = ColumnEdges(widths);
        double contentLeft = edges[1] + DocsCanvas._tableCellPadding;
        double contentRight = edges[2] - DocsCanvas._tableCellPadding;
        double lineH = LineHeight(canvas, rows[1]);

        var lines = canvas.TestLineGlyphRuns(rows[1])
            .Where(r => r.Origin.X >= edges[1] - Eps)
            .Select(r => (Line: (int)Math.Floor(r.Origin.Y / lineH), Ink: InkExtent(r)))
            .Where(r => r.Ink != null)
            .ToList();
        lines.Select(l => l.Line).Distinct().Count().Should().BeGreaterThan(1, "the cell wraps");

        foreach (var (line, ink) in lines)
        {
            if (separator == "---:")
                ink!.Value.Right.Should().BeApproximately(contentRight, 0.5, $"line {line} is right-aligned on its own");
            else
                ((ink!.Value.Left + ink.Value.Right) / 2).Should().BeApproximately((contentLeft + contentRight) / 2, 0.5,
                    $"line {line} is centred on its own");
        }
    }
}
