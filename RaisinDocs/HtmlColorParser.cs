using System.Text;

namespace RaisinDocs;

internal static class HtmlColorParser
{
    private readonly record struct ColoredSegment(
        string Text, RgbColor? Foreground, RgbColor? Background, bool Bold, bool Italic);

    internal static string? ConvertToColoredMarkdown(string cfHtml)
    {
        var fragment = ExtractFragment(cfHtml);
        if (fragment == null) return null;

        var lines = ParseHtmlFragment(fragment);
        if (lines == null) return null;

        return ConvertToMarkdown(lines);
    }

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
                if (pos + 4 <= contentEnd && html.AsSpan(pos, 4).Equals("<!--".AsSpan(), StringComparison.Ordinal))
                {
                    var cur = CurrentStyle(styleStack);
                    FlushText(textBuf, currentLine, cur.fg, cur.bg, cur.bold, cur.italic);
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

                var curStyle = CurrentStyle(styleStack);

                if (tagName.Equals("span".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    if (closing)
                    {
                        if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    }
                    else
                    {
                        var (fg, bg, bold, italic) = ParseStyleFromTag(html.AsSpan(pos, tagClose - pos + 1));
                        fg ??= curStyle.fg;
                        bg ??= curStyle.bg;
                        bold = bold || curStyle.bold;
                        italic = italic || curStyle.italic;
                        if (fg != null || bg != null || bold || italic) hasAnyFormatting = true;
                        styleStack.Add((fg, bg, bold, italic));
                    }
                }
                else if (IsBoldTag(tagName))
                {
                    FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
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
                else if (IsItalicTag(tagName))
                {
                    FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
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
                        FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                        currentLine = new List<ColoredSegment>();
                        lines.Add(currentLine);
                    }
                    hadParagraph = true;
                }
                else if (!closing && tagName.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    FlushText(textBuf, currentLine, curStyle.fg, curStyle.bg, curStyle.bold, curStyle.italic);
                    currentLine = new List<ColoredSegment>();
                    lines.Add(currentLine);
                }

                pos = tagClose + 1;
            }
            else if (c == '\n')
            {
                if (preMode)
                {
                    var cur = CurrentStyle(styleStack);
                    FlushText(textBuf, currentLine, cur.fg, cur.bg, cur.bold, cur.italic);
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
                pos += DecodeEntity(html, pos, textBuf);
            }
            else
            {
                textBuf.Append(c);
                pos++;
            }
        }

        var finalStyle = CurrentStyle(styleStack);
        FlushText(textBuf, currentLine, finalStyle.fg, finalStyle.bg, finalStyle.bold, finalStyle.italic);

        if (!hasAnyFormatting) return null;

        MergeAdjacentSegments(lines);

        return lines;
    }

    private static (RgbColor? fg, RgbColor? bg, bool bold, bool italic) CurrentStyle(
        List<(RgbColor? fg, RgbColor? bg, bool bold, bool italic)> stack)
    {
        return stack.Count > 0 ? stack[^1] : default;
    }

