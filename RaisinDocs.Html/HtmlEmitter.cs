using System.Linq;
using System.Net;
using System.Text;
using RaisinDocs;

namespace RaisinDocs.Html;

public class HtmlEmitterOptions
{
    public bool IncludeColorExtensions { get; set; } = true;
}

public static class HtmlEmitter
{
    public static string Render(string markdown, HtmlEmitterOptions? options = null)
    {
        options ??= new HtmlEmitterOptions();
        var lines = SplitLines(markdown);
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Count);
        return RenderBlocks(blocks, lines, options, 0);
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

        // Check if this paragraph is actually a setext heading (next line is SetextUnderline)
        if (start + 1 < blocks.Count && blocks[start + 1].Kind == BlockKind.SetextUnderline)
        {
            var underText = lines[start + 1];
            int level = underText.TrimStart().StartsWith('=') ? 1 : 2;
            sb.Append($"<h{level}>");
            AppendInlineContent(sb, blocks[start], lines[start], options);
            sb.Append($"</h{level}>\n");
            return start + 2;
        }

        sb.Append("<p>");
        int i = start;
        bool first = true;
        bool prevHadBreak = false;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.Paragraph)
        {
            // Stop at blank lines
            if (string.IsNullOrWhiteSpace(lines[i]))
                break;

            if (!first && !prevHadBreak)
                sb.Append('\n');
            first = false;

            var block = blocks[i];
            var text = lines[i];

            // Determine if there's a following line in this paragraph
            bool hasNextLine = (i + 1) < blocks.Count
                && blocks[i + 1].Kind == BlockKind.Paragraph
                && !string.IsNullOrWhiteSpace(lines[i + 1])
                && (i + 1 >= blocks.Count - 1 || blocks[i + 2].Kind != BlockKind.SetextUnderline);

            // Hard breaks only apply when there's a continuation line
            bool hardBreak = hasNextLine && MarkdownParser.IsTrailingHardBreak(block, text);
            bool trailingSpaces = hasNextLine && !hardBreak && text.Length >= 2 && text[^1] == ' ' && text[^2] == ' ';
            string content;
            if (hardBreak) content = text[..^1];
            else if (trailingSpaces) content = text.TrimEnd();
            else content = text;

            // CommonMark: strip leading/trailing whitespace from paragraph lines
            int leadingSpaces = 0;
            while (leadingSpaces < content.Length && content[leadingSpaces] == ' ') leadingSpaces++;
            content = content.TrimStart();
            if (!hardBreak && !trailingSpaces)
                content = content.TrimEnd();

            AppendInlineHtml(sb, content, block, leadingSpaces, options);

            if (hardBreak || trailingSpaces)
                sb.Append("<br />\n");

            prevHadBreak = hardBreak || trailingSpaces;
            i++;

            // Stop if next block is a setext underline (belongs to this paragraph as heading)
            if (i < blocks.Count && blocks[i].Kind == BlockKind.SetextUnderline)
                break;
        }
        sb.Append("</p>\n");
        return i;
    }

    static int RenderHtmlBlock(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.HtmlBlock)
        {
            sb.Append(lines[i]);
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
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.Blockquote)
        {
            string text = lines[i];
            string stripped = StripBlockquotePrefix(text);
            innerLines.Add(stripped);
            i++;
        }

        var innerBlocks = MarkdownParser.Parse(idx => innerLines[idx], innerLines.Count);
        sb.Append(RenderBlocks(innerBlocks, innerLines, options, depth + 1));

        sb.Append("</blockquote>\n");
        return i;
    }

    static int RenderUnorderedList(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        sb.Append("<ul>\n");
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.UnorderedListItem)
        {
            sb.Append("<li>");
            string content = StripListPrefix(lines[i]);
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

    static int RenderOrderedList(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start, HtmlEmitterOptions options)
    {
        int startNum = GetOrderedListStart(lines[start]);
        if (startNum != 1)
            sb.Append($"<ol start=\"{startNum}\">\n");
        else
            sb.Append("<ol>\n");

        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.OrderedListItem)
        {
            sb.Append("<li>");
            string content = StripOrderedListPrefix(lines[i]);
            var innerBlocks = MarkdownParser.Parse(_ => content, 1);
            if (innerBlocks.Count > 0)
                AppendInlineHtml(sb, content, innerBlocks[0], 0, options);
            else
                sb.Append(HtmlEncode(content));
            sb.Append("</li>\n");
            i++;
        }
        sb.Append("</ol>\n");
        return i;
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
                ? "<input type=\"checkbox\" disabled=\"\" checked=\"\" /> "
                : "<input type=\"checkbox\" disabled=\"\" /> ");
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

        sb.Append("<table>\n<thead>\n<tr>\n");

        // Header row
        if (tableBlock.TableRow != null)
        {
            var cells = tableBlock.TableRow.Cells;
            for (int c = 0; c < cells.Count; c++)
            {
                var alignment = tableInfo != null && c < tableInfo.Alignments.Count
                    ? tableInfo.Alignments[c]
                    : ColumnAlignment.Left;
                string alignAttr = alignment switch
                {
                    ColumnAlignment.Center => " align=\"center\"",
                    ColumnAlignment.Right => " align=\"right\"",
                    _ => ""
                };
                var (cs, ce) = cells[c].TrimContent(lines[start]);
                string cellText = lines[start][cs..ce];
                sb.Append($"<th{alignAttr}>");
                sb.Append(HtmlEncode(cellText));
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
                if (row != null)
                {
                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        var alignment = tableInfo != null && c < tableInfo.Alignments.Count
                            ? tableInfo.Alignments[c]
                            : ColumnAlignment.Left;
                        string alignAttr = alignment switch
                        {
                            ColumnAlignment.Center => " align=\"center\"",
                            ColumnAlignment.Right => " align=\"right\"",
                            _ => ""
                        };
                        var (cs, ce) = row.Cells[c].TrimContent(lines[i]);
                        string cellText = lines[i][cs..ce];
                        sb.Append($"<td{alignAttr}>");
                        sb.Append(HtmlEncode(cellText));
                        sb.Append("</td>\n");
                    }
                }
                sb.Append("</tr>\n");
                i++;
            }
            sb.Append("</tbody>\n");
        }

        sb.Append("</table>\n");
        return i;
    }

    static void AppendInlineContent(StringBuilder sb, ParsedBlock block, string text, HtmlEmitterOptions options)
    {
        AppendInlineHtml(sb, text, block, 0, options);
    }

    static void AppendInlineHtml(StringBuilder sb, string text, ParsedBlock block, int offset, HtmlEmitterOptions options)
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
                    bool startsWithSpace = (contentStart - offset) >= 0 && (contentStart - offset) < text.Length && text[contentStart - offset] == ' ';
                    bool endsWithSpace = (contentEnd - 1 - offset) >= 0 && (contentEnd - 1 - offset) < text.Length && text[contentEnd - 1 - offset] == ' ';
                    if (startsWithSpace && endsWithSpace)
                    {
                        bool allSpaces = true;
                        for (int k = contentStart; k < contentEnd; k++)
                        {
                            int ti = k - offset;
                            if (ti >= 0 && ti < text.Length && text[ti] != ' ')
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
                    || htmlStyle == InlineStyle.Bold || htmlStyle == InlineStyle.BoldItalic)
                {
                    int htmlEnd = TryMatchInlineHtml(text, ti);
                    if (htmlEnd > ti)
                    {
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
        // Handle `- ` or `* ` with optional leading spaces
        var trimmed = text.AsSpan();
        int spaces = 0;
        while (spaces < trimmed.Length && trimmed[spaces] == ' ') spaces++;
        if (spaces < trimmed.Length && (trimmed[spaces] == '-' || trimmed[spaces] == '*'))
        {
            int afterMarker = spaces + 1;
            if (afterMarker < trimmed.Length && trimmed[afterMarker] == ' ')
                return text[(afterMarker + 1)..];
            return text[afterMarker..];
        }
        return text;
    }

    static string StripOrderedListPrefix(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        if (i < text.Length && (text[i] == '.' || text[i] == ')')) i++;
        if (i < text.Length && text[i] == ' ') i++;
        return text[i..];
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

        // Named entity — use WebUtility.HtmlDecode
        string result = WebUtility.HtmlDecode(entity);
        if (result != entity)
        {
            decoded = result;
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
                    if (text[p] == ' ' || text[p] == '"' || text[p] == '\'' || text[p] == '=' || text[p] == '<' || text[p] == '>' || text[p] == '`')
                        return start;
                    while (p < text.Length && text[p] != ' ' && text[p] != '"' && text[p] != '\'' && text[p] != '=' && text[p] != '<' && text[p] != '>' && text[p] != '`')
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
                if (bracketClose >= 0)
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
                    string entity = text[i..(semiIdx + 1)];
                    string decoded = WebUtility.HtmlDecode(entity);
                    if (decoded != entity)
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
