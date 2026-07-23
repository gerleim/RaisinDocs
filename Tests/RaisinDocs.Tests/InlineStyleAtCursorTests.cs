using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class InlineStyleAtCursorTests
{
    private static ParsedBlock ParseSingle(string text)
    {
        return MarkdownParser.Parse(i => text, 1)[0];
    }

    // --- Bold ---

    [Fact]
    public void CursorInsideBold_DetectsBold()
    {
        // "**bold**" — cursor at offset 4 (inside "bold")
        var block = ParseSingle("**bold**");
        block.HasStyleAt(4, InlineStyle.Bold).Should().BeTrue();
    }

    [Fact]
    public void CursorAtBoldStart_DetectsBold()
    {
        // "**bold**" — cursor at offset 2 (start of "bold")
        var block = ParseSingle("**bold**");
        block.HasStyleAt(2, InlineStyle.Bold).Should().BeTrue();
    }

    [Fact]
    public void CursorOutsideBold_DoesNotDetectBold()
    {
        // "before **bold** after" — cursor at offset 3 (inside "before")
        var block = ParseSingle("before **bold** after");
        block.HasStyleAt(3, InlineStyle.Bold).Should().BeFalse();
    }

    [Fact]
    public void CursorAfterBold_DoesNotDetectBold()
    {
        // "**bold** after" — cursor at offset 10 (inside "after")
        var block = ParseSingle("**bold** after");
        block.HasStyleAt(10, InlineStyle.Bold).Should().BeFalse();
    }

    // --- Italic ---

    [Fact]
    public void CursorInsideItalic_DetectsItalic()
    {
        var block = ParseSingle("*italic*");
        block.HasStyleAt(3, InlineStyle.Italic).Should().BeTrue();
    }

    [Fact]
    public void CursorOutsideItalic_DoesNotDetectItalic()
    {
        var block = ParseSingle("before *italic* after");
        block.HasStyleAt(3, InlineStyle.Italic).Should().BeFalse();
    }

    // --- Strikethrough ---

    [Fact]
    public void CursorInsideStrikethrough_DetectsStrikethrough()
    {
        var block = ParseSingle("~~struck~~");
        block.HasStyleAt(4, InlineStyle.Strikethrough).Should().BeTrue();
    }

    [Fact]
    public void CursorOutsideStrikethrough_DoesNotDetectStrikethrough()
    {
        var block = ParseSingle("before ~~struck~~ after");
        block.HasStyleAt(3, InlineStyle.Strikethrough).Should().BeFalse();
    }

    // --- Code ---

    [Fact]
    public void CursorInsideCode_DetectsCode()
    {
        var block = ParseSingle("`code`");
        block.HasStyleAt(3, InlineStyle.Code).Should().BeTrue();
    }

    [Fact]
    public void CursorOutsideCode_DoesNotDetectCode()
    {
        var block = ParseSingle("before `code` after");
        block.HasStyleAt(3, InlineStyle.Code).Should().BeFalse();
    }

    // --- BoldItalic ---

    [Fact]
    public void CursorInsideBoldItalic_DetectsBold()
    {
        // "***both***" — cursor at offset 4 (inside "both" content)
        var block = ParseSingle("***both***");
        block.HasStyleAt(4, InlineStyle.Bold).Should().BeTrue();
    }

    [Fact]
    public void CursorInsideBoldItalic_DetectsItalic()
    {
        var block = ParseSingle("***both***");
        block.HasStyleAt(4, InlineStyle.Italic).Should().BeTrue();
    }

    [Fact]
    public void CursorInsideBoldItalic_DoesNotDetectStrikethrough()
    {
        var block = ParseSingle("***both***");
        block.HasStyleAt(4, InlineStyle.Strikethrough).Should().BeFalse();
    }

    // --- Edge cases ---

    [Fact]
    public void CursorAtZero_InPlainText_NoStyle()
    {
        var block = ParseSingle("plain text");
        block.HasStyleAt(0, InlineStyle.Bold).Should().BeFalse();
    }

    [Fact]
    public void CursorAtEndOfBlock_NoStyle()
    {
        var block = ParseSingle("**bold**");
        block.HasStyleAt(8, InlineStyle.Bold).Should().BeFalse();
    }

    [Fact]
    public void EmptyBlock_NoStyle()
    {
        var block = ParseSingle("");
        block.HasStyleAt(0, InlineStyle.Bold).Should().BeFalse();
    }

    // --- Delimiter exclusion ---

    [Fact]
    public void CursorOnOpeningBoldMarker_DoesNotDetectBold()
    {
        // "**bold**" — offset 0 and 1 are the opening ** delimiter
        var block = ParseSingle("**bold**");
        block.HasStyleAt(0, InlineStyle.Bold).Should().BeFalse();
        block.HasStyleAt(1, InlineStyle.Bold).Should().BeFalse();
    }

    [Fact]
    public void CursorOnClosingBoldMarker_DoesNotDetectBold()
    {
        // "**bold**" — offset 6 and 7 are the closing ** delimiter
        var block = ParseSingle("**bold**");
        block.HasStyleAt(6, InlineStyle.Bold).Should().BeFalse();
        block.HasStyleAt(7, InlineStyle.Bold).Should().BeFalse();
    }

    [Fact]
    public void CursorOnOpeningItalicMarker_DoesNotDetectItalic()
    {
        var block = ParseSingle("*italic*");
        block.HasStyleAt(0, InlineStyle.Italic).Should().BeFalse();
    }

    [Fact]
    public void CursorOnClosingItalicMarker_DoesNotDetectItalic()
    {
        var block = ParseSingle("*italic*");
        block.HasStyleAt(7, InlineStyle.Italic).Should().BeFalse();
    }

    [Fact]
    public void CursorOnOpeningStrikethroughMarker_DoesNotDetect()
    {
        var block = ParseSingle("~~struck~~");
        block.HasStyleAt(0, InlineStyle.Strikethrough).Should().BeFalse();
        block.HasStyleAt(1, InlineStyle.Strikethrough).Should().BeFalse();
    }

    [Fact]
    public void CursorOnClosingStrikethroughMarker_DoesNotDetect()
    {
        var block = ParseSingle("~~struck~~");
        block.HasStyleAt(8, InlineStyle.Strikethrough).Should().BeFalse();
        block.HasStyleAt(9, InlineStyle.Strikethrough).Should().BeFalse();
    }

    [Fact]
    public void CursorOnBoldItalicMarker_DoesNotDetect()
    {
        // "***both***" — offsets 0,1,2 are markers, 3-6 are content
        var block = ParseSingle("***both***");
        block.HasStyleAt(0, InlineStyle.Bold).Should().BeFalse();
        block.HasStyleAt(1, InlineStyle.Bold).Should().BeFalse();
        block.HasStyleAt(2, InlineStyle.Italic).Should().BeFalse();
    }
}
