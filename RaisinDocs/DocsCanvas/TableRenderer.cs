using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles table rendering and measurement for DocsCanvas in visual mode.
    /// Encapsulates all logic for drawing table backgrounds, borders, cells, and hit-testing.
    /// </summary>
    internal class TableRenderer
    {
        private readonly IDocsCanvasServices _services;

    public TableRenderer(IDocsCanvasServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Computes and caches the column widths for all tables in the document.
    /// Column widths are computed based on the widest cell content in each column.
    /// </summary>
    public void ComputeAllTableColumnWidths(double maxWidth)
    {
        var seen = new HashSet<TableInfo>();
        for (int bi = 0; bi < ((DocsCanvas)_services)._doc.BlockCount; bi++)
        {
            var parsed = ((DocsCanvas)_services)._parsedBlocks![bi];
            if (parsed.Table == null || parsed.TableRow == null) continue;
            if (!seen.Add(parsed.Table)) continue;

            int colCount = parsed.Table.ColumnCount;
            var widths = new double[colCount];

            for (int bj = bi; bj < ((DocsCanvas)_services)._doc.BlockCount; bj++)
            {
                var p = ((DocsCanvas)_services)._parsedBlocks[bj];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator || p.TableRow == null) continue;

                string text = ((DocsCanvas)_services)._doc.GetBlockText(bj);
                BlockVisualMap? map = (((DocsCanvas)_services)._visualMaps != null && bj < ((DocsCanvas)_services)._visualMaps.Count) ? ((DocsCanvas)_services)._visualMaps[bj] : null;
                for (int c = 0; c < Math.Min(p.TableRow.Cells.Count, colCount); c++)
                {
                    var cell = p.TableRow.Cells[c];
                    int s = cell.Start;
                    int e = s + cell.Length;
                    while (s < e && text[s] == ' ') s++;
                    while (e > s && text[e - 1] == ' ') e--;
                    string cellText = map != null
                        ? map.BuildDisplayString(text, s, e - s)
                        : text.Substring(s, e - s);
                    double w = ((DocsCanvas)_services)._measure.MeasureStringWidth(cellText, p.Kind, p.Runs, s);
                    if (w > widths[c]) widths[c] = w;
                }
            }

            for (int c = 0; c < colCount; c++)
                widths[c] += DocsCanvas._tableCellPadding * 2;

            ((DocsCanvas)_services)._tableColumnWidths[parsed.Table] = widths;
        }
    }

    /// <summary>
    /// Draws table backgrounds, borders, and column separators for visible tables.
    /// This is called before cell content is drawn.
    /// </summary>
    public void DrawTableBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        int i = 0;
        while (i < ((DocsCanvas)_services)._visualLines.Count)
        {
            var vl = ((DocsCanvas)_services)._visualLines[i];
            // Safety check: skip if block index is out of range (can happen after merging)
            if (((DocsCanvas)_services)._parsedBlocks == null || vl.BlockIndex >= ((DocsCanvas)_services)._parsedBlocks.Count)
            {
                i++;
                continue;
            }
            var parsed = ((DocsCanvas)_services)._parsedBlocks[vl.BlockIndex];
            if (parsed.Table == null || parsed.Kind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
            {
                i++;
                continue;
            }

            var tableInfo = parsed.Table;
            int tableStart = i;
            int tableEnd = i;
            while (tableEnd < ((DocsCanvas)_services)._visualLines.Count)
            {
                var p = ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._visualLines[tableEnd].BlockIndex];
                if (p.Table != tableInfo) break;
                tableEnd++;
            }

            double tableY = ((DocsCanvas)_services)._lineYPositions[tableStart];
            double tableBottom = tableEnd > 0
                ? ((DocsCanvas)_services)._lineYPositions[tableEnd - 1] + ((DocsCanvas)_services).GetEffectiveLineHeight(((DocsCanvas)_services)._visualLines[tableEnd - 1])
                : tableY;

            if (tableBottom >= viewTop && tableY <= viewBottom
                && ((DocsCanvas)_services)._tableColumnWidths.TryGetValue(tableInfo, out var colWidths))
            {
                double tableWidth = 0;
                foreach (var w in colWidths) tableWidth += w;
                double tableX = DocsCanvas._padding;
                double yTop = tableY - effectiveScroll;
                double tableH = tableBottom - tableY;

                dc.DrawRectangle(((DocsCanvas)_services)._palette.TableBackground, null,
                    new Rect(tableX, yTop, tableWidth, tableH));

                double headerH = ((DocsCanvas)_services)._measure.GetLineHeight(((DocsCanvas)_services)._visualLines[tableStart].BlockKind);
                dc.DrawRectangle(((DocsCanvas)_services)._palette.TableHeaderBackground, null,
                    new Rect(tableX, yTop, tableWidth, headerH));

                dc.DrawRectangle(null, ((DocsCanvas)_services)._palette.TableBorderPen,
                    new Rect(tableX, yTop, tableWidth, tableH));

                for (int row = tableStart; row < tableEnd; row++)
                {
                    double rowY = ((DocsCanvas)_services)._lineYPositions[row] - effectiveScroll;
                    if (row > tableStart)
                        dc.DrawLine(((DocsCanvas)_services)._palette.TableBorderPen,
                            new Point(tableX, rowY), new Point(tableX + tableWidth, rowY));
                }

                double cx = tableX;
                for (int c = 0; c < colWidths.Length - 1; c++)
                {
                    cx += colWidths[c];
                    dc.DrawLine(((DocsCanvas)_services)._palette.TableBorderPen,
                        new Point(cx, yTop), new Point(cx, yTop + tableH));
                }
            }

            i = tableEnd;
        }
    }

    /// <summary>
    /// Draws the content of a table row, including cell text with proper alignment and styling.
    /// </summary>
    public void DrawTableRow(DrawingContext dc, VisualLine vl, string blockText,
        ParsedBlock parsed, double lineY, double effectiveScroll,
        double fontSize, Typeface baseTypeface)
    {
        if (parsed.TableRow == null || parsed.Table == null) return;
        if (!((DocsCanvas)_services)._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths)) return;

        BlockVisualMap? map = null;
        if (((DocsCanvas)_services)._visualMaps != null && vl.BlockIndex < ((DocsCanvas)_services)._visualMaps.Count)
            map = ((DocsCanvas)_services)._visualMaps[vl.BlockIndex];

        double x = DocsCanvas._padding;
        double y = lineY - effectiveScroll;
        double lineH = ((DocsCanvas)_services)._measure.GetLineHeight(vl.BlockKind);
        bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;

        for (int c = 0; c < Math.Min(parsed.TableRow.Cells.Count, colWidths.Length); c++)
        {
            var cell = parsed.TableRow.Cells[c];
            var (s, e) = cell.TrimContent(blockText);

            string cellText = map != null
                ? map.BuildDisplayString(blockText, s, e - s)
                : blockText.Substring(s, e - s);
            if (cellText.Length == 0) { x += colWidths[c]; continue; }

            var cellTypeface = isHeader ? TextMeasurer.BoldTypeface : baseTypeface;
            var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, cellTypeface, fontSize,
                ((DocsCanvas)_services)._palette.Foreground, ((DocsCanvas)_services)._measure.DpiScale);

            if (map != null)
                ApplyInlineStylesForCell(ft, parsed, map, s, e);
            else
                ApplyInlineStylesForCellRaw(ft, cellText, parsed, s, e);

            var align = parsed.Table.Alignments[c];
            double cellContentWidth = colWidths[c] - DocsCanvas._tableCellPadding * 2;
            double textX;
            if (align == ColumnAlignment.Center)
                textX = x + DocsCanvas._tableCellPadding + Math.Max(0, (cellContentWidth - ft.Width) / 2);
            else if (align == ColumnAlignment.Right)
                textX = x + DocsCanvas._tableCellPadding + Math.Max(0, cellContentWidth - ft.Width);
            else
                textX = x + DocsCanvas._tableCellPadding;

            var clipRect = new Rect(x, y, colWidths[c], lineH);
            dc.PushClip(new RectangleGeometry(clipRect));

            if (map?.ColorSpans != null)
            {
                foreach (var cs in map.ColorSpans)
                {
                    if (cs.Background == null) continue;
                    int csEnd = cs.Start + cs.Length;
                    if (csEnd <= s || cs.Start >= e) continue;

                    int rawStart = Math.Max(cs.Start, s);
                    int rawEnd = Math.Min(csEnd, e);
                    double bgX1 = ((DocsCanvas)_services).MeasureRangeWidth(blockText, s, rawStart - s, parsed.Runs, parsed.Kind, map);
                    double bgX2 = ((DocsCanvas)_services).MeasureRangeWidth(blockText, s, rawEnd - s, parsed.Runs, parsed.Kind, map);
                    if (bgX2 <= bgX1) continue;

                    var bg = cs.Background.Value;
                    var brush = new SolidColorBrush(Color.FromArgb(40, bg.R, bg.G, bg.B));
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, new Rect(textX + bgX1, y, bgX2 - bgX1, lineH));
                }
            }

            dc.DrawText(ft, new Point(textX, y));
            dc.Pop();

            x += colWidths[c];
        }
    }

    /// <summary>
    /// Calculates the X position of the cursor within a table row for rendering.
    /// Accounts for cell alignment and visual styles.
    /// </summary>
    internal double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = ((DocsCanvas)_services)._doc.GetBlockText(blockIndex);
        BlockVisualMap? map = (((DocsCanvas)_services)._visualMaps != null && blockIndex < ((DocsCanvas)_services)._visualMaps.Count) ? ((DocsCanvas)_services)._visualMaps[blockIndex] : null;

        double x = 0;
        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            var cell = cells[c];
            int cellEnd = cell.Start + cell.Length;
            if (cursorOffset >= cell.Start && cursorOffset <= cellEnd)
            {
                var (trimStart, trimEnd) = cell.TrimContent(blockText);

                string cellText = map != null
                    ? map.BuildDisplayString(blockText, trimStart, trimEnd - trimStart)
                    : blockText.Substring(trimStart, trimEnd - trimStart);

                int visualOffset;
                if (map != null)
                {
                    int visBase = map.RawToVisual(trimStart);
                    visualOffset = Math.Clamp(map.RawToVisual(cursorOffset) - visBase, 0, cellText.Length);
                }
                else
                {
                    visualOffset = Math.Clamp(cursorOffset - trimStart, 0, cellText.Length);
                }

                bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;
                double fontSize = ((DocsCanvas)_services)._measure.GetBlockFontSize(parsed.Kind);
                var cellTypeface = isHeader ? TextMeasurer.BoldTypeface : TextMeasurer.GetBlockBaseTypeface(parsed.Kind);

                var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, cellTypeface, fontSize,
                    ((DocsCanvas)_services)._palette.Foreground, ((DocsCanvas)_services)._measure.DpiScale);

                if (map != null)
                    ApplyInlineStylesForCell(ft, parsed, map, trimStart, trimEnd);
                else
                    ApplyInlineStylesForCellRaw(ft, cellText, parsed, trimStart, trimEnd);

                double textW = 0;
                if (visualOffset > 0)
                {
                    var geom = ft.BuildHighlightGeometry(new Point(0, 0), 0, visualOffset);
                    textW = geom != null ? geom.Bounds.Right : ft.WidthIncludingTrailingWhitespace;
                }

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - DocsCanvas._tableCellPadding * 2;
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - ft.Width) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - ft.Width),
                    _ => 0,
                };
                return x + DocsCanvas._tableCellPadding + alignOffset + textW;
            }
            x += colWidths[c];
        }
        return x;
    }

    /// <summary>
    /// Performs hit testing on a table row to find the character offset at a given X position.
    /// </summary>
    internal int HitTestInTableRow(VisualLine vl, ParsedBlock parsed, double[] colWidths, double x)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);
        BlockVisualMap? map = (((DocsCanvas)_services)._visualMaps != null && vl.BlockIndex < ((DocsCanvas)_services)._visualMaps.Count) ? ((DocsCanvas)_services)._visualMaps[vl.BlockIndex] : null;
        double cx = 0;

        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            if (x < cx + colWidths[c] || c == cells.Count - 1 || c == colWidths.Length - 1)
            {
                var cell = cells[c];
                var (trimStart, trimEnd) = cell.TrimContent(blockText);

                double fullTextW;
                if (map != null)
                {
                    fullTextW = 0;
                    int ri = 0;
                    for (int rawI = trimStart; rawI < trimEnd; rawI++)
                    {
                        if (map.IsHidden(rawI)) continue;
                        var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawI, ref ri);
                        fullTextW += ((DocsCanvas)_services)._measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
                    }
                }
                else
                {
                    string cellContent = blockText.Substring(trimStart, trimEnd - trimStart);
                    fullTextW = ((DocsCanvas)_services)._measure.MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, trimStart);
                }

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - DocsCanvas._tableCellPadding * 2;
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
                    _ => 0,
                };

                double localX = x - cx - DocsCanvas._tableCellPadding - alignOffset;
                double accum = 0;
                int runIdx = 0;

                if (map != null)
                {
                    for (int rawI = trimStart; rawI < trimEnd; rawI++)
                    {
                        if (map.IsHidden(rawI)) continue;
                        var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawI, ref runIdx);
                        double charW = ((DocsCanvas)_services)._measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
                        if (localX < accum + charW / 2)
                            return rawI;
                        accum += charW;
                    }
                    return trimEnd;
                }
                else
                {
                    string cellContent = blockText.Substring(trimStart, trimEnd - trimStart);
                    for (int i = 0; i < cellContent.Length; i++)
                    {
                        var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, trimStart + i, ref runIdx);
                        double charW = ((DocsCanvas)_services)._measure.MeasureCharWidth(cellContent[i], parsed.Kind, style);
                        if (localX < accum + charW / 2)
                            return trimStart + i;
                        accum += charW;
                    }
                    return trimEnd;
                }
            }
            cx += colWidths[c];
        }
        return vl.StartOffset + vl.Length;
    }

    /// <summary>
    /// Applies inline styles (bold, italic, code, etc.) to cell text with visual map support.
    /// Used in visual mode to handle hidden ranges.
    /// </summary>
    private void ApplyInlineStylesForCell(FormattedText ft, ParsedBlock parsed,
        BlockVisualMap map, int cellStart, int cellEnd)
    {
        int visBase = map.RawToVisual(cellStart);
        int ftLen = ft.Text.Length;

        foreach (var run in parsed.Runs)
        {
            if (run.Style is InlineStyle.Normal or InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= cellStart || run.Start >= cellEnd) continue;

            int rawStart = Math.Max(run.Start, cellStart);
            int rawEnd = Math.Min(runEnd, cellEnd);
            int visStart = map.RawToVisual(rawStart) - visBase;
            int visEnd = map.RawToVisual(rawEnd) - visBase;
            int count = Math.Min(visEnd - visStart, ftLen - visStart);
            if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

            switch (run.Style)
            {
                case InlineStyle.Bold or InlineStyle.BoldItalic:
                    ft.SetFontWeight(FontWeights.Bold, visStart, count);
                    break;
            }
            if (run.Style is InlineStyle.Italic or InlineStyle.BoldItalic)
                ft.SetFontStyle(FontStyles.Italic, visStart, count);
            if (run.Style == InlineStyle.Code)
                ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visStart, count);
            if (run.Style == InlineStyle.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough, visStart, count);
            if (run.Style == InlineStyle.Link)
            {
                ft.SetForegroundBrush(DocsCanvas._checkboxCheckedBrush, visStart, count);
                ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
            }
        }

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            if (ftLen > 0) ft.SetForegroundBrush(((DocsCanvas)_services).GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, ftLen);
        }

        if (map.ColorSpans != null)
        {
            foreach (var cs in map.ColorSpans)
            {
                int csEnd = cs.Start + cs.Length;
                if (csEnd <= cellStart || cs.Start >= cellEnd) continue;

                int rawStart = Math.Max(cs.Start, cellStart);
                int rawEnd = Math.Min(csEnd, cellEnd);
                int visStart = map.RawToVisual(rawStart) - visBase;
                int visEnd = map.RawToVisual(rawEnd) - visBase;
                visEnd = Math.Min(visEnd, ftLen);
                int count = visEnd - visStart;
                if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

                if (cs.Foreground is { } fg)
                {
                    ft.SetForegroundBrush(((DocsCanvas)_services).GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
                }
            }
        }
    }

    /// <summary>
    /// Applies inline styles to cell text without visual map (source mode or raw text).
    /// </summary>
    private static void ApplyInlineStylesForCellRaw(FormattedText ft, string cellText,
        ParsedBlock parsed, int cellStart, int cellEnd)
    {
        foreach (var run in parsed.Runs)
        {
            if (run.Style is InlineStyle.Normal or InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= cellStart || run.Start >= cellEnd) continue;

            int overlapStart = Math.Max(run.Start, cellStart) - cellStart;
            int overlapEnd = Math.Min(runEnd, cellEnd) - cellStart;
            int len = Math.Min(overlapEnd - overlapStart, cellText.Length - overlapStart);
            if (len <= 0 || overlapStart >= cellText.Length) continue;

            switch (run.Style)
            {
                case InlineStyle.Bold or InlineStyle.BoldItalic:
                    ft.SetFontWeight(FontWeights.Bold, overlapStart, len);
                    break;
            }
            if (run.Style is InlineStyle.Italic or InlineStyle.BoldItalic)
                ft.SetFontStyle(FontStyles.Italic, overlapStart, len);
            if (run.Style == InlineStyle.Code)
                ft.SetFontFamily(new FontFamily("Cascadia Mono,Consolas"), overlapStart, len);
            if (run.Style == InlineStyle.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough, overlapStart, len);
        }
    }
    }
}
