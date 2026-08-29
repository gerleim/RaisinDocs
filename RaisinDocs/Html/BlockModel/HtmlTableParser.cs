namespace RaisinDocs;

/// <summary>
/// Parses HTML tables from clipboard content into <see cref="TableBlockData"/>.
///
/// Written against what Excel actually puts on the clipboard, which differs from
/// textbook HTML in ways that break naive parsers:
/// <list type="bullet">
/// <item>The CF_HTML fragment markers sit *inside* the table element, so the extracted
///       fragment is a bare run of &lt;tr&gt; rows with no enclosing &lt;table&gt;.</item>
/// <item>There are no &lt;th&gt; cells at all — the header row is &lt;td&gt; carrying a
///       bold class, so the first row is taken as the header.</item>
/// <item>Attributes are unquoted (class=xl65, align=right) and tags wrap across lines.</item>
/// <item>Empty cells are &amp;nbsp;, and formatting lives in class rules, not inline styles.</item>
/// </list>
/// </summary>
internal static class HtmlTableParser
{
    /// <summary>
    /// Parses a &lt;table&gt; element at <paramref name="startPos"/>.
    /// </summary>
    internal static bool TryParseTable(
        string html, int startPos, HtmlStyleSheet styles, MarkdownOutputSettings settings,
        out BlockElement block, out int endPos)
    {
        block = null!;
        endPos = startPos;

        if (!IsOpenTag(html, startPos, "table")) return false;

        int contentStart = html.IndexOf('>', startPos);
        if (contentStart < 0) return false;
        contentStart++;

        int closeStart = FindMatchingClose(html, contentStart, "table");
        int contentEnd = closeStart >= 0 ? closeStart : html.Length;

        var table = ParseRows(html[contentStart..contentEnd], styles, settings);
        if (table == null) return false;

        block = new BlockElement { Kind = BlockKind.TableHeaderRow, TableData = table };
        endPos = closeStart >= 0
            ? html.IndexOf('>', closeStart) + 1
            : html.Length;
        if (endPos <= 0) endPos = html.Length;
        return true;
    }

    /// <summary>
    /// Parses a run of &lt;tr&gt; rows that are not wrapped in a &lt;table&gt; element.
    /// This is the shape Excel's CF_HTML fragment actually has.
    /// </summary>
    internal static bool TryParseOrphanRows(
        string html, int startPos, HtmlStyleSheet styles, MarkdownOutputSettings settings,
        out BlockElement block, out int endPos)
    {
        block = null!;
        endPos = startPos;

        if (!IsOpenTag(html, startPos, "tr")) return false;

        // Consume every consecutive row (and the whitespace/<col> noise between them).
        int scan = startPos;
        int lastRowEnd = startPos;
        while (scan < html.Length)
        {
            if (!IsOpenTag(html, scan, "tr")) break;

            int rowClose = FindMatchingClose(html, html.IndexOf('>', scan) + 1, "tr");
            if (rowClose < 0) { lastRowEnd = html.Length; break; }

            int afterRow = html.IndexOf('>', rowClose);
            lastRowEnd = afterRow < 0 ? html.Length : afterRow + 1;

            scan = SkipIgnorable(html, lastRowEnd);
        }

        var table = ParseRows(html[startPos..lastRowEnd], styles, settings);
        if (table == null) return false;

        block = new BlockElement { Kind = BlockKind.TableHeaderRow, TableData = table };
        endPos = lastRowEnd;
        return true;
    }

    /// <summary>Skips whitespace and void/structural tags that may appear between rows.</summary>
    private static int SkipIgnorable(string html, int pos)
    {
        while (pos < html.Length)
        {
            if (char.IsWhiteSpace(html[pos])) { pos++; continue; }

            if (html[pos] == '<'
                && (IsOpenTag(html, pos, "col") || IsOpenTag(html, pos, "colgroup")
                    || IsCloseTag(html, pos, "colgroup") || IsCloseTag(html, pos, "thead")
                    || IsOpenTag(html, pos, "thead") || IsOpenTag(html, pos, "tbody")
                    || IsCloseTag(html, pos, "tbody") || IsOpenTag(html, pos, "tfoot")
                    || IsCloseTag(html, pos, "tfoot")
                    || html.AsSpan(pos).StartsWith("<!--", StringComparison.Ordinal)))
            {
                int close = html.IndexOf('>', pos);
                if (close < 0) return html.Length;
                pos = close + 1;
                continue;
            }

            break;
        }
        return pos;
    }

