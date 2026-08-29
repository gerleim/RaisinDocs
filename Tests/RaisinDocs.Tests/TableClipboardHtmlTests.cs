using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class TableClipboardHtmlTests
{
    private static string? Build(string[] blocks, int startBlock, int endBlock,
        int startCol = -1, int endCol = -1)
    {
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);
        return TableClipboardHtml.TryBuildFragment(parsed, i => blocks[i],
            startBlock, endBlock, startCol, endCol);
    }

    private static readonly string[] SimpleTable =
    [
        "| Name | Qty |",
        "|------|-----|",
        "| Apple | 3 |",
        "| Pear | 12 |"
    ];

    // --- Basic structure ---

    [Fact]
    public void WholeTable_ProducesTableWithHeaderAndRows()
    {
        var html = Build(SimpleTable, 0, 3);

        html.Should().NotBeNull();
        html.Should().StartWith("<table ").And.EndWith("</table>");
        html.Should().Contain("<th").And.Contain(">Name<").And.Contain(">Qty<");
        html.Should().Contain(">Apple<").And.Contain(">3<");
        html.Should().Contain(">Pear<").And.Contain(">12<");
    }

    [Fact]
    public void SeparatorRow_IsNotEmitted()
    {
        var html = Build(SimpleTable, 0, 3)!;

        html.Should().NotContain("---");
        // header + two data rows, no row for the separator
        CountOccurrences(html, "<tr>").Should().Be(3);
    }

    [Fact]
    public void HeaderRow_UsesThDataRows_UseTd()
    {
        var html = Build(SimpleTable, 0, 3)!;

        CountOccurrences(html, "<th").Should().Be(2);
        CountOccurrences(html, "<td").Should().Be(4);
    }

    [Fact]
    public void CellText_IsTrimmed()
    {
        var html = Build(SimpleTable, 0, 3)!;

        html.Should().NotContain("> Apple <");
        html.Should().Contain(">Apple<");
    }

    [Fact]
    public void TableUsesCollapsedBorders_SoExcelDrawsGridlines()
    {
        var html = Build(SimpleTable, 0, 3)!;

        html.Should().Contain("border=\"1\"");
        html.Should().Contain("border-collapse:collapse;");
    }

    // --- Rejection cases (caller falls back to plain-text copy) ---

    [Fact]
    public void NonTableBlocksInRange_ReturnsNull()
    {
        string[] blocks =
        [
            "Some paragraph",
            "| Name | Qty |",
            "|------|-----|",
            "| Apple | 3 |"
        ];

        Build(blocks, 0, 3).Should().BeNull();
    }

    [Fact]
    public void ParagraphSeparatedFromTable_ReturnsNull()
    {
        string[] blocks = [.. SimpleTable, "", "After the table"];

        Build(blocks, 0, 5).Should().BeNull();
    }

    [Fact]
    public void LineDirectlyBelowTable_IsIncludedAsALazyContinuationRow()
    {
        // MarkdownParser.DetectTables treats an unbroken following line as a data row,
        // so the copied grid matches what the editor renders.
        string[] blocks = [.. SimpleTable, "Still the table"];

        var html = Build(blocks, 0, 4)!;

        html.Should().Contain(">Still the table<");
    }

    [Fact]
    public void RangeOfOnlySeparator_ReturnsNull()
    {
        Build(SimpleTable, 1, 1).Should().BeNull();
    }

    [Fact]
    public void InvertedRange_ReturnsNull()
    {
        Build(SimpleTable, 3, 0).Should().BeNull();
    }

    [Fact]
    public void OutOfRangeBlock_ReturnsNull()
    {
        Build(SimpleTable, 0, 99).Should().BeNull();
    }

    // --- Partial ranges ---

    [Fact]
    public void DataRowsOnly_ProducesNoHeaderCells()
    {
        var html = Build(SimpleTable, 2, 3)!;

        html.Should().NotContain("<th");
        html.Should().Contain(">Apple<").And.Contain(">Pear<");
        html.Should().NotContain(">Name<");
    }

    [Fact]
    public void ColumnRange_RestrictsEmittedCells()
    {
        var html = Build(SimpleTable, 0, 3, startCol: 1, endCol: 1)!;

        html.Should().Contain(">Qty<").And.Contain(">3<").And.Contain(">12<");
        html.Should().NotContain(">Name<");
        html.Should().NotContain(">Apple<");
    }

    [Fact]
    public void ColumnRangeBeyondTable_IsClampedToLastColumn()
    {
        var html = Build(SimpleTable, 0, 3, startCol: 0, endCol: 99)!;

        CountOccurrences(html, "<th").Should().Be(2);
    }

    [Fact]
    public void RaggedRows_ArePaddedToTheColumnRange()
    {
        string[] blocks =
        [
            "| A | B | C |",
            "|---|---|---|",
            "| 1 |"
        ];

        var html = Build(blocks, 0, 2)!;

        // the short row still emits three cells so the grid stays rectangular
        CountOccurrences(html, "<td").Should().Be(3);
    }

    // --- Inline formatting ---

    [Fact]
    public void BoldCell_BecomesFontWeightSpan()
    {
        string[] blocks =
        [
            "| Name | Qty |",
            "|------|-----|",
            "| **Apple** | 3 |"
        ];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("font-weight:bold;");
        html.Should().Contain(">Apple<");
        html.Should().NotContain("**");
    }

    [Fact]
    public void ItalicCell_BecomesFontStyleSpan()
    {
        string[] blocks =
        [
            "| Name |",
            "|------|",
            "| *Apple* |"
        ];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("font-style:italic;");
        html.Should().NotContain("*Apple*");
    }

    [Fact]
    public void ColorTagInCell_BecomesColorSpan()
    {
        string[] blocks =
        [
            "| Status |",
            "|--------|",
            "| <!--@fg:red-->failed<!--/@fg--> |"
        ];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("color:#FF0000;");
        html.Should().Contain(">failed<");
        html.Should().NotContain("<!--");
    }

    [Fact]
    public void SpecialCharactersInCell_AreHtmlEncoded()
    {
        string[] blocks =
        [
            "| Expr |",
            "|------|",
            "| a < b & \"c\" |"
        ];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("a &lt; b &amp; &quot;c&quot;");
    }

    // --- Alignment ---

    [Fact]
    public void ColumnAlignments_BecomeTextAlignStyles()
    {
        string[] blocks =
        [
            "| L | C | R |",
            "|:---|:---:|---:|",
            "| 1 | 2 | 3 |"
        ];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("text-align:center;");
        html.Should().Contain("text-align:right;");
    }

    [Fact]
    public void HeaderCells_AreExplicitlyLeftAlignedByDefault()
    {
        // <th> centers by default in HTML; markdown tables are left-aligned unless told otherwise
        var html = Build(SimpleTable, 0, 3)!;

        html.Should().Contain("<th style=\"text-align:left;\"");
    }

    // --- Excel value-mangling guards ---

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("@handle")]
    [InlineData("007")]
    [InlineData("1234567890123456")]
    public void CellsExcelWouldReinterpret_AreForcedToTextFormat(string cell)
    {
        string[] blocks = ["| Value |", "|-------|", $"| {cell} |"];

        var html = Build(blocks, 0, 2)!;

        html.Should().Contain("mso-number-format:'\\@';");
    }

    [Theory]
    [InlineData("42")]
    [InlineData("3.14")]
    [InlineData("-5")]
    [InlineData("Apple")]
    [InlineData("")]
    public void OrdinaryValues_AreLeftAsExcelDefaults(string cell)
    {
        string[] blocks = ["| Value |", "|-------|", $"| {cell} |"];

        var html = Build(blocks, 0, 2)!;

        html.Should().NotContain("mso-number-format");
    }

    // --- CF_HTML wrapper ---

    [Fact]
    public void TryBuild_WrapsFragmentInCfHtmlHeader()
    {
        var parsed = MarkdownParser.Parse(i => SimpleTable[i], SimpleTable.Length);
        var cfHtml = TableClipboardHtml.TryBuild(parsed, i => SimpleTable[i], 0, 3);

        cfHtml.Should().NotBeNull();
        cfHtml.Should().StartWith("Version:0.9\r\n");
        cfHtml.Should().Contain("StartFragment:");
        cfHtml.Should().Contain("<!--StartFragment--><table ");
        cfHtml.Should().Contain("</table><!--EndFragment-->");
    }

    [Fact]
    public void TryBuild_NonTableRange_ReturnsNull()
    {
        string[] blocks = ["Just a paragraph", "And another"];
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);

        TableClipboardHtml.TryBuild(parsed, i => blocks[i], 0, 1).Should().BeNull();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, pos = 0;
        while ((pos = haystack.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += needle.Length;
        }
        return count;
    }
}
