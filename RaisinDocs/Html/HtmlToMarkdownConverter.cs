using System.Text;

namespace RaisinDocs;

/// <summary>
/// Converts HTML content (typically from clipboard) to RaisinDocs markdown.
/// Handles structural elements (headers, blockquotes, rules) and inline formatting (bold, italic, colors).
///
/// Conversion pipeline:
/// 1. Extract HTML fragment from CF_HTML clipboard format
/// 2. Preprocess block-level elements (headers, hr, blockquotes)
/// 3. Parse HTML into typed segments with formatting information
/// 4. Convert segments to markdown with appropriate syntax
/// 5. Merge adjacent segments and apply div color wrapping
/// </summary>
internal static class HtmlToMarkdownConverter
{
    /// <summary>
    /// Converts HTML from clipboard format to RaisinDocs markdown with color support.
    /// Returns null if no formatting is detected in the HTML.
    /// </summary>
    internal static string? ConvertToColoredMarkdown(string cfHtml)
    {
        var fragment = ExtractFragment(cfHtml);
        if (fragment == null) return null;

        // Preprocess block-level elements
        var preprocessed = PreprocessBlockElements(fragment);

        var lines = ParseHtmlFragment(preprocessed);
        if (lines == null) return null;

        return ConvertToMarkdown(lines);
    }

    /// <summary>
    /// Extracts the HTML fragment from CF_HTML clipboard format (strips header and delimiters).
    /// </summary>
    internal static string? ExtractFragment(string cfHtml)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";

        int start = cfHtml.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += startMarker.Length;

