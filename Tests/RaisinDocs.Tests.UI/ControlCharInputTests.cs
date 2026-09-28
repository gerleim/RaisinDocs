using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Keys OnKeyDown leaves unhandled still raise TextInput with a control character — Escape
/// sends "\x1b". That is not typing, so it must neither insert anything nor replace a selection.
/// </summary>
public class ControlCharInputTests
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

    private static void RaiseTextInput(DocsCanvas canvas, string text)
    {
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, canvas, text))
        {
            RoutedEvent = UIElement.TextInputEvent,
        };
        canvas.RaiseEvent(args);
    }

    [StaFact]
    public void Escape_WithSelection_KeepsSelectedText()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End, shift: true);

        RaiseTextInput(canvas, "\x1b");

        canvas.GetText().Should().Be("hello world");
    }

    [StaFact]
    public void Escape_WithoutSelection_InsertsNothing()
    {
        var canvas = CreateCanvas("hello");
        canvas.TestSetCursor(0, 2);

        RaiseTextInput(canvas, "\x1b");

        canvas.GetText().Should().Be("hello");
    }

    [StaFact]
    public void PrintableText_StillReplacesSelection()
    {
        var canvas = CreateCanvas("hello world");
        canvas.TestSetCursor(0, 0);
        canvas.TestNavigate(Key.End, shift: true);

        RaiseTextInput(canvas, "x");

        canvas.GetText().Should().Be("x");
    }
}
