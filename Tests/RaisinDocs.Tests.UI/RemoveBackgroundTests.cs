using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class RemoveBackgroundTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    [StaFact]
    public void SelectionHasBackground_ReturnsFalse_WhenNoSelection()
    {
        var canvas = CreateCanvas("hello world");
        canvas.SelectionHasBackground().Should().BeFalse();
    }

    [StaFact]
    public void SelectionHasBackground_ReturnsFalse_WhenNoBgTags()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestSetSelection(0, 0, 0, 5);
        canvas.SelectionHasBackground().Should().BeFalse();
    }

    [StaFact]
    public void SelectionHasBackground_ReturnsTrue_WhenInlineBgInSelection()
    {
        var canvas = CreateCanvas("hello <!--@bg:red-->world<!--/@bg--> end");
        canvas.TestSetSelection(0, 0, 0, 35);
        canvas.SelectionHasBackground().Should().BeTrue();
    }

    [StaFact]
    public void SelectionHasBackground_ReturnsTrue_WhenCombinedFgBgInSelection()
    {
        var canvas = CreateCanvas("hello <!--@fg:blue bg:red-->world<!--/@--> end");
        canvas.TestSetSelection(0, 0, 0, 42);
        canvas.SelectionHasBackground().Should().BeTrue();
    }

    [StaFact]
    public void RemoveBackground_RemovesBgOnlyTags()
    {
        var canvas = CreateCanvas("hello <!--@bg:red-->world<!--/@bg--> end");
        canvas.TestSetSelection(0, 0, 0, 36);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("hello world end");
    }

    [StaFact]
    public void RemoveBackground_StripsBgFromCombined_FgFirst()
    {
        var canvas = CreateCanvas("<!--@fg:blue bg:red-->text<!--/@-->");
        canvas.TestSetSelection(0, 0, 0, 34);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue-->text<!--/@-->");
    }

    [StaFact]
    public void RemoveBackground_StripsBgFromCombined_BgFirst()
    {
        var canvas = CreateCanvas("<!--@bg:red fg:blue-->text<!--/@-->");
        canvas.TestSetSelection(0, 0, 0, 34);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue-->text<!--/@-->");
    }

    [StaFact]
    public void RemoveBackground_LeavesUnrelatedFgTags()
    {
        var canvas = CreateCanvas("<!--@fg:blue-->text<!--/@fg-->");
        canvas.TestSetSelection(0, 0, 0, 29);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue-->text<!--/@fg-->");
    }

    [StaFact]
    public void RemoveBackground_HandlesMultipleBgTags()
    {
        var canvas = CreateCanvas("<!--@bg:red-->a<!--/@bg--> <!--@bg:blue-->b<!--/@bg-->");
        canvas.TestSetSelection(0, 0, 0, 53);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("a b");
    }

    [StaFact]
    public void SelectionHasBackground_ReturnsTrue_WithBlockColor()
    {
        var canvas = CreateCanvas("<!--@div bg:red-->\nhello world\n<!--/@div-->");
        canvas.TestSetSelection(1, 0, 1, 11);
        canvas.SelectionHasBackground().Should().BeTrue();
    }

    // --- Cursor (no selection) tests ---

    [StaFact]
    public void CursorHasBackground_ReturnsFalse_WhenNoBg()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestSetCursor(0, 3);
        canvas.CursorHasBackground().Should().BeFalse();
    }

    [StaFact]
    public void CursorHasBackground_ReturnsTrue_WhenInsideBgSpan()
    {
        // <!--@bg:red--> is 14 chars (0-13), "world" starts at 20
        var canvas = CreateCanvas("hello <!--@bg:red-->world<!--/@bg--> end");
        canvas.TestSetCursor(0, 22);
        canvas.CursorHasBackground().Should().BeTrue();
    }

    [StaFact]
    public void CursorHasBackground_ReturnsFalse_WhenOutsideBgSpan()
    {
        var canvas = CreateCanvas("hello <!--@bg:red-->world<!--/@bg--> end");
        canvas.TestSetCursor(0, 3);
        canvas.CursorHasBackground().Should().BeFalse();
    }

    [StaFact]
    public void RemoveBackgroundAtCursor_RemovesBgOnlyTags()
    {
        // cursor at 22 = 'r' of "world", inside bg span content [20,25)
        var canvas = CreateCanvas("hello <!--@bg:red-->world<!--/@bg--> end");
        canvas.TestSetCursor(0, 22);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("hello world end");
    }

    [StaFact]
    public void RemoveBackgroundAtCursor_StripsBgFromCombined()
    {
        // <!--@fg:blue bg:red--> is 22 chars, "text" starts at 22
        var canvas = CreateCanvas("<!--@fg:blue bg:red-->text<!--/@-->");
        canvas.TestSetCursor(0, 23);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue-->text<!--/@-->");
    }

    [StaFact]
    public void RemoveBackgroundAtCursor_LeavesOtherBgSpans()
    {
        // First span: <!--@bg:red--> (14) a (1) <!--/@bg--> (11) = 26, space at 26
        // Second span: <!--@bg:blue--> starts at 27 (15 chars), 'b' at 42
        var canvas = CreateCanvas("<!--@bg:red-->a<!--/@bg--> <!--@bg:blue-->b<!--/@bg-->");
        canvas.TestSetCursor(0, 42);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:red-->a<!--/@bg--> b");
    }

    [StaFact]
    public void RemoveBackgroundAtCursor_DoesNothing_WhenNoBg()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestSetCursor(0, 3);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("hello world");
    }

    // --- Div background tests ---

    [StaFact]
    public void RemoveBackgroundAtCursor_RemovesBgOnlyDiv()
    {
        var canvas = CreateCanvas("<!--@div bg:green-->\nhello world\n<!--/@div-->");
        canvas.TestSetCursor(1, 3);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("hello world");
    }

    [StaFact]
    public void RemoveBackgroundFromSelection_RemovesBgOnlyDiv()
    {
        var canvas = CreateCanvas("<!--@div bg:green-->\nhello world\n<!--/@div-->");
        canvas.TestSetSelection(1, 0, 1, 11);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("hello world");
    }

    [StaFact]
    public void RemoveBackgroundAtCursor_StripsBgFromCombinedDiv()
    {
        var canvas = CreateCanvas("<!--@div fg:red bg:green-->\nhello world\n<!--/@div-->");
        canvas.TestSetCursor(1, 3);
        canvas.RemoveBackgroundAtCursor();
        canvas.TestGetBlockText(0).Should().Be("<!--@div fg:red-->");
        canvas.TestGetBlockText(1).Should().Be("hello world");
    }

    [StaFact]
    public void CursorHasBackground_ReturnsTrue_InsideDiv()
    {
        var canvas = CreateCanvas("<!--@div bg:green-->\nhello world\n<!--/@div-->");
        canvas.TestSetCursor(1, 3);
        canvas.CursorHasBackground().Should().BeTrue();
    }

    // --- Partial selection tests ---

    [StaFact]
    public void RemoveBackground_PartialRight_KeepsLeftPortion()
    {
        // "<!--@bg:red-->" [0,14), content "hello world" [14,25), "<!--/@bg-->" [25,36)
        // Select "world" [20,25) → right-partial: bg stays on "hello "
        var canvas = CreateCanvas("<!--@bg:red-->hello world<!--/@bg-->");
        canvas.TestSetSelection(0, 20, 0, 25);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:red-->hello <!--/@bg-->world");
    }

    [StaFact]
    public void RemoveBackground_PartialLeft_KeepsRightPortion()
    {
        // Select "hello" [14,19) → left-partial: bg stays on " world"
        var canvas = CreateCanvas("<!--@bg:red-->hello world<!--/@bg-->");
        canvas.TestSetSelection(0, 14, 0, 19);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("hello<!--@bg:red--> world<!--/@bg-->");
    }

    [StaFact]
    public void RemoveBackground_PartialMiddle_KeepsBothSides()
    {
        // content "hello world foo" [14,29), "<!--/@bg-->" [29,40)
        // Select "world" [20,25) → middle: bg stays on "hello " and " foo"
        var canvas = CreateCanvas("<!--@bg:red-->hello world foo<!--/@bg-->");
        canvas.TestSetSelection(0, 20, 0, 25);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:red-->hello <!--/@bg-->world<!--@bg:red--> foo<!--/@bg-->");
    }

    [StaFact]
    public void RemoveBackground_PartialLeftCombined_StripsBgFromOpenerReopensBgAfter()
    {
        // "<!--@fg:blue bg:red-->" [0,22), "text" [22,26), "<!--/@-->" [26,35)
        // Select "te" [22,24) → left-partial combined: strip bg from opener, reopen bg after selection
        var canvas = CreateCanvas("<!--@fg:blue bg:red-->text<!--/@-->");
        canvas.TestSetSelection(0, 22, 0, 24);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue-->te<!--@bg:red-->xt<!--/@-->");
    }

    [StaFact]
    public void RemoveBackground_PartialMiddleCombined_ClosesAndReopensBgLayer()
    {
        // "<!--@fg:#F8F8F2 bg:#FFE800-->" [0,29), "aaa bbb ccc " [29,41), "<!--/@-->" [41,50)
        // Select "bbb" [33,36) → middle combined: close bg before, reopen bg after
        var canvas = CreateCanvas("<!--@fg:#F8F8F2 bg:#FFE800-->aaa bbb ccc <!--/@-->");
        canvas.TestSetSelection(0, 33, 0, 36);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:#F8F8F2 bg:#FFE800-->aaa <!--/@bg-->bbb<!--@bg:#FFE800--> ccc <!--/@-->");
    }

    [StaFact]
    public void RemoveBackground_PartialRightCombined_ClosesBgBeforeSelection()
    {
        // "<!--@fg:blue bg:red-->" [0,22), "hello world" [22,33), "<!--/@-->" [33,42)
        // Select "world" [28,33) → right-partial combined: close bg before selection
        var canvas = CreateCanvas("<!--@fg:blue bg:red-->hello world<!--/@-->");
        canvas.TestSetSelection(0, 28, 0, 33);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@fg:blue bg:red-->hello <!--/@bg-->world<!--/@-->");
    }

    [StaFact]
    public void RemoveBackground_PartialMiddle_BgOnlyWithTrailingSpace()
    {
        // Opener "<!--@bg:#F8F8F2 -->" has trailing space before -->
        // [0,19) opener, "aaa bbb ccc" [19,30), "<!--/@bg-->" [30,41)
        // Select "bbb" [23,26) → middle bg-only split
        var canvas = CreateCanvas("<!--@bg:#F8F8F2 -->aaa bbb ccc<!--/@bg-->");
        canvas.TestSetSelection(0, 23, 0, 26);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:#F8F8F2 -->aaa <!--/@bg-->bbb<!--@bg:#F8F8F2 --> ccc<!--/@bg-->");
    }

    // --- Partial selection within div tests ---

    [StaFact]
    public void RemoveBackground_PartialInBgOnlyDiv_ConvertsThenSplits()
    {
        // block0: <!--@div bg:green-->  block1: aaa bbb ccc  block2: <!--/@div-->
        // Select "bbb" on block1 [4,7) → removes div, wraps line, splits at selection
        var canvas = CreateCanvas("<!--@div bg:green-->\naaa bbb ccc\n<!--/@div-->");
        canvas.TestSetSelection(1, 4, 1, 7);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:green-->aaa <!--/@bg-->bbb<!--@bg:green--> ccc<!--/@bg-->");
    }

    [StaFact]
    public void RemoveBackground_PartialInCombinedDiv_KeepsDivFgConvertsBg()
    {
        // block0: <!--@div fg:red bg:green-->  block1: aaa bbb ccc  block2: <!--/@div-->
        // Select "bbb" on block1 → keeps div with fg:red, converts bg to inline and splits
        var canvas = CreateCanvas("<!--@div fg:red bg:green-->\naaa bbb ccc\n<!--/@div-->");
        canvas.TestSetSelection(1, 4, 1, 7);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@div fg:red-->");
        canvas.TestGetBlockText(1).Should().Be("<!--@bg:green-->aaa <!--/@bg-->bbb<!--@bg:green--> ccc<!--/@bg-->");
        canvas.TestGetBlockText(2).Should().Be("<!--/@div-->");
    }

    [StaFact]
    public void RemoveBackground_PartialInMultiLineDiv_SplitsDivAroundSelection()
    {
        // 3 content lines, select "bbb" on line 2 → div splits: lines 1,3 stay in divs, line 2 gets inline
        var canvas = CreateCanvas("<!--@div bg:green-->\nline one\naaa bbb ccc\nline three\n<!--/@div-->");
        canvas.TestSetSelection(2, 4, 2, 7);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@div bg:green-->");
        canvas.TestGetBlockText(1).Should().Be("line one");
        canvas.TestGetBlockText(2).Should().Be("<!--/@div-->");
        canvas.TestGetBlockText(3).Should().Be("<!--@bg:green-->aaa <!--/@bg-->bbb<!--@bg:green--> ccc<!--/@bg-->");
        canvas.TestGetBlockText(4).Should().Be("<!--@div bg:green-->");
        canvas.TestGetBlockText(5).Should().Be("line three");
        canvas.TestGetBlockText(6).Should().Be("<!--/@div-->");
    }

    [StaFact]
    public void RemoveBackground_FullSelectionInDiv_RemovesBgEntirely()
    {
        // Full selection of content → bg cleared, div removed
        var canvas = CreateCanvas("<!--@div bg:green-->\nhello world\n<!--/@div-->");
        canvas.TestSetSelection(1, 0, 1, 11);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("hello world");
    }

    // --- Edge case: selection spanning div boundary ---

    [StaFact]
    public void RemoveBackground_SelectionSpansDivAndInline_ClearsBoth()
    {
        // block0: <!--@div bg:green-->  block1: aaa  block2: <!--/@div-->
        // block3: <!--@bg:red-->bbb<!--/@bg-->
        // Select all of block1 and block3 → both bg cleared
        var canvas = CreateCanvas("<!--@div bg:green-->\naaa\n<!--/@div-->\n<!--@bg:red-->bbb<!--/@bg-->");
        canvas.TestSetSelection(1, 0, 3, 28);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("aaa");
        canvas.TestGetBlockText(1).Should().Be("bbb");
    }

    [StaFact]
    public void RemoveBackground_PartialSpanningDivBoundary_SplitsCorrectly()
    {
        // block0: <!--@div bg:green-->  block1: aaa bbb  block2: <!--/@div-->
        // block3: ccc ddd (no bg)
        // Select "bbb" from block1 through "ccc" on block3
        var canvas = CreateCanvas("<!--@div bg:green-->\naaa bbb\n<!--/@div-->\nccc ddd");
        canvas.TestSetSelection(1, 4, 3, 3);
        canvas.RemoveBackgroundFromSelection();
        // "aaa " keeps green bg, "bbb" cleared. "ccc ddd" had no bg.
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:green-->aaa <!--/@bg-->bbb");
        canvas.TestGetBlockText(1).Should().Be("ccc ddd");
    }

    // --- Edge case: nested bg spans ---

    [StaFact]
    public void RemoveBackground_NestedBgSpans_ClearsAllLayersFromSelection()
    {
        // <!--@bg:red-->aaa <!--@bg:blue-->bbb<!--/@bg--> ccc<!--/@bg-->
        // Select "bbb" [33,36) → clear both red and blue from "bbb"
        // Red stays on "aaa " and " ccc"
        var canvas = CreateCanvas("<!--@bg:red-->aaa <!--@bg:blue-->bbb<!--/@bg--> ccc<!--/@bg-->");
        canvas.TestSetSelection(0, 33, 0, 36);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:red-->aaa <!--/@bg-->bbb<!--@bg:red--> ccc<!--/@bg-->");
    }

    [StaFact]
    public void RemoveBackground_NestedBgSpans_PartialInner_ClearsAllLayers()
    {
        // <!--@bg:red-->aaa <!--@bg:blue-->bbb ccc<!--/@bg-->ddd<!--/@bg-->
        // Select "bbb" [33,36) — left-partial of blue, middle of red
        var canvas = CreateCanvas("<!--@bg:red-->aaa <!--@bg:blue-->bbb ccc<!--/@bg-->ddd<!--/@bg-->");
        canvas.TestSetSelection(0, 33, 0, 36);
        canvas.RemoveBackgroundFromSelection();
        // "bbb" cleared of both layers; blue reopens for " ccc", red reopens for " ccc" and "ddd"
        canvas.TestGetBlockText(0).Should()
            .Be("<!--@bg:red-->aaa <!--/@bg-->bbb<!--@bg:red--><!--@bg:blue--> ccc<!--/@bg-->ddd<!--/@bg-->");
    }

    // --- Edge case: mixed div + inline in selection ---

    [StaFact]
    public void RemoveBackground_MixedDivAndInline_ClearsBoth()
    {
        // block0: <!--@div bg:green-->  block1: aaa  block2: <!--/@div-->
        // block3: <!--@bg:red-->bbb<!--/@bg-->
        // Full selection of both
        var canvas = CreateCanvas("<!--@div bg:green-->\naaa\n<!--/@div-->\n<!--@bg:red-->bbb<!--/@bg-->");
        canvas.TestSetSelection(1, 0, 3, 28);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("aaa");
        canvas.TestGetBlockText(1).Should().Be("bbb");
    }

    [StaFact]
    public void RemoveBackground_TwoDivsInSelection_ClearsBoth()
    {
        // Two separate bg divs, select all content across both
        var canvas = CreateCanvas("<!--@div bg:red-->\naaa\n<!--/@div-->\ntext\n<!--@div bg:blue-->\nbbb\n<!--/@div-->");
        canvas.TestSetSelection(1, 0, 5, 3);
        canvas.RemoveBackgroundFromSelection();
        canvas.TestGetBlockText(0).Should().Be("aaa");
        canvas.TestGetBlockText(1).Should().Be("text");
        canvas.TestGetBlockText(2).Should().Be("bbb");
    }

    [StaFact]
    public void RemoveBackground_DivPartialAndInlinePartial_SplitsCorrectly()
    {
        // block0: <!--@div bg:green-->  block1: aaa bbb  block2: <!--/@div-->
        // block3: <!--@bg:red-->ccc ddd<!--/@bg-->
        // Select "bbb" on block1 through "ccc" on block3
        var canvas = CreateCanvas("<!--@div bg:green-->\naaa bbb\n<!--/@div-->\n<!--@bg:red-->ccc ddd<!--/@bg-->");
        // "ccc" is at offset [14, 17) in block3's raw text (after <!--@bg:red-->)
        canvas.TestSetSelection(1, 4, 3, 17);
        canvas.RemoveBackgroundFromSelection();
        // "aaa " keeps green, "bbb" cleared, "ccc" cleared, " ddd" keeps red
        canvas.TestGetBlockText(0).Should().Be("<!--@bg:green-->aaa <!--/@bg-->bbb");
        canvas.TestGetBlockText(1).Should().Be("ccc<!--@bg:red--> ddd<!--/@bg-->");
    }
}