    private static TableBlockData? ParseRows(
        string content, HtmlStyleSheet styles, MarkdownOutputSettings settings)
    {
        var table = new TableBlockData();
        int pos = 0;
        bool sawHeaderCell = false;

        while (pos < content.Length)
        {
            int rowStart = IndexOfOpenTag(content, pos, "tr");
            if (rowStart < 0) break;

            int rowContentStart = content.IndexOf('>', rowStart);
            if (rowContentStart < 0) break;
            rowContentStart++;

            int rowClose = FindMatchingClose(content, rowContentStart, "tr");
            int rowContentEnd = rowClose >= 0 ? rowClose : content.Length;

            var row = ParseCells(content[rowContentStart..rowContentEnd], styles, settings, out bool rowHasTh);
            if (row.Cells.Count > 0)
            {
                row.IsHeader = rowHasTh;
                sawHeaderCell |= rowHasTh;
                table.Rows.Add(row);
            }

            pos = rowClose >= 0 ? rowContentEnd + 1 : content.Length;
        }

        if (table.Rows.Count == 0) return null;

        // Markdown tables require a header row. Excel never emits <th>, so when the source
        // states no header the first row becomes one.
        if (!sawHeaderCell)
            table.Rows[0].IsHeader = true;

        // A markdown header row already renders bold, so carrying Excel's bold header class
        // through would only add redundant ** markers to every heading.
        foreach (var row in table.Rows)
        {
            if (!row.IsHeader) continue;
            foreach (var cell in row.Cells)
                foreach (var segment in cell.Content)
                    segment.Format.Bold = false;
        }

        PadRowsToWidth(table);
        table.Alignments = InferAlignments(table);
        return table;
    }

    private static TableRowContent ParseCells(
        string rowContent, HtmlStyleSheet styles, MarkdownOutputSettings settings, out bool hasHeaderCell)
    {
        var row = new TableRowContent();
        hasHeaderCell = false;
        int pos = 0;

        while (pos < rowContent.Length)
        {
            int tdStart = IndexOfOpenTag(rowContent, pos, "td");
            int thStart = IndexOfOpenTag(rowContent, pos, "th");

            bool isHeaderCell = thStart >= 0 && (tdStart < 0 || thStart < tdStart);
            int cellStart = isHeaderCell ? thStart : tdStart;
            if (cellStart < 0) break;

            string name = isHeaderCell ? "th" : "td";
            int tagEnd = rowContent.IndexOf('>', cellStart);
            if (tagEnd < 0) break;

            string tag = rowContent[cellStart..(tagEnd + 1)];
            int cellClose = FindMatchingClose(rowContent, tagEnd + 1, name);
            int cellEnd = cellClose >= 0 ? cellClose : rowContent.Length;

            row.Cells.Add(BuildCell(tag, rowContent[(tagEnd + 1)..cellEnd], styles, settings));
            hasHeaderCell |= isHeaderCell;

            // A merged cell occupies several columns; fill the rest so columns stay aligned.
            for (int i = 1; i < GetSpan(tag); i++)
                row.Cells.Add(new TableCellContent { IsFiller = true });

            pos = cellClose >= 0 ? cellEnd + 1 : rowContent.Length;
        }

        return row;
    }

    private static TableCellContent BuildCell(
        string tag, string innerHtml, HtmlStyleSheet styles, MarkdownOutputSettings settings)
    {
        var content = HtmlBlockModelParser.ParseInlineContent(innerHtml, BlockKind.TableDataRow, settings);

        // Excel keeps cell formatting in class rules rather than inline markup, so fold the
        // resolved cell format into any segment that has none of its own.
        var cellFormat = styles.ResolveFormat(tag);
        if (!cellFormat.IsEmpty)
        {
            foreach (var segment in content)
            {
                segment.Format.ForegroundColor ??= cellFormat.ForegroundColor;
                segment.Format.BackgroundColor ??= cellFormat.BackgroundColor;
                segment.Format.Bold |= cellFormat.Bold;
                segment.Format.Italic |= cellFormat.Italic;
            }
        }

        return new TableCellContent { Content = content, Align = GetAlignment(tag, styles) };
    }

    private static ColumnAlignment? GetAlignment(string tag, HtmlStyleSheet styles)
    {
        string align = HtmlStyleSheet.GetAttributeValue(tag, "align");
        if (align.Length == 0)
            align = ExtractTextAlign(tag, styles);

        if (align.Equals("right", StringComparison.OrdinalIgnoreCase)) return ColumnAlignment.Right;
        if (align.Equals("center", StringComparison.OrdinalIgnoreCase)) return ColumnAlignment.Center;
        if (align.Equals("left", StringComparison.OrdinalIgnoreCase)) return ColumnAlignment.Left;
        return null;
    }

