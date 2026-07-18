using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class PageBreakTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    private static string GenerateLines(int count)
    {
        var lines = new string[count];
        for (int i = 0; i < count; i++)
            lines[i] = $"Line {i + 1}: This is some text content for testing page breaks.";
        return string.Join("\n", lines);
    }

    [StaFact]
    public void PageBreaks_ShortDocument_NoBreaks()
    {
        var canvas = CreateCanvas("Hello\nWorld");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().BeEmpty();
    }

    [StaFact]
    public void PageBreaks_LongDocument_FirstBreakIsPage2()
    {
        var canvas = CreateCanvas(GenerateLines(100));
        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().NotBeEmpty("a 100-line document should have at least one page break");
        breaks.Count.Should().BeGreaterThanOrEqualTo(1);

        // First break should be at a reasonable Y position (well past the start)
        breaks[0].Should().BeGreaterThan(100, "first break should not be near the top of the document");
    }

    [StaFact]
    public void PageBreaks_BreaksAreWellSpaced()
    {
        var canvas = CreateCanvas(GenerateLines(200));
        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().HaveCountGreaterThanOrEqualTo(2);

        for (int i = 1; i < breaks.Count; i++)
        {
            double gap = breaks[i] - breaks[i - 1];
            gap.Should().BeGreaterThan(100, "page breaks should be well-spaced, not close together");
        }
    }

    [StaFact]
    public void PageBreaks_FirstBreakPosition_MatchesPageContentHeight()
    {
        var canvas = CreateCanvas(GenerateLines(100));
        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().NotBeEmpty();

        // The first break should be around DefaultPageHeight - 2*MarginY = 1056 - 120 = 936
        // plus the initial padding of 10, so approximately 946
        double expectedApprox = 936 + 10; // pageContentH + _padding
        breaks[0].Should().BeInRange(expectedApprox - 100, expectedApprox + 100,
            "first break should be near the end of one page of content");
    }

    [StaFact]
    public void PageBreaks_VisualMode_FirstBreakIsPage2()
    {
        var canvas = new DocsCanvas();
        canvas.SetText(GenerateLines(100));
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().NotBeEmpty("100 lines in visual mode should produce page breaks");
        breaks[0].Should().BeGreaterThan(100, "first break should not be near the top");
    }

    [StaFact]
    public void PageBreaks_VisualMode_WithHeadings()
    {
        var lines = new List<string>();
        lines.Add("# Heading 1");
        lines.Add("");
        for (int i = 0; i < 30; i++)
        {
            lines.Add($"Paragraph {i + 1} text here.");
            lines.Add("");
        }
        lines.Add("## Heading 2");
        lines.Add("");
        for (int i = 0; i < 30; i++)
        {
            lines.Add($"More text paragraph {i + 1}.");
            lines.Add("");
        }

        var canvas = new DocsCanvas();
        canvas.SetText(string.Join("\n", lines));
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().NotBeEmpty(
            $"total content height = {canvas.TestTotalContentHeight}, visual lines = {canvas.TestVisualLineCount}");
        breaks[0].Should().BeGreaterThan(200,
            "first break should be well past the start even with headings");
    }

    [StaFact]
    public void PageBreaks_ConsecutiveBreaks_HaveIncreasingYValues()
    {
        var canvas = CreateCanvas(GenerateLines(300));
        var breaks = canvas.TestGetPageBreakYs();

        breaks.Should().HaveCountGreaterThanOrEqualTo(3);

        for (int i = 1; i < breaks.Count; i++)
            breaks[i].Should().BeGreaterThan(breaks[i - 1]);
    }

    [StaFact]
    public void PageBreaks_AfterLayoutAtDifferentWidth_StillCorrect()
    {
        var canvas = CreateCanvas(GenerateLines(100));

        var breaksBefore = canvas.TestGetPageBreakYs();
        breaksBefore.Should().NotBeEmpty();
        double firstBreakBefore = breaksBefore[0];

        canvas.TestComputeLayoutAtWidth(400);

        var breaksAtPrintWidth = canvas.TestGetPageBreakYs();
        breaksAtPrintWidth.Should().NotBeEmpty();

        canvas.TestComputeLayout();

        var breaksAfter = canvas.TestGetPageBreakYs();
        breaksAfter.Should().NotBeEmpty();
        breaksAfter[0].Should().BeApproximately(firstBreakBefore, 1.0,
            "page breaks should be restored after layout returns to screen width");
    }
}
