namespace RaisinDocs;

public enum InlineStyle
{
    Normal,
    Bold,
    Italic,
    BoldItalic,
    Code,
}

public readonly record struct StyledRun(int Start, int Length, InlineStyle Style);

public enum BlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    UnorderedListItem,
    FencedCodeLine,
}

public class ParsedBlock
{
    public required BlockKind Kind { get; init; }
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public bool IsFenceDelimiter { get; init; }
}

public static class MarkdownParser
{
    public static List<ParsedBlock> Parse(Func<int, string> getBlockText, int blockCount)
    {
        var result = new List<ParsedBlock>(blockCount);
        bool insideFence = false;

        for (int i = 0; i < blockCount; i++)
        {
            string text = getBlockText(i);

            if (IsFenceLine(text))
            {
                insideFence = !insideFence;
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.FencedCodeLine,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                    IsFenceDelimiter = true,
                });
                continue;
            }

            if (insideFence)
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.FencedCodeLine,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            var kind = ClassifyBlock(text);
            var runs = kind == BlockKind.FencedCodeLine
                ? [new StyledRun(0, text.Length, InlineStyle.Normal)]
                : ParseInlines(text);

            result.Add(new ParsedBlock { Kind = kind, Runs = runs });
        }

        return result;
    }

    public static bool IsFenceLine(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("```");
    }

    internal static BlockKind ClassifyBlock(string text)
    {
        if (text.StartsWith("######") && text.Length > 6 && text[6] == ' ')
            return BlockKind.Heading6;
        if (text.StartsWith("#####") && text.Length > 5 && text[5] == ' ')
            return BlockKind.Heading5;
        if (text.StartsWith("####") && text.Length > 4 && text[4] == ' ')
            return BlockKind.Heading4;
        if (text.StartsWith("###") && text.Length > 3 && text[3] == ' ')
            return BlockKind.Heading3;
        if (text.StartsWith("##") && text.Length > 2 && text[2] == ' ')
            return BlockKind.Heading2;
        if (text.StartsWith("#") && text.Length > 1 && text[1] == ' ')
            return BlockKind.Heading1;

        if (text.StartsWith("- ") || text.StartsWith("* "))
            return BlockKind.UnorderedListItem;

        return BlockKind.Paragraph;
    }

    internal static List<StyledRun> ParseInlines(string text)
    {
        if (text.Length == 0)
            return [new StyledRun(0, 0, InlineStyle.Normal)];

        var styles = new InlineStyle[text.Length];

        MarkCodeSpans(text, styles);
        MarkEmphasis(text, styles);

        return BuildRuns(styles);
    }

    private static void MarkCodeSpans(string text, InlineStyle[] styles)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                int backtickCount = 0;
                int start = i;
                while (i < text.Length && text[i] == '`') { backtickCount++; i++; }

                int closeStart = FindClosingBackticks(text, i, backtickCount);
                if (closeStart >= 0)
                {
                    for (int j = start; j < closeStart + backtickCount; j++)
                        styles[j] = InlineStyle.Code;
                    i = closeStart + backtickCount;
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static int FindClosingBackticks(string text, int searchFrom, int count)
    {
        for (int i = searchFrom; i <= text.Length - count; i++)
        {
            if (text[i] == '`')
            {
                int run = 0;
                int start = i;
                while (i < text.Length && text[i] == '`') { run++; i++; }
                if (run == count) return start;
            }
        }
        return -1;
    }

    private static void MarkEmphasis(string text, InlineStyle[] styles)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (styles[i] != InlineStyle.Normal || text[i] != '*')
            {
                i++;
                continue;
            }

            int starCount = 0;
            int openStart = i;
            while (i < text.Length && text[i] == '*' && styles[i] == InlineStyle.Normal)
            {
                starCount++;
                i++;
            }

            int searchFrom = openStart + starCount;

            if (starCount >= 3)
            {
                int closeStart = FindClosingStars(text, styles, searchFrom, 3);
                if (closeStart >= 0)
                {
                    for (int j = openStart; j < closeStart + 3; j++)
                        if (styles[j] == InlineStyle.Normal)
                            styles[j] = InlineStyle.BoldItalic;
                    i = closeStart + 3;
                    continue;
                }
            }

            if (starCount >= 2)
            {
                int closeStart = FindClosingStars(text, styles, searchFrom, 2);
                if (closeStart >= 0)
                {
                    for (int j = openStart; j < closeStart + 2; j++)
                        if (styles[j] == InlineStyle.Normal)
                            styles[j] = InlineStyle.Bold;
                    i = closeStart + 2;
                    continue;
                }
            }

            if (starCount >= 1)
            {
                int closeStart = FindClosingStars(text, styles, searchFrom, 1);
                if (closeStart >= 0)
                {
                    for (int j = openStart; j < closeStart + 1; j++)
                        if (styles[j] == InlineStyle.Normal)
                            styles[j] = InlineStyle.Italic;
                    i = closeStart + 1;
                    continue;
                }
            }
        }
    }

    private static int FindClosingStars(string text, InlineStyle[] styles, int searchFrom, int count)
    {
        for (int i = searchFrom; i <= text.Length - count; i++)
        {
            if (styles[i] != InlineStyle.Normal) continue;
            if (text[i] != '*') continue;

            int run = 0;
            int start = i;
            while (i < text.Length && text[i] == '*' && styles[i] == InlineStyle.Normal)
            {
                run++;
                i++;
            }

            if (run == count) return start;
            i = start;
            i++;
        }
        return -1;
    }

    private static List<StyledRun> BuildRuns(InlineStyle[] styles)
    {
        var runs = new List<StyledRun>();
        if (styles.Length == 0)
        {
            runs.Add(new StyledRun(0, 0, InlineStyle.Normal));
            return runs;
        }

        int start = 0;
        var current = styles[0];
        for (int i = 1; i < styles.Length; i++)
        {
            if (styles[i] != current)
            {
                runs.Add(new StyledRun(start, i - start, current));
                start = i;
                current = styles[i];
            }
        }
        runs.Add(new StyledRun(start, styles.Length - start, current));
        return runs;
    }
}
