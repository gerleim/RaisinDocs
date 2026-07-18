using System.Text;

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

public readonly record struct InlineLink(int Start, int Length, string Text, string Url, string? Title, string? RefLabel = null, bool IsAngleBracket = false);

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
    ThematicBreak,
    SetextUnderline,
    IndentedCodeLine,
    PageBreak,
}

public enum ColumnAlignment { Left, Center, Right }

public readonly record struct TableCellInfo(int Start, int Length)
{
    public (int Start, int End) TrimContent(string blockText)
    {
        int s = Start, e = Start + Length;
        while (s < e && blockText[s] == ' ') s++;
        while (e > s && blockText[e - 1] == ' ') e--;
        return (s, e);
    }
}

public class TableRowInfo
{
    public required IReadOnlyList<TableCellInfo> Cells { get; init; }
}

public class TableInfo
{
    public required int ColumnCount { get; init; }
    public required IReadOnlyList<ColumnAlignment> Alignments { get; init; }
}

public readonly record struct EmphasisMarker(int Start, int Length);

public record class ParsedBlock
{
    public required BlockKind Kind { get; init; }
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public bool IsFenceDelimiter { get; init; }
    public bool IsTableSeparator { get; init; }
    public bool IsSkippedInVisual => IsFenceDelimiter || IsTableSeparator || Kind == BlockKind.LinkDefinition
        || Kind == BlockKind.ThemeDefinition || Kind == BlockKind.ColorDivOpen || Kind == BlockKind.ColorDivClose
        || Kind == BlockKind.SetextUnderline || Kind == BlockKind.PageBreak;
    public IReadOnlyList<InlineImage>? Images { get; init; }
    public IReadOnlyList<InlineLink>? Links { get; init; }
    public IReadOnlyList<EmphasisMarker>? EmphasisMarkers { get; init; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; init; }
    public BlockColor? BlockColor { get; init; }
    public BlockColor? DivOpenColor { get; init; }
    public bool HasDivClose { get; init; }
    public TableRowInfo? TableRow { get; init; }
    public TableInfo? Table { get; init; }
    public int LeadingSpaces { get; init; }
    public int ContentColumn { get; init; }
    public bool IsLazyContinuation { get; init; }
    public bool IsIndentedContinuation { get; init; }
    public int OwnerBlock { get; init; } = -1;
    public string? CodeLanguage { get; init; }
    public IReadOnlyList<SyntaxToken>? SyntaxTokens { get; init; }

    public bool HasStyleAt(int offset, InlineStyle targetStyle)
    {
        foreach (var run in Runs)
        {
            if (offset >= run.Start && offset < run.Start + run.Length)
            {
                if (run.Style == targetStyle)
                    return true;
                if (run.Style == InlineStyle.BoldItalic &&
                    (targetStyle == InlineStyle.Bold || targetStyle == InlineStyle.Italic))
                    return true;
                return false;
            }
        }
        return false;
    }
}

public static class MarkdownParser
{
    public static List<ParsedBlock> Parse(Func<int, string> getBlockText, int blockCount)
        => Parse(getBlockText, blockCount, null);

    internal static List<ParsedBlock> Parse(Func<int, string> getBlockText, int blockCount,
        SyntaxHighlighter? highlighter)
    {
        var (defs, theme) = CollectDefinitions(getBlockText, blockCount);

        var result = new List<ParsedBlock>(blockCount);
        int fenceLen = 0;
        char fenceChar = '\0';
        string? fenceLanguage = null;

        for (int i = 0; i < blockCount; i++)
        {
            string text = getBlockText(i);
            var fenceInfo = GetFenceInfo(text);

            if (fenceLen == 0 && fenceInfo.Count > 0)
            {
                fenceLen = fenceInfo.Count;
                fenceChar = fenceInfo.Char;
                fenceLanguage = fenceInfo.Language;
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.FencedCodeLine,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                    IsFenceDelimiter = true,
                    CodeLanguage = fenceLanguage,
                });
                continue;
            }

