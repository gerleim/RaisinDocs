using System.Windows;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Checks layout against what actually gets drawn: whatever width <c>FitLine</c> wraps to,
/// the FormattedText that OnRender builds for that line must not be wider, or its tail is
/// clipped at the right edge of the canvas.
/// </summary>
public class JoinedLineRenderWidthTests
{
    private readonly ITestOutputHelper _output;
    public JoinedLineRenderWidthTests(ITestOutputHelper output) => _output = output;

    private const double Padding = 10;

    // A paragraph mixing soft breaks, inline code and bold spans (bold crossing a soft break),
    // matching the shape of a real document that showed clipped line ends on a wide window.
    private const string Paragraph =
        "More than it looks, because **adjusted** carries a precise meaning by accident of implementation:\n" +
        "`UpdateStopPrice` appends **only** when `isManual: true` (`InternalStopService.cs:151`), and the\n" +
        "automatic paths take the default (`IstpStrategyService.cs:357`, `QuickGrabStrategyService.cs:265`),\n" +
        "logging trailed separately. So for stop labels, **adjusted** **means the trader moved this stop by\n" +
        "hand**. That is the behavioural signal, and it is already there.";

    private static DocsCanvas CreateCanvas(string text, double width)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(width, 600));
        canvas.Arrange(new Rect(0, 0, width, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    [StaFact]
    public void JoinedLines_RenderNoWiderThanTheWidthTheyWereWrappedTo()
    {
        var canvas = CreateCanvas(Paragraph, 1900);

        int checkedLines = 0;
        double worstOverflow = 0;

        // Sweep widths so soft breaks, code spans and bold runs land at every position
        // relative to the wrap point.
        for (double maxWidth = 300; maxWidth <= 1900; maxWidth += 7)
        {
            canvas.TestComputeLayoutAtWidth(maxWidth);

            for (int vi = 0; vi < canvas.TestVisualLineCount; vi++)
            {
                if (canvas.TestVisualLines[vi].Group == null) continue;
                checkedLines++;

                var vl = canvas.TestVisualLines[vi];
                double rendered = canvas.TestRenderedJoinedLineWidth(vi);
                double measured = canvas.MeasureJoinedRange(vl.Group!, vl.StartOffset, vl.Length);

                // Rendering may come out narrower than the per-character sum, because WPF
                // applies kerning that character-by-character measurement cannot see. Wider
                // means the renderer styled different characters than layout measured.
                rendered.Should().BeLessThanOrEqualTo(measured + 0.01,
                    $"line {vi} at width {maxWidth} must not render wider than layout measured it");

                double overflow = rendered - maxWidth;
                if (overflow > worstOverflow)
                {
                    worstOverflow = overflow;
                    _output.WriteLine($"width={maxWidth} line={vi} rendered={rendered:F2} overflow={overflow:F2}");
                }
            }
        }

        checkedLines.Should().BeGreaterThan(0);
        worstOverflow.Should().BeLessThanOrEqualTo(0.01,
            "a line that renders wider than it was wrapped to gets its tail clipped");
    }
}
