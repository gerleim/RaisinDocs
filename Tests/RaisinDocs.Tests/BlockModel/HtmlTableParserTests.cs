using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.BlockModel;

/// <summary>
/// Excel → markdown table conversion. The fixture is a verbatim capture of what Excel 15
/// puts on the Windows clipboard, so these tests pin the real-world quirks rather than an
/// idealized HTML shape.
/// </summary>
public class HtmlTableParserTests
{
    private static string ExcelFixture() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "../../../BlockModel/excel_table_clipboard.html"));

    private static string? Convert(string html) =>
        HtmlBlockModelParser.ConvertHtmlToMarkdown(html, new MarkdownOutputSettings());

    /// <summary>Wraps a fragment the way Excel does — markers *inside* the table element.</summary>
    private static string AsExcelClipboard(string rowsHtml, string styleBlock = "") =>
        "Version:1.0\r\nStartHTML:0000000105\r\nEndHTML:0000000200\r\n" +
        "StartFragment:0000000141\r\nEndFragment:0000000180\r\n" +
        $"<html><head><style><!--{styleBlock}--></style></head><body>\r\n" +
        $"<table border=0 cellpadding=0 cellspacing=0>\r\n<!--StartFragment-->\r\n{rowsHtml}\r\n" +
        "<!--EndFragment-->\r\n</table>\r\n</body></html>";

    private static string[] Lines(string markdown) =>
        markdown.Replace("\r\n", "\n").Split('\n');

    // --- Real Excel clipboard payload ---

    [Fact]
    public void ExcelClipboard_ProducesAMarkdownTable()
    {
        var lines = Lines(Convert(ExcelFixture())!);

        lines[0].Should().Be("| RT | Time | Side | Qty | Price | P&L |");
        lines[1].Should().Be("| ---: | ---: | --- | ---: | --- | --- |");
        lines.Should().HaveCount(10); // header + separator + 8 data rows
    }

    [Fact]
    public void ExcelClipboard_DecodesEntitiesInCells()
    {
        // the header reads P&amp;L in the source
        Convert(ExcelFixture())!.Should().Contain("P&L").And.NotContain("&amp;");
    }

    [Fact]
    public void ExcelClipboard_EmptyCellsBecomeEmpty()
    {
        // Excel writes &nbsp; for a blank cell; it must not survive as a literal space glyph
        var lines = Lines(Convert(ExcelFixture())!);

        lines[2].Should().Be("| 1 | 09:33:43 | Buy | 100 | $2.96 |  |");
    }

    [Fact]
    public void ExcelClipboard_ResolvesCellColorsFromTheStyleBlock()
    {
        // .xl68 { color:green } lives in <head>, outside the CF_HTML fragment
        var lines = Lines(Convert(ExcelFixture())!);

        lines[4].Should().Contain("<!--@fg:green-->+$3.00<!--/@fg-->");
    }

    [Fact]
    public void ExcelClipboard_HeaderIsNotRedundantlyBolded()
    {
        // Excel's header class carries font-weight:700, but a markdown header renders bold anyway
        Lines(Convert(ExcelFixture())!)[0].Should().NotContain("**");
    }

    [Fact]
    public void ExcelClipboard_InfersColumnAlignmentFromDataCells()
    {
        // numeric columns carry align=right; text columns carry nothing
        Lines(Convert(ExcelFixture())!)[1].Should().Be("| ---: | ---: | --- | ---: | --- | --- |");
    }

    // --- Structure ---

    [Fact]
    public void RowsWithoutATableWrapper_AreStillParsed()
    {
        // Excel's fragment begins after <table>, so the rows arrive orphaned
        string md = Convert(AsExcelClipboard("<tr><td>A</td><td>B</td></tr><tr><td>1</td><td>2</td></tr>"))!;

        Lines(md).Should().Equal("| A | B |", "| --- | --- |", "| 1 | 2 |");
    }

    [Fact]
    public void ExplicitTableElement_IsParsed()
    {
        string md = Convert("<table><tr><td>A</td></tr><tr><td>1</td></tr></table>")!;

        Lines(md).Should().Equal("| A |", "| --- |", "| 1 |");
    }

    [Fact]
    public void HeaderCells_AreUsedAsTheHeaderRow()
    {
        string md = Convert("<table><tr><th>Name</th><th>Qty</th></tr><tr><td>Pear</td><td>3</td></tr></table>")!;

        Lines(md)[0].Should().Be("| Name | Qty |");
    }

    [Fact]
    public void WithoutHeaderCells_FirstRowBecomesTheHeader()
    {
        string md = Convert("<table><tr><td>a</td></tr><tr><td>b</td></tr></table>")!;

        Lines(md).Should().Equal("| a |", "| --- |", "| b |");
    }

    [Fact]
    public void TheadAndTbodyWrappers_AreTransparent()
    {
        string md = Convert(
            "<table><thead><tr><th>H</th></tr></thead><tbody><tr><td>d</td></tr></tbody></table>")!;

        Lines(md).Should().Equal("| H |", "| --- |", "| d |");
    }

    [Fact]
    public void RaggedRows_ArePaddedToTheWidestRow()
    {
        string md = Convert("<table><tr><td>A</td><td>B</td><td>C</td></tr><tr><td>1</td></tr></table>")!;

        Lines(md)[2].Should().Be("| 1 |  |  |");
    }

    [Fact]
    public void MergedCells_ReserveTheColumnsTheySpan()
    {
        string md = Convert(
            "<table><tr><td>A</td><td>B</td><td>C</td></tr><tr><td colspan=2>wide</td><td>3</td></tr></table>")!;

        Lines(md)[2].Should().Be("| wide |  | 3 |");
    }

    [Fact]
    public void UnquotedAttributes_AreRead()
    {
        // Excel never quotes its attribute values
        string md = Convert("<table><tr><td>H</td></tr><tr><td align=right>1</td></tr></table>")!;

        Lines(md)[1].Should().Be("| ---: |");
    }

    [Fact]
    public void TagsSpanningMultipleLines_AreRead()
    {
        string md = Convert("<table><tr><td>H</td></tr><tr><td align=right\n  width=64\n  >1</td></tr></table>")!;

        Lines(md)[1].Should().Be("| ---: |");
    }

    // --- Cell content ---

    [Fact]
    public void PipesInCellText_AreEscaped()
    {
        string md = Convert("<table><tr><td>a|b</td></tr><tr><td>c</td></tr></table>")!;

        Lines(md)[0].Should().Be(@"| a\|b |");
    }

    [Fact]
    public void LineBreaksInACell_AreFlattenedToASpace()
    {
        // a markdown table row cannot span lines
        string md = Convert("<table><tr><td>H</td></tr><tr><td>one<br>two</td></tr></table>")!;

        Lines(md).Should().HaveCount(3);
        Lines(md)[2].Should().Be("| one two |");
    }

    [Fact]
    public void InlineMarkupInCells_BecomesMarkdown()
    {
        string md = Convert("<table><tr><td>H</td></tr><tr><td><b>bold</b> and <i>italic</i></td></tr></table>")!;

        Lines(md)[2].Should().Be("| **bold** and *italic* |");
    }

    // --- Alignment inference ---

    [Fact]
    public void ConflictingCellAlignments_FallBackToLeft()
    {
        string md = Convert(
            "<table><tr><td>H</td></tr><tr><td align=right>1</td></tr><tr><td align=left>2</td></tr></table>")!;

        Lines(md)[1].Should().Be("| --- |");
    }

    [Fact]
    public void CenterAlignment_IsPreserved()
    {
        string md = Convert("<table><tr><td>H</td></tr><tr><td align=center>1</td></tr></table>")!;

        Lines(md)[1].Should().Be("| :---: |");
    }

    [Fact]
    public void HeaderAlignment_DoesNotDecideTheColumn()
    {
        // Excel left-aligns headers regardless of the column's own alignment
        string md = Convert(
            "<table><tr><td align=left>H</td></tr><tr><td align=right>1</td></tr></table>")!;

        Lines(md)[1].Should().Be("| ---: |");
    }

    [Fact]
    public void ExcelGeneralTextAlign_CountsAsUnspecified()
    {
        string md = Convert(AsExcelClipboard(
            "<tr><td class=x1>H</td></tr><tr><td class=x1>1</td></tr>",
            ".x1 {text-align:general;}"))!;

        Lines(md)[1].Should().Be("| --- |");
    }

    // --- Class-based formatting (the only way Excel expresses it) ---

    [Fact]
    public void ClassRules_SupplyCellColor()
    {
        string md = Convert(AsExcelClipboard(
            "<tr><td>H</td></tr><tr><td class=xl68>loss</td></tr>",
            ".xl68 {color:red;}"))!;

        Lines(md)[2].Should().Be("| <!--@fg:red-->loss<!--/@fg--> |");
    }

    [Fact]
    public void ClassRules_SupplyBoldAndItalic()
    {
        string md = Convert(AsExcelClipboard(
            "<tr><td>H</td></tr><tr><td class=b>x</td><td class=i>y</td></tr>",
            ".b {font-weight:700;} .i {font-style:italic;}"))!;

        Lines(md)[2].Should().Be("| **x** | *y* |");
    }

    [Fact]
    public void NumericFontWeightBelowBold_IsNotBold()
    {
        string md = Convert(AsExcelClipboard(
            "<tr><td>H</td></tr><tr><td class=n>x</td></tr>",
            ".n {font-weight:400;}"))!;

        Lines(md)[2].Should().Be("| x |");
    }

    [Fact]
    public void ElementLevelRules_AreIgnored()
    {
        // Excel emits a `td { color:black; font-weight:400 }` baseline that describes
        // defaults; honoring it would tag every pasted cell as explicitly black.
        string md = Convert(AsExcelClipboard(
            "<tr><td>H</td></tr><tr><td>plain</td></tr>",
            "td {color:black;font-weight:400;}"))!;

        Lines(md)[2].Should().Be("| plain |");
    }

    [Fact]
    public void MsoProperties_AreIgnored()
    {
        string md = Convert(AsExcelClipboard(
            "<tr><td>H</td></tr><tr><td class=t>09:33</td></tr>",
            ".t {mso-number-format:\"hh\\:mm\\:ss\";}"))!;

        Lines(md)[2].Should().Be("| 09:33 |");
    }

    [Fact]
    public void InlineStyleOnACell_IsApplied()
    {
        string md = Convert("<table><tr><td>H</td></tr><tr><td style='color:blue'>x</td></tr></table>")!;

        Lines(md)[2].Should().Be("| <!--@fg:blue-->x<!--/@fg--> |");
    }

    // --- Non-table content is unaffected ---

    [Fact]
    public void OrdinaryHtml_StillConvertsAsBefore()
    {
        string md = Convert("<h1>Title</h1><p>Some <strong>bold</strong> text</p>")!;

        Lines(md).Should().Equal("# Title", "Some **bold** text");
    }

    [Fact]
    public void WhitespaceBetweenInlineElements_IsPreserved()
    {
        // segments keep their edge whitespace, so words don't run together across tags
        Convert("<p>a <b>b</b> c</p>")!.Should().Be("a **b** c");
    }

    [Fact]
    public void BlockEdgeWhitespace_IsStillTrimmed()
    {
        Convert("<p>  spaced  </p>")!.Should().Be("spaced");
        Convert("<h2> Title </h2>")!.Should().Be("## Title");
    }

    [Fact]
    public void ContentAroundATable_IsKept()
    {
        string md = Convert("<h2>Report</h2><table><tr><td>A</td></tr><tr><td>1</td></tr></table>")!;

        Lines(md).Should().Equal("## Report", "| A |", "| --- |", "| 1 |");
    }

    [Fact]
    public void EmptyTable_YieldsNoTableOutput()
    {
        Convert("<table></table>").Should().BeNull();
    }

    // --- Round trip: our own copy-out HTML must survive our paste-in ---

    private static string RoundTrip(params string[] blocks)
    {
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);
        var cfHtml = TableClipboardHtml.TryBuild(parsed, i => blocks[i], 0, blocks.Length - 1)!;
        return Convert(cfHtml)!;
    }

    [Fact]
    public void CopiedTable_PastesBackUnchanged()
    {
        string md = RoundTrip(
            "| Name | Qty |",
            "|------|-----|",
            "| Apple | 3 |",
            "| Pear | 12 |");

        Lines(md).Should().Equal(
            "| Name | Qty |",
            "| --- | --- |",
            "| Apple | 3 |",
            "| Pear | 12 |");
    }

    [Fact]
    public void CopiedTable_PreservesAlignmentThroughARoundTrip()
    {
        string md = RoundTrip(
            "| L | C | R |",
            "|:---|:---:|---:|",
            "| 1 | 2 | 3 |");

        Lines(md)[1].Should().Be("| --- | :---: | ---: |");
    }

    [Fact]
    public void CopiedTable_PreservesInlineFormattingThroughARoundTrip()
    {
        string md = RoundTrip(
            "| Status |",
            "|--------|",
            "| <!--@fg:red-->**failed**<!--/@fg--> |");

        Lines(md)[2].Should().Be("| <!--@fg:red-->**failed**<!--/@fg--> |");
    }

    [Fact]
    public void CopiedTable_PreservesEmptyCellsThroughARoundTrip()
    {
        string md = RoundTrip(
            "| A | B |",
            "|---|---|",
            "| 1 |  |");

        Lines(md)[2].Should().Be("| 1 |  |");
    }
}
