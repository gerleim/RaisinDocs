using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Copying a visual-mode selection. The markers are hidden, so the selection ends sit on either
/// side of them depending on how the caret got there; the copy must carry the formatting of the
/// selected text, and only that.
/// </summary>
public class VisualSelectionCopyTests
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

    /// <summary>
    /// Home, <paramref name="skip"/> presses of Right, then <paramref name="take"/> presses of
    /// Shift+Right - or Shift+End when <paramref name="take"/> is negative - and copy.
    /// </summary>
    private static string CopyAfterKeys(string text, int skip, int take,
        DocsCanvas.EditMode mode = DocsCanvas.EditMode.Visual)
    {
        var canvas = CreateCanvas(text, mode);
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.Home);
        for (int i = 0; i < skip; i++) canvas.TestNavigate(Key.Right);
        if (take < 0)
            canvas.TestNavigate(Key.End, shift: true);
        else
            for (int i = 0; i < take; i++) canvas.TestNavigate(Key.Right, shift: true);

        return canvas.TestBuildClipboardPayload().Text;
    }

    [StaFact]
    public void PartOfABoldWord_CopiesAsBold()
    {
        // "whole" shows in bold; "who" selected from it is bold on screen.
        CopyAfterKeys("**whole**", skip: 0, take: 3).Should().Be("**who**");
    }

    [StaFact]
    public void MiddleOfABoldWord_CopiesAsBold()
    {
        CopyAfterKeys("**bold** tail", skip: 1, take: 2).Should().Be("**ol**");
    }

    [StaFact]
    public void WholeLine_EndingInBold_KeepsTheClosingMarker()
    {
        // Shift+End stops before the hidden closing "**".
        CopyAfterKeys("2*3=6 **x**", skip: 0, take: -1).Should().Be("2*3=6 **x**");
    }

    [StaFact]
    public void WholeLine_StartingWithBold_KeepsTheOpeningMarker()
    {
        CopyAfterKeys("**bold** tail", skip: 0, take: -1).Should().Be("**bold** tail");
    }

    [StaFact]
    public void TextBeforeABoldWord_TakesNoneOfItsMarkers()
    {
        CopyAfterKeys("a **b** c", skip: 0, take: 2).Should().Be("a ");
    }

    [StaFact]
    public void JustTheBoldWord_TakesBothMarkers()
    {
        CopyAfterKeys("a **b** c", skip: 2, take: 1).Should().Be("**b**");
    }

    [StaFact]
    public void FromTheBoldWordOn_TakesItsOpeningMarker()
    {
        CopyAfterKeys("a **b** c", skip: 2, take: 3).Should().Be("**b** c");
    }

    [StaFact]
    public void NestedEmphasis_TakesEveryLevel()
    {
        // "b" is italic inside bold.
        CopyAfterKeys("**a *b* c**", skip: 2, take: 1).Should().Be("***b***");
    }

    [StaFact]
    public void WholeHeading_KeepsItsPrefix()
    {
        CopyAfterKeys("# Title *it*", skip: 0, take: -1).Should().Be("# Title *it*");
    }

    [StaFact]
    public void LinkText_CopiesAsTheLink()
    {
        CopyAfterKeys("see [link](http://x) now", skip: 4, take: 4).Should().Be("[link](http://x)");
    }

    [StaFact]
    public void PartOfLinkText_CopiesAsALink()
    {
        CopyAfterKeys("see [link](http://x) now", skip: 5, take: 2).Should().Be("[in](http://x)");
    }

    [StaFact]
    public void ColouredText_KeepsItsTags()
    {
        CopyAfterKeys("<!--@fg:red-->err<!--/@fg--> ok", skip: 0, take: 3)
            .Should().Be("<!--@fg:red-->err<!--/@fg-->");
    }

    [StaFact]
    public void PartOfColouredText_KeepsItsTags()
    {
        CopyAfterKeys("<!--@fg:red-->error<!--/@fg--> ok", skip: 1, take: 2)
            .Should().Be("<!--@fg:red-->rr<!--/@fg-->");
    }

    [StaFact]
    public void PlainText_IsCopiedAsIs()
    {
        CopyAfterKeys("2*3=6 plain", skip: 2, take: 5).Should().Be("3=6 p");
    }

    [StaFact]
    public void MultipleLines_EdgesBalancedAndMiddleWhole()
    {
        // Blank lines keep the paragraphs from merging into one block.
        var canvas = CreateCanvas("x **ab**\n\n**mid** line\n\n*cd* y");
        canvas.TestSetSelection(0, 5, 4, 2); // from before "b" of "ab" to after "c" of "cd"

        canvas.TestBuildClipboardPayload().Text.Should().Be("**b**\r\n\r\n**mid** line\r\n\r\n*c*");
    }

    [StaFact]
    public void SourceMode_CopiesExactlyTheSelectedCharacters()
    {
        // The markers are visible there: "who" inside "**whole**" is just those three characters.
        CopyAfterKeys("**whole**", skip: 2, take: 3, DocsCanvas.EditMode.Source).Should().Be("who");
    }
}
