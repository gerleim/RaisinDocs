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

    // --- Task list items ---

    [Fact]
    public void TaskListUnchecked_Dash()
    {
        var result = ParseBlocks("- [ ] buy milk");
        result[0].Kind.Should().Be(BlockKind.TaskListItemUnchecked);
    }

    [Fact]
    public void TaskListChecked_Dash()
    {
        var result = ParseBlocks("- [x] buy milk");
        result[0].Kind.Should().Be(BlockKind.TaskListItemChecked);
    }

    [Fact]
    public void TaskListChecked_UppercaseX()
    {
        var result = ParseBlocks("- [X] buy milk");
        result[0].Kind.Should().Be(BlockKind.TaskListItemChecked);
    }

    [Fact]
    public void TaskListUnchecked_Star()
    {
        var result = ParseBlocks("* [ ] buy milk");
        result[0].Kind.Should().Be(BlockKind.TaskListItemUnchecked);
    }

    [Fact]
    public void TaskListChecked_Star()
    {
        var result = ParseBlocks("* [x] buy milk");
        result[0].Kind.Should().Be(BlockKind.TaskListItemChecked);
    }

    [Fact]
    public void TaskList_InlineStyles()
    {
        var result = ParseBlocks("- [ ] **bold** task");
        result[0].Kind.Should().Be(BlockKind.TaskListItemUnchecked);
        result[0].Runs.Should().Contain(new StyledRun(6, 8, InlineStyle.Bold));
    }

    [Fact]
    public void TaskList_NoSpaceAfterBracket_IsRegularList()
    {
        var result = ParseBlocks("- [x]word");
        result[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    [Fact]
    public void TaskList_InvalidChar_IsRegularList()
    {
        var result = ParseBlocks("- [a] text");
        result[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    [Fact]
    public void TaskList_InsideFencedCode_NotDetected()
    {
        var result = ParseBlocks("```", "- [ ] not a task", "```");
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void TaskList_EmptyTextAfterCheckbox()
    {
        var result = ParseBlocks("- [ ] ");
        result[0].Kind.Should().Be(BlockKind.TaskListItemUnchecked);
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

    [Fact]
    public void Table_LastRow_HasSameRunsAsOtherRows()
    {
        var result = ParseBlocks(
            "| Shortcut | Action |",
            "|---|---|",
            "| Ctrl+B | Toggle bold |",
            "| Ctrl+I | Toggle italic |",
            "| Ctrl+Z | Undo |",
            "| Ctrl+Y | Redo |",
            "| Ctrl+X / C / V | Cut / Copy / Paste |",
            "| Tab | Toggle Source / Visual mode |"
        );

        // All data rows should have single Normal run (no unexpected styles)
        for (int i = 2; i < result.Count; i++)
        {
            result[i].Kind.Should().Be(BlockKind.TableDataRow, $"block {i}");
            result[i].Runs.Should().HaveCount(1, $"block {i} should have 1 run");
            result[i].Runs[0].Style.Should().Be(InlineStyle.Normal, $"block {i} run style");
        }

        // Last row cells should parse correctly
        var lastRow = result[7];
        lastRow.TableRow!.Cells.Should().HaveCount(2);
    }

    // --- Inline parsing: links ---

    [Fact]
    public void Link_BasicSyntax_ParsedCorrectly()
    {
        var result = ParseBlocks("[click here](https://example.com)");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Start.Should().Be(0);
        link.Length.Should().Be(33);
        link.Text.Should().Be("click here");
        link.Url.Should().Be("https://example.com");
        link.Title.Should().BeNull();
    }

    [Fact]
    public void Link_WithTitle()
    {
        var result = ParseBlocks("[text](url \"My Title\")");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Url.Should().Be("url");
        link.Title.Should().Be("My Title");
    }

    [Fact]
    public void Link_AngleBracketDestination()
    {
        var result = ParseBlocks("[text](<url with spaces>)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("url with spaces");
    }

    [Fact]
    public void Link_WithInlineBold()
    {
        var result = ParseBlocks("[**bold** text](url)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("**bold** text");
        // The whole range is marked as Link, suppressing bold
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Link);
    }

    [Fact]
    public void Link_MissingClosingParen_NotParsed()
    {
        var result = ParseBlocks("[text](url");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Link_ImageSyntax_StaysAsImage()
    {
        var result = ParseBlocks("![alt](image.png)");
        result[0].Images.Should().HaveCount(1);
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Link_InsideFencedCode_NotParsed()
    {
        var result = ParseBlocks("```", "[text](url)", "```");
        result[1].Links.Should().BeNull();
    }

    [Fact]
    public void Link_InsideCodeSpan_NotParsed()
    {
        var result = ParseBlocks("`[text](url)`");
        result[0].Links.Should().BeNull();
        result[0].Runs[0].Style.Should().Be(InlineStyle.Code);
    }

    [Fact]
    public void Link_MultiplePerBlock()
    {
        var result = ParseBlocks("[a](url1) and [b](url2)");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].Text.Should().Be("a");
        result[0].Links![0].Url.Should().Be("url1");
        result[0].Links![1].Text.Should().Be("b");
        result[0].Links![1].Url.Should().Be("url2");
    }

    [Fact]
    public void Link_AdjacentToImage()
    {
        var result = ParseBlocks("[link](a) ![img](b)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("link");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("img");
    }

    [Fact]
    public void Link_EmptyText()
    {
        var result = ParseBlocks("[](url)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().BeEmpty();
        result[0].Links![0].Url.Should().Be("url");
    }

    [Fact]
    public void Link_WithSurroundingText()
    {
        var result = ParseBlocks("before [link](url) after");
        result[0].Links.Should().HaveCount(1);
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 7, InlineStyle.Normal));
        result[0].Runs[1].Should().Be(new StyledRun(7, 11, InlineStyle.Link));
        result[0].Runs[2].Should().Be(new StyledRun(18, 6, InlineStyle.Normal));
    }

    [Fact]
    public void Link_StyleArrayCoversEntireSpan()
    {
        var result = ParseBlocks("[text](url)");
        var runs = result[0].Runs;
        runs.Should().HaveCount(1);
        runs[0].Start.Should().Be(0);
        runs[0].Length.Should().Be(11);
        runs[0].Style.Should().Be(InlineStyle.Link);
    }

    [Fact]
    public void Link_NoLinks_PropertyIsNull()
    {
        var result = ParseBlocks("just plain text");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Link_UnclosedBracket_NotParsed()
    {
        var result = ParseBlocks("[text without closing");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Link_NoBracketAfterClose_NotParsed()
    {
        var result = ParseBlocks("[text] no paren");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Link_BalancedParensInUrl()
    {
        var result = ParseBlocks("[wiki](https://en.wikipedia.org/wiki/Foo_(bar))");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://en.wikipedia.org/wiki/Foo_(bar)");
    }

    // --- Autolinks ---

    [Fact]
    public void Autolink_HttpsUrl()
    {
        var result = ParseBlocks("visit https://example.com today");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("https://example.com");
        link.Url.Should().Be("https://example.com");
        link.Start.Should().Be(6);
        link.Length.Should().Be(19);
    }

    [Fact]
    public void Autolink_HttpUrl()
    {
        var result = ParseBlocks("see http://example.com/path");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("http://example.com/path");
    }

    [Fact]
    public void Autolink_WwwUrl()
    {
        var result = ParseBlocks("go to www.example.com");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("www.example.com");
        link.Url.Should().Be("http://www.example.com");
    }

    [Fact]
    public void Autolink_TrailingPunctuation_Trimmed()
    {
        var result = ParseBlocks("see https://example.com.");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Autolink_TrailingComma_Trimmed()
    {
        var result = ParseBlocks("see https://example.com, and");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Autolink_InsideCodeSpan_NotDetected()
    {
        var result = ParseBlocks("`https://example.com`");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Autolink_InsideExistingLink_NotDetected()
    {
        var result = ParseBlocks("[click](https://example.com)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("click");
    }

    [Fact]
    public void Autolink_Multiple()
    {
        var result = ParseBlocks("https://a.com and https://b.com");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].Url.Should().Be("https://a.com");
        result[0].Links![1].Url.Should().Be("https://b.com");
    }

    [Fact]
    public void Autolink_BalancedParens()
    {
        var result = ParseBlocks("see https://en.wikipedia.org/wiki/Foo_(bar) ok");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://en.wikipedia.org/wiki/Foo_(bar)");
    }

    [Fact]
    public void Autolink_UnbalancedTrailingParen_Trimmed()
    {
        var result = ParseBlocks("(see https://example.com)");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Autolink_AtEndOfLine()
    {
        var result = ParseBlocks("visit https://example.com");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Autolink_WithQueryString()
    {
        var result = ParseBlocks("https://example.com/search?q=test&page=1");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com/search?q=test&page=1");
    }

    [Fact]
    public void Autolink_TextEqualsUrl()
    {
        var result = ParseBlocks("https://example.com");
        var link = result[0].Links![0];
        link.Text.Should().Be(link.Url);
    }

    [Fact]
    public void Autolink_PrefixOnly_NotDetected()
    {
        var result = ParseBlocks("see https:// end");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void Autolink_AdjacentToTraditionalLink()
    {
        var result = ParseBlocks("[a](url1) https://b.com");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].Text.Should().Be("a");
        result[0].Links![1].Text.Should().Be("https://b.com");
    }

    // --- Reference Links and Images ---

    [Fact]
    public void RefLink_FullForm()
    {
        var result = ParseBlocks("[click here][docs]", "[docs]: https://example.com");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("click here");
        link.Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_CollapsedForm()
    {
        var result = ParseBlocks("[docs][]", "[docs]: https://example.com");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("docs");
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_CaseInsensitive()
    {
        var result = ParseBlocks("[click][DOCS]", "[docs]: https://example.com");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_UndefinedLabel_NotLinked()
    {
        var result = ParseBlocks("[click][missing]");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void RefLink_WithTitle()
    {
        var result = ParseBlocks("[click][docs]", "[docs]: https://example.com \"Example\"");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Title.Should().Be("Example");
    }

    [Fact]
    public void RefImage_FullForm()
    {
        var result = ParseBlocks("![screenshot][img1]", "[img1]: ./image.png");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("screenshot");
        result[0].Images![0].Url.Should().Be("./image.png");
    }

    [Fact]
    public void RefImage_CollapsedForm()
    {
        var result = ParseBlocks("![logo][]", "[logo]: ./logo.png");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("logo");
        result[0].Images![0].Url.Should().Be("./logo.png");
    }

    [Fact]
    public void RefLink_MultipleDefinitions()
    {
        var result = ParseBlocks("[a][d1] and [b][d2]", "[d1]: https://a.com", "[d2]: https://b.com");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].Url.Should().Be("https://a.com");
        result[0].Links![1].Url.Should().Be("https://b.com");
    }

    [Fact]
    public void RefLink_DefinitionInsideFencedCode_Ignored()
    {
        var result = ParseBlocks("[click][docs]", "```", "[docs]: https://example.com", "```");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void RefLink_FirstDefinitionWins()
    {
        var result = ParseBlocks("[click][docs]", "[docs]: https://first.com", "[docs]: https://second.com");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://first.com");
    }

    [Fact]
    public void LinkDefinition_BlockKind()
    {
        var result = ParseBlocks("[docs]: https://example.com");
        result[0].Kind.Should().Be(BlockKind.LinkDefinition);
    }

    [Fact]
    public void LinkDefinition_IsSkippedInVisual()
    {
        var result = ParseBlocks("[docs]: https://example.com");
        result[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void LinkDefinition_AngleBracketUrl()
    {
        var result = ParseBlocks("[click][docs]", "[docs]: <https://example.com>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_StyleCoverage()
    {
        var result = ParseBlocks("[text][ref]", "[ref]: https://example.com");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Link);
    }

    // --- IsTrailingHardBreak ---

    [Fact]
    public void IsTrailingHardBreak_SimpleBackslash_ReturnsTrue()
    {
        var parsed = ParseBlocks("hello\\")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "hello\\").Should().BeTrue();
    }

    [Fact]
    public void IsTrailingHardBreak_NoBackslash_ReturnsFalse()
    {
        var parsed = ParseBlocks("hello")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "hello").Should().BeFalse();
    }

    [Fact]
    public void IsTrailingHardBreak_EscapedBackslash_ReturnsFalse()
    {
        var parsed = ParseBlocks("hello\\\\")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "hello\\\\").Should().BeFalse();
    }

    [Fact]
    public void IsTrailingHardBreak_TripleBackslash_ReturnsTrue()
    {
        var parsed = ParseBlocks("hi\\\\\\")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "hi\\\\\\").Should().BeTrue();
    }

    [Fact]
    public void IsTrailingHardBreak_InCodeSpan_ReturnsFalse()
    {
        var parsed = ParseBlocks("`code\\`")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "`code\\`").Should().BeFalse();
    }

    [Fact]
    public void IsTrailingHardBreak_InFencedCode_ReturnsFalse()
    {
        var blocks = ParseBlocks("```", "path\\", "```");
        MarkdownParser.IsTrailingHardBreak(blocks[1], "path\\").Should().BeFalse();
    }
}
