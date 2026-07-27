using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Xunit;
using Xunit.Abstractions;

namespace RaisinDocs.Tests.UI;

/// <summary>
/// Test utility to regenerate spec_with_text.json with correct format.
/// Run this once to update the spec file.
/// </summary>
public class GenerateSpecJsonTest
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;
    private readonly ITestOutputHelper _output;

    public GenerateSpecJsonTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [StaFact]
    [Trait("Category", "SpecGeneration")]
    public void RegenerateSpecWithCorrectFormat()
    {
        var specPath = Path.Combine(AppContext.BaseDirectory, "spec_with_text.json");
        var json = File.ReadAllText(specPath);
        var examples = JsonSerializer.Deserialize<List<SpecExample>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        _output.WriteLine($"Loaded {examples.Count} test cases");

        int generated = 0;
        foreach (var example in examples)
        {
            try
            {
                var text = ExtractVisualText(example.Markdown);
                example.Text = text;
                generated++;

                if (generated % 50 == 0)
                    _output.WriteLine($"Generated {generated}/{examples.Count}...");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error on example {example.Example}: {ex.Message}");
            }
        }

        _output.WriteLine($"Generated {generated} test cases");

        // Save back to file with formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var updatedJson = JsonSerializer.Serialize(examples, options);
        File.WriteAllText(specPath, updatedJson);

        _output.WriteLine($"Updated spec saved to {specPath}");
    }

    private string ExtractVisualText(string markdown)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

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

            // Skip bare block type indicators with no content (e.g., [PARA] with no colon)
            if (text.StartsWith("[") && text.EndsWith("]") && !text.Contains(":"))
                continue;

            // For blockquotes, hide the > marker if present at start
            if (text.StartsWith("[QUOTE: >"))
            {
                text = "[QUOTE: " + text.Substring("[QUOTE: >".Length);
            }

            if (result.Length > 0)
                result.Append('\n');
            result.Append(text);
        }

        return result.ToString();
    }
}
