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
    public void SkipHidden_ForwardAtRangeStart_Skips()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(0, true).Should().Be(2);
        map.SkipHidden(6, true).Should().Be(8);
    }

    [Fact]
    public void SkipHidden_BackwardAtRangeStart_NoChange()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(6, false).Should().Be(6);
    }

    [Fact]
    public void SkipHidden_AtRangeEnd_NoChange()
    {
        var map = ComputeMap("**bold**");
        map.SkipHidden(2, true).Should().Be(2);
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

    // --- Images ---

    [Fact]
    public void Image_EntireSyntaxHidden()
    {
        var map = ComputeMap("![alt](image.png)");
        for (int i = 0; i < 17; i++)
            map.IsHidden(i).Should().BeTrue($"offset {i} should be hidden");
    }

    [Fact]
    public void Image_BuildDisplayString_ExcludesImageSyntax()
    {
        var map = ComputeMap("![alt](image.png)");
        map.BuildDisplayString("![alt](image.png)", 0, 17).Should().BeEmpty();
    }

    [Fact]
    public void Image_WithSurroundingText_OnlyImageHidden()
    {
        var map = ComputeMap("before ![img](x.png) after");
        for (int i = 0; i < 7; i++)
            map.IsHidden(i).Should().BeFalse($"offset {i} should be visible");
        for (int i = 7; i < 20; i++)
            map.IsHidden(i).Should().BeTrue($"offset {i} should be hidden");
        for (int i = 20; i < 26; i++)
            map.IsHidden(i).Should().BeFalse($"offset {i} should be visible");
    }

    [Fact]
    public void Image_BuildDisplayString_SurroundingTextPreserved()
    {
        var map = ComputeMap("before ![img](x.png) after");
        map.BuildDisplayString("before ![img](x.png) after", 0, 26).Should().Be("before  after");
    }

    [Fact]
    public void Image_RawToVisual_SkipsImageLength()
    {
        var map = ComputeMap("before ![img](x.png) after");
        map.RawToVisual(0).Should().Be(0);
        map.RawToVisual(7).Should().Be(7);
        map.RawToVisual(20).Should().Be(7);
        map.RawToVisual(21).Should().Be(8);
    }

    [Fact]
    public void Image_ImagesPropertyPassedThrough()
    {
        var map = ComputeMap("![alt](pic.png)");
        map.Images.Should().HaveCount(1);
        map.Images![0].Url.Should().Be("pic.png");
    }

    [Fact]
    public void Image_NoImages_PropertyIsNull()
    {
        var map = ComputeMap("just text");
        map.Images.Should().BeNull();
    }

    // --- Task list items ---

    [Fact]
    public void TaskListUnchecked_PrefixHidden()
    {
        var map = ComputeMap("- [ ] Task");
        for (int i = 0; i < 6; i++)
            map.IsHidden(i).Should().BeTrue($"offset {i} should be hidden");
        map.IsHidden(6).Should().BeFalse();
    }

    [Fact]
    public void TaskListChecked_PrefixHidden()
    {
        var map = ComputeMap("- [x] Task");
        for (int i = 0; i < 6; i++)
            map.IsHidden(i).Should().BeTrue($"offset {i} should be hidden");
        map.IsHidden(6).Should().BeFalse();
    }

    [Fact]
    public void TaskListUnchecked_ReplacementPrefix()
    {
        var map = ComputeMap("- [ ] Task");
        map.ReplacementPrefix.Should().Contain("☐");
    }

    [Fact]
    public void TaskListChecked_ReplacementPrefix()
    {
        var map = ComputeMap("- [x] Task");
        map.ReplacementPrefix.Should().Contain("☑");
    }

    [Fact]
    public void TaskList_RawToVisual()
    {
        var map = ComputeMap("- [ ] Task");
        int prefixLen = map.ReplacementPrefix!.Length;
        map.RawToVisual(6).Should().Be(prefixLen);
        map.RawToVisual(7).Should().Be(prefixLen + 1);
    }

    [Fact]
    public void TaskList_VisualToRaw()
    {
        var map = ComputeMap("- [ ] Task");
        int prefixLen = map.ReplacementPrefix!.Length;
        map.VisualToRaw(prefixLen).Should().Be(6);
        map.VisualToRaw(prefixLen + 1).Should().Be(7);
    }

    // --- Tables ---

    private static List<BlockVisualMap> ComputeTableMaps(params string[] blocks)
    {
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);
        return parsed.Select((p, i) => BlockVisualMap.Compute(p, blocks[i])).ToList();
    }

    [Fact]
    public void Table_PipesAndPaddingHidden()
    {
        var maps = ComputeTableMaps("| A | B |", "| --- | --- |", "| 1 | 2 |");
        var headerMap = maps[0];
        // "| A | B |" — pipes and padding spaces hidden, only cell content visible
        headerMap.IsHidden(0).Should().BeTrue();  // leading |
        headerMap.IsHidden(1).Should().BeTrue();  // padding space
        headerMap.IsHidden(2).Should().BeFalse(); // 'A'
        headerMap.IsHidden(3).Should().BeTrue();  // padding space
        headerMap.IsHidden(4).Should().BeTrue();  // middle |
        headerMap.IsHidden(5).Should().BeTrue();  // padding space
        headerMap.IsHidden(6).Should().BeFalse(); // 'B'
        headerMap.IsHidden(7).Should().BeTrue();  // padding space
        headerMap.IsHidden(8).Should().BeTrue();  // trailing |
    }

    [Fact]
    public void Table_DataRowPaddingHidden()
    {
        var maps = ComputeTableMaps("| A |", "| --- |", "| hello |");
        var dataMap = maps[2];
        // "| hello |" — pipes and padding hidden
        dataMap.IsHidden(0).Should().BeTrue();   // leading |
        dataMap.IsHidden(1).Should().BeTrue();   // padding space
        dataMap.IsHidden(2).Should().BeFalse();  // 'h'
        dataMap.IsHidden(6).Should().BeFalse();  // 'o'
        dataMap.IsHidden(7).Should().BeTrue();   // padding space
        dataMap.IsHidden(8).Should().BeTrue();   // trailing |
    }

    [Fact]
    public void Table_BuildDisplayString_HidesPipesAndPadding()
    {
        var maps = ComputeTableMaps("| A | B |", "| --- | --- |", "| 1 | 2 |");
        maps[0].BuildDisplayString("| A | B |", 0, 9).Should().Be("AB");
    }

    [Fact]
    public void Table_RawToVisual_AcrossCellBoundary()
    {
        var maps = ComputeTableMaps("| A | B |", "| --- | --- |", "| 1 | 2 |");
        var m = maps[0];
        // "| A | B |" — hidden: [0,2), [3,6), [7,9). Visible: 2='A', 6='B'
        m.RawToVisual(2).Should().Be(0);  // 'A' is first visible char
        m.RawToVisual(6).Should().Be(1);  // 'B' is second visible char
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

    [Fact]
    public void CodeBlock_WithTrailingSpaces_ContentNotMarkedAsDelimiter()
    {
        // Simulates loading a file where code lines have trailing spaces
        var doc = new Document();
        doc.SetText("```python  \ndef greet(name):  \n    print(f\"Hello\")\n\nasdasd\n```\n# Heading");

        doc.BlockCount.Should().Be(7);
        doc.GetBlockText(0).Should().Be("```python  ");
        doc.GetBlockText(1).Should().Be("def greet(name):  ");
        doc.GetBlockText(2).Should().Be("    print(f\"Hello\")");
        doc.GetBlockText(3).Should().Be("");
        doc.GetBlockText(4).Should().Be("asdasd");
        doc.GetBlockText(5).Should().Be("```");
        doc.GetBlockText(6).Should().Be("# Heading");

        var parsed = MarkdownParser.Parse(i => doc.GetBlockText(i), doc.BlockCount);
        parsed[0].IsFenceDelimiter.Should().BeTrue();
        parsed[1].IsFenceDelimiter.Should().BeFalse();
        parsed[1].Kind.Should().Be(BlockKind.FencedCodeLine);
        parsed[2].IsFenceDelimiter.Should().BeFalse();
        parsed[2].Kind.Should().Be(BlockKind.FencedCodeLine);
        parsed[3].IsFenceDelimiter.Should().BeFalse();
        parsed[4].IsFenceDelimiter.Should().BeFalse();
        parsed[4].Kind.Should().Be(BlockKind.FencedCodeLine);
        parsed[5].IsFenceDelimiter.Should().BeTrue();
        parsed[6].Kind.Should().Be(BlockKind.Heading1);
    }

    // --- Hard break marker hiding ---

    [Fact]
    public void TrailingBackslash_HiddenInVisual()
    {
        var map = ComputeMap("hello\\");
        map.IsHidden(5).Should().BeTrue();
        map.IsHidden(4).Should().BeFalse();
    }

    [Fact]
    public void TrailingTwoSpaces_HiddenInVisual()
    {
        var map = ComputeMap("hello  ");
        map.IsHidden(5).Should().BeTrue();
        map.IsHidden(6).Should().BeTrue();
        map.IsHidden(4).Should().BeFalse();
    }

    [Fact]
    public void SingleTrailingSpace_NotHidden()
    {
        var map = ComputeMap("hello ");
        map.IsHidden(5).Should().BeFalse();
    }

    [Fact]
    public void TrailingBackslash_InFencedCode_NotHidden()
    {
        var map = ComputeMap("path\\to\\file", BlockKind.FencedCodeLine);
        for (int i = 0; i < "path\\to\\file".Length; i++)
            map.IsHidden(i).Should().BeFalse();
    }

    [Fact]
    public void TrailingBackslash_InCodeSpan_NotHidden()
    {
        var map = ComputeMap("`code\\`");
        map.IsHidden(5).Should().BeFalse();
    }

    [Fact]
    public void EscapedBackslash_DoubleTrailing_NotHidden()
    {
        var map = ComputeMap("hello\\\\");
        map.IsHidden(5).Should().BeFalse();
        map.IsHidden(6).Should().BeFalse();
    }

    [Fact]
    public void TripleTrailingBackslash_LastOneHidden()
    {
        var map = ComputeMap("hi\\\\\\");
        map.IsHidden(4).Should().BeTrue();
    }

    // --- Links ---

    [Fact]
    public void Link_BracketAndUrlHidden_TextVisible()
    {
        // [text](url) — positions: [=0, text=1-4, ]=5, (=6, url=7-9, )=10
        var map = ComputeMap("[text](url)");
        map.IsHidden(0).Should().BeTrue();   // [
        map.IsHidden(1).Should().BeFalse();  // t
        map.IsHidden(2).Should().BeFalse();  // e
        map.IsHidden(3).Should().BeFalse();  // x
        map.IsHidden(4).Should().BeFalse();  // t
        map.IsHidden(5).Should().BeTrue();   // ]
        map.IsHidden(6).Should().BeTrue();   // (
        map.IsHidden(7).Should().BeTrue();   // u
        map.IsHidden(8).Should().BeTrue();   // r
        map.IsHidden(9).Should().BeTrue();   // l
        map.IsHidden(10).Should().BeTrue();  // )
    }

    [Fact]
    public void Link_WithTitle_AllSyntaxHidden()
    {
        // [text](url "title") — ](url "title") all hidden
        var map = ComputeMap("[text](url \"title\")");
        map.IsHidden(0).Should().BeTrue();   // [
        map.IsHidden(1).Should().BeFalse();  // t
        map.IsHidden(4).Should().BeFalse();  // t
        map.IsHidden(5).Should().BeTrue();   // ]
        map.IsHidden(18).Should().BeTrue();  // )
    }

    [Fact]
    public void Link_DisplayString_ShowsOnlyText()
    {
        var map = ComputeMap("[click here](https://example.com)");
        var display = map.BuildDisplayString("[click here](https://example.com)", 0, 33);
        display.Should().Be("click here");
    }

    [Fact]
    public void Link_RawToVisual()
    {
        // [text](url) — text starts at raw 1, visual 0
        var map = ComputeMap("[text](url)");
        map.RawToVisual(0).Should().Be(0);   // before [ → visual 0
        map.RawToVisual(1).Should().Be(0);   // t → visual 0
        map.RawToVisual(5).Should().Be(4);   // ] → visual 4 (end of text)
    }

    [Fact]
    public void Link_VisualToRaw()
    {
        // [text](url) — visual 0 = raw 1 (t), visual 3 = raw 4 (t)
        var map = ComputeMap("[text](url)");
        map.VisualToRaw(0).Should().Be(1);   // visual start → raw 1 (past [)
        map.VisualToRaw(4).Should().Be(11);  // past visual text → raw 11 (past ))
    }

    [Fact]
    public void Link_MultipleLinks_BothHidden()
    {
        var map = ComputeMap("[a](u1) [b](u2)");
        // First link: [=0, a=1, ]=2...(u1)=3-6
        map.IsHidden(0).Should().BeTrue();   // [
        map.IsHidden(1).Should().BeFalse();  // a
        map.IsHidden(2).Should().BeTrue();   // ]
        // Second link: [=8, b=9, ]=10...(u2)=11-14
        map.IsHidden(8).Should().BeTrue();   // [
        map.IsHidden(9).Should().BeFalse();  // b
        map.IsHidden(10).Should().BeTrue();  // ]
    }

    [Fact]
    public void Link_LinksPropertyPopulated()
    {
        var map = ComputeMap("[text](url)");
        map.Links.Should().HaveCount(1);
        map.Links![0].Text.Should().Be("text");
        map.Links![0].Url.Should().Be("url");
    }

    // --- Autolinks ---

    [Fact]
    public void Autolink_NoHiddenRanges()
    {
        var map = ComputeMap("see https://example.com end");
        for (int i = 4; i < 23; i++)
            map.IsHidden(i).Should().BeFalse($"offset {i} should be visible");
    }

    [Fact]
    public void Autolink_DisplayStringUnchanged()
    {
        string text = "see https://example.com end";
        var map = ComputeMap(text);
        map.BuildDisplayString(text, 0, text.Length).Should().Be(text);
    }

    [Fact]
    public void Autolink_RawToVisual_Identity()
    {
        var map = ComputeMap("see https://example.com end");
        map.RawToVisual(4).Should().Be(4);
        map.RawToVisual(23).Should().Be(23);
    }

    // --- Reference Links ---

    private static BlockVisualMap ComputeMapMultiBlock(string[] blocks, int blockIndex)
    {
        var parsed = MarkdownParser.Parse(i => blocks[i], blocks.Length);
        return BlockVisualMap.Compute(parsed[blockIndex], blocks[blockIndex]);
    }

    [Fact]
    public void RefLink_HiddenRanges_MatchInlinePattern()
    {
        // [text][ref] should hide [ and ][ref] — same as [text](url) hides [ and ](url)
        var map = ComputeMapMultiBlock(["[text][ref]", "[ref]: https://example.com"], 0);
        map.IsHidden(0).Should().BeTrue();   // [
        map.IsHidden(1).Should().BeFalse();  // t
        map.IsHidden(4).Should().BeFalse();  // t
        map.IsHidden(5).Should().BeTrue();   // ]
        map.IsHidden(6).Should().BeTrue();   // [
        map.IsHidden(10).Should().BeTrue();  // ]
    }

    [Fact]
    public void RefLink_DisplayString()
    {
        var map = ComputeMapMultiBlock(["[text][ref]", "[ref]: https://example.com"], 0);
        map.BuildDisplayString("[text][ref]", 0, 11).Should().Be("text");
    }

    [Fact]
    public void RefLink_Collapsed_HiddenRanges()
    {
        var map = ComputeMapMultiBlock(["[docs][]", "[docs]: https://example.com"], 0);
        map.IsHidden(0).Should().BeTrue();   // [
        map.IsHidden(1).Should().BeFalse();  // d
        map.IsHidden(4).Should().BeFalse();  // s
        map.IsHidden(5).Should().BeTrue();   // ]
        map.IsHidden(6).Should().BeTrue();   // [
        map.IsHidden(7).Should().BeTrue();   // ]
    }

    // --- Inline color tags ---

    [Fact]
    public void InlineColorTag_Hidden()
    {
        // "hello <!--@fg:red-->world<!--/@fg--> end"
        var text = "hello <!--@fg:red-->world<!--/@fg--> end";
        var map = ComputeMap(text);
        // "<!--@fg:red-->" at 6..19 hidden
        for (int i = 6; i < 20; i++)
            map.IsHidden(i).Should().BeTrue($"index {i} should be hidden (opener tag)");
        // "world" at 20..24 visible
        for (int i = 20; i < 25; i++)
            map.IsHidden(i).Should().BeFalse($"index {i} should be visible (content)");
        // "<!--/@fg-->" at 25..35 hidden
        for (int i = 25; i < 36; i++)
            map.IsHidden(i).Should().BeTrue($"index {i} should be hidden (closer tag)");
    }

    [Fact]
    public void InlineColorTag_DisplayString()
    {
        var text = "hello <!--@fg:red-->world<!--/@fg--> end";
        var map = ComputeMap(text);
        map.BuildDisplayString(text, 0, text.Length).Should().Be("hello world end");
    }

    [Fact]
    public void InlineColorTag_RawToVisual()
    {
        var text = "A<!--@fg:red-->B<!--/@fg-->C";
        var map = ComputeMap(text);
        // A=0, tag=1..14 (hidden), B=15 -> vis 1, tag=16..25 (hidden), C=26 -> vis 2
        map.RawToVisual(0).Should().Be(0);   // A
        map.RawToVisual(15).Should().Be(1);  // B
        map.RawToVisual(26).Should().Be(2);  // C
    }

    [Fact]
    public void InlineColorTag_ColorSpansPassedThrough()
    {
        var text = "<!--@fg:red-->colored<!--/@fg-->";
        var map = ComputeMap(text);
        map.ColorSpans.Should().NotBeNull();
        map.ColorSpans!.Count.Should().Be(1);
        map.ColorSpans[0].Foreground.Should().Be(new RgbColor(255, 0, 0));
    }

    [Fact]
    public void InlineColorTag_VisualSpanCoversCorrectRange()
    {
        var text = "asdasdasd <!--@fg:orange-->dgfhfzgjhfg fghfgh<!--/@fg-->";
        var map = ComputeMap(text);
        var display = map.BuildDisplayString(text, 0, text.Length);
        display.Should().Be("asdasdasd dgfhfzgjhfg fghfgh");

        map.ColorSpans.Should().NotBeNull();
        var cs = map.ColorSpans![0];
        cs.Foreground.Should().Be(new RgbColor(255, 165, 0));

        int visStart = map.RawToVisual(cs.Start);
        int visEnd = map.RawToVisual(cs.Start + cs.Length);
        visStart.Should().Be(10);
        visEnd.Should().Be(28);
        display[visStart..visEnd].Should().Be("dgfhfzgjhfg fghfgh");
    }
}
