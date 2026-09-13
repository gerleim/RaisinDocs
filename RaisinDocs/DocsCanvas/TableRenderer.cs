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
        private readonly ITableServices _table;
        private readonly IRenderingServices _rendering;
        private readonly IDocumentServices _doc;
        private readonly IParsedContentServices _content;
        private readonly ILayoutDataServices _layout;

        public TableRenderer(ITableServices table, IRenderingServices rendering, IDocumentServices doc, IParsedContentServices content, ILayoutDataServices layout)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        /// <summary>Tables whose columns had to shrink below their natural width to fit.</summary>
        /// <remarks>
        /// Only their rows get a <see cref="TableRowLayout"/>. A table that fits is laid out exactly
        /// as it was before cells could wrap - no wrap pass, no extra height - which keeps the
        /// common case off the new path's cost. Rebuilt on every width pass, print's included.
        /// </remarks>
        private readonly HashSet<TableInfo> _shrunkTables = new();

        public bool IsShrunk(TableInfo table) => _shrunkTables.Contains(table);

        /// <summary>
        /// Computes and caches the column widths for all tables in the document, shrinking the
        /// columns of any table wider than <paramref name="maxWidth"/> so it fits.
        /// </summary>
        /// <remarks>
        /// The browser's rule, in three tiers. A column's natural width is its widest cell and its
        /// minimum its longest word, both with the cell padding included.
        /// <list type="number">
        /// <item>Natural widths fit: use them.</item>
        /// <item>Minimums fit: every column keeps its longest word, and the shortfall comes out of
        /// each in proportion to how far it is above its minimum.</item>
        /// <item>Not even the minimums fit: the same blend between a floor of about three
        /// characters and the minimum, so long words break mid-word. At the floor the table still
        /// overflows and is clipped, as every wide table used to be.</item>
        /// </list>
        /// Widths are measured as advance sums over the visible characters - the same sums, in the
        /// same order, that FitLine wraps by - so a cell at its natural width is never wrapped by
        /// the pass that follows. Header rows measure bold through their block kind.
        /// </remarks>
        public void ComputeAllTableColumnWidths(double maxWidth)
        {
            _shrunkTables.Clear();
            var seen = new HashSet<TableInfo>();
            for (int bi = 0; bi < _doc.BlockCount; bi++)
            {
                var parsed = _content.ParsedBlocks![bi];
                if (parsed.Table == null || parsed.TableRow == null) continue;
                if (!seen.Add(parsed.Table)) continue;

                int colCount = parsed.Table.ColumnCount;
                var natural = new double[colCount];
                var minimum = new double[colCount];

                for (int bj = bi; bj < _doc.BlockCount; bj++)
                {
                    var p = _content.ParsedBlocks[bj];
                    if (p.Table != parsed.Table) break;
                    if (p.IsTableSeparator || p.TableRow == null) continue;

                    string text = _doc.GetBlockText(bj);
                    BlockVisualMap? map = (_content.VisualMaps != null && bj < _content.VisualMaps.Count) ? _content.VisualMaps[bj] : null;
                    for (int c = 0; c < Math.Min(p.TableRow.Cells.Count, colCount); c++)
                    {
                        var (s, e) = p.TableRow.Cells[c].TrimContent(text);
                        var (nat, longestWord) = MeasureCell(text, s, e, p, map);
                        if (nat > natural[c]) natural[c] = nat;
                        if (longestWord > minimum[c]) minimum[c] = longestWord;
                    }
                }

                double pad = DocsCanvas._tableCellPadding * 2;
                for (int c = 0; c < colCount; c++)
                {
                    natural[c] += pad;
                    minimum[c] += pad;
                }

                var widths = FitColumns(natural, minimum, maxWidth,
                    3 * _rendering.Measure.MeasureCharWidth('0', BlockKind.TableHeaderRow, InlineStyle.Normal) + pad);
                if (!ReferenceEquals(widths, natural))
                    _shrunkTables.Add(parsed.Table);

                _table.TableColumnWidths[parsed.Table] = widths;
            }
        }

        /// <summary>
        /// A cell's width and its longest word's, over the visible characters of [s, e). Words end
        /// at a visible space, the only place FitLine breaks a line.
        /// </summary>
        private (double Width, double LongestWord) MeasureCell(string text, int s, int e,
            ParsedBlock parsed, BlockVisualMap? map)
        {
            double width = _rendering.MeasureRangeWidth(text, s, e - s, parsed.Runs, parsed.Kind, map);

            double longest = 0;
            int wordStart = s;
            for (int i = s; i <= e; i++)
            {
                bool boundary = i == e || (text[i] == ' ' && (map == null || !map.IsHidden(i)));
                if (!boundary) continue;
                if (i > wordStart)
                {
                    double w = _rendering.MeasureRangeWidth(text, wordStart, i - wordStart, parsed.Runs, parsed.Kind, map);
                    if (w > longest) longest = w;
                }
                wordStart = i + 1;
            }
            return (width, longest);
        }

        /// <summary>
        /// The three tiers of <see cref="ComputeAllTableColumnWidths"/>. Returns
        /// <paramref name="natural"/> itself when the table fits, so the caller can tell.
        /// </summary>
        internal static double[] FitColumns(double[] natural, double[] minimum, double available, double floorWidth)
        {
            double sumNat = natural.Sum();
            if (available <= 0 || sumNat <= available) return natural;

            int n = natural.Length;
            var widths = new double[n];
            double sumMin = minimum.Sum();
            if (sumMin <= available)
            {
                double share = sumNat > sumMin ? (available - sumMin) / (sumNat - sumMin) : 0;
                for (int c = 0; c < n; c++)
                    widths[c] = minimum[c] + (natural[c] - minimum[c]) * share;
                return widths;
            }

            var floor = new double[n];
            for (int c = 0; c < n; c++) floor[c] = Math.Min(minimum[c], floorWidth);
            double sumFloor = floor.Sum();
            if (sumFloor >= available) return floor;

            double blend = (available - sumFloor) / (sumMin - sumFloor);
            for (int c = 0; c < n; c++)
                widths[c] = floor[c] + (minimum[c] - floor[c]) * blend;
            return widths;
        }

        /// <summary>
        /// Tints one row: its slice of the table background, and the header shade on row one.
        /// </summary>
        /// <remarks>
        /// Drawn into the row's own line visual rather than under the whole table, because an
        /// opaque line visual covers anything painted beneath it. Only the fills need to be
        /// behind the text; the borders do not touch a glyph and stay whole-table geometry in
        /// <see cref="DrawTableLines"/>.
        ///
        /// A table's visual lines are exactly its header and data rows - the separator row is
        /// IsSkippedInVisual and never gets one - so every line with a Table is one this
        /// paints, which is the same set the whole-table rect used to cover.
        /// </remarks>
        public void DrawTableRowBackground(DrawingContext dc, ParsedBlock parsed,
            double y, double bgH)
        {
            if (parsed.Table == null) return;
            if (!_table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths)) return;

            double tableWidth = 0;
            foreach (var w in colWidths) tableWidth += w;

            dc.DrawRectangle(_rendering.Palette.TableBackground, null,
                new Rect(DocsCanvas._padding, y, tableWidth, bgH));

            if (parsed.Kind == BlockKind.TableHeaderRow)
                dc.DrawRectangle(_rendering.Palette.TableHeaderBackground, null,
                    new Rect(DocsCanvas._padding, y, tableWidth, bgH));
        }

        /// <summary>
        /// Draws every visible table's border, row separators and column separators.
        /// </summary>
        /// <remarks>
        /// Runs from the overlay, above the line visuals, which is why it can keep whole-table
        /// geometry: a column separator crosses every row, and decomposing it per row would be
        /// all of the seam risk and none of the benefit. The lines never cross a glyph - a
        /// column is the widest cell plus twice the 8 DIP cell padding - so drawing them over
        /// the text is not drawing them over anything. See design/_done/Opaque Line Visuals.md.
        ///
        /// Positions are snapped to the same whole-pixel grid the row tints now use, or the
        /// borders drift up to a pixel from the fills they are supposed to bound. The half
        /// pixel puts a 1 px stroke inside one pixel row rather than across two, which is also
        /// what stops it rendering as two half-intensity lines.
        /// </remarks>
        public void DrawTableLines(DrawingContext dc, double effectiveScroll,
            double viewTop, double viewBottom)
        {
            int i = 0;
            while (i < _layout.VisualLines.Count)
            {
                var vl = _layout.VisualLines[i];
                // Safety check: skip if block index is out of range (can happen after merging)
                if (_content.ParsedBlocks == null || vl.BlockIndex >= _content.ParsedBlocks.Count)
                {
                    i++;
                    continue;
                }
                var parsed = _content.ParsedBlocks[vl.BlockIndex];
                if (parsed.Table == null || parsed.Kind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
                {
                    i++;
                    continue;
                }

                var tableInfo = parsed.Table;
                int tableStart = i;
                int tableEnd = i;
                while (tableEnd < _layout.VisualLines.Count)
                {
                    var p = _content.ParsedBlocks[_layout.VisualLines[tableEnd].BlockIndex];
                    if (p.Table != tableInfo) break;
                    tableEnd++;
                }

                double tableY = _layout.LineYPositions[tableStart];
                double tableBottom = tableEnd > 0
                    ? _layout.LineYPositions[tableEnd - 1] + _layout.GetEffectiveLineHeight(_layout.VisualLines[tableEnd - 1])
                    : tableY;

                if (tableBottom >= viewTop && tableY <= viewBottom
                    && _table.TableColumnWidths.TryGetValue(tableInfo, out var colWidths))
                {
                    double tableWidth = 0;
                    foreach (var w in colWidths) tableWidth += w;
                    double tableX = DocsCanvas._padding;
                    double yTop = Math.Round(tableY) - effectiveScroll;
                    double tableH = Math.Round(tableBottom) - Math.Round(tableY);

                    dc.DrawRectangle(null, _rendering.Palette.TableBorderPen,
                        new Rect(tableX + 0.5, yTop + 0.5, tableWidth - 1, tableH - 1));

                    for (int row = tableStart; row < tableEnd; row++)
                    {
                        double rowY = Math.Round(_layout.LineYPositions[row]) - effectiveScroll + 0.5;
                        if (row > tableStart)
                            dc.DrawLine(_rendering.Palette.TableBorderPen,
                                new Point(tableX, rowY), new Point(tableX + tableWidth, rowY));
                    }

                    double cx = tableX;
                    for (int c = 0; c < colWidths.Length - 1; c++)
                    {
                        cx += colWidths[c];
                        double sx = Math.Round(cx) + 0.5;
                        dc.DrawLine(_rendering.Palette.TableBorderPen,
                            new Point(sx, yTop), new Point(sx, yTop + tableH));
                    }
                }

                i = tableEnd;
            }
        }

        /// <summary>
        /// Draws the content of a table row, including cell text with proper alignment and styling.
        /// </summary>
        /// <remarks>
        /// Each cell's lines are drawn one under the next, top-aligned, inside one clip the height
        /// of the whole row. A row of a table that fits has no layout and draws each cell as its
        /// single line, through the same <see cref="BuildCellLine"/>.
        /// </remarks>
        public void DrawTableRow(DrawingContext dc, VisualLine vl, string blockText,
            ParsedBlock parsed, double y,
            double fontSize, Typeface baseTypeface)
        {
            if (parsed.TableRow == null || parsed.Table == null) return;
            if (!_table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths)) return;

            BlockVisualMap? map = null;
            if (_content.VisualMaps != null && vl.BlockIndex < _content.VisualMaps.Count)
                map = _content.VisualMaps[vl.BlockIndex];

            var layout = vl.TableLayout;
            double x = DocsCanvas._padding;
            double lineH = _rendering.Measure.GetLineHeight(vl.BlockKind);
            int rowLines = layout?.LineCount ?? 1;

            for (int c = 0; c < Math.Min(parsed.TableRow.Cells.Count, colWidths.Length); c++)
            {
                var (s, e) = parsed.TableRow.Cells[c].TrimContent(blockText);
                int cellLines = layout != null && c < layout.CellCount ? layout.CellLineCount(c) : 1;

                dc.PushClip(new RectangleGeometry(new Rect(x, y, colWidths[c], rowLines * lineH)));

                for (int k = 0; k < cellLines; k++)
                {
                    var (ls, le) = layout != null && c < layout.CellCount ? layout.GetLine(c, k) : (s, e);
                    if (BuildCellLine(blockText, parsed, map, c, colWidths, ls, le, fontSize, baseTypeface)
                        is not { } line)
                        continue;

                    double textX = x + DocsCanvas._tableCellPadding + line.AlignX;
                    double lineY = y + k * lineH;
                    DrawCellColorBackgrounds(dc, line.Ft, map, ls, le, textX, lineY, lineH);
                    dc.DrawText(line.Ft, new Point(textX, lineY));
                }

                dc.Pop();
                x += colWidths[c];
            }
        }

        /// <summary>
        /// One line of one cell, ready to draw: its text styled as the cell styles it, and how far
        /// its column's alignment moves it right of the cell's padded left edge. Null when nothing
        /// on the line is visible.
        /// </summary>
        /// <remarks>
        /// Drawing and every position question about a cell are to come through here, so the caret,
        /// a click and the glyphs cannot disagree about where a character is. Alignment is taken
        /// from this line's own width, which excludes the trailing space a wrapped line keeps, so a
        /// centred or right-aligned column aligns each of a cell's lines.
        /// </remarks>
        private (FormattedText Ft, double AlignX)? BuildCellLine(string blockText, ParsedBlock parsed,
            BlockVisualMap? map, int column, double[] colWidths, int ls, int le,
            double fontSize, Typeface baseTypeface)
        {
            string lineText = map != null
                ? map.BuildDisplayString(blockText, ls, le - ls)
                : blockText.Substring(ls, le - ls);
            if (lineText.Length == 0) return null;

            var typeface = parsed.Kind == BlockKind.TableHeaderRow ? TextMeasurer.BoldTypeface : baseTypeface;
            var ft = new FormattedText(lineText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize,
                _rendering.Palette.Foreground, _rendering.Measure.DpiScale);

            if (map != null)
                ApplyInlineStylesForCell(ft, parsed, map, ls, le);
            else
                ApplyInlineStylesForCellRaw(ft, lineText, parsed, ls, le);

            double contentWidth = colWidths[column] - DocsCanvas._tableCellPadding * 2;
            double alignX = parsed.Table!.Alignments[column] switch
            {
                ColumnAlignment.Center => Math.Max(0, (contentWidth - ft.Width) / 2),
                ColumnAlignment.Right => Math.Max(0, contentWidth - ft.Width),
                _ => 0,
            };
            return (ft, alignX);
        }

        /// <summary>
        /// Tints the parts of one cell line that a colour span with a background covers, measured
        /// off the line's own text so the tint sits under the glyphs it belongs to.
        /// </summary>
        private void DrawCellColorBackgrounds(DrawingContext dc, FormattedText ft, BlockVisualMap? map,
            int ls, int le, double textX, double lineY, double lineH)
        {
            if (map?.ColorSpans == null) return;

            int visBase = map.RawToVisual(ls);
            foreach (var cs in map.ColorSpans)
            {
                if (cs.Background == null) continue;
                int csEnd = cs.Start + cs.Length;
                if (csEnd <= ls || cs.Start >= le) continue;

                int visStart = map.RawToVisual(Math.Max(cs.Start, ls)) - visBase;
                int visEnd = Math.Min(map.RawToVisual(Math.Min(csEnd, le)) - visBase, ft.Text.Length);
                if (visStart < 0 || visEnd <= visStart) continue;

                var bounds = ft.BuildHighlightGeometry(new Point(textX, lineY), visStart, visEnd - visStart)?.Bounds;
                if (bounds is not { } b || b.Width <= 0) continue;

                var bg = cs.Background.Value;
                var brush = new SolidColorBrush(Color.FromArgb(40, bg.R, bg.G, bg.B));
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(b.X, lineY, b.Width, lineH));
            }
        }

        /// <summary>
        /// Calculates the X position of the cursor within a table row for rendering.
        /// Accounts for cell alignment and visual styles.
        /// </summary>
        internal double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
        {
            var cells = parsed.TableRow!.Cells;
            string blockText = _doc.GetBlockText(blockIndex);
            BlockVisualMap? map = (_content.VisualMaps != null && blockIndex < _content.VisualMaps.Count) ? _content.VisualMaps[blockIndex] : null;

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
                    double fontSize = _rendering.Measure.GetBlockFontSize(parsed.Kind);
                    var cellTypeface = isHeader ? TextMeasurer.BoldTypeface : TextMeasurer.GetBlockBaseTypeface(parsed.Kind);

                    var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, cellTypeface, fontSize,
                        _rendering.Palette.Foreground, _rendering.Measure.DpiScale);

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
            string blockText = _doc.GetBlockText(vl.BlockIndex);
            BlockVisualMap? map = (_content.VisualMaps != null && vl.BlockIndex < _content.VisualMaps.Count) ? _content.VisualMaps[vl.BlockIndex] : null;
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
                            fullTextW += _rendering.Measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
                        }
                    }
                    else
                    {
                        string cellContent = blockText.Substring(trimStart, trimEnd - trimStart);
                        fullTextW = _rendering.Measure.MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, trimStart);
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
                            double charW = _rendering.Measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
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
                            double charW = _rendering.Measure.MeasureCharWidth(cellContent[i], parsed.Kind, style);
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
                if (ftLen > 0) ft.SetForegroundBrush(_rendering.GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, ftLen);
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
                        ft.SetForegroundBrush(_rendering.GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
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
