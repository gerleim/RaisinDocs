using System.Linq;
using System.Text;
using RaisinDocs;

namespace RaisinDocs.Html;

public class HtmlEmitterOptions
{
    public bool IncludeColorExtensions { get; set; } = true;
    internal Dictionary<string, (string Url, string? Title)>? LinkDefinitions { get; set; }
}

public static class HtmlEmitter
{
    static readonly string[] DisallowedHtmlTags = [
        "title", "textarea", "style", "xmp", "iframe",
        "noembed", "noframes", "script", "plaintext"
    ];
    public static string Render(string markdown, HtmlEmitterOptions? options = null)
    {
        options ??= new HtmlEmitterOptions();
        var lines = SplitLines(markdown);
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Contains('\t'))
                lines[i] = ExpandTabs(lines[i]);
        var multiLineDefs = ExtractMultiLineLinkDefs(lines);
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Count, out var linkDefs);
        if (multiLineDefs != null)
        {
            linkDefs ??= new Dictionary<string, (string Url, string? Title)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in multiLineDefs)
                linkDefs.TryAdd(k, v);
        }
        ExtractContainerLinkDefs(blocks, lines, ref linkDefs);
        options.LinkDefinitions = linkDefs;
        return RenderBlocks(blocks, lines, options, 0);
    }

    static void ExtractContainerLinkDefs(List<ParsedBlock> blocks, List<string> lines,
        ref Dictionary<string, (string Url, string? Title)>? linkDefs)
    {
        int i = 0;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind != BlockKind.Blockquote) { i++; continue; }

            var innerLines = new List<string>();
            bool canLazy = false;
            int j = i;
            while (j < blocks.Count)
            {
                if (blocks[j].Kind == BlockKind.Blockquote)
                {
                    string stripped = StripBlockquotePrefix(lines[j]);
                    innerLines.Add(stripped);
                    canLazy = !string.IsNullOrWhiteSpace(stripped) && !IsBlockStructureStart(stripped);
                    j++;
                }
                else if (blocks[j].Kind is BlockKind.Paragraph or BlockKind.IndentedCodeLine
                    && !string.IsNullOrWhiteSpace(lines[j])
                    && innerLines.Count > 0 && canLazy)
                {
                    innerLines.Add(lines[j]);
                    j++;
                }
                else break;
            }

            var innerBlocks = MarkdownParser.Parse(idx => innerLines[idx], innerLines.Count, out var innerDefs);
            if (innerDefs != null)
            {
                linkDefs ??= new Dictionary<string, (string Url, string? Title)>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in innerDefs)
                    linkDefs.TryAdd(k, v);
            }
            ExtractContainerLinkDefs(innerBlocks, innerLines, ref linkDefs);

            i = j;
        }
    }

    static string ExpandTabs(string line)
    {
        var sb = new StringBuilder();
        int col = 0;
        int i = 0;

        // Phase 1: expand tabs in leading whitespace
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            if (line[i] == '\t')
            {
                int spaces = 4 - (col % 4);
                sb.Append(' ', spaces);
                col += spaces;
            }
            else
            {
                sb.Append(' ');
                col++;
            }
            i++;
        }

        // Phase 2: if at a block marker, expand tabs after it
        if (i < line.Length)
        {
            char ch = line[i];
            bool isMarker = ch is '>' or '-' or '*' or '+';
            if (!isMarker && char.IsDigit(ch))
            {
                int d = i;
                while (d < line.Length && char.IsDigit(line[d])) d++;
                isMarker = d < line.Length && line[d] is '.' or ')';
                if (isMarker)
                {
                    while (i <= d) { sb.Append(line[i]); col++; i++; }
                }
            }
            if (isMarker && !(ch is '>' or '-' or '*' or '+' && false))
            {
                if (ch is '>' or '-' or '*' or '+')
                { sb.Append(line[i]); col++; i++; }
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                {
                    if (line[i] == '\t')
                    {
                        int spaces = 4 - (col % 4);
                        sb.Append(' ', spaces);
                        col += spaces;
                    }
                    else
                    {
                        sb.Append(' ');
                        col++;
                    }
                    i++;
                }
            }
        }

        // Phase 3: append the rest as-is (preserving content tabs)
        if (i < line.Length)
            sb.Append(line, i, line.Length - i);

        return sb.ToString();
    }

    static List<string> SplitLines(string markdown)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < markdown.Length; i++)
        {
            if (markdown[i] == '\n')
            {
                int end = (i > 0 && markdown[i - 1] == '\r') ? i - 1 : i;
                lines.Add(markdown[start..end]);
                start = i + 1;
            }
        }
        if (start <= markdown.Length)
            lines.Add(markdown[start..]);
        if (lines.Count > 0 && lines[^1] == "")
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    static Dictionary<string, (string Url, string? Title)>? ExtractMultiLineLinkDefs(List<string> lines)
    {
        Dictionary<string, (string Url, string? Title)>? defs = null;
        bool inFencedCode = false;
        string? fenceMarker = null;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Track fenced code blocks
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                string marker = trimmed.StartsWith("```") ? "```" : "~~~";
                if (inFencedCode && fenceMarker != null && trimmed.StartsWith(fenceMarker))
                { inFencedCode = false; fenceMarker = null; continue; }
                else if (!inFencedCode)
                { inFencedCode = true; fenceMarker = marker; continue; }
            }
            if (inFencedCode) continue;

            // Skip HTML blocks, headings, blockquotes
            if (trimmed.StartsWith('#') || trimmed.StartsWith('>')) continue;

            int indent = 0;
            while (indent < line.Length && indent < 3 && line[indent] == ' ') indent++;
            if (indent >= line.Length || line[indent] != '[') continue;

            // Find end of label (handling escaped brackets and multi-line labels)
            int labelStart = indent + 1;
            int bracketClose = -1;
            int labelEndLine = i;
            string labelText = line;

            // Try multi-line label: [\nfoo\n]: /url
            for (int li = i; li < lines.Count && li < i + 4; li++)
            {
                string searchIn = li == i ? line : labelText + "\n" + lines[li];
                if (li > i) labelText = searchIn;
                for (int k = labelStart; k < searchIn.Length; k++)
                {
                    if (searchIn[k] == '\\' && k + 1 < searchIn.Length) { k++; continue; }
                    if (searchIn[k] == '[') goto nextLine;
                    if (searchIn[k] == ']') { bracketClose = k; labelEndLine = li; goto foundLabel; }
                }
            }
            continue;
            foundLabel:

            if (bracketClose == labelStart) continue;
            if (bracketClose + 1 >= labelText.Length || labelText[bracketClose + 1] != ':') continue;

            string label = labelText[labelStart..bracketClose];
            // Normalize label: collapse internal whitespace
            label = System.Text.RegularExpressions.Regex.Replace(label.Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(label)) continue;

            // Parse URL and title from remainder (may span multiple lines)
            int afterColon = bracketClose + 2;
            string rest = labelText[afterColon..];
            // Gather continuation lines
            int consumed = labelEndLine;
            for (int li = labelEndLine + 1; li < lines.Count && li <= labelEndLine + 10; li++)
            {
                if (string.IsNullOrWhiteSpace(lines[li])) break;
                // Only continue if previous rest needs more (URL missing, title open)
                rest += "\n" + lines[li];
                consumed = li;
            }

            if (!TryParseDefUrlTitle(rest, out string? url, out string? title, out int linesUsed))
                continue;

            int totalLinesUsed = labelEndLine - i + linesUsed;
            // Only count as multi-line if more than 1 line consumed
            if (totalLinesUsed <= 1) continue;

            defs ??= new Dictionary<string, (string Url, string? Title)>(StringComparer.OrdinalIgnoreCase);
            var foldedLabel = label.ToLowerInvariant().Replace("ß", "ss");
            defs.TryAdd(foldedLabel, (url!, title));

            // Mark consumed lines as empty so the main parser skips them
            for (int li = i; li < i + totalLinesUsed && li < lines.Count; li++)
                lines[li] = "";
            i += totalLinesUsed - 1;
            continue;
            nextLine:;
        }

        return defs;
    }

    static bool TryParseDefUrlTitle(string text, out string? url, out string? title, out int linesUsed)
    {
        url = null; title = null; linesUsed = 1;
        int i = 0;

        // Skip whitespace (including newlines)
        while (i < text.Length && (text[i] == ' ' || text[i] == '\n')) i++;
        if (i >= text.Length) return false;

        // Count lines consumed by whitespace skip
        for (int k = 0; k < i; k++)
            if (text[k] == '\n') linesUsed++;

        // Parse URL
        int urlStart;
        if (text[i] == '<')
        {
            urlStart = i + 1;
            i++;
            while (i < text.Length && text[i] != '>' && text[i] != '\n') { if (text[i] == '\\' && i + 1 < text.Length) i++; i++; }
            if (i >= text.Length || text[i] != '>') return false;
            url = text[urlStart..i];
            i++;
        }
        else
        {
            urlStart = i;
            while (i < text.Length && text[i] != ' ' && text[i] != '\n') i++;
            url = text[urlStart..i];
        }

        if (url.Length == 0 && (urlStart == 0 || text[urlStart - 1] != '>')) return false;

        // Skip whitespace (including one newline for title continuation)
        int beforeTitle = i;
        int linesBeforeTitle = linesUsed;
        bool sawNewline = false;
        while (i < text.Length && (text[i] == ' ' || (!sawNewline && text[i] == '\n')))
        {
            if (text[i] == '\n') { sawNewline = true; linesUsed++; }
            i++;
        }

        // No title, URL only — don't consume extra lines
        if (i >= text.Length || text[i] == '\n')
        {
            linesUsed = linesBeforeTitle;
            return true;
        }

        // Parse title
        char q = text[i];
        char qClose = q == '"' ? '"' : q == '\'' ? '\'' : q == '(' ? ')' : '\0';
        if (qClose == '\0')
        {
            // Not a valid title — revert to URL-only, don't consume extra lines
            linesUsed = linesBeforeTitle;
            return sawNewline; // if title was on same line, it's invalid; if newline, URL stands alone
        }

        i++; // past opening quote
        int titleStart = i;
        while (i < text.Length && text[i] != qClose)
        {
            if (text[i] == '\\' && i + 1 < text.Length) i++;
            if (text[i] == '\n') linesUsed++;
            i++;
        }
        if (i >= text.Length) return false;
        title = text[titleStart..i];
        i++; // past closing quote

        // Rest of line must be blank
        while (i < text.Length && text[i] == ' ') i++;
        if (i < text.Length && text[i] != '\n') return false;

        return true;
    }

    static string RenderBlocks(List<ParsedBlock> blocks, List<string> lines, HtmlEmitterOptions options, int depth = 0)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < blocks.Count)
        {
            i = RenderBlock(sb, blocks, lines, i, options, depth: depth);
        }
        return sb.ToString();
    }

    static int RenderBlock(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int i, HtmlEmitterOptions options, int depth = 0)
    {
        var block = blocks[i];
        var text = lines[i];

        switch (block.Kind)
        {
            case BlockKind.Heading1:
            case BlockKind.Heading2:
            case BlockKind.Heading3:
            case BlockKind.Heading4:
            case BlockKind.Heading5:
            case BlockKind.Heading6:
                return RenderHeading(sb, block, text, i, options);

            case BlockKind.Paragraph:
                return RenderParagraph(sb, blocks, lines, i, options);

            case BlockKind.ThematicBreak:
                sb.Append("<hr />\n");
                return i + 1;

            case BlockKind.FencedCodeLine:
                return RenderFencedCode(sb, blocks, lines, i);

            case BlockKind.IndentedCodeLine:
                return RenderIndentedCode(sb, blocks, lines, i);

            case BlockKind.Blockquote:
                return RenderBlockquote(sb, blocks, lines, i, options, depth);

            case BlockKind.UnorderedListItem:
                return RenderUnorderedList(sb, blocks, lines, i, options);

            case BlockKind.OrderedListItem:
                return RenderOrderedList(sb, blocks, lines, i, options);

            case BlockKind.TaskListItemUnchecked:
            case BlockKind.TaskListItemChecked:
                return RenderTaskList(sb, blocks, lines, i, options);

            case BlockKind.TableHeaderRow:
                return RenderTable(sb, blocks, lines, i, options);

            case BlockKind.HtmlBlock:
                return RenderHtmlBlock(sb, blocks, lines, i);

            case BlockKind.SetextUnderline:
                return i + 1;

            case BlockKind.LinkDefinition:
            case BlockKind.ThemeDefinition:
            case BlockKind.ColorDivOpen:
            case BlockKind.ColorDivClose:
                return i + 1;

            default:
                sb.Append("<p>");
                AppendInlineContent(sb, block, text, options);
                sb.Append("</p>\n");
                return i + 1;
        }
    }

    static int RenderHeading(StringBuilder sb, ParsedBlock block, string text, int index, HtmlEmitterOptions options)
    {
        int level = block.Kind - BlockKind.Heading1 + 1;
        string content = GetHeadingContent(text);
        sb.Append($"<h{level}>");
        var innerBlocks = MarkdownParser.Parse(_ => content, 1);
        if (innerBlocks.Count > 0)
        {
            var innerBlock = innerBlocks[0];
            // Inherit link refs from outer parse that inner re-parse can't resolve
            if (innerBlock.Links == null && block.Links != null)
            {
                int prefixLen = GetHeadingPrefixLength(text);
                var adjustedLinks = block.Links.Select(l =>
                    new InlineLink(l.Start - prefixLen, l.Length, l.Text, l.Url, l.Title, l.RefLabel, l.IsAngleBracket))
                    .Where(l => l.Start >= 0 && l.Start + l.Length <= content.Length)
                    .ToList();
                if (adjustedLinks.Count > 0)
                    innerBlock = innerBlock with { Links = adjustedLinks };
            }
            AppendInlineHtml(sb, content, innerBlock, 0, options);
        }
        else
            sb.Append(HtmlEncode(content));
        sb.Append($"</h{level}>\n");
        return index + 1;
    }

    static int RenderParagraph(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        // Blank line — skip
        if (string.IsNullOrWhiteSpace(lines[start]))
            return start + 1;

        // Check if this paragraph block sequence ends with a setext underline
        int setextEnd = FindSetextEnd(blocks, lines, start);
        if (setextEnd > 0)
        {
            var underText = lines[setextEnd];
            int level = underText.TrimStart().StartsWith('=') ? 1 : 2;
            sb.Append($"<h{level}>");
            var (joinedH, _) = JoinParagraphLines(blocks, lines, start, setextEnd);
            AppendJoinedInlineHtml(sb, joinedH, null, options);
            sb.Append($"</h{level}>\n");
            return setextEnd + 1;
        }

        sb.Append("<p>");
        var (joined, _) = JoinParagraphLines(blocks, lines, start);
        AppendJoinedInlineHtml(sb, joined, null, options);
        sb.Append("</p>\n");

        // Advance past consumed lines
        int i = start;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind == BlockKind.Paragraph)
            { /* ok */ }
            else if (blocks[i].Kind is BlockKind.Heading1 or BlockKind.Heading2)
            {
                if (i + 1 >= blocks.Count || blocks[i + 1].Kind != BlockKind.SetextUnderline)
                    break;
            }
            else if (i > start && (blocks[i].Kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem)
                && !CanListInterruptParagraph(blocks[i].Kind, lines[i]))
            { /* empty/non-1 list item can't interrupt paragraph */ }
            else
                break;

            if (string.IsNullOrWhiteSpace(lines[i])) break;
            i++;
            if (i < blocks.Count && blocks[i].Kind == BlockKind.SetextUnderline) break;
        }
        return i;
    }

    static int FindSetextEnd(List<ParsedBlock> blocks, List<string> lines, int start)
    {
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.Paragraph && !string.IsNullOrWhiteSpace(lines[i]))
        {
            if (i + 1 < blocks.Count && blocks[i + 1].Kind == BlockKind.SetextUnderline)
                return i + 1;
            // Parser converts last paragraph before underline to Heading — check for that
            if (i + 1 < blocks.Count && blocks[i + 1].Kind is BlockKind.Heading1 or BlockKind.Heading2
                && i + 2 < blocks.Count && blocks[i + 2].Kind == BlockKind.SetextUnderline)
                return i + 2;
            i++;
        }
        return 0;
    }

    static (string Joined, HashSet<int>? HardBreaks) JoinParagraphLines(List<ParsedBlock> blocks, List<string> lines, int start, int endBefore = -1)
    {
        var joined = new StringBuilder();

        int i = start;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind == BlockKind.Paragraph)
            { /* ok */ }
            else if (blocks[i].Kind is BlockKind.Heading1 or BlockKind.Heading2)
            {
                // Only include if followed by SetextUnderline (parser converted last paragraph line)
                if (i + 1 >= blocks.Count || blocks[i + 1].Kind != BlockKind.SetextUnderline)
                    break;
            }
            else if (i > start && (blocks[i].Kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem)
                && !CanListInterruptParagraph(blocks[i].Kind, lines[i]))
            { /* empty/non-1 list item can't interrupt paragraph — treat as continuation */ }
            else
                break;

            if (string.IsNullOrWhiteSpace(lines[i])) break;
            if (endBefore > 0 && i >= endBefore) break;
            if (endBefore < 0 && i + 1 < blocks.Count && blocks[i + 1].Kind == BlockKind.SetextUnderline)
                break;

            var text = lines[i];
            bool hasNextLine;
            if (endBefore > 0)
                hasNextLine = i + 1 < endBefore;
            else
            {
                bool nextIsContinuation = (i + 1) < blocks.Count
                    && !string.IsNullOrWhiteSpace(lines[i + 1])
                    && (i + 1 >= blocks.Count - 1 || blocks[i + 2].Kind != BlockKind.SetextUnderline);
                if (nextIsContinuation)
                {
                    var nextKind = blocks[i + 1].Kind;
                    nextIsContinuation = nextKind == BlockKind.Paragraph
                        || (nextKind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                            && !CanListInterruptParagraph(nextKind, lines[i + 1]));
                }
                hasNextLine = nextIsContinuation;
            }

            string content = text.TrimStart();
            if (!hasNextLine)
                content = content.TrimEnd();

            joined.Append(content);

            if (hasNextLine)
                joined.Append('\n');

            i++;
        }

        return (joined.ToString(), null);
    }

    static void AppendJoinedInlineHtml(StringBuilder sb, string text, HashSet<int>? hardBreaks, HtmlEmitterOptions options)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var block = MarkdownParser.ParseInlineContent(text, options.LinkDefinitions);
        AppendInlineHtml(sb, text, block, 0, options, hardBreaks);
    }

    static int RenderHtmlBlock(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.HtmlBlock)
        {
            sb.Append(FilterDisallowedHtmlTags(lines[i]));
            sb.Append('\n');
            i++;
        }
        return i;
    }

    static int RenderFencedCode(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        // First block should be the fence delimiter
        var firstBlock = blocks[start];
        string? language = firstBlock.CodeLanguage != null ? ResolveEntities(ProcessBackslashEscapes(firstBlock.CodeLanguage)) : null;

        // Determine opening fence indentation (strip up to this many spaces from content)
        int fenceIndent = 0;
        string fenceLine = lines[start];
        while (fenceIndent < fenceLine.Length && fenceLine[fenceIndent] == ' ' && fenceIndent < 3)
            fenceIndent++;

        if (language != null)
            sb.Append($"<pre><code class=\"language-{HtmlEncodeAttribute(language)}\">");
        else
            sb.Append("<pre><code>");

        int i = start;
        // Skip the opening fence delimiter
        if (firstBlock.IsFenceDelimiter)
            i++;

        while (i < blocks.Count)
        {
            if (blocks[i].IsFenceDelimiter)
            {
                i++;
                break;
            }
            if (blocks[i].Kind != BlockKind.FencedCodeLine)
                break;

            string contentLine = fenceIndent > 0 ? RemoveIndent(lines[i], fenceIndent) : lines[i];
            sb.Append(HtmlEncode(contentLine));
            sb.Append('\n');
            i++;
        }

        sb.Append("</code></pre>\n");
        return i;
    }

    static int RenderIndentedCode(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        sb.Append("<pre><code>");
        var contentLines = new List<string>();
        int i = start;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind == BlockKind.IndentedCodeLine)
            {
                string content = RemoveIndent(lines[i], 4);
                contentLines.Add(content);
                i++;
            }
            else if (blocks[i].Kind == BlockKind.Paragraph && string.IsNullOrWhiteSpace(lines[i]))
            {
                int lookahead = i + 1;
                while (lookahead < blocks.Count && blocks[lookahead].Kind == BlockKind.Paragraph
                       && string.IsNullOrWhiteSpace(lines[lookahead]))
                    lookahead++;
                if (lookahead < blocks.Count && blocks[lookahead].Kind == BlockKind.IndentedCodeLine)
                {
                    for (int k = i; k < lookahead; k++)
                        contentLines.Add("");
                    i = lookahead;
                }
                else
                    break;
            }
            else
                break;
        }
        while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[^1]))
            contentLines.RemoveAt(contentLines.Count - 1);
        while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[0]))
            contentLines.RemoveAt(0);
        foreach (var line in contentLines)
        {
            sb.Append(HtmlEncode(line));
            sb.Append('\n');
        }
        sb.Append("</code></pre>\n");
        return i;
    }

    static int RenderBlockquote(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options, int depth)
    {
        if (depth > 10)
        {
            int i2 = start;
            while (i2 < blocks.Count && blocks[i2].Kind == BlockKind.Blockquote) i2++;
            return i2;
        }

        sb.Append("<blockquote>\n");

        var innerLines = new List<string>();
        var lazyIndices = new HashSet<int>();
        int i = start;
        bool canLazyContinue = false;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind == BlockKind.Blockquote)
            {
                string stripped = StripBlockquotePrefix(lines[i]);
                innerLines.Add(stripped);
                canLazyContinue = !string.IsNullOrWhiteSpace(stripped) && !IsBlockStructureStart(stripped);
                i++;
            }
            else if (blocks[i].Kind is BlockKind.Paragraph or BlockKind.IndentedCodeLine
                && !string.IsNullOrWhiteSpace(lines[i])
                && innerLines.Count > 0 && canLazyContinue)
            {
                lazyIndices.Add(innerLines.Count);
                innerLines.Add(lines[i]);
                i++;
            }
            else
                break;
        }

        var innerBlocks = MarkdownParser.Parse(idx => innerLines[idx], innerLines.Count);

        // Setext heading underlines cannot be lazy continuation lines (CommonMark §4.3)
        for (int b = 0; b < innerBlocks.Count; b++)
        {
            if (innerBlocks[b].Kind == BlockKind.SetextUnderline && lazyIndices.Contains(b))
            {
                innerBlocks[b] = innerBlocks[b] with { Kind = BlockKind.Paragraph };
                if (b > 0 && innerBlocks[b - 1].Kind is BlockKind.Heading1 or BlockKind.Heading2)
                    innerBlocks[b - 1] = innerBlocks[b - 1] with { Kind = BlockKind.Paragraph };
            }
        }
        sb.Append(RenderBlocks(innerBlocks, innerLines, options, depth + 1));

        sb.Append("</blockquote>\n");
        return i;
    }

    static int RenderUnorderedList(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        char marker = lines[start].TrimStart()[0];
        int markerIndent = GetListMarkerIndent(lines[start]);
        int contentIndent = GetListContentIndent(lines[start]);

        var items = CollectListItems(blocks, lines, start, BlockKind.UnorderedListItem, contentIndent, out int end, out bool isLoose, marker);

        sb.Append("<ul>\n");
        foreach (var item in items)
            RenderListItem(sb, item, isLoose, options);
        sb.Append("</ul>\n");
        return end;
    }

    static int RenderOrderedList(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        int startNum = GetOrderedListStart(lines[start]);
        if (startNum != 1)
            sb.Append($"<ol start=\"{startNum}\">\n");
        else
            sb.Append("<ol>\n");

        char delimiter = GetOrderedListDelimiter(lines[start]);
        int contentIndent = GetListContentIndent(lines[start]);
        var items = CollectListItems(blocks, lines, start, BlockKind.OrderedListItem, contentIndent, out int end, out bool isLoose, delimiter: delimiter);

        foreach (var item in items)
            RenderListItem(sb, item, isLoose, options);
        sb.Append("</ol>\n");
        return end;
    }

    static int RenderTaskList(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        sb.Append("<ul>\n");
        int i = start;
        while (i < blocks.Count && (blocks[i].Kind == BlockKind.TaskListItemChecked || blocks[i].Kind == BlockKind.TaskListItemUnchecked))
        {
            bool isChecked = blocks[i].Kind == BlockKind.TaskListItemChecked;
            sb.Append("<li>");
            sb.Append(isChecked
                ? "<input checked=\"\" disabled=\"\" type=\"checkbox\"> "
                : "<input disabled=\"\" type=\"checkbox\"> ");
            string content = StripTaskListPrefix(lines[i]);
            var innerBlocks = MarkdownParser.Parse(_ => content, 1);
            if (innerBlocks.Count > 0)
                AppendInlineHtml(sb, content, innerBlocks[0], 0, options);
            else
                sb.Append(HtmlEncode(content));
            sb.Append("</li>\n");
            i++;
        }
        sb.Append("</ul>\n");
        return i;
    }

    static int RenderTable(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        var tableBlock = blocks[start];
        var tableInfo = tableBlock.Table;
        int columnCount = tableInfo?.ColumnCount ?? 0;

        sb.Append("<table>\n<thead>\n<tr>\n");

        // Header row
        if (tableBlock.TableRow != null)
        {
            var cells = tableBlock.TableRow.Cells;
            for (int c = 0; c < cells.Count; c++)
            {
                string alignAttr = GetAlignAttr(tableInfo, c);
                string cellText = GetCellText(cells[c], lines[start]);
                sb.Append($"<th{alignAttr}>");
                AppendTableCellContent(sb, cellText, options);
                sb.Append("</th>\n");
            }
        }
        sb.Append("</tr>\n</thead>\n");

        int i = start + 1;
        // Skip separator row
        if (i < blocks.Count && blocks[i].Kind == BlockKind.TableSeparatorRow)
            i++;

        // Data rows
        if (i < blocks.Count && blocks[i].Kind == BlockKind.TableDataRow)
        {
            sb.Append("<tbody>\n");
            while (i < blocks.Count && blocks[i].Kind == BlockKind.TableDataRow)
            {
                sb.Append("<tr>\n");
                var row = blocks[i].TableRow;
                int cellCount = row?.Cells.Count ?? 0;
                int cols = Math.Min(cellCount, columnCount);
                for (int c = 0; c < cols; c++)
                {
                    string alignAttr = GetAlignAttr(tableInfo, c);
                    string cellText = GetCellText(row!.Cells[c], lines[i]);
                    sb.Append($"<td{alignAttr}>");
                    AppendTableCellContent(sb, cellText, options);
                    sb.Append("</td>\n");
                }
                for (int c = cols; c < columnCount; c++)
                {
                    string alignAttr = GetAlignAttr(tableInfo, c);
                    sb.Append($"<td{alignAttr}></td>\n");
                }
                sb.Append("</tr>\n");
                i++;
            }
            sb.Append("</tbody>\n");
        }

        sb.Append("</table>\n");
        return i;
    }

    static string GetAlignAttr(TableInfo? tableInfo, int col)
    {
        var alignment = tableInfo != null && col < tableInfo.Alignments.Count
            ? tableInfo.Alignments[col]
            : ColumnAlignment.Left;
        return alignment switch
        {
            ColumnAlignment.Center => " align=\"center\"",
            ColumnAlignment.Right => " align=\"right\"",
            _ => ""
        };
    }

    static string GetCellText(TableCellInfo cell, string line)
    {
        var (cs, ce) = cell.TrimContent(line);
        var text = line[cs..ce];
        return text.Replace("\\|", "|");
    }

    static void AppendTableCellContent(StringBuilder sb, string cellText, HtmlEmitterOptions options)
    {
        if (string.IsNullOrEmpty(cellText)) return;
        var cellBlocks = MarkdownParser.Parse(_ => cellText, 1);
        if (cellBlocks.Count > 0)
            AppendInlineContent(sb, cellBlocks[0], cellText, options);
        else
            sb.Append(HtmlEncode(cellText));
    }

    static void AppendInlineContent(StringBuilder sb, ParsedBlock block, string text, HtmlEmitterOptions options)
    {
        AppendInlineHtml(sb, text, block, 0, options);
    }

    static void AppendInlineHtml(StringBuilder sb, string text, ParsedBlock block, int offset, HtmlEmitterOptions options, HashSet<int>? hardBreaks = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var runs = block.Runs;
        var links = block.Links;
        var images = block.Images;
        var emphMarkers = block.EmphasisMarkers;

        // Build a set of hidden ranges (delimiter characters to skip)
        var hidden = new HashSet<int>();

        // Emphasis markers (*, **, ***, ~~, _)
        if (emphMarkers != null)
        {
            foreach (var m in emphMarkers)
                for (int j = m.Start; j < m.Start + m.Length; j++)
                    hidden.Add(j);
        }

        // Backslash escapes: the escaped char has InlineStyle.Image sentinel
        for (int bi = 0; bi < text.Length - 1; bi++)
        {
            if (text[bi] == '\\' && IsAsciiPunctuation(text[bi + 1]))
            {
                var nextStyle = GetStyleAt(runs, bi + 1 + offset);
                if (nextStyle == InlineStyle.Image)
                    hidden.Add(bi + offset);
            }
        }

        // Code span delimiters: find backtick boundaries within Code runs
        foreach (var run in runs)
        {
            if (run.Style == InlineStyle.Code)
            {
                int rs = run.Start, re = run.Start + run.Length;
                // Leading backticks
                int i = rs;
                while (i < re && (i - offset) >= 0 && (i - offset) < text.Length && text[i - offset] == '`')
                { hidden.Add(i); i++; }
                // Trailing backticks
                int j = re - 1;
                while (j >= rs && (j - offset) >= 0 && (j - offset) < text.Length && text[j - offset] == '`')
                { hidden.Add(j); j--; }
                // CommonMark: strip one leading+trailing space if both present and content not all spaces
                int contentStart = i, contentEnd = j + 1;
                int contentLen = contentEnd - contentStart;
                if (contentLen >= 2)
                {
                    bool startsWithSpace = (contentStart - offset) >= 0 && (contentStart - offset) < text.Length && text[contentStart - offset] is ' ' or '\n';
                    bool endsWithSpace = (contentEnd - 1 - offset) >= 0 && (contentEnd - 1 - offset) < text.Length && text[contentEnd - 1 - offset] is ' ' or '\n';
                    if (startsWithSpace && endsWithSpace)
                    {
                        bool allSpaces = true;
                        for (int k = contentStart; k < contentEnd; k++)
                        {
                            int ti = k - offset;
                            if (ti >= 0 && ti < text.Length && text[ti] is not ' ' and not '\n')
                            { allSpaces = false; break; }
                        }
                        if (!allSpaces)
                        {
                            hidden.Add(contentStart);
                            hidden.Add(contentEnd - 1);
                        }
                    }
                }
            }
        }

        // Strikethrough delimiters: hide leading/trailing ~~ within Strikethrough runs
        foreach (var run in runs)
        {
            if (run.Style == InlineStyle.Strikethrough)
            {
                int rs = run.Start, re = run.Start + run.Length;
                int i = rs;
                while (i < re && (i - offset) >= 0 && (i - offset) < text.Length && text[i - offset] == '~')
                { hidden.Add(i); i++; }
                int j = re - 1;
                while (j >= rs && (j - offset) >= 0 && (j - offset) < text.Length && text[j - offset] == '~')
                { hidden.Add(j); j--; }
            }
        }

        // Build emphasis open/close events from markers
        // Markers are in pairs: [0]=opener, [1]=closer, [2]=opener, [3]=closer, ...
        var emphOpen = new Dictionary<int, int>(); // position → consume (1=em, 2=strong)
        var emphClose = new Dictionary<int, int>(); // position → consume
        if (emphMarkers != null)
        {
            for (int mi = 0; mi + 1 < emphMarkers.Count; mi += 2)
            {
                var opener = emphMarkers[mi];
                var closer = emphMarkers[mi + 1];
                int openPos = opener.Start + opener.Length;
                int closePos = closer.Start;
                emphOpen.TryGetValue(openPos, out int existingOpen);
                emphOpen[openPos] = existingOpen + opener.Length;
                emphClose.TryGetValue(closePos, out int existingClose);
                emphClose[closePos] = existingClose + closer.Length;
            }
        }

        // Collect link/image replacements sorted by position
        var replacements = new List<(int Start, int End, string Html)>();

        if (images != null)
        {
            foreach (var img in images)
            {
                string alt = HtmlEncode(StripInlineMarkdown(img.AltText));
                string url = HtmlEncode(PercentEncodeUrl(ResolveEntities(ProcessBackslashEscapes(img.Url))));
                string titleAttr = img.Title != null ? $" title=\"{HtmlEncodeAttribute(ResolveEntities(ProcessBackslashEscapes(img.Title)))}\"" : "";
                replacements.Add((img.Start, img.Start + img.Length, $"<img src=\"{url}\" alt=\"{alt}\"{titleAttr} />"));
            }
        }

        if (links != null)
        {
            foreach (var link in links)
            {
                // Skip bare-URL autolinks (GFM extension, not in CommonMark base spec)
                if (!link.IsAngleBracket && link.Url.Length > 0 && link.Text == link.Url && link.Title == null && link.RefLabel == null)
                    continue;

                string url;
                if (link.IsAngleBracket)
                    url = HtmlEncode(PercentEncodeUrl(link.Url));
                else
                    url = HtmlEncode(PercentEncodeUrl(ResolveEntities(ProcessBackslashEscapes(link.Url))));
                string titleAttr = link.Title != null ? $" title=\"{HtmlEncodeAttribute(ResolveEntities(ProcessBackslashEscapes(link.Title)))}\"" : "";
                var linkTextSb = new StringBuilder();
                if (link.IsAngleBracket)
                {
                    linkTextSb.Append(HtmlEncode(link.Text));
                }
                else
                {
                    var linkTextBlocks = MarkdownParser.Parse(_ => link.Text, 1);
                    if (linkTextBlocks.Count > 0)
                    {
                        var innerBlock = linkTextBlocks[0] with { Links = null };
                        AppendInlineHtml(linkTextSb, link.Text, innerBlock, 0, options);
                    }
                    else
                        linkTextSb.Append(HtmlEncode(link.Text));
                }
                replacements.Add((link.Start, link.Start + link.Length, $"<a href=\"{url}\"{titleAttr}>{linkTextSb}</a>"));
            }
        }

        replacements.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Walk through text positions, emit content with styling
        int pos = offset;
        int end = offset + text.Length;
        int replIdx = 0;
        InlineStyle? currentStyle = null;

        while (pos < end)
        {
            // Check for replacement (link/image)
            if (replIdx < replacements.Count && pos == replacements[replIdx].Start)
            {
                sb.Append(replacements[replIdx].Html);
                pos = replacements[replIdx].End;
                replIdx++;
                continue;
            }

            // Skip if inside a replacement range
            if (replIdx < replacements.Count && pos > replacements[replIdx].Start && pos < replacements[replIdx].End)
            {
                pos = replacements[replIdx].End;
                replIdx++;
                continue;
            }

            // Emit emphasis close tags at this position
            if (emphClose.TryGetValue(pos, out int closeLen))
            {
                if (currentStyle != null) { CloseTag(sb, currentStyle.Value); currentStyle = null; }
                while (closeLen >= 2) { sb.Append("</strong>"); closeLen -= 2; }
                if (closeLen == 1) sb.Append("</em>");
            }

            // Emit emphasis open tags at this position (before hidden skip,
            // since nested openers may overlap with delimiter chars)
            if (emphOpen.TryGetValue(pos, out int openLen))
            {
                while (openLen >= 2) { sb.Append("<strong>"); openLen -= 2; }
                if (openLen == 1) sb.Append("<em>");
            }

            // Skip hidden (delimiter) characters
            if (hidden.Contains(pos))
            {
                pos++;
                continue;
            }

            // Check for raw inline HTML (pass through verbatim)
            int ti = pos - offset;
            if (ti < text.Length && text[ti] == '<')
            {
                var htmlStyle = GetStyleAt(runs, pos);
                if (htmlStyle == InlineStyle.Normal || htmlStyle == InlineStyle.Italic
                    || htmlStyle == InlineStyle.Bold || htmlStyle == InlineStyle.BoldItalic
                    || htmlStyle == InlineStyle.Link)
                {
                    int htmlEnd = TryMatchInlineHtml(text, ti);
                    if (htmlEnd > ti)
                    {
                        if (IsDisallowedHtmlTag(text, ti))
                            sb.Append("&lt;").Append(text[(ti + 1)..htmlEnd]);
                        else
                            sb.Append(text[ti..htmlEnd]);
                        pos = offset + htmlEnd;
                        continue;
                    }
                }
            }

            // Determine style at this position (code/strikethrough only — emphasis handled via markers)
            var rawStyle = GetStyleAt(runs, pos);
            var style = rawStyle;
            if (style is InlineStyle.Italic or InlineStyle.Bold or InlineStyle.BoldItalic
                or InlineStyle.Image or InlineStyle.Link or InlineStyle.Normal)
                style = InlineStyle.Normal;

            // Handle code/strikethrough style transitions
            if (style != currentStyle)
            {
                if (currentStyle != null) CloseTag(sb, currentStyle.Value);
                currentStyle = style != InlineStyle.Normal ? style : null;
                if (currentStyle != null) OpenTag(sb, currentStyle.Value);
            }

            // Entity resolution (not inside code spans or escaped characters)
            int ci = pos - offset;
            if (style != InlineStyle.Code && rawStyle != InlineStyle.Image && ci < text.Length && text[ci] == '&')
            {
                int entityEnd = TryResolveEntity(text, ci, out string? decoded);
                if (entityEnd > ci && decoded != null)
                {
                    sb.Append(HtmlEncode(decoded));
                    pos = offset + entityEnd;
                    continue;
                }
            }

            // Handle newline characters (from joined paragraph lines)
            if (text[ci] == '\n')
            {
                if (currentStyle == InlineStyle.Code)
                {
                    sb.Append(' ');
                }
                else
                {
                    // Detect hard break: backslash or 2+ spaces before \n
                    bool isHardBreak = false;
                    if (ci > 0 && text[ci - 1] == '\\')
                    {
                        // Backslash hard break — remove the trailing backslash from output
                        if (sb.Length > 0 && sb[^1] == '\\') sb.Length--;
                        isHardBreak = true;
                    }
                    else if (ci >= 2 && text[ci - 1] == ' ' && text[ci - 2] == ' ')
                    {
                        // Trailing spaces hard break — remove trailing spaces from output
                        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                        isHardBreak = true;
                    }

                    if (isHardBreak)
                    {
                        sb.Append("<br />\n");
                    }
                    else
                    {
                        // Soft break: strip trailing spaces from preceding output
                        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                        sb.Append('\n');
                    }
                }
                pos++;
                continue;
            }

            sb.Append(HtmlEncode(text[ci..(ci + 1)]));
            pos++;
        }

        // Close any remaining code/strikethrough
        if (currentStyle != null) CloseTag(sb, currentStyle.Value);
    }

    static void OpenTag(StringBuilder sb, InlineStyle style)
    {
        switch (style)
        {
            case InlineStyle.Bold: sb.Append("<strong>"); break;
            case InlineStyle.Italic: sb.Append("<em>"); break;
            case InlineStyle.BoldItalic: sb.Append("<em><strong>"); break;
            case InlineStyle.Code: sb.Append("<code>"); break;
            case InlineStyle.Strikethrough: sb.Append("<del>"); break;
        }
    }

    static void CloseTag(StringBuilder sb, InlineStyle style)
    {
        switch (style)
        {
            case InlineStyle.Bold: sb.Append("</strong>"); break;
            case InlineStyle.Italic: sb.Append("</em>"); break;
            case InlineStyle.BoldItalic: sb.Append("</strong></em>"); break;
            case InlineStyle.Code: sb.Append("</code>"); break;
            case InlineStyle.Strikethrough: sb.Append("</del>"); break;
        }
    }

    static InlineStyle GetStyleAt(IReadOnlyList<StyledRun> runs, int offset)
    {
        foreach (var run in runs)
        {
            if (offset >= run.Start && offset < run.Start + run.Length)
                return run.Style;
        }
        return InlineStyle.Normal;
    }

    static int GetHeadingPrefixLength(string text)
    {
        int i = 0;
        // Skip leading spaces (0-3)
        while (i < text.Length && text[i] == ' ' && i < 3) i++;
        while (i < text.Length && text[i] == '#') i++;
        if (i < text.Length && text[i] == ' ') i++;
        return i;
    }

    static string GetHeadingContent(string text)
    {
        int prefixLen = GetHeadingPrefixLength(text);
        string content = text[prefixLen..];

        // Strip trailing closing # sequence (CommonMark 4.2)
        content = content.TrimEnd();
        if (content.EndsWith('#'))
        {
            int end = content.Length - 1;
            while (end >= 0 && content[end] == '#') end--;
            if (end < 0 || content[end] == ' ')
            {
                content = end < 0 ? "" : content[..end].TrimEnd();
            }
        }

        return content.Trim();
    }

    static bool IsBlockStructureStart(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return false;
        // Fenced code
        if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) return true;
        // ATX heading
        if (trimmed.StartsWith('#') && (trimmed.Length == 1 || trimmed[1] == ' ' || trimmed[1] == '#')) return true;
        // Indented code (4+ spaces)
        if (text.Length >= 4 && text[..4] == "    ") return true;
        return false;
    }

    static string StripBlockquotePrefix(string text)
    {
        int indent = 0;
        while (indent < text.Length && indent < 3 && text[indent] == ' ') indent++;
        if (indent < text.Length && text[indent] == '>')
        {
            int after = indent + 1;
            if (after < text.Length && text[after] == ' ') after++;
            return text[after..];
        }
        return text;
    }

    static string StripListPrefix(string text)
    {
        int indent = GetListContentIndent(text);
        if (indent >= text.Length) return "";
        var content = text[indent..];
        return string.IsNullOrWhiteSpace(content) ? "" : content;
    }

    static string StripOrderedListPrefix(string text)
    {
        int indent = GetListContentIndent(text);
        if (indent >= text.Length) return "";
        var content = text[indent..];
        return string.IsNullOrWhiteSpace(content) ? "" : content;
    }

    static string StripTaskListPrefix(string text)
    {
        // `- [ ] ` or `- [x] ` with optional leading spaces
        var trimmed = text.AsSpan();
        int i = 0;
        while (i < trimmed.Length && trimmed[i] == ' ') i++;
        if (i < trimmed.Length && (trimmed[i] == '-' || trimmed[i] == '*')) i++;
        if (i < trimmed.Length && trimmed[i] == ' ') i++;
        if (i < trimmed.Length && trimmed[i] == '[') i++;
        if (i < trimmed.Length) i++; // x or space
        if (i < trimmed.Length && trimmed[i] == ']') i++;
        if (i < trimmed.Length && trimmed[i] == ' ') i++;
        return text[i..];
    }

    static char GetOrderedListDelimiter(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        return i < text.Length ? text[i] : '.';
    }

    static int GetOrderedListStart(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        int numStart = i;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        if (i > numStart && int.TryParse(text[numStart..i], out int num))
            return num;
        return 1;
    }

    static bool CanListInterruptParagraph(BlockKind kind, string line)
    {
        if (kind == BlockKind.UnorderedListItem)
        {
            var content = StripListPrefix(line);
            return content.Length > 0;
        }
        if (kind == BlockKind.OrderedListItem)
        {
            if (GetOrderedListStart(line) != 1) return false;
            var content = StripOrderedListPrefix(line);
            return content.Length > 0;
        }
        return true;
    }

    static int GetListMarkerIndent(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        return i;
    }

    static (int Count, string? Language, char Char) GetLocalFenceInfo(string text)
    {
        int s = 0;
        while (s < text.Length && s < 3 && text[s] == ' ') s++;
        if (s >= text.Length) return (0, null, '\0');
        char fc = text[s];
        if (fc != '`' && fc != '~') return (0, null, '\0');
        int count = 0;
        int p = s;
        while (p < text.Length && text[p] == fc) { count++; p++; }
        if (count < 3) return (0, null, '\0');
        var info = text[p..].Trim();
        if (fc == '`' && info.Contains('`')) return (0, null, '\0');
        return (count, info.Length > 0 ? info : null, fc);
    }

    static bool IsListMarkerStart(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return false;
        if (trimmed[0] is '-' or '*' or '+' && (trimmed.Length == 1 || trimmed[1] is ' ' or '\t'))
            return true;
        int j = 0;
        while (j < trimmed.Length && char.IsDigit(trimmed[j])) j++;
        return j > 0 && j < trimmed.Length && trimmed[j] is '.' or ')'
            && (j + 1 >= trimmed.Length || trimmed[j + 1] is ' ' or '\t');
    }

    static int GetListContentIndent(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        // Skip marker
        if (i < text.Length && (text[i] == '-' || text[i] == '*' || text[i] == '+'))
        {
            i++;
        }
        else
        {
            // Ordered: skip digits and delimiter
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i < text.Length && (text[i] == '.' || text[i] == ')')) i++;
        }
        int markerEnd = i;
        // Skip spaces after marker to find actual content start
        while (i < text.Length && text[i] == ' ') i++;
        // If no content on this line (blank after marker), use marker_end + 1
        if (i >= text.Length)
            return markerEnd + 1;
        int spacesAfterMarker = i - markerEnd;
        // Rule #3: if 5+ spaces after marker (4+ from W+1 position), content is indented code
        // and content column collapses to marker_end + 1
        if (spacesAfterMarker >= 5)
            return markerEnd + 1;
        return i;
    }

    static List<List<string>> CollectListItems(List<ParsedBlock> blocks, List<string> lines, int start,
        BlockKind itemKind, int contentIndent, out int end, out bool isLoose, char marker = '\0', char delimiter = '\0')
    {
        var items = new List<List<string>>();
        var currentItem = new List<string>();
        isLoose = false;
        int i = start;
        bool seenBlank = false;
        bool consumedAfterBlank = false;
        int firstMarkerIndent = GetListMarkerIndent(lines[start]);

        while (i < blocks.Count)
        {
            if (blocks[i].Kind == itemKind)
            {
                int thisMarkerIndent = GetListMarkerIndent(lines[i]);

                // Check if same marker type for unordered lists
                if (itemKind == BlockKind.UnorderedListItem && marker != '\0')
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.Length > 0 && trimmed[0] != marker)
                    {
                        // Different marker — if at same indent level, break list
                        if (thisMarkerIndent <= firstMarkerIndent + 1)
                            break;
                        // Otherwise it's a nested sublist — add as continuation
                        if (currentItem.Count > 0 && thisMarkerIndent >= contentIndent)
                        {
                            currentItem.Add(lines[i][contentIndent..]);
                            if (seenBlank) consumedAfterBlank = true;
                            i++;
                            continue;
                        }
                        break;
                    }
                }

                // Check if same delimiter for ordered lists
                if (itemKind == BlockKind.OrderedListItem && delimiter != '\0')
                {
                    char thisDelim = GetOrderedListDelimiter(lines[i]);
                    if (thisDelim != delimiter)
                    {
                        if (thisMarkerIndent <= firstMarkerIndent + 1)
                            break;
                        if (currentItem.Count > 0 && thisMarkerIndent >= contentIndent)
                        {
                            currentItem.Add(lines[i][contentIndent..]);
                            if (seenBlank) consumedAfterBlank = true;
                            i++;
                            continue;
                        }
                        break;
                    }
                }

                // If indented beyond current content column, this is a nested sublist
                if (currentItem.Count > 0 && thisMarkerIndent >= contentIndent)
                {
                    currentItem.Add(lines[i][contentIndent..]);
                    if (seenBlank) consumedAfterBlank = true;
                    i++;
                    continue;
                }

                if (seenBlank && !consumedAfterBlank) isLoose = true;
                if (currentItem.Count > 0)
                {
                    items.Add(currentItem);
                    currentItem = new List<string>();
                }
                string content = itemKind == BlockKind.OrderedListItem
                    ? StripOrderedListPrefix(lines[i])
                    : StripListPrefix(lines[i]);
                currentItem.Add(content);
                contentIndent = GetListContentIndent(lines[i]);
                seenBlank = false;
                consumedAfterBlank = false;
                i++;
            }
            else if (string.IsNullOrWhiteSpace(lines[i]))
            {
                if (currentItem.Count > 0)
                {
                    if (currentItem.Count == 1 && currentItem[0] == "")
                    {
                        items.Add(currentItem);
                        currentItem = new List<string>();
                        seenBlank = true;
                        consumedAfterBlank = false;
                        i++;
                        continue;
                    }
                    currentItem.Add("");
                    seenBlank = true;
                    consumedAfterBlank = false;
                }
                i++;
            }
            else if (currentItem.Count > 0)
            {
                // Check if indented enough to be continuation
                int lineIndent = 0;
                while (lineIndent < lines[i].Length && lines[i][lineIndent] == ' ') lineIndent++;
                if (lineIndent >= contentIndent)
                {
                    currentItem.Add(lines[i][contentIndent..]);
                    if (seenBlank) consumedAfterBlank = true;
                    i++;
                }
                else if (seenBlank)
                    break;
                else if (blocks[i].Kind == BlockKind.Paragraph)
                {
                    // Lazy continuation (only for paragraphs)
                    currentItem.Add(lines[i]);
                    i++;
                }
                else
                    break;
            }
            else
                break;
        }

        if (currentItem.Count > 0)
            items.Add(currentItem);

        // Trim leading and trailing blank lines from all items
        foreach (var item in items)
        {
            while (item.Count > 0 && string.IsNullOrWhiteSpace(item[^1]))
                item.RemoveAt(item.Count - 1);
            while (item.Count > 0 && string.IsNullOrWhiteSpace(item[0]))
                item.RemoveAt(0);
        }
        if (!isLoose)
        {
            foreach (var item in items)
            {
                bool seenContent = false;
                bool pendingBlank = false;
                bool prevWasNested = false;
                int itemFenceLen = 0;
                char itemFenceChar = '\0';
                foreach (var line in item)
                {
                    if (itemFenceLen > 0)
                    {
                        var (cfc, _, cfch) = GetLocalFenceInfo(line);
                        if (cfc >= itemFenceLen && cfch == itemFenceChar)
                            itemFenceLen = 0;
                        continue;
                    }
                    var (ofc, _, ofch) = GetLocalFenceInfo(line);
                    if (ofc >= 3)
                    {
                        itemFenceLen = ofc;
                        itemFenceChar = ofch;
                        seenContent = true;
                        pendingBlank = false;
                        prevWasNested = false;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (seenContent) pendingBlank = true;
                    }
                    else
                    {
                        bool isIndented = line.Length > 0 && line[0] == ' ';
                        if (pendingBlank && (!prevWasNested || !isIndented))
                        { isLoose = true; break; }

                        seenContent = true;
                        pendingBlank = false;
                        prevWasNested = isIndented || IsListMarkerStart(line);
                    }
                }
                if (isLoose) break;
            }
        }

        end = i;
        return items;
    }

    static void RenderListItem(StringBuilder sb, List<string> itemLines, bool isLoose, HtmlEmitterOptions options)
    {
        sb.Append("<li>");

        if (itemLines.Count == 0 || itemLines.All(string.IsNullOrWhiteSpace))
        {
            sb.Append("</li>\n");
            return;
        }

        // Check if the item has only paragraph content (no sublists, code blocks, etc.)
        var innerBlocks = MarkdownParser.Parse(idx => itemLines[idx], itemLines.Count, out var innerDefs);
        var innerOptions = new HtmlEmitterOptions { LinkDefinitions = options.LinkDefinitions };
        if (innerDefs != null)
        {
            innerOptions.LinkDefinitions ??= new Dictionary<string, (string Url, string? Title)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in innerDefs)
                innerOptions.LinkDefinitions.TryAdd(k, v);
        }

        bool hasNonParagraph = false;
        for (int b = 0; b < innerBlocks.Count; b++)
        {
            var kind = innerBlocks[b].Kind;
            if (kind != BlockKind.Paragraph && kind != BlockKind.SetextUnderline
                && kind != BlockKind.LinkDefinition)
            {
                hasNonParagraph = true;
                break;
            }
        }

        if (!isLoose && !hasNonParagraph)
        {
            // Tight, paragraph-only: inline content, no <p> wrapping
            var (joined, _) = JoinParagraphLines(innerBlocks, itemLines, 0);
            AppendJoinedInlineHtml(sb, joined, null, innerOptions);
        }
        else if (!isLoose && hasNonParagraph)
        {
            // Tight with block content: paragraphs inline, other blocks rendered normally
            bool hasEmittedInline = false;
            int b = 0;
            while (b < innerBlocks.Count)
            {
                var kind = innerBlocks[b].Kind;
                if (kind == BlockKind.Paragraph && !string.IsNullOrWhiteSpace(itemLines[b]))
                {
                    var (joined, _) = JoinParagraphLines(innerBlocks, itemLines, b);
                    AppendJoinedInlineHtml(sb, joined, null, innerOptions);
                    hasEmittedInline = true;
                    while (b < innerBlocks.Count && innerBlocks[b].Kind == BlockKind.Paragraph
                        && !string.IsNullOrWhiteSpace(itemLines[b]))
                        b++;
                    // Only add newline if there's more content after
                    bool hasMoreContent = false;
                    for (int nb = b; nb < innerBlocks.Count; nb++)
                    {
                        if (innerBlocks[nb].Kind != BlockKind.Paragraph || !string.IsNullOrWhiteSpace(itemLines[nb]))
                        { hasMoreContent = true; break; }
                    }
                    if (hasMoreContent) sb.Append('\n');
                }
                else if (kind == BlockKind.Paragraph || kind == BlockKind.LinkDefinition)
                {
                    b++;
                }
                else
                {
                    if (!hasEmittedInline)
                        sb.Append('\n');
                    var blockSb = new StringBuilder();
                    b = RenderBlock(blockSb, innerBlocks, itemLines, b, innerOptions, 0);
                    sb.Append(blockSb);
                    hasEmittedInline = true;
                }
            }
        }
        else
        {
            // Loose: render as blocks with <p> wrapping
            sb.Append('\n');
            sb.Append(RenderBlocks(innerBlocks, itemLines, innerOptions));
        }

        sb.Append("</li>\n");
    }

    static string RemoveIndent(string text, int count)
    {
        int removed = 0;
        int i = 0;
        while (i < text.Length && removed < count)
        {
            if (text[i] == '\t')
            {
                removed += 4 - (removed % 4);
                i++;
            }
            else if (text[i] == ' ')
            {
                removed++;
                i++;
            }
            else break;
        }
        return text[i..];
    }

    static int TryResolveEntity(string text, int start, out string? decoded)
    {
        decoded = null;
        if (start >= text.Length || text[start] != '&') return start;

        // Find semicolon within 32 chars (CommonMark limit)
        int maxEnd = Math.Min(start + 32, text.Length);
        int semi = -1;
        for (int i = start + 1; i < maxEnd; i++)
        {
            if (text[i] == ';') { semi = i; break; }
            if (text[i] == '&' || text[i] == ' ' || text[i] == '\n') break;
        }
        if (semi < 0) return start;

        string entity = text[start..(semi + 1)];

        // Numeric character reference
        if (entity.Length >= 4 && entity[1] == '#')
        {
            int codePoint = -1;
            if (entity[2] == 'x' || entity[2] == 'X')
            {
                // Hex: &#x...; or &#X...;
                var hex = entity[3..^1];
                if (hex.Length > 0 && hex.Length <= 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out codePoint))
                { }
                else return start;
            }
            else
            {
                // Decimal: &#...;
                var dec = entity[2..^1];
                if (dec.Length > 0 && dec.Length <= 7 && int.TryParse(dec, out codePoint))
                { }
                else return start;
            }

            // CommonMark: 0, surrogates, and > 0x10FFFF → U+FFFD
            if (codePoint == 0 || (codePoint >= 0xD800 && codePoint <= 0xDFFF) || codePoint > 0x10FFFF)
                decoded = "�";
            else
                decoded = char.ConvertFromUtf32(codePoint);
            return semi + 1;
        }

        // Named entity
        var name = entity[1..^1];
        if (HtmlEntities.Map.TryGetValue(name, out var resolved))
        {
            decoded = resolved;
            return semi + 1;
        }

        return start;
    }

    static int TryMatchInlineHtml(string text, int start)
    {
        if (start + 1 >= text.Length) return start;
        char next = text[start + 1];

        // Closing tag: </tagname\s*>
        if (next == '/')
        {
            if (start + 2 >= text.Length || !char.IsAsciiLetter(text[start + 2])) return start;
            int i = start + 3;
            while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '-')) i++;
            while (i < text.Length && text[i] == ' ') i++;
            if (i < text.Length && text[i] == '>') return i + 1;
            return start;
        }

        // Comment: <!-- ... -->
        if (next == '!' && start + 3 < text.Length && text[start + 2] == '-' && text[start + 3] == '-')
        {
            // Minimal comments <!--> and <!---->
            int i = start + 4;
            if (i <= text.Length && text.AsSpan(start).StartsWith("<!-->".AsSpan()))
                return start + 5 <= text.Length ? start + 5 : start;
            if (i <= text.Length && text.AsSpan(start).StartsWith("<!--->".AsSpan()))
                return start + 6 <= text.Length ? start + 6 : start;
            // Standard comment: find -->
            while (i < text.Length - 2)
            {
                if (text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '>')
                    return i + 3;
                i++;
            }
            return start;
        }

        // CDATA: <![CDATA[ ... ]]>
        if (next == '!' && start + 8 < text.Length && text.AsSpan(start, 9).SequenceEqual("<![CDATA[".AsSpan()))
        {
            int i = start + 9;
            while (i < text.Length - 2)
            {
                if (text[i] == ']' && text[i + 1] == ']' && text[i + 2] == '>')
                    return i + 3;
                i++;
            }
            return start;
        }

        // Declaration: <![A-Z] ... >
        if (next == '!' && start + 2 < text.Length && char.IsAsciiLetterUpper(text[start + 2]))
        {
            int i = start + 3;
            while (i < text.Length && text[i] != '>') i++;
            if (i < text.Length) return i + 1;
            return start;
        }

        // Processing instruction: <? ... ?>
        if (next == '?')
        {
            int i = start + 2;
            while (i < text.Length - 1)
            {
                if (text[i] == '?' && text[i + 1] == '>')
                    return i + 2;
                i++;
            }
            return start;
        }

        // Open tag: <tagname (attributes)* \s* /? >
        if (!char.IsAsciiLetter(next)) return start;
        int p = start + 2;
        while (p < text.Length && (char.IsAsciiLetterOrDigit(text[p]) || text[p] == '-')) p++;

        // Consume attributes
        while (p < text.Length)
        {
            // Skip whitespace
            int ws = p;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t' || text[p] == '\n')) p++;
            if (p >= text.Length) return start;

            if (text[p] == '>') return p + 1;
            if (text[p] == '/' && p + 1 < text.Length && text[p + 1] == '>') return p + 2;

            // Must have whitespace before attribute
            if (p == ws) return start;

            // Attribute name: [a-zA-Z_:][a-zA-Z0-9_.:-]*
            if (!char.IsAsciiLetter(text[p]) && text[p] != '_' && text[p] != ':') return start;
            p++;
            while (p < text.Length && (char.IsAsciiLetterOrDigit(text[p]) || text[p] == '_' || text[p] == '.' || text[p] == ':' || text[p] == '-'))
                p++;

            // Optional value
            int beforeEq = p;
            while (p < text.Length && text[p] == ' ') p++;
            if (p < text.Length && text[p] == '=')
            {
                p++;
                while (p < text.Length && text[p] == ' ') p++;
                if (p >= text.Length) return start;

                if (text[p] == '"')
                {
                    p++;
                    while (p < text.Length && text[p] != '"') p++;
                    if (p >= text.Length) return start;
                    p++;
                }
                else if (text[p] == '\'')
                {
                    p++;
                    while (p < text.Length && text[p] != '\'') p++;
                    if (p >= text.Length) return start;
                    p++;
                }
                else
                {
                    // Unquoted value
                    if (text[p] == ' ' || text[p] == '\n' || text[p] == '"' || text[p] == '\'' || text[p] == '=' || text[p] == '<' || text[p] == '>' || text[p] == '`')
                        return start;
                    while (p < text.Length && text[p] != ' ' && text[p] != '\n' && text[p] != '"' && text[p] != '\'' && text[p] != '=' && text[p] != '<' && text[p] != '>' && text[p] != '`')
                        p++;
                }
            }
            else
            {
                p = beforeEq; // backtrack — no value, just boolean attribute
            }
        }
        return start;
    }

    static string StripInlineMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]))
            {
                sb.Append(text[i + 1]);
                i++;
            }
            else if (text[i] == '`')
            {
                int ticks = 1;
                while (i + ticks < text.Length && text[i + ticks] == '`') ticks++;
                int closeStart = text.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                if (closeStart >= 0)
                {
                    string content = text[(i + ticks)..closeStart];
                    sb.Append(content.Trim());
                    i = closeStart + ticks - 1;
                }
                else
                {
                    sb.Append('`', ticks);
                    i += ticks - 1;
                }
            }
            else if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[')
            {
                int bracketClose = FindClosingBracket(text, i + 2);
                if (bracketClose >= 0)
                {
                    int afterBracket = bracketClose + 1;
                    if (afterBracket < text.Length && text[afterBracket] == '(')
                    {
                        int parenClose = FindClosingParen(text, afterBracket + 1);
                        if (parenClose >= 0)
                        {
                            sb.Append(StripInlineMarkdown(text[(i + 2)..bracketClose]));
                            i = parenClose;
                            continue;
                        }
                    }
                    else if (afterBracket < text.Length && text[afterBracket] == '[')
                    {
                        int refClose = text.IndexOf(']', afterBracket + 1);
                        if (refClose >= 0)
                        {
                            sb.Append(StripInlineMarkdown(text[(i + 2)..bracketClose]));
                            i = refClose;
                            continue;
                        }
                    }
                }
                sb.Append('!');
            }
            else if (text[i] == '[')
            {
                int bracketClose = FindClosingBracket(text, i + 1);
                if (bracketClose >= 0 && !ContentContainsLink(text, i + 1, bracketClose))
                {
                    int afterBracket = bracketClose + 1;
                    if (afterBracket < text.Length && text[afterBracket] == '(')
                    {
                        int parenClose = FindClosingParen(text, afterBracket + 1);
                        if (parenClose >= 0)
                        {
                            sb.Append(StripInlineMarkdown(text[(i + 1)..bracketClose]));
                            i = parenClose;
                            continue;
                        }
                    }
                    else if (afterBracket < text.Length && text[afterBracket] == '[')
                    {
                        int refClose = text.IndexOf(']', afterBracket + 1);
                        if (refClose >= 0)
                        {
                            sb.Append(StripInlineMarkdown(text[(i + 1)..bracketClose]));
                            i = refClose;
                            continue;
                        }
                    }
                }
                sb.Append('[');
            }
            else if (text[i] is '*' or '_')
            {
                continue;
            }
            else if (text[i] == '&')
            {
                int semiIdx = text.IndexOf(';', i + 1);
                if (semiIdx > i && semiIdx - i <= 32)
                {
                    string entityName = text[(i + 1)..semiIdx];
                    if (HtmlEntities.Map.TryGetValue(entityName, out var decoded))
                    {
                        sb.Append(decoded);
                        i = semiIdx;
                        continue;
                    }
                }
                sb.Append('&');
            }
            else
            {
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    static int FindClosingBracket(string text, int from)
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

    static bool ContentContainsLink(string text, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\\' && i + 1 < to) { i++; continue; }
            if (text[i] == '!' && i + 1 < to && text[i + 1] == '[')
            {
                int imgClose = FindClosingBracket(text, i + 2);
                if (imgClose >= 0 && imgClose < to)
                {
                    int after = imgClose + 1;
                    if (after < to && text[after] == '(')
                    {
                        int p = FindClosingParen(text, after + 1);
                        if (p >= 0) { i = p; continue; }
                    }
                    else if (after < to && text[after] == '[')
                    {
                        int r = text.IndexOf(']', after + 1);
                        if (r >= 0 && r < to) { i = r; continue; }
                    }
                }
                i++;
                continue;
            }
            if (text[i] == '[')
            {
                int close = FindClosingBracket(text, i + 1);
                if (close >= 0 && close < to)
                {
                    int after = close + 1;
                    if (after < to && text[after] == '(')
                    {
                        int p = FindClosingParen(text, after + 1);
                        if (p >= 0) return true;
                    }
                    if (after < to && text[after] == '[')
                    {
                        int r = text.IndexOf(']', after + 1);
                        if (r >= 0) return true;
                    }
                }
            }
        }
        return false;
    }

    static int FindClosingParen(string text, int from)
    {
        int depth = 1;
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { i++; continue; }
            if (text[i] == '(') depth++;
            else if (text[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    static string ProcessBackslashEscapes(string text)
    {
        if (!text.Contains('\\')) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]))
            {
                sb.Append(text[i + 1]);
                i++;
            }
            else
            {
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    static string ResolveEntities(string text)
    {
        if (!text.Contains('&')) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                int end = TryResolveEntity(text, i, out string? decoded);
                if (end > i && decoded != null)
                {
                    sb.Append(decoded);
                    i = end - 1;
                    continue;
                }
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    static string PercentEncodeUrl(string url)
    {
        var sb = new StringBuilder(url.Length);
        for (int i = 0; i < url.Length; i++)
        {
            char c = url[i];
            if (c > 0x7E || char.IsHighSurrogate(c))
            {
                string chunk = char.IsHighSurrogate(c) && i + 1 < url.Length && char.IsLowSurrogate(url[i + 1])
                    ? url.Substring(i++, 2)
                    : c.ToString();
                foreach (byte b in Encoding.UTF8.GetBytes(chunk))
                    sb.Append($"%{b:X2}");
            }
            else if (c == '%' && i + 2 < url.Length && IsHexDigit(url[i + 1]) && IsHexDigit(url[i + 2]))
            {
                sb.Append(c);
            }
            else if (IsUrlSafe(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append($"%{(int)c:X2}");
            }
        }
        return sb.ToString();
    }

    static bool IsUrlSafe(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
        || c == '-' || c == '_' || c == '.' || c == '~'
        || c == ':' || c == '/' || c == '?' || c == '#' || c == '@'
        || c == '!' || c == '$' || c == '&' || c == '\'' || c == '(' || c == ')'
        || c == '*' || c == '+' || c == ',' || c == ';' || c == '=';

    static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    static string FilterDisallowedHtmlTags(string line)
    {
        int idx = line.IndexOf('<');
        if (idx < 0) return line;
        var sb = new StringBuilder(line.Length);
        int pos = 0;
        while (idx >= 0 && idx < line.Length)
        {
            if (IsDisallowedHtmlTag(line, idx))
            {
                sb.Append(line, pos, idx - pos);
                sb.Append("&lt;");
                pos = idx + 1;
            }
            idx = line.IndexOf('<', idx + 1);
        }
        if (pos == 0) return line;
        sb.Append(line, pos, line.Length - pos);
        return sb.ToString();
    }

    static bool IsDisallowedHtmlTag(string text, int start)
    {
        int tagStart = start + 1;
        if (tagStart < text.Length && text[tagStart] == '/') tagStart++;
        foreach (var tag in DisallowedHtmlTags)
        {
            if (tagStart + tag.Length > text.Length) continue;
            bool match = true;
            for (int i = 0; i < tag.Length; i++)
            {
                if (char.ToLowerInvariant(text[tagStart + i]) != tag[i])
                { match = false; break; }
            }
            if (match)
            {
                int after = tagStart + tag.Length;
                if (after >= text.Length || text[after] is ' ' or '\t' or '\n' or '>' or '/')
                    return true;
            }
        }
        return false;
    }

    static bool IsAsciiPunctuation(char c) =>
        c is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '*' or '+' or ','
        or '-' or '.' or '/' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '[' or '\\'
        or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';

    static string HtmlEncode(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    static string HtmlEncodeAttribute(string text)
    {
        return HtmlEncode(text);
    }
}
