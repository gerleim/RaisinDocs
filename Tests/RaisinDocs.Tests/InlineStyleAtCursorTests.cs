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
        // "***both***" — BoldItalic run should report true for Bold
        var block = ParseSingle("***both***");
        var boldItalicRun = block.Runs.First(r => r.Style == InlineStyle.BoldItalic);
        block.HasStyleAt(boldItalicRun.Start, InlineStyle.Bold).Should().BeTrue();
    }

    [Fact]
    public void CursorInsideBoldItalic_DetectsItalic()
    {
        var block = ParseSingle("***both***");
        var boldItalicRun = block.Runs.First(r => r.Style == InlineStyle.BoldItalic);
        block.HasStyleAt(boldItalicRun.Start, InlineStyle.Italic).Should().BeTrue();
    }

    [Fact]
    public void CursorInsideBoldItalic_DoesNotDetectStrikethrough()
    {
        var block = ParseSingle("***both***");
        var boldItalicRun = block.Runs.First(r => r.Style == InlineStyle.BoldItalic);
        block.HasStyleAt(boldItalicRun.Start, InlineStyle.Strikethrough).Should().BeFalse();
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
}
