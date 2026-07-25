using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class VisualBlockStructureTests
{
    private static List<ParsedBlock> ParseBlocks(params string[] blocks)
    {
        return MarkdownParser.Parse(i => blocks[i], blocks.Length);
    }

    private static VisualBlockStructure BuildVisualStructure(params string[] blocks)
    {
        var parsed = ParseBlocks(blocks);
        return VisualBlockStructure.Build(parsed, i => blocks[i]);
    }

    [Fact]
    public void SingleParagraph_NoMerging()
    {
        var result = BuildVisualStructure("hello world");
        result.Blocks.Should().HaveCount(1);
        result.Blocks[0].MergedText.ToString().Should().Be("hello world");
        result.Blocks[0].SourceBlockIndices.Should().Equal([0]);
    }

    [Fact]
    public void TwoSeparateBlocks_NoMerging()
    {
        var result = BuildVisualStructure("first", "second");
        result.Blocks.Should().HaveCount(2);
        result.Blocks[0].MergedText.ToString().Should().Be("first");
        result.Blocks[1].MergedText.ToString().Should().Be("second");
    }

    [Fact]
    public void ContinuationWithChildren_PreservesKind()
    {
        var blocks = new[] { "text" };
        var parsed = ParseBlocks(blocks);

        // Create a paragraph with children (empty array is fine for this test)
        parsed[0] = parsed[0] with { Children = [] };

        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        result.Blocks[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void BlockProperties_Preserved()
    {
        var blocks = new[] { "text" };
        var parsed = ParseBlocks(blocks);

        var block = parsed[0] with
        {
            LeadingSpaces = 4,
            ListNestingLevel = 2,
            CreateVisualSeparation = true,
            ColorSpans = [new ColorSpan(0, 4, new RgbColor(255, 0, 0), null)]
        };
        parsed[0] = block;

        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        result.Blocks[0].LeadingSpaces.Should().Be(4);
        result.Blocks[0].ListNestingLevel.Should().Be(2);
        result.Blocks[0].CreateVisualSeparation.Should().Be(true);
        result.Blocks[0].ColorSpans.Should().NotBeNull();
    }

    [Fact]
    public void NonParagraphBlocks_NoChildren()
    {
        var blocks = new[] { "# Heading" };
        var parsed = ParseBlocks(blocks);

        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        // Heading without children should not be merged
        result.Blocks.Should().HaveCount(1);
        result.Blocks[0].Kind.Should().Be(BlockKind.Heading1);
    }

    [Fact]
    public void BlocksWithoutChildren_NotMerged()
    {
        var blocks = new[] { "para 1", "para 2" };
        var parsed = ParseBlocks(blocks);

        // Don't set up any children relationship - should not merge
        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        // Each block should remain separate
        result.Blocks.Should().HaveCount(2);
        result.Blocks[0].MergedText.ToString().Should().Be("para 1");
        result.Blocks[1].MergedText.ToString().Should().Be("para 2");
    }

    [Fact]
    public void ImagesPreserved()
    {
        var blocks = new[] { "![alt](url)" };
        var parsed = ParseBlocks(blocks);

        var block = parsed[0] with
        {
            Images = [new InlineImage { Start = 0, Length = 10, Url = "url", AltText = "alt" }]
        };
        parsed[0] = block;

        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        result.Blocks[0].Images.Should().NotBeNull();
        result.Blocks[0].Images!.Should().HaveCount(1);
    }

    [Fact]
    public void LinksPreserved()
    {
        var blocks = new[] { "[text](url)" };
        var parsed = ParseBlocks(blocks);

        var block = parsed[0] with
        {
            Links = [new InlineLink { Start = 0, Length = 11, Url = "url", Title = null }]
        };
        parsed[0] = block;

        var result = VisualBlockStructure.Build(parsed, i => blocks[i]);

        result.Blocks[0].Links.Should().NotBeNull();
        result.Blocks[0].Links!.Should().HaveCount(1);
    }
}
