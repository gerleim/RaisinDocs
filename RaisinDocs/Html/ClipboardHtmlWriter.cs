using System.Text;

namespace RaisinDocs;

/// <summary>
/// Shared building blocks for producing CF_HTML clipboard payloads:
/// HTML escaping, inline markdown → HTML rendering, and the CF_HTML header.
///
/// Used by <see cref="TableClipboardHtml"/> (table copy-out) and by the legacy
/// HtmlToMarkdownConverter copy-out path.
/// </summary>
internal static class ClipboardHtmlWriter
{
    internal static string HtmlEncode(string text)
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

    /// <summary>
    /// Renders one line of RaisinDocs markdown (bold/italic stars plus color comment tags)
    /// as inline HTML. <paramref name="divFg"/>/<paramref name="divBg"/> supply the enclosing
    /// block color, if any.
    /// </summary>
    internal static (string html, bool hadFormatting) RenderInlineMarkdown(
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

    /// <summary>
    /// Wraps an HTML fragment in the CF_HTML header Windows requires, with byte offsets.
    /// </summary>
    internal static string WrapClipboardHeader(string htmlFragment)
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
