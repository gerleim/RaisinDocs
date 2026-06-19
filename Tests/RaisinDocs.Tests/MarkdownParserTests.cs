using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class MarkdownParserTests
{
    private static List<ParsedBlock> ParseBlocks(params string[] blocks)
    {
        return MarkdownParser.Parse(i => blocks[i], blocks.Length);
    }

    // --- Block classification ---

    [Fact]
    public void PlainText_IsParagraph()
    {
        var result = ParseBlocks("hello world");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void EmptyBlock_IsParagraph()
    {
        var result = ParseBlocks("");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void Heading1()
    {
        var result = ParseBlocks("# Heading");
        result[0].Kind.Should().Be(BlockKind.Heading1);
    }

    [Fact]
    public void Heading2()
    {
        var result = ParseBlocks("## Heading");
        result[0].Kind.Should().Be(BlockKind.Heading2);
    }

    [Fact]
    public void Heading3()
    {
        var result = ParseBlocks("### Heading");
        result[0].Kind.Should().Be(BlockKind.Heading3);
    }

    [Fact]
    public void Heading4()
    {
        var result = ParseBlocks("#### Heading");
        result[0].Kind.Should().Be(BlockKind.Heading4);
    }

    [Fact]
    public void Heading5()
    {
        var result = ParseBlocks("##### Heading");
        result[0].Kind.Should().Be(BlockKind.Heading5);
    }

    [Fact]
    public void Heading6()
    {
        var result = ParseBlocks("###### Heading");
        result[0].Kind.Should().Be(BlockKind.Heading6);
    }

    [Fact]
    public void HashWithoutSpace_IsParagraph()
    {
        var result = ParseBlocks("#NotAHeading");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void HashAlone_IsParagraph()
    {
        var result = ParseBlocks("#");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void DashList_IsUnorderedListItem()
    {
        var result = ParseBlocks("- item");
        result[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    [Fact]
    public void StarList_IsUnorderedListItem()
    {
        var result = ParseBlocks("* item");
        result[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    [Fact]
    public void DashAlone_IsParagraph()
    {
        var result = ParseBlocks("-");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    // --- Fenced code blocks ---

    [Fact]
    public void FencedCodeBlock_AllLinesAreFencedCodeLine()
    {
        var result = ParseBlocks("```", "code here", "more code", "```");
        result.Should().HaveCount(4);
        result[0].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[3].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void FencedCodeBlock_NoInlineParsing()
    {
        var result = ParseBlocks("```", "**not bold**", "```");
        result[1].Runs.Should().HaveCount(1);
        result[1].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    [Fact]
    public void UnterminatedFence_RemainingBlocksAreFencedCodeLine()
    {
        var result = ParseBlocks("before", "```", "code", "still code");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[3].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void FencedCode_WithLanguageTag()
    {
        var result = ParseBlocks("```csharp", "var x = 1;", "```");
        result[0].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void TextAfterFence_IsParagraph()
    {
        var result = ParseBlocks("```", "code", "```", "after");
        result[3].Kind.Should().Be(BlockKind.Paragraph);
    }

    // --- Inline parsing: bold ---

    [Fact]
    public void Bold_ParsedCorrectly()
    {
        var result = ParseBlocks("**bold**");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Should().Be(new StyledRun(0, 8, InlineStyle.Bold));
    }

    [Fact]
    public void Bold_WithSurroundingText()
    {
        var result = ParseBlocks("before **bold** after");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 8, InlineStyle.Bold));
        result[0].Runs[2].Should().Be(new StyledRun(15, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Bold_Unclosed_IsNormal()
    {
        var result = ParseBlocks("**unclosed");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    // --- Inline parsing: italic ---

    [Fact]
    public void Italic_ParsedCorrectly()
    {
        var result = ParseBlocks("*italic*");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Should().Be(new StyledRun(0, 8, InlineStyle.Italic));
    }

    [Fact]
    public void Italic_WithSurroundingText()
    {
        var result = ParseBlocks("before *italic* after");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 8, InlineStyle.Italic));
        result[0].Runs[2].Should().Be(new StyledRun(15, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Italic_Unclosed_IsNormal()
    {
        var result = ParseBlocks("*unclosed");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    // --- Inline parsing: bold+italic ---

    [Fact]
    public void BoldItalic_ParsedCorrectly()
    {
        var result = ParseBlocks("***both***");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Should().Be(new StyledRun(0, 10, InlineStyle.BoldItalic));
    }

    // --- Inline parsing: code ---

    [Fact]
    public void Code_ParsedCorrectly()
    {
        var result = ParseBlocks("`code`");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Should().Be(new StyledRun(0, 6, InlineStyle.Code));
    }

    [Fact]
    public void Code_WithSurroundingText()
    {
        var result = ParseBlocks("before `code` after");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 6, InlineStyle.Code));
        result[0].Runs[2].Should().Be(new StyledRun(13, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Code_SuppressesEmphasis()
    {
        var result = ParseBlocks("`**not bold**`");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Code);
    }

    [Fact]
    public void Code_Unclosed_IsNormal()
    {
        var result = ParseBlocks("`unclosed");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    [Fact]
    public void DoubleBacktick_Code()
    {
        var result = ParseBlocks("``code with ` inside``");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Code);
    }

    // --- Multiple inline styles ---

    [Fact]
    public void MultipleBoldRuns()
    {
        var result = ParseBlocks("**a** and **b**");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 5, InlineStyle.Bold));
        result[0].Runs[1].Should().Be(new StyledRun(5, 5, InlineStyle.Normal));
        result[0].Runs[2].Should().Be(new StyledRun(10, 5, InlineStyle.Bold));
    }

    [Fact]
    public void MixedBoldAndItalic()
    {
        var result = ParseBlocks("**bold** and *italic*");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Bold);
        result[0].Runs[1].Style.Should().Be(InlineStyle.Normal);
        result[0].Runs[2].Style.Should().Be(InlineStyle.Italic);
    }

    // --- Headings with inline styles ---

    [Fact]
    public void Heading_WithBold()
    {
        var result = ParseBlocks("# **bold** heading");
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[0].Runs.Should().HaveCountGreaterThan(1);
        result[0].Runs.Should().Contain(r => r.Style == InlineStyle.Bold);
    }

    // --- Run coverage ---

    [Fact]
    public void AllCharactersCovered()
    {
        var result = ParseBlocks("hello **bold** world");
        var runs = result[0].Runs;
        int totalLength = runs.Sum(r => r.Length);
        totalLength.Should().Be(20);
        runs[0].Start.Should().Be(0);
        for (int i = 1; i < runs.Count; i++)
            runs[i].Start.Should().Be(runs[i - 1].Start + runs[i - 1].Length);
    }

    // --- Multiple blocks ---

    [Fact]
    public void MultipleBlocks_ParsedIndependently()
    {
        var result = ParseBlocks("# Heading", "paragraph", "- list item");
        result.Should().HaveCount(3);
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[1].Kind.Should().Be(BlockKind.Paragraph);
        result[2].Kind.Should().Be(BlockKind.UnorderedListItem);
    }
}
