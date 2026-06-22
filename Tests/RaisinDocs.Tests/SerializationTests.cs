using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class SerializationTests
{
    private static Document LoadMarkdown(string text)
    {
        var doc = new Document();
        doc.SetText(text);
        return doc;
    }

    private static string SaveMarkdown(Document doc) => doc.GetText();

    private static List<ParsedBlock> ParseDoc(Document doc) =>
        MarkdownParser.Parse(i => doc.GetBlockText(i), doc.BlockCount);

    // --- Block structure after loading ---

    [Fact]
    public void Load_FencedCodeBlock_EachLineIsOwnBlock()
    {
        var doc = LoadMarkdown("```python\ndef greet():\n    pass\n```");
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("```python");
        doc.GetBlockText(1).Should().Be("def greet():");
        doc.GetBlockText(2).Should().Be("    pass");
        doc.GetBlockText(3).Should().Be("```");
    }

    [Fact]
    public void Load_FencedCodeBlock_WithEmptyLine()
    {
        var doc = LoadMarkdown("```python\n\ndef greet():\n```");
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("```python");
        doc.GetBlockText(1).Should().Be("");
        doc.GetBlockText(2).Should().Be("def greet():");
        doc.GetBlockText(3).Should().Be("```");
    }

    [Fact]
    public void Load_FencedCodeBlock_TrailingSpacesPreserved()
    {
        var doc = LoadMarkdown("```\nline with spaces  \nnext line\n```");
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("```");
        doc.GetBlockText(1).Should().Be("line with spaces  ");
        doc.GetBlockText(2).Should().Be("next line");
        doc.GetBlockText(3).Should().Be("```");
    }

    [Fact]
    public void Load_EachLineIsOwnBlock()
    {
        var doc = LoadMarkdown("line one  \nline two  \nline three");
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("line one  ");
        doc.GetBlockText(1).Should().Be("line two  ");
        doc.GetBlockText(2).Should().Be("line three");
    }

    [Fact]
    public void Load_ParagraphBreaks_SeparateBlocks()
    {
        var doc = LoadMarkdown("paragraph one\nparagraph two\nparagraph three");
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("paragraph one");
        doc.GetBlockText(1).Should().Be("paragraph two");
        doc.GetBlockText(2).Should().Be("paragraph three");
    }

    [Fact]
    public void Load_MixedContent_FenceAndParagraphs()
    {
        var doc = LoadMarkdown("# Hello\n```\ncode\n```\nworld");
        doc.BlockCount.Should().Be(5);
        doc.GetBlockText(0).Should().Be("# Hello");
        doc.GetBlockText(1).Should().Be("```");
        doc.GetBlockText(2).Should().Be("code");
        doc.GetBlockText(3).Should().Be("```");
        doc.GetBlockText(4).Should().Be("world");
    }

    // --- Parser classification after loading ---

    [Fact]
    public void Parse_FencedBlock_DelimitersMarkedCorrectly()
    {
        var doc = LoadMarkdown("```python\ndef greet():\n```");
        var parsed = ParseDoc(doc);

        parsed[0].IsFenceDelimiter.Should().BeTrue();
        parsed[0].Kind.Should().Be(BlockKind.FencedCodeLine);

        parsed[1].IsFenceDelimiter.Should().BeFalse();
        parsed[1].Kind.Should().Be(BlockKind.FencedCodeLine);

        parsed[2].IsFenceDelimiter.Should().BeTrue();
        parsed[2].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void Parse_ContentAfterFence_NotCodeLine()
    {
        var doc = LoadMarkdown("```\ncode\n```\nnormal text");
        var parsed = ParseDoc(doc);

        parsed[3].Kind.Should().Be(BlockKind.Paragraph);
        parsed[3].IsFenceDelimiter.Should().BeFalse();
    }

    // --- Visual mode visibility ---

    [Fact]
    public void Visual_FenceDelimitersHidden_ContentVisible()
    {
        var doc = LoadMarkdown("```python\ndef greet():\n    pass\n```");
        var parsed = ParseDoc(doc);

        var visibleBlocks = new List<int>();
        for (int i = 0; i < doc.BlockCount; i++)
        {
            if (!parsed[i].IsFenceDelimiter)
                visibleBlocks.Add(i);
        }

        visibleBlocks.Should().BeEquivalentTo([1, 2]);
        doc.GetBlockText(1).Should().Be("def greet():");
        doc.GetBlockText(2).Should().Be("    pass");
    }

    [Fact]
    public void Visual_HeadingPrefix_Hidden()
    {
        var doc = LoadMarkdown("# My Heading");
        var parsed = ParseDoc(doc);
        var map = BlockVisualMap.Compute(parsed[0], doc.GetBlockText(0));

        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
        map.BuildDisplayString(doc.GetBlockText(0), 0, doc.GetBlockLength(0))
            .Should().Be("My Heading");
    }

    [Fact]
    public void Visual_ListBullet_ReplacementPrefix()
    {
        var doc = LoadMarkdown("- Item one\n- Item two");
        var parsed = ParseDoc(doc);

        for (int i = 0; i < doc.BlockCount; i++)
        {
            var map = BlockVisualMap.Compute(parsed[i], doc.GetBlockText(i));
            map.ReplacementPrefix.Should().NotBeNull();
            map.BuildDisplayString(doc.GetBlockText(i), 0, doc.GetBlockLength(i))
                .Should().StartWith("Item");
        }
    }

    [Fact]
    public void Visual_BoldMarkers_Hidden()
    {
        var doc = LoadMarkdown("some **bold** text");
        var parsed = ParseDoc(doc);
        var map = BlockVisualMap.Compute(parsed[0], doc.GetBlockText(0));

        map.BuildDisplayString(doc.GetBlockText(0), 0, doc.GetBlockLength(0))
            .Should().Be("some bold text");
    }

    // --- Round-trip ---

    [Fact]
    public void RoundTrip_PlainText()
    {
        var doc = LoadMarkdown("hello\nworld");
        SaveMarkdown(doc).Should().Be("hello\r\nworld");
    }

    [Fact]
    public void RoundTrip_FencedCodeBlock()
    {
        var doc = LoadMarkdown("```python\ndef greet():\n    pass\n```");
        SaveMarkdown(doc).Should().Be("```python\r\ndef greet():\r\n    pass\r\n```");
    }

    [Fact]
    public void RoundTrip_FencedCodeWithTrailingSpaces()
    {
        var doc = LoadMarkdown("```\nline with spaces  \nnext line\n```");
        SaveMarkdown(doc).Should().Be("```\r\nline with spaces  \r\nnext line\r\n```");
    }

    [Fact]
    public void RoundTrip_LinesWithTrailingSpaces()
    {
        var doc = LoadMarkdown("line one  \nline two  \nline three");
        SaveMarkdown(doc).Should().Be("line one  \r\nline two  \r\nline three");
    }

    [Fact]
    public void RoundTrip_MixedContent()
    {
        var doc = LoadMarkdown("# Heading\nsome **bold** text\n```\ncode\n```\n- item one\n- item two");
        SaveMarkdown(doc).Should().Be("# Heading\r\nsome **bold** text\r\n```\r\ncode\r\n```\r\n- item one\r\n- item two");
    }
}
