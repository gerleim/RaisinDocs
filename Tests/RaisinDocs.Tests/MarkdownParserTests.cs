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

    // --- Inline parsing: strikethrough ---

    [Fact]
    public void Strikethrough_ParsedCorrectly()
    {
        var result = ParseBlocks("~~struck~~");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Should().Be(new StyledRun(0, 10, InlineStyle.Strikethrough));
    }

    [Fact]
    public void Strikethrough_WithSurroundingText()
    {
        var result = ParseBlocks("before ~~struck~~ after");
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 10, InlineStyle.Strikethrough));
        result[0].Runs[2].Should().Be(new StyledRun(17, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Strikethrough_Unclosed_IsNormal()
    {
        var result = ParseBlocks("~~unclosed");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    [Fact]
    public void Strikethrough_SingleTilde_IsNormal()
    {
        var result = ParseBlocks("~not struck~");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    // --- Block classification: blockquote ---

    [Fact]
    public void Blockquote_IsBlockquote()
    {
        var result = ParseBlocks("> quoted text");
        result[0].Kind.Should().Be(BlockKind.Blockquote);
    }

    [Fact]
    public void Blockquote_EmptyContent()
    {
        var result = ParseBlocks(">");
        result[0].Kind.Should().Be(BlockKind.Blockquote);
    }

    [Fact]
    public void GreaterThan_WithoutSpace_IsParagraph()
    {
        var result = ParseBlocks(">nospace");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
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

    // --- Inline parsing: images ---

    [Fact]
    public void Image_BasicSyntax_ParsedCorrectly()
    {
        var result = ParseBlocks("![alt](image.png)");
        result[0].Images.Should().HaveCount(1);
        var img = result[0].Images![0];
        img.Start.Should().Be(0);
        img.Length.Should().Be(17);
        img.AltText.Should().Be("alt");
        img.Url.Should().Be("image.png");
        img.Title.Should().BeNull();
    }

    [Fact]
    public void Image_WithTitle()
    {
        var result = ParseBlocks("![photo](pic.jpg \"My photo\")");
        result[0].Images.Should().HaveCount(1);
        var img = result[0].Images![0];
        img.Url.Should().Be("pic.jpg");
        img.Title.Should().Be("My photo");
    }

    [Fact]
    public void Image_WithSingleQuoteTitle()
    {
        var result = ParseBlocks("![a](b.png 'title')");
        result[0].Images![0].Title.Should().Be("title");
    }

    [Fact]
    public void Image_EmptyAlt()
    {
        var result = ParseBlocks("![](image.png)");
        result[0].Images.Should().HaveCount(1);
        var img = result[0].Images![0];
        img.AltText.Should().BeEmpty();
        img.Url.Should().Be("image.png");
    }

    [Fact]
    public void Image_InCodeSpan_NotParsed()
    {
        var result = ParseBlocks("`![not](image)`");
        result[0].Images.Should().BeNull();
        result[0].Runs[0].Style.Should().Be(InlineStyle.Code);
    }

    [Fact]
    public void Image_InFencedCode_NotParsed()
    {
        var result = ParseBlocks("```", "![not](image.png)", "```");
        result[1].Images.Should().BeNull();
    }

    [Fact]
    public void Image_WithSurroundingText()
    {
        var result = ParseBlocks("before ![img](x.png) after");
        result[0].Images.Should().HaveCount(1);
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 13, InlineStyle.Image));
        result[0].Runs[2].Should().Be(new StyledRun(20, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Image_MultiplePerBlock()
    {
        var result = ParseBlocks("![a](1.png) and ![b](2.png)");
        result[0].Images.Should().HaveCount(2);
        result[0].Images![0].Url.Should().Be("1.png");
        result[0].Images![1].Url.Should().Be("2.png");
    }

    [Fact]
    public void Image_UnclosedBracket_NotParsed()
    {
        var result = ParseBlocks("![alt text without closing");
        result[0].Images.Should().BeNull();
    }

    [Fact]
    public void Image_UnclosedParen_NotParsed()
    {
        var result = ParseBlocks("![alt](no-close-paren");
        result[0].Images.Should().BeNull();
    }

    [Fact]
    public void Image_NoParenAfterBracket_NotParsed()
    {
        var result = ParseBlocks("![alt] no paren");
        result[0].Images.Should().BeNull();
    }

    [Fact]
    public void Image_SuppressesEmphasis()
    {
        var result = ParseBlocks("![**not bold**](path.png)");
        result[0].Images.Should().HaveCount(1);
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Image);
    }

    [Fact]
    public void Image_AngleBracketDestination()
    {
        var result = ParseBlocks("![alt](<path with spaces.png>)");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].Url.Should().Be("path with spaces.png");
    }

    [Fact]
    public void Image_BalancedParensInUrl()
    {
        var result = ParseBlocks("![alt](wiki/File_(1).png)");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].Url.Should().Be("wiki/File_(1).png");
    }

    [Fact]
    public void Image_NestedBracketsInAlt()
    {
        var result = ParseBlocks("![text [with] brackets](url.png)");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("text [with] brackets");
    }

    [Fact]
    public void Image_StyleArrayCoversEntireSpan()
    {
        var result = ParseBlocks("![alt](url.png)");
        var runs = result[0].Runs;
        runs.Should().HaveCount(1);
        runs[0].Start.Should().Be(0);
        runs[0].Length.Should().Be(15);
        runs[0].Style.Should().Be(InlineStyle.Image);
    }

    [Fact]
    public void Image_NoImages_PropertyIsNull()
    {
        var result = ParseBlocks("just plain text");
        result[0].Images.Should().BeNull();
    }

    // --- Tables ---

    [Fact]
    public void Table_BasicDetection()
    {
        var result = ParseBlocks("| A | B |", "| --- | --- |", "| 1 | 2 |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[1].Kind.Should().Be(BlockKind.TableSeparatorRow);
        result[2].Kind.Should().Be(BlockKind.TableDataRow);
    }

    [Fact]
    public void Table_SeparatorRow_IsTableSeparator()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |");
        result[1].IsTableSeparator.Should().BeTrue();
        result[0].IsTableSeparator.Should().BeFalse();
        result[2].IsTableSeparator.Should().BeFalse();
    }

    [Fact]
    public void Table_SharedTableInfo()
    {
        var result = ParseBlocks("| A | B |", "| --- | --- |", "| 1 | 2 |");
        result[0].Table.Should().NotBeNull();
        result[0].Table.Should().BeSameAs(result[1].Table);
        result[0].Table.Should().BeSameAs(result[2].Table);
        result[0].Table!.ColumnCount.Should().Be(2);
    }

    [Fact]
    public void Table_AlignmentLeft()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |");
        result[0].Table!.Alignments[0].Should().Be(ColumnAlignment.Left);
    }

    [Fact]
    public void Table_AlignmentCenter()
    {
        var result = ParseBlocks("| A |", "| :---: |", "| 1 |");
        result[0].Table!.Alignments[0].Should().Be(ColumnAlignment.Center);
    }

    [Fact]
    public void Table_AlignmentRight()
    {
        var result = ParseBlocks("| A |", "| ---: |", "| 1 |");
        result[0].Table!.Alignments[0].Should().Be(ColumnAlignment.Right);
    }

    [Fact]
    public void Table_MixedAlignments()
    {
        var result = ParseBlocks("| A | B | C |", "| --- | :---: | ---: |", "| 1 | 2 | 3 |");
        var aligns = result[0].Table!.Alignments;
        aligns[0].Should().Be(ColumnAlignment.Left);
        aligns[1].Should().Be(ColumnAlignment.Center);
        aligns[2].Should().Be(ColumnAlignment.Right);
    }

    [Fact]
    public void Table_CellBoundaries()
    {
        var result = ParseBlocks("| A | B |", "| --- | --- |", "| 1 | 2 |");
        var headerCells = result[0].TableRow!.Cells;
        headerCells.Should().HaveCount(2);
        // "| A | B |" — cells are " A " and " B "
        "| A | B |".Substring(headerCells[0].Start, headerCells[0].Length).Should().Contain("A");
        "| A | B |".Substring(headerCells[1].Start, headerCells[1].Length).Should().Contain("B");
    }

    [Fact]
    public void Table_DataRowCellBoundaries()
    {
        var result = ParseBlocks("| A |", "| --- |", "| hello |");
        var cells = result[2].TableRow!.Cells;
        cells.Should().HaveCount(1);
        "| hello |".Substring(cells[0].Start, cells[0].Length).Should().Contain("hello");
    }

    [Fact]
    public void Table_WithoutLeadingTrailingPipes()
    {
        var result = ParseBlocks("A | B", "--- | ---", "1 | 2");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[1].Kind.Should().Be(BlockKind.TableSeparatorRow);
        result[2].Kind.Should().Be(BlockKind.TableDataRow);
        result[0].Table!.ColumnCount.Should().Be(2);
    }

    [Fact]
    public void Table_SingleColumn()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[0].Table!.ColumnCount.Should().Be(1);
    }

    [Fact]
    public void Table_InlineStylesInCells()
    {
        var result = ParseBlocks("| **bold** | `code` |", "| --- | --- |", "| data |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[0].Runs.Should().Contain(r => r.Style == InlineStyle.Bold);
        result[0].Runs.Should().Contain(r => r.Style == InlineStyle.Code);
    }

    [Fact]
    public void Table_EscapedPipe_NotCellBoundary()
    {
        var result = ParseBlocks(@"| A \| B | C |", "| --- | --- |", "| 1 | 2 |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[0].Table!.ColumnCount.Should().Be(2);
        var cells = result[0].TableRow!.Cells;
        cells.Should().HaveCount(2);
    }

    [Fact]
    public void Table_InsideFencedCode_NotDetected()
    {
        var result = ParseBlocks("```", "| A | B |", "| --- | --- |", "| 1 | 2 |", "```");
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[3].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void Table_FollowedByParagraph()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |", "normal text");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[1].Kind.Should().Be(BlockKind.TableSeparatorRow);
        result[2].Kind.Should().Be(BlockKind.TableDataRow);
        result[3].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void Table_MultipleDataRows()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |", "| 2 |", "| 3 |");
        result[2].Kind.Should().Be(BlockKind.TableDataRow);
        result[3].Kind.Should().Be(BlockKind.TableDataRow);
        result[4].Kind.Should().Be(BlockKind.TableDataRow);
    }

    [Fact]
    public void Table_InvalidSeparator_NotDetected()
    {
        var result = ParseBlocks("| A | B |", "| not separator |", "| 1 | 2 |");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
        result[1].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void Table_ColumnCountMismatch_NotDetected()
    {
        var result = ParseBlocks("| A | B | C |", "| --- | --- |", "| 1 | 2 |");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void Table_LeftColonAlignment()
    {
        var result = ParseBlocks("| A |", "| :--- |", "| 1 |");
        result[0].Table!.Alignments[0].Should().Be(ColumnAlignment.Left);
    }

    [Fact]
    public void Table_TwoSeparateTables()
    {
        var result = ParseBlocks("| A |", "| --- |", "| 1 |", "text", "| B |", "| --- |", "| 2 |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[0].Table.Should().NotBeSameAs(result[4].Table);
        result[3].Kind.Should().Be(BlockKind.Paragraph);
        result[4].Kind.Should().Be(BlockKind.TableHeaderRow);
    }
}
