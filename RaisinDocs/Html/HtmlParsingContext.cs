using System.Text;

namespace RaisinDocs;

/// <summary>
/// Shared helpers for clipboard HTML: entity decoding, CSS colour parsing, and the
/// colour-props and segment helpers the copy-out uses.
/// </summary>
internal static class HtmlParsingContext
{
    /// <summary>
    /// Decodes HTML entities like &amp;, &nbsp;, &lt;, &#123;, &#xAB;
    /// </summary>
    internal static int DecodeEntity(string html, int pos, StringBuilder output)
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

    /// <summary>
    /// Parses a CSS color value (hex or named color).
    /// </summary>
    internal static RgbColor? ParseCssColor(ReadOnlySpan<char> value)
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

    /// <summary>
    /// Flushes accumulated text into a line of colored segments.
    /// </summary>
    internal static void FlushText(StringBuilder buf, List<ColoredSegment> line,
        RgbColor? fg, RgbColor? bg, bool bold, bool italic)
    {
        if (buf.Length == 0) return;
        line.Add(new ColoredSegment(buf.ToString(), fg, bg, bold, italic));
        buf.Clear();
    }

    /// <summary>
    /// Parses color properties from a string like "fg:red bg:#FF0000".
    /// </summary>
    internal static void ParseColorProps(ReadOnlySpan<char> props, out RgbColor? fg, out RgbColor? bg)
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
}
