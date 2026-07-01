using System.Text;

namespace RaisinDocs;

public readonly record struct HiddenRange(int Start, int Length);

public class BlockVisualMap
{
    public IReadOnlyList<HiddenRange> HiddenRanges { get; }
    public string? ReplacementPrefix { get; }
    public IReadOnlyList<InlineImage>? Images { get; }
    public IReadOnlyList<InlineLink>? Links { get; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; }

    public BlockVisualMap(IReadOnlyList<HiddenRange> hiddenRanges, string? replacementPrefix = null,
        IReadOnlyList<InlineImage>? images = null, IReadOnlyList<InlineLink>? links = null,
        IReadOnlyList<ColorSpan>? colorSpans = null)
    {
        HiddenRanges = hiddenRanges;
        ReplacementPrefix = replacementPrefix;
        Images = images;
        Links = links;
        ColorSpans = colorSpans;
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
            int end = hr.Start + hr.Length;
            if (forward && rawOffset >= hr.Start && rawOffset < end)
                return end;
            if (!forward && rawOffset > hr.Start && rawOffset < end)
                return hr.Start;
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
        else if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
        {
            if (blockText.Length >= 6)
            {
                ranges.Add(new HiddenRange(0, 6));
                replacementPrefix = parsed.Kind == BlockKind.TaskListItemChecked ? "  ☑ " : "  ☐ ";
            }
        }
        else if (parsed.Kind == BlockKind.UnorderedListItem)
        {
            if (blockText.StartsWith("- ") || blockText.StartsWith("* "))
            {
                ranges.Add(new HiddenRange(0, 2));
                replacementPrefix = "  • ";
            }
        }
        else if (parsed.Kind == BlockKind.OrderedListItem)
        {
            int prefixLen = MarkdownParser.GetOrderedListPrefixLength(blockText);
            if (prefixLen > 0)
            {
                ranges.Add(new HiddenRange(0, prefixLen));
                string number = blockText.Substring(0, prefixLen - 2);
                char delim = blockText[prefixLen - 2];
                replacementPrefix = "  " + number + delim + " ";
            }
        }

        if (parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow && parsed.TableRow != null)
        {
            int prev = 0;
            foreach (var cell in parsed.TableRow.Cells)
            {
                int contentStart = cell.Start;
                int contentEnd = cell.Start + cell.Length;
                while (contentStart < contentEnd && blockText[contentStart] == ' ') contentStart++;
                while (contentEnd > contentStart && blockText[contentEnd - 1] == ' ') contentEnd--;

                if (contentStart > prev)
                    ranges.Add(new HiddenRange(prev, contentStart - prev));
                prev = contentEnd;
            }
            if (prev < blockText.Length)
                ranges.Add(new HiddenRange(prev, blockText.Length - prev));
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal) continue;

            if (run.Style is InlineStyle.Image or InlineStyle.Link) continue;

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

        if (MarkdownParser.IsTrailingHardBreak(parsed, blockText))
        {
            ranges.Add(new HiddenRange(blockText.Length - 1, 1));
        }
        else if (parsed.Kind != BlockKind.FencedCodeLine && blockText.EndsWith("  "))
        {
            int trailStart = blockText.Length;
            while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
            ranges.Add(new HiddenRange(trailStart, blockText.Length - trailStart));
        }

        if (parsed.Images != null)
        {
            foreach (var img in parsed.Images)
                ranges.Add(new HiddenRange(img.Start, img.Length));
        }

        if (parsed.Links != null)
        {
            foreach (var link in parsed.Links)
            {
                if (link.Text == link.Url) continue;
                ranges.Add(new HiddenRange(link.Start, 1));
                int closeBracket = link.Start + 1 + link.Text.Length;
                ranges.Add(new HiddenRange(closeBracket, link.Start + link.Length - closeBracket));
            }
        }

        var colorTagRanges = MarkdownParser.FindInlineColorTagRanges(blockText);
        if (colorTagRanges != null)
        {
            foreach (var tag in colorTagRanges)
                ranges.Add(tag);
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        return new BlockVisualMap(ranges, replacementPrefix, parsed.Images, parsed.Links, parsed.ColorSpans);
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }
}
