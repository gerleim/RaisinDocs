namespace RaisinDocs;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

public readonly record struct ColorSpan(int Start, int Length, RgbColor? Foreground, RgbColor? Background);

public readonly record struct BlockColor(RgbColor? Foreground, RgbColor? Background);

public enum InlineStyle
{
    Normal,
    Bold,
    Italic,
    BoldItalic,
    Code,
    Strikethrough,
    Image,
    Link,
}

public readonly record struct StyledRun(int Start, int Length, InlineStyle Style);

public readonly record struct InlineImage(int Start, int Length, string AltText, string Url, string? Title);

public readonly record struct InlineLink(int Start, int Length, string Text, string Url, string? Title, string? RefLabel = null);

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
    OrderedListItem,
    TaskListItemUnchecked,
    TaskListItemChecked,
    FencedCodeLine,
    Blockquote,
    TableHeaderRow,
    TableSeparatorRow,
    TableDataRow,
    LinkDefinition,
    ThemeDefinition,
    ColorDivOpen,
    ColorDivClose,
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
    public bool IsSkippedInVisual => IsFenceDelimiter || IsTableSeparator || Kind == BlockKind.LinkDefinition
        || Kind == BlockKind.ThemeDefinition || Kind == BlockKind.ColorDivOpen || Kind == BlockKind.ColorDivClose;
    public IReadOnlyList<InlineImage>? Images { get; init; }
    public IReadOnlyList<InlineLink>? Links { get; init; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; init; }
    public BlockColor? BlockColor { get; init; }
    public TableRowInfo? TableRow { get; init; }
    public TableInfo? Table { get; init; }
}

