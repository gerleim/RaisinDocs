using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class TableCursorTests
{
    private const string N = "\n";

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

    [StaFact]
    public void SetEditMode_ToVisual_PreservesCursorInSecondTableCell()
    {
        // Source: "|  one | two |" — cursor at 't' in "two" (offset 9)
        // Switching to visual mode must keep cursor in the second cell.
        var canvas = CreateCanvas("| Header 1 | Header 2 |\n| --- | --- |\n|  one | two |",
            DocsCanvas.EditMode.Source);
        canvas.TestSetCursor(2, 9); // 't' in "two"

        canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().BeInRange(9, 12,
            "cursor should remain within the second cell content (offsets 9–12)");
    }

    [StaFact]
    public void SetEditMode_ToVisual_PreservesCursorAtEndOfFirstCell()
    {
        // Cursor at end of "one" content (offset 6) — in the inter-cell hidden zone.
        // Must clamp back to cell 1, not skip forward to cell 2.
        var canvas = CreateCanvas("| Header 1 | Header 2 |\n| --- | --- |\n|  one | two |",
            DocsCanvas.EditMode.Source);
        canvas.TestSetCursor(2, 6); // end of "one"

        canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().BeInRange(3, 6,
            "cursor should remain within the first cell content (offsets 3–6)");
    }

    [StaFact]
    public void SetEditMode_ToVisual_PreservesCursorAtEndOfLastCell()
    {
        // Cursor at end of "two" content (offset 12) — sits at boundary of trailing hidden region.
        var canvas = CreateCanvas("| Header 1 | Header 2 |\n| --- | --- |\n|  one | two |",
            DocsCanvas.EditMode.Source);
        canvas.TestSetCursor(2, 12); // end of "two"

        canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().BeInRange(9, 12,
            "cursor should clamp to second cell, not jump to first cell");
    }

    // --- Backspace/Delete: general text-changed handler must not jump cells ---

    [StaFact]
    public void Backspace_AtEndOfCell_CursorStaysInSameCell()
    {
        // "| ab | cd |" — cursor at offset 4 (end of cell 1 "ab").
        // Backspace deletes 'b', leaving "| a | cd |". Cursor must stay in cell 1.
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n| ab | cd |");
        canvas.TestSetCursor(2, 4); // end of "ab"

        canvas.TestNavigate(Key.Back);

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestGetBlockText(2).Should().Be("| a | cd |");
        canvas.TestCursorOffset.Should().BeInRange(2, 3,
            "cursor should stay in cell 1 after backspace");
    }

    [StaFact]
    public void Backspace_InMiddleOfSecondCell_CursorStaysInSecondCell()
    {
        // "| ab | cd |" — cursor at offset 8 ('d'). Backspace deletes 'c'.
        // After: "| ab | d |". Cursor must remain in cell 2.
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n| ab | cd |");
        canvas.TestSetCursor(2, 8); // 'd' in "cd"

        canvas.TestNavigate(Key.Back);

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestGetBlockText(2).Should().Be("| ab | d |");
        canvas.TestCursorOffset.Should().BeInRange(7, 8,
            "cursor should stay in cell 2 after backspace");
    }

    // --- Right-arrow collapse selection must not jump cells ---

    [StaFact]
    public void RightCollapseSelection_AtEndOfCell_CursorStaysInCell()
    {
        // Selection spans "ab" in cell 1: anchor=2, cursor=4.
        // Pressing Right (no shift) should collapse to offset 4 (end of cell 1).
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n| ab | cd |");
        canvas.TestSetSelection(2, 2, 2, 4); // select "ab"

        canvas.TestNavigate(Key.Right);

        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().BeInRange(2, 4,
            "collapsing right should stay within cell 1");
    }

    // --- Ctrl+Right: word navigation should clamp to table cell ---

    // --- Backspace/Delete at cell boundary must not cross into adjacent cell ---

    [StaFact]
    public void Backspace_AtStartOfSecondCell_IsNoop()
    {
        // Cursor at 't' in "two" (offset 9 = start of cell 2 content).
        // Backspace must not delete from cell 1.
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n|  one | two |");
        canvas.TestSetCursor(2, 9); // 't' in "two"

        canvas.TestNavigate(Key.Back);

        canvas.TestGetBlockText(2).Should().Be("|  one | two |",
            "backspace at start of cell should not modify text");
        canvas.TestCursorOffset.Should().Be(9);
    }

    [StaFact]
    public void Backspace_AtStartOfFirstCell_IsNoop()
    {
        // Cursor at 'o' in "one" (offset 3 = start of cell 1 content).
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n|  one | two |");
        canvas.TestSetCursor(2, 3); // 'o' in "one"

        canvas.TestNavigate(Key.Back);

        canvas.TestGetBlockText(2).Should().Be("|  one | two |",
            "backspace at start of first cell should not modify text");
        canvas.TestCursorOffset.Should().Be(3);
    }

    [StaFact]
    public void Delete_AtEndOfFirstCell_IsNoop()
    {
        // Cursor at end of "one" (offset 6 = TrimContent end of cell 1).
        // Delete must not remove from cell 2.
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n|  one | two |");
        canvas.TestSetCursor(2, 6); // end of "one"

        canvas.TestNavigate(Key.Delete);

        canvas.TestGetBlockText(2).Should().Be("|  one | two |",
            "delete at end of cell should not modify text");
        canvas.TestCursorOffset.Should().Be(6);
    }

    [StaFact]
    public void Delete_AtEndOfLastCell_IsNoop()
    {
        // Cursor at end of "two" (offset 12 = TrimContent end of cell 2).
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n|  one | two |");
        canvas.TestSetCursor(2, 12); // end of "two"

        canvas.TestNavigate(Key.Delete);

        canvas.TestGetBlockText(2).Should().Be("|  one | two |",
            "delete at end of last cell should not modify text");
        canvas.TestCursorOffset.Should().Be(12);
    }

    [StaFact]
    public void CtrlRight_FromWithinCell_CursorLandsOnValidPosition()
    {
        // "| ab | cd |" — cursor at 'a' (offset 2). Ctrl+Right jumps past word "ab".
        // Cursor must land on a valid cell position (not in a hidden zone).
        // MoveWordRight may cross to the next cell's word — that's acceptable.
        var canvas = CreateCanvas("| H1 | H2 |\n|---|---|\n| ab | cd |");
        canvas.TestSetCursor(2, 2); // 'a'

        canvas.TestNavigate(Key.Right, ctrl: true);

        canvas.TestCursorBlock.Should().Be(2);
        // Valid positions: cell 1 content (2–4) or cell 2 content (7–9)
        var offset = canvas.TestCursorOffset;
        ((offset >= 2 && offset <= 4) || (offset >= 7 && offset <= 9)).Should().BeTrue(
            $"cursor at offset {offset} should be within a cell's content range");
    }

    private static int FindVisualLineForBlock(DocsCanvas canvas, int blockIndex)
    {
        for (int vi = 0; vi < canvas.TestVisualLineCount; vi++)
            if (canvas.TestGetVisualLineBlockIndex(vi) == blockIndex)
                return vi;
        throw new InvalidOperationException($"No visual line for block {blockIndex}");
    }

    [StaFact]
    public void Click_InTableDataRow_RoundTripsToTheClickedOffset()
    {
        // Columns are far wider than the data row's content, so a hit test that walks the
        // raw text instead of the table layout drifts several columns to the right.
        // | AEHL | +$213.77 | 1h 2m |
        //  0123456789...
        var canvas = CreateCanvas(
            "| Symbol | Max Gain/Loss | Avg Hold |" + N +
            "|---|---|---|" + N +
            "| AEHL | +$213.77 | 1h 2m |");

        int vi = FindVisualLineForBlock(canvas, 2);
        double y = canvas.TestGetLineYPosition(vi) + 2;

        // Interior offsets of each cell's trimmed content (cell edges are ambiguous).
        foreach (int offset in new[] { 2, 3, 4, 5, 9, 11, 13, 16, 20, 21, 24 })
        {
            canvas.TestSetCursor(2, offset);
            double x = canvas.TestCursorX;

            canvas.HitTestToPosition(new Point(x, y), out int hitBlock, out int hitOffset);

            hitBlock.Should().Be(2);
            hitOffset.Should().Be(offset,
                $"a click at the caret position of offset {offset} must land back on it");
        }
    }

    [StaFact]
    public void Click_PastTheEndOfACell_StaysInThatCell()
    {
        // Clicking in the empty space to the right of a cell's text must keep the caret
        // in that cell, not fall through to the last column.
        var canvas = CreateCanvas(
            "| Symbol | Max Gain/Loss | Avg Hold |" + N +
            "|---|---|---|" + N +
            "| AEHL | +$213.77 | 1h 2m |");

        int vi = FindVisualLineForBlock(canvas, 2);
        double y = canvas.TestGetLineYPosition(vi) + 2;

        // Caret at the end of "+$213.77" (offset 17), then click a little further right —
        // still inside the "Max Gain/Loss" column, which is padded out by the header.
        canvas.TestSetCursor(2, 17);
        double endOfCellX = canvas.TestCursorX;

        canvas.HitTestToPosition(new Point(endOfCellX + 12, y), out int hitBlock, out int hitOffset);

        hitBlock.Should().Be(2);
        hitOffset.Should().Be(17, "a click in the cell's trailing space belongs to that cell");
    }
}
