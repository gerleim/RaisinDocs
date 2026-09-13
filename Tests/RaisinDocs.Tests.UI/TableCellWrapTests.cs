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

    // --- 7. The caret sits on its own line of the cell ---

    private const int WrapWidth = 400;

    private static string WrappedRowTable() =>
        $"| Element | Contents |\n|---|---|\n| RootPanel | {LongProse(10)} **{LongProse(6)}** {LongProse(30)} |\n| Last | short |";

    [StaFact]
    public void Caret_MovesDownALine_ExactlyWhereTheCellsNextLineStarts_AndIsOneLineTall()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        int vi = TableRows(canvas)[1];
        var vl = canvas.TestVisualLines[vi];
        var layout = vl.TableLayout!;
        layout.CellLineCount(1).Should().BeGreaterThan(2);

        double rowY = canvas.TestGetLineYPosition(vi);
        double lineH = LineHeight(canvas, vi);

        for (int k = 1; k < layout.CellLineCount(1); k++)
        {
            int start = layout.LineStarts[1][k];

            canvas.TestSetCursor(vl.BlockIndex, start - 1);
            (canvas.TestCursorY - rowY).Should().BeApproximately((k - 1) * lineH, Eps, $"the offset before line {k} starts is still on line {k - 1}");

            canvas.TestSetCursor(vl.BlockIndex, start);
            (canvas.TestCursorY - rowY).Should().BeApproximately(k * lineH, Eps, $"line {k} starts at raw offset {start}");
            canvas.TestCaretHeight.Should().BeApproximately(lineH, Eps, "the caret spans one line of the cell, not the row");
        }
    }

    // --- 8. A click lands on the offset the caret draws at ---

    [StaFact]
    public void Click_AtTheCaret_ReturnsItsOffset_OnEveryLineOfAWrappedCell()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        int vi = TableRows(canvas)[1];
        var vl = canvas.TestVisualLines[vi];
        var layout = vl.TableLayout!;
        double lineH = LineHeight(canvas, vi);

        int checkedOffsets = 0;
        for (int o = layout.LineStarts[1][0]; o <= layout.CellEnds[1]; o++)
        {
            if (o < layout.CellEnds[1] && canvas.TestIsHiddenInVisual(vl.BlockIndex, o)) continue;

            canvas.TestSetCursor(vl.BlockIndex, o);
            var point = new Point(canvas.TestCursorX, canvas.TestCursorY + lineH / 2);
            canvas.HitTestToPosition(point, out int block, out int offset);

            block.Should().Be(vl.BlockIndex);
            offset.Should().Be(o, $"the caret for offset {o} is drawn at ({point.X:F1}, {point.Y:F1})");
            checkedOffsets++;
        }
        checkedOffsets.Should().BeGreaterThan(100);
    }

    [StaFact]
    public void Click_PastTheEndOfALineThatIsNotTheCellsLast_StaysOnThatLine()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        int vi = TableRows(canvas)[1];
        var vl = canvas.TestVisualLines[vi];
        var layout = vl.TableLayout!;
        var edges = ColumnEdges(canvas.TestTableColumnWidths(vl.BlockIndex)!);
        double rowY = canvas.TestGetLineYPosition(vi);
        double lineH = LineHeight(canvas, vi);

        for (int k = 0; k + 1 < layout.CellLineCount(1); k++)
        {
            canvas.HitTestToPosition(new Point(edges[2] - 1, rowY + k * lineH + lineH / 2), out _, out int offset);
            offset.Should().BeInRange(layout.LineStarts[1][k], layout.LineStarts[1][k + 1] - 1,
                $"past the end of line {k} is still line {k}, not the start of the next");

            canvas.TestSetCursor(vl.BlockIndex, offset);
            (canvas.TestCursorY - rowY).Should().BeApproximately(k * lineH, Eps);
        }
    }

    // --- 17. Printing cannot leave page-width cell lines in the screen's cache ---

    [StaFact]
    public void CaretX_AfterAPrintLayout_IsTheScreensAgain()
    {
        // Right-aligned, so a cell line's alignment depends on what that line holds: the page's
        // first line of the cell is aligned differently from the screen's whole-cell line.
        string md = $"| Key | Value |\n|---|---:|\n| k | {LongProse(12)} |";
        var canvas = MakeCanvasAt(md, 800);
        int vi = TableRows(canvas)[1];
        var vl = canvas.TestVisualLines[vi];
        vl.TableLayout.Should().BeNull("the table fits the screen");

        string text = canvas.TestGetBlockText(vl.BlockIndex);
        canvas.TestSetCursor(vl.BlockIndex, text.IndexOf('|', 1) + 3); // inside the first word
        double screen = canvas.TestCursorX;

        // Empties the cache, so the next read is a miss built against whatever lines are current.
        canvas.InvalidateRenderCache();
        canvas.TestComputeLayoutAtWidth(250);
        canvas.TestVisualLines[vi].TableLayout.Should().NotBeNull("the table has to wrap at the page width");
        canvas.TestCursorXNoLayout.Should().NotBeApproximately(screen, 0.5,
            "read against the page's lines, the caret is somewhere else - so a cache entry was built from them");

        canvas.TestCursorX.Should().BeApproximately(screen, Eps,
            "laying the screen out again moves LayoutVersion, and the page's cell lines must not be reused");
    }

    // --- 18. An empty cell in a wrapped row ---

    [StaFact]
    public void EmptyCell_InAWrappedRow_HasACaretOnTheRowsFirstLine_AndTakesClicks()
    {
        string md = $"| Key | Note | Contents |\n|---|---|---|\n| k |  | {LongProse(60)} |";
        var canvas = MakeCanvasAt(md, WrapWidth);
        int vi = TableRows(canvas)[1];
        var vl = canvas.TestVisualLines[vi];
        vl.TableLayout!.LineCount.Should().BeGreaterThan(1);

        int emptyStart = vl.TableLayout.LineStarts[1][0];
        vl.TableLayout.CellEnds[1].Should().Be(emptyStart, "the cell holds nothing");

        canvas.TestSetCursor(vl.BlockIndex, emptyStart);
        double rowY = canvas.TestGetLineYPosition(vi);
        double lineH = LineHeight(canvas, vi);
        (canvas.TestCursorY - rowY).Should().BeApproximately(0, Eps);
        canvas.TestCaretHeight.Should().BeApproximately(lineH, Eps);

        var edges = ColumnEdges(canvas.TestTableColumnWidths(vl.BlockIndex)!);
        double caretX = canvas.TestCursorX;
        caretX.Should().BeInRange(edges[1], edges[2]);

        // Below the empty cell's only line, in a row two or more lines tall.
        canvas.HitTestToPosition(new Point((edges[1] + edges[2]) / 2, rowY + lineH * 1.5), out int block, out int offset);
        block.Should().Be(vl.BlockIndex);
        offset.Should().Be(emptyStart, "a click anywhere in an empty cell puts the caret in it");
    }

    // --- 9. Up and Down move one line of a wrapped cell at a time ---

    [StaFact]
    public void Down_MovesALineWithinTheCell_KeepingX_ThenLeavesForTheNextRow()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        var rows = TableRows(canvas);
        var vl = canvas.TestVisualLines[rows[1]];
        var layout = vl.TableLayout!;
        int lines = layout.CellLineCount(1);
        double rowY = canvas.TestGetLineYPosition(rows[1]);
        double lineH = LineHeight(canvas, rows[1]);

        canvas.TestSetCursor(vl.BlockIndex, layout.LineStarts[1][0] + 8);
        double x = canvas.TestCursorX;

        for (int k = 1; k < lines; k++)
        {
            canvas.TestNavigate(System.Windows.Input.Key.Down);
            canvas.TestCursorBlock.Should().Be(vl.BlockIndex, $"line {k} is still in the same cell");
            (canvas.TestCursorY - rowY).Should().BeApproximately(k * lineH, Eps);
            if (k < lines - 1)
                canvas.TestCursorX.Should().BeApproximately(x, 12, "the caret keeps its X from line to line");
        }

        canvas.TestNavigate(System.Windows.Input.Key.Down);
        canvas.TestCursorBlock.Should().Be(canvas.TestVisualLines[rows[2]].BlockIndex, "from the cell's last line Down goes to the next row");
        canvas.TestCursorY.Should().BeApproximately(canvas.TestGetLineYPosition(rows[2]), Eps);
    }

    [StaFact]
    public void Up_FromTheRowBelow_LandsOnTheCellsBottomLine_AndUpFromItsTopLeavesTheRow()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        var rows = TableRows(canvas);
        var vl = canvas.TestVisualLines[rows[1]];
        var layout = vl.TableLayout!;
        int lines = layout.CellLineCount(1);
        double rowY = canvas.TestGetLineYPosition(rows[1]);
        double lineH = LineHeight(canvas, rows[1]);

        var below = canvas.TestVisualLines[rows[2]];
        string belowText = canvas.TestGetBlockText(below.BlockIndex);
        canvas.TestSetCursor(below.BlockIndex, belowText.LastIndexOf("short", StringComparison.Ordinal) + 2);

        canvas.TestNavigate(System.Windows.Input.Key.Up);
        canvas.TestCursorBlock.Should().Be(vl.BlockIndex);
        (canvas.TestCursorY - rowY).Should().BeApproximately((lines - 1) * lineH, Eps, "arriving from below lands on the bottom line");

        for (int k = lines - 2; k >= 0; k--)
        {
            canvas.TestNavigate(System.Windows.Input.Key.Up);
            canvas.TestCursorBlock.Should().Be(vl.BlockIndex);
            (canvas.TestCursorY - rowY).Should().BeApproximately(k * lineH, Eps);
        }

        canvas.TestNavigate(System.Windows.Input.Key.Up);
        canvas.TestCursorBlock.Should().Be(canvas.TestVisualLines[rows[0]].BlockIndex, "from the top line Up goes to the header");
    }

    [StaFact]
    public void DownThenUp_ThroughAShortRow_ReturnsToTheGoalX()
    {
        var canvas = MakeCanvasAt(WrappedRowTable(), WrapWidth);
        var rows = TableRows(canvas);
        var vl = canvas.TestVisualLines[rows[1]];
        var layout = vl.TableLayout!;
        int last = layout.CellLineCount(1) - 1;
        double rowY = canvas.TestGetLineYPosition(rows[1]);
        double lineH = LineHeight(canvas, rows[1]);

        // Far along the cell's bottom line, well past where "short" in the row below ends.
        var (ls, le) = layout.GetLine(1, last);
        canvas.TestSetCursor(vl.BlockIndex, ls + Math.Max(0, (le - ls) - 2));
        double x = canvas.TestCursorX;

        canvas.TestNavigate(System.Windows.Input.Key.Down);
        canvas.TestCursorBlock.Should().Be(canvas.TestVisualLines[rows[2]].BlockIndex);
        canvas.TestNavigate(System.Windows.Input.Key.Up);

        canvas.TestCursorBlock.Should().Be(vl.BlockIndex);
        (canvas.TestCursorY - rowY).Should().BeApproximately(last * lineH, Eps);
        canvas.TestCursorX.Should().BeApproximately(x, 12, "the goal X survives the short row in between");
    }

    // --- 11. A page is a page, however tall the rows ---

    [StaFact]
    public void PageDown_PastTallRows_MovesAWholePage()
    {
        var sb = new System.Text.StringBuilder("| Element | Contents |\n|---|---|\n");
        for (int r = 0; r < 30; r++) sb.Append($"| Row{r} | {LongProse(50)} |\n");
        var canvas = MakeCanvasAt(sb.ToString(), WrapWidth);
        var rows = TableRows(canvas);
        var first = canvas.TestVisualLines[rows[1]];
        first.TableLayout!.LineCount.Should().BeGreaterThan(3, "rows several lines tall are the case that shrank the page");
        double lineH = LineHeight(canvas, rows[1]);

        canvas.TestSetCursor(first.BlockIndex, first.TableLayout.LineStarts[1][0] + 4);
        double before = canvas.TestCursorY;

        canvas.TestNavigate(System.Windows.Input.Key.PageDown);

        (canvas.TestCursorY - before).Should().BeGreaterThanOrEqualTo(CanvasHeight - 3 * lineH - lineH,
            "a page is the viewport less three lines of text, not less three rows");
    }

    // --- 16. Moving into a row, the cell under X decides the line ---

    [StaFact]
    public void Up_IntoARowWhoseCellIsShorterThanTheRow_LandsOnThatCellsLastLine()
    {
        string md = $"| Key | Short | Long |\n|---|---|---|\n| k | tiny | {LongProse(60)} |\n| m | below | x |";
        var canvas = MakeCanvasAt(md, WrapWidth);
        var rows = TableRows(canvas);
        var tall = canvas.TestVisualLines[rows[1]];
        tall.TableLayout!.LineCount.Should().BeGreaterThan(2);
        tall.TableLayout.CellLineCount(1).Should().Be(1);
        double rowY = canvas.TestGetLineYPosition(rows[1]);

        var below = canvas.TestVisualLines[rows[2]];
        string belowText = canvas.TestGetBlockText(below.BlockIndex);
        canvas.TestSetCursor(below.BlockIndex, belowText.IndexOf("below", StringComparison.Ordinal) + 2);

        canvas.TestNavigate(System.Windows.Input.Key.Up);

        canvas.TestCursorBlock.Should().Be(tall.BlockIndex);
        canvas.TestCursorOffset.Should().BeInRange(tall.TableLayout.LineStarts[1][0], tall.TableLayout.CellEnds[1],
            "the caret stays in the column it came from");
        (canvas.TestCursorY - rowY).Should().BeApproximately(0, Eps, "the short cell's last line is its first");
    }

    // --- 18, continued. Up and Down reach an empty cell ---

    [StaFact]
    public void EmptyCell_InAWrappedRow_IsReachedByUpAndLeftByDown()
    {
        string md = $"| Key | Note | Contents |\n|---|---|---|\n| k |  | {LongProse(60)} |\n| m | here | x |";
        var canvas = MakeCanvasAt(md, WrapWidth);
        var rows = TableRows(canvas);
        var wrapped = canvas.TestVisualLines[rows[1]];
        int emptyStart = wrapped.TableLayout!.LineStarts[1][0];

        var below = canvas.TestVisualLines[rows[2]];
        string belowText = canvas.TestGetBlockText(below.BlockIndex);
        canvas.TestSetCursor(below.BlockIndex, belowText.IndexOf("here", StringComparison.Ordinal));

        canvas.TestNavigate(System.Windows.Input.Key.Up);
        canvas.TestCursorBlock.Should().Be(wrapped.BlockIndex);
        canvas.TestCursorOffset.Should().Be(emptyStart, "Up from below lands in the empty cell above");

        canvas.TestNavigate(System.Windows.Input.Key.Down);
        canvas.TestCursorBlock.Should().Be(below.BlockIndex, "an empty cell has one line, so Down leaves the row");
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
