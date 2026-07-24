using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace RaisinDocs.Tests.UI;

public class CommonMarkVisualRenderingTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    static readonly Lazy<List<SpecTextExample>> _examples = new(LoadExamples);

    public static IEnumerable<object[]> SpecTextExamples()
    {
        foreach (var ex in _examples.Value)
            yield return [ex.Example, ex.Section, ex.Markdown, ex.Text];
    }

    [StaTheory]
    [MemberData(nameof(SpecTextExamples))]
    public void Visual_TextRendering_MatchesSpec(int example, string section, string markdown, string expectedText)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(markdown);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();

        var actualText = ExtractVisualText(canvas);
        actualText.Should().Be(expectedText,
            because: $"example {example} in section '{section}' visual rendering should match text output");
    }

    /// <summary>
    /// Extract visible text from visual mode rendering.
    /// Joins block text from all blocks, normalizing whitespace.
    /// </summary>
    private static string ExtractVisualText(DocsCanvas canvas)
    {
        var lines = new List<string>();

        for (int i = 0; i < canvas.TestBlockCount; i++)
        {
            var blockText = canvas.TestGetBlockText(i);
            if (!string.IsNullOrEmpty(blockText))
            {
                lines.Add(blockText.TrimEnd());
            }
        }

        // Join blocks with newlines
        var result = string.Join("\n", lines);

        // Remove trailing blank lines
        while (result.EndsWith("\n"))
            result = result.Substring(0, result.Length - 1);

        return result;
    }

    static List<SpecTextExample> LoadExamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec_with_text.json");
        var json = File.ReadAllText(path);
        var allExamples = JsonSerializer.Deserialize<List<SpecExample>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        // Only return examples that have a text field (testable cases)
        return allExamples
            .Where(ex => !string.IsNullOrEmpty(ex.Text))
            .Select(ex => new SpecTextExample
            {
                Example = ex.Example,
                Section = ex.Section,
                Markdown = ex.Markdown,
                Text = ex.Text ?? "",
                StartLine = ex.Start_line,
                EndLine = ex.End_line
            })
            .ToList();
    }
}

public class SpecExample
{
    public string Markdown { get; set; } = "";
    public string Html { get; set; } = "";
    public string? Text { get; set; }
    public int Example { get; set; }
    public string Section { get; set; } = "";
    public int Start_line { get; set; }
    public int End_line { get; set; }
}

public class SpecTextExample
{
    public string Markdown { get; set; } = "";
    public string Text { get; set; } = "";
    public int Example { get; set; }
    public string Section { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}
