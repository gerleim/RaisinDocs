using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.BlockModel;

/// <summary>
/// Colour and emphasis on paste: coloured &lt;pre&gt; text as terminals and RaisinDocs's own
/// copy-out produce it, and Word-style &lt;p&gt; spans.
/// </summary>
public class HtmlColorPasteTests
{
    private static string WrapCfHtml(string fragment)
    {
        var body = $"<html><body>\n<!--StartFragment-->{fragment}<!--EndFragment-->\n</body></html>";
        return $"Version:0.9\nStartHTML:00000097\nEndHTML:00000200\nStartFragment:00000131\nEndFragment:00000180\n{body}";
    }

    private static string PreWrap(string inner) =>
        $"<pre style=\"font-family:Consolas,'Courier New',monospace;font-size:10pt;\">{inner}</pre>";

    private static string? Convert(string fragment) =>
        HtmlBlockModelParser.ConvertHtmlToMarkdown(WrapCfHtml(fragment), new MarkdownOutputSettings { PreserveColors = true });

    // --- Uncoloured ---

    [Fact]
    public void PlainPre_PastesItsText()
    {
        Convert(PreWrap("hello world")).Should().Be("hello world");
    }

    [Fact]
    public void NoBlockElements_ReturnsNull()
    {
        Convert("<div>hello</div>").Should().BeNull();
    }

    // --- Single line, single colour -> inline tag ---

