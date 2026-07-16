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

    // --- Enter at end of line ---

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

    // --- Enter between paragraphs ---

    [StaFact]
    public void Enter_AtEndOfFirstParagraph_WithBlankLineBefore_SecondParagraph_InsertsNewParagraph()
    {
        // "First paragraph\n\nSecond paragraph"
        var canvas = CreateCanvas("First paragraph\n\nSecond paragraph");
        canvas.TestSetCursor(0, 15); // end of "First paragraph"
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("First paragraph");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestGetBlockText(2).Should().Be(""); // cursor here
        canvas.TestGetBlockText(3).Should().Be("");
        canvas.TestGetBlockText(4).Should().Be("Second paragraph");
        canvas.TestCursorBlock.Should().Be(2);
    }

    [StaFact]
    public void Enter_AtEndOfLine_WithNextLine_InsertsNewParagraph()
    {
        var canvas = CreateCanvas("asd\n123");
        canvas.TestSetCursor(0, 3); // end of "asd"
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("asd");
        canvas.TestGetBlockText(1).Should().Be("");
        canvas.TestGetBlockText(2).Should().Be(""); // cursor here
        canvas.TestGetBlockText(3).Should().Be("123");
        canvas.TestCursorBlock.Should().Be(2);
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

    // --- Enter auto-continuation: bullet lists ---

    [StaFact]
    public void Enter_BulletDash_ContinuesWithPrefix()
    {
        var canvas = CreateCanvas("- item");
        canvas.TestSetCursor(0, 6);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("- item");
        canvas.TestGetBlockText(1).Should().Be("- ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(2);
    }

    [StaFact]
    public void Enter_BulletStar_ContinuesWithPrefix()
    {
        var canvas = CreateCanvas("* item");
        canvas.TestSetCursor(0, 6);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("* item");
        canvas.TestGetBlockText(1).Should().Be("* ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(2);
    }

    [StaFact]
    public void Enter_EmptyBullet_ClearsPrefix()
    {
        var canvas = CreateCanvas("- ");
        canvas.TestSetCursor(0, 2);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("");
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(0);
    }

    // --- Enter auto-continuation: task lists ---

    [StaFact]
    public void Enter_UncheckedTask_ContinuesUnchecked()
    {
        var canvas = CreateCanvas("- [ ] task");
        canvas.TestSetCursor(0, 10);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("- [ ] task");
        canvas.TestGetBlockText(1).Should().Be("- [ ] ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(6);
    }

    [StaFact]
    public void Enter_CheckedTask_ContinuesUnchecked()
    {
        var canvas = CreateCanvas("- [x] done");
        canvas.TestSetCursor(0, 10);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("- [x] done");
        canvas.TestGetBlockText(1).Should().Be("- [ ] ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(6);
    }

    [StaFact]
    public void Enter_EmptyTask_ClearsPrefix()
    {
        var canvas = CreateCanvas("- [ ] ");
        canvas.TestSetCursor(0, 6);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("");
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(0);
    }

    // --- Enter auto-continuation: blockquotes ---

    [StaFact]
    public void Enter_Blockquote_ContinuesWithPrefix()
    {
        var canvas = CreateCanvas("> quote");
        canvas.TestSetCursor(0, 7);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("> quote");
        canvas.TestGetBlockText(1).Should().Be("> ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(2);
    }

    [StaFact]
    public void Enter_EmptyBlockquote_ClearsPrefix()
    {
        var canvas = CreateCanvas("> ");
        canvas.TestSetCursor(0, 2);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("");
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(0);
    }

    [StaFact]
    public void Enter_BareBlockquote_ClearsPrefix()
    {
        var canvas = CreateCanvas(">");
        canvas.TestSetCursor(0, 1);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("");
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(0);
    }

    // --- Enter auto-continuation: indented lists ---

    [StaFact]
    public void Enter_IndentedBullet_PreservesIndentation()
    {
        var canvas = CreateCanvas("  - nested");
        canvas.TestSetCursor(0, 10);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("  - nested");
        canvas.TestGetBlockText(1).Should().Be("  - ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(4);
    }

    [StaFact]
    public void Enter_IndentedTask_PreservesIndentation()
    {
        var canvas = CreateCanvas("  - [x] nested task");
        canvas.TestSetCursor(0, 19);
        canvas.TestHandleEnter();

        canvas.TestGetBlockText(0).Should().Be("  - [x] nested task");
        canvas.TestGetBlockText(1).Should().Be("  - [ ] ");
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(8);
    }
}
