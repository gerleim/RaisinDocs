using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class FindReplaceTests
{
    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    /// <summary>Drives a real OnRender pass, which is where a dirty search is re-run.</summary>
    private static void Render(DocsCanvas canvas)
    {
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32).Render(canvas);
    }

    [StaFact]
    public void Search_FindsAllMatchesInSingleBlock()
    {
        var canvas = CreateCanvas("aababab");
        canvas.TestExecuteSearch("ab", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(3);
    }

    [StaFact]
    public void Search_CaseInsensitive_FindsAllVariants()
    {
        var canvas = CreateCanvas("Hello HELLO hello");
        canvas.TestExecuteSearch("hello", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(3);
    }

    [StaFact]
    public void Search_CaseSensitive_FindsExactOnly()
    {
        var canvas = CreateCanvas("Hello HELLO hello");
        canvas.TestExecuteSearch("Hello", caseSensitive: true);

        canvas.TestSearchMatchCount.Should().Be(1);
    }

    [StaFact]
    public void Search_AcrossMultipleBlocks()
    {
        var canvas = CreateCanvas("foo bar\nfoo baz\nqux foo");
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(3);
    }

    [StaFact]
    public void Search_EmptyQuery_ReturnsNoMatches()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestExecuteSearch("", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(0);
        canvas.TestCurrentMatchIndex.Should().Be(-1);
    }

    [StaFact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestExecuteSearch("xyz", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(0);
    }

    [StaFact]
    public void Search_SpecialCharacters_FindsLiteral()
    {
        var canvas = CreateCanvas("**bold** and **more**");
        canvas.TestExecuteSearch("**", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(4);
    }

    [StaFact]
    public void Search_SetsCurrentMatchNearCursor()
    {
        var canvas = CreateCanvas("foo bar foo baz foo");
        canvas.TestSetCursor(0, 10);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        // Matches at 0, 8, 16. Cursor at 10 is past match at 8, so next is index 2 (offset 16).
        canvas.TestCurrentMatchIndex.Should().Be(2);
    }

    [StaFact]
    public void Search_WrapsToFirstWhenCursorAfterAllMatches()
    {
        var canvas = CreateCanvas("foo bar");
        canvas.TestSetCursor(0, 7);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestCurrentMatchIndex.Should().Be(0);
    }

    [StaFact]
    public void Navigate_WrapsForward()
    {
        var canvas = CreateCanvas("foo foo foo");
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(3);
        int initial = canvas.TestCurrentMatchIndex;
        canvas.NavigateMatch(1);
        canvas.NavigateMatch(1);
        canvas.NavigateMatch(1);

        canvas.TestCurrentMatchIndex.Should().Be(initial);
    }

    [StaFact]
    public void Navigate_WrapsBackward()
    {
        var canvas = CreateCanvas("foo foo foo");
        canvas.TestSetCursor(0, 0);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestCurrentMatchIndex.Should().Be(0);
        canvas.NavigateMatch(-1);
        canvas.TestCurrentMatchIndex.Should().Be(2);
    }

    [StaFact]
    public void ReplaceAll_ReplacesAllOccurrences()
    {
        var canvas = CreateCanvas("foo bar foo");
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.ReplaceAll("baz");

        canvas.TestGetBlockText(0).Should().Be("baz bar baz");
        canvas.TestSearchMatchCount.Should().Be(0);
    }

    [StaFact]
    public void ReplaceAll_SingleUndoUnit()
    {
        var canvas = CreateCanvas("foo bar foo baz foo");
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.ReplaceAll("x");

        canvas.TestGetBlockText(0).Should().Be("x bar x baz x");

        canvas.TestUndo();

        canvas.TestGetBlockText(0).Should().Be("foo bar foo baz foo");
    }

    [StaFact]
    public void ReplaceAll_AcrossBlocks()
    {
        var canvas = CreateCanvas("foo one\nfoo two\nfoo three");
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.ReplaceAll("bar");

        canvas.TestGetBlockText(0).Should().Be("bar one");
        canvas.TestGetBlockText(1).Should().Be("bar two");
        canvas.TestGetBlockText(2).Should().Be("bar three");
    }

    [StaFact]
    public void ReplaceCurrent_ReplacesAndAdvances()
    {
        var canvas = CreateCanvas("foo bar foo");
        canvas.TestSetCursor(0, 0);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestCurrentMatchIndex.Should().Be(0);
        canvas.ReplaceCurrent("baz");

        canvas.TestGetBlockText(0).Should().Be("baz bar foo");
        canvas.TestSearchMatchCount.Should().Be(1);
    }

    [StaFact]
    public void Search_NonOverlapping()
    {
        var canvas = CreateCanvas("aaa");
        canvas.TestExecuteSearch("aa", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(1);
    }

    [StaFact]
    public void Search_WithDifferentLengthReplacement()
    {
        var canvas = CreateCanvas("ab ab ab");
        canvas.TestExecuteSearch("ab", caseSensitive: false);
        canvas.ReplaceAll("xyz");

        canvas.TestGetBlockText(0).Should().Be("xyz xyz xyz");
    }

    [StaFact]
    public void Search_AfterReplace_UpdatesMatches()
    {
        var canvas = CreateCanvas("foo foo foo");
        canvas.TestSetCursor(0, 0);
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.TestSearchMatchCount.Should().Be(3);

        canvas.ReplaceCurrent("bar");
        canvas.TestSearchMatchCount.Should().Be(2);

        canvas.ReplaceCurrent("bar");
        canvas.TestSearchMatchCount.Should().Be(1);

        canvas.ReplaceCurrent("bar");
        canvas.TestSearchMatchCount.Should().Be(0);
    }

    // --- Incremental typing in the find box ---
    //
    // Every keystroke re-runs the whole search. Picking the current match relative to the
    // caret made the hit jump forward on almost every letter, because making a match current
    // leaves the caret at its *end*: with "alpha nothing beta nothing", "n" lands on offset 6
    // and puts the caret at 7, so "no" then looks from 7 and skips straight to offset 19 - even
    // though offset 6 is still a perfectly good "no". The search resumes from the current
    // match's start instead, so a hit is only given up when it genuinely stops matching.

    [StaFact]
    public void IncrementalTyping_KeepsTheCurrentMatchWhileItStillMatches()
    {
        var canvas = CreateCanvas("alpha nothing beta nothing");
        canvas.TestSetCursor(0, 0);

        canvas.TestExecuteSearch("n", caseSensitive: false);
        canvas.TestAnchorOffset.Should().Be(6);

        canvas.TestExecuteSearch("no", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(2);
        canvas.TestCurrentMatchIndex.Should().Be(0, "offset 6 still matches, so it stays current");
        canvas.TestAnchorOffset.Should().Be(6);
    }

    [StaFact]
    public void IncrementalTyping_AdvancesWhenTheCurrentMatchStopsMatching()
    {
        var canvas = CreateCanvas("nap and nothing");
        canvas.TestSetCursor(0, 0);

        canvas.TestExecuteSearch("n", caseSensitive: false);
        canvas.TestAnchorOffset.Should().Be(0);

        canvas.TestExecuteSearch("no", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(1);
        canvas.TestAnchorOffset.Should().Be(8, "the hit at 0 is no longer a hit, so the search moves on");
    }

    [StaFact]
    public void IncrementalTyping_AfterNavigating_ResumesFromTheMatchTheUserIsOn()
    {
        var canvas = CreateCanvas("alpha nothing beta nothing gamma");
        canvas.TestSetCursor(0, 0);

        canvas.TestExecuteSearch("nothing", caseSensitive: false);
        canvas.NavigateMatch(1);
        canvas.TestAnchorOffset.Should().Be(19);

        canvas.TestExecuteSearch("nothing ", caseSensitive: false);

        canvas.TestCurrentMatchIndex.Should().Be(1, "typing continues from the match the user navigated to");
        canvas.TestAnchorOffset.Should().Be(19);
    }

    [StaFact]
    public void IncrementalTyping_Backspace_KeepsTheCurrentMatch()
    {
        var canvas = CreateCanvas("alpha nothing beta nothing gamma");
        canvas.TestSetCursor(0, 0);

        canvas.TestExecuteSearch("nothing", caseSensitive: false);
        canvas.NavigateMatch(1);
        canvas.TestAnchorOffset.Should().Be(19);

        canvas.TestExecuteSearch("nothin", caseSensitive: false);

        canvas.TestAnchorOffset.Should().Be(19, "deleting a letter must not throw the hit back to the top");
    }

    [StaFact]
    public void OpenFind_RestartsTheSearchFromTheCaret()
    {
        var canvas = CreateCanvas("foo bar foo baz foo");
        canvas.TestSetCursor(0, 0);
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.TestCurrentMatchIndex.Should().Be(0);

        // The user clicked further down the document and hit Ctrl+F again.
        canvas.TestSetCursor(0, 10);
        canvas.OpenFind(showReplace: false);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        canvas.TestCurrentMatchIndex.Should().Be(2);
    }

    // --- ISearchServices contract ---
    //
    // RenderingContext gates the highlight pass on ISearchServices, not on the internal
    // DocsCanvas members the tests above use. Between 9ce9893 and this test, the explicit
    // implementation was a stub returning 0 while those internal members returned the real
    // count, so search highlights silently never painted and every test above still passed.
    // These assert through the interface so that shadowing cannot regress unnoticed.

    [StaFact]
    public void HasSearchHighlights_MatchesConcreteMatchCount()
    {
        var canvas = CreateCanvas("aababab");
        var services = (ISearchServices)canvas;

        services.HasSearchHighlights.Should().BeFalse("no search has run yet");

        canvas.TestExecuteSearch("ab", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(3);
        services.HasSearchHighlights.Should().BeTrue(
            "the render pass reads the interface, which must agree with the concrete match count");
    }

    [StaFact]
    public void HasSearchHighlights_FalseWhenSearchFindsNothing()
    {
        var canvas = CreateCanvas("hello world");
        var services = (ISearchServices)canvas;

        canvas.TestExecuteSearch("xyz", caseSensitive: false);

        canvas.TestSearchMatchCount.Should().Be(0);
        services.HasSearchHighlights.Should().BeFalse();
    }

    [StaFact]
    public void HasSearchHighlights_FalseAfterMatchesReplacedAway()
    {
        var canvas = CreateCanvas("foo foo");
        var services = (ISearchServices)canvas;

        canvas.TestSetCursor(0, 0);
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        services.HasSearchHighlights.Should().BeTrue();

        canvas.ReplaceAll("bar");

        canvas.TestSearchMatchCount.Should().Be(0);
        services.HasSearchHighlights.Should().BeFalse();
    }

    [StaFact]
    public void HasSearchHighlights_FalseAfterFindClosed()
    {
        var canvas = CreateCanvas("foo foo");
        var services = (ISearchServices)canvas;

        canvas.TestExecuteSearch("foo", caseSensitive: false);
        services.HasSearchHighlights.Should().BeTrue();

        canvas.CloseFind();

        services.HasSearchHighlights.Should().BeFalse();
    }

    // --- Re-search after an edit ---
    //
    // The render pass owns re-running a search the document invalidated. Two things have to
    // hold: a search that currently has no matches must still be re-run (or typing the first
    // match never lights up), and the re-run must not navigate - ExecuteSearch ends in
    // ScrollToMatch, which would drag the caret to the next match on every keystroke.

    [StaFact]
    public void EditCreatingFirstMatch_LightsUpOnNextRender()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestExecuteSearch("foo", caseSensitive: false);
        canvas.TestSearchMatchCount.Should().Be(0);

        canvas.TestSetCursor(0, 0);
        canvas.TestInsert("foo ");
        Render(canvas);

        canvas.TestSearchMatchCount.Should().Be(1,
            "a search with no matches must still be re-run, or the first match never appears");
    }

    [StaFact]
    public void EditWithActiveSearch_DoesNotMoveCursorOnRender()
    {
        var canvas = CreateCanvas("foo bar foo");
        canvas.TestSetCursor(0, 4);
        canvas.TestExecuteSearch("foo", caseSensitive: false);

        // Put the caret where a user would be typing, not where the search left it.
        canvas.TestSetCursor(0, 4);
        canvas.TestInsert("X");
        int block = canvas.TestCursorBlock, offset = canvas.TestCursorOffset;

        Render(canvas);

        canvas.TestCursorBlock.Should().Be(block);
        canvas.TestCursorOffset.Should().Be(offset, "rendering must not navigate the caret");
        canvas.TestAnchorOffset.Should().Be(offset, "rendering must not create a selection");
        canvas.TestSearchMatchCount.Should().Be(2, "matches are still refreshed");
    }
}
