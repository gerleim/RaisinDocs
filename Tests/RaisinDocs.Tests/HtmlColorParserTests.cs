using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class HtmlToMarkdownConverterTests
{
    private static string WrapCfHtml(string fragment)
    {
        var body = $"<html><body>\n<!--StartFragment-->{fragment}<!--EndFragment-->\n</body></html>";
        return $"Version:0.9\nStartHTML:00000097\nEndHTML:00000200\nStartFragment:00000131\nEndFragment:00000180\n{body}";
    }

    private static string PreWrap(string inner) =>
        $"<pre style=\"font-family:Consolas,'Courier New',monospace;font-size:10pt;\">{inner}</pre>";

    // --- Fragment extraction ---

    [Fact]
    public void ExtractFragment_ValidCfHtml_ReturnsFragment()
    {
        var cfHtml = WrapCfHtml("<pre>hello</pre>");
        var result = HtmlToMarkdownConverter.ExtractFragment(cfHtml);
        result.Should().Be("<pre>hello</pre>");
    }

    [Fact]
    public void ExtractFragment_NoMarkers_ReturnsNull()
    {
        HtmlToMarkdownConverter.ExtractFragment("just some text").Should().BeNull();
    }

    // --- No colors -> null ---

    [Fact]
    public void ConvertToColoredMarkdown_PlainPreNoSpans_ReturnsNull()
    {
        var cfHtml = WrapCfHtml(PreWrap("hello world"));
        HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    [Fact]
    public void ConvertToColoredMarkdown_NoPre_ReturnsNull()
    {
        var cfHtml = WrapCfHtml("<div>hello</div>");
        HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    // --- Single line, single color -> inline tag ---

    [Fact]
    public void SingleSpan_FgColor_InlineTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void SingleSpan_BgColor_InlineTag()
    {
        var html = PreWrap("<span style=\"background-color:#00FF00;\">highlight</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@bg:lime-->highlight<!--/@bg-->");
    }

    [Fact]
    public void SingleSpan_FgAndBg_InlineTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;background-color:#0000FF;\">alert</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red bg:blue-->alert<!--/@-->");
    }

    // --- Mixed colors on one line ---

    [Fact]
    public void MixedColors_SingleLine_MultipleInlineTags()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>: file not found");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->: file not found");
    }

    [Fact]
    public void TwoColors_SingleLine()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">red</span> and <span style=\"color:#00FF00;\">green</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->red<!--/@fg--> and <!--@fg:lime-->green<!--/@fg-->");
    }

    // --- Multiple lines, same color -> div ---

    [Fact]
    public void TwoLines_SameUniformColor_DivWrapper()
    {
        var html = PreWrap("<span style=\"color:#00FF00;\">line one</span>\n<span style=\"color:#00FF00;\">line two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->");
    }

    [Fact]
    public void ThreeLines_SameColor_SingleDiv()
    {
        var html = PreWrap(
            "<span style=\"color:#0000FF;\">one</span>\n" +
            "<span style=\"color:#0000FF;\">two</span>\n" +
            "<span style=\"color:#0000FF;\">three</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div fg:blue-->\none\ntwo\nthree\n<!--/@div-->");
    }

    // --- Mixed: some lines div, some inline ---

    [Fact]
    public void MixedLines_DivAndInline()
    {
        var html = PreWrap(
            "<span style=\"color:#FF0000;\">error</span>: bad\n" +
            "<span style=\"color:#00FF00;\">ok one</span>\n" +
            "<span style=\"color:#00FF00;\">ok two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be(
            "<!--@fg:red-->error<!--/@fg-->: bad\n" +
            "<!--@div fg:lime-->\n" +
            "ok one\n" +
            "ok two\n" +
            "<!--/@div-->");
    }

    // --- Single uniform line stays inline (not div) ---

    [Fact]
    public void SingleUniformLine_UsesInlineNotDiv()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">all red</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().NotContain("div");
        result.Should().Be("<!--@fg:red-->all red<!--/@fg-->");
    }

    // --- HTML entity decoding ---

    [Fact]
    public void HtmlEntities_DecodedCorrectly()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">&lt;tag&gt; &amp; &quot;text&quot;</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red--><tag> & \"text\"<!--/@fg-->");
    }

    // --- Empty lines preserved ---

    [Fact]
    public void EmptyLines_Preserved()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">line1</span>\n\n<span style=\"color:#FF0000;\">line3</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("\n\n");
    }

    // --- Adjacent same-color segments merged ---

    [Fact]
    public void AdjacentSameColorSegments_Merged()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">hello </span><span style=\"color:#FF0000;\">world</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->hello world<!--/@fg-->");
    }

    // --- Div with bg (no CSS name for #333333) ---

    [Fact]
    public void TwoLines_SameBg_DivWrapper()
    {
        var html = PreWrap(
            "<span style=\"background-color:#333333;\">line one</span>\n" +
            "<span style=\"background-color:#333333;\">line two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div bg:#333333-->\nline one\nline two\n<!--/@div-->");
    }

    // --- Round-trip with MarkdownParser ---

    [Fact]
    public void RoundTrip_InlineTag_ParsesCorrectly()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>: ok");
        var cfHtml = WrapCfHtml(html);

        var markdown = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml)!;

        var spans = MarkdownParser.ParseInlineColorTags(markdown, null);
        spans.Should().NotBeNull();
        spans.Should().ContainSingle();
        spans![0].Foreground.Should().Be(new RgbColor(0xFF, 0, 0));
    }

    // --- Short hex colors ---

    [Fact]
    public void ShortHexColor_Parsed()
    {
        var html = PreWrap("<span style=\"color:#F00;\">red</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->red<!--/@fg-->");
    }

    // --- Non-named color stays as hex ---

    [Fact]
    public void NonNamedColor_StaysHex()
    {
        var html = PreWrap("<span style=\"color:#F8F8F2;\">text</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#F8F8F2-->text<!--/@fg-->");
    }

    // --- Default text mixed with colored ---

    [Fact]
    public void DefaultTextBeforeAndAfterSpan()
    {
        var html = PreWrap("prefix <span style=\"color:#00FF00;\">green</span> suffix");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("prefix <!--@fg:lime-->green<!--/@fg--> suffix");
    }

    // --- Bold ---

    [Fact]
    public void BoldOnly_WrapsInMarkdown()
    {
        var html = PreWrap("<span style=\"font-weight:bold;\">important</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("**important**");
    }

    [Fact]
    public void BoldWithColor_MarkdownInsideColorTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;font-weight:bold;\">error</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->**error**<!--/@fg-->");
    }

    // --- Italic ---

    [Fact]
    public void ItalicOnly_WrapsInMarkdown()
    {
        var html = PreWrap("<span style=\"font-style:italic;\">note</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("*note*");
    }

    [Fact]
    public void ItalicWithColor_MarkdownInsideColorTag()
    {
        var html = PreWrap("<span style=\"color:#00FF00;font-style:italic;\">hint</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:lime-->*hint*<!--/@fg-->");
    }

    // --- Bold + Italic ---

    [Fact]
    public void BoldItalic_TripleAsterisks()
    {
        var html = PreWrap("<span style=\"font-weight:bold;font-style:italic;\">wow</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("***wow***");
    }

    [Fact]
    public void BoldItalicWithColor()
    {
        var html = PreWrap("<span style=\"color:#FF0000;font-weight:bold;font-style:italic;\">alert</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->***alert***<!--/@fg-->");
    }

    // --- Bold inside div ---

    [Fact]
    public void BoldInsideDiv_StylePreserved()
    {
        var html = PreWrap(
            "<span style=\"color:#00FF00;font-weight:bold;\">line one</span>\n" +
            "<span style=\"color:#00FF00;font-weight:bold;\">line two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div fg:lime-->\n**line one**\n**line two**\n<!--/@div-->");
    }

    // --- Mixed bold and non-bold on same line ---

    [Fact]
    public void MixedBoldAndNormal_SameColor()
    {
        var html = PreWrap(
            "<span style=\"color:#FF0000;font-weight:bold;\">error</span>" +
            "<span style=\"color:#FF0000;\">: details</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->**error**: details<!--/@fg-->");
    }

    // --- Non-pre HTML (Word-style <p> elements) ---

    [Fact]
    public void WordStyle_ParagraphsWithColorSpans()
    {
        var html =
            "<p><span style='color:#B1B9F9'>first = true</span></p>" +
            "<p><span style='color:#4EBA65'>ok</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be(
            "<!--@fg:#B1B9F9-->first = true<!--/@fg-->\n" +
            "<!--@fg:#4EBA65-->ok<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NestedSpans_InnerColorWins()
    {
        var html =
            "<p><span style='font-size:10pt'>text <span style='color:#B1B9F9'>colored</span> more</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("text <!--@fg:#B1B9F9-->colored<!--/@fg--> more");
    }

    [Fact]
    public void WordStyle_BoldTag()
    {
        var html =
            "<p><b><span style='font-size:10pt'>Update</span></b></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("**Update**");
    }

    [Fact]
    public void WordStyle_SingleQuotedStyle()
    {
        var html = "<p><span style='color:#FF0000'>error</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NamedCssColor()
    {
        var html = "<p><span style='color:white'>text</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:white-->text<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NbspEntity_DecodedAsSpace()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">a&nbsp;b</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->a b<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NoParagraphsNoColors_ReturnsNull()
    {
        var cfHtml = WrapCfHtml("<div>hello</div>");
        HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    [Fact]
    public void WordStyle_BoldWithColoredSpan()
    {
        var html =
            "<p><span style='color:#4EBA65'>● </span>" +
            "<b><span style='font-size:10pt'>Update</span></b>" +
            "<span style='font-size:10pt'>(file.cs)</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#4EBA65-->● <!--/@fg-->**Update**(file.cs)");
    }

    // === Copy-out tests (markdown → HTML clipboard) ===

    [Fact]
    public void CopyOut_PlainText_ReturnsNull()
    {
        HtmlToMarkdownConverter.ConvertToHtmlClipboard("hello world").Should().BeNull();
    }

    [Fact]
    public void CopyOut_InlineFgColor_ProducesHtmlSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#FF0000;\">error</span>");
    }

    [Fact]
    public void CopyOut_InlineBgColor_ProducesHtmlSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@bg:lime-->highlight<!--/@bg-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"background-color:#00FF00;\">highlight</span>");
    }

    [Fact]
    public void CopyOut_FgAndBg_ProducesHtmlSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red bg:blue-->alert<!--/@-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("background-color:#0000FF;");
        fragment.Should().Contain("alert");
    }

    [Fact]
    public void CopyOut_MixedColorAndPlain_OneLine()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->: ok");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#FF0000;\">error</span>: ok");
    }

    [Fact]
    public void CopyOut_Bold_ProducesBoldSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("**important**");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"font-weight:bold;\">important</span>");
    }

    [Fact]
    public void CopyOut_Italic_ProducesItalicSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("*note*");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"font-style:italic;\">note</span>");
    }

    [Fact]
    public void CopyOut_BoldItalic_ProducesCombinedSpan()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("***wow***");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("font-weight:bold;");
        fragment.Should().Contain("font-style:italic;");
        fragment.Should().Contain("wow");
    }

    [Fact]
    public void CopyOut_BoldWithColor()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red-->**error**<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("font-weight:bold;");
        fragment.Should().Contain("error");
    }

    [Fact]
    public void CopyOut_DivColor_AppliesColorToLines()
    {
        var markdown = "<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->";
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#00FF00;\">line one</span>");
        fragment.Should().Contain("<span style=\"color:#00FF00;\">line two</span>");
        fragment.Should().NotContain("div");
    }

    [Fact]
    public void CopyOut_DivWithBg()
    {
        var markdown = "<!--@div bg:#333333-->\ntext\n<!--/@div-->";
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("background-color:#333333;");
        fragment.Should().Contain("text");
    }

    [Fact]
    public void CopyOut_HexColor_PreservedExactly()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:#F8F8F2-->text<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("color:#F8F8F2;");
    }

    [Fact]
    public void CopyOut_SpecialChars_HtmlEncoded()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red--><tag> & \"x\"<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("&lt;tag&gt; &amp; &quot;x&quot;");
    }

    [Fact]
    public void CopyOut_MultiLine_PreservesLines()
    {
        var markdown = "<!--@fg:red-->error<!--/@fg-->\nok";
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("error</span>\nok");
    }

    [Fact]
    public void CopyOut_HasCfHtmlHeaders()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->");

        result.Should().NotBeNull();
        result.Should().StartWith("Version:0.9");
        result.Should().Contain("StartHTML:");
        result.Should().Contain("EndHTML:");
        result.Should().Contain("StartFragment:");
        result.Should().Contain("EndFragment:");
        result.Should().Contain("<!--StartFragment-->");
        result.Should().Contain("<!--EndFragment-->");
    }

    [Fact]
    public void CopyOut_HasPreWrapper()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("**bold**");

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().StartWith("<pre ");
        fragment.Should().EndWith("</pre>");
    }

    [Fact]
    public void CopyOut_ListItemAsterisk_NotTreatedAsItalic()
    {
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard("* list item");

        result.Should().BeNull();
    }

    [Fact]
    public void CopyOut_RoundTrip_ColorSurvives()
    {
        var markdown = "<!--@fg:red-->error<!--/@fg-->: ok";
        var cfHtml = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown)!;

        var roundTripped = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        roundTripped.Should().NotBeNull();
        roundTripped.Should().Contain("red");
        roundTripped.Should().Contain("error");
    }

    [Fact]
    public void CopyOut_RoundTrip_BoldSurvives()
    {
        var markdown = "<!--@fg:red-->**error**<!--/@fg-->";
        var cfHtml = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown)!;

        var roundTripped = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        roundTripped.Should().Be("<!--@fg:red-->**error**<!--/@fg-->");
    }

    [Fact]
    public void CopyOut_InlineColorInsideDiv_OverridesDiv()
    {
        var markdown = "<!--@div fg:lime-->\nplain\n<!--@fg:red-->error<!--/@fg-->\n<!--/@div-->";
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#00FF00;\">plain</span>");
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("error");
    }

    [Fact]
    public void CopyOut_CrLfLineEndings_Handled()
    {
        var markdown = "<!--@fg:red-->error<!--/@fg-->\r\nok";
        var result = HtmlToMarkdownConverter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = HtmlToMarkdownConverter.ExtractFragment(result!);
        fragment.Should().Contain("error</span>\nok");
    }

    // === Phase 1 Tests: Headers, Blockquotes, Horizontal Rules ===

    [Fact]
    public void Header_H1_ConvertsToMarkdownHeading()
    {
        var html = "<h1>RAW (Chronicles of Darkness)</h1>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("# RAW (Chronicles of Darkness)");
    }

    [Fact]
    public void Header_H2_ConvertsToDoubleHash()
    {
        var html = "<h2>One Leg Wrack</h2>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("## One Leg Wrack");
    }

    [Fact]
    public void Header_H3_ConvertsToTripleHash()
    {
        var html = "<h3>Question 1</h3>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("### Question 1");
    }

    [Fact]
    public void Header_H4_ConvertsToFourHash()
    {
        var html = "<h4>Details</h4>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("#### Details");
    }

    [Fact]
    public void Header_H5_ConvertsToFiveHash()
    {
        var html = "<h5>Subheading</h5>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("##### Subheading");
    }

    [Fact]
    public void Header_H6_ConvertsSixHash()
    {
        var html = "<h6>Tiny heading</h6>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("###### Tiny heading");
    }

    [Fact]
    public void Header_WithBold_PreservesFormatting()
    {
        var html = "<h2><strong>Important</strong> Section</h2>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("## ");
        result.Should().Contain("Important");
        result.Should().Contain("Section");
    }

    [Fact]
    public void Header_WithDataAttributes_IgnoresAttributes()
    {
        var html = "<h1 data-section-id=\"abc123\" data-start=\"0\" data-end=\"10\">Title</h1>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("# Title");
    }

    [Fact]
    public void HorizontalRule_SimpleHr_ConvertsToTripleDash()
    {
        var html = "<hr>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("---");
    }

    [Fact]
    public void HorizontalRule_HrWithAttributes_Ignored()
    {
        var html = "<hr data-start=\"100\" data-end=\"200\">";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("---");
    }

    [Fact]
    public void Blockquote_SimpleQuote_ConvertsToGreaterThan()
    {
        var html = "<blockquote><p>If you move at all, you cannot take any other action.</p></blockquote>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result?.Trim().Should().Contain("> ");
        result.Should().Contain("If you move at all");
    }

    [Fact]
    public void Blockquote_MultiLineQuote_EachLineGetsPrefixed()
    {
        var html = "<blockquote><p>Line one</p><p>Line two</p></blockquote>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        var lines = result!.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lines.Should().AllSatisfy(line =>
            line.Should().StartWith(">", "each line in blockquote should start with >"));
    }

    [Fact]
    public void HeaderAndHr_SeparatedByRule()
    {
        var html = "<h1>Title</h1><hr><h2>Subtitle</h2>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("# Title");
        result.Should().Contain("---");
        result.Should().Contain("## Subtitle");
    }

    [Fact]
    public void ComplexDocument_HeaderListHrParagraph()
    {
        var html =
            "<h1>RAW (Chronicles of Darkness)</h1>" +
            "<h2>One Leg Wrack</h2>" +
            "<p>Effects:</p>" +
            "<ul>" +
            "<li><strong>Speed is halved</strong> (minimum 1).</li>" +
            "<li><strong>Defense is reduced by 2.</strong></li>" +
            "</ul>" +
            "<hr>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("# RAW (Chronicles of Darkness)");
        result.Should().Contain("## One Leg Wrack");
        result.Should().Contain("Effects:");
        result.Should().Contain("**Speed is halved**");
        result.Should().Contain("**Defense is reduced by 2.**");
        result.Should().Contain("---");
    }

    [Fact]
    public void Header_WithInlineFormatting_ConvertsCorrectly()
    {
        var html = "<h3>Question 1 — What counts as a <em>Physical roll</em> requiring <strong>movement</strong>?</h3>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlToMarkdownConverter.ConvertToColoredMarkdown(cfHtml);

        // Core functionality: header is converted to markdown
        result.Should().Contain("### ");
        result.Should().Contain("Question 1");
        result.Should().Contain("Physical roll");
        result.Should().Contain("movement");
    }
}



