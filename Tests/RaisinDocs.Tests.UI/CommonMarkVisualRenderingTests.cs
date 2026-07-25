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
    /// Normalizes visual glyphs (●, ○, ■) back to markdown markers (-) for comparison.
    /// Uses soft break (space) within same block type (paragraph continuation),
    /// or hard break (newline) between different block types.
    /// </summary>
    private static string ExtractVisualText(DocsCanvas canvas)
    {
        var blocks = canvas.TestGetVisualBlockInfos();
        var result = new System.Text.StringBuilder();
        int lastNonEmptyIndex = -1;

        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            var text = block.VisualText;

            if (string.IsNullOrEmpty(text))
                continue;

            // Skip HTML comment blocks (not visible in rendering)
            if (text.TrimStart().StartsWith("<!--"))
                continue;

            if (lastNonEmptyIndex >= 0)
            {
                var prevBlock = blocks[lastNonEmptyIndex];
                // Use space for soft break within same logical block (both Paragraph)
                // Use newline for hard break between different block types
                bool isSoftBreak = block.Kind == BlockKind.Paragraph && prevBlock.Kind == BlockKind.Paragraph;
                result.Append(isSoftBreak ? ' ' : '\n');
            }

            // Normalize visual glyphs back to markdown markers for comparison
            text = text.Replace("●", "-").Replace("○", "-").Replace("■", "-");
            result.Append(text.TrimEnd());
            lastNonEmptyIndex = i;
        }

        return result.ToString();
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
