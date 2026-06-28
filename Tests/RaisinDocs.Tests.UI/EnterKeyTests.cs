using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class EnterKeyTests
{
    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    // --- Enter at end of line with content below (original bug) ---

    [StaFact]
    public void Enter_AtEndOfLine_WithNextLine_InsertsOneParagraphBreak()
    {
        var canvas = CreateCanvas("asd\n123");
        canvas.TestSetCursor(0, 3); // end of "asd"
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestGetBlockText(2).Should().Be("123");
        canvas.TestCursorBlock.Should().Be(1);
    }

    [StaFact]
    public void Enter_AtEndOfLastLine_InsertsParagraphBreak()
    {
        var canvas = CreateCanvas("asd");
        canvas.TestSetCursor(0, 3);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestGetBlockText(2).Should().Be("");
        canvas.TestCursorBlock.Should().Be(2);
    }

    [StaFact]
    public void Enter_MidLine_InsertsParagraphBreakWithSeparator()
    {
        var canvas = CreateCanvas("asd123");
        canvas.TestSetCursor(0, 3);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestGetBlockText(2).Should().Be("123");
        canvas.TestCursorBlock.Should().Be(2);
    }

    // --- Strip trailing hard break on Enter ---

    [StaFact]
    public void Enter_StripsTrailingBackslash()
    {
        var canvas = CreateCanvas("asd\\");
        canvas.TestSetCursor(0, 4); // after backslash
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
    }

    [StaFact]
    public void Enter_StripsTrailingDoubleSpaces()
    {
        var canvas = CreateCanvas("asd  ");
        canvas.TestSetCursor(0, 5); // after trailing spaces
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
    }

    [StaFact]
    public void Enter_OnHeading_InsertsSingleBreak()
    {
        var canvas = CreateCanvas("# Heading");
        canvas.TestSetCursor(0, 9);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("# Heading");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestCursorBlock.Should().Be(1);
    }

    // --- Ctrl+Enter (soft break) ---

    [StaFact]
    public void CtrlEnter_InsertsSingleBreak()
    {
        var canvas = CreateCanvas("asd");
        canvas.TestSetCursor(0, 3);
        canvas.TestHandleEnter(ctrl: true);

        canvas.TestGetBlockText(0).Should().Be("asd");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestCursorBlock.Should().Be(1);
    }

    // --- Shift+Enter (hard break) ---

    [StaFact]
    public void ShiftEnter_AppendsBackslashAndSplits()
    {
        var canvas = CreateCanvas("asd");
        canvas.TestSetCursor(0, 3);
        canvas.TestHandleEnter(shift: true);

        canvas.TestGetBlockText(0).Should().Be("asd\\");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestCursorBlock.Should().Be(1);
    }

    [StaFact]
    public void ShiftEnter_DoesNotDuplicateExistingBackslash()
    {
        var canvas = CreateCanvas("asd\\");
        canvas.TestSetCursor(0, 4);
        canvas.TestHandleEnter(shift: true);

        canvas.TestGetBlockText(0).Should().Be("asd\\");
        canvas.TestGetBlockText(1).Should().Be("");
    }
}
