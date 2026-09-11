using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// The selection highlight has to land on the same pixels the caret does. Clicking in a list
/// item put the caret exactly between two glyphs, but the highlight was measured from the left
/// margin plus the width of the marker's replacement text - which is not the marker column the
/// text is laid out on, and is not applied at all to a wrapped line - so the box sat beside the
/// words it was meant to cover. Every highlight now measures through the same routine the caret
/// does, and these tests read the rectangle the renderer actually paints.
/// </summary>
public class SelectionHighlightAlignmentTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas MakeCanvas(string text,
        DocsCanvas.EditMode mode = DocsCanvas.EditMode.Visual)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(mode);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    /// <summary>The visual line a block's <paramref name="nth"/> line occupies.</summary>
    private static int LineOf(DocsCanvas canvas, int block, int nth = 0)
    {
        int seen = 0;
        for (int i = 0; i < canvas.TestVisualLineCount; i++)
        {
            if (canvas.TestGetVisualLineBlockIndex(i) != block) continue;
            if (seen++ == nth) return i;
        }
        throw new Xunit.Sdk.XunitException($"block {block} has no line {nth}");
    }

    /// <summary>
    /// Selects <paramref name="from"/>..<paramref name="to"/> in one block and returns the
    /// rectangle the renderer paints for it on <paramref name="vi"/>, with the caret X at each
    /// end of the selection for comparison.
    /// </summary>
    private static (Rect Rect, double CaretAtFrom, double CaretAtTo) SelectAndDraw(
        DocsCanvas canvas, int vi, int block, int from, int to)
    {
        canvas.TestSetCursor(block, from);
        double caretAtFrom = canvas.TestCursorX;
        canvas.TestSetCursor(block, to);
        double caretAtTo = canvas.TestCursorX;

        canvas.TestSetSelection(block, from, block, to);
        canvas.TestComputeLayout();

        var rect = canvas.TestSelectionRect(vi);
        rect.Should().NotBeNull("the line carries part of the selection");
        return (rect!.Value, caretAtFrom, caretAtTo);
    }

    // --- The bug, block kind by block kind ---

    [StaTheory]
    [InlineData("- one two three four", "unordered list item")]
    [InlineData("1. one two three four", "ordered list item")]
    [InlineData("- [ ] one two three four", "unchecked task item")]
    [InlineData("- [x] one two three four", "checked task item")]
    [InlineData("> one two three four", "blockquote")]
    [InlineData("# one two three four", "heading")]
    [InlineData("one two three four", "paragraph")]
    public void SelectionStartingAtALine_BeginsOnTheLinesTextColumn(string markdown, string kind)
    {
        var canvas = MakeCanvas(markdown);
        int vi = LineOf(canvas, 0);
        int end = canvas.TestGetBlockText(0).Length;

        var (rect, caretAtFrom, _) = SelectAndDraw(canvas, vi, 0, 0, end);

        rect.X.Should().BeApproximately(canvas.TestGetVisualLineContentStartX(vi), 0.001,
            $"a {kind}'s highlight starts where its text starts");
        rect.X.Should().BeApproximately(caretAtFrom, 0.001,
            $"a {kind}'s highlight starts where the caret would sit");
    }

    [StaTheory]
    [InlineData("- one two three four")]
    [InlineData("1. one two three four")]
    [InlineData("- [ ] one two three four")]
    [InlineData("> one two three four")]
    [InlineData("one two three four")]
    public void SelectionEndingMidLine_EndsUnderTheCaret(string markdown)
    {
        var canvas = MakeCanvas(markdown);
        int vi = LineOf(canvas, 0);
        string text = canvas.TestGetBlockText(0);
        int mid = text.Length - 5; // "one two three| four"

        var (rect, _, caretAtTo) = SelectAndDraw(canvas, vi, 0, 0, mid);

        rect.Right.Should().BeApproximately(caretAtTo, 0.001);
    }

    [StaFact]
    public void OnAWrappedListLine_TheHighlightHangsOnTheSameColumnAsTheText()
    {
        // Long enough to wrap at 800px, so the second line is a continuation that draws no
        // marker of its own but still hangs on the item's text column.
        string item = "- " + string.Join(' ', Enumerable.Repeat("wrapping", 40));
        var canvas = MakeCanvas(item);

        int second = LineOf(canvas, 0, 1);
        canvas.TestVisualLineCount.Should().BeGreaterThan(1, "the item has to wrap for this test");

        int lineStart = canvas.TestVisualLines[second].StartOffset;
        var (rect, caretAtFrom, _) = SelectAndDraw(canvas, second, 0,
            lineStart, lineStart + 8);

        rect.X.Should().BeApproximately(canvas.TestGetVisualLineContentStartX(second), 0.001);
        rect.X.Should().BeApproximately(caretAtFrom, 0.001);
    }

    [StaFact]
    public void OnANestedListItem_TheHighlightTakesTheNestedIndent()
    {
        var canvas = MakeCanvas("- outer\n    - inner text here");
        int vi = LineOf(canvas, 1);

        var (rect, caretAtFrom, _) = SelectAndDraw(canvas, vi, 1, 0,
            canvas.TestGetBlockText(1).Length);

        rect.X.Should().BeApproximately(canvas.TestGetVisualLineContentStartX(vi), 0.001);
        rect.X.Should().BeApproximately(caretAtFrom, 0.001);
        rect.X.Should().BeGreaterThan(canvas.TestGetVisualLineContentStartX(LineOf(canvas, 0)),
            "the nested item is indented past its parent");
    }

    // --- The other highlights drawn over text share the same measurement ---

    [StaFact]
    public void SearchHighlight_OnAListItem_SitsOverTheMatch()
    {
        var canvas = MakeCanvas("- alpha beta gamma");
        int vi = LineOf(canvas, 0);

        canvas.TestExecuteSearch("beta", caseSensitive: false);
        canvas.TestComputeLayout();

        var rect = canvas.TestSearchMatchRect(vi);
        rect.Should().NotBeNull();

        string text = canvas.TestGetBlockText(0);
        int at = text.IndexOf("beta", StringComparison.Ordinal);
        canvas.TestSetCursor(0, at);
        double caretAtMatchStart = canvas.TestCursorX;

        rect!.Value.X.Should().BeApproximately(caretAtMatchStart, 0.001);
    }

    // --- An inline image must not move the line's text off its column ---

    [StaTheory]
    [InlineData("- words ![alt](nope.png) more", "unordered list item")]
    [InlineData("1. words ![alt](nope.png) more", "ordered list item")]
    [InlineData("- [ ] words ![alt](nope.png) more", "task item")]
    [InlineData("> words ![alt](nope.png) more", "blockquote")]
    [InlineData("words ![alt](nope.png) more", "paragraph")]
    public void ALineCarryingAnImage_StillDrawsItsTextOnTheBlocksColumn(string markdown, string kind)
    {
        // The image path draws text and pictures itself instead of handing OnRender one
        // FormattedText, and it used to rebuild the text column from the margin plus the
        // marker's replacement width - so the words landed short of the column, and the caret
        // and the highlight, which both use the real column, sat beside them.
        var canvas = MakeCanvas(markdown);
        int vi = LineOf(canvas, 0);

        canvas.TestLineGlyphOriginXs(vi).Should().Contain(
            x => Math.Abs(x - canvas.TestGetVisualLineContentStartX(vi)) < 0.001,
            $"a {kind} draws its words on its own text column, image or no image");
    }

    // --- Kerning: positions come off the glyphs WPF drew, not summed advance widths ---

    private static DocsCanvas MakeCanvasAt(string text, double zoom, int width,
        DocsCanvas.EditMode mode = DocsCanvas.EditMode.Visual)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(mode);
        canvas.Measure(new Size(width, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, width, CanvasHeight));
        canvas.SetZoom(zoom);
        canvas.TestComputeLayout();
        return canvas;
    }

    /// <summary>
    /// Every visible offset on a line, with the X the caret and the highlights use for it and
    /// the left edge of the glyph WPF drew for it. The text is drawn after any marker glyphs,
    /// so its characters are the tail of the glyph list.
    /// </summary>
    private static List<(int Offset, double Caret, double Glyph)> CaretsAgainstGlyphs(DocsCanvas canvas, int vi)
    {
        var carets = canvas.TestLineVisibleOffsetXs(vi);
        var glyphs = canvas.TestLineGlyphLeftEdges(vi);
        glyphs.Count.Should().BeGreaterThanOrEqualTo(carets.Count, "every visible character draws a glyph");
        int skip = glyphs.Count - carets.Count;
        return carets.Select((c, k) => (c.Offset, c.X, glyphs[skip + k])).ToList();
    }

    private static void AssertCaretsOnGlyphs(DocsCanvas canvas, int vi, string because)
    {
        string text = canvas.TestGetBlockText(canvas.TestGetVisualLineBlockIndex(vi));
        foreach (var (offset, caret, glyph) in CaretsAgainstGlyphs(canvas, vi))
            caret.Should().BeApproximately(glyph, 0.01,
                $"{because}: offset {offset} ('{text[offset]}') is drawn at {glyph:F2}");
    }

    [StaTheory]
    // "y." in "away." is the pair from the screenshot; it pulled " Defaults" 1.9px left of the sum.
    [InlineData("- **The three current `SystemSounds` calls go away.** Defaults become theme sounds.", 1.0)]
    [InlineData("- **The three current `SystemSounds` calls go away.** Defaults become theme sounds.", 1.9)]
    [InlineData("# Step 0 — To Wave, AVATAR", 1.0)]
    [InlineData("# Step 0 — To Wave, AVATAR", 1.9)]
    [InlineData("1. Today, Yesterday. WAVE", 1.3)]
    [InlineData("- [ ] Take a look at P.T. Barnum, Tony", 1.3)]
    [InlineData("> To Wit: LYNX AV, Yo.", 1.3)]
    [InlineData("Paragraph with Wave, *Type*, and y. kerning", 1.9)]
    public void EveryVisibleOffset_SitsOnTheGlyphWpfDrew(string markdown, double zoom)
    {
        var canvas = MakeCanvasAt(markdown, zoom, 4000);

        AssertCaretsOnGlyphs(canvas, LineOf(canvas, 0), $"zoom {zoom}");
    }

    [StaFact]
    public void InSourceMode_EveryOffsetSitsOnItsGlyph()
    {
        var canvas = MakeCanvasAt("# Step 0 — To Wave, AVATAR **y.** Yo", 1.9, 4000, DocsCanvas.EditMode.Source);

        AssertCaretsOnGlyphs(canvas, LineOf(canvas, 0), "source mode");
    }

    [StaFact]
    public void OnEveryLineOfAWrappedItem_EveryOffsetSitsOnItsGlyph()
    {
        // Wrapped lines end on the space they broke at, so this also covers trailing whitespace.
        string item = "- " + string.Join(' ', Enumerable.Repeat("Today, Yesterday. To Wave", 12));
        var canvas = MakeCanvasAt(item, 1.6, 800);

        int lines = Enumerable.Range(0, canvas.TestVisualLineCount)
            .Count(i => canvas.TestGetVisualLineBlockIndex(i) == 0);
        lines.Should().BeGreaterThan(1, "the item has to wrap for this test");

        for (int n = 0; n < lines; n++)
            AssertCaretsOnGlyphs(canvas, LineOf(canvas, 0, n), $"line {n}");
    }

    [StaTheory]
    [InlineData(DocsCanvas.EditMode.Visual)]
    [InlineData(DocsCanvas.EditMode.Source)]
    public void AClick_EitherSideOfAGlyphsMiddle_LandsOnTheMatchingSideOfIt(DocsCanvas.EditMode mode)
    {
        // Only the leading "# " is hidden, so a character's right side is the next offset.
        var canvas = MakeCanvasAt("# Step To Wave, AVATAR", 1.9, 4000, mode);
        int vi = LineOf(canvas, 0);
        double y = canvas.TestGetLineYPosition(vi) + 2;
        var chars = CaretsAgainstGlyphs(canvas, vi);

        for (int k = 0; k < chars.Count - 1; k++)
        {
            double middle = (chars[k].Glyph + chars[k + 1].Glyph) / 2;

            canvas.HitTestToPosition(new Point(middle - 0.6, y), out _, out int left);
            canvas.HitTestToPosition(new Point(middle + 0.6, y), out _, out int right);

            left.Should().Be(chars[k].Offset, $"just left of the middle of glyph {k}");
            right.Should().Be(chars[k].Offset + 1, $"just right of the middle of glyph {k}");
        }
    }

    // --- Source mode is measured the same way, from the margin ---

    [StaFact]
    public void InSourceMode_TheHighlightStartsAtTheMargin()
    {
        var canvas = MakeCanvas("- one two three four", DocsCanvas.EditMode.Source);
        int vi = LineOf(canvas, 0);

        var (rect, caretAtFrom, _) = SelectAndDraw(canvas, vi, 0, 0,
            canvas.TestGetBlockText(0).Length);

        rect.X.Should().BeApproximately(caretAtFrom, 0.001,
            "source mode shows the marker as text, so the highlight covers it too");
    }
}
