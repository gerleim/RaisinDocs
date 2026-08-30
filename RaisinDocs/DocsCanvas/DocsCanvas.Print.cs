using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RaisinDocs;

partial class DocsCanvas
{
    private PageBreakManager? _pageBreakManager;

    public bool ShowPageBreaks => _pageBreakManager?.ShowPageBreaks ?? false;

    public void SetShowPageBreaks(bool show)
    {
        _pageBreakManager ??= new PageBreakManager((ILayoutDataServices)this, (IRenderingServices)this,
            (IParsedContentServices)this);
        _pageBreakManager.SetShowPageBreaks(show);
    }

    internal List<double> TestGetPageBreakYs()
    {
        _pageBreakManager ??= new PageBreakManager((ILayoutDataServices)this, (IRenderingServices)this,
            (IParsedContentServices)this);
        return _pageBreakManager.TestGetPageBreakYs();
    }

    internal int TestVisualLineCount => _visualLines.Count;

    /// <summary>
    /// Width of the visible ink a joined visual line actually renders, measured from the very
    /// <see cref="System.Windows.Media.FormattedText"/> OnRender draws (trailing whitespace
    /// excluded, since it draws nothing). Must fit the layout width or the tail is clipped.
    /// </summary>
    internal double TestRenderedJoinedLineWidth(int vi)
        => _renderingContext.BuildJoinedLineText(_visualLines[vi])?.Width ?? 0;
    internal double TestTotalContentHeight => _totalContentHeight;
    internal List<VisualLine> TestVisualLines => _visualLines;
    internal List<double> TestLineYPositions => _lineYPositions;
    internal List<ParsedBlock>? TestParsedBlocks => _parsedBlocks;
    internal int TestLayoutVersion => _layoutVersion;
    internal TextMeasurer TestMeasure => _measure;

    internal void TestComputeLayoutAtWidth(double width)
    {
        _parsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _syntaxHighlighter);
        ComputeLayoutCore(width);
        _layoutDirty = true;
    }

    internal double GetEffectiveLineHeightPublic(VisualLine visualLine)
    {
        return GetEffectiveLineHeight(visualLine);
    }

    private void DrawPageBreaks(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        _pageBreakManager ??= new PageBreakManager((ILayoutDataServices)this, (IRenderingServices)this,
            (IParsedContentServices)this);
        _pageBreakManager.DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);
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

            // Build parent map for O(1) parent lookup during visual map computation
            var parentMap = BlockVisualMap.BuildParentMap(_parsedBlocks);

            for (int i = 0; i < _doc.BlockCount; i++)
                _visualMaps.Add(BlockVisualMap.Compute(_parsedBlocks[i], _doc.GetBlockText(i), _parsedBlocks, _doc.GetBlockText, parentMap));
        }

        var savedMode = _editMode;
        _editMode = EditMode.Visual;

        var paginator = new DocsPaginator(this, dialog);

        _editMode = savedMode;

        try
        {
            dialog.PrintDocument(paginator, "Document");
        }
        finally
        {
            if (createdMaps && !IsVisual)
                _visualMaps = null;

            _layoutDirty = true;
            _pageBreakManager?.ResetLayoutVersion();
            InvalidateVisual();
        }
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
                                var spacing = _canvas.GetVisualLineSpacing(vl);
                                if (spacing != null)
                                {
                                    _canvas.DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                                        new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(lineY - effectiveScroll),
                                        parsed.Kind);
                                    textX += spacing.MarkerWidth + spacing.SpacingAfterMarker;
                                }
                            }
                            else if (parsed.Kind == BlockKind.UnorderedListItem)
                            {
                                var spacing = _canvas.GetVisualLineSpacing(vl);
                                if (spacing != null)
                                {
                                    _canvas.DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX),
                                        new AbsoluteY(lineY - effectiveScroll),
                                        parsed.Kind, parsed.ListNestingLevel);
                                    textX += spacing.MarkerWidth + spacing.SpacingAfterMarker;
                                }
                            }
                            else if (parsed.Kind == BlockKind.OrderedListItem)
                            {
                                var spacing = _canvas.GetVisualLineSpacing(vl);
                                if (spacing != null)
                                {
                                    _canvas.DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX),
                                        new AbsoluteY(lineY - effectiveScroll),
                                        map.ReplacementPrefix, fontSize, parsed.ListNestingLevel);
                                    textX += spacing.MarkerWidth + spacing.SpacingAfterMarker;
                                }
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
