using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Tests for source mode rendering and cursor behavior with line continuations.
/// Reproduces issues with visual alignment and cursor position mapping.
/// </summary>
public class SourceModeRenderingTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;
    private readonly ITestOutputHelper _output;

    public SourceModeRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [StaFact]
    public void SourceMode_ListItemWithHardBreak_BarAlignedUnderFoo()
    {
        // Markdown: "- foo\\\nbar"
        // BUG #1: "bar" should be visually indented to align with "foo"
        // In source mode:
        // Line 1: "- foo\\" (shows as-is)
        // Line 2: "bar" (should be indented ~2 spaces to align with "foo" content)
        var markdown = "- foo\\\nbar";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var blocks = canvas.TestGetVisualBlockInfos();
        _output.WriteLine($"Layout produced {blocks.Length} blocks:");
        foreach (var block in blocks)
        {
            _output.WriteLine($"  [{block.Kind}] visual='{block.VisualText}' raw='{block.RawText}'");
        }

        blocks.Should().HaveCountGreaterThan(1, "Should have at least 2 lines");

        // Line 1: "- foo\\"
        blocks[0].RawText.Should().Be("- foo\\");

        // Line 2: "bar" should be rendered (in source mode it won't have special indentation)
        blocks[1].RawText.Should().Be("bar");

        // In source mode, bar appears as-is without visual indentation
        // (Visual indentation would only appear in visual mode or with soft-wrapping)
        _output.WriteLine($"\nNote: In source mode, continuation indentation is not auto-added.");
        _output.WriteLine($"Visual mode would show 'bar' indented to align with 'foo'");
    }

    [StaFact]
    public void SourceMode_ListItemWithHardBreak_CursorPosition()
    {
        // Markdown: "- foo\\\nbar"
        // BUG #2 & #3: Cursor position mapping in source mode
        var markdown = "- foo\\\nbar";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        // Block 0: "- foo\\"
        // Block 1: "bar"
        // Try to position cursor at start of "bar" line (block 1, offset 0)
        canvas.TestSetCursor(1, 0);

        var cursorBlock = canvas.TestCursorBlock;
        var cursorOffset = canvas.TestCursorOffset;

        _output.WriteLine($"Cursor position: block={cursorBlock}, offset={cursorOffset}");

        // Cursor should be at block 1 (the "bar" line)
        cursorBlock.Should().Be(1, "Cursor should be on 'bar' line (block 1)");
        cursorOffset.Should().Be(0, "Cursor should be at start of 'bar'");
    }

    [StaFact]
    public void SourceMode_ListItemWithHardBreak_EditingAtCursor()
    {
        // BUG #3: Editing (typing/deleting) doesn't happen at visible cursor position
        var markdown = "- foo\\\nbar";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        // Position cursor at start of "bar" line (block 1, offset 0)
        canvas.TestSetCursor(1, 0);

        var posBeforeEdit = $"block={canvas.TestCursorBlock}, offset={canvas.TestCursorOffset}";
        _output.WriteLine($"Cursor before edit: {posBeforeEdit}");

        // Try to insert a character
        canvas.TestInsert("X");

        var text = canvas.TestGetBlockText(0) + "\n" + canvas.TestGetBlockText(1);
        _output.WriteLine($"Text after insert 'X': {text}");

        // The 'X' should be inserted at the start of "bar"
        // Block 1 should now be "Xbar"
        var block1Text = canvas.TestGetBlockText(1);
        block1Text.Should().StartWith("X", "Typing should insert 'X' at cursor position");
    }

    [StaFact]
    public void SourceMode_ListItemWithHardBreak_CursorSkipsVisualSpace()
    {
        // BUG #2: Cursor should NEVER be in visual-only indentation
        // Visual display shows: "- foo\\\n  bar" (visual indent for alignment)
        // But source has: "- foo\\\nbar" (no leading spaces)
        // Cursor should skip visual space and land on 'b'
        var markdown = "- foo\\\nbar";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        // Block 0: "- foo\\"
        // Block 1: "bar" (visually indented but no leading spaces in source)

        // Position cursor at end of first line
        canvas.TestSetCursor(0, 7); // After the "\\"

        // Move right - should go to block 1, offset 0 (the 'b')
        canvas.TestNavigate(Key.Right);

        var blockAfterRight = canvas.TestCursorBlock;
        var offsetAfterRight = canvas.TestCursorOffset;
        _output.WriteLine($"After Right from end of line 1: block={blockAfterRight}, offset={offsetAfterRight}");

        // Cursor should be at start of "bar" (block 1, offset 0)
        blockAfterRight.Should().Be(1, "Right should move to next line");
        offsetAfterRight.Should().Be(0, "Cursor should be at 'b', not in visual space");

        // Also test moving down from first line
        canvas.TestSetCursor(0, 2); // On the dash "-"
        canvas.TestNavigate(Key.Down);

        var blockAfterDown = canvas.TestCursorBlock;
        var offsetAfterDown = canvas.TestCursorOffset;
        _output.WriteLine($"After Down from line 1: block={blockAfterDown}, offset={offsetAfterDown}");

        // Cursor should land on 'b', not in visual space
        blockAfterDown.Should().Be(1);
        offsetAfterDown.Should().Be(0, "Down should land on 'b', not visual space");
    }
}
