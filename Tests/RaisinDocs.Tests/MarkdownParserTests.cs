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
    public void HashAlone_IsHeading()
    {
        var result = ParseBlocks("#");
        result[0].Kind.Should().Be(BlockKind.Heading1);
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
    public void DashAlone_IsListItem()
    {
        var result = ParseBlocks("-");
        result[0].Kind.Should().Be(BlockKind.UnorderedListItem);
    }

    // --- Ordered list items ---

    [Fact]
    public void OrderedList_DotDelimiter()
    {
        var result = ParseBlocks("1. item");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_ParenDelimiter()
    {
        var result = ParseBlocks("1) item");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_MultiDigit()
    {
        var result = ParseBlocks("123. item");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_NineDigits_Valid()
    {
        var result = ParseBlocks("999999999. item");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_TenDigits_IsParagraph()
    {
        var result = ParseBlocks("1234567890. item");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void OrderedList_ZeroStart()
    {
        var result = ParseBlocks("0. item");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_NoSpace_IsParagraph()
    {
        var result = ParseBlocks("1.item");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void OrderedList_NoContent_Valid()
    {
        var result = ParseBlocks("1. ");
        result[0].Kind.Should().Be(BlockKind.OrderedListItem);
    }

    [Fact]
    public void OrderedList_PrefixLength_SingleDigitDot()
    {
        MarkdownParser.GetOrderedListPrefixLength("1. item").Should().Be(3);
    }

    [Fact]
    public void OrderedList_PrefixLength_MultiDigitParen()
    {
        MarkdownParser.GetOrderedListPrefixLength("12) item").Should().Be(4);
    }

    [Fact]
    public void OrderedList_PrefixLength_NotOrdered()
    {
        MarkdownParser.GetOrderedListPrefixLength("not a list").Should().Be(0);
    }

    // --- Content column ---

    [Fact]
    public void ContentColumn_UnorderedList()
    {
        var blocks = ParseBlocks("- item");
        blocks[0].ContentColumn.Should().Be(2);
    }

    [Fact]
    public void ContentColumn_OrderedList_SingleDigit()
    {
        var blocks = ParseBlocks("1. item");
        blocks[0].ContentColumn.Should().Be(3);
    }

    [Fact]
    public void ContentColumn_OrderedList_MultiDigit()
    {
        var blocks = ParseBlocks("10. item");
        blocks[0].ContentColumn.Should().Be(4);
    }

    [Fact]
    public void ContentColumn_OrderedList_Paren()
    {
        var blocks = ParseBlocks("1) item");
        blocks[0].ContentColumn.Should().Be(3);
    }

    [Fact]
    public void ContentColumn_TaskList()
    {
        var blocks = ParseBlocks("- [ ] task");
        blocks[0].ContentColumn.Should().Be(2);
    }

    [Fact]
    public void ContentColumn_Blockquote()
    {
        var blocks = ParseBlocks("> quoted");
        blocks[0].ContentColumn.Should().Be(2);
    }

    [Fact]
    public void ContentColumn_ExtraSpacesAfterMarker()
    {
        var blocks = ParseBlocks(" -    one");
        blocks[0].ContentColumn.Should().Be(6);
    }

    [Fact]
    public void ContentColumn_FivePlusSpaces_CollapsesToMarkerPlusOne()
    {
        var blocks = ParseBlocks("-      code");
        blocks[0].ContentColumn.Should().Be(2);
    }

    [Fact]
    public void ContentColumn_ExtraSpacesAfterMarker_OrderedList()
    {
        var blocks = ParseBlocks("1.   one");
        blocks[0].ContentColumn.Should().Be(5);
    }

    [Fact]
    public void ContentColumn_MarkerOnly_NoContent()
    {
        var blocks = ParseBlocks("- ");
        blocks[0].ContentColumn.Should().Be(2);
    }

    [Fact]
    public void ContentColumn_Paragraph_IsZero()
    {
        var blocks = ParseBlocks("plain text");
        blocks[0].ContentColumn.Should().Be(0);
    }

    [Fact]
    public void ContentColumn_Heading_IsZero()
    {
        var blocks = ParseBlocks("# heading");
        blocks[0].ContentColumn.Should().Be(0);
    }

    // --- Continuation with extra spaces after marker (CommonMark §5.3 examples 257-258) ---

    [Fact]
    public void IndentedContinuation_ExtraSpaces_BelowContentColumn_NotContinuation()
    {
        // " -    one" has content column 6, so 5 spaces is not enough
        var blocks = ParseBlocks(" -    one", "", "     two");
        blocks[2].IsIndentedContinuation.Should().BeFalse();
    }

    [Fact]
    public void IndentedContinuation_ExtraSpaces_AtContentColumn_IsContinuation()
    {
        // " -    one" has content column 6, so 6 spaces is enough
        var blocks = ParseBlocks(" -    one", "", "      two");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
    }

    [Fact]
    public void IndentedContinuation_TwoSpacesAfterMarker_SixSpaces_IsParagraph()
    {
        // "-  one" has content column 3, relative indent 6-3=3 < 4 → paragraph continuation
        var blocks = ParseBlocks("-  one", "", "      two");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
    }

    [Fact]
    public void IndentedContinuation_OneSpaceAfterMarker_SixSpaces_IsCodeBlock()
    {
        // "- one" has content column 2, relative indent 6-2=4 → indented code within list item
        var blocks = ParseBlocks("- one", "", "      two");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedContinuation_AtContentColumn_IsParagraph()
    {
        // "- one" has content column 2, relative indent 2-2=0 → paragraph continuation
        var blocks = ParseBlocks("- one", "", "  two");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void IndentedContinuation_FiveSpaces_IsParagraph()
    {
        // "- one" has content column 2, relative indent 5-2=3 < 4 → paragraph
        var blocks = ParseBlocks("- one", "", "     two");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void IndentedContinuation_BelowContentColumn_NotContinuation()
    {
        // "- one" has content column 2, 1 space < 2 → not a continuation
        var blocks = ParseBlocks("- one", "", " two");
        blocks[2].IsIndentedContinuation.Should().BeFalse();
    }

    // --- Lazy continuation ---

    [Fact]
    public void LazyContinuation_ParagraphAfterUnorderedList()
    {
        var blocks = ParseBlocks("- item", "continuation");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void LazyContinuation_ParagraphAfterOrderedList()
    {
        var blocks = ParseBlocks("1. item", "continuation");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void LazyContinuation_ParagraphAfterBlockquote()
    {
        var blocks = ParseBlocks("> quote", "continuation");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void LazyContinuation_MultipleParagraphs()
    {
        var blocks = ParseBlocks("- item", "second line", "third line");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
        blocks[2].IsLazyContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void LazyContinuation_StopsAtBlankLine()
    {
        var blocks = ParseBlocks("1. item", "", "not continuation");
        blocks[2].IsLazyContinuation.Should().BeFalse();
        blocks[2].OwnerBlock.Should().Be(-1);
    }

    [Fact]
    public void LazyContinuation_StopsAtHeading()
    {
        var blocks = ParseBlocks("- item", "# heading");
        blocks[1].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void LazyContinuation_StopsAtListItem()
    {
        var blocks = ParseBlocks("- item one", "- item two");
        blocks[1].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void LazyContinuation_StopsAtOrderedListItem()
    {
        var blocks = ParseBlocks("- item", "1. ordered");
        blocks[1].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void LazyContinuation_StopsAtBlockquote()
    {
        var blocks = ParseBlocks("- item", "> quote");
        blocks[1].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void LazyContinuation_StopsAtFencedCode()
    {
        var blocks = ParseBlocks("- item", "```");
        blocks[1].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void LazyContinuation_TaskList()
    {
        var blocks = ParseBlocks("- [ ] task", "continuation");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void LazyContinuation_OwnerNotSetOnNonContinuation()
    {
        var blocks = ParseBlocks("plain text", "more text");
        blocks[1].IsLazyContinuation.Should().BeFalse();
        blocks[1].OwnerBlock.Should().Be(-1);
    }

    // --- Indented continuation ---

    [Fact]
    public void IndentedContinuation_AfterBlankLine_OrderedList()
    {
        var blocks = ParseBlocks("1. item", "", "   continuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_AfterBlankLine_MultiDigitOrderedList()
    {
        var blocks = ParseBlocks("10. item", "", "    continuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_AfterBlankLine_UnorderedList()
    {
        var blocks = ParseBlocks("- item", "", "  continuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_AfterBlankLine_Blockquote()
    {
        var blocks = ParseBlocks("> quote", "", "  continuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_NotEnoughIndent()
    {
        var blocks = ParseBlocks("1. item", "", "  not enough");
        blocks[2].IsIndentedContinuation.Should().BeFalse();
        blocks[2].OwnerBlock.Should().Be(-1);
    }

    [Fact]
    public void IndentedContinuation_NoIndentAfterBlank()
    {
        var blocks = ParseBlocks("1. item", "", "no indent");
        blocks[2].IsIndentedContinuation.Should().BeFalse();
        blocks[2].OwnerBlock.Should().Be(-1);
    }

    [Fact]
    public void IndentedContinuation_MultipleBlankLines()
    {
        var blocks = ParseBlocks("- item", "", "", "  continuation");
        blocks[3].IsIndentedContinuation.Should().BeTrue();
        blocks[3].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_MultipleParagraphs()
    {
        var blocks = ParseBlocks("- first", "", "  second", "", "  third");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
        blocks[4].IsIndentedContinuation.Should().BeTrue();
        blocks[4].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_LazyThenIndented()
    {
        var blocks = ParseBlocks("- first", "lazy", "", "  indented");
        blocks[1].IsLazyContinuation.Should().BeTrue();
        blocks[1].OwnerBlock.Should().Be(0);
        blocks[3].IsIndentedContinuation.Should().BeTrue();
        blocks[3].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_IsNotLazy()
    {
        var blocks = ParseBlocks("- item", "", "  continuation");
        blocks[2].IsLazyContinuation.Should().BeFalse();
    }

    [Fact]
    public void IndentedContinuation_BlankLineMarkedWithOwner()
    {
        var blocks = ParseBlocks("- item", "", "  continuation");
        blocks[1].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_MultipleBlankLinesMarkedWithOwner()
    {
        var blocks = ParseBlocks("- item", "", "", "  continuation");
        blocks[1].OwnerBlock.Should().Be(0);
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void IndentedContinuation_BlankLineNotMarkedWhenNoContinuation()
    {
        var blocks = ParseBlocks("- item", "", "no indent");
        blocks[1].OwnerBlock.Should().Be(-1);
    }

    // --- 0–3 space prefix tolerance ---

    [Theory]
    [InlineData("# heading", BlockKind.Heading1)]
    [InlineData(" # heading", BlockKind.Heading1)]
    [InlineData("  # heading", BlockKind.Heading1)]
    [InlineData("   # heading", BlockKind.Heading1)]
    [InlineData("    # heading", BlockKind.IndentedCodeLine)]
    public void PrefixTolerance_Headings(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("## h2", BlockKind.Heading2)]
    [InlineData("   ## h2", BlockKind.Heading2)]
    [InlineData("### h3", BlockKind.Heading3)]
    [InlineData("   ### h3", BlockKind.Heading3)]
    [InlineData("#### h4", BlockKind.Heading4)]
    [InlineData("   #### h4", BlockKind.Heading4)]
    [InlineData("##### h5", BlockKind.Heading5)]
    [InlineData("   ##### h5", BlockKind.Heading5)]
    [InlineData("###### h6", BlockKind.Heading6)]
    [InlineData("   ###### h6", BlockKind.Heading6)]
    public void PrefixTolerance_AllHeadingLevels(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("- item", BlockKind.UnorderedListItem)]
    [InlineData(" - item", BlockKind.UnorderedListItem)]
    [InlineData("  - item", BlockKind.UnorderedListItem)]
    [InlineData("   - item", BlockKind.UnorderedListItem)]
    [InlineData("    - item", BlockKind.IndentedCodeLine)]
    [InlineData("  * item", BlockKind.UnorderedListItem)]
    public void PrefixTolerance_UnorderedList(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("1. item", BlockKind.OrderedListItem)]
    [InlineData(" 1. item", BlockKind.OrderedListItem)]
    [InlineData("  1. item", BlockKind.OrderedListItem)]
    [InlineData("   1. item", BlockKind.OrderedListItem)]
    [InlineData("    1. item", BlockKind.IndentedCodeLine)]
    [InlineData("  10. item", BlockKind.OrderedListItem)]
    public void PrefixTolerance_OrderedList(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("> quote", BlockKind.Blockquote)]
    [InlineData(" > quote", BlockKind.Blockquote)]
    [InlineData("  > quote", BlockKind.Blockquote)]
    [InlineData("   > quote", BlockKind.Blockquote)]
    [InlineData("    > quote", BlockKind.IndentedCodeLine)]
    public void PrefixTolerance_Blockquote(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("- [ ] task", BlockKind.TaskListItemUnchecked)]
    [InlineData("  - [ ] task", BlockKind.TaskListItemUnchecked)]
    [InlineData("   - [x] task", BlockKind.TaskListItemChecked)]
    [InlineData("    - [ ] task", BlockKind.IndentedCodeLine)]
    public void PrefixTolerance_TaskList(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Fact]
    public void PrefixTolerance_LeadingSpaces_Stored()
    {
        MarkdownParser.ClassifyBlock("   # heading", out int ls);
        ls.Should().Be(3);
    }

    [Fact]
    public void PrefixTolerance_LeadingSpaces_ZeroForParagraph()
    {
        MarkdownParser.ClassifyBlock("    # heading", out int ls);
        ls.Should().Be(0);
    }

    [Fact]
    public void PrefixTolerance_LeadingSpaces_ZeroForPlainParagraph()
    {
        MarkdownParser.ClassifyBlock("   plain text", out int ls);
        ls.Should().Be(0);
    }

    [Fact]
    public void PrefixTolerance_ContentColumn_IncludesLeadingSpaces()
    {
        var blocks = ParseBlocks("  - item");
        blocks[0].ContentColumn.Should().Be(4);
    }

    [Fact]
    public void PrefixTolerance_ContentColumn_OrderedListWithLeadingSpaces()
    {
        var blocks = ParseBlocks("  1. item");
        blocks[0].ContentColumn.Should().Be(5);
    }

    [Fact]
    public void PrefixTolerance_ContentColumn_BlockquoteWithLeadingSpaces()
    {
        var blocks = ParseBlocks("   > quote");
        blocks[0].ContentColumn.Should().Be(5);
    }

    [Fact]
    public void PrefixTolerance_FencedCode_ThreeSpaces()
    {
        MarkdownParser.GetFenceBacktickCount("   ```").Should().BeGreaterThan(0);
    }

    [Fact]
    public void PrefixTolerance_FencedCode_FourSpaces()
    {
        MarkdownParser.GetFenceBacktickCount("    ```").Should().Be(0);
    }

    [Fact]
    public void PrefixTolerance_ParsedBlock_LeadingSpaces()
    {
        var blocks = ParseBlocks("  - item");
        blocks[0].LeadingSpaces.Should().Be(2);
    }

    // --- Tab structural expansion ---

    [Theory]
    [InlineData("\t# heading", BlockKind.IndentedCodeLine)]
    [InlineData(" \t# heading", BlockKind.IndentedCodeLine)]
    [InlineData("\ttext", BlockKind.IndentedCodeLine)]
    public void TabExpansion_TabIsIndentedCode(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    [Fact]
    public void TabExpansion_LeadingSpaces_CharCount()
    {
        MarkdownParser.ClassifyBlock("\t# heading", out int ls);
        ls.Should().Be(0);
    }

    [Fact]
    public void TabExpansion_FencedCode_TabRejects()
    {
        MarkdownParser.GetFenceBacktickCount("\t```").Should().Be(0);
    }

    [Fact]
    public void TabExpansion_FencedCode_SpaceTabRejects()
    {
        MarkdownParser.GetFenceBacktickCount(" \t```").Should().Be(0);
    }

    [Fact]
    public void TabExpansion_MeasureLeadingWhitespace_Tab()
    {
        var (chars, cols) = MarkdownParser.MeasureLeadingWhitespace("\ttext");
        chars.Should().Be(1);
        cols.Should().Be(4);
    }

    [Fact]
    public void TabExpansion_MeasureLeadingWhitespace_SpaceTab()
    {
        var (chars, cols) = MarkdownParser.MeasureLeadingWhitespace(" \ttext");
        chars.Should().Be(2);
        cols.Should().Be(4);
    }

    [Fact]
    public void TabExpansion_MeasureLeadingWhitespace_TwoSpacesTab()
    {
        var (chars, cols) = MarkdownParser.MeasureLeadingWhitespace("  \ttext");
        chars.Should().Be(3);
        cols.Should().Be(4);
    }

    [Fact]
    public void TabExpansion_MeasureLeadingWhitespace_ThreeSpacesTab()
    {
        var (chars, cols) = MarkdownParser.MeasureLeadingWhitespace("   \ttext");
        chars.Should().Be(4);
        cols.Should().Be(4);
    }

    [Fact]
    public void TabExpansion_MeasureLeadingWhitespace_TwoTabs()
    {
        var (chars, cols) = MarkdownParser.MeasureLeadingWhitespace("\t\ttext");
        chars.Should().Be(2);
        cols.Should().Be(8);
    }

    [Fact]
    public void TabExpansion_CharsForColumns_TabSatisfies()
    {
        MarkdownParser.CharsForColumns("\ttext", 2).Should().Be(1);
    }

    [Fact]
    public void TabExpansion_CharsForColumns_SpacesOnly()
    {
        MarkdownParser.CharsForColumns("   text", 2).Should().Be(2);
    }

    [Fact]
    public void TabExpansion_CharsForColumns_Mixed()
    {
        MarkdownParser.CharsForColumns(" \ttext", 3).Should().Be(2);
    }

    [Fact]
    public void TabExpansion_IndentedContinuation_TabSatisfies()
    {
        var blocks = ParseBlocks("- item", "", "\tcontinuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
    }

    [Fact]
    public void TabExpansion_IndentedContinuation_OrderedList_TabSatisfies()
    {
        var blocks = ParseBlocks("1. item", "", "\tcontinuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
        blocks[2].OwnerBlock.Should().Be(0);
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
    public void FencedCode_BacktickInInfoString_NotAFence()
    {
        var result = ParseBlocks("```c`sharp", "var x = 1;", "```");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
        result[1].Kind.Should().Be(BlockKind.Paragraph);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void FencedCode_LongerFence_IgnoresShorterClose()
    {
        var result = ParseBlocks("````", "```", "some code", "````");
        result[0].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[3].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[1].IsFenceDelimiter.Should().BeFalse();
        result[3].IsFenceDelimiter.Should().BeTrue();
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
        result[0].Runs.Should().HaveCount(3);
        result[0].Runs[0].Should().Be(new StyledRun(0, 1, InlineStyle.Italic));
        result[0].Runs[1].Should().Be(new StyledRun(1, 8, InlineStyle.BoldItalic));
        result[0].Runs[2].Should().Be(new StyledRun(9, 1, InlineStyle.Italic));
    }

    [Fact]
    public void NestedEmphasis_ItalicWrappingBold()
    {
        // *foo **bar** baz* → <em>foo <strong>bar</strong> baz</em>
        var result = ParseBlocks("*foo **bar** baz*");
        result[0].Runs.Should().SatisfyRespectively(
            r => { r.Style.Should().Be(InlineStyle.Italic); r.Start.Should().Be(0); },
            r => { r.Style.Should().Be(InlineStyle.BoldItalic); r.Start.Should().Be(5); },
            r => { r.Style.Should().Be(InlineStyle.Italic); r.Start.Should().Be(12); }
        );
    }

    [Fact]
    public void NestedEmphasis_BoldClosesInsideItalic()
    {
        // ***foo** bar* → <em><strong>foo</strong> bar</em>
        var result = ParseBlocks("***foo** bar*");
        result[0].Runs.Should().SatisfyRespectively(
            r => { r.Style.Should().Be(InlineStyle.Italic); r.Start.Should().Be(0); },
            r => { r.Style.Should().Be(InlineStyle.BoldItalic); r.Start.Should().Be(1); },
            r => { r.Style.Should().Be(InlineStyle.Italic); r.Start.Should().Be(8); }
        );
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
    public void GreaterThan_WithoutSpace_IsBlockquote()
    {
        var result = ParseBlocks(">nospace");
        result[0].Kind.Should().Be(BlockKind.Blockquote);
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
        // GFM: non-pipe line after table is a lazy continuation row, not a paragraph
        var result = ParseBlocks("| A |", "| --- |", "| 1 |", "normal text");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[1].Kind.Should().Be(BlockKind.TableSeparatorRow);
        result[2].Kind.Should().Be(BlockKind.TableDataRow);
        result[3].Kind.Should().Be(BlockKind.TableDataRow);
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
        // GFM: blank line separates two tables; non-pipe lines are lazy continuation rows
        var result = ParseBlocks("| A |", "| --- |", "| 1 |", "", "| B |", "| --- |", "| 2 |");
        result[0].Kind.Should().Be(BlockKind.TableHeaderRow);
        result[0].Table.Should().NotBeSameAs(result[4].Table);
        result[3].Kind.Should().Be(BlockKind.Paragraph); // blank line
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

    // --- Angle-bracket autolinks ---

    [Fact]
    public void AngleBracketAutolink_HttpsUrl()
    {
        var result = ParseBlocks("see <https://example.com> end");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("https://example.com");
        link.Url.Should().Be("https://example.com");
        link.Start.Should().Be(4);
        link.Length.Should().Be(21);
        link.IsAngleBracket.Should().BeTrue();
    }

    [Fact]
    public void AngleBracketAutolink_HttpUrl()
    {
        var result = ParseBlocks("<http://example.com/path>");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("http://example.com/path");
        link.Url.Should().Be("http://example.com/path");
        link.IsAngleBracket.Should().BeTrue();
    }

    [Fact]
    public void AngleBracketAutolink_FtpScheme()
    {
        var result = ParseBlocks("<ftp://files.example.com>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("ftp://files.example.com");
        result[0].Links![0].IsAngleBracket.Should().BeTrue();
    }

    [Fact]
    public void AngleBracketAutolink_MailtoScheme()
    {
        var result = ParseBlocks("<mailto:user@example.com>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("mailto:user@example.com");
    }

    [Fact]
    public void AngleBracketAutolink_Email()
    {
        var result = ParseBlocks("<user@example.com>");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("user@example.com");
        link.Url.Should().Be("mailto:user@example.com");
        link.IsAngleBracket.Should().BeTrue();
    }

    [Fact]
    public void AngleBracketAutolink_Email_Complex()
    {
        var result = ParseBlocks("<foo+bar.baz@sub.example.com>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("foo+bar.baz@sub.example.com");
        result[0].Links![0].Url.Should().Be("mailto:foo+bar.baz@sub.example.com");
    }

    [Fact]
    public void AngleBracketAutolink_NotEmail_NoDot()
    {
        var result = ParseBlocks("<user@localhost>");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_NotEmail_InvalidChars()
    {
        var result = ParseBlocks("<us er@example.com>");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_Empty_NotDetected()
    {
        var result = ParseBlocks("<>");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_SingleCharScheme_NotDetected()
    {
        var result = ParseBlocks("<a:foo>");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_InsideCodeSpan_NotDetected()
    {
        var result = ParseBlocks("`<https://example.com>`");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_Multiple()
    {
        var result = ParseBlocks("<https://a.com> and <user@b.com>");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].Url.Should().Be("https://a.com");
        result[0].Links![1].Url.Should().Be("mailto:user@b.com");
    }

    [Fact]
    public void AngleBracketAutolink_WithGfmAutolink()
    {
        var result = ParseBlocks("<https://a.com> and https://b.com");
        result[0].Links.Should().HaveCount(2);
        result[0].Links![0].IsAngleBracket.Should().BeTrue();
        result[0].Links![1].IsAngleBracket.Should().BeFalse();
    }

    [Fact]
    public void AngleBracketAutolink_GfmAutolink_IsAngleBracketFalse()
    {
        var result = ParseBlocks("https://example.com");
        result[0].Links![0].IsAngleBracket.Should().BeFalse();
    }

    [Fact]
    public void AngleBracketAutolink_NoSpaceInUrl()
    {
        var result = ParseBlocks("<https://example .com>");
        result[0].Links.Should().BeNull();
    }

    [Fact]
    public void AngleBracketAutolink_UrlWithQueryAndFragment()
    {
        var result = ParseBlocks("<https://example.com/search?q=test#anchor>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com/search?q=test#anchor");
    }

    // --- Reference Links and Images ---

    [Fact]
    public void RefLink_FullForm()
    {
        var result = ParseBlocks("[click here][docs]", "", "[docs]: https://example.com");
        result[0].Links.Should().HaveCount(1);
        var link = result[0].Links![0];
        link.Text.Should().Be("click here");
        link.Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_CollapsedForm()
    {
        var result = ParseBlocks("[docs][]", "", "[docs]: https://example.com");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Text.Should().Be("docs");
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_CaseInsensitive()
    {
        var result = ParseBlocks("[click][DOCS]", "", "[docs]: https://example.com");
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
        var result = ParseBlocks("[click][docs]", "", "[docs]: https://example.com \"Example\"");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Title.Should().Be("Example");
    }

    [Fact]
    public void RefImage_FullForm()
    {
        var result = ParseBlocks("![screenshot][img1]", "", "[img1]: ./image.png");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("screenshot");
        result[0].Images![0].Url.Should().Be("./image.png");
    }

    [Fact]
    public void RefImage_CollapsedForm()
    {
        var result = ParseBlocks("![logo][]", "", "[logo]: ./logo.png");
        result[0].Images.Should().HaveCount(1);
        result[0].Images![0].AltText.Should().Be("logo");
        result[0].Images![0].Url.Should().Be("./logo.png");
    }

    [Fact]
    public void RefLink_MultipleDefinitions()
    {
        var result = ParseBlocks("[a][d1] and [b][d2]", "", "[d1]: https://a.com", "[d2]: https://b.com");
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
        var result = ParseBlocks("[click][docs]", "", "[docs]: https://first.com", "[docs]: https://second.com");
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
        var result = ParseBlocks("[click][docs]", "", "[docs]: <https://example.com>");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void RefLink_StyleCoverage()
    {
        var result = ParseBlocks("[text][ref]", "", "[ref]: https://example.com");
        result[0].Runs.Should().HaveCount(1);
        result[0].Runs[0].Style.Should().Be(InlineStyle.Link);
    }

    [Fact]
    public void LinkDefinition_TitleWithEscapedQuote()
    {
        var result = ParseBlocks("[click][docs]", "", "[docs]: https://example.com \"foo \\\"bar\\\" baz\"");
        result[0].Links.Should().HaveCount(1);
        result[0].Links![0].Url.Should().Be("https://example.com");
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

    [Fact]
    public void IsTrailingHardBreak_BeforeClosingColorTag()
    {
        var parsed = ParseBlocks("<!--@fg:red-->hello\\<!--/@fg-->")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "<!--@fg:red-->hello\\<!--/@fg-->").Should().BeTrue();
    }

    [Fact]
    public void IsTrailingHardBreak_TrailingSpacesBeforeClosingColorTag()
    {
        var parsed = ParseBlocks("<!--@bg:red-->hello  <!--/@bg-->")[0];
        MarkdownParser.IsTrailingHardBreak(parsed, "<!--@bg:red-->hello  <!--/@bg-->").Should().BeFalse();
        MarkdownParser.GetContentEnd("<!--@bg:red-->hello  <!--/@bg-->").Should().Be(21);
    }

    // --- Theme parsing ---

    [Fact]
    public void ThemeBlock_ParsesHexColors()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\n  warning = #FF6B6B\n  accent = #4ECDC4\n-->");
        theme.Should().HaveCount(2);
        theme["warning"].Should().Be(new RgbColor(0xFF, 0x6B, 0x6B));
        theme["accent"].Should().Be(new RgbColor(0x4E, 0xCD, 0xC4));
    }

    [Fact]
    public void ThemeBlock_ParsesShortHex()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\nred = #F00\n-->");
        theme["red"].Should().Be(new RgbColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void ThemeBlock_ParsesNamedColors()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\nwarn = red\ninfo = dodgerblue\n-->");
        theme["warn"].Should().Be(new RgbColor(255, 0, 0));
        theme["info"].Should().Be(new RgbColor(30, 144, 255));
    }

    [Fact]
    public void ThemeBlock_CaseInsensitiveLookup()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\nMyColor = #AABBCC\n-->");
        theme.ContainsKey("mycolor").Should().BeTrue();
        theme.ContainsKey("MYCOLOR").Should().BeTrue();
    }

    [Fact]
    public void ThemeBlock_SkipsInvalidValues()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\ngood = #FF0000\nbad = notacolor\nalso_bad = #ZZZZZZ\n-->");
        theme.Should().HaveCount(1);
        theme.ContainsKey("good").Should().BeTrue();
    }

    [Fact]
    public void ThemeBlock_EmptyBlock()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\n-->");
        theme.Should().BeEmpty();
    }

    [Fact]
    public void ThemeBlock_WhitespaceVariations()
    {
        var theme = MarkdownParser.ParseThemeBlock("<!--@theme\n  a=#FF0000\n  b =  #00FF00  \n-->");
        theme.Should().HaveCount(2);
        theme["a"].Should().Be(new RgbColor(255, 0, 0));
        theme["b"].Should().Be(new RgbColor(0, 255, 0));
    }

    [Fact]
    public void ThemeBlock_ClassifiedAsThemeDefinition()
    {
        var blocks = ParseBlocks("<!--@theme\nwarn = red\n-->");
        blocks[0].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void MultipleThemeBlocks_Merge()
    {
        var blocks = ParseBlocks(
            "<!--@theme\na = #FF0000\nb = #00FF00\n-->",
            "hello",
            "<!--@theme\nb = #0000FF\nc = #FFFFFF\n-->");

        var coloredBlock = blocks[1];
        coloredBlock.Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void ThemeBlock_MultiLine_AcrossBlocks()
    {
        var blocks = ParseBlocks(
            "<!--@theme",
            "myred=#FF0000",
            "myblue=#0000FF",
            "-->",
            "<!--@fg:myred-->red<!--/@fg--> <!--@fg:myblue-->blue<!--/@fg-->");
        blocks[0].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[1].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[2].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[3].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[4].ColorSpans.Should().HaveCount(2);
        blocks[4].ColorSpans![0].Foreground.Should().Be(new RgbColor(0xFF, 0, 0));
        blocks[4].ColorSpans![1].Foreground.Should().Be(new RgbColor(0, 0, 0xFF));
    }

    [Fact]
    public void ThemeBlock_SingleLine_MultipleEntries()
    {
        var blocks = ParseBlocks(
            "<!--@theme myred=#FF0000 myblue=#0000FF -->",
            "<!--@fg:myred-->red<!--/@fg--> <!--@fg:myblue-->blue<!--/@fg-->");
        blocks[0].Kind.Should().Be(BlockKind.ThemeDefinition);
        blocks[1].ColorSpans.Should().HaveCount(2);
        blocks[1].ColorSpans![0].Foreground.Should().Be(new RgbColor(0xFF, 0, 0));
        blocks[1].ColorSpans![1].Foreground.Should().Be(new RgbColor(0, 0, 0xFF));
    }

    // --- Inline color tags ---

    [Fact]
    public void InlineColor_FgWithTheme()
    {
        var theme = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["warning"] = new(255, 0, 0)
        };
        var spans = MarkdownParser.ParseInlineColorTags("hello <!--@fg:warning-->red text<!--/@fg--> end", theme);
        spans.Should().NotBeNull();
        spans.Should().HaveCount(1);
        spans![0].Foreground.Should().Be(new RgbColor(255, 0, 0));
        spans[0].Background.Should().BeNull();
        spans[0].Start.Should().Be(24);
        "hello <!--@fg:warning-->red text<!--/@fg--> end"[spans[0].Start..].Should().StartWith("red text");
    }

    [Fact]
    public void InlineColor_BgWithTheme()
    {
        var theme = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["highlight"] = new(255, 255, 0)
        };
        var spans = MarkdownParser.ParseInlineColorTags("<!--@bg:highlight-->text<!--/@bg-->", theme);
        spans.Should().HaveCount(1);
        spans![0].Background.Should().Be(new RgbColor(255, 255, 0));
        spans[0].Foreground.Should().BeNull();
    }

    [Fact]
    public void InlineColor_FgAndBgCombined()
    {
        var theme = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["accent"] = new(0, 255, 0),
            ["highlight"] = new(255, 255, 0)
        };
        var spans = MarkdownParser.ParseInlineColorTags("<!--@fg:accent bg:highlight-->text<!--/@-->", theme);
        spans.Should().HaveCount(1);
        spans![0].Foreground.Should().Be(new RgbColor(0, 255, 0));
        spans[0].Background.Should().Be(new RgbColor(255, 255, 0));
    }

    [Fact]
    public void InlineColor_LiteralHex()
    {
        var spans = MarkdownParser.ParseInlineColorTags("<!--@fg:#FF0000-->red<!--/@fg-->", null);
        spans.Should().HaveCount(1);
        spans![0].Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void InlineColor_UnresolvedName_NoSpan()
    {
        var spans = MarkdownParser.ParseInlineColorTags("<!--@fg:unknown-->text<!--/@fg-->", null);
        spans.Should().BeNull();
    }

    [Fact]
    public void InlineColor_NamedCssColor_NoTheme()
    {
        var spans = MarkdownParser.ParseInlineColorTags("<!--@fg:red-->text<!--/@fg-->", null);
        spans.Should().HaveCount(1);
        spans![0].Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void InlineColor_Unclosed_ExtendsToEnd()
    {
        var spans = MarkdownParser.ParseInlineColorTags("<!--@fg:red-->text to end", null);
        spans.Should().HaveCount(1);
        spans![0].Start.Should().Be(14);
        spans![0].Length.Should().Be(11);
    }

    [Fact]
    public void InlineColor_MultipleSpans()
    {
        var spans = MarkdownParser.ParseInlineColorTags(
            "<!--@fg:red-->one<!--/@fg--> <!--@fg:blue-->two<!--/@fg-->", null);
        spans.Should().HaveCount(2);
    }

    [Fact]
    public void InlineColor_IntegratedParse_WithTheme()
    {
        var blocks = ParseBlocks(
            "<!--@theme\nwarn = #FF6B6B\n-->",
            "hello <!--@fg:warn-->warning<!--/@fg--> end");
        blocks[1].ColorSpans.Should().HaveCount(1);
        blocks[1].ColorSpans![0].Foreground.Should().Be(new RgbColor(0xFF, 0x6B, 0x6B));
    }

    // --- Block div ---

    [Fact]
    public void ColorDivOpen_Classified()
    {
        var blocks = ParseBlocks("<!--@div fg:red-->");
        blocks[0].Kind.Should().Be(BlockKind.ColorDivOpen);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void ColorDivClose_Classified()
    {
        var blocks = ParseBlocks("<!--/@div-->");
        blocks[0].Kind.Should().Be(BlockKind.ColorDivClose);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void ColorDiv_AppliesBlockColor()
    {
        var blocks = ParseBlocks(
            "<!--@div fg:red-->",
            "colored paragraph",
            "<!--/@div-->");
        blocks[1].BlockColor.Should().NotBeNull();
        blocks[1].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_BgOnly()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:#00FF00-->",
            "paragraph",
            "<!--/@div-->");
        blocks[1].BlockColor!.Value.Foreground.Should().BeNull();
        blocks[1].BlockColor!.Value.Background.Should().Be(new RgbColor(0, 255, 0));
    }

    [Fact]
    public void ColorDiv_FgAndBg()
    {
        var blocks = ParseBlocks(
            "<!--@div fg:red bg:blue-->",
            "paragraph",
            "<!--/@div-->");
        blocks[1].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
        blocks[1].BlockColor!.Value.Background.Should().Be(new RgbColor(0, 0, 255));
    }

    [Fact]
    public void ColorDiv_NestedDivs_InnerOverrides()
    {
        var blocks = ParseBlocks(
            "<!--@div fg:red-->",
            "outer",
            "<!--@div fg:blue-->",
            "inner",
            "<!--/@div-->",
            "back to outer",
            "<!--/@div-->");
        blocks[1].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
        blocks[3].BlockColor!.Value.Foreground.Should().Be(new RgbColor(0, 0, 255));
        blocks[5].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_Unclosed_ImplicitClose()
    {
        var blocks = ParseBlocks(
            "<!--@div fg:red-->",
            "paragraph");
        blocks[1].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_WithThemeColors()
    {
        var blocks = ParseBlocks(
            "<!--@theme\naccent = #4ECDC4\n-->",
            "<!--@div fg:accent-->",
            "styled text",
            "<!--/@div-->");
        blocks[2].BlockColor!.Value.Foreground.Should().Be(new RgbColor(0x4E, 0xCD, 0xC4));
    }

    [Fact]
    public void ColorDiv_EmptyDiv()
    {
        var blocks = ParseBlocks(
            "<!--@div fg:red-->",
            "<!--/@div-->");
        blocks.Should().HaveCount(2);
    }

    [Fact]
    public void ColorDiv_DoesNotAffectOutsideBlocks()
    {
        var blocks = ParseBlocks(
            "before",
            "<!--@div fg:red-->",
            "inside",
            "<!--/@div-->",
            "after");
        blocks[0].BlockColor.Should().BeNull();
        blocks[2].BlockColor!.Value.Foreground.Should().Be(new RgbColor(255, 0, 0));
        blocks[4].BlockColor.Should().BeNull();
    }

    [Fact]
    public void ThemeBlock_InsideFencedCode_NotParsed()
    {
        var blocks = ParseBlocks("```", "<!--@theme\nwarn = red\n-->", "```");
        blocks[1].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void ColorDiv_InsideFencedCode_NotParsed()
    {
        var blocks = ParseBlocks("```", "<!--@div fg:red-->", "```");
        blocks[1].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    // --- Mixed-content div (open at line start, close at line end) ---

    [Fact]
    public void ColorDiv_MixedOpen_ClassifiedAsParagraph()
    {
        var blocks = ParseBlocks("<!--@div bg:red-->Hello world");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[0].IsSkippedInVisual.Should().BeFalse();
        blocks[0].DivOpenColor.Should().NotBeNull();
        blocks[0].DivOpenColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_MixedOpen_AppliesBlockColor()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:red-->First line",
            "Second line",
            "<!--/@div-->");
        blocks[0].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
        blocks[1].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_MixedClose_AppliesBlockColor()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:red-->",
            "Last line<!--/@div-->");
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].HasDivClose.Should().BeTrue();
        blocks[1].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_MixedClose_DoesNotAffectNextBlock()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:red-->",
            "Last line<!--/@div-->",
            "after");
        blocks[2].BlockColor.Should().BeNull();
    }

    [Fact]
    public void ColorDiv_BothOnSameLine()
    {
        var blocks = ParseBlocks("<!--@div bg:red-->colored text<!--/@div-->");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[0].DivOpenColor.Should().NotBeNull();
        blocks[0].HasDivClose.Should().BeTrue();
        blocks[0].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_BothOnSameLine_DoesNotAffectNextBlock()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:red-->colored text<!--/@div-->",
            "after");
        blocks[1].BlockColor.Should().BeNull();
    }

    [Fact]
    public void ColorDiv_MixedOpen_NestedInOuter()
    {
        var blocks = ParseBlocks(
            "<!--@div bg:red-->",
            "<!--@div fg:blue-->Hello",
            "<!--/@div-->",
            "still red",
            "<!--/@div-->");
        blocks[1].BlockColor!.Value.Foreground.Should().Be(new RgbColor(0, 0, 255));
        blocks[1].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
        blocks[3].BlockColor!.Value.Foreground.Should().BeNull();
        blocks[3].BlockColor!.Value.Background.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void ColorDiv_MixedOpen_TagHiddenInVisualMode()
    {
        var ranges = MarkdownParser.FindInlineColorTagRanges("<!--@div bg:red-->Hello");
        ranges.Should().NotBeNull();
        ranges.Should().ContainSingle();
        ranges![0].Start.Should().Be(0);
        ranges![0].Length.Should().Be("<!--@div bg:red-->".Length);
    }

    [Fact]
    public void ColorDiv_MixedClose_TagHiddenInVisualMode()
    {
        var ranges = MarkdownParser.FindInlineColorTagRanges("Goodbye<!--/@div-->");
        ranges.Should().NotBeNull();
        ranges.Should().ContainSingle();
        ranges![0].Start.Should().Be("Goodbye".Length);
        ranges![0].Length.Should().Be("<!--/@div-->".Length);
    }

    [Fact]
    public void ColorDiv_StandaloneOpen_StillWorks()
    {
        var blocks = ParseBlocks("<!--@div fg:red-->");
        blocks[0].Kind.Should().Be(BlockKind.ColorDivOpen);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void ColorDiv_StandaloneClose_StillWorks()
    {
        var blocks = ParseBlocks("<!--/@div-->");
        blocks[0].Kind.Should().Be(BlockKind.ColorDivClose);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    // --- Thematic breaks ---

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("----")]
    [InlineData("- - -")]
    [InlineData("* * *")]
    [InlineData("_ _ _")]
    [InlineData("  ---")]
    [InlineData("   ---")]
    [InlineData(" - - - ")]
    public void ThematicBreak_Detected(string text)
    {
        var result = ParseBlocks(text);
        result[0].Kind.Should().Be(BlockKind.ThematicBreak);
    }

    [Theory]
    [InlineData("--")]
    [InlineData("**")]
    [InlineData("__")]
    [InlineData("-_-")]
    [InlineData("- - text")]
    [InlineData("    ---")]
    public void ThematicBreak_NotDetected(string text)
    {
        var result = ParseBlocks(text);
        result[0].Kind.Should().NotBe(BlockKind.ThematicBreak);
    }

    [Fact]
    public void ThematicBreak_NotInsideFencedCode()
    {
        var result = ParseBlocks("```", "---", "```");
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    // --- Setext headings ---

    [Fact]
    public void SetextHeading_EqualsSign_IsH1()
    {
        var result = ParseBlocks("Title", "===");
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_DashSign_IsH2()
    {
        var result = ParseBlocks("Subtitle", "---");
        result[0].Kind.Should().Be(BlockKind.Heading2);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_SingleEquals()
    {
        var result = ParseBlocks("Title", "=");
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_SingleDash()
    {
        var result = ParseBlocks("Title", "-");
        result[0].Kind.Should().Be(BlockKind.Heading2);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_LeadingSpaces()
    {
        var result = ParseBlocks("Title", "  ===");
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_TrailingSpaces()
    {
        var result = ParseBlocks("Title", "===  ");
        result[0].Kind.Should().Be(BlockKind.Heading1);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextUnderline_IsSkippedInVisual()
    {
        var result = ParseBlocks("Title", "===");
        result[1].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void SetextHeading_NotPrecededByParagraph_StaysThematicBreak()
    {
        var result = ParseBlocks("---");
        result[0].Kind.Should().Be(BlockKind.ThematicBreak);
    }

    [Fact]
    public void SetextHeading_EqualsNotPrecededByParagraph_StaysParagraph()
    {
        var result = ParseBlocks("", "===");
        result[1].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void SetextHeading_NotInsideFencedCode()
    {
        var result = ParseBlocks("```", "Title", "===", "```");
        result[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        result[2].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void SetextHeading_DashDisambiguation_AfterParagraph()
    {
        var result = ParseBlocks("text", "---");
        result[0].Kind.Should().Be(BlockKind.Heading2);
        result[1].Kind.Should().Be(BlockKind.SetextUnderline);
    }

    [Fact]
    public void SetextHeading_DashDisambiguation_AfterBlankLine()
    {
        var result = ParseBlocks("text", "", "---");
        result[0].Kind.Should().Be(BlockKind.Paragraph);
        result[2].Kind.Should().Be(BlockKind.ThematicBreak);
    }

    [Theory]
    [InlineData("---", BlockKind.ThematicBreak)]
    [InlineData(" ---", BlockKind.ThematicBreak)]
    [InlineData("  ---", BlockKind.ThematicBreak)]
    [InlineData("   ---", BlockKind.ThematicBreak)]
    [InlineData("    ---", BlockKind.IndentedCodeLine)]
    public void PrefixTolerance_ThematicBreak(string text, BlockKind expected)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(expected);
    }

    // --- Indented code blocks (iteration 16) ---

    [Theory]
    [InlineData("    code")]
    [InlineData("     code")]
    [InlineData("      indented more")]
    [InlineData("\tcode")]
    [InlineData("    ")]
    public void IndentedCode_ClassifyBlock(string text)
    {
        MarkdownParser.ClassifyBlock(text).Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_Detected_AfterBlankLine()
    {
        var blocks = ParseBlocks("text", "", "    code");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_Detected_AtStart()
    {
        var blocks = ParseBlocks("    code", "text");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void IndentedCode_CannotInterruptParagraph()
    {
        var blocks = ParseBlocks("paragraph", "    not code");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void IndentedCode_CanFollowHeading()
    {
        var blocks = ParseBlocks("# heading", "    code");
        blocks[0].Kind.Should().Be(BlockKind.Heading1);
        blocks[1].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_CanFollowThematicBreak()
    {
        var blocks = ParseBlocks("---", "    code");
        blocks[0].Kind.Should().Be(BlockKind.ThematicBreak);
        blocks[1].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_AfterListItem_IsLazyContinuation()
    {
        var blocks = ParseBlocks("- item", "    code");
        blocks[1].IsLazyContinuation.Should().BeTrue();
    }

    [Fact]
    public void IndentedCode_ConsecutiveLines()
    {
        var blocks = ParseBlocks("    line1", "    line2", "    line3");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_BlankLineBetweenChunks()
    {
        var blocks = ParseBlocks("    chunk1", "", "    chunk2");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_TrailingBlankNotIncluded()
    {
        var blocks = ParseBlocks("    code", "", "text");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
        blocks[2].Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void IndentedCode_NoInlineParsing()
    {
        var blocks = ParseBlocks("    **not bold**");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[0].Runs.Should().HaveCount(1);
        blocks[0].Runs[0].Style.Should().Be(InlineStyle.Normal);
    }

    [Fact]
    public void IndentedCode_NotInsideFencedCode()
    {
        var blocks = ParseBlocks("```", "    indented", "```");
        blocks[0].Kind.Should().Be(BlockKind.FencedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        blocks[2].Kind.Should().Be(BlockKind.FencedCodeLine);
    }

    [Fact]
    public void IndentedCode_TabIndent()
    {
        var blocks = ParseBlocks("\tcode");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_AfterBlankAfterParagraph()
    {
        var blocks = ParseBlocks("text", "", "    code");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].Kind.Should().Be(BlockKind.Paragraph);
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_ListContinuation_NotCode()
    {
        var blocks = ParseBlocks("- item", "", "    continuation");
        blocks[2].IsIndentedContinuation.Should().BeTrue();
    }

    [Fact]
    public void IndentedCode_MultipleBlanksBetweenChunks()
    {
        var blocks = ParseBlocks("    a", "", "", "    b");
        blocks[0].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[1].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[2].Kind.Should().Be(BlockKind.IndentedCodeLine);
        blocks[3].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    [Fact]
    public void IndentedCode_AfterFencedCode()
    {
        var blocks = ParseBlocks("```", "code", "```", "    indented");
        blocks[3].Kind.Should().Be(BlockKind.IndentedCodeLine);
    }

    // --- Emphasis with >3 stars (gap #1) ---

    [Fact]
    public void Emphasis_FourStars_Bold()
    {
        // ****text**** → delimiter algorithm matches ** twice → Bold
        var result = ParseBlocks("****text****");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.Bold);
    }

    [Fact]
    public void Emphasis_FiveStars_BoldItalic()
    {
        var result = ParseBlocks("*****text*****");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.BoldItalic);
    }

    [Fact]
    public void Emphasis_FourStarsOpen_ThreeStarsClose()
    {
        // ****text*** → delimiter algorithm: consumes ** (bold), then * (italic)
        var result = ParseBlocks("****text***");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.BoldItalic);
    }

    [Fact]
    public void Emphasis_ThreeStarsOpen_FourStarsClose()
    {
        // ***text**** → CommonMark: <em><strong>text</strong></em>*
        var result = ParseBlocks("***text****");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.BoldItalic);
    }

    // --- Mismatched star run lengths (gap #2) ---

    [Fact]
    public void Emphasis_TwoStarsOpen_OneClose()
    {
        // **text* — delimiter algorithm consumes 1 from each: italic match
        var result = ParseBlocks("**text*");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.Italic);
    }

    [Fact]
    public void Emphasis_OneStarOpen_TwoClose()
    {
        // *text** — delimiter algorithm consumes 1 from each: italic match
        var result = ParseBlocks("*text**");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.Italic);
    }

    [Fact]
    public void Emphasis_MismatchedRuns_RuleOfThree()
    {
        // CommonMark rule: if opener+closer is multiple of 3 and neither is multiple of 3, skip
        // *foo**bar* — 1+2=3, 1%3!=0 and 2%3!=0 → skip per rule of three
        var result = ParseBlocks("*foo**bar*");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.Italic);
    }

    [Fact]
    public void Emphasis_MatchedThenLeftover()
    {
        // **foo*** → <strong>foo</strong>* (leftover star is literal)
        var result = ParseBlocks("**foo***");
        var runs = result[0].Runs;
        runs.Should().Contain(r => r.Style == InlineStyle.Bold);
    }

    // --- Colored text inside table cells (gap #3) ---

    [Fact]
    public void Table_CellWithInlineColor_ParsesColorSpan()
    {
        var result = ParseBlocks("| <!--@fg:red-->text<!--/@fg--> |", "| --- |", "| data |");
        result[0].ColorSpans.Should().NotBeNull();
        result[0].ColorSpans!.Count.Should().BeGreaterThanOrEqualTo(1);
        result[0].ColorSpans![0].Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void Table_DataCellWithInlineColor_ParsesColorSpan()
    {
        var result = ParseBlocks("| header |", "| --- |", "| <!--@fg:blue-->value<!--/@fg--> |");
        result[2].ColorSpans.Should().NotBeNull();
        result[2].ColorSpans!.Count.Should().BeGreaterThanOrEqualTo(1);
        result[2].ColorSpans![0].Foreground.Should().Be(new RgbColor(0, 0, 255));
    }

    [Fact]
    public void Table_MultipleCellsWithDifferentColors()
    {
        var result = ParseBlocks(
            "| <!--@fg:red-->a<!--/@fg--> | <!--@fg:green-->b<!--/@fg--> |",
            "| --- | --- |",
            "| x | y |");
        result[0].ColorSpans.Should().NotBeNull();
        result[0].ColorSpans!.Count.Should().Be(2);
        result[0].ColorSpans![0].Foreground.Should().Be(new RgbColor(255, 0, 0));
        result[0].ColorSpans![1].Foreground.Should().Be(new RgbColor(0, 128, 0));
    }

    // --- ParseTableCells degenerate inputs (gap #7) ---

    [Fact]
    public void ParseTableCells_EmptyString_ReturnsEmpty()
    {
        var cells = MarkdownParser.ParseTableCells("");
        cells.Should().BeEmpty();
    }

    [Fact]
    public void ParseTableCells_SinglePipe_ReturnsEmpty()
    {
        var cells = MarkdownParser.ParseTableCells("|");
        cells.Should().BeEmpty();
    }

    [Fact]
    public void ParseTableCells_TwoPipes_OneEmptyCell()
    {
        var cells = MarkdownParser.ParseTableCells("||");
        cells.Should().HaveCount(1);
        cells[0].Length.Should().Be(0);
    }

    [Fact]
    public void ParseTableCells_NoPipes_SingleCell()
    {
        var cells = MarkdownParser.ParseTableCells("text");
        cells.Should().HaveCount(1);
        cells[0].Start.Should().Be(0);
        cells[0].Length.Should().Be(4);
    }

    [Fact]
    public void ParseTableCells_OnlySpaces_SingleCell()
    {
        var cells = MarkdownParser.ParseTableCells("   ");
        cells.Should().HaveCount(1);
    }

    [Fact]
    public void ParseTableCells_PipeWithSpaces_CellBoundaries()
    {
        // "| a | b |" — leading pipe skipped, cells split on inner pipes
        var cells = MarkdownParser.ParseTableCells("| a | b |");
        cells.Should().HaveCount(2);
        cells[0].Start.Should().Be(1);  // after leading |
        cells[1].Start.Should().Be(5);  // after second |
    }

    // --- Page break tag ---

    [Fact]
    public void PageBreakTag_ClassifiedAsPageBreak()
    {
        var blocks = ParseBlocks("<!--@pagebreak-->");
        blocks[0].Kind.Should().Be(BlockKind.PageBreak);
        blocks[0].IsSkippedInVisual.Should().BeTrue();
    }

    [Fact]
    public void PageBreakTag_CaseInsensitive()
    {
        var blocks = ParseBlocks("<!--@PageBreak-->");
        blocks[0].Kind.Should().Be(BlockKind.PageBreak);
    }

    [Fact]
    public void PageBreakTag_WithWhitespace()
    {
        var blocks = ParseBlocks("  <!--@pagebreak-->  ");
        blocks[0].Kind.Should().Be(BlockKind.PageBreak);
    }

    [Fact]
    public void PageBreakTag_BetweenContent_PreservesNeighbors()
    {
        var blocks = ParseBlocks("Line 1", "<!--@pagebreak-->", "Line 2");
        blocks[0].Kind.Should().Be(BlockKind.Paragraph);
        blocks[1].Kind.Should().Be(BlockKind.PageBreak);
        blocks[2].Kind.Should().Be(BlockKind.Paragraph);
    }

    // --- NormalizeAdjacentMarkers ---

    [Theory]
    [InlineData("**5.** **N** **debounce** **timers**", "**5. N debounce timers**")]
    [InlineData("*a* *b* *c*", "*a b c*")]
    [InlineData("~~x~~ ~~y~~", "~~x y~~")]
    [InlineData("`a` `b` `c`", "`a b c`")]
    [InlineData("**bold** plain **bold**", "**bold** plain **bold**")]
    [InlineData("no markers here", "no markers here")]
    [InlineData("**a**  **b**", "**a b**")]
    public void NormalizeAdjacentMarkers_CollapsesMarkers(string input, string expected)
    {
        MarkdownParser.NormalizeAdjacentMarkers(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeAdjacentMarkers_DoesNotCollapseDifferentLengthMarkers()
    {
        MarkdownParser.NormalizeAdjacentMarkers("**a** *b*").Should().Be("**a** *b*");
        MarkdownParser.NormalizeAdjacentMarkers("*a* **b**").Should().Be("*a* **b**");
    }

    [Fact]
    public void NormalizeAdjacentMarkers_PreservesTripleStarMarkers()
    {
        MarkdownParser.NormalizeAdjacentMarkers("***a*** ***b***").Should().Be("***a*** ***b***");
    }

    [Fact]
    public void HasAdjacentMarkers_ReturnsTrueWhenPresent()
    {
        MarkdownParser.HasAdjacentMarkers("**a** **b**").Should().BeTrue();
        MarkdownParser.HasAdjacentMarkers("*a* *b*").Should().BeTrue();
        MarkdownParser.HasAdjacentMarkers("~~x~~ ~~y~~").Should().BeTrue();
        MarkdownParser.HasAdjacentMarkers("`a` `b`").Should().BeTrue();
    }

    [Fact]
    public void HasAdjacentMarkers_ReturnsFalseWhenAbsent()
    {
        MarkdownParser.HasAdjacentMarkers("**bold** plain").Should().BeFalse();
        MarkdownParser.HasAdjacentMarkers("no markers").Should().BeFalse();
    }

    [Fact]
    public void NormalizeAdjacentMarkers_CollapsesAdjacentColorTags()
    {
        var input = "<!--@fg:red-->hello<!--/@--><!--@fg:red--> world<!--/@-->";
        MarkdownParser.NormalizeAdjacentMarkers(input).Should().Be("<!--@fg:red-->hello world<!--/@-->");
    }

    [Fact]
    public void NormalizeAdjacentMarkers_CollapsesChainedColorTags()
    {
        var input = "<!--@fg:white bg:#373737-->**N**<!--/@--><!--@fg:white bg:#373737--> <!--/@--><!--@fg:white bg:#373737-->**debounce**<!--/@-->";
        var expected = "<!--@fg:white bg:#373737-->**N debounce**<!--/@-->";
        MarkdownParser.NormalizeAdjacentMarkers(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeAdjacentMarkers_PreservesDifferentColorTags()
    {
        var input = "<!--@fg:red-->hello<!--/@--><!--@fg:blue-->world<!--/@-->";
        MarkdownParser.NormalizeAdjacentMarkers(input).Should().Be(input);
    }

    [Fact]
    public void HasAdjacentMarkers_DetectsAdjacentColorTags()
    {
        MarkdownParser.HasAdjacentMarkers("<!--@fg:red-->a<!--/@--><!--@fg:red-->b<!--/@-->").Should().BeTrue();
    }

    [Fact]
    public void HasAdjacentMarkers_FalseForNonAdjacentColorTags()
    {
        MarkdownParser.HasAdjacentMarkers("<!--@fg:red-->a<!--/@--> <!--@fg:red-->b<!--/@-->").Should().BeFalse();
    }

}
