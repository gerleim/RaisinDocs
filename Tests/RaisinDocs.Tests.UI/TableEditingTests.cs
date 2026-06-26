using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class TableEditingTests
{
    private const string TestTable =
        "| Shortcut | Action |\n|---|---|\n| Ctrl+B | Toggle bold |\n| Ctrl+I | Toggle italic |";
    //  block 0: header       block 1: separator  block 2: data row 1       block 3: data row 2

    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    // --- TryGetTableRectSelection ---

    [StaFact]
    public void RectSelection_SameCell_ReturnsNull()
    {
        var canvas = CreateCanvas(TestTable);
        // Select within "Toggle bold" (block 2, cell 1: offsets 11='T' to 15='l')
        canvas.TestSetSelection(2, 11, 2, 15);
        canvas.TestTryGetTableRectSelection().Should().BeNull();
    }

    [StaFact]
    public void RectSelection_DifferentCellsSameRow_ReturnsRect()
    {
        var canvas = CreateCanvas(TestTable);
        // Anchor in cell 0, cursor in cell 1 of same row
        canvas.TestSetSelection(2, 2, 2, 10);
        var rect = canvas.TestTryGetTableRectSelection();
        rect.Should().NotBeNull();
        rect!.Value.StartCol.Should().Be(0);
        rect.Value.EndCol.Should().Be(1);
        rect.Value.StartBlock.Should().Be(2);
        rect.Value.EndBlock.Should().Be(2);
    }

    [StaFact]
    public void RectSelection_DifferentRows_ReturnsRect()
    {
        var canvas = CreateCanvas(TestTable);
        // Anchor in block 2 cell 1, cursor in block 3 cell 1
        canvas.TestSetSelection(2, 10, 3, 10);
        var rect = canvas.TestTryGetTableRectSelection();
        rect.Should().NotBeNull();
        rect!.Value.StartCol.Should().Be(1);
        rect.Value.EndCol.Should().Be(1);
        rect.Value.StartBlock.Should().Be(2);
        rect.Value.EndBlock.Should().Be(3);
    }

    [StaFact]
    public void RectSelection_DifferentTables_ReturnsNull()
    {
        var canvas = CreateCanvas("| A |\n|---|\n| 1 |\ntext\n| B |\n|---|\n| 2 |");
        // Anchor in first table (block 2), cursor in second table (block 6)
        canvas.TestSetSelection(2, 2, 6, 2);
        canvas.TestTryGetTableRectSelection().Should().BeNull();
    }

    // --- GetTableRectSelectedText ---

    [StaFact]
    public void RectSelectedText_SingleColumn()
    {
        var canvas = CreateCanvas(TestTable);
        // Select Action column (cell 1) across both data rows
        canvas.TestSetSelection(2, 10, 3, 10);
        var text = canvas.TestGetTableRectSelectedText();
        text.Should().Be("| Toggle bold |\r\n| Toggle italic |");
    }

    [StaFact]
    public void RectSelectedText_BothColumns()
    {
        var canvas = CreateCanvas(TestTable);
        // Select both columns across both data rows
        canvas.TestSetSelection(2, 2, 3, 10);
        var text = canvas.TestGetTableRectSelectedText();
        text.Should().Be("| Ctrl+B | Toggle bold |\r\n| Ctrl+I | Toggle italic |");
    }

    [StaFact]
    public void RectSelectedText_IncludesHeader()
    {
        var canvas = CreateCanvas(TestTable);
        // Select from header to data row, column 0
        canvas.TestSetSelection(0, 2, 2, 2);
        var text = canvas.TestGetTableRectSelectedText();
        text.Should().Be("| Shortcut |\r\n| Ctrl+B |");
    }

    // --- ClearTableRectCells ---

    [StaFact]
    public void ClearCells_ReplacesWithSingleSpace()
    {
        var canvas = CreateCanvas(TestTable);
        // Select Action column across both data rows
        canvas.TestSetSelection(2, 10, 3, 10);
        canvas.TestClearTableRectCells().Should().BeTrue();
        canvas.TestGetBlockText(2).Should().Be("| Ctrl+B | |");
        canvas.TestGetBlockText(3).Should().Be("| Ctrl+I | |");
    }

    [StaFact]
    public void ClearCells_FirstColumn_PreservesSecond()
    {
        var canvas = CreateCanvas(TestTable);
        // Select Shortcut column across both data rows
        canvas.TestSetSelection(2, 2, 3, 2);
        canvas.TestClearTableRectCells().Should().BeTrue();
        canvas.TestGetBlockText(2).Should().Be("| | Toggle bold |");
        canvas.TestGetBlockText(3).Should().Be("| | Toggle italic |");
    }

    // --- HandleTableEnter ---

    [StaFact]
    public void Enter_InsertsRowBelowCurrent()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 5); // in first data row
        canvas.TestHandleTableEnter().Should().BeTrue();
        // New row should be block 3 (after current block 2)
        canvas.TestGetBlockText(3).Should().Be("|  |  |");
        canvas.TestCursorBlock.Should().Be(3);
    }

    [StaFact]
    public void Enter_OnHeader_InsertsAfterSeparator()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(0, 5); // in header
        canvas.TestHandleTableEnter().Should().BeTrue();
        // New row should be after separator (block 1), so at block 2
        canvas.TestGetBlockText(2).Should().Be("|  |  |");
        canvas.TestCursorBlock.Should().Be(2);
    }

    [StaFact]
    public void Enter_PreservesExistingRows()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 5);
        canvas.TestHandleTableEnter();
        // Original data rows should still exist (shifted by 1)
        canvas.TestGetBlockText(2).Should().Be("| Ctrl+B | Toggle bold |");
        canvas.TestGetBlockText(4).Should().Be("| Ctrl+I | Toggle italic |");
    }
}
