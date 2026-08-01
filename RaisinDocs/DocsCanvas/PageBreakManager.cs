using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

internal class PageBreakManager
{
    private readonly DocsCanvas _canvas;
    private bool _showPageBreaks;
    private readonly List<double> _pageBreakYs = [];
    private int _pageBreakLayoutVersion = -1;

    private const double DefaultPageHeight = 1056;
    private const double PageBreakMarginY = 60;

    private static readonly Pen _pageBreakPen = BuildPageBreakPen();
    private static readonly Brush _pageBreakLabelBrush = BuildPageBreakLabelBrush();

    public PageBreakManager(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    public bool ShowPageBreaks => _showPageBreaks;

    public void SetShowPageBreaks(bool show)
    {
        if (_showPageBreaks == show) return;
        _showPageBreaks = show;
        _canvas.InvalidateVisual();
    }

    internal List<double> TestGetPageBreakYs()
    {
        _showPageBreaks = true;
        _pageBreakLayoutVersion = -1;
        ComputePageBreakPositions();
        return new List<double>(_pageBreakYs);
    }

    public void ComputePageBreakPositions()
    {
        if (_pageBreakLayoutVersion == _canvas.TestLayoutVersion) return;
        _pageBreakLayoutVersion = _canvas.TestLayoutVersion;
        _pageBreakYs.Clear();

        if (_canvas.TestVisualLineCount == 0) return;

        double pageContentH = DefaultPageHeight - PageBreakMarginY * 2;
        double pageTopY = _canvas.TestLineYPositions[0];
        int pageStartLine = 0;
        int prevBlockIndex = _canvas.TestVisualLines[0].BlockIndex;

        for (int i = 1; i < _canvas.TestVisualLineCount; i++)
        {
            int bi = _canvas.TestVisualLines[i].BlockIndex;

            if (_canvas.TestParsedBlocks != null && bi > prevBlockIndex)
            {
                bool hasExplicitBreak = false;
                for (int b = prevBlockIndex; b < bi; b++)
                {
                    if (_canvas.TestParsedBlocks[b].Kind == BlockKind.PageBreak)
                    {
                        hasExplicitBreak = true;
                        break;
                    }
                }
                if (hasExplicitBreak)
                {
                    _pageBreakYs.Add(_canvas.TestLineYPositions[i]);
                    pageTopY = _canvas.TestLineYPositions[i];
                    pageStartLine = i;
                    prevBlockIndex = bi;
                    continue;
                }
            }

            double lineBottom = _canvas.TestLineYPositions[i] + _canvas.GetEffectiveLineHeightPublic(_canvas.TestVisualLines[i]) - pageTopY;
            if (lineBottom > pageContentH && i > pageStartLine)
            {
                int breakAt = AvoidOrphanedHeading(i, pageStartLine, _canvas.TestVisualLines);
                _pageBreakYs.Add(_canvas.TestLineYPositions[breakAt]);
                pageTopY = _canvas.TestLineYPositions[breakAt];
                pageStartLine = breakAt;
            }

            prevBlockIndex = bi;
        }
    }

    private static int AvoidOrphanedHeading(int breakAt, int pageStart, List<DocsCanvas.VisualLine> lines)
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

    public void DrawPageBreaks(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        ComputePageBreakPositions();
        if (_pageBreakYs.Count == 0) return;

        var pen = _pageBreakPen;
        double width = _canvas.ActualWidth;

        for (int i = 0; i < _pageBreakYs.Count; i++)
        {
            double y = _pageBreakYs[i];
            if (y + 10 < viewTop) continue;
            if (y - 10 > viewBottom) break;

            double screenY = Math.Round(y - effectiveScroll) + 0.5;
            dc.DrawLine(pen, new Point(0, screenY), new Point(width, screenY));

            string label = $"Page {i + 2}";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, 10,
                _pageBreakLabelBrush, _canvas.TestMeasure.DpiScale);
            dc.DrawText(ft, new Point(width - ft.Width - 6, screenY + 2));
        }
    }

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

    public void ResetLayoutVersion()
    {
        _pageBreakLayoutVersion = -1;
    }
}
