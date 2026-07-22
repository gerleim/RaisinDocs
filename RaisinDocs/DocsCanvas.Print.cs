using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RaisinDocs;

partial class DocsCanvas
{
    private bool _showPageBreaks;
    private readonly List<double> _pageBreakYs = [];
    private int _pageBreakLayoutVersion = -1;

    public bool ShowPageBreaks => _showPageBreaks;

    public void SetShowPageBreaks(bool show)
    {
        if (_showPageBreaks == show) return;
        _showPageBreaks = show;
        InvalidateVisual();
    }

    internal List<double> TestGetPageBreakYs()
    {
        _showPageBreaks = true;
        _pageBreakLayoutVersion = -1;
        ComputePageBreakPositions();
        return new List<double>(_pageBreakYs);
    }

    internal int TestVisualLineCount => _visualLines.Count;
    internal double TestTotalContentHeight => _totalContentHeight;

    internal void TestComputeLayoutAtWidth(double width)
    {
        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);
        ComputeLayoutCore(width);
        _layoutDirty = true;
    }

    private void ComputePageBreakPositions()
    {
        if (_pageBreakLayoutVersion == _layoutVersion) return;
        _pageBreakLayoutVersion = _layoutVersion;
        _pageBreakYs.Clear();

        if (_visualLines.Count == 0) return;

        double pageContentH = DefaultPageHeight - DocsPaginator.MarginY * 2;
        double pageTopY = _lineYPositions[0];
        int pageStartLine = 0;
        int prevBlockIndex = _visualLines[0].BlockIndex;

        for (int i = 1; i < _visualLines.Count; i++)
        {
            int bi = _visualLines[i].BlockIndex;

            if (_parsedBlocks != null && bi > prevBlockIndex)
            {
                bool hasExplicitBreak = false;
                for (int b = prevBlockIndex; b < bi; b++)
                {
                    if (_parsedBlocks[b].Kind == BlockKind.PageBreak)
                    {
                        hasExplicitBreak = true;
                        break;
                    }
                }
                if (hasExplicitBreak)
                {
                    _pageBreakYs.Add(_lineYPositions[i]);
                    pageTopY = _lineYPositions[i];
                    pageStartLine = i;
                    prevBlockIndex = bi;
                    continue;
                }
            }

            double lineBottom = _lineYPositions[i] + GetEffectiveLineHeight(_visualLines[i]) - pageTopY;
            if (lineBottom > pageContentH && i > pageStartLine)
            {
                int breakAt = AvoidOrphanedHeading(i, pageStartLine, _visualLines);
                _pageBreakYs.Add(_lineYPositions[breakAt]);
                pageTopY = _lineYPositions[breakAt];
                pageStartLine = breakAt;
            }

            prevBlockIndex = bi;
        }
    }

    private static int AvoidOrphanedHeading(int breakAt, int pageStart, List<VisualLine> lines)
    {
        int candidate = breakAt;
        while (candidate > pageStart + 1)
        {
            var prev = lines[candidate - 1];
            if (prev.Length == 0)
                candidate--;
            else if (prev.BlockKind is >= BlockKind.Heading1 and <= BlockKind.Heading6)
                return candidate - 1;
            else
                break;
        }
        return breakAt;
    }

    private void DrawPageBreaks(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        ComputePageBreakPositions();
        if (_pageBreakYs.Count == 0) return;

        var pen = _pageBreakPen;
        double width = ActualWidth;

        for (int i = 0; i < _pageBreakYs.Count; i++)
        {
            double y = _pageBreakYs[i];
            if (y + 10 < viewTop) continue;
            if (y - 10 > viewBottom) break;

            double screenY = Math.Round(y - effectiveScroll) + 0.5;
            dc.DrawLine(pen, new Point(0, screenY), new Point(width, screenY));

            string label = $"Page {i + 2}";
            var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, 10,
                _pageBreakLabelBrush, _measure.DpiScale);
            dc.DrawText(ft, new Point(width - ft.Width - 6, screenY + 2));
        }
    }

    private const double DefaultPageHeight = 1056;

    private static readonly Pen _pageBreakPen = BuildPageBreakPen();
    private static readonly Brush _pageBreakLabelBrush = BuildPageBreakLabelBrush();

    private static Pen BuildPageBreakPen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 100, 150, 220));
        brush.Freeze();
        var pen = new Pen(brush, 1) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();
        return pen;
    }

    private static Brush BuildPageBreakLabelBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 100, 150, 220));
        brush.Freeze();
        return brush;
    }

    private static readonly ThemePalette _printPalette = BuildPalette(
        background: Colors.White,
        foreground: Colors.Black,
        cursor: Colors.Black,
        selection: Colors.Transparent,
        scrollTrack: Colors.Transparent,
        scrollThumb: Colors.Transparent,
        syntax: Color.FromRgb(140, 140, 140),
        codeBackground: Color.FromArgb(20, 0, 0, 0),
        tableBg: Color.FromArgb(12, 0, 0, 0),
        tableHeaderBg: Color.FromArgb(25, 0, 0, 0),
        tableBorder: Color.FromArgb(80, 0, 0, 0),
        searchMatch: Colors.Transparent,
        currentSearchMatch: Colors.Transparent);

    internal void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        _measure.EnsureMeasured(this);
        ComputeLayout();

        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);

        bool createdMaps = _visualMaps == null;
        if (createdMaps)
        {
            _visualMaps = new List<BlockVisualMap>(_doc.BlockCount);
            for (int i = 0; i < _doc.BlockCount; i++)
                _visualMaps.Add(BlockVisualMap.Compute(_parsedBlocks[i], _doc.GetBlockText(i), _parsedBlocks, _doc.GetBlockText));
        }

        var savedMode = _editMode;
        _editMode = EditMode.Visual;

        var paginator = new DocsPaginator(this, dialog);

        _editMode = savedMode;

        dialog.PrintDocument(paginator, "Document");

        if (createdMaps && !IsVisual)
            _visualMaps = null;

        _layoutDirty = true;
        _pageBreakLayoutVersion = -1;
        InvalidateVisual();
    }

    private sealed class DocsPaginator : DocumentPaginator
    {
        private readonly DocsCanvas _canvas;
        private Size _pageSize;
        private const double MarginX = 48;
        internal const double MarginY = 60;

        private readonly List<VisualLine> _lines;
        private readonly List<double> _lineYs;
        private readonly Dictionary<TableInfo, double[]> _colWidths;
        private readonly double _contentWidth;
        private readonly double _contentHeight;
        private readonly List<int> _pageStarts;

        public DocsPaginator(DocsCanvas canvas, PrintDialog dialog)
        {
            _canvas = canvas;
            _pageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            _contentWidth = _pageSize.Width - MarginX * 2;
            _contentHeight = _pageSize.Height - MarginY * 2;

            canvas.ComputeLayoutCore(_contentWidth);

            _lines = new List<VisualLine>(canvas._visualLines);
            _lineYs = new List<double>(canvas._lineYPositions);
            _colWidths = new Dictionary<TableInfo, double[]>(canvas._tableColumnWidths);

            canvas._layoutDirty = true;

            _pageStarts = ComputePageBreaks();
        }

        private double GetLineHeight(int i)
        {
            var vl = _lines[i];
            double h = _canvas._measure.GetLineHeight(vl.BlockKind);
            return vl.OverrideHeight > h ? vl.OverrideHeight : h;
        }

        private List<int> ComputePageBreaks()
        {
            if (_lines.Count == 0)
                return [0];

            var pages = new List<int> { 0 };
            double pageTopY = _lineYs[0];

            for (int i = 0; i < _lines.Count; i++)
            {
                if (IsExplicitPageBreakBefore(i) && i > pages[^1])
                {
                    pages.Add(i);
                    pageTopY = _lineYs[i];
                    continue;
                }

                double lineBottom = _lineYs[i] + GetLineHeight(i) - pageTopY;
                if (lineBottom > _contentHeight && i > pages[^1])
                {
                    int breakAt = FindBestBreak(pages[^1], i);
                    pages.Add(breakAt);
                    pageTopY = _lineYs[breakAt];
                }
            }
            return pages;
        }

        private bool IsExplicitPageBreakBefore(int lineIndex)
        {
            if (_canvas._parsedBlocks == null || lineIndex == 0) return false;
            int bi = _lines[lineIndex].BlockIndex;
            int prevBi = _lines[lineIndex - 1].BlockIndex;
            for (int b = prevBi; b < bi; b++)
            {
                if (b < _canvas._parsedBlocks.Count
                    && _canvas._parsedBlocks[b].Kind == BlockKind.PageBreak)
                    return true;
            }
            return false;
        }

        private int FindBestBreak(int pageStart, int overflowLine)
            => AvoidOrphanedHeading(overflowLine, pageStart, _lines);

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageStarts.Count)
                return DocumentPage.Missing;

            int first = _pageStarts[pageNumber];
            int last = pageNumber + 1 < _pageStarts.Count
                ? _pageStarts[pageNumber + 1] - 1
                : _lines.Count - 1;
            double pageTopY = first < _lineYs.Count ? _lineYs[first] : 0;
            double effectiveScroll = pageTopY - MarginY;

            var savedPalette = _canvas._palette;
            var savedColWidths = new Dictionary<TableInfo, double[]>(_canvas._tableColumnWidths);
            var savedMaxWidth = _canvas._layoutMaxWidth;

            _canvas._palette = _printPalette;
            _canvas._tableColumnWidths.Clear();
            foreach (var kv in _colWidths)
                _canvas._tableColumnWidths[kv.Key] = kv.Value;
            _canvas._layoutMaxWidth = _contentWidth;

            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, _pageSize.Width, _pageSize.Height));

                    dc.PushClip(new RectangleGeometry(
                        new Rect(0, MarginY, _pageSize.Width, _contentHeight)));

                    DrawCodeBlockBgs(dc, first, last, effectiveScroll);
                    DrawColorBlockBgs(dc, first, last, effectiveScroll);

                    dc.PushTransform(new TranslateTransform(MarginX - _padding, 0));
                    DrawTableBgs(dc, first, last, effectiveScroll);
                    DrawTextContent(dc, first, last, effectiveScroll);
                    dc.Pop();

                    dc.Pop();

                    DrawPageNumber(dc, pageNumber + 1, _pageStarts.Count);
                }

                return new DocumentPage(visual, _pageSize,
                    new Rect(_pageSize),
                    new Rect(MarginX, MarginY, _contentWidth, _contentHeight));
            }
            finally
            {
                _canvas._palette = savedPalette;
                _canvas._tableColumnWidths.Clear();
                foreach (var kv in savedColWidths)
                    _canvas._tableColumnWidths[kv.Key] = kv.Value;
                _canvas._layoutMaxWidth = savedMaxWidth;
            }
        }

        private void DrawCodeBlockBgs(DrawingContext dc, int first, int last, double effectiveScroll)
        {
            for (int i = first; i <= last; i++)
            {
                var vl = _lines[i];
                if (vl.BlockKind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine) continue;
                double lineH = _canvas._measure.GetLineHeight(vl.BlockKind);
                double y = _lineYs[i] - effectiveScroll;
                dc.DrawRectangle(_printPalette.CodeBackground, null,
                    new Rect(MarginX, y, _contentWidth, lineH));
            }
        }

        private void DrawColorBlockBgs(DrawingContext dc, int first, int last, double effectiveScroll)
        {
            if (_canvas._parsedBlocks == null) return;
            for (int i = first; i <= last; i++)
            {
                var vl = _lines[i];
                if (vl.BlockIndex >= _canvas._parsedBlocks.Count) continue;
                var parsed = _canvas._parsedBlocks[vl.BlockIndex];
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;
                if (parsed.BlockColor?.Background is not { } bg) continue;

                double lineH = GetLineHeight(i);
                double y = _lineYs[i] - effectiveScroll;
                dc.DrawRectangle(_canvas.GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                    new Rect(MarginX, y, _contentWidth, lineH));
            }
        }

        private void DrawTableBgs(DrawingContext dc, int first, int last, double effectiveScroll)
        {
            if (_canvas._parsedBlocks == null) return;
            int i = first;
            while (i <= last)
            {
                var vl = _lines[i];
                var parsed = _canvas._parsedBlocks[vl.BlockIndex];
                if (parsed.Table == null || parsed.Kind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
                {
                    i++;
                    continue;
                }

                var tableInfo = parsed.Table;
                int tableStart = i;
                while (tableStart > 0)
                {
                    var p = _canvas._parsedBlocks[_lines[tableStart - 1].BlockIndex];
                    if (p.Table != tableInfo) break;
                    tableStart--;
                }

                int tableEnd = i + 1;
                while (tableEnd < _lines.Count)
                {
                    var p = _canvas._parsedBlocks[_lines[tableEnd].BlockIndex];
                    if (p.Table != tableInfo) break;
                    tableEnd++;
                }

                if (_colWidths.TryGetValue(tableInfo, out var colWidths))
                {
                    int visFirst = Math.Max(tableStart, first);
                    int visLast = Math.Min(tableEnd - 1, last);

                    double tableY = _lineYs[visFirst] - effectiveScroll;
                    double tableBottomY = _lineYs[visLast] + GetLineHeight(visLast) - effectiveScroll;
                    double tableH = tableBottomY - tableY;

                    double tableWidth = 0;
                    foreach (var w in colWidths) tableWidth += w;

                    dc.DrawRectangle(_printPalette.TableBackground, null,
                        new Rect(_padding, tableY, tableWidth, tableH));

                    if (tableStart >= first && tableStart <= last)
                    {
                        double headerH = _canvas._measure.GetLineHeight(_lines[tableStart].BlockKind);
                        double headerY = _lineYs[tableStart] - effectiveScroll;
                        dc.DrawRectangle(_printPalette.TableHeaderBackground, null,
                            new Rect(_padding, headerY, tableWidth, headerH));
                    }

                    dc.DrawRectangle(null, _printPalette.TableBorderPen,
                        new Rect(_padding, tableY, tableWidth, tableH));

                    for (int row = visFirst; row <= visLast; row++)
                    {
                        double rowY = _lineYs[row] - effectiveScroll;
                        if (row > tableStart)
                            dc.DrawLine(_printPalette.TableBorderPen,
                                new Point(_padding, rowY), new Point(_padding + tableWidth, rowY));
                    }

                    double cx = _padding;
                    for (int c = 0; c < colWidths.Length - 1; c++)
                    {
                        cx += colWidths[c];
                        dc.DrawLine(_printPalette.TableBorderPen,
                            new Point(cx, tableY), new Point(cx, tableY + tableH));
                    }
                }

                i = Math.Min(tableEnd, last + 1);
            }
        }

        private void DrawTextContent(DrawingContext dc, int first, int last, double effectiveScroll)
        {
            for (int i = first; i <= last; i++)
            {
                var vl = _lines[i];
                if (vl.Length == 0) continue;

                double lineY = _lineYs[i];

                if (vl.Group != null)
                {
                    _canvas.DrawJoinedLine(dc, vl, lineY, effectiveScroll);
                    continue;
                }

                string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
                var parsed = _canvas._parsedBlocks![vl.BlockIndex];
                double fontSize = _canvas._measure.GetBlockFontSize(parsed.Kind);
                var baseTypeface = TextMeasurer.GetBlockBaseTypeface(parsed.Kind);
                var map = _canvas._visualMaps?[vl.BlockIndex];

                if (parsed.Kind == BlockKind.ThematicBreak)
                {
                    double ruleY = lineY - effectiveScroll + 10;
                    dc.DrawLine(_printPalette.TableBorderPen,
                        new Point(_padding, ruleY), new Point(_padding + _contentWidth, ruleY));
                }
                else if (parsed.Table != null && parsed.TableRow != null)
                {
                    _canvas.DrawTableRow(dc, vl, blockText, parsed, lineY, effectiveScroll, fontSize, baseTypeface);
                }
                else if (map != null)
                {
                    if (_canvas.HasImagesOnLine(vl, map))
                    {
                        _canvas.DrawVisualLineWithImages(dc, vl, blockText, parsed, map,
                            lineY, effectiveScroll, fontSize, baseTypeface);
                    }
                    else
                    {
                        double textX = _padding;

                        if (map.ReplacementPrefix != null && vl.StartOffset == 0)
                        {
                            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                            {
                                double nestOff = _canvas._measure.MeasureReplacementPrefix(
                                    map.ReplacementPrefix, map.PrefixMeasureKind) - TextMeasurer.ListIndent;
                                textX += _canvas.DrawTaskListCheckbox(dc,
                                    parsed.Kind == BlockKind.TaskListItemChecked,
                                    _padding, lineY - effectiveScroll, parsed.Kind, nestOff);
                            }
                            else if (map.IsContinuationIndent)
                            {
                                textX += _canvas._measure.MeasureReplacementPrefix(
                                    map.ReplacementPrefix, map.PrefixMeasureKind);
                            }
                            else
                            {
                                var prefixFt = new FormattedText(map.ReplacementPrefix,
                                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                    TextMeasurer.NormalTypeface, fontSize, _printPalette.Syntax,
                                    _canvas._measure.DpiScale);
                                dc.DrawText(prefixFt, new Point(_padding, lineY - effectiveScroll));
                                textX += _canvas._measure.MeasureReplacementPrefix(
                                    map.ReplacementPrefix, map.PrefixMeasureKind);
                            }
                        }

                        string displayText = map.BuildDisplayString(blockText, vl.StartOffset, vl.Length);
                        if (displayText.Length > 0)
                        {
                            var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, baseTypeface, fontSize,
                                _printPalette.Foreground, _canvas._measure.DpiScale);
                            _canvas.ApplyInlineStylesVisual(ft, vl, parsed, map);
                            if (parsed.Kind == BlockKind.TaskListItemChecked)
                            {
                                ft.SetForegroundBrush(_printPalette.Syntax, 0, displayText.Length);
                                ft.SetTextDecorations(TextDecorations.Strikethrough, 0, displayText.Length);
                            }
                            dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));
                        }
                    }
                }
            }
        }

        private void DrawPageNumber(DrawingContext dc, int page, int total)
        {
            string text = $"{page} / {total}";
            var brush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            brush.Freeze();
            var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, 10,
                brush, _canvas._measure.DpiScale);
            double x = (_pageSize.Width - ft.Width) / 2;
            double y = _pageSize.Height - MarginY * 0.5 - ft.Height * 0.5;
            dc.DrawText(ft, new Point(x, y));
        }

        public override int PageCount => _pageStarts.Count;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override bool IsPageCountValid => true;
        public override IDocumentPaginatorSource? Source => null;
    }
}
