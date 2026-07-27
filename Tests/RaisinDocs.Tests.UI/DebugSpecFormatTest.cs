using System;
using System.Collections.Generic;
using System.Windows;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Debug test to show what format we're actually producing.
/// </summary>
public class DebugSpecFormatTest
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;
    private readonly ITestOutputHelper _output;

    public DebugSpecFormatTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [StaFact]
    [Trait("Category", "Debug")]
    public void Show_Format_For_Example_1()
    {
        var markdown = "\tfoo\tbaz\t\tbim\n";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var blocks = canvas.TestGetVisualBlockInfos();
        _output.WriteLine($"Example 1 produced {blocks.Length} blocks:");
        foreach (var block in blocks)
        {
            _output.WriteLine($"  Block: Kind={block.Kind}, VisualText={Escape(block.VisualText)}");
        }

        var actualText = ExtractVisualText(canvas);
        _output.WriteLine($"Final output: {Escape(actualText)}");
    }

    [StaFact]
    [Trait("Category", "Debug")]
    public void Show_Format_For_Example_367()
    {
        var markdown = "*foo bar\n*\n";
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var blocks = canvas.TestGetVisualBlockInfos();
        _output.WriteLine($"Example 367 produced {blocks.Length} blocks:");
        foreach (var block in blocks)
        {
            _output.WriteLine($"  Block: Kind={block.Kind}, RawText={Escape(block.RawText)}, VisualText={Escape(block.VisualText)}");
        }

        var actualText = ExtractVisualText(canvas);
        _output.WriteLine($"Final output: {Escape(actualText)}");
    }

    private static string ExtractVisualText(DocsCanvas canvas)
    {
        var blocks = canvas.TestGetVisualBlockInfos();
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            var text = block.VisualText;

            if (string.IsNullOrEmpty(text))
                continue;
            if (block.Kind == BlockKind.HtmlBlock && block.CreateVisualSeparation)
                continue;

            if (result.Length > 0)
                result.Append('\n');
            result.Append(text);
        }

        return result.ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "\"\"";

        var result = new System.Text.StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '\n':
                    result.Append("\\n");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                case '"':
                    result.Append("\\\"");
                    break;
                case '\\':
                    result.Append("\\\\");
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }
        result.Append("\"");
        return result.ToString();
    }
}
