using System.Text;
using System.Web;
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
        // For now, render content directly — inline parsing uses raw offsets which
        // don't map well after closing # stripping. Re-parse the content inline.
        var innerBlocks = MarkdownParser.Parse(_ => content, 1);
        if (innerBlocks.Count > 0)
            AppendInlineHtml(sb, content, innerBlocks[0], 0, options);
        else
            sb.Append(HttpUtility.HtmlEncode(content));
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
        while (i < blocks.Count && blocks[i].Kind == BlockKind.Paragraph)
        {
            // Stop at blank lines
            if (string.IsNullOrWhiteSpace(lines[i]))
                break;

            if (!first)
                sb.Append('\n');
            first = false;

            var block = blocks[i];
            var text = lines[i];
            bool hardBreak = MarkdownParser.IsTrailingHardBreak(block, text);
            string content = hardBreak ? text[..^1] : text;
            AppendInlineHtml(sb, content, block, 0, options);

            if (hardBreak)
                sb.Append("<br />\n");

            i++;

            // Stop if next block is a setext underline (belongs to this paragraph as heading)
            if (i < blocks.Count && blocks[i].Kind == BlockKind.SetextUnderline)
                break;
        }
        sb.Append("</p>\n");
        return i;
    }

    static int RenderFencedCode(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        // First block should be the fence delimiter
        var firstBlock = blocks[start];
        string? language = firstBlock.CodeLanguage;

        if (language != null)
            sb.Append($"<pre><code class=\"language-{HttpUtility.HtmlAttributeEncode(language)}\">");
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

            sb.Append(HttpUtility.HtmlEncode(lines[i]));
            sb.Append('\n');
            i++;
        }

        sb.Append("</code></pre>\n");
        return i;
    }

    static int RenderIndentedCode(StringBuilder sb, List<ParsedBlock> blocks, List<string> lines, int start)
    {
        sb.Append("<pre><code>");
        int i = start;
        while (i < blocks.Count && blocks[i].Kind == BlockKind.IndentedCodeLine)
        {
            string text = lines[i];
            // Remove the 4-space indent
            string content = RemoveIndent(text, 4);
            sb.Append(HttpUtility.HtmlEncode(content));
            sb.Append('\n');
            i++;
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
                sb.Append(HttpUtility.HtmlEncode(content));
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
                sb.Append(HttpUtility.HtmlEncode(content));
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
                sb.Append(HttpUtility.HtmlEncode(content));
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
                sb.Append(HttpUtility.HtmlEncode(cellText));
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
                        sb.Append(HttpUtility.HtmlEncode(cellText));
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

        // Emphasis markers (*, **, ***, ~~)
        if (emphMarkers != null)
        {
            foreach (var m in emphMarkers)
                for (int j = m.Start; j < m.Start + m.Length; j++)
                    hidden.Add(j);
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

        // Collect link/image replacements sorted by position
        var replacements = new List<(int Start, int End, string Html)>();

        if (images != null)
        {
            foreach (var img in images)
            {
                string alt = HttpUtility.HtmlEncode(img.AltText);
                string url = HttpUtility.HtmlEncode(img.Url);
                string titleAttr = img.Title != null ? $" title=\"{HttpUtility.HtmlEncode(img.Title)}\"" : "";
                replacements.Add((img.Start, img.Start + img.Length, $"<img src=\"{url}\" alt=\"{alt}\"{titleAttr} />"));
            }
        }

        if (links != null)
        {
            foreach (var link in links)
            {
                string url = HttpUtility.HtmlEncode(link.Url);
                string titleAttr = link.Title != null ? $" title=\"{HttpUtility.HtmlEncode(link.Title)}\"" : "";
                string linkText = HttpUtility.HtmlEncode(link.Text);
                replacements.Add((link.Start, link.Start + link.Length, $"<a href=\"{url}\"{titleAttr}>{linkText}</a>"));
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
                if (currentStyle != null) { CloseTag(sb, currentStyle.Value); currentStyle = null; }
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

            // Skip hidden (delimiter) characters
            if (hidden.Contains(pos))
            {
                pos++;
                continue;
            }

            // Determine style at this position
            var style = GetStyleAt(runs, pos);
            if (style == InlineStyle.Image || style == InlineStyle.Link)
                style = InlineStyle.Normal;

            // Handle style transitions
            if (style != currentStyle)
            {
                if (currentStyle != null) CloseTag(sb, currentStyle.Value);
                currentStyle = style != InlineStyle.Normal ? style : null;
                if (currentStyle != null) OpenTag(sb, currentStyle.Value);
            }

            sb.Append(HttpUtility.HtmlEncode(text[(pos - offset)..(pos - offset + 1)]));
            pos++;
        }

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
        if (text.StartsWith("> ")) return text[2..];
        if (text.StartsWith(">")) return text[1..];
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
}
