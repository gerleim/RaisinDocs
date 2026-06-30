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
}
