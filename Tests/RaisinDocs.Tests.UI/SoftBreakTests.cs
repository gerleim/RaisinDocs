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

    // --- Cursor positioning precision ---

    [StaFact]
    public void SoftBreak_CursorPositioning_BeforePilcrow()
    {
        var canvas = CreateCanvas("sad\nasd");

        // Set cursor before soft break
        canvas.TestSetCursor(0, 3);  // After "sad", before "\n"
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(3);
    }

    [StaFact]
    public void SoftBreak_CursorPositioning_AfterPilcrow()
    {
        var canvas = CreateCanvas("sad\nasd");

        // Set cursor after soft break (at start of next part)
        canvas.TestSetCursor(0, 4);  // After "\n", before "asd"
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(4);
    }

    [StaFact]
    public void SoftBreak_CursorPositioning_NotInsideNextCharacter()
    {
        // This is the key test: ensure cursor doesn't appear in middle of 's'
        var canvas = CreateCanvas("a\ns");

        // Set cursor to position after soft break
        canvas.TestSetCursor(0, 2);  // Should point to 's', not in middle of it

        // Verify position is at character boundary
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(2);

        // Text should be "a\ns", so offset 2 is the 's'
        canvas.TestGetBlockText(0).Should().Be("a\ns");
        canvas.TestGetBlockText(0)[2].Should().Be('s');
    }

    [StaFact]
    public void SoftBreak_CursorPositioning_MultipleBreaks()
    {
        var canvas = CreateCanvas("a\nb\nc");

        // Test cursor at each position
        canvas.TestSetCursor(0, 0);
        canvas.TestCursorOffset.Should().Be(0);  // 'a'

        canvas.TestSetCursor(0, 1);
        canvas.TestCursorOffset.Should().Be(1);  // '\n'

        canvas.TestSetCursor(0, 2);
        canvas.TestCursorOffset.Should().Be(2);  // 'b'

        canvas.TestSetCursor(0, 3);
        canvas.TestCursorOffset.Should().Be(3);  // '\n'

        canvas.TestSetCursor(0, 4);
        canvas.TestCursorOffset.Should().Be(4);  // 'c'
    }

    [StaFact]
    public void SoftBreak_CursorPositioning_TypeThenNavigate()
    {
        var canvas = CreateCanvas("");

        canvas.TestSetCursor(0, 0);
        canvas.TestTypeText("hello");
        canvas.TestInsert("\n");
        canvas.TestTypeText("world");

        // Now cursor should be at position 11 (after "hello\nworld")
        canvas.TestCursorOffset.Should().Be(11);

        // Move cursor back to after soft break
        canvas.TestSetCursor(0, 6);  // After "hello\n"
        canvas.TestCursorOffset.Should().Be(6);
        canvas.TestGetBlockText(0)[6].Should().Be('w');  // Should point to 'w'
    }

    // --- Visual cursor position (caret placement) ---

    [StaFact]
    public void SoftBreak_VisualCursorPosition_BeforeSoftBreak()
    {
        var canvas = CreateCanvas("hello\nworld");

        // Position cursor before soft break
        canvas.TestSetCursor(0, 5);  // After "hello", before "\n"

        // Get visual x position
        double xBefore = canvas.TestCursorX;

        // Position cursor after soft break
        canvas.TestSetCursor(0, 6);  // After "\n", before "world"

        double xAfter = canvas.TestCursorX;

        // Cursor x should advance significantly (by pilcrow + visual space width)
        // It should not be at the same position or only slightly advanced
        (xAfter - xBefore).Should().BeGreaterThan(0, "Cursor should move right after soft break");
    }

    [StaFact]
    public void SoftBreak_VisualCursorPosition_Consistency()
    {
        var canvas = CreateCanvas("a\nb\nc");

        // Get visual positions at each character
        canvas.TestSetCursor(0, 0);
        double x0 = canvas.TestCursorX;  // 'a'

        canvas.TestSetCursor(0, 2);
        double x2 = canvas.TestCursorX;  // 'b'

        canvas.TestSetCursor(0, 4);
        double x4 = canvas.TestCursorX;  // 'c'

        // Each position should be different (cursor moves across screen)
        x0.Should().NotBe(x2, "Cursor at 'b' should be at different x than 'a'");
        x2.Should().NotBe(x4, "Cursor at 'c' should be at different x than 'b'");
        x0.Should().BeLessThan(x2, "Text flows left-to-right");
        x2.Should().BeLessThan(x4, "Text flows left-to-right");
    }

    [StaFact]
    public void SoftBreak_VisualCursorPosition_NotAtStartOfNextChar()
    {
        var canvas = CreateCanvas("abc\nxyz");

        // Get cursor position at 'x' (first char after soft break)
        canvas.TestSetCursor(0, 4);  // Points to 'x'
        double xAtX = canvas.TestCursorX;

        // The cursor should be AT the start of 'x', not after the visual space
        // It should NOT be at the same position as if we had skipped the space

        // Get position at 'c' (before soft break)
        canvas.TestSetCursor(0, 3);  // Points to soft break
        double xAtBreak = canvas.TestCursorX;

        // There should be visible gap for the pilcrow + space between 'c' and 'x'
        (xAtX - xAtBreak).Should().BeGreaterThan(10, "Visual space should create visible gap between break and next char");
    }
}

public class HardBreakTests
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

    [StaFact]
    public void HardBreak_Backslash_SeparatesBlocks()
    {
        var canvas = CreateCanvas("a\\\nb");

        // Source has 2 blocks: "a\" and "b"
        canvas.TestBlockCount.Should().Be(2);

        // In visual mode, hard break should NOT merge blocks
        // Each block should be rendered as a separate visual block
        var blockInfos = canvas.TestGetVisualBlockInfos();

        // Should have exactly 2 visual blocks (not merged)
        blockInfos.Length.Should().Be(2, "Hard break should prevent block merging");
    }
}
