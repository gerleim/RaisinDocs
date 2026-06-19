using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class BlockVisualMapTests
{
    private static BlockVisualMap ComputeMap(string text, BlockKind? kindOverride = null)
    {
        var blocks = MarkdownParser.Parse(_ => text, 1);
        var parsed = blocks[0];
        if (kindOverride.HasValue)
            parsed = new ParsedBlock { Kind = kindOverride.Value, Runs = parsed.Runs };
        return BlockVisualMap.Compute(parsed, text);
    }

    // --- IsHidden ---

    [Fact]
    public void PlainText_NothingHidden()
    {
        var map = ComputeMap("hello world");
        for (int i = 0; i < 11; i++)
            map.IsHidden(i).Should().BeFalse();
    }

    [Fact]
    public void Heading1_PrefixHidden()
    {
        var map = ComputeMap("# Hello");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
    }

    [Fact]
    public void Heading3_PrefixHidden()
    {
        var map = ComputeMap("### Hello");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeTrue();
        map.IsHidden(3).Should().BeTrue();
        map.IsHidden(4).Should().BeFalse();
    }

    [Fact]
    public void ListItem_PrefixHidden()
    {
        var map = ComputeMap("- Item");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
    }

    [Fact]
    public void Bold_MarkersHidden()
    {
        var map = ComputeMap("**bold**");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
        map.IsHidden(3).Should().BeFalse();
        map.IsHidden(4).Should().BeFalse();
        map.IsHidden(5).Should().BeFalse();
        map.IsHidden(6).Should().BeTrue();
        map.IsHidden(7).Should().BeTrue();
    }

    [Fact]
    public void Italic_MarkersHidden()
    {
        var map = ComputeMap("*italic*");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeFalse();
        map.IsHidden(7).Should().BeTrue();
    }

    [Fact]
    public void BoldItalic_MarkersHidden()
    {
        var map = ComputeMap("***bolditalic***");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeTrue();
        map.IsHidden(3).Should().BeFalse();
        map.IsHidden(12).Should().BeFalse();
        map.IsHidden(13).Should().BeTrue();
        map.IsHidden(14).Should().BeTrue();
        map.IsHidden(15).Should().BeTrue();
    }

    [Fact]
    public void CodeSpan_BackticksHidden()
    {
        var map = ComputeMap("`code`");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeFalse();
        map.IsHidden(4).Should().BeFalse();
        map.IsHidden(5).Should().BeTrue();
    }

    [Fact]
    public void DoubleBacktickCodeSpan_BackticksHidden()
    {
        var map = ComputeMap("``co`de``");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
        map.IsHidden(6).Should().BeFalse();
        map.IsHidden(7).Should().BeTrue();
        map.IsHidden(8).Should().BeTrue();
    }

    // --- RawToVisual ---

    [Fact]
    public void RawToVisual_NoHiddenRanges_Identity()
    {
        var map = ComputeMap("hello");
        map.RawToVisual(0).Should().Be(0);
        map.RawToVisual(3).Should().Be(3);
        map.RawToVisual(5).Should().Be(5);
    }

    [Fact]
    public void RawToVisual_HeadingPrefix()
    {
        var map = ComputeMap("# Hello");
        map.RawToVisual(0).Should().Be(0);
        map.RawToVisual(1).Should().Be(0);
        map.RawToVisual(2).Should().Be(0);
        map.RawToVisual(3).Should().Be(1);
        map.RawToVisual(7).Should().Be(5);
    }

    [Fact]
    public void RawToVisual_BoldMarkers()
    {
        // "text **bold** end"
        var map = ComputeMap("text **bold** end");
        map.RawToVisual(0).Should().Be(0);
        map.RawToVisual(4).Should().Be(4);
        map.RawToVisual(5).Should().Be(5);
        map.RawToVisual(6).Should().Be(5);
        map.RawToVisual(7).Should().Be(5);
        map.RawToVisual(11).Should().Be(9);
        map.RawToVisual(12).Should().Be(9);
        map.RawToVisual(13).Should().Be(9);
        map.RawToVisual(17).Should().Be(13);
    }

    [Fact]
    public void RawToVisual_ListWithReplacementPrefix()
    {
        var map = ComputeMap("- Item");
        map.ReplacementPrefix.Should().Be("  • ");
        map.RawToVisual(0).Should().Be(4);
        map.RawToVisual(1).Should().Be(4);
        map.RawToVisual(2).Should().Be(4);
        map.RawToVisual(3).Should().Be(5);
        map.RawToVisual(5).Should().Be(7);
    }

    // --- VisualToRaw ---

    [Fact]
    public void VisualToRaw_NoHiddenRanges_Identity()
    {
        var map = ComputeMap("hello");
        map.VisualToRaw(0).Should().Be(0);
        map.VisualToRaw(3).Should().Be(3);
        map.VisualToRaw(5).Should().Be(5);
    }

    [Fact]
    public void VisualToRaw_HeadingPrefix()
    {
        var map = ComputeMap("# Hello");
        map.VisualToRaw(0).Should().Be(2);
        map.VisualToRaw(1).Should().Be(3);
        map.VisualToRaw(5).Should().Be(7);
    }

    [Fact]
    public void VisualToRaw_BoldMarkers()
    {
        var map = ComputeMap("text **bold** end");
        map.VisualToRaw(0).Should().Be(0);
        map.VisualToRaw(4).Should().Be(4);
        map.VisualToRaw(5).Should().Be(7);
        map.VisualToRaw(9).Should().Be(13);
        map.VisualToRaw(13).Should().Be(17);
    }

    [Fact]
    public void VisualToRaw_ListWithReplacementPrefix()
    {
        var map = ComputeMap("- Item");
        map.VisualToRaw(4).Should().Be(2);
        map.VisualToRaw(5).Should().Be(3);
        map.VisualToRaw(7).Should().Be(5);
    }

    // --- RoundTrip ---

    [Fact]
    public void RoundTrip_HeadingWithBold()
    {
        var map = ComputeMap("# **bold** heading");
        string raw = "# **bold** heading";
        for (int v = 0; v <= 12; v++)
        {
            int r = map.VisualToRaw(v);
            r.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(raw.Length);
            map.RawToVisual(r).Should().Be(v, $"RoundTrip failed at visual={v}, raw={r}");
        }
    }

    // --- BuildDisplayString ---

    [Fact]
    public void BuildDisplayString_PlainText()
    {
        var map = ComputeMap("hello");
        map.BuildDisplayString("hello", 0, 5).Should().Be("hello");
    }

    [Fact]
    public void BuildDisplayString_HeadingHidesPrefix()
    {
        var map = ComputeMap("# Hello");
        map.BuildDisplayString("# Hello", 0, 7).Should().Be("Hello");
    }

    [Fact]
    public void BuildDisplayString_BoldHidesMarkers()
    {
        var map = ComputeMap("**bold**");
        map.BuildDisplayString("**bold**", 0, 8).Should().Be("bold");
    }

    [Fact]
    public void BuildDisplayString_MixedContent()
    {
        var map = ComputeMap("text **bold** end");
        map.BuildDisplayString("text **bold** end", 0, 17).Should().Be("text bold end");
    }

    [Fact]
    public void BuildDisplayString_Substring()
    {
        var map = ComputeMap("text **bold** end");
        map.BuildDisplayString("text **bold** end", 5, 8).Should().Be("bold");
    }

    // --- SkipHidden ---

    [Fact]
    public void SkipHidden_NotInHiddenRange_NoChange()
    {
        var map = ComputeMap("text **bold** end");
        map.SkipHidden(4, true).Should().Be(4);
        map.SkipHidden(8, false).Should().Be(8);
    }

    [Fact]
    public void SkipHidden_InsideOpeningMarker_SkipsForward()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(1, true).Should().Be(2);
    }

    [Fact]
    public void SkipHidden_InsideClosingMarker_SkipsBackward()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(7, false).Should().Be(6);
    }

    [Fact]
    public void SkipHidden_AtBoundary_NoChange()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(0, true).Should().Be(0);
        map.SkipHidden(2, true).Should().Be(2);
        map.SkipHidden(6, false).Should().Be(6);
        map.SkipHidden(8, false).Should().Be(8);
    }

    // --- Adjacent hidden ranges ---

    [Fact]
    public void HeadingWithBold_AdjacentRanges()
    {
        var map = ComputeMap("# **bold**");
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeTrue();
        map.IsHidden(3).Should().BeTrue();
        map.IsHidden(4).Should().BeFalse();
        map.IsHidden(7).Should().BeFalse();
        map.IsHidden(8).Should().BeTrue();
        map.IsHidden(9).Should().BeTrue();
        map.BuildDisplayString("# **bold**", 0, 10).Should().Be("bold");
    }

    // --- Multi-segment list items (hard breaks) ---

    [Fact]
    public void ListItem_HardBreak_AllPrefixesHidden()
    {
        var map = ComputeMap("- alpha\n- beta\n- gamma");
        // First prefix
        map.IsHidden(0).Should().BeTrue();
        map.IsHidden(1).Should().BeTrue();
        map.IsHidden(2).Should().BeFalse();
        // Second prefix (after \n at position 7)
        map.IsHidden(8).Should().BeTrue();
        map.IsHidden(9).Should().BeTrue();
        map.IsHidden(10).Should().BeFalse();
        // Third prefix (after \n at position 14)
        map.IsHidden(15).Should().BeTrue();
        map.IsHidden(16).Should().BeTrue();
        map.IsHidden(17).Should().BeFalse();
    }

    [Fact]
    public void ListItem_HardBreak_BuildDisplayString_PerSegment()
    {
        var map = ComputeMap("- alpha\n- beta\n- gamma");
        map.BuildDisplayString("- alpha\n- beta\n- gamma", 0, 7).Should().Be("alpha");
        map.BuildDisplayString("- alpha\n- beta\n- gamma", 8, 6).Should().Be("beta");
        map.BuildDisplayString("- alpha\n- beta\n- gamma", 15, 7).Should().Be("gamma");
    }

    [Fact]
    public void ListItem_HasListPrefixAt()
    {
        var text = "- alpha\n- beta\n- gamma";
        var map = ComputeMap(text);
        map.HasListPrefixAt(0, text).Should().BeTrue();
        map.HasListPrefixAt(8, text).Should().BeTrue();
        map.HasListPrefixAt(15, text).Should().BeTrue();
        map.HasListPrefixAt(2, text).Should().BeFalse();
    }

    [Fact]
    public void ListItem_ContinuationLine_NoPrefix()
    {
        var text = "- item\ncontinuation";
        var map = ComputeMap(text);
        map.HasListPrefixAt(0, text).Should().BeTrue();
        map.HasListPrefixAt(7, text).Should().BeFalse();
    }

    // --- IsFenceDelimiter ---

    [Fact]
    public void Parser_FenceDelimiter_MarkedCorrectly()
    {
        var blocks = MarkdownParser.Parse(i => new[] { "```", "code", "```" }[i], 3);
        blocks[0].IsFenceDelimiter.Should().BeTrue();
        blocks[1].IsFenceDelimiter.Should().BeFalse();
        blocks[2].IsFenceDelimiter.Should().BeTrue();
    }

    [Fact]
    public void Parser_NonFence_NotMarkedAsDelimiter()
    {
        var blocks = MarkdownParser.Parse(i => new[] { "hello", "**bold**" }[i], 2);
        blocks[0].IsFenceDelimiter.Should().BeFalse();
        blocks[1].IsFenceDelimiter.Should().BeFalse();
    }
}
