using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// A run of Up/Down keeps aiming at the column it started from, so a short or empty line
/// passed through on the way does not truncate the column.
/// </summary>
public class VerticalGoalColumnTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas MakeCanvas(string text, DocsCanvas.EditMode mode)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(mode);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    [StaTheory]
    [InlineData(DocsCanvas.EditMode.Visual)]
    [InlineData(DocsCanvas.EditMode.Source)]
    public void Down_ThroughEmptyLine_KeepsColumn(DocsCanvas.EditMode mode)
    {
        var canvas = MakeCanvas("same words here\n\nsame words here", mode);
        canvas.TestSetCursor(0, 3); // sam|e words here

        canvas.TestNavigate(Key.Down);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(0); // the empty line has nowhere else to be

        canvas.TestNavigate(Key.Down);
        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().Be(3); // sam|e words here
    }

    [StaTheory]
    [InlineData(DocsCanvas.EditMode.Visual)]
    [InlineData(DocsCanvas.EditMode.Source)]
    public void Up_ThroughEmptyLine_KeepsColumn(DocsCanvas.EditMode mode)
    {
        var canvas = MakeCanvas("same words here\n\nsame words here", mode);
        canvas.TestSetCursor(2, 3); // sam|e words here

        canvas.TestNavigate(Key.Up);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(0);

        canvas.TestNavigate(Key.Up);
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(3); // sam|e words here
    }

    [StaTheory]
    [InlineData(DocsCanvas.EditMode.Visual)]
    [InlineData(DocsCanvas.EditMode.Source)]
    public void Down_ThroughShortLine_KeepsColumn(DocsCanvas.EditMode mode)
    {
        var canvas = MakeCanvas("same words here\n\nab\n\nsame words here", mode);
        canvas.TestSetCursor(0, 10);

        canvas.TestNavigate(Key.Down); // empty
        canvas.TestNavigate(Key.Down); // "ab" — clamped to its end
        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().Be(2);

        canvas.TestNavigate(Key.Down); // empty
        canvas.TestNavigate(Key.Down);
        canvas.TestCursorBlock.Should().Be(4);
        canvas.TestCursorOffset.Should().Be(10);
    }

    [StaFact]
    public void HorizontalMove_ClearsGoalColumn()
    {
        var canvas = MakeCanvas("same words here\n\nsame words here", DocsCanvas.EditMode.Visual);
        canvas.TestSetCursor(0, 3);

        canvas.TestNavigate(Key.Down);  // empty line, still aiming at column 3
        canvas.TestNavigate(Key.Right); // a horizontal move discards the goal
        canvas.TestCursorBlock.Should().Be(2);
        canvas.TestCursorOffset.Should().Be(0);

        canvas.TestNavigate(Key.Up);
        canvas.TestNavigate(Key.Up);
        canvas.TestCursorBlock.Should().Be(0);
        canvas.TestCursorOffset.Should().Be(0);
    }
}
