using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>
/// Edge case tests for hierarchical block structure. These tests focus on ensuring
/// complex scenarios parse without errors and maintain basic structural integrity.
/// </summary>
public class BlockHierarchyEdgeCaseTests
{
    private static List<ParsedBlock> ParseBlocks(params string[] lines)
    {
        return MarkdownParser.Parse(i => lines[i], lines.Length);
    }

    // --- Nested list continuations with blank lines ---

    [Fact]
    public void NestedListWithBlankLineContinuation_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "- Item",
            "",
            "  Continuation"
        );

        blocks.Should().NotBeEmpty();
        blocks[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    [Fact]
    public void NestedListMultipleLevels_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "- Level 1",
            "  - Level 2",
            "    - Level 3"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Hard breaks in various contexts ---

    [Fact]
    public void HardBreakBackslash_PreventsContinuation()
    {
        var blocks = ParseBlocks(
            "First\\",
            "Second"
        );

        blocks.Should().NotBeEmpty();
        blocks[0].Children.Should().BeNullOrEmpty();
    }

    [Fact]
    public void HardBreakTrailingSpaces_PreventsContinuation()
    {
        var blocks = ParseBlocks(
            "First  ",
            "Second"
        );

        blocks.Should().NotBeEmpty();
        blocks[0].Children.Should().BeNullOrEmpty();
    }

    // --- Color tags with various formatting ---

    [Fact]
    public void ColorTagsWithTrailingSpaces_ParsesSafely()
    {
        var blocks = ParseBlocks("<!--@fg:red-->hello  <!--/@fg-->");

        blocks.Should().NotBeEmpty();
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void NestedColorTags_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "<!--@bg:blue-->",
            "Text with <!--@fg:red-->nested color<!--/@fg-->",
            "<!--/@bg-->"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Empty block handling ---

    [Fact]
    public void MultipleConsecutiveEmptyLines_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "First",
            "",
            "",
            "Second"
        );

        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void EmptyBlocksAtStartAndEnd_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "",
            "Content",
            ""
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Soft breaks interaction with hard breaks ---

    [Fact]
    public void SoftBreakFollowedByHardBreak_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "line1\\",
            "line2\\",
            "line3"
        );

        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void HardBreakFollowedBySoftBreak_ParsesSafely()
    {
        var blocks = ParseBlocks("line1\\", "line2\nline3");

        blocks.Should().NotBeEmpty();
        if (blocks.Count > 0)
            blocks[0].Children.Should().BeNullOrEmpty();
    }

    // --- Code blocks with continuations ---

    [Fact]
    public void FencedCodeBlock_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "```",
            "code",
            "```",
            "After code"
        );

        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void IndentedCodeWithContinuation_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "    code line 1",
            "    code line 2",
            "Normal text"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- List items with code blocks ---

    [Fact]
    public void ListItemWithIndentedCodeBlock_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "- Item",
            "",
            "      indented code"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Blockquotes with continuations ---

    [Fact]
    public void BlockquoteWithContinuation_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "> Quote",
            "Continuation"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Heading edge cases ---

    [Fact]
    public void HeadingWithTrailingHardBreak_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "# Heading\\",
            "Not part of heading"
        );

        blocks.Should().NotBeEmpty();
    }

    // --- Single character blocks ---

    [Fact]
    public void SingleCharacterBlocks_ParsesSafely()
    {
        var blocks = ParseBlocks("a", "b", "c");

        blocks.Should().NotBeEmpty();
        blocks.Count.Should().Be(3);
    }

    // --- Block with only whitespace followed by content ---

    [Fact]
    public void WhitespaceOnlyBlockFollowedByContent_ParsesSafely()
    {
        var blocks = ParseBlocks(
            "Content",
            "   ",
            "More content"
        );

        blocks.Should().NotBeEmpty();
    }
}
