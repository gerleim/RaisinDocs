using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class EmptyVisualLinesTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    // Gap #8: Verify HitTestVisualLine, HitTestToPosition, and EnsureCursorVisible
    // don't crash when _visualLines is empty (guards added for H2 fix).

    [StaFact]
    public void EnsureCursorVisible_BeforeLayout_DoesNotCrash()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("hello");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        // Don't call Measure/Arrange/TestComputeLayout — _visualLines stays empty
        canvas.EnsureCursorVisible();
    }

    [StaFact]
    public void Navigate_BeforeLayout_DoesNotCrash()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("hello");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        // TestNavigate calls ComputeLayout internally, which should populate
        // _visualLines — but verify it doesn't crash if called on empty doc
        var act = () => canvas.TestNavigate(Key.Down);
        act.Should().NotThrow();
    }

    [StaFact]
    public void Navigate_EmptyDocument_DoesNotCrash()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var act = () =>
        {
            canvas.TestNavigate(Key.Up);
            canvas.TestNavigate(Key.Down);
            canvas.TestNavigate(Key.Left);
            canvas.TestNavigate(Key.Right);
            canvas.TestNavigate(Key.Home);
            canvas.TestNavigate(Key.End);
        };
        act.Should().NotThrow();
    }

    [StaFact]
    public void CursorX_EmptyDocument_DoesNotCrash()
    {
        var canvas = new DocsCanvas();
        canvas.SetText("");
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var act = () => { var _ = canvas.TestCursorX; };
        act.Should().NotThrow();
    }
}
