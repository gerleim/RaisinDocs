using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Deleting a visual-mode selection. The raw range between the selection ends can hold half a
/// pair of hidden markers; a construct whose text is all deleted must lose both, and any other
/// keep both.
/// </summary>
public class VisualSelectionDeleteTests
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

    /// <summary>Home, <paramref name="skip"/> × Right, then <paramref name="take"/> × Shift+Right (Shift+End if negative).</summary>
    private static DocsCanvas Select(string text, int skip, int take,
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
        return canvas;
    }

    private static string Backspace(string text, int skip, int take)
    {
        var canvas = Select(text, skip, take);
        canvas.TestNavigate(Key.Back);
        return canvas.GetText();
    }

    private static string TypeOver(string text, int skip, int take, string typed)
    {
        var canvas = Select(text, skip, take);
        canvas.TestTypeText(typed);
        return canvas.GetText();
    }

    // --- Deleting ---

    [StaFact]
    public void AWholeBoldWord_GoesWithItsMarkers()
    {
        Backspace("a **b** c", skip: 2, take: 1).Should().Be("a  c");
    }

    [StaFact]
    public void AWholeLineEndingInBold_LeavesNothing()
    {
        // Shift+End stops before the hidden closing "**".
        Backspace("2*3=6 **x**", skip: 0, take: -1).Should().Be("");
    }

    [StaFact]
    public void TheStartOfABoldWord_KeepsItsOpeningMarker()
    {
        Backspace("a **bold** c", skip: 0, take: 4).Should().Be("**ld** c");
    }

    [StaFact]
    public void TheEndOfABoldWord_KeepsItsClosingMarker()
    {
        Backspace("x **bold** y", skip: 4, take: 4).Should().Be("x **bo**");
    }

    [StaFact]
    public void TheMiddleOfABoldWord_LeavesItBold()
    {
        Backspace("**whole**", skip: 0, take: 3).Should().Be("**le**");
    }

    [StaFact]
    public void AWholeLink_GoesWithItsUrl()
    {
        Backspace("see [link](http://x) now", skip: 4, take: 4).Should().Be("see  now");
    }

    [StaFact]
    public void PartOfALink_KeepsTheLink()
    {
        Backspace("see [link](http://x) now", skip: 5, take: 2).Should().Be("see [lk](http://x) now");
    }

    [StaFact]
    public void AcrossLines_EachEdgeKeepsWhatStillHasText()
    {
        // Blank line keeps the paragraphs from merging into one block.
        var canvas = CreateCanvas("x **ab** z\n\ny *cd*");
        canvas.TestSetSelection(0, 5, 2, 4); // from before "b" of "ab" to after "c" of "cd"

        canvas.TestNavigate(Key.Back);

        canvas.GetText().Should().Be("x **a***d*");
    }

    [StaFact]
    public void Undo_RestoresTheTextInOneStep()
    {
        var canvas = Select("a **b** c", skip: 2, take: 1);
        canvas.TestNavigate(Key.Back);

        canvas.TestUndo();

        canvas.GetText().Should().Be("a **b** c");
    }

    [StaFact]
    public void SourceMode_DeletesExactlyTheSelectedCharacters()
    {
        var canvas = CreateCanvas("a **b** c", DocsCanvas.EditMode.Source);
        canvas.TestSetSelection(0, 2, 0, 5); // "**b"

        canvas.TestNavigate(Key.Back);

        canvas.GetText().Should().Be("a ** c");
    }

    // A data row after the header and separator; "who" is raw [4, 7) of "| **whole** | x |".
    private const string Table = "| h | i |\n|---|---|\n| **whole** | x |";

    [StaFact]
    public void PartOfABoldWordInATableCell_LeavesTheRestBold()
    {
        var canvas = CreateCanvas(Table);
        canvas.TestSetSelection(2, 4, 2, 7);

        canvas.TestNavigate(Key.Back);

        canvas.TestGetBlockText(2).Should().Be("| **le** | x |");
    }

    [StaFact]
    public void AWholeBoldWordInATableCell_GoesWithItsMarkers()
    {
        var canvas = CreateCanvas(Table);
        canvas.TestSetSelection(2, 4, 2, 9);

        canvas.TestNavigate(Key.Back);

        canvas.TestGetBlockText(2).Should().Be("|  | x |");
    }

    [StaFact]
    public void TypingOverABoldWordInATableCell_IsBold()
    {
        var canvas = CreateCanvas(Table);
        canvas.TestSetSelection(2, 4, 2, 9);

        canvas.TestTypeText("X");

        canvas.TestGetBlockText(2).Should().Be("| **X** | x |");
    }

    // --- Typing over a selection ---

    [StaFact]
    public void TypingOverABoldWord_IsBold()
    {
        TypeOver("a **b** c", skip: 2, take: 1, "X").Should().Be("a **X** c");
    }

    [StaFact]
    public void TypingOverASelectionStartingInPlainText_IsPlain()
    {
        TypeOver("a **bold** c", skip: 0, take: 4, "X").Should().Be("X**ld** c");
    }

    [StaFact]
    public void TypingOverASelectionStartingInBold_IsBold()
    {
        TypeOver("x **bold** y", skip: 4, take: 4, "X").Should().Be("x **boX**");
    }

    [StaFact]
    public void TypingOverAWholeLineStartingInBold_IsBold()
    {
        TypeOver("**bold** tail", skip: 0, take: -1, "X").Should().Be("**X**");
    }
}
