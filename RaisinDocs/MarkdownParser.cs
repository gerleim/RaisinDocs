namespace RaisinDocs;

public enum InlineStyle
{
    Normal,
    Bold,
    Italic,
    BoldItalic,
    Code,
    Strikethrough,
    Image,
}

public readonly record struct StyledRun(int Start, int Length, InlineStyle Style);

public readonly record struct InlineImage(int Start, int Length, string AltText, string Url, string? Title);

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
    Blockquote,
    TableHeaderRow,
    TableSeparatorRow,
    TableDataRow,
}

public enum ColumnAlignment { Left, Center, Right }

public readonly record struct TableCellInfo(int Start, int Length);

public class TableRowInfo
{
    public required IReadOnlyList<TableCellInfo> Cells { get; init; }
}

public class TableInfo
{
    public required int ColumnCount { get; init; }
    public required IReadOnlyList<ColumnAlignment> Alignments { get; init; }
}

public class ParsedBlock
{
    public required BlockKind Kind { get; init; }
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public bool IsFenceDelimiter { get; init; }
    public bool IsTableSeparator { get; init; }
    public bool IsSkippedInVisual => IsFenceDelimiter || IsTableSeparator;
    public IReadOnlyList<InlineImage>? Images { get; init; }
    public TableRowInfo? TableRow { get; init; }
    public TableInfo? Table { get; init; }
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
            List<InlineImage>? images = null;
            var runs = kind == BlockKind.FencedCodeLine
                ? [new StyledRun(0, text.Length, InlineStyle.Normal)]
                : ParseInlines(text, out images);