        int end = cfHtml.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) return null;

        return cfHtml[start..end];
    }

    /// <summary>
    /// Preprocesses block-level HTML elements by converting them to markdown and wrapping in markers.
    /// This allows the downstream parser to recognize and handle them appropriately.
    /// </summary>
    private static string PreprocessBlockElements(string html)
    {
        var result = new StringBuilder();
        int pos = 0;

        while (pos < html.Length)
        {
            int tagStart = html.IndexOf('<', pos);
            if (tagStart < 0)
            {
                result.Append(html[pos..]);
                break;
            }

            // Append text before tag
            if (tagStart > pos)
                result.Append(html[pos..tagStart]);

            // Find tag end
            int tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                result.Append(html[tagStart..]);
                break;
            }

            string tag = html[tagStart..(tagEnd + 1)];

            // Handle headers
            if (IsHeaderTag(tag, out int headerLevel))
            {
                string closeTagStr = $"</h{headerLevel}>";
                int closeTagStart = html.IndexOf(closeTagStr, tagEnd, StringComparison.OrdinalIgnoreCase);
                if (closeTagStart > 0)
                {
                    string headerContent = html[(tagEnd + 1)..closeTagStart];
                    string markdown = new string('#', headerLevel) + " " + StripTags(headerContent).Trim();
                    result.Append("<!--@MARKDOWN_BLOCK-->");
                    result.Append(markdown);
                    result.Append("<!--/@MARKDOWN_BLOCK-->\n");  // Add newline to separate from next block
                    pos = closeTagStart + closeTagStr.Length;
                    continue;
                }
                // If close tag not found, fall through to regular tag handling
            }

            // Handle horizontal rule
            if (IsHrTag(tag))
            {
                result.Append("<!--@MARKDOWN_BLOCK-->---<!--/@MARKDOWN_BLOCK-->\n");  // Add newline
                pos = tagEnd + 1;
                continue;
            }

            // Handle blockquote
            if (IsBlockquoteOpenTag(tag))
            {
                int closeTagStart = html.IndexOf("</blockquote>", tagEnd, StringComparison.OrdinalIgnoreCase);
                if (closeTagStart > 0)
                {
                    string quoteContent = html[(tagEnd + 1)..closeTagStart];
                    // Extract text from nested p tags
                    string plainText = ExtractTextFromBlockquote(quoteContent);
                    var lines = plainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    result.Append("<!--@MARKDOWN_BLOCK-->");
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            if (result[^1] != '\n')
                                result.Append('\n');
                            result.Append("> ").Append(trimmed);
                        }
                    }
                    result.Append("<!--/@MARKDOWN_BLOCK-->\n");  // Add newline
                    pos = closeTagStart + "</blockquote>".Length;
                    continue;
                }
            }

            // Handle unordered lists
            if (IsListOpenTag(tag, out bool isOrderedList) && !isOrderedList)
            {
                int closeTagStart = FindMatchingListCloseTag(html, tagEnd + 1, "ul");
                if (closeTagStart > 0)
                {
                    string listContent = html[(tagEnd + 1)..closeTagStart];
                    string markdown = ListConverter.ConvertList(listContent, false, 0).Trim();
                    result.Append("<!--@MARKDOWN_BLOCK-->");
                    result.Append(markdown);
                    result.Append("<!--/@MARKDOWN_BLOCK-->\n");  // Add newline
                    pos = closeTagStart + "</ul>".Length;
                    continue;
                }
            }

            // Handle ordered lists
            if (IsListOpenTag(tag, out isOrderedList) && isOrderedList)
            {
                int closeTagStart = FindMatchingListCloseTag(html, tagEnd + 1, "ol");
                if (closeTagStart > 0)
                {
                    string listContent = html[(tagEnd + 1)..closeTagStart];
                    string markdown = ListConverter.ConvertList(listContent, true, 0).Trim();
                    result.Append("<!--@MARKDOWN_BLOCK-->");
                    result.Append(markdown);
                    result.Append("<!--/@MARKDOWN_BLOCK-->\n");  // Add newline
                    pos = closeTagStart + "</ol>".Length;
                    continue;
                }
            }

            result.Append(tag);
            pos = tagEnd + 1;
        }

        return result.ToString();
    }

    private static bool IsHeaderTag(string tag, out int level)
    {
        level = 0;
        if (tag.Length < 4) return false;

        // Check for <h1> through <h6> - handle both <h1> and <h1 ...>
        if ((tag[1] == 'h' || tag[1] == 'H') && char.IsDigit(tag[2]))
        {
            level = tag[2] - '0';
            // Check what comes after the digit: > or space (for attributes)
            if (level >= 1 && level <= 6)
            {
                if (tag.Length == 3) return false; // Just <h
                char nextChar = tag[3];
                if (nextChar == '>' || char.IsWhiteSpace(nextChar))
                    return true;
            }
        }

        return false;
    }

    private static bool IsHrTag(string tag)
    {
        return tag.StartsWith("<hr", StringComparison.OrdinalIgnoreCase) &&
               (tag.Contains("/>") || tag.EndsWith(">"));
    }

    private static bool IsBlockquoteOpenTag(string tag)
    {
        return tag.Equals("<blockquote>", StringComparison.OrdinalIgnoreCase) ||
               (tag.StartsWith("<blockquote", StringComparison.OrdinalIgnoreCase) && tag.EndsWith(">"));
    }

    private static bool IsListOpenTag(string tag, out bool isOrdered)
    {
        isOrdered = false;

        // Check for <ul>
        if (tag.Equals("<ul>", StringComparison.OrdinalIgnoreCase) ||
            (tag.StartsWith("<ul", StringComparison.OrdinalIgnoreCase) && tag.EndsWith(">")))
        {
            isOrdered = false;
            return true;
        }

        // Check for <ol>
        if (tag.Equals("<ol>", StringComparison.OrdinalIgnoreCase) ||
            (tag.StartsWith("<ol", StringComparison.OrdinalIgnoreCase) && tag.EndsWith(">")))
        {
            isOrdered = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the position of the matching closing tag for a list, accounting for nested lists.
    /// </summary>
    private static int FindMatchingListCloseTag(string html, int startPos, string tagName)
    {
        int depth = 1;
        int pos = startPos;
        string openTag = $"<{tagName}";
        string closeTag = $"</{tagName}>";

        while (pos < html.Length && depth > 0)
        {
            int nextOpen = html.IndexOf(openTag, pos, StringComparison.OrdinalIgnoreCase);
            int nextClose = html.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);

            // If we can't find either tag, we're done (tag mismatch)
            if (nextClose < 0) return -1;

            // If we find an opening tag before the next closing tag, increment depth
            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + 1;
            }
            else
            {
                depth--;
                if (depth == 0)
                    return nextClose; // Found matching close tag
                pos = nextClose + closeTag.Length;
            }
        }

        return depth == 0 ? pos : -1;
    }

    private static string StripTags(string html)
    {
        var result = new StringBuilder();
        int pos = 0;

        while (pos < html.Length)
        {
            int tagStart = html.IndexOf('<', pos);
            if (tagStart < 0)
            {
                result.Append(html[pos..]);
                break;
            }

            result.Append(html[pos..tagStart]);

            int tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) break;

            string tagName = html.Substring(tagStart + 1, Math.Min(tagEnd - tagStart - 1, 10)).Split(' ', '/')[0].ToLowerInvariant();

            // Preserve inline formatting tags
            if (tagName == "strong" || tagName == "b" || tagName == "em" || tagName == "i")
            {
                result.Append(html[tagStart..(tagEnd + 1)]);
            }

            pos = tagEnd + 1;
        }

        return result.ToString();
    }

    private static string ExtractTextFromBlockquote(string html)
    {
        var result = new StringBuilder();
        int pos = 0;

        while (pos < html.Length)
        {
            // Look for <p> tags
            int pStart = html.IndexOf("<p", pos, StringComparison.OrdinalIgnoreCase);
            if (pStart < 0)
            {
                // No more <p> tags, add remaining text
                var remaining = StripTags(html[pos..]);
                if (!string.IsNullOrWhiteSpace(remaining))
                    result.Append(remaining.Trim());
                break;
            }

            // Find end of <p> tag
            int pTagEnd = html.IndexOf('>', pStart);
            if (pTagEnd < 0) break;

            // Find </p>
            int pCloseStart = html.IndexOf("</p>", pTagEnd, StringComparison.OrdinalIgnoreCase);
            if (pCloseStart < 0) break;

            string pContent = html[(pTagEnd + 1)..pCloseStart];
            var text = StripTags(pContent).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (result.Length > 0)
                    result.Append('\n');
                result.Append(text);
            }

            pos = pCloseStart + 4;
        }

        return result.ToString();
    }

    private static List<List<ColoredSegment>>? ParseHtmlFragment(string html)
    {
        int contentStart, contentEnd;
        bool preMode;

        int preStart = html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase);
        if (preStart >= 0)
        {
            int afterPre = html.IndexOf('>', preStart + 4);
            if (afterPre < 0) return null;
            contentStart = afterPre + 1;
            contentEnd = html.IndexOf("</pre>", contentStart, StringComparison.OrdinalIgnoreCase);
            if (contentEnd < 0) contentEnd = html.Length;
            preMode = true;
        }
        else
        {
            contentStart = 0;
            contentEnd = html.Length;
            preMode = false;
        }

        var lines = new List<List<ColoredSegment>>();
        var currentLine = new List<ColoredSegment>();
        lines.Add(currentLine);

        bool hasAnyFormatting = false;
        int pos = contentStart;
        var textBuf = new StringBuilder();
        var styleStack = new List<(RgbColor? fg, RgbColor? bg, bool bold, bool italic)>();
        bool hadParagraph = false;

        while (pos < contentEnd)
        {
            char c = html[pos];

            if (c == '<')
            {
                // Handle markdown blocks - check for the marker string
                const string markdownBlockStart = "<!--@MARKDOWN_BLOCK-->";
                const string markdownBlockEnd = "<!--/@MARKDOWN_BLOCK-->";
                if (pos + markdownBlockStart.Length <= contentEnd &&
                    html.AsSpan(pos, markdownBlockStart.Length).Equals(markdownBlockStart.AsSpan(), StringComparison.Ordinal))
                {
                    var cur = HtmlParsingContext.CurrentStyle(styleStack);
                    HtmlParsingContext.FlushText(textBuf, currentLine, cur.fg, cur.bg, cur.bold, cur.italic);

                    int blockEnd = html.IndexOf(markdownBlockEnd, pos + markdownBlockStart.Length, StringComparison.Ordinal);
                    if (blockEnd > 0)
                    {
                        string markdownContent = html[(pos + markdownBlockStart.Length)..blockEnd];
                        var markdownLines = markdownContent.Split('\n');

                        foreach (var mdLine in markdownLines)
                        {
                            if (currentLine.Count > 0 || mdLine.Length > 0)
                            {
                                HtmlParsingContext.FlushText(textBuf, currentLine, null, null, false, false);
                                currentLine = new List<ColoredSegment>();
                                lines.Add(currentLine);
                            }
                            if (!string.IsNullOrEmpty(mdLine))
                            {
                                currentLine.Add(new ColoredSegment(mdLine, null, null, false, false));
                                hasAnyFormatting = true;
                            }
                        }

                        pos = blockEnd + markdownBlockEnd.Length;
                        continue;
                    }
                }

                if (pos + 4 <= contentEnd && html.AsSpan(pos, 4).Equals("<!--".AsSpan(), StringComparison.Ordinal))
                {
                    var cur = HtmlParsingContext.CurrentStyle(styleStack);
                    HtmlParsingContext.FlushText(textBuf, currentLine, cur.fg, cur.bg, cur.bold, cur.italic);
                    int commentEnd = html.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                    pos = commentEnd >= 0 ? commentEnd + 3 : contentEnd;
                    continue;
                }

                bool closing = pos + 1 < contentEnd && html[pos + 1] == '/';
                int nameStart = pos + (closing ? 2 : 1);
                int nameEnd = nameStart;
                while (nameEnd < contentEnd && html[nameEnd] != '>' && html[nameEnd] != ' '
                       && html[nameEnd] != '/' && html[nameEnd] != '\t' && html[nameEnd] != '\n'
                       && html[nameEnd] != '\r')
                    nameEnd++;

                var tagName = html.AsSpan(nameStart, nameEnd - nameStart);

                int tagClose = html.IndexOf('>', pos + 1);
                if (tagClose < 0) break;

                var curStyle = HtmlParsingContext.CurrentStyle(styleStack);

                if (tagName.Equals("span".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    HtmlParsingContext.FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    if (closing)
                    {
                        if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    }
                    else
                    {
                        var (fg, bg, bold, italic) = HtmlParsingContext.ParseStyleFromTag(html.AsSpan(pos, tagClose - pos + 1));
                        fg ??= curStyle.fg;
                        bg ??= curStyle.bg;
                        bold = bold || curStyle.bold;
                        italic = italic || curStyle.italic;
                        if (fg != null || bg != null || bold || italic) hasAnyFormatting = true;
                        styleStack.Add((fg, bg, bold, italic));
                    }
                }
                else if (HtmlParsingContext.IsBoldTag(tagName))
                {
                    HtmlParsingContext.FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    if (closing)
                    {
                        if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    }
                    else
                    {
                        styleStack.Add((curStyle.fg, curStyle.bg, true, curStyle.italic));
                        hasAnyFormatting = true;
                    }
                }
                else if (HtmlParsingContext.IsItalicTag(tagName))
                {
                    HtmlParsingContext.FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    if (closing)
                    {
                        if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    }
                    else
                    {
                        styleStack.Add((curStyle.fg, curStyle.bg, curStyle.bold, true));
                        hasAnyFormatting = true;
                    }
                }
                else if (!preMode && !closing && tagName.Equals("p".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (hadParagraph)
                    {
                        HtmlParsingContext.FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                        currentLine = new List<ColoredSegment>();
                        lines.Add(currentLine);
                    }
                    hadParagraph = true;
                }
                else if (!closing && tagName.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    HtmlParsingContext.FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    currentLine = new List<ColoredSegment>();
                    lines.Add(currentLine);
                }

                pos = tagClose + 1;
            }
            else if (c == '\n')
            {
                if (preMode)
                {
                    var cur = HtmlParsingContext.CurrentStyle(styleStack);
                    HtmlParsingContext.FlushText(textBuf, currentLine, cur.fg, cur.bg, cur.bold, cur.italic);
                    currentLine = new List<ColoredSegment>();
                    lines.Add(currentLine);
                }
                else if (textBuf.Length > 0 && textBuf[textBuf.Length - 1] != ' ')
                {
                    textBuf.Append(' ');
                }
                pos++;
            }
            else if (c == '\r')
            {
                pos++;
            }
            else if (c == '&')
            {
                pos += HtmlParsingContext.DecodeEntity(html, pos, textBuf);
            }
            else
            {
                textBuf.Append(c);
                pos++;
            }
        }

        var finalStyle = HtmlParsingContext.CurrentStyle(styleStack);
        HtmlParsingContext.FlushText(textBuf, currentLine, finalStyle.fg, finalStyle.bg, finalStyle.bold, finalStyle.italic);

        if (!hasAnyFormatting) return null;

        HtmlParsingContext.MergeAdjacentSegments(lines);

        return lines;
    }

    private static string ConvertToMarkdown(List<List<ColoredSegment>> lines)
    {
        var uniformColors = new (RgbColor? fg, RgbColor? bg, bool isUniform)[lines.Count];
        for (int i = 0; i < lines.Count; i++)
            uniformColors[i] = AnalyzeLine(lines[i]);

        var output = new List<string>();
        int idx = 0;

        while (idx < lines.Count)
        {
            var (fg, bg, isUniform) = uniformColors[idx];

            if (isUniform && (fg != null || bg != null))
            {
                int runEnd = idx + 1;
                while (runEnd < lines.Count
                       && uniformColors[runEnd].isUniform
                       && uniformColors[runEnd].fg == fg
                       && uniformColors[runEnd].bg == bg)
                    runEnd++;

                if (runEnd - idx >= 2)
                {
                    output.Add(FormatDivOpen(fg, bg));
                    for (int k = idx; k < runEnd; k++)
                        output.Add(FormatTextWithStyle(lines[k]));
                    output.Add("<!--/@div-->");
                    idx = runEnd;
                    continue;
                }
            }

            output.Add(FormatInlineLine(lines[idx]));
            idx++;
        }

        return string.Join("\n", output);
    }

    private static (RgbColor? fg, RgbColor? bg, bool isUniform) AnalyzeLine(List<ColoredSegment> segments)
    {
        if (segments.Count == 0) return (null, null, true);

        RgbColor? fg = null;
        RgbColor? bg = null;
        bool first = true;
        bool allSameColor = true;
        bool hasColor = false;

        foreach (var seg in segments)
        {
            if (seg.Text.Length == 0) continue;

            if (seg.Foreground != null || seg.Background != null)
                hasColor = true;

            if (first)
            {
                fg = seg.Foreground;
                bg = seg.Background;
                first = false;
            }
            else if (seg.Foreground != fg || seg.Background != bg)
            {
                allSameColor = false;
                break;
            }
        }

        if (!hasColor) return (null, null, true);
        return (fg, bg, allSameColor);
    }

    private static string FormatColor(RgbColor color)
    {
        return MarkdownParser.TryGetColorName(color) ?? color.ToHex();
    }

    private static string FormatDivOpen(RgbColor? fg, RgbColor? bg)
    {
        var parts = new List<string>(2);
        if (fg != null) parts.Add($"fg:{FormatColor(fg.Value)}");
        if (bg != null) parts.Add($"bg:{FormatColor(bg.Value)}");
        return $"<!--@div {string.Join(" ", parts)}-->";
    }

    private static string WrapStyle(string text, bool bold, bool italic)
    {
        if (bold && italic) return $"***{text}***";
        if (bold) return $"**{text}**";
        if (italic) return $"*{text}*";
        return text;
    }

    private static string FormatTextWithStyle(List<ColoredSegment> segments)
        => FormatTextWithStyle(segments, 0, segments.Count);

    private static string FormatTextWithStyle(List<ColoredSegment> segments, int start, int count)
    {
        if (count == 0) return "";
        int end = start + count;
        var sb = new StringBuilder();
        int i = start;
        while (i < end)
        {
            int runEnd = i + 1;
            while (runEnd < end
                   && segments[runEnd].Bold == segments[i].Bold
                   && segments[runEnd].Italic == segments[i].Italic)
                runEnd++;

            var text = new StringBuilder();
            for (int k = i; k < runEnd; k++)
                text.Append(segments[k].Text);

            sb.Append(WrapStyle(text.ToString(), segments[i].Bold, segments[i].Italic));
            i = runEnd;
        }
        return sb.ToString();
    }

    private static string FormatInlineLine(List<ColoredSegment> segments)
    {
        if (segments.Count == 0) return "";
        if (segments.Count == 1 && segments[0].Foreground == null && segments[0].Background == null)
            return WrapStyle(segments[0].Text, segments[0].Bold, segments[0].Italic);

        var sb = new StringBuilder();
        int i = 0;
        while (i < segments.Count)
        {
            var seg = segments[i];
            bool hasFg = seg.Foreground != null;
            bool hasBg = seg.Background != null;

            int runEnd = i + 1;
            while (runEnd < segments.Count
                   && segments[runEnd].Foreground == seg.Foreground
                   && segments[runEnd].Background == seg.Background)
                runEnd++;

            if (!hasFg && !hasBg)
            {
                sb.Append(FormatTextWithStyle(segments, i, runEnd - i));
                i = runEnd;
                continue;
            }

            sb.Append("<!--@");
            if (hasFg) sb.Append($"fg:{FormatColor(seg.Foreground!.Value)}");
            if (hasFg && hasBg) sb.Append(' ');
            if (hasBg) sb.Append($"bg:{FormatColor(seg.Background!.Value)}");
            sb.Append("-->");

            sb.Append(FormatTextWithStyle(segments, i, runEnd - i));

            if (hasFg && hasBg) sb.Append("<!--/@-->");
            else if (hasFg) sb.Append("<!--/@fg-->");
            else sb.Append("<!--/@bg-->");

            i = runEnd;
        }
        return sb.ToString();
    }

    // --- Copy-out: markdown → HTML clipboard ---

    /// <summary>
    /// Converts RaisinDocs markdown to HTML for clipboard (reverse direction).
    /// Handles bold, italic, colors, and RaisinDocs color comment syntax.
    /// </summary>
    internal static string? ConvertToHtmlClipboard(string markdownText)
    {
        if (!markdownText.Contains("<!--@") && !markdownText.Contains('*'))
            return null;

        var lines = markdownText.Replace("\r\n", "\n").Split('\n');
        var htmlLines = new List<string>();
        RgbColor? divFg = null, divBg = null;
        bool anyFormatting = false;

        foreach (var line in lines)
        {
            bool hasDivOpen = MarkdownParser.TryExtractDivOpen(line, out int divOpenTagEnd);
            bool hasDivClose = MarkdownParser.TryExtractDivClose(line, out int divCloseTagStart);

            if (hasDivOpen)
            {
                var tagText = line[..divOpenTagEnd].TrimEnd();
                var props = tagText.AsSpan().Trim();
                props = props[9..^3]; // strip <!--@div  and -->
                HtmlParsingContext.ParseColorProps(props, out divFg, out divBg);
                anyFormatting = true;
            }

            bool hasContent;
            if (hasDivOpen || hasDivClose)
            {
                int cs = hasDivOpen ? divOpenTagEnd : 0;
                int ce = hasDivClose ? divCloseTagStart : line.Length;
                hasContent = ce > cs && line.AsSpan()[cs..ce].Trim().Length > 0;
            }
            else
            {
                hasContent = true;
            }

            if (hasContent || (!hasDivOpen && !hasDivClose))
            {
                var (html, hadFormatting) = ConvertLineToHtml(line, divFg, divBg);
                if (hadFormatting) anyFormatting = true;
                htmlLines.Add(html);
            }

            if (hasDivClose)
            {
                divFg = null;
                divBg = null;
            }
        }

        if (!anyFormatting) return null;

        var fragment = "<pre style=\"font-family:Consolas,'Courier New',monospace;font-size:10pt;\">"
                       + string.Join("\n", htmlLines) + "</pre>";
        return WrapClipboardHeader(fragment);
    }

    private static (string html, bool hadFormatting) ConvertLineToHtml(
        string line, RgbColor? divFg, RgbColor? divBg)
    {
        bool hadFormatting = divFg != null || divBg != null;
        var segments = new List<ColoredSegment>();
        var textBuf = new StringBuilder();
        RgbColor? fg = divFg, bg = divBg;
        bool bold = false, italic = false;
        int pos = 0;

        while (pos < line.Length)
        {
            if (line[pos] == '<' && pos + 4 < line.Length
                && line[pos + 1] == '!' && line[pos + 2] == '-' && line[pos + 3] == '-')
            {
                if (pos + 5 < line.Length && line[pos + 4] == '/' && line[pos + 5] == '@')
                {
                    int end = line.IndexOf("-->", pos + 6, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        HtmlParsingContext.FlushText(textBuf, segments, fg, bg, bold, italic);
                        var closeTag = line.AsSpan(pos + 6, end - pos - 6);
                        if (closeTag.Equals("fg".AsSpan(), StringComparison.Ordinal))
                            fg = divFg;
                        else if (closeTag.Equals("bg".AsSpan(), StringComparison.Ordinal))
                            bg = divBg;
                        else
                        {
                            fg = divFg;
                            bg = divBg;
                        }
                        pos = end + 3;
                        hadFormatting = true;
                        continue;
                    }
                }
                else if (line[pos + 4] == '@')
                {
                    int end = line.IndexOf("-->", pos + 5, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        HtmlParsingContext.FlushText(textBuf, segments, fg, bg, bold, italic);
                        var props = line.AsSpan(pos + 5, end - pos - 5);
                        HtmlParsingContext.ParseColorProps(props, out var inlineFg, out var inlineBg);
                        fg = inlineFg ?? divFg;
                        bg = inlineBg ?? divBg;
                        pos = end + 3;
                        hadFormatting = true;
                        continue;
                    }
                }
            }

            if (line[pos] == '*')
            {
                int starStart = pos;
                int starCount = 0;
                while (pos < line.Length && line[pos] == '*') { pos++; starCount++; }

                if (starStart == 0 && starCount == 1 && pos < line.Length && line[pos] == ' ')
                {
                    textBuf.Append('*');
                    continue;
                }

                HtmlParsingContext.FlushText(textBuf, segments, fg, bg, bold, italic);
                if (starCount >= 3) { bold = !bold; italic = !italic; }
                else if (starCount == 2) bold = !bold;
                else italic = !italic;
                hadFormatting = true;
                continue;
            }

            textBuf.Append(line[pos]);
            pos++;
        }

        HtmlParsingContext.FlushText(textBuf, segments, fg, bg, bold, italic);

        var html = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.Text.Length == 0) continue;
            bool hasFg = seg.Foreground != null;
            bool hasBg = seg.Background != null;
            bool needsSpan = hasFg || hasBg || seg.Bold || seg.Italic;

            if (needsSpan)
            {
                html.Append("<span style=\"");
                if (hasFg) html.Append($"color:{seg.Foreground!.Value.ToHex()};");
                if (hasBg) html.Append($"background-color:{seg.Background!.Value.ToHex()};");
                if (seg.Bold) html.Append("font-weight:bold;");
                if (seg.Italic) html.Append("font-style:italic;");
                html.Append("\">");
            }

            html.Append(HtmlEncode(seg.Text));

            if (needsSpan) html.Append("</span>");
        }

        return (html.ToString(), hadFormatting);
    }

    private static string HtmlEncode(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string WrapClipboardHeader(string htmlFragment)
    {
        const string header =
            "Version:0.9\r\n" +
            "StartHTML:{0:D8}\r\n" +
            "EndHTML:{1:D8}\r\n" +
            "StartFragment:{2:D8}\r\n" +
            "EndFragment:{3:D8}\r\n";
        const string prefix = "<html><body>\r\n<!--StartFragment-->";
        const string suffix = "<!--EndFragment-->\r\n</body></html>";

        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int prefixLen = Encoding.UTF8.GetByteCount(prefix);
        int fragmentLen = Encoding.UTF8.GetByteCount(htmlFragment);
        int suffixLen = Encoding.UTF8.GetByteCount(suffix);

        int startHtml = headerLen;
        int startFragment = headerLen + prefixLen;
        int endFragment = startFragment + fragmentLen;
        int endHtml = endFragment + suffixLen;

        return string.Format(header, startHtml, endHtml, startFragment, endFragment) +
               prefix + htmlFragment + suffix;
    }
}
