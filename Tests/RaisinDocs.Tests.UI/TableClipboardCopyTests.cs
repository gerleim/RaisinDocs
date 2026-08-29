using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Covers the selection → clipboard payload path: the plain-text slot keeps markdown,
/// while table selections also get a CF_HTML &lt;table&gt; that Excel pastes as cells.
/// </summary>
public class TableClipboardCopyTests
{
    private const string TestTable =
        "| Shortcut | Action |\n|---|---|\n| Ctrl+B | Toggle bold |\n| Ctrl+I | Toggle italic |";
    //  block 0: header       block 1: separator  block 2: data row 1       block 3: data row 2

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

    // --- Whole-table selection ---

    [StaFact]
    public void SelectingWholeTable_ProducesHtmlTable()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetSelection(0, 0, 3, 24);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain("<table ");
        html.Should().Contain(">Shortcut<").And.Contain(">Toggle italic<");
    }

    [StaFact]
    public void SelectingWholeTable_KeepsMarkdownInTextSlot()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetSelection(0, 0, 3, 24);

        var (text, _) = canvas.TestBuildClipboardPayload();

        // in visual mode this is a column rect covering every column, so the text slot gets
        // the normalized pipe rows (separator omitted) that rect copy has always produced
        text.Should().StartWith("| Shortcut | Action |");
        text.Should().Contain("| Ctrl+I | Toggle italic |");
    }

    [StaFact]
    public void SourceModeWholeTableSelection_KeepsRawMarkdownInTextSlot()
    {
        var canvas = CreateCanvas(TestTable, DocsCanvas.EditMode.Source);
        canvas.TestSetSelection(0, 0, 3, 24);

        var (text, _) = canvas.TestBuildClipboardPayload();

        text.Should().StartWith("| Shortcut | Action |");
        text.Should().Contain("|---|---|");
    }

    [StaFact]
    public void SelectionEndingAtStartOfNextLine_DoesNotPullInThatLine()
    {
        var canvas = CreateCanvas(TestTable + "\n\nTrailing paragraph");
        // drag from the header through to the very start of the blank line after the table
        canvas.TestSetSelection(0, 0, 4, 0);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain("<table ");
        html.Should().NotContain("Trailing paragraph");
    }

    [StaFact]
    public void SelectionSpanningTableAndParagraph_FallsBackToPlainText()
    {
        var canvas = CreateCanvas(TestTable + "\n\nTrailing paragraph");
        canvas.TestSetSelection(0, 0, 5, 18);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().BeNull();
    }

    [StaFact]
    public void SelectionWithinASingleRow_FallsBackToPlainText()
    {
        var canvas = CreateCanvas(TestTable);
        // inside one cell of one row — an ordinary text copy
        canvas.TestSetSelection(2, 11, 2, 15);

        var (text, html) = canvas.TestBuildClipboardPayload();

        html.Should().BeNull();
        text.Should().Be("Togg");
    }

    [StaFact]
    public void TableCopy_WorksInSourceMode()
    {
        var canvas = CreateCanvas(TestTable, DocsCanvas.EditMode.Source);
        canvas.TestSetSelection(0, 0, 3, 24);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain("<table ");
    }

    // --- Rectangular (column) selection ---

    [StaFact]
    public void RectSelection_EmitsOnlySelectedColumn()
    {
        var canvas = CreateCanvas(TestTable);
        // Action column across both data rows
        canvas.TestSetSelection(2, 10, 3, 10);

        var (text, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain(">Toggle bold<").And.Contain(">Toggle italic<");
        html.Should().NotContain("Ctrl+B");
        // plain-text slot keeps the existing pipe form
        text.Should().Be("| Toggle bold |\r\n| Toggle italic |");
    }

    [StaFact]
    public void RectSelection_WithinOneRow_StillEmitsHtmlTable()
    {
        var canvas = CreateCanvas(TestTable);
        // both columns of a single row — an explicit column rect, so a grid copy is intended
        canvas.TestSetSelection(2, 2, 2, 10);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain(">Ctrl+B<").And.Contain(">Toggle bold<");
    }

    [StaFact]
    public void RectSelection_IncludingHeader_EmitsHeaderCells()
    {
        var canvas = CreateCanvas(TestTable);
        canvas.TestSetSelection(0, 2, 2, 2);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain("<th").And.Contain(">Shortcut<");
        html.Should().NotContain(">Action<");
    }

    // --- Non-table content is unaffected ---

    [StaFact]
    public void PlainParagraphSelection_ProducesNoTableHtml()
    {
        var canvas = CreateCanvas("First paragraph\n\nSecond paragraph");
        canvas.TestSetSelection(0, 0, 2, 16);

        var (text, html) = canvas.TestBuildClipboardPayload();

        (html ?? "").Should().NotContain("<table");
        text.Should().Contain("First paragraph");
    }

    [StaFact]
    public void BoldParagraphSelection_StillUsesTheExistingHtmlCopyOut()
    {
        var canvas = CreateCanvas("Some **bold** text\n\nMore text");
        canvas.TestSetSelection(0, 0, 2, 9);

        var (_, html) = canvas.TestBuildClipboardPayload();

        html.Should().NotBeNull();
        html.Should().Contain("font-weight:bold;");
        html.Should().NotContain("<table");
    }
}
