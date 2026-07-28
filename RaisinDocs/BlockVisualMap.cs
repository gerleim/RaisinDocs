using System.Text;

namespace RaisinDocs;

public readonly record struct HiddenRange(int Start, int Length);

public class BlockVisualSpacing
{
    public double MarkerStartX { get; set; }
    public double MarkerWidth { get; set; }
    public double SpacingAfterMarker { get; set; }
    public double ContentStartX { get; set; }
}

public class BlockVisualMap
{
    public IReadOnlyList<HiddenRange> HiddenRanges { get; }
    public string? ReplacementPrefix { get; }
    public bool IsContinuationIndent { get; }
    public BlockKind PrefixMeasureKind { get; }
    public IReadOnlyList<InlineImage>? Images { get; }
    public IReadOnlyList<InlineLink>? Links { get; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; }
    public BlockVisualSpacing? Spacing { get; }

    public BlockVisualMap(IReadOnlyList<HiddenRange> hiddenRanges, string? replacementPrefix = null,
        bool isContinuationIndent = false, BlockKind prefixMeasureKind = BlockKind.Paragraph,
        IReadOnlyList<InlineImage>? images = null, IReadOnlyList<InlineLink>? links = null,
        IReadOnlyList<ColorSpan>? colorSpans = null, BlockVisualSpacing? spacing = null)
    {
        HiddenRanges = hiddenRanges;
        ReplacementPrefix = replacementPrefix;
        IsContinuationIndent = isContinuationIndent;
        PrefixMeasureKind = prefixMeasureKind;
        Images = images;
        Links = links;
        ColorSpans = colorSpans;
        Spacing = spacing;
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
            if (!forward && rawOffset >= hr.Start && rawOffset < end)
                return hr.Start;
        }
        return rawOffset;
    }

    /// <summary>
    /// Build a parent map for O(1) parent lookup during visual map computation.
    /// Maps each child block to its parent block's index in allBlocks.
    /// </summary>
    public static Dictionary<ParsedBlock, int> BuildParentMap(IReadOnlyList<ParsedBlock> allBlocks)
    {
        var parentMap = new Dictionary<ParsedBlock, int>();
        for (int i = 0; i < allBlocks.Count; i++)
        {
            var block = allBlocks[i];
            if (block.Children != null)
            {
                foreach (var child in block.Children)
                {
                    parentMap[child] = i;
                }
            }
        }
        return parentMap;
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
            BlockKind.UnorderedListItem => nestIndent + "  " + GetBulletChar(nestingLevel) + "  ",
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
        return NestingIndentString(nestingLevel) + "  " + number + delim + "  ";
    }

    public static BlockVisualMap Compute(ParsedBlock parsed, string blockText,
        IReadOnlyList<ParsedBlock>? allBlocks = null, Func<int, string>? getBlockText = null,
        Dictionary<ParsedBlock, int>? parentMap = null, double padding = 0, double listIndent = 0,
        Func<string, BlockKind, double>? measureReplacementPrefix = null)
    {
        var ranges = new List<HiddenRange>();
        string? replacementPrefix = null;

        bool isContinuation = false;
        BlockKind prefixMeasureKind = parsed.Kind;

        // Find parent block from hierarchy (block that has this block as a child)
        ParsedBlock? owner = null;
        string? ownerText = null;
        int ownerIndex = -1;

        if (allBlocks != null && parsed.Children == null)
        {
            // Use parent map for O(1) lookup if available, otherwise search O(n)
            if (parentMap != null && parentMap.TryGetValue(parsed, out int parentIndex))
            {
                owner = allBlocks[parentIndex];
                if (getBlockText != null)
                    ownerText = getBlockText(parentIndex);
                ownerIndex = parentIndex;
            }
            else
            {
                // Fallback to O(n) search if no parent map provided
                for (int i = 0; i < allBlocks.Count; i++)
                {
                    var block = allBlocks[i];
                    if (block.Children != null && block.Children.Contains(parsed))
                    {
                        owner = block;
                        if (getBlockText != null)
                            ownerText = getBlockText(i);
                        ownerIndex = i;
                        break;
                    }
                }
            }
        }

        if (owner != null && ownerText != null)
        {
            if (owner.ContentColumn > 0)
            {
                var (leadChars, cols) = MarkdownParser.MeasureLeadingWhitespace(blockText);

                // Check if this is an indented continuation (has more indentation than owner's content column)
                if (cols >= owner.ContentColumn)
                {
                    int hideChars = MarkdownParser.CharsForColumns(blockText, owner.ContentColumn);
                    ranges.Add(new HiddenRange(0, hideChars));
                }
                // Otherwise it's a lazy continuation - only hide leading spaces if present
                else if (leadChars > 0)
                {
                    ranges.Add(new HiddenRange(0, leadChars));
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
            var stripped = parsed.LeadingSpaces > 0 ? blockText[parsed.LeadingSpaces..] : blockText;
            if (stripped.Length > 0 && (stripped[0] == '-' || stripped[0] == '*'))
            {
                // Hide the marker and all spacing (1-4 spaces) up to content column
                ranges.Add(new HiddenRange(0, parsed.ContentColumn));
                string nestIndent = NestingIndentString(parsed.ListNestingLevel);
                replacementPrefix = nestIndent + "  " + GetBulletChar(parsed.ListNestingLevel) + "  ";
            }
        }
        else if (parsed.Kind == BlockKind.OrderedListItem)
        {
            var stripped = parsed.LeadingSpaces > 0 ? blockText[parsed.LeadingSpaces..] : blockText;
            int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
            if (prefixLen > 0)
            {
                // Hide the marker and all spacing (1-4 spaces) up to content column
                ranges.Add(new HiddenRange(0, parsed.ContentColumn));
                string nestIndent = NestingIndentString(parsed.ListNestingLevel);
                string number = stripped.Substring(0, prefixLen - 2);
                char delim = stripped[prefixLen - 2];
                replacementPrefix = nestIndent + "  " + number + delim + "  ";
            }
        }

        if (parsed.Kind == BlockKind.Blockquote)
        {
            var stripped = parsed.LeadingSpaces > 0 ? blockText[parsed.LeadingSpaces..] : blockText;
            if (stripped.Length > 0 && stripped[0] == '>')
            {
                int hideLen = 1;
                if (stripped.Length > 1 && stripped[1] == ' ')
                    hideLen = 2;
                ranges.Add(new HiddenRange(0, parsed.LeadingSpaces + hideLen));
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
            // Check if it's a backslash or trailing spaces
            if (effectiveEnd > 0 && blockText[effectiveEnd - 1] == '\\')
            {
                // Backslash hard break - hide just the backslash
                ranges.Add(new HiddenRange(effectiveEnd - 1, 1));
            }
            else if (parsed.Kind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine && effectiveEnd >= 2
                     && blockText[effectiveEnd - 1] == ' ' && blockText[effectiveEnd - 2] == ' ')
            {
                // Trailing spaces hard break - hide all trailing spaces
                int trailStart = effectiveEnd;
                while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
                ranges.Add(new HiddenRange(trailStart, effectiveEnd - trailStart));
            }
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

        // Merge overlapping ranges (e.g., color tags found by both color tag and HTML comment finders)
        var merged = new List<HiddenRange>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0)
            {
                merged.Add(range);
            }
            else
            {
                var last = merged[^1];
                // Check if current range overlaps with or is adjacent to the last merged range
                if (range.Start <= last.Start + last.Length)
                {
                    // Merge: extend the last range to cover both
                    int newEnd = Math.Max(last.Start + last.Length, range.Start + range.Length);
                    merged[^1] = new HiddenRange(last.Start, newEnd - last.Start);
                }
                else
                {
                    // No overlap, add as new range
                    merged.Add(range);
                }
            }
        }
        var deduped = merged;

        // Compute visual spacing if measure function is provided
        BlockVisualSpacing? spacing = null;
        if (measureReplacementPrefix != null && padding > 0)
        {
            spacing = ComputeSpacing(parsed, replacementPrefix, prefixMeasureKind, padding, listIndent, measureReplacementPrefix);
        }

        return new BlockVisualMap(deduped, replacementPrefix, isContinuation, prefixMeasureKind,
            parsed.Images, parsed.Links, parsed.ColorSpans, spacing);
    }

    private static BlockVisualSpacing ComputeSpacing(ParsedBlock parsed, string? replacementPrefix,
        BlockKind prefixMeasureKind, double padding, double listIndent,
        Func<string, BlockKind, double> measureReplacementPrefix)
    {
        var spacing = new BlockVisualSpacing();
        var aligner = new ContentBlockAligner(padding, listIndent);

        if (parsed.Kind == BlockKind.Blockquote)
        {
            spacing.MarkerStartX = aligner.GetBlockquoteBarX();
            spacing.MarkerWidth = 3; // blockquote bar width
            spacing.SpacingAfterMarker = 8;
            spacing.ContentStartX = aligner.GetBlockquoteContentIndentX();
        }
        else if (parsed.Kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem or
                 BlockKind.TaskListItemChecked or BlockKind.TaskListItemUnchecked)
        {
            double listNestingOffset = parsed.ListNestingLevel * aligner.GetBlockIndentWidth();
            spacing.ContentStartX = aligner.CalculateContentStartX(listNestingOffset);
            spacing.MarkerStartX = padding;
            spacing.MarkerWidth = replacementPrefix != null ? measureReplacementPrefix(replacementPrefix, prefixMeasureKind) : 0;
            spacing.SpacingAfterMarker = 0;
        }
        else
        {
            spacing.MarkerStartX = padding;
            spacing.MarkerWidth = 0;
            spacing.SpacingAfterMarker = 0;
            spacing.ContentStartX = padding;
        }

        return spacing;
    }

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }
}
