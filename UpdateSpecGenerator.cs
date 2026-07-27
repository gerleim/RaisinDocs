// Utility to regenerate spec_with_text.json using actual DocsCanvas rendering
// Run with: dotnet run --project UpdateSpecGenerator.csproj

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public class SpecExample
{
    [JsonPropertyName("markdown")]
    public string Markdown { get; set; } = "";

    [JsonPropertyName("html")]
    public string Html { get; set; } = "";

    [JsonPropertyName("example")]
    public int Example { get; set; }

    [JsonPropertyName("start_line")]
    public int StartLine { get; set; }

    [JsonPropertyName("end_line")]
    public int EndLine { get; set; }

    [JsonPropertyName("section")]
    public string Section { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

class Program
{
    static void Main(string[] args)
    {
        var specPath = "Tests/RaisinDocs.Tests.UI/bin/Release/net8.0-windows/spec_with_text.json";

        if (!File.Exists(specPath))
        {
            Console.WriteLine($"Error: {specPath} not found");
            return;
        }

        // Read the spec
        var json = File.ReadAllText(specPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var spec = JsonSerializer.Deserialize<List<SpecExample>>(json, options) ?? [];

        Console.WriteLine($"Loaded {spec.Count} test cases");

        // Count cases we can generate
        int generated = 0;
        var casesWithText = spec.Where(x => !string.IsNullOrEmpty(x.Text)).ToList();
        Console.WriteLine($"{casesWithText.Count} cases already have text field");

        // For now, just show the format we need
        Console.WriteLine("\nExample spec entries:");
        foreach (var ex in spec.Take(5))
        {
            Console.WriteLine($"\nExample {ex.Example}: {ex.Section}");
            Console.WriteLine($"  Markdown: {JsonSerializer.Serialize(ex.Markdown)}");
            Console.WriteLine($"  Current text: {ex.Text}");
            Console.WriteLine($"  Needs regeneration: {string.IsNullOrEmpty(ex.Text)}");
        }

        Console.WriteLine("\n" +
            "To regenerate all text fields, we need to parse each markdown with DocsCanvas.\n" +
            "This requires running in WPF context. Use the test infrastructure instead.");
    }
}
