using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Pasting an Excel copy into an existing table. The clipboard HTML now converts to a
/// markdown table, which routes through the cell-fill path rather than being inserted
/// as raw text.
/// </summary>
public class ExcelPasteIntoTableTests
{
    private const string TestTable =
        "| Shortcut | Action |\n|---|---|\n| Ctrl+B | Toggle bold |\n| Ctrl+I | Toggle italic |";

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

    [StaFact]
    public void PastedMarkdownTable_FillsCellsInPlace()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 2); // first data row, first cell

        bool handled = canvas.TestTryPasteIntoTableCells("| Alt+X | Do thing |");

        handled.Should().BeTrue();
        canvas.GetText().Should().Contain("| Alt+X | Do thing |");
    }

    [StaFact]
    public void PastedSeparatorRow_IsNotWrittenIntoCells()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 2);

        // exactly the shape ConvertHtmlToMarkdown produces from an Excel copy
        bool handled = canvas.TestTryPasteIntoTableCells(
            "| A | B |\n| ---: | --- |\n| 1 | 2 |");

        handled.Should().BeTrue();
        var text = canvas.GetText();
        text.Should().NotContain("---:");
        text.Should().Contain("| A | B |");
        text.Should().Contain("| 1 | 2 |");
    }

    [StaFact]
    public void PastedPlainText_IsNotTreatedAsTableRows()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 2);

        canvas.TestTryPasteIntoTableCells("just some text").Should().BeFalse();
    }

    [StaFact]
    public void SeparatorOnlyPaste_IsRejected()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetCursor(2, 2);

        // nothing but syntax would leave the cells untouched, so the caller should
        // fall back to ordinary insertion rather than silently doing nothing
        canvas.TestTryPasteIntoTableCells("| --- | --- |").Should().BeFalse();
    }
}
