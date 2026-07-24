using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class VisualModeTypingTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    // --- Typing into list items should not delete the prefix ---

    [StaFact]
    public void Type_IntoNewUnorderedListItem_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("- a");
        canvas.TestGetBlockText(0).Should().Be("- a");
    }

    [StaFact]
    public void Type_IntoNewOrderedListItem_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("1. a");
        canvas.TestGetBlockText(0).Should().Be("1. a");
    }

    [StaFact]
    public void Type_IntoNewTaskListItem_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("- [ ] a");
        canvas.TestGetBlockText(0).Should().Be("- [ ] a");
    }

    [StaFact]
    public void Type_IntoNewHeading_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("# a");
        canvas.TestGetBlockText(0).Should().Be("# a");
    }

    [StaFact]
    public void Type_IntoNewH2_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("## a");
        canvas.TestGetBlockText(0).Should().Be("## a");
    }

    // --- Multiple characters after prefix ---

    [StaFact]
    public void Type_MultipleCharsAfterListPrefix_AllPreserved()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("- hello");
        canvas.TestGetBlockText(0).Should().Be("- hello");
    }

    [StaFact]
    public void Type_MultipleCharsAfterHeadingPrefix_AllPreserved()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("# hello");
        canvas.TestGetBlockText(0).Should().Be("# hello");
    }

    // --- No ghost selection after typing prefix ---

    [StaFact]
    public void Type_ListPrefix_NoGhostSelection()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("- ");
        canvas.TestAnchorBlock.Should().Be(canvas.TestCursorBlock);
        canvas.TestAnchorOffset.Should().Be(canvas.TestCursorOffset);
    }

    [StaFact]
    public void Type_HeadingPrefix_NoGhostSelection()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("# ");
        canvas.TestAnchorBlock.Should().Be(canvas.TestCursorBlock);
        canvas.TestAnchorOffset.Should().Be(canvas.TestCursorOffset);
    }

    // --- Typing at end of existing list item ---

    [StaFact]
    public void Type_AtEndOfExistingListItem_AppendsText()
    {
        var canvas = CreateCanvas("- existing");
        canvas.TestSetCursor(0, 10);
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("- existingX");
    }

    // --- Blockquote prefix ---

    [StaFact]
    public void Type_IntoNewBlockquote_PreservesPrefix()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("> a");
        canvas.TestGetBlockText(0).Should().Be("> a");
    }

    // --- Trailing hard break edge case ---

    [StaFact]
    public void Type_AtEndOfLineWithTrailingBackslash_InsertsBeforeBackslash()
    {
        // Start in visual mode with "Line1\"
        var canvas = CreateCanvas("Line1\\");

        // Set cursor at end (after the backslash)
        canvas.TestSetCursor(0, 6);  // "Line1\" has length 6

        // Type 'f'
        canvas.TestTypeText("f");

        // Result should be "Line1f\" not "Line1\f"
        canvas.TestGetBlockText(0).Should().Be("Line1f\\", "Character should be inserted before the trailing backslash hard break");
    }

    [StaFact]
    public void Type_AtEndOfLineWithTrailingSpaces_InsertsBeforeSpaces()
    {
        // Start in visual mode with "Line1  " (trailing spaces hard break)
        var canvas = CreateCanvas("Line1  ");

        // Set cursor at end (after the spaces)
        canvas.TestSetCursor(0, 7);

        // Type 'f'
        canvas.TestTypeText("f");

        // Result should be "Line1f  " not "Line1  f"
        canvas.TestGetBlockText(0).Should().Be("Line1f  ", "Character should be inserted before trailing spaces hard break");
    }
}