            if (fenceLen > 0)
            {
                bool isClosing = fenceInfo.Count >= fenceLen && fenceInfo.Char == fenceChar && fenceInfo.Language == null;
                if (isClosing)
                    fenceLen = 0;
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.FencedCodeLine,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                    IsFenceDelimiter = isClosing,
                    CodeLanguage = fenceLanguage,
                });
                if (isClosing)
                    fenceLanguage = null;
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

            if (IsThemeBlockStart(text))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.ThemeDefinition,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                for (int j = i + 1; j < blockCount; j++)
                {
                    string inner = getBlockText(j);
                    result.Add(new ParsedBlock
                    {
                        Kind = BlockKind.ThemeDefinition,
                        Runs = [new StyledRun(0, inner.Length, InlineStyle.Normal)],
                    });
                    i = j;
                    if (inner.TrimEnd().EndsWith(CommentClose))
                        break;
                }
                continue;
            }

            if (IsPageBreak(text))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.PageBreak,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            bool hasDivOpen = TryExtractDivOpen(text, out int divOpenTagEnd);
            bool hasDivClose = TryExtractDivClose(text, out int divCloseTagStart);

            if (hasDivOpen || hasDivClose)
            {
                int contentStart = hasDivOpen ? divOpenTagEnd : 0;
                int contentEnd = hasDivClose ? divCloseTagStart : text.Length;
                bool hasContent = contentEnd > contentStart
                                  && text.AsSpan()[contentStart..contentEnd].Trim().Length > 0;

                if (!hasContent)
                {
                    if (hasDivOpen && !hasDivClose)
                    {
                        result.Add(new ParsedBlock
                        {
                            Kind = BlockKind.ColorDivOpen,
                            Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                            BlockColor = ParseDivProperties(text, theme),
                        });
                        continue;
                    }

                    if (hasDivClose && !hasDivOpen)
                    {
                        result.Add(new ParsedBlock
                        {
                            Kind = BlockKind.ColorDivClose,
                            Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                        });
                        continue;
                    }
                }
            }

            BlockColor? divOpenColor = hasDivOpen ? ParseDivProperties(text[..divOpenTagEnd], theme) : null;
            bool blockHasDivClose = hasDivClose;

            if (TryParseLinkDefinition(text, out _, out _, out _))
            {
                result.Add(new ParsedBlock
                {
                    Kind = BlockKind.LinkDefinition,
                    Runs = [new StyledRun(0, text.Length, InlineStyle.Normal)],
                });
                continue;
            }

            var kind = ClassifyBlock(text, out int leadingSpaces, out int leadingColumns);
            List<InlineImage>? images = null;
            List<InlineLink>? links = null;
            List<EmphasisMarker>? emphasisMarkers = null;
            bool isCode = kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;
            var runs = isCode
                ? [new StyledRun(0, text.Length, InlineStyle.Normal)]
                : ParseInlines(text, out images, out links, out emphasisMarkers, defs);

            var colorSpans = isCode ? null : ParseInlineColorTags(text, theme);

            result.Add(new ParsedBlock
            {
                Kind = kind, Runs = runs, Images = images, Links = links,
                EmphasisMarkers = emphasisMarkers, ColorSpans = colorSpans,
                DivOpenColor = divOpenColor, HasDivClose = blockHasDivClose,
                LeadingSpaces = leadingSpaces,
                ContentColumn = GetContentColumn(kind, text, leadingSpaces, leadingColumns),
            });
        }

        DetectSetextHeadings(result, getBlockText);
        DetectTables(result, getBlockText);
        DetectContinuations(result, getBlockText);
        DetectIndentedCode(result, getBlockText, defs);
        ApplyBlockDivColors(result);
        ApplySyntaxHighlighting(result, getBlockText, highlighter);

        return result;
    }

    private static (Dictionary<string, (string Url, string? Title)>? Defs, Dictionary<string, RgbColor>? Theme)
        CollectDefinitions(Func<int, string> getBlockText, int blockCount)
    {
        Dictionary<string, (string Url, string? Title)>? defs = null;
        Dictionary<string, RgbColor>? theme = null;
        int fenceLen = 0;
        char fenceC = '\0';
        for (int i = 0; i < blockCount; i++)
        {
            string text = getBlockText(i);
            var fi = GetFenceInfo(text);
            if (fenceLen == 0 && fi.Count > 0) { fenceLen = fi.Count; fenceC = fi.Char; continue; }
            if (fenceLen > 0) { if (fi.Count >= fenceLen && fi.Char == fenceC && fi.Language == null) fenceLen = 0; continue; }

            if (TryParseLinkDefinition(text, out string? label, out string? url, out string? title))
            {
                defs ??= new(StringComparer.OrdinalIgnoreCase);
                defs.TryAdd(label!, (url!, title));
            }

            string? themeText = null;
            if (IsThemeBlock(text))
            {
                themeText = text;
            }
            else if (IsThemeBlockStart(text))
            {
                var joined = new StringBuilder(text);
                int j = i + 1;
                while (j < blockCount)
                {
                    joined.Append('\n').Append(getBlockText(j));
                    if (getBlockText(j).TrimEnd().EndsWith(CommentClose))
                        break;
                    j++;
                }
                if (j < blockCount)
                {
                    themeText = joined.ToString();
                    i = j;
                }
            }

            if (themeText != null)
            {
                var parsed = ParseThemeBlock(themeText);
                if (parsed.Count > 0)
                {
                    theme ??= new(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in parsed)
                        theme[kvp.Key] = kvp.Value;
                }
            }
        }
        return (defs, theme);
    }

    private static void ApplySyntaxHighlighting(List<ParsedBlock> blocks,
        Func<int, string> getBlockText, SyntaxHighlighter? highlighter)
    {
        if (highlighter == null) return;

        int i = 0;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind != BlockKind.FencedCodeLine || !blocks[i].IsFenceDelimiter)
            {
                i++;
                continue;
            }

            string? language = blocks[i].CodeLanguage;
            int fenceStart = i;
            i++;

            int contentStart = i;
            while (i < blocks.Count && blocks[i].Kind == BlockKind.FencedCodeLine && !blocks[i].IsFenceDelimiter)
                i++;
            int contentEnd = i;

            if (i < blocks.Count && blocks[i].IsFenceDelimiter)
                i++;

            if (language == null || contentEnd <= contentStart)
                continue;

            var lines = new List<string>(contentEnd - contentStart);
            for (int j = contentStart; j < contentEnd; j++)
                lines.Add(getBlockText(j));

            var tokenized = highlighter.Tokenize(language, lines);
            if (tokenized == null) continue;

            for (int j = 0; j < tokenized.Length; j++)
            {
                int blockIdx = contentStart + j;
                if (tokenized[j].Count > 0)
                    blocks[blockIdx] = blocks[blockIdx] with { SyntaxTokens = tokenized[j] };
            }
        }
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

            if (block.DivOpenColor != null)
                divStack.Push(block.DivOpenColor.Value);

            if (divStack.Count > 0 && block.Kind != BlockKind.ThemeDefinition)
            {
                var merged = MergeBlockColors(divStack);
                blocks[i] = block with { BlockColor = merged };
            }

            if (block.HasDivClose && divStack.Count > 0)
                divStack.Pop();
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
        int titleEnd = titleStart;
        while (titleEnd < text.Length && text[titleEnd] != qClose)
        {
            if (text[titleEnd] == '\\' && titleEnd + 1 < text.Length) titleEnd++;
            titleEnd++;
        }
        if (titleEnd >= text.Length) return false;
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

            blocks[i] = blocks[i] with
            {
                Kind = BlockKind.TableHeaderRow,
                TableRow = headerRow,
                Table = tableInfo,
            };

            var sepCells = ParseTableCells(sepText);
            blocks[i + 1] = blocks[i + 1] with
            {
                Kind = BlockKind.TableSeparatorRow,
                IsTableSeparator = true,
                TableRow = new TableRowInfo { Cells = sepCells },
                Table = tableInfo,
            };

            int j = i + 2;
            while (j < blocks.Count && blocks[j].Kind == BlockKind.Paragraph
                   && ContainsUnescapedPipe(getBlockText(j)))
            {
                var dataCells = ParseTableCells(getBlockText(j));
                blocks[j] = blocks[j] with
                {
                    Kind = BlockKind.TableDataRow,
                    TableRow = new TableRowInfo { Cells = dataCells },
                    Table = tableInfo,
                };
                j++;
            }

            i = j;
        }
    }

    private static void DetectSetextHeadings(List<ParsedBlock> blocks, Func<int, string> getBlockText)
    {
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            if (blocks[i].Kind != BlockKind.Paragraph || getBlockText(i).Length == 0)
                continue;

            var nextKind = blocks[i + 1].Kind;
            string nextText = getBlockText(i + 1);

            BlockKind? headingKind = null;
            if (nextKind is BlockKind.Paragraph or BlockKind.ThematicBreak && IsSetextUnderline(nextText, out char underlineChar))
            {
                headingKind = underlineChar == '=' ? BlockKind.Heading1 : BlockKind.Heading2;
            }

            if (headingKind == null)
                continue;

            blocks[i] = blocks[i] with { Kind = headingKind.Value };
            blocks[i + 1] = blocks[i + 1] with { Kind = BlockKind.SetextUnderline };
            i++;
        }
    }

    internal static bool IsSetextUnderline(string text, out char underlineChar)
    {
        underlineChar = '\0';
        int i = 0;
        while (i < text.Length && i < 3 && text[i] == ' ') i++;
        if (i >= text.Length) return false;
        char ch = text[i];
        if (ch is not '=' and not '-') return false;
        for (int j = i; j < text.Length; j++)
        {
            if (text[j] != ch && text[j] != ' ') return false;
        }
        if (i >= text.Length || text[i] != ch) return false;
        underlineChar = ch;
        return true;
    }

    private static bool IsContainerBlock(BlockKind kind) => kind is
        BlockKind.UnorderedListItem or BlockKind.OrderedListItem
        or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked
        or BlockKind.Blockquote;

    internal static int CountLeadingSpaces(string text)
    {
        int count = 0;
        while (count < text.Length && text[count] == ' ')
            count++;
        return count;
    }

    internal static (int chars, int columns) MeasureLeadingWhitespace(string text)
    {
        int col = 0, i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ') { col++; i++; }
            else if (text[i] == '\t') { col = ((col / 4) + 1) * 4; i++; }
            else break;
        }
        return (i, col);
    }

    internal static int CharsForColumns(string text, int targetColumns)
    {
        int col = 0, i = 0;
        while (i < text.Length && col < targetColumns)
        {
            if (text[i] == '\t') col = ((col / 4) + 1) * 4;
            else if (text[i] == ' ') col++;
            else break;
            i++;
        }
        return i;
    }

    private static void DetectContinuations(List<ParsedBlock> blocks, Func<int, string> getBlockText)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!IsContainerBlock(blocks[i].Kind))
                continue;

            int contentColumn = blocks[i].ContentColumn;
            int blankStart = -1;

            for (int j = i + 1; j < blocks.Count; j++)
            {
                string text = getBlockText(j);

                if (text.Length == 0)
                {
                    if (blankStart < 0) blankStart = j;
                    continue;
                }

                if (blankStart >= 0)
                {
                    if (MeasureLeadingWhitespace(text).columns >= contentColumn
                        && blocks[j].Kind is BlockKind.Paragraph or BlockKind.IndentedCodeLine)
                    {
                        for (int b = blankStart; b < j; b++)
                            blocks[b] = blocks[b] with { OwnerBlock = i };
                        blocks[j] = blocks[j] with { IsIndentedContinuation = true, OwnerBlock = i };
                        blankStart = -1;
                        continue;
                    }
                    break;
                }

                if (blocks[j].Kind is not BlockKind.Paragraph and not BlockKind.IndentedCodeLine)
                    break;

                blocks[j] = blocks[j] with { IsLazyContinuation = true, OwnerBlock = i };
            }
        }
    }

    private static void DetectIndentedCode(List<ParsedBlock> blocks, Func<int, string> getBlockText,
        Dictionary<string, (string Url, string? Title)>? defs)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != BlockKind.IndentedCodeLine)
                continue;
            if (blocks[i].IsLazyContinuation || blocks[i].IsIndentedContinuation)
                continue;

            string text = getBlockText(i);
            if (text.Length == 0)
                continue;

            bool canStart = true;
            if (i > 0)
            {
                string prevText = getBlockText(i - 1);
                if (prevText.Length > 0 && blocks[i - 1].Kind == BlockKind.Paragraph)
                    canStart = false;
            }

            if (!canStart)
            {
                blocks[i] = ReclassifyAsParagraph(blocks[i], text, defs);
                continue;
            }

            int lastCodeLine = i;
            int j = i + 1;
            while (j < blocks.Count)
            {
                string jText = getBlockText(j);
                if (jText.Length == 0)
                {
                    j++;
                    continue;
                }

                if (blocks[j].Kind != BlockKind.IndentedCodeLine)
                    break;
                if (blocks[j].IsLazyContinuation || blocks[j].IsIndentedContinuation)
                    break;

                for (int k = lastCodeLine + 1; k < j; k++)
                    blocks[k] = blocks[k] with { Kind = BlockKind.IndentedCodeLine };

                lastCodeLine = j;
                j++;
            }

            i = j - 1;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != BlockKind.IndentedCodeLine)
                continue;
            if (blocks[i].IsLazyContinuation)
            {
                blocks[i] = ReclassifyAsParagraph(blocks[i], getBlockText(i), defs);
            }
            else if (blocks[i].IsIndentedContinuation)
            {
                int ownerCC = blocks[blocks[i].OwnerBlock].ContentColumn;
                int indent = MeasureLeadingWhitespace(getBlockText(i)).columns;
                if (indent < ownerCC + 4)
                    blocks[i] = ReclassifyAsParagraph(blocks[i], getBlockText(i), defs);
            }
        }
    }

    private static ParsedBlock ReclassifyAsParagraph(ParsedBlock block, string text,
        Dictionary<string, (string Url, string? Title)>? defs)
    {
        return block with
        {
            Kind = BlockKind.Paragraph,
            Runs = ParseInlines(text, out var imgs, out var lnks, out var emph, defs),
            Images = imgs, Links = lnks, EmphasisMarkers = emph,
        };
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

    public static bool IsFenceLine(string text) => GetFenceInfo(text).Count > 0;

    public static int GetFenceBacktickCount(string text) => GetFenceInfo(text).Count;

    internal static (int Count, string? Language, char Char) GetFenceInfo(string text)
    {
        var (chars, cols) = MeasureLeadingWhitespace(text);
        if (cols >= 4) return (0, null, '\0');
        var trimmed = chars > 0 ? text[chars..] : text;

        char fenceChar;
        if (trimmed.StartsWith("```")) fenceChar = '`';
        else if (trimmed.StartsWith("~~~")) fenceChar = '~';
        else return (0, null, '\0');

        int count = 0;
        while (count < trimmed.Length && trimmed[count] == fenceChar) count++;
        var infoString = trimmed[count..];
        if (fenceChar == '`' && infoString.Contains('`')) return (0, null, '\0');
        var lang = infoString.Trim().Split(' ', 2)[0];
        return (count, lang.Length > 0 ? lang : null, fenceChar);
    }

    internal static BlockKind ClassifyBlock(string text) => ClassifyBlock(text, out _, out _);

    internal static BlockKind ClassifyBlock(string text, out int leadingSpaces) =>
        ClassifyBlock(text, out leadingSpaces, out _);

    internal static BlockKind ClassifyBlock(string text, out int leadingSpaces, out int leadingColumns)
    {
        var (chars, cols) = MeasureLeadingWhitespace(text);
        if (cols >= 4) { leadingSpaces = 0; leadingColumns = 0; return BlockKind.IndentedCodeLine; }
        leadingSpaces = chars;
        leadingColumns = cols;
        if (chars > 0) text = text[chars..];

        if (text.StartsWith("######") && (text.Length == 6 || text[6] == ' '))
            return BlockKind.Heading6;
        if (text.StartsWith("#####") && !text.StartsWith("######") && (text.Length == 5 || text[5] == ' '))
            return BlockKind.Heading5;
        if (text.StartsWith("####") && !text.StartsWith("#####") && (text.Length == 4 || text[4] == ' '))
            return BlockKind.Heading4;
        if (text.StartsWith("###") && !text.StartsWith("####") && (text.Length == 3 || text[3] == ' '))
            return BlockKind.Heading3;
        if (text.StartsWith("##") && !text.StartsWith("###") && (text.Length == 2 || text[2] == ' '))
            return BlockKind.Heading2;
        if (text.StartsWith("#") && !text.StartsWith("##") && (text.Length == 1 || text[1] == ' '))
            return BlockKind.Heading1;

        if (IsThematicBreak(text))
            return BlockKind.ThematicBreak;

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

        if (text.StartsWith(">"))
            return BlockKind.Blockquote;

        leadingSpaces = 0;
        leadingColumns = 0;
        return BlockKind.Paragraph;
    }

    internal static int GetContentColumn(BlockKind kind, string text, int leadingChars = 0, int leadingColumns = 0)
    {
        if (kind == BlockKind.Blockquote)
            return leadingColumns + 2;

        int rawMarkerWidth = kind switch
        {
            BlockKind.UnorderedListItem
                or BlockKind.TaskListItemUnchecked
                or BlockKind.TaskListItemChecked => 1,
            BlockKind.OrderedListItem =>
                GetOrderedListPrefixLength(leadingChars > 0 ? text[leadingChars..] : text) - 1,
            _ => 0,
        };
        if (rawMarkerWidth <= 0) return 0;

        int markerEndCol = leadingColumns + rawMarkerWidth;
        int i = leadingChars + rawMarkerWidth;
        int col = markerEndCol;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
        {
            if (text[i] == '\t')
                col = ((col / 4) + 1) * 4;
            else
                col++;
            i++;
        }

        if (col - markerEndCol > 4 || i >= text.Length)
            return markerEndCol + 1;

        return col;
    }

    internal static bool IsThematicBreak(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        if (i >= text.Length) return false;
        char marker = text[i];
        if (marker is not '-' and not '*' and not '_') return false;
        int count = 0;
        for (int j = i; j < text.Length; j++)
        {
            if (text[j] == marker) count++;
            else if (text[j] is ' ' or '\t') continue;
            else return false;
        }
        return count >= 3;
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

    internal static int GetContentEnd(string text)
    {
        int end = text.Length;
        while (true)
        {
            var span = text.AsSpan(0, end);
            if (span.EndsWith("<!--/@fg-->".AsSpan(), StringComparison.Ordinal))
                end -= 11;
            else if (span.EndsWith("<!--/@bg-->".AsSpan(), StringComparison.Ordinal))
                end -= 11;
            else if (span.EndsWith("<!--/@-->".AsSpan(), StringComparison.Ordinal))
                end -= 9;
            else
                break;
        }
        return end;
    }

    public static bool IsTrailingHardBreak(ParsedBlock parsed, string blockText)
    {
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return false;

        int end = GetContentEnd(blockText);
        if (end == 0) return false;

        if (blockText[end - 1] != '\\') return false;

        int count = 0;
        for (int i = end - 1; i >= 0 && blockText[i] == '\\'; i--)
            count++;
        if (count % 2 == 0) return false;

        int backslashPos = end - 1;
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
        return ParseInlines(text, out images, out links, out _, defs);
    }

    internal static List<StyledRun> ParseInlines(string text, out List<InlineImage>? images, out List<InlineLink>? links,
        out List<EmphasisMarker>? emphasisMarkers, Dictionary<string, (string Url, string? Title)>? defs = null)
    {
        images = null;
        links = null;
        emphasisMarkers = null;
        if (text.Length == 0)
            return [new StyledRun(0, 0, InlineStyle.Normal)];

        var styles = new InlineStyle[text.Length];

        MarkCodeSpans(text, styles);
        MarkBackslashEscapes(text, styles);
        images = MarkImages(text, styles, defs);
        links = MarkLinks(text, styles, defs);
        links = MarkAutolinks(text, styles, links);
        MarkStrikethrough(text, styles);
        MarkEmphasis(text, styles, out emphasisMarkers);

        return BuildRuns(styles);
    }

    private static void MarkBackslashEscapes(string text, InlineStyle[] styles)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (styles[i] != InlineStyle.Normal) continue;
            if (text[i] == '\\' && IsAsciiPunctuation(text[i + 1]))
            {
                styles[i] = InlineStyle.Bold; // backslash: hidden in emitter
                styles[i + 1] = InlineStyle.Image; // escaped char: skipped by subsequent parsers, shown as Normal
                i++;
            }
        }
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '*' or '+' or ','
        or '-' or '.' or '/' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '[' or '\\'
        or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';

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
        while (i <= text.Length - 4) // minimum: ![x] (shortcut reference)
        {
            if (text[i] != '!' || styles[i] != InlineStyle.Normal ||
                i + 1 >= text.Length || text[i + 1] != '[')
            {
                i++;
                continue;
            }

            int altStart = i + 2;
            int bracketClose = FindMatchingBracket(text, altStart);
            if (bracketClose < 0)
            {
                i++;
                continue;
            }

            string altText = text[altStart..bracketClose];
            string url;
            string? title;
            int end;

            if (bracketClose + 1 < text.Length && text[bracketClose + 1] == '(')
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
        while (i <= text.Length - 3) // minimum: [x] (shortcut reference)
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
            if (bracketClose < 0)
            {
                i++;
                continue;
            }

            string linkText = text[textStart..bracketClose];
            string url;
            string? title;
            int end;
            string? refLabel = null;

            if (bracketClose + 1 < text.Length && text[bracketClose + 1] == '(')
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

            if (text[i] == '<')
            {
                links = TryAngleBracketAutolink(text, styles, links, i, out int abEnd);
                if (abEnd > i) { i = abEnd - 1; continue; }
            }

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

    private static List<InlineLink>? TryAngleBracketAutolink(string text, InlineStyle[] styles,
        List<InlineLink>? links, int start, out int end)
    {
        end = start;
        int closeAngle = text.IndexOf('>', start + 1);
        if (closeAngle < 0 || closeAngle == start + 1) return links;

        for (int j = start + 1; j < closeAngle; j++)
        {
            char c = text[j];
            if (c == '<' || c == ' ' || c == '\t') return links;
        }

        var inner = text.AsSpan(start + 1, closeAngle - start - 1);

        int colonPos = inner.IndexOf(':');
        if (colonPos >= 2 && colonPos <= 32)
        {
            bool schemeValid = true;
            for (int j = 0; j < colonPos; j++)
            {
                if (!char.IsAsciiLetter(inner[j])) { schemeValid = false; break; }
            }
            if (schemeValid && inner.Length > colonPos + 1)
            {
                int totalLength = closeAngle - start + 1;
                string innerStr = inner.ToString();
                links ??= [];
                links.Add(new InlineLink(start, totalLength, innerStr, innerStr, null, IsAngleBracket: true));
                for (int j = start; j <= closeAngle; j++)
                    styles[j] = InlineStyle.Link;
                end = closeAngle + 1;
                return links;
            }
        }

        if (IsEmailAutolink(inner))
        {
            int totalLength = closeAngle - start + 1;
            string innerStr = inner.ToString();
            links ??= [];
            links.Add(new InlineLink(start, totalLength, innerStr, "mailto:" + innerStr, null, IsAngleBracket: true));
            for (int j = start; j <= closeAngle; j++)
                styles[j] = InlineStyle.Link;
            end = closeAngle + 1;
            return links;
        }

        return links;
    }

    private static bool IsEmailAutolink(ReadOnlySpan<char> s)
    {
        int at = s.IndexOf('@');
        if (at < 1 || at == s.Length - 1) return false;

        for (int i = 0; i < at; i++)
        {
            char c = s[i];
            if (char.IsAsciiLetterOrDigit(c)) continue;
            if (".!#$%&'*+/=?^_`{|}~-".Contains(c)) continue;
            return false;
        }

        int domainStart = at + 1;
        if (s[domainStart] == '-' || s[domainStart] == '.') return false;
        if (s[^1] == '-' || s[^1] == '.') return false;

        bool hasDot = false;
        for (int i = domainStart; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsAsciiLetterOrDigit(c)) continue;
            if (c == '-') continue;
            if (c == '.')
            {
                if (i + 1 < s.Length && (s[i + 1] == '.' || s[i + 1] == '-')) return false;
                hasDot = true;
                continue;
            }
            return false;
        }

        return hasDot;
    }

    private static bool TryResolveReference(string text, int bracketClose, string fallbackLabel,
        Dictionary<string, (string Url, string? Title)>? defs, out string url, out string? title, out int end, out string refLabel)
    {
        url = ""; title = null; end = 0; refLabel = "";
        if (defs == null) return false;

        // Full reference [text][ref] or collapsed reference [text][]
        if (bracketClose + 1 < text.Length && text[bracketClose + 1] == '[')
        {
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

        // Shortcut reference [text]
        if (defs.TryGetValue(fallbackLabel, out var shortcutDef))
        {
            url = shortcutDef.Url;
            title = shortcutDef.Title;
            end = bracketClose + 1;
            refLabel = fallbackLabel;
            return true;
        }

        return false;
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

            int closeStart = FindClosingDelimiter(text, styles, i, 2, '~');
            if (closeStart >= 0)
            {
                for (int k = openStart; k < closeStart + 2; k++)
                    styles[k] = InlineStyle.Strikethrough;
                i = closeStart + 2;
            }
        }
    }

    private static void MarkEmphasis(string text, InlineStyle[] styles, out List<EmphasisMarker>? markers)
    {
        markers = null;

        var delimiters = new List<(int Pos, int Count, bool CanOpen, bool CanClose, char Char)>();
        int i = 0;
        while (i < text.Length)
        {
            if (styles[i] != InlineStyle.Normal || (text[i] != '*' && text[i] != '_')) { i++; continue; }

            char dc = text[i];
            int start = i;
            while (i < text.Length && text[i] == dc && styles[i] == InlineStyle.Normal) i++;
            int count = i - start;

            char before = start > 0 ? text[start - 1] : ' ';
            char after = i < text.Length ? text[i] : ' ';
            bool leftFlanking = !char.IsWhiteSpace(after)
                && (!char.IsPunctuation(after) || char.IsWhiteSpace(before) || char.IsPunctuation(before));
            bool rightFlanking = !char.IsWhiteSpace(before)
                && (!char.IsPunctuation(before) || char.IsWhiteSpace(after) || char.IsPunctuation(after));

            bool canOpen, canClose;
            if (dc == '*')
            {
                canOpen = leftFlanking;
                canClose = rightFlanking;
            }
            else // '_'
            {
                canOpen = leftFlanking && (!rightFlanking || char.IsPunctuation(before));
                canClose = rightFlanking && (!leftFlanking || char.IsPunctuation(after));
            }

            delimiters.Add((start, count, canOpen, canClose, dc));
        }

        for (int ci = 0; ci < delimiters.Count; ci++)
        {
            var closer = delimiters[ci];
            if (!closer.CanClose || closer.Count == 0) continue;

            for (int oi = ci - 1; oi >= 0; oi--)
            {
                var opener = delimiters[oi];
                if (!opener.CanOpen || opener.Count == 0) continue;
                if (opener.Char != closer.Char) continue;

                if ((opener.CanClose || closer.CanOpen) && (opener.Count + closer.Count) % 3 == 0
                    && opener.Count % 3 != 0 && closer.Count % 3 != 0)
                    continue;

                int consume = (opener.Count >= 2 && closer.Count >= 2) ? 2 : 1;
                var emphStyle = consume == 2 ? InlineStyle.Bold : InlineStyle.Italic;

                int markerOpenStart = opener.Pos + opener.Count - consume;
                int markerCloseStart = closer.Pos;

                for (int j = markerOpenStart; j < markerCloseStart + consume; j++)
                {
                    if (styles[j] == InlineStyle.Normal)
                        styles[j] = emphStyle;
                    else if ((styles[j] == InlineStyle.Bold && emphStyle == InlineStyle.Italic)
                          || (styles[j] == InlineStyle.Italic && emphStyle == InlineStyle.Bold))
                        styles[j] = InlineStyle.BoldItalic;
                }

                markers ??= new();
                markers.Add(new EmphasisMarker(markerOpenStart, consume));
                markers.Add(new EmphasisMarker(markerCloseStart, consume));

                opener = opener with { Count = opener.Count - consume };
                closer = closer with { Count = closer.Count - consume, Pos = closer.Pos + consume };
                delimiters[oi] = opener;
                delimiters[ci] = closer;

                for (int ri = oi + 1; ri < ci; ri++)
                    delimiters[ri] = delimiters[ri] with { Count = 0 };

                if (closer.Count > 0) ci--;
                break;
            }
        }
    }

    public static int GetMarkerLength(InlineStyle style) => style switch
    {
        InlineStyle.Bold => 2,
        InlineStyle.Italic => 1,
        InlineStyle.BoldItalic => 3,
        InlineStyle.Strikethrough => 2,
        _ => 0,
    };

    private static int FindClosingDelimiter(string text, InlineStyle[] styles, int searchFrom, int count, char delimiter)
    {
        for (int i = searchFrom; i <= text.Length - count; i++)
        {
            if (styles[i] != InlineStyle.Normal) continue;
            if (text[i] != delimiter) continue;

            int run = 0;
            int start = i;
            while (i < text.Length && text[i] == delimiter && styles[i] == InlineStyle.Normal)
            {
                run++;
                i++;
            }

            if (run >= count) return start;
            i = start;
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
    private const string PageBreakTag = "<!--@pagebreak-->";

    internal static bool IsPageBreak(string text) =>
        text.AsSpan().Trim().Equals(PageBreakTag, StringComparison.OrdinalIgnoreCase);

    internal static bool IsThemeBlock(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.StartsWith(ThemeOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
               && trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal);
    }

    internal static bool IsThemeBlockStart(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.StartsWith(ThemeOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
               && !trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal);
    }

    internal static bool IsColorDivOpen(string text) => TryExtractDivOpen(text, out _);

    internal static bool IsColorDivClose(string text) => TryExtractDivClose(text, out _);

    internal static bool TryExtractDivOpen(string text, out int tagEnd)
    {
        tagEnd = 0;
        var span = text.AsSpan();
        int leading = span.Length - span.TrimStart().Length;
        var after = span[leading..];
        if (!after.StartsWith(DivOpen.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;
        int closeIdx = text.IndexOf(CommentClose, leading + DivOpen.Length, StringComparison.Ordinal);
        if (closeIdx < 0) return false;
        tagEnd = closeIdx + CommentClose.Length;
        return true;
    }

    internal static bool TryExtractDivClose(string text, out int tagStart)
    {
        tagStart = 0;
        var span = text.AsSpan();
        var trimmedEnd = span.TrimEnd();
        if (!trimmedEnd.EndsWith(DivClose.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;
        tagStart = trimmedEnd.Length - DivClose.Length;
        return true;
    }

    internal static int FindInlineColorOpenEnd(string text)
    {
        if (!text.StartsWith("<!--@", StringComparison.Ordinal))
            return -1;
        if (text.Length > 5 && text[5] == '/')
            return -1;
        int close = text.IndexOf("-->", 5, StringComparison.Ordinal);
        if (close < 0) return -1;
        return close + 3;
    }

    internal static int FindInlineColorCloseStart(string text)
    {
        int idx = text.LastIndexOf("<!--/@", StringComparison.Ordinal);
        if (idx < 0) return -1;
        int close = text.IndexOf("-->", idx + 6, StringComparison.Ordinal);
        if (close < 0) return -1;
        if (close + 3 != text.Length) return -1;
        return idx;
    }

    internal static string InlineOpenToDivOpen(string tag)
    {
        var body = tag.AsSpan()[5..^3].Trim();
        return $"{DivOpen}{body}{CommentClose}";
    }

    internal static Dictionary<string, RgbColor> ParseThemeBlock(string text)
    {
        var result = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase);
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith(ThemeOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(CommentClose.AsSpan(), StringComparison.Ordinal))
            return result;

        var body = trimmed[ThemeOpen.Length..^CommentClose.Length].ToString();
        var entries = body.Contains('\n')
            ? SplitLines(body)
            : new List<string>(body.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var rawLine in entries)
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

    internal static bool FindNextColorTag(string text, ref int pos,
        out int tagStart, out int tagEnd, out bool isOpener, out int bodyStart, out int bodyEnd)
    {
        tagStart = tagEnd = bodyStart = bodyEnd = 0;
        isOpener = false;

        while (pos < text.Length - 6)
        {
            if (text[pos] != '<' || pos + 4 >= text.Length
                || text[pos + 1] != '!' || text[pos + 2] != '-' || text[pos + 3] != '-')
            {
                pos++;
                continue;
            }

            if (text[pos + 4] == '@')
            {
                int closeIdx = text.IndexOf("-->", pos + 5, StringComparison.Ordinal);
                if (closeIdx < 0) { pos++; continue; }
                tagStart = pos;
                tagEnd = closeIdx + 3;
                isOpener = true;
                bodyStart = pos + 5;
                bodyEnd = closeIdx;
                pos = tagEnd;
                return true;
            }

            if (pos + 5 < text.Length && text[pos + 4] == '/' && text[pos + 5] == '@')
            {
                int closeIdx = text.IndexOf("-->", pos + 6, StringComparison.Ordinal);
                if (closeIdx < 0) { pos++; continue; }
                tagStart = pos;
                tagEnd = closeIdx + 3;
                isOpener = false;
                bodyStart = pos + 6;
                bodyEnd = closeIdx;
                pos = tagEnd;
                return true;
            }

            pos++;
        }
        return false;
    }

    internal static List<ColorSpan>? ParseInlineColorTags(string text, Dictionary<string, RgbColor>? theme)
    {
        List<ColorSpan>? spans = null;
        var openFg = new Stack<(int tagEnd, RgbColor color)>();
        var openBg = new Stack<(int tagEnd, RgbColor color)>();

        int pos = 0;
        while (FindNextColorTag(text, ref pos, out int tagStart, out int tagEnd, out bool isOpener,
                   out int bodyStart, out int bodyEnd))
        {
            var body = text.AsSpan()[bodyStart..bodyEnd].Trim();

            if (isOpener)
            {
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
            }
            else
            {
                bool closeFg = body.Equals("fg".AsSpan(), StringComparison.OrdinalIgnoreCase)
                               || body.IsEmpty;
                bool closeBg = body.Equals("bg".AsSpan(), StringComparison.OrdinalIgnoreCase)
                               || body.IsEmpty;

                if (closeFg && openFg.Count > 0)
                {
                    var (start, color) = openFg.Pop();
                    if (start < tagStart)
                    {
                        spans ??= [];
                        AddOrMergeColorSpan(ref spans, start, tagStart - start, color, null);
                    }
                }
                if (closeBg && openBg.Count > 0)
                {
                    var (start, color) = openBg.Pop();
                    if (start < tagStart)
                    {
                        spans ??= [];
                        AddOrMergeColorSpan(ref spans, start, tagStart - start, null, color);
                    }
                }
            }
        }

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

        spans?.Sort((a, b) =>
        {
            int cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
        });

        return spans;
    }

    internal static List<HiddenRange>? FindInlineColorTagRanges(string text)
    {
        List<HiddenRange>? ranges = null;
        int pos = 0;
        while (FindNextColorTag(text, ref pos, out int tagStart, out int tagEnd, out _, out _, out _))
        {
            ranges ??= [];
            ranges.Add(new HiddenRange(tagStart, tagEnd - tagStart));
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

    private static List<string> SplitLines(string str)
    {
        var lines = new List<string>();
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

    internal static string? TryGetColorName(RgbColor color)
    {
        RgbToName ??= BuildRgbToName();
        return RgbToName.GetValueOrDefault(color);
    }

    private static Dictionary<RgbColor, string>? RgbToName;

    private static Dictionary<RgbColor, string> BuildRgbToName()
    {
        var map = new Dictionary<RgbColor, string>();
        foreach (var (name, rgb) in CssNamedColors)
            map.TryAdd(rgb, name);
        return map;
    }

    internal static bool TryGetNamedColor(ReadOnlySpan<char> name, out RgbColor color)
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
