using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.BlockModel;

public class HtmlBlockModelParserTests
{
    // --- Block Structure Tests ---

    [Fact]
    public void ParseBlockStructure_SimpleHeader_CreatesHeaderBlock()
    {
        var html = "<h3>Title</h3>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].Kind.Should().Be(BlockKind.Heading3);
        blocks[0].Content.Should().HaveCount(1);
        blocks[0].Content[0].Text.Should().Be("Title");
    }

    [Fact]
    public void ParseBlockStructure_AllHeaderLevels_CreateCorrectKinds()
    {
        for (int level = 1; level <= 6; level++)
        {
            var html = $"<h{level}>Heading {level}</h{level}>";
            var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

            blocks.Should().HaveCount(1);
            var expectedKind = level switch
            {
                1 => BlockKind.Heading1,
                2 => BlockKind.Heading2,
                3 => BlockKind.Heading3,
                4 => BlockKind.Heading4,
                5 => BlockKind.Heading5,
                6 => BlockKind.Heading6,
            };
            blocks[0].Kind.Should().Be(expectedKind);
        }
    }

    [Fact]
    public void ParseBlockStructure_SimpleParagraph_CreatesParagraphBlock()
    {
        var html = "<p>Hello world</p>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[0].Content.Should().HaveCount(1);
        blocks[0].Content[0].Text.Should().Be("Hello world");
    }

    [Fact]
    public void ParseBlockStructure_MultipleParagraphs_CreatesMultipleBlocks()
    {
        var html = "<p>Para 1</p><p>Para 2</p>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(2);
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
        blocks[0].Content[0].Text.Should().Be("Para 1");
        blocks[1].Content[0].Text.Should().Be("Para 2");
    }

