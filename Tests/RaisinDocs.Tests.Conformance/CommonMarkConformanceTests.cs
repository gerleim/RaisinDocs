using System.Text.Json;
using FluentAssertions;
using RaisinDocs.Html;
using Xunit;

namespace RaisinDocs.Tests.Conformance;

public class CommonMarkConformanceTests
{
    static readonly Lazy<List<SpecExample>> _examples = new(LoadExamples);

    public static IEnumerable<object[]> SpecExamples()
    {
        foreach (var ex in _examples.Value)
            yield return [ex.Example, ex.Section, ex.Markdown, ex.Html];
    }

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void CommonMark_Spec(int example, string section, string markdown, string expectedHtml)
    {
        var actual = HtmlEmitter.Render(markdown, new HtmlEmitterOptions { GfmExtensions = false });
        actual.Should().Be(expectedHtml, because: $"example {example} in section '{section}' should match");
    }

    static List<SpecExample> LoadExamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SpecExample>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }
}

public class SpecExample
{
    public string Markdown { get; set; } = "";
    public string Html { get; set; } = "";
    public int Example { get; set; }
    public string Section { get; set; } = "";
    public int Start_line { get; set; }
    public int End_line { get; set; }
}
