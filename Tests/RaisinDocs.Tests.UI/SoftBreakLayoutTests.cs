using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// A soft break renders as a pilcrow followed by a visual space. Layout and hit-testing
/// must both account for that extra space, otherwise long lines overflow the layout width
/// (their tail gets clipped) and clicks land to the right of where the user pointed.
/// </summary>
public class SoftBreakLayoutTests
{
    private const int CanvasWidth = 420;
    private const int CanvasHeight = 600;
    private const double Padding = 10;
    private const double MaxTextWidth = CanvasWidth - Padding * 2;

    // Short soft-broken segments, so several pilcrows land in the middle of a wrapped
    // visual line rather than at its end.
    private const string SoftBrokenParagraph =
        "alpha beta\ngamma delta\nepsilon zeta\neta theta iota\nkappa lambda mu\n" +
        "nu xi omicron pi\nrho sigma tau\nupsilon phi chi\npsi omega done";

    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    /// <summary>
    /// Rendered width of a visual line, excluding trailing whitespace — which is allowed to
    /// hang past the right edge because it draws nothing. A soft break's visual space counts
    /// as trailing whitespace; the pilcrow itself is ink and must fit.
    /// </summary>
    private static double MeasureVisibleInk(DocsCanvas canvas, DocsCanvas.VisualLine vl)
    {
        var group = vl.Group!;
        double spaceWidth = canvas.TestMeasure.MeasureCharWidth(' ', BlockKind.Paragraph, InlineStyle.Normal);

        int len = vl.Length;
        while (len > 0 && group.JoinedText[vl.StartOffset + len - 1] == ' ')
            len--;

        double width = canvas.MeasureJoinedRange(group, vl.StartOffset, len);

        int last = vl.StartOffset + len - 1;
        if (len > 0 && group.JoinedText[last] == '¶' && group.SoftBreakOffsets.Contains(last))
            width -= spaceWidth;

        return width;
    }

    [StaFact]
    public void SoftBreakParagraph_WrappedLines_StayWithinLayoutWidth()
    {
        var canvas = CreateCanvas(SoftBrokenParagraph);

        int groupedLines = 0;

        // Sweep the available width so that soft breaks land at every position relative to
        // the wrap point, including the tight ones where the trailing visual space decides
        // whether the line still fits.
        for (double width = 200; width <= 600; width += 3)
        {
            canvas.TestComputeLayoutAtWidth(width);

            for (int vi = 0; vi < canvas.TestVisualLineCount; vi++)
            {
                var vl = canvas.TestVisualLines[vi];
                if (vl.Group == null) continue;
                groupedLines++;

                double inkWidth = MeasureVisibleInk(canvas, vl);
                inkWidth.Should().BeLessThanOrEqualTo(width + 0.01,
                    $"visual line {vi} at width {width} must fit or its tail is clipped");
            }
        }

        groupedLines.Should().BeGreaterThan(1, "the paragraph should wrap onto several joined lines");
    }

    [StaFact]
    public void SoftBreakParagraph_ClickRoundTripsToTheClickedOffset()
    {
        var canvas = CreateCanvas(SoftBrokenParagraph);

        int checkedOffsets = 0;
        for (int vi = 0; vi < canvas.TestVisualLineCount; vi++)
        {
            var vl = canvas.TestVisualLines[vi];
            if (vl.Group == null || vl.Length < 3) continue;

            double y = canvas.TestGetLineYPosition(vi) + 2;

            // Skip the line's first/last offset: those are ambiguous between adjacent lines.
            for (int joined = vl.StartOffset + 1; joined < vl.StartOffset + vl.Length; joined++)
            {
                var (block, offset) = vl.Group.JoinedToSource(joined);
                canvas.TestSetCursor(block, offset);
                double x = canvas.TestCursorX;

                canvas.HitTestToPosition(new Point(x, y), out int hitBlock, out int hitOffset);

                hitBlock.Should().Be(block);
                hitOffset.Should().Be(offset,
                    $"a click at the caret position of offset {offset} must land back on it");
                checkedOffsets++;
            }
        }

        checkedOffsets.Should().BeGreaterThan(0);
    }
}