    [Fact]
    public void ParseBlockStructure_HeaderFollowedByParagraph_CreatesSeparateBlocks()
    {
        var html = "<h3>RPG.net</h3><p>Nothing substantial.</p>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(2);
        blocks[0].Kind.Should().Be(BlockKind.Heading3);
        blocks[0].Content[0].Text.Should().Be("RPG.net");
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].Content[0].Text.Should().Be("Nothing substantial.");
    }

    // --- Inline Content Tests ---

    [Fact]
    public void ParseInlineContent_PlainText_CreatesSimpleSegment()
    {
        var html = "Simple text";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("Simple text");
        inline[0].Format.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_StrongTag_SetsBoldFormat()
    {
        var html = "<strong>bold text</strong>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("bold text");
        inline[0].Format.Bold.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_BTag_SetsBoldFormat()
    {
        var html = "<b>bold text</b>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("bold text");
        inline[0].Format.Bold.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_EmTag_SetsItalicFormat()
    {
        var html = "<em>italic text</em>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("italic text");
        inline[0].Format.Italic.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_ITag_SetsItalicFormat()
    {
        var html = "<i>italic text</i>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("italic text");
        inline[0].Format.Italic.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_SpanWithColorStyle_ExtractsColorCorrectly()
    {
        var html = "<span style='color:red'>colored text</span>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("colored text");
        inline[0].Format.ForegroundColor.Should().NotBeNull();
        inline[0].Format.ForegroundColor?.R.Should().Be(255);
        inline[0].Format.ForegroundColor?.G.Should().Be(0);
        inline[0].Format.ForegroundColor?.B.Should().Be(0);
    }

    [Fact]
    public void ParseInlineContent_SpanWithHexColor_ExtractsColorCorrectly()
    {
        var html = "<span style='color:#FF0000'>red text</span>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("red text");
        inline[0].Format.ForegroundColor?.R.Should().Be(255);
        inline[0].Format.ForegroundColor?.G.Should().Be(0);
        inline[0].Format.ForegroundColor?.B.Should().Be(0);
    }

    [Fact]
    public void ParseInlineContent_BrTag_MarksHardBreak()
    {
        var html = "Line 1<br>Line 2";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(2);
        inline[0].Text.Should().Be("Line 1");
        inline[0].FollowedByHardBreak.Should().BeTrue();
        inline[1].Text.Should().Be("Line 2");
        inline[1].FollowedByHardBreak.Should().BeFalse();
    }

    [Fact]
    public void ParseInlineContent_BrTagSelfClosing_MarksHardBreak()
    {
        var html = "Line 1<br/>Line 2";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(2);
        inline[0].FollowedByHardBreak.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineContent_MixedFormatting_PreservesAllStyles()
    {
        var html = "Text with <strong>bold</strong> and <em>italic</em>";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        // Parser creates separate segments at each tag boundary
        inline.Should().HaveCountGreaterThanOrEqualTo(3);

        // Find the bold segment
        var boldSegment = inline.FirstOrDefault(s => s.Format.Bold);
        boldSegment.Should().NotBeNull();
        boldSegment!.Text.Should().Be("bold");

        // Find the italic segment
        var italicSegment = inline.FirstOrDefault(s => s.Format.Italic);
        italicSegment.Should().NotBeNull();
        italicSegment!.Text.Should().Contain("italic");
    }

    [Fact]
    public void ParseInlineContent_WhitespaceCollapse_NormalizesMultipleSpaces()
    {
        var html = "Text   with    multiple     spaces";
        var inline = HtmlBlockModelParser.ParseInlineContent(html, BlockKind.Paragraph);

        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("Text with multiple spaces");
    }

    // --- Markdown Conversion Tests ---

    [Fact]
    public void ConvertToMarkdown_SimpleHeader_FormatsWithHashes()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Heading3,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Title" }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("### Title");
    }

    [Fact]
    public void ConvertToMarkdown_Paragraph_FormatsAsText()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Simple paragraph" }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("Simple paragraph");
    }

    [Fact]
    public void ConvertToMarkdown_HeaderAndParagraph_SeparatesWithBlankLine()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Heading3,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Title" }
                }
            },
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Content" }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        var lines = markdown.Split('\n');
        lines.Should().HaveCountGreaterThan(2);
        lines[0].Should().Be("### Title");
        lines[1].Should().Be("");  // Blank line
        lines[2].Should().Be("Content");
    }

    [Fact]
    public void ConvertToMarkdown_ParagraphWithBold_AppliesBoldFormatting()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent
                    {
                        Text = "important",
                        Format = new InlineFormat { Bold = true }
                    }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("**important**");
    }

    [Fact]
    public void ConvertToMarkdown_ParagraphWithColor_PreservesColorTag()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent
                    {
                        Text = "red text",
                        Format = new InlineFormat { ForegroundColor = new RgbColor(255, 0, 0) }
                    }
                }
            }
        };

        var settings = new MarkdownOutputSettings { PreserveColors = true };
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks, settings);

        markdown.Should().Contain("<!--@fg:red-->");
        markdown.Should().Contain("red text");
        markdown.Should().Contain("<!--/@fg-->");
    }

    [Fact]
    public void ConvertToMarkdown_ParagraphWithHardBreak_UsesBackslashByDefault()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Line 1", FollowedByHardBreak = true },
                    new InlineContent { Text = "Line 2" }
                }
            }
        };

        var settings = new MarkdownOutputSettings { HardBreak = DocsCanvas.HardBreakStyle.Backslash };
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks, settings);

        markdown.Should().Contain("Line 1\\");
        var lines = markdown.Split('\n');
        lines.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ConvertToMarkdown_ParagraphWithHardBreak_UsesTrailingSpaces()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Paragraph,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "Line 1", FollowedByHardBreak = true },
                    new InlineContent { Text = "Line 2" }
                }
            }
        };

        var settings = new MarkdownOutputSettings { HardBreak = DocsCanvas.HardBreakStyle.TrailingSpaces };
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks, settings);

        // Line 1 should have trailing spaces before the newline
        markdown.Should().Contain("Line 1  ");
    }

    // --- Integration Tests ---

    [Fact]
    public void FullPipeline_HeaderFollowedByParagraph_KeepsSeparate()
    {
        var html = "<h3>RPG.net</h3><p>Nothing substantial.</p>";

        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        var lines = markdown.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lines.Should().HaveCountGreaterThanOrEqualTo(2);
        lines[0].Should().Contain("### RPG.net");
        lines[1].Should().Contain("Nothing substantial.");
    }

    [Fact]
    public void FullPipeline_HeaderWithFormattedParagraph_PreservesFormatting()
    {
        var html = "<h2>Title</h2><p>Text with <strong>bold</strong> and <em>italic</em></p>";

        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("## Title");
        markdown.Should().Contain("**bold**");
        markdown.Should().Contain("*italic*");
    }

    [Fact]
    public void FullPipeline_ParagraphWithColor_PreservesColorAndStructure()
    {
        var html = "<p><span style='color:#FF0000'>Red</span> and normal</p>";

        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var settings = new MarkdownOutputSettings { PreserveColors = true };
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks, settings);

        // Color is converted to color name if available ("red" instead of "#FF0000")
        markdown.Should().Contain("<!--@fg:");
        markdown.Should().Contain("-->Red<!--/@fg-->");
        markdown.Should().Contain("and normal");
    }

    // --- Phase 2: Lists Tests ---

    [Fact]
    public void ParseBlockStructure_SimpleUnorderedList_CreatesListBlock()
    {
        var html = "<ul><li>Item 1</li><li>Item 2</li></ul>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].Kind.Should().Be(BlockKind.UnorderedListItem);
        blocks[0].NestedBlocks.Should().HaveCount(2);
        blocks[0].NestedBlocks![0].Content[0].Text.Should().Be("Item 1");
        blocks[0].NestedBlocks[1].Content[0].Text.Should().Be("Item 2");
    }

    [Fact]
    public void ParseBlockStructure_SimpleOrderedList_CreatesOrderedListBlock()
    {
        var html = "<ol><li>First</li><li>Second</li></ol>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].Kind.Should().Be(BlockKind.OrderedListItem);
        blocks[0].NestedBlocks.Should().HaveCount(2);
        blocks[0].NestedBlocks![0].Content[0].Text.Should().Be("First");
        blocks[0].NestedBlocks[1].Content[0].Text.Should().Be("Second");
    }

    [Fact]
    public void ParseBlockStructure_SimpleBlockquote_CreatesBlockquoteBlock()
    {
        var html = "<blockquote>A wise quote</blockquote>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].Kind.Should().Be(BlockKind.Blockquote);
        blocks[0].Content.Should().HaveCount(1);
        blocks[0].Content[0].Text.Should().Be("A wise quote");
    }

    [Fact]
    public void ParseBlockStructure_ListWithFormattedItems_PreservesFormatting()
    {
        var html = "<ul><li>Item with <strong>bold</strong></li></ul>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);

        blocks.Should().HaveCount(1);
        blocks[0].NestedBlocks.Should().HaveCount(1);
        var item = blocks[0].NestedBlocks![0];

        // Should have multiple segments: "Item with ", "bold"
        item.Content.Should().HaveCountGreaterThanOrEqualTo(1);
        var boldSegment = item.Content.FirstOrDefault(s => s.Format.Bold);
        boldSegment.Should().NotBeNull();
        boldSegment!.Text.Should().Be("bold");
    }

    [Fact]
    public void ConvertToMarkdown_UnorderedList_FormatsWithDashes()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.UnorderedListItem,
                NestedBlocks = new List<BlockElement>
                {
                    new BlockElement
                    {
                        Kind = BlockKind.UnorderedListItem,
                        Content = new List<InlineContent>
                        {
                            new InlineContent { Text = "Item 1" }
                        }
                    },
                    new BlockElement
                    {
                        Kind = BlockKind.UnorderedListItem,
                        Content = new List<InlineContent>
                        {
                            new InlineContent { Text = "Item 2" }
                        }
                    }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("- Item 1");
        markdown.Should().Contain("- Item 2");
    }

    [Fact]
    public void ConvertToMarkdown_OrderedList_FormatsWithNumbers()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.OrderedListItem,
                NestedBlocks = new List<BlockElement>
                {
                    new BlockElement
                    {
                        Kind = BlockKind.OrderedListItem,
                        Content = new List<InlineContent>
                        {
                            new InlineContent { Text = "First" }
                        }
                    },
                    new BlockElement
                    {
                        Kind = BlockKind.OrderedListItem,
                        Content = new List<InlineContent>
                        {
                            new InlineContent { Text = "Second" }
                        }
                    }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("1. First");
        markdown.Should().Contain("2. Second");
    }

    [Fact]
    public void ConvertToMarkdown_Blockquote_FormatsWithGreaterThan()
    {
        var blocks = new List<BlockElement>
        {
            new BlockElement
            {
                Kind = BlockKind.Blockquote,
                Content = new List<InlineContent>
                {
                    new InlineContent { Text = "A famous quote" }
                }
            }
        };

        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("> A famous quote");
    }

    [Fact]
    public void FullPipeline_ListFollowedByParagraph_SeparatesBlocks()
    {
        var html = "<ul><li>Item</li></ul><p>After list</p>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        var lines = markdown.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lines.Should().HaveCountGreaterThanOrEqualTo(2);
        lines[0].Should().Contain("- Item");
        lines.Should().ContainSingle(l => l.Contains("After list"));
    }

    [Fact]
    public void FullPipeline_HeaderBlockquoteParagraph_AllSeparated()
    {
        var html = "<h2>Title</h2><blockquote>Quote</blockquote><p>Text</p>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("## Title");
        markdown.Should().Contain("> Quote");
        markdown.Should().Contain("Text");

        var lines = markdown.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lines.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void FullPipeline_ListWithFormattedItems_PreservesFormatting()
    {
        var html = "<ul><li>Item with <strong>bold</strong> text</li></ul>";
        var blocks = HtmlBlockModelParser.ParseBlockStructure(html);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        markdown.Should().Contain("- Item with");
        markdown.Should().Contain("**bold**");
        markdown.Should().Contain("text");
    }

    [Fact]
    public void RealWorldContent_LegWrackAnalysis_ParsesCorrectly()
    {
        // This test uses real clipboard content from a web page (leg-wrack-analysis.html)
        // to verify the parser handles real-world HTML structure
        string htmlFragment = File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(typeof(HtmlBlockModelParserTests).Assembly.Location) ?? "",
                "../../../BlockModel/leg_wrack_test.html"));

        if (string.IsNullOrEmpty(htmlFragment))
        {
            // Skip test if file not found
            return;
        }

        var blocks = HtmlBlockModelParser.ParseBlockStructure(htmlFragment);
        var markdown = HtmlBlockModelParser.ConvertToMarkdown(blocks);

        // Basic sanity checks
        blocks.Should().NotBeEmpty("Should parse blocks from HTML");
        markdown.Should().NotBeNullOrEmpty("Should generate markdown");
        markdown.Length.Should().BeGreaterThan(100, "Should produce substantial output");

        // Verify it contains expected elements
        markdown.Should().ContainAny("# ", "## ", "### ", "- ", "1. ", "> ")
            .And.Subject.Should().Contain("Effects");

        // Save markdown output for inspection
        string outFile = Path.Combine(
            Path.GetDirectoryName(typeof(HtmlBlockModelParserTests).Assembly.Location) ?? "",
            "../../../BlockModel/leg_wrack_output.md");
        File.WriteAllText(outFile, markdown);
    }
}
