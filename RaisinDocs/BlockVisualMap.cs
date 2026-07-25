using System.Text;

namespace RaisinDocs;

public readonly record struct HiddenRange(int Start, int Length);

public class BlockVisualMap
{
    public IReadOnlyList<HiddenRange> HiddenRanges { get; }
    public string? ReplacementPrefix { get; }
    public bool IsContinuationIndent { get; }
    public BlockKind PrefixMeasureKind { get; }
    public IReadOnlyList<InlineImage>? Images { get; }
    public IReadOnlyList<InlineLink>? Links { get; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; }

    public BlockVisualMap(IReadOnlyList<HiddenRange> hiddenRanges, string? replacementPrefix = null,
        bool isContinuationIndent = false, BlockKind prefixMeasureKind = BlockKind.Paragraph,
        IReadOnlyList<InlineImage>? images = null, IReadOnlyList<InlineLink>? links = null,
        IReadOnlyList<ColorSpan>? colorSpans = null)
    {
        HiddenRanges = hiddenRanges;
        ReplacementPrefix = replacementPrefix;
        IsContinuationIndent = isContinuationIndent;
        PrefixMeasureKind = prefixMeasureKind;
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

    internal const int SpacesPerNestingLevel = 4;
    private static readonly char[] BulletChars = ['●', '○', '■'];

    internal static char GetBulletChar(int nestingLevel) =>
        BulletChars[nestingLevel % BulletChars.Length];

    internal static string NestingIndentString(int nestingLevel) =>
        nestingLevel <= 0 ? "" : new string(' ', nestingLevel * SpacesPerNestingLevel);

    internal static string? GetOwnerVisualPrefix(BlockKind kind, string blockText, int leadingSpaces = 0,
        int nestingLevel = 0)
    {
        string nestIndent = NestingIndentString(nestingLevel);
        return kind switch
        {
            BlockKind.UnorderedListItem => nestIndent + "  " + GetBulletChar(nestingLevel) + " ",
            BlockKind.TaskListItemUnchecked => nestIndent + "  ☐ ",
            BlockKind.TaskListItemChecked => nestIndent + "  ☑ ",
            BlockKind.OrderedListItem => GetOrderedListVisualPrefix(blockText, leadingSpaces, nestingLevel),
            _ => null,
        };
    }

    private static string? GetOrderedListVisualPrefix(string blockText, int leadingSpaces = 0,
        int nestingLevel = 0)
    {
        var text = leadingSpaces > 0 ? blockText[leadingSpaces..] : blockText;
        int prefixLen = MarkdownParser.GetOrderedListPrefixLength(text);
        if (prefixLen <= 0) return null;
        string number = text.Substring(0, prefixLen - 2);
        char delim = text[prefixLen - 2];
        return NestingIndentString(nestingLevel) + "  " + number + delim + " ";
    }

    public static BlockVisualMap Compute(ParsedBlock parsed, string blockText,
        IReadOnlyList<ParsedBlock>? allBlocks = null, Func<int, string>? getBlockText = null)
    {
        var ranges = new List<HiddenRange>();
        string? replacementPrefix = null;

        bool isContinuation = false;
        BlockKind prefixMeasureKind = parsed.Kind;
        if ((parsed.IsLazyContinuation || parsed.IsIndentedContinuation)
            && parsed.OwnerBlock >= 0 && allBlocks != null && getBlockText != null)
        {
            var owner = allBlocks[parsed.OwnerBlock];
            string ownerText = getBlockText(parsed.OwnerBlock);

            if (owner.ContentColumn > 0)
            {
                var (leadChars, cols) = MarkdownParser.MeasureLeadingWhitespace(blockText);
                if (parsed.IsIndentedContinuation && cols >= owner.ContentColumn)
                {
                    int hideChars = MarkdownParser.CharsForColumns(blockText, owner.ContentColumn);
                    ranges.Add(new HiddenRange(0, hideChars));
                }
                else if (parsed.IsLazyContinuation && leadChars > 0)
                {
                    int hideChars = cols <= owner.ContentColumn
                        ? leadChars
                        : MarkdownParser.CharsForColumns(blockText, owner.ContentColumn);
                    ranges.Add(new HiddenRange(0, hideChars));
                }
            }

            replacementPrefix = GetOwnerVisualPrefix(owner.Kind, ownerText, owner.LeadingSpaces,
                owner.ListNestingLevel);
            isContinuation = replacementPrefix != null;
            if (isContinuation)
                prefixMeasureKind = owner.Kind;
        }
        else if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
        {
            int ls = parsed.LeadingSpaces;
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            if (stripped.Length > 0 && stripped[0] == '#')
            {
                int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
                int prefixLen = hashCount + 1;
                if (blockText.Length >= ls + prefixLen)
                    ranges.Add(new HiddenRange(0, ls + prefixLen));
            }
        }
        else if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
        {
            int ls = parsed.LeadingSpaces;
            if (blockText.Length >= ls + 6)
            {
                ranges.Add(new HiddenRange(0, ls + 6));
                string nestIndent = NestingIndentString(parsed.ListNestingLevel);
                replacementPrefix = nestIndent + (parsed.Kind == BlockKind.TaskListItemChecked ? "  ☑ " : "  ☐ ");
            }
        }
        else if (parsed.Kind == BlockKind.UnorderedListItem)
        {
            int ls = parsed.LeadingSpaces;
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            if (stripped.StartsWith("- ") || stripped.StartsWith("* "))
            {
                ranges.Add(new HiddenRange(0, ls + 2));
                string nestIndent = NestingIndentString(parsed.ListNestingLevel);
                replacementPrefix = nestIndent + "  " + GetBulletChar(parsed.ListNestingLevel) + " ";
            }
        }
        else if (parsed.Kind == BlockKind.OrderedListItem)
        {
            int ls = parsed.LeadingSpaces;
            var stripped = ls > 0 ? blockText[ls..] : blockText;
            int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
            if (prefixLen > 0)
            {
                ranges.Add(new HiddenRange(0, ls + prefixLen));
                string nestIndent = NestingIndentString(parsed.ListNestingLevel);
                string number = stripped.Substring(0, prefixLen - 2);
                char delim = stripped[prefixLen - 2];
                replacementPrefix = nestIndent + "  " + number + delim + " ";
            }
        }

        if (parsed.Kind == BlockKind.IndentedCodeLine && blockText.Length > 0)
        {
            int hideChars = MarkdownParser.CharsForColumns(blockText, 4);
            if (hideChars > 0 && hideChars <= blockText.Length)
                ranges.Add(new HiddenRange(0, hideChars));
        }

        if (parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow && parsed.TableRow != null)
        {
            int prev = 0;
            foreach (var cell in parsed.TableRow.Cells)
            {
                var (contentStart, contentEnd) = cell.TrimContent(blockText);

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

            if (run.Style is InlineStyle.Code)
            {
                int markerLen = CountBackticks(blockText, run.Start);
                if (markerLen == 0) continue;
                int runEnd = run.Start + run.Length;
                ranges.Add(new HiddenRange(run.Start, markerLen));
                ranges.Add(new HiddenRange(runEnd - markerLen, markerLen));
            }
        }

        if (parsed.EmphasisMarkers != null)
        {
            foreach (var marker in parsed.EmphasisMarkers)
                ranges.Add(new HiddenRange(marker.Start, marker.Length));
        }

        int effectiveEnd = MarkdownParser.GetContentEnd(blockText);
        if (MarkdownParser.IsTrailingHardBreak(parsed, blockText))
        {
            ranges.Add(new HiddenRange(effectiveEnd - 1, 1));
        }
        else if (parsed.Kind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine && effectiveEnd >= 2
                 && blockText[effectiveEnd - 1] == ' ' && blockText[effectiveEnd - 2] == ' ')
        {
            int trailStart = effectiveEnd;
            while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
            ranges.Add(new HiddenRange(trailStart, effectiveEnd - trailStart));
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
                if (link.IsAngleBracket)
                {
                    ranges.Add(new HiddenRange(link.Start, 1));
                    ranges.Add(new HiddenRange(link.Start + link.Length - 1, 1));
                    continue;
                }
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

        var htmlCommentRanges = MarkdownParser.FindHtmlCommentRanges(blockText);
        if (htmlCommentRanges != null)
        {
            foreach (var comment in htmlCommentRanges)
                ranges.Add(comment);
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        return new BlockVisualMap(ranges, replacementPrefix, isContinuation, prefixMeasureKind,
            parsed.Images, parsed.Links, parsed.ColorSpans);
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }
}
