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
    public void End_LandsInsideClosingMarker()
    {
        var canvas = CreateCanvas("text **bold**");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End);
        canvas.TestCursorOffset.Should().Be(11); // after 'd', before closing **
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

    // --- Typing at styled boundaries ---

    [StaFact]
    public void Type_AfterEndKey_ContinuesBold()
    {
        var canvas = CreateCanvas("text **bold**");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End);
        canvas.TestInsert("X");
        canvas.GetText().Should().Be("text **boldX**");
    }

    [StaFact]
    public void Type_AfterRightPastBold_ExitsBold()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 10); // after 'd'
        canvas.TestNavigate(Key.Right); // skip closing ** → offset 13
        canvas.TestInsert("X");
        canvas.GetText().Should().Be("text **bold**X end");
    }

    [StaFact]
    public void Type_AfterHomeOnBold_InsideStyle()
    {
        var canvas = CreateCanvas("**bold** end");
        canvas.TestSetCursor(0, 6);
        canvas.TestNavigate(Key.Home); // skip opening ** → offset 2
        canvas.TestInsert("X");
        canvas.GetText().Should().Be("**Xbold** end");
    }

    [StaFact]
    public void Type_AfterLeftPastBold_ExitsStyle()
    {
        var canvas = CreateCanvas("text **bold** end");
        canvas.TestSetCursor(0, 7); // 'b' of bold
        canvas.TestNavigate(Key.Left); // skip opening ** → offset 4
        canvas.TestInsert("X");
        canvas.GetText().Should().Be("textX **bold** end");
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

    // --- Image navigation ---

    [StaFact]
    public void Right_SkipsImageSyntax()
    {
        // "before ![alt](img.png) after"
        //  0123456                      7=!, 21=), 22=space
        var canvas = CreateCanvas("before ![alt](img.png) after");
        canvas.TestSetCursor(0, 6); // space before !
        canvas.TestNavigate(Key.Right);
        canvas.TestCursorOffset.Should().Be(22); // space after closing )
    }

    [StaFact]
    public void Left_SkipsImageSyntax()
    {
        var canvas = CreateCanvas("before ![alt](img.png) after");
        canvas.TestSetCursor(0, 22); // space after )
        canvas.TestNavigate(Key.Left);
        canvas.TestCursorOffset.Should().Be(6); // space before !
    }

    [StaFact]
    public void Home_SkipsImageAtStart()
    {
        var canvas = CreateCanvas("![alt](img.png) text");
        canvas.TestSetCursor(0, 18);
        canvas.TestNavigate(Key.Home);
        canvas.TestCursorOffset.Should().Be(15); // after image, first visible text position
    }

    [StaFact]
    public void End_SkipsImageAtEnd()
    {
        var canvas = CreateCanvas("text ![alt](img.png)");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End);
        canvas.TestCursorOffset.Should().Be(5); // space after "text", before hidden image
    }
}
