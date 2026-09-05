using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// One marker column shared by bullets, checkboxes and ordered numbers. Numbers are
/// right-aligned into it, so the text after them does not move with the digit count; a number
/// too wide for the column pushes the column - and the text - right rather than crossing the
/// left margin.
/// </summary>
public class ListMarkerColumnTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

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

    /// <summary>Content start X of the first visual line of each block, in order.</summary>
    private static double[] ContentStarts(string text)
    {
        var canvas = CreateCanvas(text);
        var seen = new List<double>();
        int lastBlock = -1;
        for (int i = 0; i < canvas.TestVisualLineCount; i++)
        {
            int b = canvas.TestGetVisualLineBlockIndex(i);
            if (b == lastBlock) continue;
            lastBlock = b;
            seen.Add(canvas.TestGetVisualLineContentStartX(i));
        }
        return seen.ToArray();
    }

    // --- One shared column ---

    [StaFact]
    public void BulletCheckboxAndNumber_ShareOneTextColumn()
    {
        var xs = ContentStarts("- bullet\n- [ ] task\n- [x] done\n1. one\n9. nine\n99. ninety nine");

        xs.Should().HaveCount(6);
        xs.Should().AllBeEquivalentTo(xs[0], "every list kind starts its text on the same column");
    }

    [StaFact]
    public void OrderedNumbers_UpToThreeDigits_DoNotMoveTheTextColumn()
    {
        // The number may reach back over the space before the marker, so three digits still fit.
        var xs = ContentStarts("1. a\n12. a\n123. a");

        xs.Should().AllBeEquivalentTo(xs[0]);
    }

    [StaFact]
    public void SharedColumn_IsTheCheckboxWidthPlusTheGap()
    {
        // padding 10 + two spaces 8.766 + column 21.970 (the checkbox, wider than "99.")
        // + the 10px gap after the marker.
        ContentStarts("- bullet")[0].Should().Be(50.735625);
    }

    // --- Numbers right-aligned ---

    [StaFact]
    public void OrderedNumbers_ShareADelimiterPosition()
    {
        var canvas = CreateCanvas("1. a\n10. a\n100. a");

        var rights = new List<double>();
        for (int i = 0; i < canvas.TestVisualLineCount; i++)
            rights.Add(canvas.TestGetVisualLineMarkerRightX(i));

        rights.Should().AllBeEquivalentTo(rights[0],
            "right-aligned numbers put every delimiter on one X");
    }

    // --- Overflow ---

    [StaFact]
    public void OrderedNumber_TooWideForTheColumn_PushesTheTextRight()
    {
        var xs = ContentStarts("1. a\n1010101. a");

        xs[1].Should().BeGreaterThan(xs[0], "an oversized number carries its text right with it");
    }

    [StaFact]
    public void OrderedNumber_TooWideForTheColumn_KeepsTheGapAfterTheMarker()
    {
        var canvas = CreateCanvas("1. a\n1010101. a");

        double NarrowGap(int vi) =>
            canvas.TestGetVisualLineContentStartX(vi) - canvas.TestGetVisualLineMarkerRightX(vi);

        NarrowGap(1).Should().Be(NarrowGap(0), "the gap after the marker is constant");
    }

    [StaFact]
    public void OrderedNumber_TooWideForTheColumn_StaysInsideTheLeftMargin()
    {
        var canvas = CreateCanvas("1010101. a");

        double markerLeft = canvas.TestGetVisualLineMarkerRightX(0)
            - canvas.TestMeasure.MeasureReplacementPrefix("1010101.", BlockKind.OrderedListItem);

        markerLeft.Should().BeGreaterThanOrEqualTo(DocsCanvas._padding,
            "the number clamps at the margin rather than crossing it");
    }

    // --- The clipping regression this rule removes ---

    [StaFact]
    public void FirstLine_NeverWrapsPastTheRightMargin()
    {
        // Before the shared column, a wide marker over-granted the first line width, and the
        // overrun past the right padding was cut off by ClipToBounds.
        const double width = 300;
        foreach (var text in new[] { "- one two three four five six", "12. one two three four five",
                                     "1010101. one two three four five" })
        {
            var canvas = new DocsCanvas();
            canvas.SetText(text);
            canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
            canvas.Measure(new Size(width, CanvasHeight));
            canvas.Arrange(new Rect(0, 0, width, CanvasHeight));
            canvas.TestComputeLayout();

            double right = canvas.TestGetVisualLineContentStartX(0) + canvas.TestVisualLineInkWidth(0);
            right.Should().BeLessThanOrEqualTo(width - DocsCanvas._padding + 0.001,
                $"\"{text}\" must not draw past the right margin");
        }
    }
}