public static class MarkdownParser
{
    public static List<ParsedBlock> Parse(Func<int, string> getBlockText, int blockCount)
    {
        var defs = CollectLinkDefinitions(getBlockText, blockCount);
        var theme = CollectThemeDefinitions(getBlockText, blockCount);

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

            if (IsThemeBlock(text))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.ThemeDefinition,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            if (IsColorDivOpen(text))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.ColorDivOpen,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                    BlockColor = ParseDivProperties(text, theme),
                });
                continue;
            }

            if (IsColorDivClose(text))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.ColorDivClose,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            if (TryParseLinkDefinition(text, out _, out _, out _))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.LinkDefinition,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            var kind = ClassifyBlock(text);
            List<InlineImage>? images = null;
            List<InlineLink>? links = null;
            var runs = kind == BlockKind.FencedCodeLine
                ? [new StyledRun(0, text.Length, InlineStyle.Normal)]
                : ParseInlines(text, out images, out links, defs);

            var colorSpans = (kind == BlockKind.FencedCodeLine) ? null : ParseInlineColorTags(text, theme);

            result.Add(new ParsedBlock { Kind = kind, Runs = runs, Images = images, Links = links, ColorSpans = colorSpans });
        }

        DetectTables(result, getBlockText);
        ApplyBlockDivColors(result);

        return result;
    }

    private static Dictionary<string, RgbColor>? CollectThemeDefinitions(
        Func<int, string> getBlockText, int blockCount)
    {
        Dictionary<string, RgbColor>? theme = null;
        bool insideFence = false;
        for (int i = 0; i < blockCount; i++)
        {
            string text = getBlockText(i);
            if (IsFenceLine(text)) { insideFence = !insideFence; continue; }
            if (insideFence) continue;

            if (IsThemeBlock(text))
            {
                var parsed = ParseThemeBlock(text);
                if (parsed.Count > 0)
                {
                    theme ??= new(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in parsed)
                        theme[kvp.Key] = kvp.Value;
                }
            }
        }
        return theme;
    }

    private static void ApplyBlockDivColors(List<ParsedBlock> blocks)
    {
        var divStack = new Stack<BlockColor>();

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];

            if (block.Kind == BlockKind.ColorDivOpen && block.BlockColor != null)
            {
                divStack.Push(block.BlockColor.Value);
                continue;
            }

            if (block.Kind == BlockKind.ColorDivClose)
            {
                if (divStack.Count > 0) divStack.Pop();
                continue;
            }

            if (divStack.Count > 0 && block.Kind != BlockKind.ThemeDefinition)
            {
                var merged = MergeBlockColors(divStack);
                blocks[i] = new ParsedBlock
                {
                    Kind = block.Kind,
                    Runs = block.Runs,
                    IsFenceDelimiter = block.IsFenceDelimiter,
                    IsTableSeparator = block.IsTableSeparator,
                    Images = block.Images,
                    Links = block.Links,
                    ColorSpans = block.ColorSpans,
                    BlockColor = merged,
                    TableRow = block.TableRow,
                    Table = block.Table,
                };
            }
        }
    }

    private static BlockColor MergeBlockColors(Stack<BlockColor> stack)
    {
        RgbColor? fg = null, bg = null;
        foreach (var bc in stack)
        {
            fg ??= bc.Foreground;
            bg ??= bc.Background;
            if (fg != null && bg != null) break;
        }
        return new BlockColor(fg, bg);
    }

    private static Dictionary<string, (string Url, string? Title)>? CollectLinkDefinitions(
        Func<int, string> getBlockText, int blockCount)
    {
        Dictionary<string, (string Url, string? Title)>? defs = null;
        bool insideFence = false;
        for (int i = 0; i < blockCount; i++)
        {
            string text = getBlockText(i);
            if (IsFenceLine(text)) { insideFence = !insideFence; continue; }
            if (insideFence) continue;

            if (TryParseLinkDefinition(text, out string? label, out string? url, out string? title))
            {
                defs ??= new(StringComparer.OrdinalIgnoreCase);
                defs.TryAdd(label!, (url!, title));
            }
        }
        return defs;
    }

    internal static bool TryParseLinkDefinition(string text, out string? label, out string? url, out string? title)
    {
        label = null; url = null; title = null;

        int i = 0;
        while (i < text.Length && i < 3 && text[i] == ' ') i++;
        if (i >= text.Length || text[i] != '[') return false;

        int labelStart = i + 1;
        int bracketClose = text.IndexOf(']', labelStart);
        if (bracketClose < 0 || bracketClose == labelStart) return false;
        if (bracketClose + 1 >= text.Length || text[bracketClose + 1] != ':') return false;

        label = text[labelStart..bracketClose];
        int afterColon = bracketClose + 2;

        while (afterColon < text.Length && text[afterColon] == ' ') afterColon++;
        if (afterColon >= text.Length) return false;

        int urlStart;
        if (text[afterColon] == '<')
        {
            urlStart = afterColon + 1;
            int angleClose = text.IndexOf('>', urlStart);
            if (angleClose < 0) return false;
            url = text[urlStart..angleClose];
            afterColon = angleClose + 1;
        }
        else
        {
            urlStart = afterColon;
            while (afterColon < text.Length && text[afterColon] != ' ') afterColon++;
            url = text[urlStart..afterColon];
        }

        if (string.IsNullOrEmpty(url)) return false;

        while (afterColon < text.Length && text[afterColon] == ' ') afterColon++;
        if (afterColon >= text.Length) return true;

        char q = text[afterColon];
        char qClose = q == '"' ? '"' : q == '\'' ? '\'' : q == '(' ? ')' : '\0';
        if (qClose == '\0') return false;

        int titleStart = afterColon + 1;
        int titleEnd = text.IndexOf(qClose, titleStart);
        if (titleEnd < 0) return false;
        title = text[titleStart..titleEnd];

        return true;
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
                Links = blocks[i].Links,
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
                    Links = blocks[j].Links,
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
        {
            if (text.Length >= 6 && text[2] == '[' && text[4] == ']' && text[5] == ' ')
            {
                if (text[3] == ' ') return BlockKind.TaskListItemUnchecked;
                if (text[3] is 'x' or 'X') return BlockKind.TaskListItemChecked;
            }
            return BlockKind.UnorderedListItem;
        }

        if (GetOrderedListPrefixLength(text) > 0)
            return BlockKind.OrderedListItem;

        if (text.StartsWith("> ") || text == ">")
            return BlockKind.Blockquote;

        return BlockKind.Paragraph;
    }

    internal static int GetOrderedListPrefixLength(string text)
    {
        int i = 0;
        while (i < text.Length && i < 9 && text[i] >= '0' && text[i] <= '9')
            i++;
        if (i == 0 || i > 9) return 0;
        if (i < text.Length && text[i] is '.' or ')')
        {
            if (i + 1 < text.Length && text[i + 1] == ' ')
                return i + 2;
        }
        return 0;
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
        return ParseInlines(text, out images, out _);
    }

    internal static List<StyledRun> ParseInlines(string text, out List<InlineImage>? images, out List<InlineLink>? links,
        Dictionary<string, (string Url, string? Title)>? defs = null)
    {
        images = null;
        links = null;
        if (text.Length == 0)
            return [new StyledRun(0, 0, InlineStyle.Normal)];

        var styles = new InlineStyle[text.Length];

        MarkCodeSpans(text, styles);
        images = MarkImages(text, styles, defs);
        links = MarkLinks(text, styles, defs);
        links = MarkAutolinks(text, styles, links);
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

    private static List<InlineImage>? MarkImages(string text, InlineStyle[] styles,
        Dictionary<string, (string Url, string? Title)>? defs = null)
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
            if (bracketClose < 0 || bracketClose + 1 >= text.Length)
            {
                i++;
                continue;
            }

            string altText = text[altStart..bracketClose];
            string url;
            string? title;
            int end;

            if (text[bracketClose + 1] == '(')
            {
                int parenOpen = bracketClose + 2;
                int parenClose = ParseDestinationAndTitle(text, parenOpen, out url, out title);
                if (parenClose < 0) { i++; continue; }
                end = parenClose + 1;
            }
            else if (TryResolveReference(text, bracketClose, altText, defs, out url!, out title, out end, out _))
            {
                // resolved reference image
            }
            else { i++; continue; }

            int totalLength = end - i;
            images ??= [];
            images.Add(new InlineImage(i, totalLength, altText, url, title));

            for (int j = i; j < end; j++)
                styles[j] = InlineStyle.Image;

            i = end;
        }
        return images;
    }

    private static List<InlineLink>? MarkLinks(string text, InlineStyle[] styles,
        Dictionary<string, (string Url, string? Title)>? defs = null)
    {
        List<InlineLink>? links = null;
        int i = 0;
        while (i <= text.Length - 4) // minimum: [](x)
        {
            if (text[i] != '[' || styles[i] != InlineStyle.Normal)
            {
                i++;
                continue;
            }

            if (i > 0 && text[i - 1] == '!' && styles[i - 1] == InlineStyle.Image)
            {
                i++;
                continue;
            }

            int textStart = i + 1;
            int bracketClose = FindMatchingBracket(text, textStart);
            if (bracketClose < 0 || bracketClose + 1 >= text.Length)
            {
                i++;
                continue;
            }

            string linkText = text[textStart..bracketClose];
            string url;
            string? title;
            int end;
            string? refLabel = null;

            if (text[bracketClose + 1] == '(')
            {
                int parenOpen = bracketClose + 2;
                int parenClose = ParseDestinationAndTitle(text, parenOpen, out url, out title);
                if (parenClose < 0) { i++; continue; }
                end = parenClose + 1;
            }
            else if (TryResolveReference(text, bracketClose, linkText, defs, out url!, out title, out end, out var resolvedLabel))
            {
                refLabel = resolvedLabel;
            }
            else { i++; continue; }

            int totalLength = end - i;
            links ??= [];
            links.Add(new InlineLink(i, totalLength, linkText, url, title, refLabel));

            for (int j = i; j < end; j++)
                styles[j] = InlineStyle.Link;

            i = end;
        }
        return links;
    }

    private static readonly string[] _autolinkPrefixes = ["https://", "http://", "www."];

    private static List<InlineLink>? MarkAutolinks(string text, InlineStyle[] styles, List<InlineLink>? links)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (styles[i] != InlineStyle.Normal) continue;

            string? matchedPrefix = null;
            foreach (var prefix in _autolinkPrefixes)
            {
                if (i + prefix.Length < text.Length &&
                    text.AsSpan(i, prefix.Length).Equals(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    matchedPrefix = prefix;
                    break;
                }
            }
            if (matchedPrefix == null) continue;

            if (i > 0 && !char.IsWhiteSpace(text[i - 1]) && text[i - 1] != '(' && text[i - 1] != '"' && text[i - 1] != '\'')
                continue;

            int urlEnd = i + matchedPrefix.Length;
            if (urlEnd >= text.Length || char.IsWhiteSpace(text[urlEnd])) continue;

            while (urlEnd < text.Length && text[urlEnd] != '<' && !char.IsWhiteSpace(text[urlEnd]))
                urlEnd++;

            urlEnd = TrimAutolinkTrailing(text, i, urlEnd);

            int length = urlEnd - i;
            if (length <= matchedPrefix.Length) continue;

            string urlText = text[i..urlEnd];
            string url = matchedPrefix == "www."
                ? "http://" + urlText
                : urlText;

            links ??= [];
            links.Add(new InlineLink(i, length, urlText, url, null));

            for (int j = i; j < urlEnd; j++)
                styles[j] = InlineStyle.Link;

            i = urlEnd - 1;
        }
        return links;
    }

    private static int TrimAutolinkTrailing(string text, int start, int end)
    {
        while (end > start)
        {
            char c = text[end - 1];
            if (c == '?' || c == '!' || c == '.' || c == ',' || c == ':' || c == ';' || c == '*' || c == '_' || c == '~' || c == '\'' || c == '"')
            {
                end--;
                continue;
            }
            if (c == ')')
            {
                int open = 0, close = 0;
                for (int j = start; j < end; j++)
                {
                    if (text[j] == '(') open++;
                    else if (text[j] == ')') close++;
                }
                if (close > open) { end--; continue; }
            }
            break;
        }
        return end;
    }

    private static bool TryResolveReference(string text, int bracketClose, string fallbackLabel,
        Dictionary<string, (string Url, string? Title)>? defs, out string url, out string? title, out int end, out string refLabel)
    {
        url = ""; title = null; end = 0; refLabel = "";
        if (defs == null || bracketClose + 1 >= text.Length || text[bracketClose + 1] != '[')
            return false;

        int refStart = bracketClose + 2;
        string label;

        if (refStart < text.Length && text[refStart] == ']')
        {
            label = fallbackLabel;
            end = refStart + 1;
        }
        else
        {
            int refClose = text.IndexOf(']', refStart);
            if (refClose < 0) return false;
            label = text[refStart..refClose];
            end = refClose + 1;
        }

        if (!defs.TryGetValue(label, out var def)) return false;
        url = def.Url;
        title = def.Title;
        refLabel = label;
        return true;
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

    // ---- Comment-based extensions (<!--@...-->) ----

    private const string ThemeOpen = "<!--@theme";
    private const string CommentClose = "-->";
    private const string DivOpen = "<!--@div ";
    private const string DivClose = "<!--/@div-->";

    internal static bool IsThemeBlock(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.StartsWith(ThemeOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal);
    }

    internal static bool IsColorDivOpen(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.StartsWith(DivOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal);
    }

    internal static bool IsColorDivClose(string text)
    {
        return text.AsSpan().Trim().Equals(DivClose.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, RgbColor> ParseThemeBlock(string text)
    {
        var result = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase);
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith(ThemeOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal))
            return result;

        var body = trimmed[ThemeOpen.Length..^CommentClose.Length];

        foreach (var rawLine in SplitLines(body))
        {
            var line = rawLine.AsSpan().Trim();
            if (line.IsEmpty) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (name.IsEmpty || value.IsEmpty) continue;

            if (TryParseColor(value, out var color))
                result[name.ToString()] = color;
        }
        return result;
    }

    internal static BlockColor? ParseDivProperties(string text, Dictionary<string, RgbColor>? theme)
    {
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith(DivOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal))
            return null;

        var props = trimmed[DivOpen.Length..^CommentClose.Length].Trim();
        return ParseColorProperties(props, theme);
    }

    private static BlockColor? ParseColorProperties(ReadOnlySpan<char> props, Dictionary<string, RgbColor>? theme)
    {
        RgbColor? fg = null, bg = null;

        while (!props.IsEmpty)
        {
            while (!props.IsEmpty && props[0] == ' ') props = props[1..];
            if (props.IsEmpty) break;

            int space = props.IndexOf(' ');
            var token = space >= 0 ? props[..space] : props;
            props = space >= 0 ? props[(space + 1)..] : ReadOnlySpan<char>.Empty;

            if (token.StartsWith("fg:".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (ResolveColor(token[3..], theme, out var c)) fg = c;
            }
            else if (token.StartsWith("bg:".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (ResolveColor(token[3..], theme, out var c)) bg = c;
            }
        }

        if (fg == null && bg == null) return null;
        return new BlockColor(fg, bg);
    }

    internal static List<ColorSpan>? ParseInlineColorTags(string text, Dictionary<string, RgbColor>? theme)
    {
        List<ColorSpan>? spans = null;
        var openFg = new Stack<(int tagEnd, RgbColor color)>();
        var openBg = new Stack<(int tagEnd, RgbColor color)>();

        int i = 0;
        while (i < text.Length - 6)
        {
            if (text[i] != '<' || i + 4 >= text.Length || text[i + 1] != '!' || text[i + 2] != '-' || text[i + 3] != '-')
            {
                i++;
                continue;
            }

            if (text[i + 4] == '@')
            {
                int closeIdx = text.IndexOf("-->", i + 5, StringComparison.Ordinal);
                if (closeIdx < 0) { i++; continue; }

                int tagEnd = closeIdx + 3;
                var body = text.AsSpan()[(i + 5)..closeIdx].Trim();

                if (body.StartsWith("fg:".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    int spaceInBody = body.IndexOf(' ');
                    var fgToken = spaceInBody >= 0 ? body[3..spaceInBody] : body[3..];
                    if (ResolveColor(fgToken, theme, out var fgColor))
                        openFg.Push((tagEnd, fgColor));

                    if (spaceInBody >= 0)
                    {
                        var rest = body[(spaceInBody + 1)..].Trim();
                        if (rest.StartsWith("bg:".AsSpan(), StringComparison.OrdinalIgnoreCase)
                            && ResolveColor(rest[3..], theme, out var bgColor))
                            openBg.Push((tagEnd, bgColor));
                    }
                }
                else if (body.StartsWith("bg:".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    int spaceInBody = body.IndexOf(' ');
                    var bgToken = spaceInBody >= 0 ? body[3..spaceInBody] : body[3..];
                    if (ResolveColor(bgToken, theme, out var bgColor))
                        openBg.Push((tagEnd, bgColor));

                    if (spaceInBody >= 0)
                    {
                        var rest = body[(spaceInBody + 1)..].Trim();
                        if (rest.StartsWith("fg:".AsSpan(), StringComparison.OrdinalIgnoreCase)
                            && ResolveColor(rest[3..], theme, out var fgColor))
                            openFg.Push((tagEnd, fgColor));
                    }
                }

                i = tagEnd;
                continue;
            }

            if (i + 5 < text.Length && text[i + 4] == '/' && text[i + 5] == '@')
            {
                int closeIdx = text.IndexOf("-->", i + 6, StringComparison.Ordinal);
                if (closeIdx < 0) { i++; continue; }

                int tagEnd = closeIdx + 3;
                var body = text.AsSpan()[(i + 6)..closeIdx].Trim();

                bool closeFg = body.Equals("fg".AsSpan(), StringComparison.OrdinalIgnoreCase)
                               || body.IsEmpty;
                bool closeBg = body.Equals("bg".AsSpan(), StringComparison.OrdinalIgnoreCase)
                               || body.IsEmpty;

                if (closeFg && openFg.Count > 0)
                {
                    var (start, color) = openFg.Pop();
                    if (start < i)
                    {
                        spans ??= [];
                        AddOrMergeColorSpan(ref spans, start, i - start, color, null);
                    }
                }
                if (closeBg && openBg.Count > 0)
                {
                    var (start, color) = openBg.Pop();
                    if (start < i)
                    {
                        spans ??= [];
                        AddOrMergeColorSpan(ref spans, start, i - start, null, color);
                    }
                }

                i = tagEnd;
                continue;
            }

            i++;
        }

        // Unclosed tags: extend to end of block
        while (openFg.Count > 0)
        {
            var (start, color) = openFg.Pop();
            if (start < text.Length)
            {
                spans ??= [];
                AddOrMergeColorSpan(ref spans, start, text.Length - start, color, null);
            }
        }
        while (openBg.Count > 0)
        {
            var (start, color) = openBg.Pop();
            if (start < text.Length)
            {
                spans ??= [];
                AddOrMergeColorSpan(ref spans, start, text.Length - start, null, color);
            }
        }

        return spans;
    }

    internal static List<HiddenRange>? FindInlineColorTagRanges(string text)
    {
        List<HiddenRange>? ranges = null;
        int i = 0;
        while (i < text.Length - 6)
        {
            if (text[i] != '<' || i + 4 >= text.Length || text[i + 1] != '!' || text[i + 2] != '-' || text[i + 3] != '-')
            {
                i++;
                continue;
            }

            bool isOpener = text[i + 4] == '@';
            bool isCloser = i + 5 < text.Length && text[i + 4] == '/' && text[i + 5] == '@';

            if (isOpener || isCloser)
            {
                int searchFrom = isOpener ? i + 5 : i + 6;
                int closeIdx = text.IndexOf("-->", searchFrom, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    int tagEnd = closeIdx + 3;
                    ranges ??= [];
                    ranges.Add(new HiddenRange(i, tagEnd - i));
                    i = tagEnd;
                    continue;
                }
            }
            i++;
        }
        return ranges;
    }

    private static void AddOrMergeColorSpan(ref List<ColorSpan> spans, int start, int length,
        RgbColor? fg, RgbColor? bg)
    {
        for (int i = 0; i < spans.Count; i++)
        {
            var existing = spans[i];
            if (existing.Start == start && existing.Length == length)
            {
                spans[i] = new ColorSpan(start, length,
                    fg ?? existing.Foreground, bg ?? existing.Background);
                return;
            }
        }
        spans.Add(new ColorSpan(start, length, fg, bg));
    }

    internal static bool TryParseColor(ReadOnlySpan<char> value, out RgbColor color)
    {
        color = default;
        if (value.IsEmpty) return false;

        if (value[0] == '#')
        {
            var hex = value[1..];
            if (hex.Length == 6
                && byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r6)
                && byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g6)
                && byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b6))
            {
                color = new RgbColor(r6, g6, b6);
                return true;
            }
            if (hex.Length == 3
                && byte.TryParse(stackalloc char[] { hex[0], hex[0] }, System.Globalization.NumberStyles.HexNumber, null, out byte r3)
                && byte.TryParse(stackalloc char[] { hex[1], hex[1] }, System.Globalization.NumberStyles.HexNumber, null, out byte g3)
                && byte.TryParse(stackalloc char[] { hex[2], hex[2] }, System.Globalization.NumberStyles.HexNumber, null, out byte b3))
            {
                color = new RgbColor(r3, g3, b3);
                return true;
            }
            return false;
        }

        return TryGetNamedColor(value, out color);
    }

    private static bool ResolveColor(ReadOnlySpan<char> name, Dictionary<string, RgbColor>? theme, out RgbColor color)
    {
        color = default;
        if (name.IsEmpty) return false;

        if (name[0] == '#')
            return TryParseColor(name, out color);

        var nameStr = name.ToString();
        if (theme != null && theme.TryGetValue(nameStr, out color))
            return true;

        return TryGetNamedColor(name, out color);
    }

    private static List<string> SplitLines(ReadOnlySpan<char> text)
    {
        var lines = new List<string>();
        var str = text.ToString();
        int start = 0;
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '\n')
            {
                lines.Add(str[start..i]);
                start = i + 1;
            }
        }
        if (start <= str.Length)
            lines.Add(str[start..]);
        return lines;
    }

    private static bool TryGetNamedColor(ReadOnlySpan<char> name, out RgbColor color)
    {
        color = default;
        var key = name.ToString();
        if (CssNamedColors.TryGetValue(key, out color))
            return true;
        return false;
    }

    private static readonly Dictionary<string, RgbColor> CssNamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = new(240, 248, 255),
        ["antiquewhite"] = new(250, 235, 215),
        ["aqua"] = new(0, 255, 255),
        ["aquamarine"] = new(127, 255, 212),
        ["azure"] = new(240, 255, 255),
        ["beige"] = new(245, 245, 220),
        ["bisque"] = new(255, 228, 196),
        ["black"] = new(0, 0, 0),
        ["blanchedalmond"] = new(255, 235, 205),
        ["blue"] = new(0, 0, 255),
        ["blueviolet"] = new(138, 43, 226),
        ["brown"] = new(165, 42, 42),
        ["burlywood"] = new(222, 184, 135),
        ["cadetblue"] = new(95, 158, 160),
        ["chartreuse"] = new(127, 255, 0),
        ["chocolate"] = new(210, 105, 30),
        ["coral"] = new(255, 127, 80),
        ["cornflowerblue"] = new(100, 149, 237),
        ["cornsilk"] = new(255, 248, 220),
        ["crimson"] = new(220, 20, 60),
        ["cyan"] = new(0, 255, 255),
        ["darkblue"] = new(0, 0, 139),
        ["darkcyan"] = new(0, 139, 139),
        ["darkgoldenrod"] = new(184, 134, 11),
        ["darkgray"] = new(169, 169, 169),
        ["darkgreen"] = new(0, 100, 0),
        ["darkgrey"] = new(169, 169, 169),
        ["darkkhaki"] = new(189, 183, 107),
        ["darkmagenta"] = new(139, 0, 139),
        ["darkolivegreen"] = new(85, 107, 47),
        ["darkorange"] = new(255, 140, 0),
        ["darkorchid"] = new(153, 50, 204),
        ["darkred"] = new(139, 0, 0),
        ["darksalmon"] = new(233, 150, 122),
        ["darkseagreen"] = new(143, 188, 143),
        ["darkslateblue"] = new(72, 61, 139),
        ["darkslategray"] = new(47, 79, 79),
        ["darkslategrey"] = new(47, 79, 79),
        ["darkturquoise"] = new(0, 206, 209),
        ["darkviolet"] = new(148, 0, 211),
        ["deeppink"] = new(255, 20, 147),
        ["deepskyblue"] = new(0, 191, 255),
        ["dimgray"] = new(105, 105, 105),
        ["dimgrey"] = new(105, 105, 105),
        ["dodgerblue"] = new(30, 144, 255),
        ["firebrick"] = new(178, 34, 34),
        ["floralwhite"] = new(255, 250, 240),
        ["forestgreen"] = new(34, 139, 34),
        ["fuchsia"] = new(255, 0, 255),
        ["gainsboro"] = new(220, 220, 220),
        ["ghostwhite"] = new(248, 248, 255),
        ["gold"] = new(255, 215, 0),
        ["goldenrod"] = new(218, 165, 32),
        ["gray"] = new(128, 128, 128),
        ["green"] = new(0, 128, 0),
        ["greenyellow"] = new(173, 255, 47),
        ["grey"] = new(128, 128, 128),
        ["honeydew"] = new(240, 255, 240),
        ["hotpink"] = new(255, 105, 180),
        ["indianred"] = new(205, 92, 92),
        ["indigo"] = new(75, 0, 130),
        ["ivory"] = new(255, 255, 240),
        ["khaki"] = new(240, 230, 140),
        ["lavender"] = new(230, 230, 250),
        ["lavenderblush"] = new(255, 240, 245),
        ["lawngreen"] = new(124, 252, 0),
        ["lemonchiffon"] = new(255, 250, 205),
        ["lightblue"] = new(173, 216, 230),
        ["lightcoral"] = new(240, 128, 128),
        ["lightcyan"] = new(224, 255, 255),
        ["lightgoldenrodyellow"] = new(250, 250, 210),
        ["lightgray"] = new(211, 211, 211),
        ["lightgreen"] = new(144, 238, 144),
        ["lightgrey"] = new(211, 211, 211),
        ["lightpink"] = new(255, 182, 193),
        ["lightsalmon"] = new(255, 160, 122),
        ["lightseagreen"] = new(32, 178, 170),
        ["lightskyblue"] = new(135, 206, 250),
        ["lightslategray"] = new(119, 136, 153),
        ["lightslategrey"] = new(119, 136, 153),
        ["lightsteelblue"] = new(176, 196, 222),
        ["lightyellow"] = new(255, 255, 224),
        ["lime"] = new(0, 255, 0),
        ["limegreen"] = new(50, 205, 50),
        ["linen"] = new(250, 240, 230),
        ["magenta"] = new(255, 0, 255),
        ["maroon"] = new(128, 0, 0),
        ["mediumaquamarine"] = new(102, 205, 170),
        ["mediumblue"] = new(0, 0, 205),
        ["mediumorchid"] = new(186, 85, 211),
        ["mediumpurple"] = new(147, 111, 219),
        ["mediumseagreen"] = new(60, 179, 113),
        ["mediumslateblue"] = new(123, 104, 238),
        ["mediumspringgreen"] = new(0, 250, 154),
        ["mediumturquoise"] = new(72, 209, 204),
        ["mediumvioletred"] = new(199, 21, 133),
        ["midnightblue"] = new(25, 25, 112),
        ["mintcream"] = new(245, 255, 250),
        ["mistyrose"] = new(255, 228, 225),
        ["moccasin"] = new(255, 228, 181),
        ["navajowhite"] = new(255, 222, 173),
        ["navy"] = new(0, 0, 128),
        ["oldlace"] = new(253, 245, 230),
        ["olive"] = new(128, 128, 0),
        ["olivedrab"] = new(107, 142, 35),
        ["orange"] = new(255, 165, 0),
        ["orangered"] = new(255, 69, 0),
        ["orchid"] = new(218, 112, 214),
        ["palegoldenrod"] = new(238, 232, 170),
        ["palegreen"] = new(152, 251, 152),
        ["paleturquoise"] = new(175, 238, 238),
        ["palevioletred"] = new(219, 112, 147),
        ["papayawhip"] = new(255, 239, 213),
        ["peachpuff"] = new(255, 218, 185),
        ["peru"] = new(205, 133, 63),
        ["pink"] = new(255, 192, 203),
        ["plum"] = new(221, 160, 221),
        ["powderblue"] = new(176, 224, 230),
        ["purple"] = new(128, 0, 128),
        ["rebeccapurple"] = new(102, 51, 153),
        ["red"] = new(255, 0, 0),
        ["rosybrown"] = new(188, 143, 143),
        ["royalblue"] = new(65, 105, 225),
        ["saddlebrown"] = new(139, 69, 19),
        ["salmon"] = new(250, 128, 114),
        ["sandybrown"] = new(244, 164, 96),
        ["seagreen"] = new(46, 139, 87),
        ["seashell"] = new(255, 245, 238),
        ["sienna"] = new(160, 82, 45),
        ["silver"] = new(192, 192, 192),
        ["skyblue"] = new(135, 206, 235),
        ["slateblue"] = new(106, 90, 205),
        ["slategray"] = new(112, 128, 144),
        ["slategrey"] = new(112, 128, 144),
        ["snow"] = new(255, 250, 250),
        ["springgreen"] = new(0, 255, 127),
        ["steelblue"] = new(70, 130, 180),
        ["tan"] = new(210, 180, 140),
        ["teal"] = new(0, 128, 128),
        ["thistle"] = new(216, 191, 216),
        ["tomato"] = new(255, 99, 71),
        ["turquoise"] = new(64, 224, 208),
        ["violet"] = new(238, 130, 238),
        ["wheat"] = new(245, 222, 179),
        ["white"] = new(255, 255, 255),
        ["whitesmoke"] = new(245, 245, 245),
        ["yellow"] = new(255, 255, 0),
        ["yellowgreen"] = new(154, 205, 50),
    };
}
