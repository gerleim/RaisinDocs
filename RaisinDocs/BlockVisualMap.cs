using System.Text;

namespace RaisinDocs;

public readonly record struct HiddenRange(int Start, int Length);

public class BlockVisualMap
{
    public IReadOnlyList<HiddenRange> HiddenRanges { get; }
    public string? ReplacementPrefix { get; }

    public BlockVisualMap(IReadOnlyList<HiddenRange> hiddenRanges, string? replacementPrefix = null)
    {
        HiddenRanges = hiddenRanges;
        ReplacementPrefix = replacementPrefix;
    }

    public bool IsHidden(int rawOffset)
    {
        foreach (var hr in HiddenRanges)
        {
            if (rawOffset < hr.Start) return false;
            if (rawOffset < hr.Start + hr.Length) return true;
        }
        return false;
    }

    public int RawToVisual(int rawOffset)
    {
        int visualOffset = rawOffset;
        foreach (var hr in HiddenRanges)
        {
            if (rawOffset <= hr.Start) break;
            if (rawOffset < hr.Start + hr.Length)
            {
                visualOffset -= (rawOffset - hr.Start);
                break;
            }
            visualOffset -= hr.Length;
        }
        if (ReplacementPrefix != null)
            visualOffset += ReplacementPrefix.Length;
        return visualOffset;
    }

    public int VisualToRaw(int visualOffset)
    {
        if (ReplacementPrefix != null)
            visualOffset -= ReplacementPrefix.Length;

        int accumulated = 0;
        foreach (var hr in HiddenRanges)
        {
            if (visualOffset + accumulated < hr.Start)
                break;
            accumulated += hr.Length;
        }
        return visualOffset + accumulated;
    }

    public string BuildDisplayString(string rawText, int start, int length)
    {
        var sb = new StringBuilder();
        for (int i = start; i < start + length; i++)
        {
            if (!IsHidden(i))
                sb.Append(rawText[i]);
        }
        return sb.ToString();
    }

    public int SkipHidden(int rawOffset, bool forward)
    {
        foreach (var hr in HiddenRanges)
        {
            if (rawOffset > hr.Start && rawOffset < hr.Start + hr.Length)
                return forward ? hr.Start + hr.Length : hr.Start;
        }
        return rawOffset;
    }

    public static BlockVisualMap Compute(ParsedBlock parsed, string blockText)
    {
        var ranges = new List<HiddenRange>();
        string? replacementPrefix = null;

        if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
        {
            int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
            int prefixLen = hashCount + 1;
            if (blockText.Length >= prefixLen)
                ranges.Add(new HiddenRange(0, prefixLen));
        }
        else if (parsed.Kind == BlockKind.UnorderedListItem)
        {
            if (blockText.StartsWith("- ") || blockText.StartsWith("* "))
            {
                ranges.Add(new HiddenRange(0, 2));
                replacementPrefix = "  • ";
            }
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal) continue;

            int markerLen = run.Style switch
            {
                InlineStyle.BoldItalic => 3,
                InlineStyle.Bold => 2,
                InlineStyle.Italic => 1,
                InlineStyle.Code => CountBackticks(blockText, run.Start),
                _ => 0,
            };
            if (markerLen == 0) continue;

            int runEnd = run.Start + run.Length;
            ranges.Add(new HiddenRange(run.Start, markerLen));
            ranges.Add(new HiddenRange(runEnd - markerLen, markerLen));
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        return new BlockVisualMap(ranges, replacementPrefix);
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }
}
