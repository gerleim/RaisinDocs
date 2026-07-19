using System.Text.Json;
using RaisinDocs.Html;
using Xunit;

namespace RaisinDocs.Tests.Conformance;

public class FailureAnalysis
{
    [Fact]
    public void DumpFailures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec.json");
        var json = File.ReadAllText(path);
        var examples = JsonSerializer.Deserialize<List<SpecExample>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        var lines = new List<string>();
        foreach (var ex in examples)
        {
            try
            {
                var actual = HtmlEmitter.Render(ex.Markdown);
                if (actual != ex.Html)
                {
                    lines.Add($"=== Example {ex.Example} [{ex.Section}] ===");
                    lines.Add($"MD:  {Escape(ex.Markdown)}");
                    lines.Add($"EXP: {Escape(ex.Html)}");
                    lines.Add($"GOT: {Escape(actual)}");
                    lines.Add("");
                }
            }
            catch (Exception e)
            {
                lines.Add($"=== Example {ex.Example} [{ex.Section}] === EXCEPTION");
                lines.Add($"MD:  {Escape(ex.Markdown)}");
                lines.Add($"ERR: {e.Message}");
                lines.Add("");
            }
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "failures.txt"), string.Join("\n", lines));
    }

static string Escape(string s) => s.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");
}
