using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Pasting in visual mode. Markers pasted into text that already has their formatting would
/// close that formatting instead of adding to it, so they are dropped.
/// </summary>
public class VisualSelectionPasteTests
{
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

    /// <summary>Moves the caret to line start, then <paramref name="right"/> × Right.</summary>
    private static void PlaceCaret(DocsCanvas canvas, int block, int right)
    {
        canvas.TestSetCursor(block, 0);
        canvas.TestNavigate(Key.Home);
        for (int i = 0; i < right; i++) canvas.TestNavigate(Key.Right);
    }

    private static string PasteAt(string text, int right, string pasted)
    {
        var canvas = CreateCanvas(text);
        PlaceCaret(canvas, 0, right);
        canvas.TestPaste(pasted);
        return canvas.GetText();
    }

    [StaFact]
    public void CopyFromABoldWord_PastedBackInsideIt_StaysOneBoldRun()
    {
        var canvas = CreateCanvas("**whole**");
        PlaceCaret(canvas, 0, 0);
        for (int i = 0; i < 3; i++) canvas.TestNavigate(Key.Right, shift: true);
        string copied = canvas.TestBuildClipboardPayload().Text;

        PlaceCaret(canvas, 0, 4); // between "l" and "e"
        canvas.TestPaste(copied);

        canvas.GetText().Should().Be("**wholwhoe**");
    }

    [StaFact]
    public void BoldIntoPlainText_KeepsItsMarkers()
    {
        PasteAt("abc", right: 1, "**who**").Should().Be("a**who**bc");
    }

    [StaFact]
    public void BoldItalicIntoBold_KeepsOnlyTheItalic()
    {
        PasteAt("**whole**", right: 4, "***x***").Should().Be("**whol*x*e**");
    }

    [StaFact]
    public void ItalicIntoBold_KeepsItsMarkers()
    {
        PasteAt("**whole**", right: 4, "*it*").Should().Be("**whol*it*e**");
    }

    [StaFact]
    public void AtTheEndOfABoldWord_CountsAsInsideIt()
    {
        // The caret at the end of the visible text sits before the hidden closing "**".
        var canvas = CreateCanvas("**whole**");
        PlaceCaret(canvas, 0, 0);
        canvas.TestNavigate(Key.End);
        canvas.TestPaste("**who**");

        canvas.GetText().Should().Be("**wholewho**");
    }

    [StaFact]
    public void SameColourIntoColour_DropsTheTags()
    {
        PasteAt("<!--@fg:red-->error<!--/@fg-->", right: 2, "<!--@fg:red-->x<!--/@fg-->")
            .Should().Be("<!--@fg:red-->erxror<!--/@fg-->");
    }

    [StaFact]
    public void OtherColourIntoColour_KeepsTheTags()
    {
        PasteAt("<!--@fg:red-->error<!--/@fg-->", right: 2, "<!--@fg:blue-->x<!--/@fg-->")
            .Should().Be("<!--@fg:red-->er<!--@fg:blue-->x<!--/@fg-->ror<!--/@fg-->");
    }

    [StaFact]
    public void MultipleLines_ArePastedAsTheyAre()
    {
        // A known gap, not a goal: splitting a bold run across lines breaks it whatever the
        // pasted markers do, as Enter inside it does. Only single-line pastes are adapted.
        var canvas = CreateCanvas("**whole**");
        PlaceCaret(canvas, 0, 4);
        canvas.TestPaste("**a**\n**b**");

        canvas.GetText().Should().Be("**whol**a**\r\n**b**e**");
    }

    [StaFact]
    public void OverAWholeBoldWord_PastesInPlainContext()
    {
        var canvas = CreateCanvas("a **b** c");
        PlaceCaret(canvas, 0, 2);
        canvas.TestNavigate(Key.Right, shift: true);

        canvas.TestPaste("**X**");

        canvas.GetText().Should().Be("a **X** c");
    }

    [StaFact]
    public void SourceMode_PastesExactlyTheText()
    {
        var canvas = CreateCanvas("**whole**", DocsCanvas.EditMode.Source);
        canvas.TestSetCursor(0, 6); // between "l" and "e"

        canvas.TestPaste("**who**");

        canvas.GetText().Should().Be("**whol**who**e**");
    }
}