    [Fact]
    public void SingleSpan_FgColor_InlineTag()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">error</span>"))
            .Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void SingleSpan_BgColor_InlineTag()
    {
        Convert(PreWrap("<span style=\"background-color:#00FF00;\">highlight</span>"))
            .Should().Be("<!--@bg:lime-->highlight<!--/@bg-->");
    }

    [Fact]
    public void SingleSpan_FgAndBg_InlineTag()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;background-color:#0000FF;\">alert</span>"))
            .Should().Be("<!--@fg:red bg:blue-->alert<!--/@-->");
    }

    // --- Mixed colours on one line ---

    [Fact]
    public void MixedColors_SingleLine_MultipleInlineTags()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">error</span>: file not found"))
            .Should().Be("<!--@fg:red-->error<!--/@fg-->: file not found");
    }

    [Fact]
    public void TwoColors_SingleLine()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">red</span> and <span style=\"color:#00FF00;\">green</span>"))
            .Should().Be("<!--@fg:red-->red<!--/@fg--> and <!--@fg:lime-->green<!--/@fg-->");
    }

    [Fact]
    public void DefaultTextBeforeAndAfterSpan()
    {
        Convert(PreWrap("prefix <span style=\"color:#00FF00;\">green</span> suffix"))
            .Should().Be("prefix <!--@fg:lime-->green<!--/@fg--> suffix");
    }

    // --- Multiple lines, same colour -> div ---

    [Fact]
    public void TwoLines_SameUniformColor_DivWrapper()
    {
        Convert(PreWrap("<span style=\"color:#00FF00;\">line one</span>\n<span style=\"color:#00FF00;\">line two</span>"))
            .Should().Be("<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->");
    }

    [Fact]
    public void ThreeLines_SameColor_SingleDiv()
    {
        Convert(PreWrap(
                "<span style=\"color:#0000FF;\">one</span>\n" +
                "<span style=\"color:#0000FF;\">two</span>\n" +
                "<span style=\"color:#0000FF;\">three</span>"))
            .Should().Be("<!--@div fg:blue-->\none\ntwo\nthree\n<!--/@div-->");
    }

    [Fact]
    public void TwoLines_SameBg_DivWrapper()
    {
        Convert(PreWrap(
                "<span style=\"background-color:#333333;\">line one</span>\n" +
                "<span style=\"background-color:#333333;\">line two</span>"))
            .Should().Be("<!--@div bg:#333333-->\nline one\nline two\n<!--/@div-->");
    }

    [Fact]
    public void SpanAcrossANewline_ColoursBothLines()
    {
        Convert(PreWrap("<span style=\"color:#00FF00;\">line one\nline two</span>"))
            .Should().Be("<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->");
    }

    [Fact]
    public void MixedLines_DivAndInline()
    {
        Convert(PreWrap(
                "<span style=\"color:#FF0000;\">error</span>: bad\n" +
                "<span style=\"color:#00FF00;\">ok one</span>\n" +
                "<span style=\"color:#00FF00;\">ok two</span>"))
            .Should().Be(
                "<!--@fg:red-->error<!--/@fg-->: bad\n" +
                "<!--@div fg:lime-->\n" +
                "ok one\n" +
                "ok two\n" +
                "<!--/@div-->");
    }

    [Fact]
    public void SingleUniformLine_UsesInlineNotDiv()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">all red</span>"))
            .Should().Be("<!--@fg:red-->all red<!--/@fg-->");
    }

    // --- Text handling ---

    [Fact]
    public void HtmlEntities_DecodedCorrectly()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">&lt;tag&gt; &amp; &quot;text&quot;</span>"))
            .Should().Be("<!--@fg:red--><tag> & \"text\"<!--/@fg-->");
    }

    [Fact]
    public void NbspEntity_DecodedAsSpace()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">a&nbsp;b</span>"))
            .Should().Be("<!--@fg:red-->a b<!--/@fg-->");
    }

    [Fact]
    public void EmptyLines_Preserved()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">line1</span>\n\n<span style=\"color:#FF0000;\">line3</span>"))
            .Should().Be("<!--@fg:red-->line1<!--/@fg-->\n\n<!--@fg:red-->line3<!--/@fg-->");
    }

    [Fact]
    public void Whitespace_IsKeptAsWritten()
    {
        Convert(PreWrap("  indented   gap\n\ttab"))
            .Should().Be("  indented   gap\n\ttab");
    }

    [Fact]
    public void NewlinesJustInsideTheTags_AreNotLines()
    {
        Convert("<pre>\nfirst\nsecond\n</pre>").Should().Be("first\nsecond");
    }

    [Fact]
    public void BrTag_EndsALine()
    {
        Convert(PreWrap("one<br>two")).Should().Be("one\ntwo");
    }

    [Fact]
    public void AdjacentSameColorSegments_Merged()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;\">hello </span><span style=\"color:#FF0000;\">world</span>"))
            .Should().Be("<!--@fg:red-->hello world<!--/@fg-->");
    }

    // --- Colour values ---

    [Fact]
    public void ShortHexColor_Parsed()
    {
        Convert(PreWrap("<span style=\"color:#F00;\">red</span>"))
            .Should().Be("<!--@fg:red-->red<!--/@fg-->");
    }

    [Fact]
    public void NonNamedColor_StaysHex()
    {
        Convert(PreWrap("<span style=\"color:#F8F8F2;\">text</span>"))
            .Should().Be("<!--@fg:#F8F8F2-->text<!--/@fg-->");
    }

    [Fact]
    public void PreserveColorsOff_DropsTheTags()
    {
        var markdown = HtmlBlockModelParser.ConvertHtmlToMarkdown(
            WrapCfHtml(PreWrap("<span style=\"color:#FF0000;font-weight:bold;\">error</span>: ok")),
            new MarkdownOutputSettings { PreserveColors = false });

        markdown.Should().Be("**error**: ok");
    }

    [Fact]
    public void InlineTag_ParsesBackAsAColorSpan()
    {
        var markdown = Convert(PreWrap("<span style=\"color:#FF0000;\">error</span>: ok"))!;

        var spans = MarkdownParser.ParseInlineColorTags(markdown, null);
        spans.Should().NotBeNull();
        spans.Should().ContainSingle();
        spans![0].Foreground.Should().Be(new RgbColor(0xFF, 0, 0));
    }

    // --- Emphasis ---

    [Fact]
    public void BoldOnly_WrapsInMarkdown()
    {
        Convert(PreWrap("<span style=\"font-weight:bold;\">important</span>")).Should().Be("**important**");
    }

    [Fact]
    public void BoldWithColor_MarkdownInsideColorTag()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;font-weight:bold;\">error</span>"))
            .Should().Be("<!--@fg:red-->**error**<!--/@fg-->");
    }

    [Fact]
    public void ItalicOnly_WrapsInMarkdown()
    {
        Convert(PreWrap("<span style=\"font-style:italic;\">note</span>")).Should().Be("*note*");
    }

    [Fact]
    public void ItalicWithColor_MarkdownInsideColorTag()
    {
        Convert(PreWrap("<span style=\"color:#00FF00;font-style:italic;\">hint</span>"))
            .Should().Be("<!--@fg:lime-->*hint*<!--/@fg-->");
    }

    [Fact]
    public void BoldItalic_TripleAsterisks()
    {
        Convert(PreWrap("<span style=\"font-weight:bold;font-style:italic;\">wow</span>")).Should().Be("***wow***");
    }

    [Fact]
    public void BoldItalicWithColor()
    {
        Convert(PreWrap("<span style=\"color:#FF0000;font-weight:bold;font-style:italic;\">alert</span>"))
            .Should().Be("<!--@fg:red-->***alert***<!--/@fg-->");
    }

    [Fact]
    public void BoldTagInsidePre()
    {
        Convert(PreWrap("<b>bold</b> plain")).Should().Be("**bold** plain");
    }

    [Fact]
    public void BoldInsideDiv_StylePreserved()
    {
        Convert(PreWrap(
                "<span style=\"color:#00FF00;font-weight:bold;\">line one</span>\n" +
                "<span style=\"color:#00FF00;font-weight:bold;\">line two</span>"))
            .Should().Be("<!--@div fg:lime-->\n**line one**\n**line two**\n<!--/@div-->");
    }

    [Fact]
    public void MixedBoldAndNormal_SameColor_OneColorTag()
    {
        Convert(PreWrap(
                "<span style=\"color:#FF0000;font-weight:bold;\">error</span>" +
                "<span style=\"color:#FF0000;\">: details</span>"))
            .Should().Be("<!--@fg:red-->**error**: details<!--/@fg-->");
    }

    // --- Pre among other blocks ---

    [Fact]
    public void PreFollowedByParagraph_BothKept()
    {
        // "<pre" also starts with "<p": the paragraph parser must not take it.
        Convert("<pre>code</pre><p>text</p>").Should().Be("code\n\ntext");
    }

    [Fact]
    public void ParagraphFollowedByPre_BothKept()
    {
        Convert("<p>text</p><pre><span style=\"color:#FF0000;\">error</span></pre>")
            .Should().Be("text\n\n<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void EmptyPre_AddsNothing()
    {
        Convert("<p>text</p><pre></pre>").Should().Be("text");
    }

    // --- Word-style <p> elements ---

    [Fact]
    public void WordStyle_NestedSpans_InnerColorWins()
    {
        Convert("<p><span style='font-size:10pt'>text <span style='color:#B1B9F9'>colored</span> more</span></p>")
            .Should().Be("text <!--@fg:#B1B9F9-->colored<!--/@fg--> more");
    }

    [Fact]
    public void WordStyle_BoldTag()
    {
        Convert("<p><b><span style='font-size:10pt'>Update</span></b></p>").Should().Be("**Update**");
    }

    [Fact]
    public void WordStyle_SingleQuotedStyle()
    {
        Convert("<p><span style='color:#FF0000'>error</span></p>").Should().Be("<!--@fg:red-->error<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_NamedCssColor()
    {
        Convert("<p><span style='color:white'>text</span></p>").Should().Be("<!--@fg:white-->text<!--/@fg-->");
    }

    [Fact]
    public void WordStyle_BoldWithColoredSpan()
    {
        Convert(
                "<p><span style='color:#4EBA65'>● </span>" +
                "<b><span style='font-size:10pt'>Update</span></b>" +
                "<span style='font-size:10pt'>(file.cs)</span></p>")
            .Should().Be("<!--@fg:#4EBA65-->● <!--/@fg-->**Update**(file.cs)");
    }
}
