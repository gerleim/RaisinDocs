using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class SoftBreakTests
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

    // --- Basic soft break insertion ---

    [StaFact]
    public void SoftBreak_InsertText_ThenShiftEnter_CreatesInternalNewline()
    {
        var canvas = CreateCanvas("");
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("sad");

        // Simulate Shift+Enter (soft break - inserts newline)
        canvas.TestInsert("\n");

        canvas.TestTypeText("asd");

        // Block should contain a newline (soft break)
        canvas.TestGetBlockText(0).Should().Contain("\n");
        canvas.TestGetBlockText(0).Should().Be("sad\nasd");
    }

    // --- The bug scenario: type, soft break, type, move cursor, type ---

    [StaFact]
    public void SoftBreak_TypeMoveType_NoOffsetOverflow()
    {
        var canvas = CreateCanvas("");

        // Step 1: Type initial text
        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("ssad");

        // Step 2: Create soft break (Shift+Enter)
        canvas.TestInsert("\n");

        // Step 3: Type continuation text
        canvas.TestTypeText("assdddasddd");

        // Step 4: Move cursor to middle of text
        canvas.TestSetCursor(0, 5);  // Position after "ssad\n"

        // Step 5: Type more text - this used to crash with offset overflow
        canvas.TestTypeText("X");

        // Should not throw, text should be correct
        canvas.TestGetBlockText(0).Should().Be("ssad\nXassdddasddd");
    }

    // --- Cursor movement with soft breaks ---

    [StaFact]
    public void SoftBreak_MoveCursorToEndThenType()
    {
        var canvas = CreateCanvas("first\nsecond");

        // Move cursor to end of block
        int blockLen = canvas.TestGetBlockText(0).Length;
        canvas.TestSetCursor(0, blockLen);

        // Type at the end - should not crash
        canvas.TestTypeText("!");

        canvas.TestGetBlockText(0).Should().Be("first\nsecond!");
    }

    // --- Multiple soft breaks ---

    [StaFact]
    public void SoftBreak_MultipleSoftBreaks_NoOffsetIssues()
    {
        var canvas = CreateCanvas("");

        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("a");
        canvas.TestInsert("\n");
        canvas.TestTypeText("b");
        canvas.TestInsert("\n");
        canvas.TestTypeText("c");

        // Move to middle and type
        canvas.TestSetCursor(0, 3);  // After "a\nb"
        canvas.TestTypeText("X");

        canvas.TestGetBlockText(0).Should().Be("a\nbX\nc");
    }

    // --- Cursor at exact positions with soft breaks ---

    [StaFact]
    public void SoftBreak_CursorAtNewlinePosition_CanType()
    {
        var canvas = CreateCanvas("hello\nworld");

        // Cursor at the newline (position 5)
        canvas.TestSetCursor(0, 5);
        canvas.TestTypeText("X");

        // Should insert before newline
        canvas.TestGetBlockText(0).Should().Be("helloX\nworld");
    }

    // --- Soft breaks mixed with regular text operations ---

    [StaFact]
    public void SoftBreak_TypeDeleteAndType()
    {
        var canvas = CreateCanvas("test\ntext");

        // Move to position 8 (after "test\ntex")
        canvas.TestSetCursor(0, 8);

        // Type a character
        canvas.TestTypeText("X");

        canvas.TestGetBlockText(0).Should().Be("test\ntexXt");
    }

    // --- Soft break at the very end ---

    [StaFact]
    public void SoftBreak_TypeWithSoftBreakAtEnd_CanTypeAfter()
    {
        var canvas = CreateCanvas("line");

        canvas.TestSetCursor(0, 4);  // At end of "line"
        canvas.TestInsert("\n");

        // Now at position 5, type more
        canvas.TestTypeText("more");

        canvas.TestGetBlockText(0).Should().Be("line\nmore");
    }

    // --- Edge case: soft break after single character ---

    [StaFact]
    public void SoftBreak_SingleCharThenBreakThenType()
    {
        var canvas = CreateCanvas("");

        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("a");
        canvas.TestInsert("\n");
        canvas.TestTypeText("b");

        // Move back and type
        canvas.TestSetCursor(0, 1);
        canvas.TestTypeText("X");

        canvas.TestGetBlockText(0).Should().Be("aX\nb");
    }

    // --- Verify pilcrow is not editable ---

    [StaFact]
    public void SoftBreak_VisualRepresentation_PilcrowIsNotInText()
    {
        var canvas = CreateCanvas("sad\nasd");

        // The pilcrow is visual-only, not in the actual text
        // The actual text should have a newline, not a pilcrow
        canvas.TestGetBlockText(0).Should().Contain("\n");
        canvas.TestGetBlockText(0).Should().NotContain("¶");
    }
}
