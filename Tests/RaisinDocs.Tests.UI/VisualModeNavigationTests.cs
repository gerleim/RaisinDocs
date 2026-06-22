using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class VisualModeNavigationTests
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

    // --- Left arrow skips hidden ranges ---

    [StaFact]
    public void Left_SkipsClosingBoldMarker()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 13); // space after closing **
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorOffset.Should().Be(10); // 'd' of bold
    }

    [StaFact]
    public void Left_SkipsOpeningBoldMarker()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 7); // 'b' of bold
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorOffset.Should().Be(4); // space before **
    }

    [StaFact]
    public void Left_CrossesBlockWhenStartIsHidden()
    {
        var canvas = CreateCanvas("alma\n**345**");
        canvas.TestSetCursor(1, 2); // '3' in block 1
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(4); // end of "alma"
    }

    [StaFact]
    public void Left_SkipsHeadingPrefix()
    {
        var canvas = CreateCanvas("plain\n# Heading");
        canvas.TestSetCursor(1, 2); // 'H' of Heading
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(5); // end of "plain"
    }

    // --- Right arrow skips hidden ranges ---

    [StaFact]
    public void Right_SkipsOpeningBoldMarker()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 4); // space before **
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorOffset.Should().Be(7); // 'b' of bold, skipped **
    }

    [StaFact]
    public void Right_SkipsClosingBoldMarker()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 10); // past 'd' of bold
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorOffset.Should().Be(13); // space after **, skipped **
    }

    [StaFact]
    public void Right_CrossesBlockWhenEndIsHidden()
    {
        var canvas = CreateCanvas("**345**\nalma");
        canvas.TestSetCursor(0, 4); // '5' in block 0
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(0); // start of "alma"
    }

    // --- End key lands on visible position ---

    [StaFact]
    public void End_SkipsTrailingHiddenMarker()
    {
        var canvas = CreateCanvas("text **bold**");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End);
        canvas.TestCursorOffset.Should().Be(10); // after 'd', not at 13
    }

    // --- Home key lands on visible position ---

    [StaFact]
    public void Home_SkipsHeadingPrefix()
    {
        var canvas = CreateCanvas("# Heading");
        canvas.TestSetCursor(0, 5);
        canvas.TestNavigate(Key.Home);
        canvas.TestCursorOffset.Should().Be(2); // 'H', skipped "# "
    }

    [StaFact]
    public void Home_SkipsOpeningBoldMarker()
    {
        var canvas = CreateCanvas("**bold** end");
        canvas.TestSetCursor(0, 8);
        canvas.TestNavigate(Key.Home);
        canvas.TestCursorOffset.Should().Be(2); // 'b', skipped **
    }

    // --- Navigation within visible text is unaffected ---

    [StaFact]
    public void Left_WithinVisibleText_MovesOnePosition()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 9); // 'l' of bold
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorOffset.Should().Be(8); // 'o' of bold
    }

    [StaFact]
    public void Right_WithinVisibleText_MovesOnePosition()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 8); // 'o' of bold
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorOffset.Should().Be(9); // 'l' of bold
    }

    // --- Italic and code markers ---

    [StaFact]
    public void Left_SkipsItalicMarker()
    {
        var canvas = CreateCanvas("text *italic* end");
        canvas.TestSetCursor(0, 6); // 'i' of italic
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorOffset.Should().Be(4); // space before *
    }

    [StaFact]
    public void Right_SkipsCodeBacktick()
    {
        var canvas = CreateCanvas("text `code` end");
        canvas.TestSetCursor(0, 4); // space before `
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorOffset.Should().Be(6); // 'c' of code
    }
}
