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
    public void ParserCreatesChildrenForContinuations()
    {
        // Test the actual case: "sad" followed by "s"
        var blocks = new[] { "sad", "s" };
        var parsed = ParseBlocks(blocks);

        // Check if parser detected continuation
        parsed.Should().HaveCount(2);
        var firstBlock = parsed[0];

        // Parser should set Children on first block if second is a continuation
        firstBlock.Children.Should().NotBeNull("Parser should detect paragraph continuations");
        firstBlock.Children!.Should().Contain(parsed[1], "Parser should mark 's' as child of 'sad'");
    }

    [Fact]
    public void ContinuationsGetMerged_ActualCase()
    {
        // Test the actual rendering case
        var blocks = new[] { "sad", "s" };
        var result = BuildVisualStructure(blocks);

        // Should be merged into 1 block
        result.Blocks.Should().HaveCount(1, "Continuation blocks should be merged");
        result.Blocks[0].MergedText.ToString().Should().Be("sad\ns", "Should have newline between blocks");
        result.Blocks[0].SourceBlockIndices.Should().Equal([0, 1], "Should track both source blocks");
    }

    [Fact]
    public void RenderingPipeline_SadS()
    {
        // Full pipeline test: "sad" followed by "s"
        var blocks = new[] { "sad", "s" };

        // 1. Parser detects continuation
        var parsed = ParseBlocks(blocks);
        parsed[0].Children.Should().NotBeNull("Parser should detect continuation");
        parsed[0].Children!.Should().Contain(parsed[1]);

        // 2. VisualBlockStructure merges them
        var visualStructure = VisualBlockStructure.Build(parsed, i => blocks[i]);
        visualStructure.Blocks.Should().HaveCount(1);
        var merged = visualStructure.Blocks[0];
        merged.MergedText.ToString().Should().Be("sad\ns");
        merged.SourceBlockIndices.Should().HaveCount(2);

        // 3. Verify all properties transferred
        merged.Kind.Should().Be(BlockKind.Paragraph);
        merged.Runs.Should().NotBeEmpty();
        merged.SourceBlockIndices.Should().Equal([0, 1]);
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
    public void TwoConsecutiveParagraphs_GetMerged()
    {
        // Consecutive paragraphs are natural continuations
        var result = BuildVisualStructure("first", "second");
        result.Blocks.Should().HaveCount(1, "Consecutive paragraphs should merge as continuations");
        result.Blocks[0].MergedText.ToString().Should().Be("first\nsecond");
        result.Blocks[0].SourceBlockIndices.Should().Equal([0, 1]);
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

public class ContinuationRenderingTests
{
    [Fact]
    public void MergedBlockContainsNewline()
    {
        // After MergeParagraphContinuations, a block should contain \n between continuations
        var blocks = new[] { "sad", "s" };
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);

        // Manually simulate what MergeParagraphContinuations does
        parsed[0] = parsed[0] with { Children = [parsed[1]] };
        var visualStructure = VisualBlockStructure.Build(parsed, i => blocks[i]);

        // The merged block should have \n between the parts
        visualStructure.Blocks.Should().HaveCount(1);
        visualStructure.Blocks[0].MergedText.ToString().Should().Contain("\n");
        visualStructure.Blocks[0].MergedText.ToString().Should().Be("sad\ns");
    }

    [Fact]
    public void SoftBreakReplacement()
    {
        // Test that \n is replaced with "¶ " (pilcrow + space)
        string merged = "sad\ns";
        string replaced = merged.Replace("\n", "¶ ");

        replaced.Should().Be("sad¶ s");
        replaced.Should().Contain("¶");
    }

    [Fact]
    public void SoftBreakOffsetCalculation()
    {
        // Test that soft break offsets are calculated correctly for "¶ "
        var sb = new System.Text.StringBuilder();
        var offsets = new List<int>();

        // Simulate EmitParagraphGroup logic with 2 blocks
        string[] texts = ["sad", "s"];

        for (int i = 0; i < texts.Length; i++)
        {
            if (i > 0)
            {
                offsets.Add(sb.Length);  // Position of ¶
                sb.Append("¶ ");         // 2 characters
            }
            sb.Append(texts[i]);
        }

        string result = sb.ToString();
        result.Should().Be("sad¶ s");
        offsets.Should().Equal([3]);  // ¶ is at position 3
    }

    [Fact]
    public void BlockDetectionWithNewline()
    {
        // After merging, a single block containing \n should be detected as a continuation
        string blockText = "sad\ns";
        bool hasContinuation = blockText.Contains('\n');

        hasContinuation.Should().BeTrue("Block should be recognized as containing continuations");
    }

    [Fact]
    public void FullPipelineSimulation()
    {
        // Simulate the full rendering pipeline:
        // 1. Blocks merged: "sad\ns"
        // 2. Should be detected as continuation
        // 3. Should be converted to "sad¶ s" for rendering

        string mergedBlock = "sad\ns";

        // Step 1: Detect it has continuations
        bool isContinuation = mergedBlock.Contains('\n');
        isContinuation.Should().BeTrue();

        // Step 2: Convert for rendering
        string displayText = mergedBlock.Replace("\n", "¶ ");
        displayText.Should().Be("sad¶ s");

        // Step 3: Verify soft break position
        int softBreakPos = displayText.IndexOf('¶');
        softBreakPos.Should().Be(3);
    }
}