    private static string ExtractTextAlign(string tag, HtmlStyleSheet styles)
    {
        foreach (var source in new[]
                 {
                     HtmlStyleSheet.GetAttributeValue(tag, "style"),
                     ClassDeclarations(tag, styles),
                 })
        {
            int idx = source.IndexOf("text-align:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            int valueStart = idx + "text-align:".Length;
            int valueEnd = source.IndexOf(';', valueStart);
            if (valueEnd < 0) valueEnd = source.Length;

            string value = source[valueStart..valueEnd].Trim();
            // Excel's default; carries no more meaning than "unspecified".
            if (!value.Equals("general", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return "";
    }

    private static string ClassDeclarations(string tag, HtmlStyleSheet styles)
    {
        var combined = new System.Text.StringBuilder();
        foreach (var className in HtmlStyleSheet.GetAttributeValue(tag, "class")
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var decls = styles.GetDeclarations(className);
            if (decls != null) combined.Append(decls).Append(';');
        }
        return combined.ToString();
    }

    private static int GetSpan(string tag)
    {
        string span = HtmlStyleSheet.GetAttributeValue(tag, "colspan");
        return int.TryParse(span, out int n) && n > 1 && n <= 1000 ? n : 1;
    }

    private static void PadRowsToWidth(TableBlockData table)
    {
        int width = table.ColumnCount;
        foreach (var row in table.Rows)
        {
            while (row.Cells.Count < width)
                row.Cells.Add(new TableCellContent { IsFiller = true });
        }
    }

    /// <summary>
    /// Derives a column alignment from the data rows. Header cells are excluded because
    /// Excel left-aligns headers regardless of the column's own alignment. A column is only
    /// aligned when every cell that states an alignment agrees.
    /// </summary>
    private static List<ColumnAlignment> InferAlignments(TableBlockData table)
    {
        int width = table.ColumnCount;
        var result = new List<ColumnAlignment>(width);

        for (int col = 0; col < width; col++)
        {
            ColumnAlignment? agreed = null;
            bool conflict = false;

            foreach (var row in table.Rows)
            {
                if (row.IsHeader || col >= row.Cells.Count) continue;

                var cell = row.Cells[col];
                if (cell.IsFiller || cell.Align == null || cell.Content.Count == 0) continue;

                if (agreed == null) agreed = cell.Align;
                else if (agreed != cell.Align) { conflict = true; break; }
            }

            result.Add(conflict || agreed == null ? ColumnAlignment.Left : agreed.Value);
        }

        return result;
    }

    // --- Tag scanning helpers (tolerant of unquoted attributes and multi-line tags) ---

    private static bool IsOpenTag(string html, int pos, string name)
    {
        if (pos >= html.Length || html[pos] != '<') return false;
        int after = pos + 1;
        if (after + name.Length > html.Length) return false;
        if (!html.AsSpan(after, name.Length).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        int next = after + name.Length;
        return next >= html.Length || html[next] == '>' || html[next] == '/' || char.IsWhiteSpace(html[next]);
    }

    private static bool IsCloseTag(string html, int pos, string name)
    {
        if (pos + 1 >= html.Length || html[pos] != '<' || html[pos + 1] != '/') return false;
        int after = pos + 2;
        if (after + name.Length > html.Length) return false;
        if (!html.AsSpan(after, name.Length).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        int next = after + name.Length;
        return next >= html.Length || html[next] == '>' || char.IsWhiteSpace(html[next]);
    }

    private static int IndexOfOpenTag(string html, int start, string name)
    {
        for (int i = start; i < html.Length; i++)
        {
            if (html[i] == '<' && IsOpenTag(html, i, name)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the closing tag for an element already opened, honoring nesting.
    /// Returns the index of the '&lt;' of the closing tag, or -1 when unclosed.
    /// </summary>
    private static int FindMatchingClose(string html, int start, string name)
    {
        int depth = 1;
        for (int i = start; i < html.Length; i++)
        {
            if (html[i] != '<') continue;

            if (IsCloseTag(html, i, name))
            {
                if (--depth == 0) return i;
            }
            else if (IsOpenTag(html, i, name))
            {
                // A self-closed or void occurrence never nests.
                int close = html.IndexOf('>', i);
                if (close > i && html[close - 1] != '/') depth++;
            }
        }
        return -1;
    }
}
