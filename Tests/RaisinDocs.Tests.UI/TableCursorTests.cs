using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class TableCursorTests
{
    private static DocsCanvas CreateCanvas(string text, DocsCanvas.EditMode mode = DocsCanvas.EditMode.Visual)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(mode);
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    [StaFact]
    public void CursorX_ProgressesStrictlyPerCharacter_InTableCell()
    {
        // | Case | Spec | Example |
        // Cell 2 ("Example"): raw offsets 16='E', 17='x', 18='a', 19='m', 20='p', 21='l', 22='e'
        var canvas = CreateCanvas("| Case | Spec | Example |\n|---|---|---|");

        int cellContentStart = 16; // 'E'
        int cellContentEnd = 23;   // past 'e'

        var xPositions = new double[cellContentEnd - cellContentStart + 1];
        for (int offset = cellContentStart; offset <= cellContentEnd; offset++)
        {
            canvas.TestSetCursor(0, offset);
            xPositions[offset - cellContentStart] = canvas.TestCursorX;
        }

        for (int i = 1; i < xPositions.Length; i++)
        {
            xPositions[i].Should().BeGreaterThan(xPositions[i - 1],
                $"cursor X at offset {cellContentStart + i} should be greater than at offset {cellContentStart + i - 1}");
        }
    }

    [StaFact]
    public void CursorX_ProgressesStrictlyPerCharacter_InFirstTableCell()
    {
        var canvas = CreateCanvas("| Case | Spec | Example |\n|---|---|---|");

        int cellContentStart = 2; // 'C'
        int cellContentEnd = 6;   // past 'e'

        var xPositions = new double[cellContentEnd - cellContentStart + 1];
        for (int offset = cellContentStart; offset <= cellContentEnd; offset++)
        {
            canvas.TestSetCursor(0, offset);
            xPositions[offset - cellContentStart] = canvas.TestCursorX;
        }

        for (int i = 1; i < xPositions.Length; i++)
        {
            xPositions[i].Should().BeGreaterThan(xPositions[i - 1],
                $"cursor X at offset {cellContentStart + i} should be greater than at offset {cellContentStart + i - 1}");
        }
    }

    [StaFact]
    public void CursorX_ProgressesStrictlyPerCharacter_InStyledTableCell()
    {
        // | Normal | **Bold** |
        // Cell 1: raw offsets — ' **Bold** ' → trimmed content starts at '**Bold**'
        var canvas = CreateCanvas("| Normal | **Bold** |\n|---|---|");

        // In visual mode, ** markers are hidden, but each visible char should still advance X
        int cellContentStart = 10; // first '*'
        int cellContentEnd = 18;   // past last '*'

        double prevX = -1;
        int advances = 0;
        for (int offset = cellContentStart; offset <= cellContentEnd; offset++)
        {
            canvas.TestSetCursor(0, offset);
            double x = canvas.TestCursorX;
            if (x > prevX) advances++;
            prevX = x;
        }

        // "Bold" = 4 visible chars, so we expect at least 5 distinct X positions (before B, after B, after o, after l, after d)
        advances.Should().BeGreaterOrEqualTo(5);
    }

    // --- Table cursor X must match FormattedText measurement ---
    // CursorXInTableRow uses char-by-char glyph advances (no kerning).
    // DrawTableRow renders with FormattedText (kerning/shaping applied).
    // The source-mode paragraph path in CursorXInVisualLine uses
    // FormattedText.BuildHighlightGeometry, so we compare against that
    // as the ground truth.

    [StaFact]
    public void CursorX_InTableCell_MatchesFormattedTextMeasurement()
    {
        // Same text as paragraph (source mode → FormattedText path)
        // vs in a table DATA row (visual mode → CursorXInTableRow).
        // Both Paragraph and TableRow use NormalTypeface + BaseFontSize.
        // Header rows use BoldTypeface so we compare against a data row.
        var canvas = CreateCanvas("Example\n| Header |\n|---|\n| Example |");

        // Measure paragraph in source mode (FormattedText.BuildHighlightGeometry)
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.TestComputeLayout();

        canvas.TestSetCursor(0, 0);
        double paraBase = canvas.TestCursorX;
        var paraDeltas = new double[8];
        for (int i = 0; i <= 7; i++)
        {
            canvas.TestSetCursor(0, i);
            paraDeltas[i] = canvas.TestCursorX - paraBase;
        }

        // Measure table data row in visual mode
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();

        // Block 3: "| Example |" (data row) → TrimContent gives offsets 2..9
        canvas.TestSetCursor(3, 2);
        double tableBase = canvas.TestCursorX;
        var tableDeltas = new double[8];
        for (int i = 0; i <= 7; i++)
        {
            canvas.TestSetCursor(3, 2 + i);
            tableDeltas[i] = canvas.TestCursorX - tableBase;
        }

        for (int i = 0; i <= 7; i++)
        {
            tableDeltas[i].Should().BeApproximately(paraDeltas[i], 0.5,
                $"table cell relative X at char {i} should match FormattedText measurement");
        }
    }
}
