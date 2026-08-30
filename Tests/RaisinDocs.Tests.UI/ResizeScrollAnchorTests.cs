using System.Text;
using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Resizing the window reflows every wrapped line, which changes total content height.
/// The viewport must stay anchored to what the reader is looking at instead of drifting
/// to whatever now happens to sit at the same pixel offset.
/// </summary>
public class ResizeScrollAnchorTests
{
    private const int CanvasHeight = 300;

    private static string BuildDocument()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            sb.AppendLine($"Paragraph {i}. Some words here to force wrapping at narrow widths, " +
                          "and a few more words so that this paragraph spans several visual lines " +
                          "once the canvas gets narrower than it started out.");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static DocsCanvas CreateCanvas(double width)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(BuildDocument());
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(width, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, width, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    /// <summary>
    /// A detached element gets no OnRenderSizeChanged from WPF, so drive the width-change
    /// path the same way that handler does.
    /// </summary>
    private static void Resize(DocsCanvas canvas, double width)
    {
        canvas.Measure(new Size(width, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, width, CanvasHeight));
        canvas.ReflowPreservingViewport();
    }

    private static int TopVisibleBlock(DocsCanvas canvas)
    {
        int vli = canvas.HitTestVisualLine(canvas.ScrollOffset);
        return canvas.TestGetVisualLineBlockIndex(vli);
    }

    [StaTheory]
    [InlineData(900, 600)]   // narrower
    [InlineData(600, 900)]   // wider
    public void Resize_KeepsTheSameBlockAtTheTopOfTheViewport(double startWidth, double endWidth)
    {
        var canvas = CreateCanvas(startWidth);

        canvas.SetScrollOffsetDirect(1200);
        canvas.TestComputeLayout();

        int before = TopVisibleBlock(canvas);
        int linesBefore = canvas.TestVisualLineCount;
        before.Should().BeGreaterThan(0, "the test must actually be scrolled into the document");

        Resize(canvas, endWidth);

        canvas.TestVisualLineCount.Should().NotBe(linesBefore,
            "the resize must actually rewrap, or this test proves nothing");

        TopVisibleBlock(canvas).Should().Be(before,
            "resizing must not scroll the reader away from the part of the document they were on");
    }
}