    private static bool IsBoldTag(ReadOnlySpan<char> tagName) =>
        tagName.Equals("b".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("strong".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsItalicTag(ReadOnlySpan<char> tagName) =>
        tagName.Equals("i".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
        tagName.Equals("em".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static void FlushText(StringBuilder buf, List<ColoredSegment> line,
        RgbColor? fg, RgbColor? bg, bool bold, bool italic)
    {
        if (buf.Length == 0) return;
        line.Add(new ColoredSegment(buf.ToString(), fg, bg, bold, italic));
        buf.Clear();
    }

    private static (RgbColor? fg, RgbColor? bg, bool bold, bool italic) ParseStyleFromTag(ReadOnlySpan<char> tag)
    {
        int styleIdx = tag.IndexOf("style=".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (styleIdx < 0) return (null, null, false, false);
        int quotePos = styleIdx + 6;
        if (quotePos >= tag.Length) return (null, null, false, false);
        char quote = tag[quotePos];
        if (quote != '"' && quote != '\'') return (null, null, false, false);
        int styleStart = quotePos + 1;

        int styleEnd = tag[styleStart..].IndexOf(quote);
        if (styleEnd < 0) return (null, null, false, false);

        var style = tag[styleStart..(styleStart + styleEnd)];

        RgbColor? fg = null;
        RgbColor? bg = null;

        int bgIdx = style.IndexOf("background-color:".AsSpan(), StringComparison.OrdinalIgnoreCase);
        int fgIdx = style.IndexOf("color:".AsSpan(), StringComparison.OrdinalIgnoreCase);

        if (bgIdx >= 0)
            bg = ParseCssColor(style[(bgIdx + 17)..]);

        if (fgIdx >= 0)
        {
            bool isBgPrefix = fgIdx > 0 && style[fgIdx - 1] == '-';
            if (!isBgPrefix)
                fg = ParseCssColor(style[(fgIdx + 6)..]);
        }

        bool bold = style.IndexOf("font-weight:bold".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
        bool italic = style.IndexOf("font-style:italic".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;

        return (fg, bg, bold, italic);
    }

    private static RgbColor? ParseCssColor(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;

        if (value[0] == '#')
        {
            int end = 1;
            while (end < value.Length && char.IsAsciiHexDigit(value[end])) end++;

            var hex = value[1..end];
            if (hex.Length == 6)
            {
                byte r = (byte)((HexVal(hex[0]) << 4) | HexVal(hex[1]));
                byte g = (byte)((HexVal(hex[2]) << 4) | HexVal(hex[3]));
                byte b = (byte)((HexVal(hex[4]) << 4) | HexVal(hex[5]));
                return new RgbColor(r, g, b);
            }
            if (hex.Length == 3)
            {
                byte r = (byte)((HexVal(hex[0]) << 4) | HexVal(hex[0]));
                byte g = (byte)((HexVal(hex[1]) << 4) | HexVal(hex[1]));
                byte b = (byte)((HexVal(hex[2]) << 4) | HexVal(hex[2]));
                return new RgbColor(r, g, b);
            }
            return null;
        }

        int nameEnd = 0;
        while (nameEnd < value.Length && char.IsLetter(value[nameEnd])) nameEnd++;
        if (nameEnd > 0 && MarkdownParser.TryGetNamedColor(value[..nameEnd], out var color))
            return color;

        return null;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0
    };

    private static int DecodeEntity(string html, int pos, StringBuilder output)
    {
        int semi = html.IndexOf(';', pos + 1);
        if (semi < 0 || semi - pos > 10)
        {
            output.Append(html[pos]);
            return 1;
        }

        var entity = html.AsSpan(pos, semi - pos + 1);
        if (entity.Equals("&lt;".AsSpan(), StringComparison.Ordinal)) output.Append('<');
        else if (entity.Equals("&gt;".AsSpan(), StringComparison.Ordinal)) output.Append('>');
        else if (entity.Equals("&amp;".AsSpan(), StringComparison.Ordinal)) output.Append('&');
        else if (entity.Equals("&quot;".AsSpan(), StringComparison.Ordinal)) output.Append('"');
        else if (entity.Equals("&nbsp;".AsSpan(), StringComparison.OrdinalIgnoreCase)) output.Append(' ');
        else if (entity.Length > 3 && entity[1] == '#')
        {
            var numPart = entity[2..^1];
            int codePoint;
            if (numPart[0] == 'x' || numPart[0] == 'X')
                int.TryParse(numPart[1..].ToString(), System.Globalization.NumberStyles.HexNumber, null, out codePoint);
            else
                int.TryParse(numPart.ToString(), out codePoint);
            if (codePoint > 0) output.Append(char.ConvertFromUtf32(codePoint));
            else output.Append(entity);
        }
        else
        {
            output.Append(entity);
        }

        return semi - pos + 1;
    }

    private static void MergeAdjacentSegments(List<List<ColoredSegment>> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Count <= 1) continue;

            var merged = new List<ColoredSegment>(line.Count);
            merged.Add(line[0]);

            for (int j = 1; j < line.Count; j++)
            {
                var prev = merged[^1];
                var cur = line[j];
                if (prev.Foreground == cur.Foreground && prev.Background == cur.Background
                    && prev.Bold == cur.Bold && prev.Italic == cur.Italic)
                    merged[^1] = new ColoredSegment(
                        prev.Text + cur.Text, prev.Foreground, prev.Background, prev.Bold, prev.Italic);
                else
                    merged.Add(cur);
            }

            lines[i] = merged;
        }
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
                ParseColorProps(props, out divFg, out divBg);
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
                        FlushText(textBuf, segments, fg, bg, bold, italic);
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
                        FlushText(textBuf, segments, fg, bg, bold, italic);
                        var props = line.AsSpan(pos + 5, end - pos - 5);
                        ParseColorProps(props, out var inlineFg, out var inlineBg);
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

                FlushText(textBuf, segments, fg, bg, bold, italic);
                if (starCount >= 3) { bold = !bold; italic = !italic; }
                else if (starCount == 2) bold = !bold;
                else italic = !italic;
                hadFormatting = true;
                continue;
            }

            textBuf.Append(line[pos]);
            pos++;
        }

        FlushText(textBuf, segments, fg, bg, bold, italic);

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

    private static void ParseColorProps(ReadOnlySpan<char> props, out RgbColor? fg, out RgbColor? bg)
    {
        fg = null;
        bg = null;
        int fgIdx = props.IndexOf("fg:".AsSpan());
        if (fgIdx >= 0)
        {
            var val = props[(fgIdx + 3)..];
            int end = val.IndexOf(' ');
            if (end >= 0) val = val[..end];
            fg = ParseCssColor(val);
        }
        int bgIdx = props.IndexOf("bg:".AsSpan());
        if (bgIdx >= 0)
        {
            var val = props[(bgIdx + 3)..];
            int end = val.IndexOf(' ');
            if (end >= 0) val = val[..end];
            bg = ParseCssColor(val);
        }
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
