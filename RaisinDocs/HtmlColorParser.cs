using System.Text;

namespace RaisinDocs;

internal static class HtmlColorParser
{
    private readonly record struct ColoredSegment(string Text, RgbColor? Foreground, RgbColor? Background, bool Bold, bool Italic);

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
        int preStart = html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase);
        if (preStart < 0) return null;

        int preContentStart = html.IndexOf('>', preStart + 4);
        if (preContentStart < 0) return null;
        preContentStart++;

        int preEnd = html.IndexOf("</pre>", preContentStart, StringComparison.OrdinalIgnoreCase);
        if (preEnd < 0) preEnd = html.Length;

        var lines = new List<List<ColoredSegment>>();
        var currentLine = new List<ColoredSegment>();
        lines.Add(currentLine);

        bool hasAnyFormatting = false;
        int pos = preContentStart;
        var textBuf = new StringBuilder();

        while (pos < preEnd)
        {
            if (html[pos] == '<')
            {
                if (pos + 5 < preEnd && html.AsSpan(pos, 5).Equals("<span".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    FlushText(textBuf, currentLine, null, null, false, false);

                    int tagClose = html.IndexOf('>', pos + 5);
                    if (tagClose < 0) break;

                    var (fg, bg, bold, italic) = ParseStyleFromTag(html.AsSpan(pos, tagClose - pos + 1));
                    if (fg != null || bg != null || bold || italic) hasAnyFormatting = true;

                    pos = tagClose + 1;

                    int spanEnd = html.IndexOf("</span>", pos, StringComparison.OrdinalIgnoreCase);
                    if (spanEnd < 0) spanEnd = preEnd;

                    var spanText = new StringBuilder();
                    int spanPos = pos;
                    while (spanPos < spanEnd)
                    {
                        if (html[spanPos] == '&')
                        {
                            spanPos += DecodeEntity(html, spanPos, spanText);
                        }
                        else if (html[spanPos] == '\n')
                        {
                            FlushText(spanText, currentLine, fg, bg, bold, italic);
                            currentLine = new List<ColoredSegment>();
                            lines.Add(currentLine);
                            spanPos++;
                        }
                        else
                        {
                            spanText.Append(html[spanPos]);
                            spanPos++;
                        }
                    }
                    FlushText(spanText, currentLine, fg, bg, bold, italic);

                    pos = spanEnd + "</span>".Length;
                }
                else
                {
                    FlushText(textBuf, currentLine, null, null, false, false);
                    int tagEnd = html.IndexOf('>', pos + 1);
                    if (tagEnd < 0) break;
                    pos = tagEnd + 1;
                }
            }
            else if (html[pos] == '\n')
            {
                FlushText(textBuf, currentLine, null, null, false, false);
                currentLine = new List<ColoredSegment>();
                lines.Add(currentLine);
                pos++;
            }
            else if (html[pos] == '&')
            {
                pos += DecodeEntity(html, pos, textBuf);
            }
            else
            {
                textBuf.Append(html[pos]);
                pos++;
            }
        }

        FlushText(textBuf, currentLine, null, null, false, false);

        if (!hasAnyFormatting) return null;

        MergeAdjacentSegments(lines);

        return lines;
    }

    private static void FlushText(StringBuilder buf, List<ColoredSegment> line, RgbColor? fg, RgbColor? bg)
    {
        if (buf.Length == 0) return;
        line.Add(new ColoredSegment(buf.ToString(), fg, bg));
        buf.Clear();
    }

    private static (RgbColor? fg, RgbColor? bg) ParseStyleFromTag(ReadOnlySpan<char> tag)
    {
        int styleStart = tag.IndexOf("style=\"".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (styleStart < 0) return (null, null);
        styleStart += 7;

        int styleEnd = tag[styleStart..].IndexOf('"');
        if (styleEnd < 0) return (null, null);

        var style = tag[styleStart..(styleStart + styleEnd)];

        RgbColor? fg = null;
        RgbColor? bg = null;

        int bgIdx = style.IndexOf("background-color:".AsSpan(), StringComparison.OrdinalIgnoreCase);
        int fgIdx = style.IndexOf("color:".AsSpan(), StringComparison.OrdinalIgnoreCase);

        if (bgIdx >= 0)
        {
            bg = ParseCssColor(style[(bgIdx + 17)..]);
        }

        if (fgIdx >= 0)
        {
            bool isBgPrefix = fgIdx > 0 && style[fgIdx - 1] == '-';
            if (!isBgPrefix)
                fg = ParseCssColor(style[(fgIdx + 6)..]);
        }

        return (fg, bg);
    }

    private static RgbColor? ParseCssColor(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        if (value.Length == 0 || value[0] != '#') return null;

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
        else if (entity.Length > 3 && entity[1] == '#')
        {
            var numPart = entity[2..^1];
            int codePoint;
            if (numPart[0] == 'x' || numPart[0] == 'X')
                int.TryParse(numPart[1..].ToString(), System.Globalization.NumberStyles.HexNumber, null, out codePoint);
            else
                int.TryParse(numPart.ToString(), out codePoint);
            if (codePoint > 0) output.Append((char)codePoint);
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
                if (prev.Foreground == cur.Foreground && prev.Background == cur.Background)
                    merged[^1] = new ColoredSegment(prev.Text + cur.Text, prev.Foreground, prev.Background);
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
                        output.Add(GetPlainText(lines[k]));
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

    private static string FormatDivOpen(RgbColor? fg, RgbColor? bg)
    {
        var parts = new List<string>(2);
        if (fg != null) parts.Add($"fg:{fg.Value.ToHex()}");
        if (bg != null) parts.Add($"bg:{bg.Value.ToHex()}");
        return $"<!--@div {string.Join(" ", parts)}-->";
    }

    private static string FormatInlineLine(List<ColoredSegment> segments)
    {
        if (segments.Count == 0) return "";
        if (segments.Count == 1 && segments[0].Foreground == null && segments[0].Background == null)
            return segments[0].Text;

        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.Foreground == null && seg.Background == null)
            {
                sb.Append(seg.Text);
                continue;
            }

            bool hasFg = seg.Foreground != null;
            bool hasBg = seg.Background != null;

            sb.Append("<!--@");
            if (hasFg) sb.Append($"fg:{seg.Foreground!.Value.ToHex()}");
            if (hasFg && hasBg) sb.Append(' ');
            if (hasBg) sb.Append($"bg:{seg.Background!.Value.ToHex()}");
            sb.Append("-->");

            sb.Append(seg.Text);

            if (hasFg && hasBg) sb.Append("<!--/@-->");
            else if (hasFg) sb.Append("<!--/@fg-->");
            else sb.Append("<!--/@bg-->");
        }
        return sb.ToString();
    }

    private static string GetPlainText(List<ColoredSegment> segments)
    {
        if (segments.Count == 0) return "";
        if (segments.Count == 1) return segments[0].Text;
        var sb = new StringBuilder();
        foreach (var seg in segments)
            sb.Append(seg.Text);
        return sb.ToString();
    }
}
