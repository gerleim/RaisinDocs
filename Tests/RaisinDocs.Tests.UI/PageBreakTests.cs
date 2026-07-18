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

    [StaFact]
    public void NoPageBreakTag_NoBreaks()
    {
        var canvas = CreateCanvas("Hello\nWorld\nLine 3");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().BeEmpty();
    }

    [StaFact]
    public void SinglePageBreakTag_OneBreak()
    {
        var canvas = CreateCanvas("Line 1\n<!--@pagebreak-->\nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
    }

    [StaFact]
    public void PageBreakTag_BreakYIsAtNextContent()
    {
        var canvas = CreateCanvas("Line 1\n<!--@pagebreak-->\nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
        breaks[0].Should().BeGreaterThan(0, "break should be at a positive Y position");
    }

    [StaFact]
    public void MultiplePageBreakTags_MultipleBreaks()
    {
        var canvas = CreateCanvas("Line 1\n<!--@pagebreak-->\nLine 2\n<!--@pagebreak-->\nLine 3");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(2);
        breaks[1].Should().BeGreaterThan(breaks[0]);
    }

    [StaFact]
    public void PageBreakTag_CaseInsensitive()
    {
        var canvas = CreateCanvas("Line 1\n<!--@PageBreak-->\nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
    }

    [StaFact]
    public void PageBreakTag_WithLeadingTrailingWhitespace()
    {
        var canvas = CreateCanvas("Line 1\n  <!--@pagebreak-->  \nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
    }

    [StaFact]
    public void PageBreakTag_VisualMode_StillDetected()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("Line 1\n<!--@pagebreak-->\nLine 2");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
    }

    [StaFact]
    public void PageBreakTag_BeforeTable_BreaksBeforeTable()
    {
        var text = "Summary\n\n<!--@pagebreak-->\n\n| Col1 | Col2 |\n| --- | --- |\n| A | B |";
        var canvas = CreateCanvas(text);
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCount(1);
    }
}
