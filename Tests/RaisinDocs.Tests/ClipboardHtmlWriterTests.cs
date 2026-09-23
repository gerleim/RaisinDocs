using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>Copy-out: RaisinDocs markdown → CF_HTML clipboard payload.</summary>
public class ClipboardHtmlWriterTests
{
    private static string? ExtractFragment(string cfHtml)
    {
        const string startMarker = "<!--StartFragment-->";
        int start = cfHtml.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += startMarker.Length;
        int end = cfHtml.IndexOf("<!--EndFragment-->", start, StringComparison.Ordinal);
        return end < 0 ? null : cfHtml[start..end];
    }

    [Fact]
    public void CopyOut_PlainText_ReturnsNull()
    {
        ClipboardHtmlWriter.ConvertToHtmlClipboard("hello world").Should().BeNull();
    }

    [Fact]
    public void CopyOut_InlineFgColor_ProducesHtmlSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#FF0000;\">error</span>");
    }

    [Fact]
    public void CopyOut_InlineBgColor_ProducesHtmlSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@bg:lime-->highlight<!--/@bg-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"background-color:#00FF00;\">highlight</span>");
    }

    [Fact]
    public void CopyOut_FgAndBg_ProducesHtmlSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red bg:blue-->alert<!--/@-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("background-color:#0000FF;");
        fragment.Should().Contain("alert");
    }

    [Fact]
    public void CopyOut_MixedColorAndPlain_OneLine()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->: ok");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#FF0000;\">error</span>: ok");
    }

    [Fact]
    public void CopyOut_Bold_ProducesBoldSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("**important**");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"font-weight:bold;\">important</span>");
    }

    [Fact]
    public void CopyOut_Italic_ProducesItalicSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("*note*");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"font-style:italic;\">note</span>");
    }

    [Fact]
    public void CopyOut_BoldItalic_ProducesCombinedSpan()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("***wow***");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("font-weight:bold;");
        fragment.Should().Contain("font-style:italic;");
        fragment.Should().Contain("wow");
    }

    [Fact]
    public void CopyOut_BoldWithColor()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red-->**error**<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("font-weight:bold;");
        fragment.Should().Contain("error");
    }

    [Fact]
    public void CopyOut_DivColor_AppliesColorToLines()
    {
        var markdown = "<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->";
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#00FF00;\">line one</span>");
        fragment.Should().Contain("<span style=\"color:#00FF00;\">line two</span>");
        fragment.Should().NotContain("div");
    }

    [Fact]
    public void CopyOut_DivWithBg()
    {
        var markdown = "<!--@div bg:#333333-->\ntext\n<!--/@div-->";
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("background-color:#333333;");
        fragment.Should().Contain("text");
    }

    [Fact]
    public void CopyOut_HexColor_PreservedExactly()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:#F8F8F2-->text<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("color:#F8F8F2;");
    }

    [Fact]
    public void CopyOut_SpecialChars_HtmlEncoded()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red--><tag> & \"x\"<!--/@fg-->");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("&lt;tag&gt; &amp; &quot;x&quot;");
    }

    [Fact]
    public void CopyOut_MultiLine_PreservesLines()
    {
        var markdown = "<!--@fg:red-->error<!--/@fg-->\nok";
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("error</span>\nok");
    }

    [Fact]
    public void CopyOut_HasCfHtmlHeaders()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("<!--@fg:red-->error<!--/@fg-->");

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
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("**bold**");

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().StartWith("<pre ");
        fragment.Should().EndWith("</pre>");
    }

    [Fact]
    public void CopyOut_ListItemAsterisk_NotTreatedAsItalic()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("* list item");

        result.Should().BeNull();
    }

    [Fact]
    public void CopyOut_InlineColorInsideDiv_OverridesDiv()
    {
        var markdown = "<!--@div fg:lime-->\nplain\n<!--@fg:red-->error<!--/@fg-->\n<!--/@div-->";
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("<span style=\"color:#00FF00;\">plain</span>");
        fragment.Should().Contain("color:#FF0000;");
        fragment.Should().Contain("error");
    }

    [Fact]
    public void CopyOut_CrLfLineEndings_Handled()
    {
        var markdown = "<!--@fg:red-->error<!--/@fg-->\r\nok";
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown);

        result.Should().NotBeNull();
        var fragment = ExtractFragment(result!);
        fragment.Should().Contain("error</span>\nok");
    }

    // === Marker: a paste back into RaisinDocs uses the exact text, not the HTML ===

    [Fact]
    public void CopyOut_IsMarkedAsAMarkdownCopy()
    {
        var result = ClipboardHtmlWriter.ConvertToHtmlClipboard("2*3=6 and **x**")!;

        ClipboardHtmlWriter.IsMarkdownCopy(result).Should().BeTrue();
    }

    [Fact]
    public void OtherHtml_IsNotAMarkdownCopy()
    {
        var foreign = ClipboardHtmlWriter.WrapClipboardHeader(
            "<pre style=\"font-family:Consolas;\"><span style=\"color:#FF0000;\">error</span></pre>");

        ClipboardHtmlWriter.IsMarkdownCopy(foreign).Should().BeFalse();
    }

    // === Round-trip through the HTML alone (as any app other than RaisinDocs reads it) ===

    [Theory]
    [InlineData("<!--@fg:red-->error<!--/@fg-->: ok")]
    [InlineData("<!--@fg:red-->**error**<!--/@fg-->")]
    [InlineData("<!--@fg:red bg:blue-->alert<!--/@--> and *more*")]
    [InlineData("<!--@div fg:lime-->\nline one\nline two\n<!--/@div-->")]
    [InlineData("<!--@fg:red-->error<!--/@fg-->\nok")]
    public void RoundTrip_MarkdownSurvivesUnchanged(string markdown)
    {
        var cfHtml = ClipboardHtmlWriter.ConvertToHtmlClipboard(markdown)!;

        var pasted = HtmlBlockModelParser.ConvertHtmlToMarkdown(cfHtml, new MarkdownOutputSettings { PreserveColors = true });

        pasted.Should().Be(markdown);
    }
}
