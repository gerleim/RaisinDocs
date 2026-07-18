using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class MinimapScrollbarTests
{
    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.TestComputeLayout();
        return canvas;
    }

    private static (DocsCanvas canvas, MinimapScrollbar minimap) CreateCanvasWithMinimap(
        string text, double canvasHeight = 600, double minimapHeight = 400,
        DocsCanvas.EditMode mode = DocsCanvas.EditMode.Visual,
        string? basePath = null,
        (string url, double w, double h)[]? images = null)
    {
        var canvas = new DocsCanvas();
        canvas.TestSetEditMode(mode);
        if (basePath != null)
            canvas.DocumentBasePath = basePath;

        if (images != null)
        {
            foreach (var (url, w, h) in images)
                canvas.TestImageCache.TestInject(url, basePath, w, h);
        }

        canvas.SetText(text);
        canvas.Measure(new Size(800, canvasHeight));
        canvas.Arrange(new Rect(0, 0, 800, canvasHeight));
        canvas.TestComputeLayout();

        var minimap = new MinimapScrollbar();
        minimap.Canvas = canvas;
        canvas.Minimap = minimap;
        minimap.Measure(new Size(100, minimapHeight));
        minimap.Arrange(new Rect(0, 0, 100, minimapHeight));

        return (canvas, minimap);
    }

    [StaFact]
    public void GetMinimapLineInfo_OutOfBounds_DoesNotThrow()
    {
        var canvas = CreateCanvas("hello\nworld");
        int lineCount = canvas.MinimapLineCount;

        var act = () => canvas.GetMinimapLineInfo(lineCount, out _, out _);

        act.Should().NotThrow();
    }

    [StaFact]
    public void GetMinimapLineInfo_NegativeIndex_DoesNotThrow()
    {
        var canvas = CreateCanvas("hello");

        var act = () => canvas.GetMinimapLineInfo(-1, out _, out _);

        act.Should().NotThrow();
    }

    [StaFact]
    public void GetMinimapLineInfo_ValidIndex_ReturnsText()
    {
        var canvas = CreateCanvas("hello\nworld");

        canvas.GetMinimapLineInfo(0, out string text, out _);

        text.Should().Be("hello");
    }

    [StaFact]
    public void GetMinimapLineInfo_AfterTextChange_StaleIndexDoesNotThrow()
    {
        var canvas = CreateCanvas("line1\nline2\nline3\nline4\nline5");
        int originalCount = canvas.MinimapLineCount;

        canvas.SetText("short");
        canvas.TestComputeLayout();

        var act = () => canvas.GetMinimapLineInfo(originalCount - 1, out _, out _);

        act.Should().NotThrow();
    }

    // --- FoldToAscii: chars in 0x80-0xBF must not produce values > LastPrintable ---

    [Fact]
    public void FoldToAscii_LatinExtended_ReturnsAsciiOrZero()
    {
        for (int ch = 0xC0; ch <= 0xFF; ch++)
        {
            int result = MinimapScrollbar.FoldToAscii(ch);
            result.Should().BeLessThanOrEqualTo(126,
                $"char 0x{ch:X2} folded to {result} which exceeds glyph array bounds");
        }
    }

    [Fact]
    public void FoldToAscii_ControlRange_0x80_0xBF_ReturnsZero()
    {
        for (int ch = 0x80; ch < 0xC0; ch++)
        {
            int result = MinimapScrollbar.FoldToAscii(ch);
            result.Should().Be(0,
                $"char 0x{ch:X2} in 0x80-0xBF range should fold to 0 (skip)");
        }
    }

    [Fact]
    public void FoldToAscii_Ascii_ReturnsUnchanged()
    {
        for (int ch = 0; ch < 0x80; ch++)
        {
            int result = MinimapScrollbar.FoldToAscii(ch);
            result.Should().Be(ch);
        }
    }

    // --- Viewport tracking with real image heights ---

    [StaFact]
    public void ViewportTracking_WithImages_VpTopChangesMonotonically()
    {
        var content = BuildVeeeStyleContent();
        var images = new[]
        {
            ("charts/SKUU_trades.png", 1920.0, 1080.0),
            ("charts/VEEE_overview.png", 1920.0, 1080.0),
            ("charts/VEEE_trades.png", 1920.0, 1080.0),
        };
        var (canvas, minimap) = CreateCanvasWithMinimap(content,
            canvasHeight: 600, minimapHeight: 300, images: images, basePath: @"C:\test");

        double totalContent = canvas.MinimapTotalHeight;
        double maxScroll = totalContent - canvas.ActualHeight;
        maxScroll.Should().BeGreaterThan(0, "content must be taller than canvas for this test");

        // Verify images actually created large override heights
        var lineYs = canvas.MinimapCanvasLineYPositions;
        double baseH = canvas.MinimapBaseLineHeight;
        bool hasLargeLine = false;
        for (int i = 0; i < lineYs.Count - 1; i++)
        {
            double h = lineYs[i + 1] - lineYs[i];
            if (h > baseH * 5) { hasLargeLine = true; break; }
        }
        hasLargeLine.Should().BeTrue("images should create lines much taller than base height");

        double prevVpTop = double.MinValue;
        int steps = 500;
        double maxJump = 0;
        int jumpStep = -1;
        double jumpScroll = 0;

        for (int i = 0; i <= steps; i++)
        {
            double scroll = maxScroll * i / steps;
            canvas.SetScrollOffsetDirect(scroll);
            minimap.TestUpdateViewport();

            double vpTop = minimap.TestVpTop;

            if (i > 0)
            {
                double jump = vpTop - prevVpTop;
                jump.Should().BeGreaterOrEqualTo(-0.001,
                    $"vpTop went backwards at step {i}: prev={prevVpTop:F4}, cur={vpTop:F4}, scroll={scroll:F2}");

                if (jump > maxJump)
                {
                    maxJump = jump;
                    jumpStep = i;
                    jumpScroll = scroll;
                }
            }

            prevVpTop = vpTop;
        }

        // Check for proportionality: max jump should not exceed 2x average
        canvas.SetScrollOffsetDirect(0);
        minimap.TestUpdateViewport();
        double vpTopAtZero = minimap.TestVpTop;

        canvas.SetScrollOffsetDirect(maxScroll);
        minimap.TestUpdateViewport();
        double vpTopAtMax = minimap.TestVpTop;

        double fullRange = vpTopAtMax - vpTopAtZero;
        double avgStep = fullRange / steps;
        maxJump.Should().BeLessThan(avgStep * 2.5,
            $"excessive jump at step {jumpStep} (scroll={jumpScroll:F2}): " +
            $"maxJump={maxJump:F4}, avgStep={avgStep:F4}, range={fullRange:F2}");
    }

    [StaFact]
    public void ViewportTracking_WithImages_DragProducesLinearScrolling()
    {
        var content = BuildVeeeStyleContent();
        var images = new[]
        {
            ("charts/SKUU_trades.png", 1920.0, 1080.0),
            ("charts/VEEE_overview.png", 1920.0, 1080.0),
            ("charts/VEEE_trades.png", 1920.0, 1080.0),
        };
        var (canvas, minimap) = CreateCanvasWithMinimap(content,
            canvasHeight: 600, minimapHeight: 300, images: images, basePath: @"C:\test");

        double maxScroll = canvas.MinimapTotalHeight - canvas.ActualHeight;
        if (maxScroll <= 0) return;

        canvas.SetScrollOffsetDirect(0);
        minimap.TestUpdateViewport();

        double vpHeight = minimap.TestVpHeight;
        double screenRange = 300 - vpHeight;
        double dragPixelToScroll = screenRange > 0 ? maxScroll / screenRange : 0;

        double prevVpTop = minimap.TestVpTop;
        int steps = 200;
        double maxDelta = 0;
        double minDelta = double.MaxValue;

        for (int i = 1; i <= steps; i++)
        {
            double deltaY = i * (screenRange / steps);
            double newScroll = deltaY * dragPixelToScroll;
            newScroll = Math.Clamp(newScroll, 0, maxScroll);

            canvas.SetScrollOffsetDirect(newScroll);
            minimap.TestUpdateViewport();

            double vpTop = minimap.TestVpTop;
            double delta = vpTop - prevVpTop;

            if (delta > maxDelta) maxDelta = delta;
            if (delta < minDelta) minDelta = delta;

            prevVpTop = vpTop;
        }

        double ratio = minDelta > 0 ? maxDelta / minDelta : double.MaxValue;
        ratio.Should().BeLessThan(1.5,
            $"non-linear tracking: minDelta={minDelta:F6}, maxDelta={maxDelta:F6}, ratio={ratio:F3}");
    }

    [StaFact]
    public void ViewportTracking_WithImages_DumpDiagnostics()
    {
        var content = BuildVeeeStyleContent();
        var images = new[]
        {
            ("charts/SKUU_trades.png", 1920.0, 1080.0),
            ("charts/VEEE_overview.png", 1920.0, 1080.0),
            ("charts/VEEE_trades.png", 1920.0, 1080.0),
        };
        var (canvas, minimap) = CreateCanvasWithMinimap(content,
            canvasHeight: 600, minimapHeight: 300, images: images, basePath: @"C:\test");

        double totalContent = canvas.MinimapTotalHeight;
        double maxScroll = totalContent - canvas.ActualHeight;
        double baseH = canvas.MinimapBaseLineHeight;

        // Dump line heights to find varied-height lines
        var lineYs = canvas.MinimapCanvasLineYPositions;
        var tallLines = new System.Collections.Generic.List<string>();
        for (int i = 0; i < lineYs.Count - 1; i++)
        {
            double h = lineYs[i + 1] - lineYs[i];
            if (h > baseH * 2)
            {
                canvas.GetMinimapLineInfo(i, out string text, out BlockKind kind);
                tallLines.Add($"  line {i}: height={h:F1} kind={kind} text='{text.Substring(0, Math.Min(40, text.Length))}'");
            }
        }

        // Sweep scroll and record vpTop deltas
        int steps = 100;
        var deltas = new double[steps];
        double prevVpTop = 0;
        canvas.SetScrollOffsetDirect(0);
        minimap.TestUpdateViewport();
        prevVpTop = minimap.TestVpTop;

        for (int i = 1; i <= steps; i++)
        {
            double scroll = maxScroll * i / steps;
            canvas.SetScrollOffsetDirect(scroll);
            minimap.TestUpdateViewport();
            double vpTop = minimap.TestVpTop;
            deltas[i - 1] = vpTop - prevVpTop;
            prevVpTop = vpTop;
        }

        double avgDelta = deltas.Average();
        double maxDev = deltas.Max() - deltas.Min();

        // This test always passes — it's diagnostic
        // Check output for anomalies
        tallLines.Count.Should().BeGreaterThan(0,
            $"Expected tall lines from images. totalContent={totalContent:F1}, maxScroll={maxScroll:F1}, " +
            $"baseH={baseH:F1}, lineCount={lineYs.Count}, " +
            $"avgDelta={avgDelta:F6}, maxDeviation={maxDev:F6}, " +
            $"totalMinimapH={minimap.TestTotalMinimapH:F1}, vpHeight={minimap.TestVpHeight:F2}\n" +
            string.Join("\n", tallLines));
    }

    private static string BuildVeeeStyleContent()
    {
        var lines = new System.Collections.Generic.List<string>();

        lines.Add("## SKUU");
        lines.Add("");
        lines.Add("### P&L Summary");
        lines.Add("");
        lines.Add("| Metric | Value |");
        lines.Add("|--------|-------|");
        lines.Add("| Buy Qty | 100 |");
        lines.Add("| Sell Qty | 100 |");
        lines.Add("| Gross P&L | +$3.00 |");
        lines.Add("");
        lines.Add("### Round Trips");
        lines.Add("");
        lines.Add("| # | Entry | Exit | Qty | Entry $ | Exit $ | P&L |");
        lines.Add("|---|-------|------|-----|---------|--------|-----|");
        lines.Add("| 1 | 06:16:17 | 06:16:44 | 100 | $25.38 | $25.41 | $-1.07 |");
        lines.Add("");
        for (int i = 0; i < 20; i++)
            lines.Add($"Some detail text line {i} with enough content to fill space in the document.");
        lines.Add("");
        lines.Add("### Trades");
        lines.Add("");
        lines.Add("| RT | Time | Side | Qty | Price | P&L |");
        lines.Add("|----|------|------|-----|-------|-----|");
        lines.Add("| 1 | 06:16:17 | Buy | 100 | $25.38 |  |");
        lines.Add("| 1 | 06:16:44 | Sell | 50 | $25.41 | +$1.50 |");
        lines.Add("");
        lines.Add("![SKUU trades](charts/SKUU_trades.png)");
        lines.Add("");
        lines.Add("---");
        lines.Add("");
        lines.Add("## VEEE");
        lines.Add("");
        lines.Add("### P&L Summary");
        lines.Add("");
        lines.Add("| Metric | Value |");
        lines.Add("|--------|-------|");
        lines.Add("| Buy Qty | 94 |");
        lines.Add("| Sell Qty | 94 |");
        lines.Add("| Avg Buy Price | $45.45 |");
        lines.Add("| Avg Sell Price | $45.76 |");
        lines.Add("| Gross P&L | +$29.14 |");
        lines.Add("");
        lines.Add("### Round Trips");
        lines.Add("");
        lines.Add("| # | Entry | Exit | Qty | Entry $ | Exit $ | P&L |");
        lines.Add("|---|-------|------|-----|---------|--------|-----|");
        lines.Add("| 1 | 04:31:41 | 04:32:11 | 94 | $45.45 | $45.76 | +$27.03 |");
        lines.Add("");
        for (int i = 0; i < 20; i++)
            lines.Add($"Additional detail line {i} providing context about the trade execution.");
        lines.Add("");
        lines.Add("### Overview");
        lines.Add("");
        lines.Add("![VEEE Overview](charts/VEEE_overview.png)");
        lines.Add("");
        lines.Add("### Trades");
        lines.Add("");
        lines.Add("| RT | Time | Side | Qty | Price | P&L |");
        lines.Add("|----|------|------|-----|-------|-----|");
        lines.Add("| 1 | 04:31:41 | Buy | 94 | $45.45 |  |");
        lines.Add("| 1 | 04:32:11 | Sell | 94 | $45.76 | +$29.14 |");
        lines.Add("");
        lines.Add("![VEEE trades](charts/VEEE_trades.png)");
        lines.Add("");
        lines.Add("---");
        lines.Add("");
        lines.Add("## Additional Notes");
        lines.Add("");
        for (int i = 0; i < 30; i++)
            lines.Add($"Extra content line {i} to ensure the document is long enough to require scrolling.");

        return string.Join("\n", lines);
    }
}
