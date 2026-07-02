using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class HtmlColorParserTests
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
        var result = HtmlColorParser.ExtractFragment(cfHtml);
        result.Should().Be("<pre>hello</pre>");
    }

    [Fact]
    public void ExtractFragment_NoMarkers_ReturnsNull()
    {
        HtmlColorParser.ExtractFragment("just some text").Should().BeNull();
    }

    // --- No colors -> null ---

    [Fact]
    public void ConvertToColoredMarkdown_PlainPreNoSpans_ReturnsNull()
    {
        var cfHtml = WrapCfHtml(PreWrap("hello world"));
        HtmlColorParser.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    [Fact]
    public void ConvertToColoredMarkdown_NoPre_ReturnsNull()
    {
        var cfHtml = WrapCfHtml("<div>hello</div>");
        HtmlColorParser.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    // --- Single line, single color -> inline tag ---

    [Fact]
    public void SingleSpan_FgColor_InlineTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void SingleSpan_BgColor_InlineTag()
    {
        var html = PreWrap("<span style=\"background-color:#00FF00;\">highlight</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@bg:lime-->highlight<!--/@bg-->");
    }

    [Fact]
    public void SingleSpan_FgAndBg_InlineTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;background-color:#0000FF;\">alert</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red bg:blue-->alert<!--/@-->");
    }

    // --- Mixed colors on one line ---

    [Fact]
    public void MixedColors_SingleLine_MultipleInlineTags()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>: file not found");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->: file not found");
    }

    [Fact]
    public void TwoColors_SingleLine()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">red</span> and <span style=\"color:#00FF00;\">green</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->red<!--/@fg--> and <!--@fg:lime-->green<!--/@fg-->");
    }

    // --- Multiple lines, same color -> div ---

    [Fact]
    public void TwoLines_SameUniformColor_DivWrapper()
    {
        var html = PreWrap("<span style=\"color:#00FF00;\">line one</span>\n<span style=\"color:#00FF00;\">line two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().NotContain("div");
        result.Should().Be("<!--@fg:red-->all red<!--/@fg-->");
    }

    // --- HTML entity decoding ---

    [Fact]
    public void HtmlEntities_DecodedCorrectly()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">&lt;tag&gt; &amp; &quot;text&quot;</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red--><tag> & \"text\"<!--/@fg-->");
    }

    // --- Empty lines preserved ---

    [Fact]
    public void EmptyLines_Preserved()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">line1</span>\n\n<span style=\"color:#FF0000;\">line3</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Contain("\n\n");
    }

    // --- Adjacent same-color segments merged ---

    [Fact]
    public void AdjacentSameColorSegments_Merged()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">hello </span><span style=\"color:#FF0000;\">world</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div bg:#333333-->\nline one\nline two\n<!--/@div-->");
    }

    // --- Round-trip with MarkdownParser ---

    [Fact]
    public void RoundTrip_InlineTag_ParsesCorrectly()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>: ok");
        var cfHtml = WrapCfHtml(html);

        var markdown = HtmlColorParser.ConvertToColoredMarkdown(cfHtml)!;

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->red<!--/@fg-->");
    }

    // --- Non-named color stays as hex ---

    [Fact]
    public void NonNamedColor_StaysHex()
    {
        var html = PreWrap("<span style=\"color:#F8F8F2;\">text</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#F8F8F2-->text<!--/@fg-->");
    }

    // --- Default text mixed with colored ---

    [Fact]
    public void DefaultTextBeforeAndAfterSpan()
    {
        var html = PreWrap("prefix <span style=\"color:#00FF00;\">green</span> suffix");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("prefix <!--@fg:lime-->green<!--/@fg--> suffix");
    }

    // --- Bold ---

    [Fact]
    public void BoldOnly_WrapsInMarkdown()
    {
        var html = PreWrap("<span style=\"font-weight:bold;\">important</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("**important**");
    }

    [Fact]
    public void BoldWithColor_MarkdownInsideColorTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;font-weight:bold;\">error</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->**error**<!--/@fg-->");
    }

    // --- Italic ---

    [Fact]
    public void ItalicOnly_WrapsInMarkdown()
    {
        var html = PreWrap("<span style=\"font-style:italic;\">note</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("*note*");
    }

    [Fact]
    public void ItalicWithColor_MarkdownInsideColorTag()
    {
        var html = PreWrap("<span style=\"color:#00FF00;font-style:italic;\">hint</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:lime-->*hint*<!--/@fg-->");
    }

    // --- Bold + Italic ---

    [Fact]
    public void BoldItalic_TripleAsterisks()
    {
        var html = PreWrap("<span style=\"font-weight:bold;font-style:italic;\">wow</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("***wow***");
    }

    [Fact]
    public void BoldItalicWithColor()
    {
        var html = PreWrap("<span style=\"color:#FF0000;font-weight:bold;font-style:italic;\">alert</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->**error**<!--/@fg--><!--@fg:red-->: details<!--/@fg-->");
    }

    // --- Non-pre HTML (Word-style <p> elements) ---

    [Fact]
    public void WordStyle_ParagraphsWithColorSpans()
    {
        var html =
            "<p><span style='color:#B1B9F9'>first = true</span></p>" +
            "<p><span style='color:#4EBA65'>ok</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

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

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("text <!--@fg:#B1B9F9-->colored<!--/@fg--> more");
    }

    [Fact]
    public void WordStyle_BoldTag()
    {
        var html =
            "<p><b><span style='font-size:10pt'>Update</span></b></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("**Update**");
    }

    [Fact]
    public void WordStyle_SingleQuotedStyle()
    {
        var html = "<p><span style='color:#FF0000'>error</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NamedCssColor()
    {
        var html = "<p><span style='color:white'>text</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:white-->text<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NbspEntity_DecodedAsSpace()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">a&nbsp;b</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:red-->a b<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NoParagraphsNoColors_ReturnsNull()
    {
        var cfHtml = WrapCfHtml("<div>hello</div>");
        HtmlColorParser.ConvertToColoredMarkdown(cfHtml).Should().BeNull();
    }

    [Fact]
    public void WordStyle_BoldWithColoredSpan()
    {
        var html =
            "<p><span style='color:#4EBA65'>● </span>" +
            "<b><span style='font-size:10pt'>Update</span></b>" +
            "<span style='font-size:10pt'>(file.cs)</span></p>";
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#4EBA65-->● <!--/@fg-->**Update**(file.cs)");
    }
}
