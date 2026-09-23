using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class VisualSelectionTests
{
    private static (ParsedBlock Parsed, BlockVisualMap Map) Parse(string text)
    {
        var parsed = MarkdownParser.Parse(_ => text, 1);
        return (parsed[0], BlockVisualMap.Compute(parsed[0], text, parsed, _ => text));
    }

    private static string Copy(string text, int from, int to)
    {
        var (parsed, map) = Parse(text);
        return VisualSelection.BlockSelectionText(parsed, map, text, from, to);
    }

    private static List<(string Open, string Content, string Close)> Constructs(string text)
    {
        var (parsed, _) = Parse(text);
        return VisualSelection.FindConstructs(parsed, text)
            .Select(c => (text.Substring(c.Open.Start, c.Open.Length),
                          text[c.ContentStart..c.ContentEnd],
                          text.Substring(c.Close.Start, c.Close.Length)))
            .ToList();
    }

    // --- Constructs ---

    [Fact]
    public void NestedEmphasis_PairsEachOpenerWithItsOwnCloser()
    {
        Constructs("**a *b* c**").Should().BeEquivalentTo(new[]
        {
            ("**", "a *b* c", "**"),
            ("*", "b", "*"),
        });
    }

    [Fact]
    public void TripleDelimiters_SplitIntoBoldAndItalic()
    {
        Constructs("***x***").Should().BeEquivalentTo(new[]
        {
            ("*", "**x**", "*"),
            ("**", "x", "**"),
        });
    }

    [Fact]
    public void StrikethroughCodeLinkAndColour_AreEachAConstruct()
    {
        Constructs("~~s~~ `c` [t](u) <!--@fg:red-->r<!--/@fg-->").Should().BeEquivalentTo(new[]
        {
            ("~~", "s", "~~"),
            ("`", "c", "`"),
            ("[", "t", "](u)"),
            ("<!--@fg:red-->", "r", "<!--/@fg-->"),
        });
    }

    [Fact]
    public void FencedCode_HasNoConstructs()
    {
        var lines = new[] { "```", "**not bold**", "```" };
        var parsed = MarkdownParser.Parse(i => lines[i], lines.Length);

        VisualSelection.FindConstructs(parsed[1], lines[1]).Should().BeEmpty();
    }

    // --- Copy text for one block ---

    [Fact]
    public void ContentInsideAConstruct_GetsBothMarkers()
    {
        Copy("**whole**", 2, 5).Should().Be("**who**");
    }

    [Fact]
    public void AConstructWhoseContentIsNotSelected_ContributesNoMarkers()
    {
        // The range ends after the opening "**" but before "b".
        Copy("a **b** c", 0, 4).Should().Be("a ");
    }

    [Fact]
    public void ReachingTheLastVisibleCharacter_TakesTrailingMarkup()
    {
        Copy("2*3=6 **x**", 0, 9).Should().Be("2*3=6 **x**");
    }

    [Fact]
    public void ReachingTheFirstVisibleCharacter_TakesTheBlockPrefix()
    {
        Copy("> quoted *it*", 2, 9).Should().Be("> quoted ");
    }

    [Fact]
    public void AnImageAtTheStart_IsNotPulledIntoASelectionAfterIt()
    {
        Copy("![alt](a.png) text", 14, 18).Should().Be("text");
    }

    [Fact]
    public void EmptyRange_IsEmpty()
    {
        Copy("**x**", 2, 2).Should().BeEmpty();
    }
}
