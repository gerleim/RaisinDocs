using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class ContinuationIndentTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas CreateCanvas(string text, DocsCanvas.EditMode mode = DocsCanvas.EditMode.Source)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(mode);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    private static double GetContentStartX(DocsCanvas canvas, int block)
    {
        canvas.TestSetEditMode(DocsCanvas.EditMode.Source);
        canvas.TestComputeLayout();
        canvas.TestSetCursor(block, 0);
        canvas.TestNavigate(Key.Home);
        canvas.TestNavigate(Key.Right, ctrl: true);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();
        return canvas.TestCursorX;
    }

    private static double GetLineStartX(DocsCanvas canvas, int block)
    {
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.TestComputeLayout();
        canvas.TestSetCursor(block, 0);
        canvas.TestNavigate(Key.Home);
        return canvas.TestCursorX;
    }

    // --- Lazy continuation: unordered list ---

    [StaFact]
    public void LazyContinuation_UnorderedList_AlignsWithContent()
    {
        var canvas = CreateCanvas("- First paragraph\nstill part of item");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    [StaFact]
    public void LazyContinuation_UnorderedListWithLeadingSpaces_AlignsWithContent()
    {
        var canvas = CreateCanvas("- dfgdf\n  sdslkdfjlsd");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetContentStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    [StaFact]
    public void LazyContinuation_UnorderedListWithExtraSpaces_IndentsBeyondContent()
    {
        // content column = 2, line has 5 spaces → 2 hidden, 3 visible
        var canvas = CreateCanvas("- dfgdf\n     sdslkdfjlsd");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetContentStartX(canvas, 1);

        contX.Should().BeGreaterThan(ownerX);
    }

    [StaFact]
    public void LazyContinuation_TaskList_AlignsWithContent()
    {
        var canvas = CreateCanvas("- [ ] First paragraph\nstill part of item");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    // --- Lazy continuation: ordered list ---

    [StaFact]
    public void LazyContinuation_OrderedList_AlignsWithContent()
    {
        var canvas = CreateCanvas("1. First paragraph\nstill part of item");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    [StaFact]
    public void LazyContinuation_OrderedListTwoDigit_AlignsWithContent()
    {
        var canvas = CreateCanvas("10. First paragraph\nstill part of item");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    // --- Lazy continuation: blockquote ---
    // Blockquotes don't have visual mode prefix handling yet (GetOwnerVisualPrefix returns null).
    // This test documents the gap — enable when blockquote visual mode is implemented.

    [StaFact(Skip = "Blockquote visual mode not yet implemented")]
    public void LazyContinuation_Blockquote_AlignsWithContent()
    {
        var canvas = CreateCanvas("> First paragraph\nstill part of quote");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    // --- Indented continuation after blank line ---

    [StaFact]
    public void IndentedContinuation_UnorderedList_AlignsWithContent()
    {
        var canvas = CreateCanvas("- First paragraph\n\n  Second paragraph");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 2);

        contX.Should().Be(ownerX);
    }

    [StaFact]
    public void IndentedContinuation_OrderedList_AlignsWithContent()
    {
        var canvas = CreateCanvas("1. First paragraph\n\n   Second paragraph");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 2);

        contX.Should().Be(ownerX);
    }

    [StaFact]
    public void IndentedContinuation_TaskList_AlignsWithContent()
    {
        var canvas = CreateCanvas("- [ ] First paragraph\n\n      Second paragraph");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 2);

        contX.Should().Be(ownerX);
    }

    // --- Prefix tolerance: leading spaces on owner ---

    [StaFact]
    public void PrefixTolerance_UnorderedListWithLeadingSpaces_AlignsWithContent()
    {
        var canvas = CreateCanvas("  - First paragraph\n  still part of item");

        double ownerX = GetContentStartX(canvas, 0);
        double contX = GetLineStartX(canvas, 1);

        contX.Should().Be(ownerX);
    }

    // --- Non-continuation: no alignment ---

    [StaFact]
    public void NoContinuation_IndependentParagraph_DoesNotAlign()
    {
        var canvas = CreateCanvas("- First paragraph\n\nSecond paragraph");

        double ownerX = GetContentStartX(canvas, 0);
        double paraX = GetLineStartX(canvas, 2);

        paraX.Should().BeLessThan(ownerX);
    }
}
