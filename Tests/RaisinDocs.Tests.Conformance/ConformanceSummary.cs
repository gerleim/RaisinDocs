using System.Text.Json;
using RaisinDocs.Html;
using Xunit;

namespace RaisinDocs.Tests.Conformance;

public class ConformanceSummary
{
    [Fact]
    public void PrintSectionSummary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec.json");
        var json = File.ReadAllText(path);
        var examples = JsonSerializer.Deserialize<List<SpecExample>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        var sections = new Dictionary<string, (int pass, int fail)>();

        foreach (var ex in examples)
        {
            try
            {
                var actual = HtmlEmitter.Render(ex.Markdown);
                if (!sections.ContainsKey(ex.Section)) sections[ex.Section] = (0, 0);
                if (actual == ex.Html)
                    sections[ex.Section] = (sections[ex.Section].pass + 1, sections[ex.Section].fail);
                else
                    sections[ex.Section] = (sections[ex.Section].pass, sections[ex.Section].fail + 1);
            }
            catch
            {
                if (!sections.ContainsKey(ex.Section)) sections[ex.Section] = (0, 0);
                sections[ex.Section] = (sections[ex.Section].pass, sections[ex.Section].fail + 1);
            }
        }

        var lines = new List<string>();
        lines.Add($"{"Section",-35} {"Pass",5} {"Fail",5} {"Total",5} {"Rate",6}");
        lines.Add(new string('-', 60));
        int totalPass = 0, totalFail = 0;
        foreach (var (section, (pass, fail)) in sections.OrderBy(x => x.Key))
        {
            int total = pass + fail;
            double rate = total > 0 ? 100.0 * pass / total : 0;
            lines.Add($"{section,-35} {pass,5} {fail,5} {total,5} {rate,5:F0}%");
            totalPass += pass;
            totalFail += fail;
        }
        lines.Add(new string('-', 60));
        lines.Add($"{"TOTAL",-35} {totalPass,5} {totalFail,5} {totalPass + totalFail,5} {100.0 * totalPass / (totalPass + totalFail),5:F0}%");

        var report = string.Join("\n", lines);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "conformance-report.txt"), report);
    }
}