            result.Add(new ParsedBlock { Kind = kind, Runs = runs, Images = images });
        }

        DetectTables(result, getBlockText);

        return result;
    }

    private static void DetectTables(List<ParsedBlock> blocks, Func<int, string> getBlockText)
    {
        int i = 0;
        while (i < blocks.Count - 1)
        {
            if (blocks[i].Kind != BlockKind.Paragraph || !ContainsUnescapedPipe(getBlockText(i)))
            {
                i++;
                continue;
            }

            string sepText = getBlockText(i + 1);
            if (blocks[i + 1].Kind != BlockKind.Paragraph || !IsSeparatorRow(sepText, out var alignments))
            {
                i++;
                continue;
            }

            var headerCells = ParseTableCells(getBlockText(i));
            if (headerCells.Count != alignments.Count)
            {
                i++;
                continue;
            }

            var tableInfo = new TableInfo { ColumnCount = alignments.Count, Alignments = alignments };
            var headerRow = new TableRowInfo { Cells = headerCells };

            blocks[i] = new ParsedBlock
            {
                Kind = BlockKind.TableHeaderRow,
                Runs = blocks[i].Runs,
                Images = blocks[i].Images,
                TableRow = headerRow,
                Table = tableInfo,
            };

            var sepCells = ParseTableCells(sepText);
            blocks[i + 1] = new ParsedBlock
            {
                Kind = BlockKind.TableSeparatorRow,
                Runs = blocks[i + 1].Runs,
                IsTableSeparator = true,
                TableRow = new TableRowInfo { Cells = sepCells },
                Table = tableInfo,
            };

            int j = i + 2;
            while (j < blocks.Count && blocks[j].Kind == BlockKind.Paragraph
                   && ContainsUnescapedPipe(getBlockText(j)))
            {
                var dataCells = ParseTableCells(getBlockText(j));
                blocks[j] = new ParsedBlock
                {
                    Kind = BlockKind.TableDataRow,
                    Runs = blocks[j].Runs,
                    Images = blocks[j].Images,
                    TableRow = new TableRowInfo { Cells = dataCells },
                    Table = tableInfo,
                };
                j++;
            }

            i = j;
        }
    }

    private static bool ContainsUnescapedPipe(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '|') return true;
        }
        return false;
    }

    internal static bool IsSeparatorRow(string text, out List<ColumnAlignment> alignments)
    {
        alignments = [];
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;

        int start = 0;
        int end = trimmed.Length;
        if (trimmed[0] == '|') start = 1;
        if (end > start && trimmed[end - 1] == '|') end--;

        if (start >= end) return false;

        var inner = trimmed.Substring(start, end - start);
        int cellStart = 0;
        for (int ci = 0; ci <= inner.Length; ci++)
        {
            bool atPipe = ci < inner.Length && inner[ci] == '|';
            if (ci < inner.Length && inner[ci] == '\\') { ci++; continue; }
            if (!atPipe && ci < inner.Length) continue;

            var cell = inner.Substring(cellStart, ci - cellStart).Trim();
            if (cell.Length == 0) return false;

            bool leftColon = cell[0] == ':';
            bool rightColon = cell[cell.Length - 1] == ':';

            int dashS = leftColon ? 1 : 0;
            int dashE = rightColon ? cell.Length - 1 : cell.Length;
            if (dashE <= dashS) return false;

            for (int k = dashS; k < dashE; k++)
            {
                if (cell[k] != '-') return false;
            }

            if (leftColon && rightColon) alignments.Add(ColumnAlignment.Center);
            else if (rightColon) alignments.Add(ColumnAlignment.Right);
            else alignments.Add(ColumnAlignment.Left);

            cellStart = ci + 1;
        }

        return alignments.Count > 0;
    }

    internal static List<TableCellInfo> ParseTableCells(string text)
    {
        var cells = new List<TableCellInfo>();
        int pos = 0;

        // skip leading pipe
        if (pos < text.Length && text[pos] == '|') pos++;

        while (pos < text.Length)
        {
            int cellStart = pos;
            while (pos < text.Length)
            {
                if (text[pos] == '\\' && pos + 1 < text.Length) { pos += 2; continue; }
                if (text[pos] == '|') break;
                pos++;
            }

            // check if this is the trailing pipe (nothing after it or only whitespace)
            if (pos < text.Length && text[pos] == '|')
            {
                bool isTrailing = true;
                for (int k = pos + 1; k < text.Length; k++)
                {
                    if (text[k] != ' ' && text[k] != '\t') { isTrailing = false; break; }
                }

                if (isTrailing && cells.Count > 0)
                {
                    // include this last segment as a cell, then stop
                    cells.Add(new TableCellInfo(cellStart, pos - cellStart));
                    break;
                }

                cells.Add(new TableCellInfo(cellStart, pos - cellStart));
                pos++; // skip pipe
            }
            else
            {
                // end of text without pipe
                if (pos > cellStart || cells.Count > 0)
                    cells.Add(new TableCellInfo(cellStart, pos - cellStart));
                break;
            }
        }

        return cells;
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

        if (text.StartsWith("> ") || text == ">")
            return BlockKind.Blockquote;

        return BlockKind.Paragraph;
    }

    public static bool IsTrailingHardBreak(ParsedBlock parsed, string blockText)
    {
        if (parsed.Kind == BlockKind.FencedCodeLine) return false;
        if (!blockText.EndsWith('\\')) return false;

        int count = 0;
        for (int i = blockText.Length - 1; i >= 0 && blockText[i] == '\\'; i--)
            count++;
        if (count % 2 == 0) return false;

        int backslashPos = blockText.Length - 1;
        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Code &&
                backslashPos >= run.Start &&
                backslashPos < run.Start + run.Length)
                return false;
        }

        return true;
    }

    internal static List<StyledRun> ParseInlines(string text)
    {
        return ParseInlines(text, out _);
    }

    internal static List<StyledRun> ParseInlines(string text, out List<InlineImage>? images)
    {
        images = null;
        if (text.Length == 0)
            return [new StyledRun(0, 0, InlineStyle.Normal)];

        var styles = new InlineStyle[text.Length];

        MarkCodeSpans(text, styles);
        images = MarkImages(text, styles);
        MarkStrikethrough(text, styles);
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

    private static List<InlineImage>? MarkImages(string text, InlineStyle[] styles)
    {
        List<InlineImage>? images = null;
        int i = 0;
        while (i <= text.Length - 5) // minimum: ![](x)
        {
            if (text[i] != '!' || styles[i] != InlineStyle.Normal ||
                i + 1 >= text.Length || text[i + 1] != '[')
            {
                i++;
                continue;
            }

            int altStart = i + 2;
            int bracketClose = FindMatchingBracket(text, altStart);
            if (bracketClose < 0 || bracketClose + 1 >= text.Length || text[bracketClose + 1] != '(')
            {
                i++;
                continue;
            }

            int parenOpen = bracketClose + 2;
            int parenClose = ParseDestinationAndTitle(text, parenOpen, out string url, out string? title);
            if (parenClose < 0)
            {
                i++;
                continue;
            }

            string altText = text[altStart..bracketClose];
            int totalLength = parenClose + 1 - i;

            images ??= [];
            images.Add(new InlineImage(i, totalLength, altText, url, title));

            for (int j = i; j <= parenClose; j++)
                styles[j] = InlineStyle.Image;

            i = parenClose + 1;
        }
        return images;
    }

    private static int FindMatchingBracket(string text, int from)
    {
        int depth = 1;
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int ParseDestinationAndTitle(string text, int from, out string url, out string? title)
    {
        url = "";
        title = null;
        int i = from;

        // skip leading whitespace
        while (i < text.Length && text[i] == ' ') i++;
        if (i >= text.Length) return -1;

        // parse destination
        int urlStart;
        if (text[i] == '<')
        {
            // angle-bracket destination
            urlStart = i + 1;
            i++;
            while (i < text.Length && text[i] != '>' && text[i] != '\n')
            {
                if (text[i] == '\\' && i + 1 < text.Length) i++;
                i++;
            }
            if (i >= text.Length || text[i] != '>') return -1;
            url = text[urlStart..i];
            i++; // past '>'
        }
        else
        {
            // bare destination — balanced parens allowed
            urlStart = i;
            int parenDepth = 0;
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
                if (text[i] == ' ') break;
                if (text[i] == '(') { parenDepth++; i++; continue; }
                if (text[i] == ')') { if (parenDepth == 0) break; parenDepth--; i++; continue; }
                i++;
            }
            url = text[urlStart..i];
        }

        // skip whitespace between destination and title
        while (i < text.Length && text[i] == ' ') i++;
        if (i >= text.Length) return -1;

        // check for closing paren (no title)
        if (text[i] == ')')
            return i;

        // parse optional title
        char titleOpen = text[i];
        char titleClose;
        if (titleOpen == '"') titleClose = '"';
        else if (titleOpen == '\'') titleClose = '\'';
        else if (titleOpen == '(') titleClose = ')';
        else return -1;

        i++; // past opening quote
        int titleStart = i;
        while (i < text.Length && text[i] != titleClose)
        {
            if (text[i] == '\\' && i + 1 < text.Length) i++;
            i++;
        }
        if (i >= text.Length) return -1;
        title = text[titleStart..i];
        i++; // past closing quote

        // skip whitespace, expect closing paren
        while (i < text.Length && text[i] == ' ') i++;
        if (i >= text.Length || text[i] != ')') return -1;
        return i;
    }

    private static void MarkStrikethrough(string text, InlineStyle[] styles)
    {
        int i = 0;
        while (i <= text.Length - 4)
        {
            if (styles[i] != InlineStyle.Normal || text[i] != '~' || text[i + 1] != '~')
            {
                i++;
                continue;
            }

            int openStart = i;
            i += 2;

            for (int j = i; j <= text.Length - 2; j++)
            {
                if (styles[j] == InlineStyle.Normal && text[j] == '~' && text[j + 1] == '~')
                {
                    for (int k = openStart; k < j + 2; k++)
                        styles[k] = InlineStyle.Strikethrough;
                    i = j + 2;
                    goto next;
                }
            }
            next:;
        }
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
