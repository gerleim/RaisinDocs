using System.Text;

namespace RaisinDocs;

/// <summary>
/// Builds a CF_HTML clipboard payload containing a real &lt;table&gt; for a selected range
/// of markdown table rows.
///
/// Excel, Word and Google Sheets all prefer the HTML clipboard format over plain text, so
/// emitting a table here makes "copy table → paste into Excel" land in separate cells while
/// the plain-text slot keeps the original markdown pipe syntax for markdown-aware targets.
/// </summary>
internal static class TableClipboardHtml
{
    /// <summary>
    /// Builds the CF_HTML payload for blocks <paramref name="startBlock"/>..<paramref name="endBlock"/>,
    /// optionally restricted to the column range <paramref name="startCol"/>..<paramref name="endCol"/>
    /// (pass -1 for both to include every column).
    /// Returns null when the range is not a contiguous run of table rows.
    /// </summary>
    internal static string? TryBuild(
        IReadOnlyList<ParsedBlock> parsedBlocks,
        Func<int, string> getBlockText,
        int startBlock, int endBlock,
        int startCol = -1, int endCol = -1)
    {
        var fragment = TryBuildFragment(parsedBlocks, getBlockText, startBlock, endBlock, startCol, endCol);
        return fragment == null ? null : ClipboardHtmlWriter.WrapClipboardHeader(fragment);
    }

    /// <summary>
    /// Builds just the &lt;table&gt; fragment, without the CF_HTML header. Split out for testing.
    /// </summary>
    internal static string? TryBuildFragment(
        IReadOnlyList<ParsedBlock> parsedBlocks,
        Func<int, string> getBlockText,
        int startBlock, int endBlock,
        int startCol = -1, int endCol = -1)
    {
        if (parsedBlocks == null || getBlockText == null) return null;
        if (startBlock < 0 || endBlock >= parsedBlocks.Count || startBlock > endBlock) return null;

        // Every block in the range must belong to the table, otherwise this is an ordinary
        // text selection that merely overlaps a table.
        var rows = new List<(ParsedBlock Parsed, string Text)>();
        int maxCols = 0;
        for (int b = startBlock; b <= endBlock; b++)
        {
            var parsed = parsedBlocks[b];
            if (parsed.IsTableSeparator) continue;
            if (parsed.TableRow == null) return null;

            string text = getBlockText(b);
            foreach (var cell in parsed.TableRow.Cells)
            {
                if (cell.Start < 0 || cell.Start + cell.Length > text.Length)
                    return null; // parsed blocks are stale relative to the document
            }

            rows.Add((parsed, text));
            maxCols = Math.Max(maxCols, parsed.TableRow.Cells.Count);
        }

        if (rows.Count == 0 || maxCols == 0) return null;

        int firstCol = startCol < 0 ? 0 : startCol;
        int lastCol = endCol < 0 ? maxCols - 1 : Math.Min(endCol, maxCols - 1);
        if (firstCol > lastCol) return null;

        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse;\">");

        foreach (var (parsed, text) in rows)
        {
            bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;
            string tag = isHeader ? "th" : "td";
            var cells = parsed.TableRow!.Cells;
            var alignments = parsed.Table?.Alignments;

            sb.Append("<tr>");
            for (int c = firstCol; c <= lastCol; c++)
            {
                string content = "";
                if (c < cells.Count)
                {
                    var (s, e) = cells[c].TrimContent(text);
                    content = text[s..e];
                }

                sb.Append('<').Append(tag);
                AppendCellStyle(sb, content, isHeader,
                    alignments != null && c < alignments.Count ? alignments[c] : ColumnAlignment.Left,
                    parsed.BlockColor);
                sb.Append('>');

                if (content.Length > 0)
                {
                    var (html, _) = ClipboardHtmlWriter.RenderInlineMarkdown(
                        content, parsed.BlockColor?.Foreground, parsed.BlockColor?.Background);
                    sb.Append(html);
                }

                sb.Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static void AppendCellStyle(
        StringBuilder sb, string content, bool isHeader, ColumnAlignment alignment, BlockColor? blockColor)
    {
        var style = new StringBuilder();

        if (alignment == ColumnAlignment.Center) style.Append("text-align:center;");
        else if (alignment == ColumnAlignment.Right) style.Append("text-align:right;");
        else if (isHeader) style.Append("text-align:left;"); // th defaults to centered in HTML

        if (blockColor?.Background is { } bg)
            style.Append($"background-color:{bg.ToHex()};");

        // Excel reinterprets some cell text as a formula or a number and loses the original
        // characters. Pin those cells to text format; leave ordinary numbers alone so they
        // still paste as numbers.
        if (NeedsTextFormat(content))
            style.Append("mso-number-format:'\\@';");

        if (style.Length > 0)
            sb.Append(" style=\"").Append(style).Append('"');
    }

    private static bool NeedsTextFormat(string content)
    {
        if (content.Length == 0) return false;

        // Leading =, + and @ start a formula in Excel.
        char first = content[0];
        if (first is '=' or '+' or '@') return true;

        bool allDigits = true;
        foreach (char c in content)
        {
            if (!char.IsAsciiDigit(c)) { allDigits = false; break; }
        }

        // Leading zeros are stripped and >15 digits lose precision when treated as numbers.
        if (allDigits && (content.Length > 15 || (content.Length > 1 && first == '0')))
            return true;

        return false;
    }
}
