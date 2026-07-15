using System.Windows;
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
}
