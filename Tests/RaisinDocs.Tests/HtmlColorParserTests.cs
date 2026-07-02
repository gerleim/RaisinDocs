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

        result.Should().Be("<!--@fg:#FF0000-->error<!--/@fg-->");
    }

    [Fact]
    public void SingleSpan_BgColor_InlineTag()
    {
        var html = PreWrap("<span style=\"background-color:#00FF00;\">highlight</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@bg:#00FF00-->highlight<!--/@bg-->");
    }

    [Fact]
    public void SingleSpan_FgAndBg_InlineTag()
    {
        var html = PreWrap("<span style=\"color:#FF0000;background-color:#0000FF;\">alert</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#FF0000 bg:#0000FF-->alert<!--/@-->");
    }

    // --- Mixed colors on one line ---

    [Fact]
    public void MixedColors_SingleLine_MultipleInlineTags()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">error</span>: file not found");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#FF0000-->error<!--/@fg-->: file not found");
    }

    [Fact]
    public void TwoColors_SingleLine()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">red</span> and <span style=\"color:#00FF00;\">green</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#FF0000-->red<!--/@fg--> and <!--@fg:#00FF00-->green<!--/@fg-->");
    }

    // --- Multiple lines, same color -> div ---

    [Fact]
    public void TwoLines_SameUniformColor_DivWrapper()
    {
        var html = PreWrap("<span style=\"color:#00FF00;\">line one</span>\n<span style=\"color:#00FF00;\">line two</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@div fg:#00FF00-->\nline one\nline two\n<!--/@div-->");
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

        result.Should().Be("<!--@div fg:#0000FF-->\none\ntwo\nthree\n<!--/@div-->");
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
            "<!--@fg:#FF0000-->error<!--/@fg-->: bad\n" +
            "<!--@div fg:#00FF00-->\n" +
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
        result.Should().Be("<!--@fg:#FF0000-->all red<!--/@fg-->");
    }

    // --- HTML entity decoding ---

    [Fact]
    public void HtmlEntities_DecodedCorrectly()
    {
        var html = PreWrap("<span style=\"color:#FF0000;\">&lt;tag&gt; &amp; &quot;text&quot;</span>");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("<!--@fg:#FF0000--><tag> & \"text\"<!--/@fg-->");
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

        result.Should().Be("<!--@fg:#FF0000-->hello world<!--/@fg-->");
    }

    // --- Div with bg ---

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

        result.Should().Be("<!--@fg:#FF0000-->red<!--/@fg-->");
    }

    // --- Default text mixed with colored ---

    [Fact]
    public void DefaultTextBeforeAndAfterSpan()
    {
        var html = PreWrap("prefix <span style=\"color:#00FF00;\">green</span> suffix");
        var cfHtml = WrapCfHtml(html);

        var result = HtmlColorParser.ConvertToColoredMarkdown(cfHtml);

        result.Should().Be("prefix <!--@fg:#00FF00-->green<!--/@fg--> suffix");
    }
}
