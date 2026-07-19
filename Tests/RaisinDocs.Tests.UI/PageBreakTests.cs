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

    // --- Auto-computed breaks ---

    [StaFact]
    public void ShortDocument_NoBreaks()
    {
        var canvas = CreateCanvas("Hello\nWorld");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().BeEmpty();
    }

    [StaFact]
    public void LongDocument_HasBreaks()
    {
        var canvas = CreateCanvas(GenerateLines(100));
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().NotBeEmpty();
        breaks[0].Should().BeGreaterThan(100);
    }

    [StaFact]
    public void AutoBreaks_DoNotOrphanHeading()
    {
        var lines = new List<string>();
        for (int i = 0; i < 45; i++)
            lines.Add($"Line {i + 1}");
        lines.Add("");
        lines.Add("## My Heading");
        lines.Add("");
        for (int i = 0; i < 10; i++)
            lines.Add($"After heading line {i + 1}");

        var canvas = CreateCanvas(string.Join("\n", lines));
        var breaks = canvas.TestGetPageBreakYs();

        if (breaks.Count == 0) return;

        for (int vi = 0; vi < canvas.TestVisualLineCount; vi++)
        {
            double y = canvas.TestGetLineYPosition(vi);
            var kind = canvas.TestGetVisualLineBlockKind(vi);
            if (kind is >= BlockKind.Heading1 and <= BlockKind.Heading6)
            {
                foreach (double breakY in breaks)
                {
                    bool headingIsLastOnPage = breakY > y && breakY < y + 40;
                    headingIsLastOnPage.Should().BeFalse(
                        "a heading should not be orphaned at the bottom of a page");
                }
            }
        }
    }

    [StaFact]
    public void ConsecutiveAutoBreaks_Increasing()
    {
        var canvas = CreateCanvas(GenerateLines(300));
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCountGreaterThanOrEqualTo(3);
        for (int i = 1; i < breaks.Count; i++)
            breaks[i].Should().BeGreaterThan(breaks[i - 1]);
    }

    // --- Explicit pagebreak tag ---

    [StaFact]
    public void ExplicitTag_ProducesBreak()
    {
        var canvas = CreateCanvas("Line 1\n<!--@pagebreak-->\nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().NotBeEmpty();
    }

    [StaFact]
    public void ExplicitTag_CaseInsensitive()
    {
        var canvas = CreateCanvas("Line 1\n<!--@PageBreak-->\nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().NotBeEmpty();
    }

    [StaFact]
    public void ExplicitTag_WithWhitespace()
    {
        var canvas = CreateCanvas("Line 1\n  <!--@pagebreak-->  \nLine 2");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().NotBeEmpty();
    }

    [StaFact]
    public void ExplicitTag_VisualMode()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("Line 1\n<!--@pagebreak-->\nLine 2");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().NotBeEmpty();
    }

    [StaFact]
    public void MultipleExplicitTags()
    {
        var canvas = CreateCanvas("Part 1\n<!--@pagebreak-->\nPart 2\n<!--@pagebreak-->\nPart 3");
        var breaks = canvas.TestGetPageBreakYs();
        breaks.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
