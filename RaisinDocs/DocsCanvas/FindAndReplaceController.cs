using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

internal class FindAndReplaceController
{
    private readonly ISearchServices _search;
    private readonly IDocumentServices _doc;
    private readonly ICanvasOperations _canvas;
    private readonly IRenderingServices _rendering;
    private readonly ILayoutDataServices _layout;
    private readonly IScrollServices _scroll;
    private readonly IParsedContentServices _content;
    private readonly IVisualModeServices _visual;
    private readonly ITableServices _table;

    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";
    private bool _lastSearchCaseSensitive;
    private bool _searchDirty;

    internal record struct SearchMatch(int Block, int Offset, int Length);

    public FindAndReplaceController(
        ISearchServices search,
        IDocumentServices doc,
        ICanvasOperations canvas,
        IRenderingServices rendering,
        ILayoutDataServices layout,
        IScrollServices scroll,
        IParsedContentServices content,
        IVisualModeServices visual,
        ITableServices table)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    // --- Public API ---

    public void ExecuteSearch(string query, bool caseSensitive)
    {
        _searchMatches.Clear();
        _lastSearchQuery = query;
        _lastSearchCaseSensitive = caseSensitive;
        _searchDirty = false;

        if (string.IsNullOrEmpty(query))
        {
            _currentMatchIndex = -1;
            _search.FindBar?.UpdateMatchInfo(-1, 0);
            _rendering.InvalidateVisual();
            return;
        }

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int b = 0; b < _doc.BlockCount; b++)
        {
            string blockText = _doc.GetBlockText(b);
            int pos = 0;
            while (pos <= blockText.Length - query.Length)
            {
                int found = blockText.IndexOf(query, pos, comparison);
                if (found < 0) break;
                _searchMatches.Add(new SearchMatch(b, found, query.Length));
                pos = found + query.Length;
            }
        }

        _currentMatchIndex = -1;
        for (int i = 0; i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (Document.ComparePositions(m.Block, m.Offset, _doc.Document.CursorBlock, _doc.Document.CursorOffset) >= 0)
            {
                _currentMatchIndex = i;
                break;
            }
        }
        if (_currentMatchIndex == -1 && _searchMatches.Count > 0)
            _currentMatchIndex = 0;

        _search.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);

        if (_currentMatchIndex >= 0)
            ScrollToMatch(_currentMatchIndex);
        else
            _rendering.InvalidateVisual();
    }

    public void NavigateMatch(int direction)
    {
        if (_searchMatches.Count == 0) return;
        _currentMatchIndex += direction;
        if (_currentMatchIndex >= _searchMatches.Count)
            _currentMatchIndex = 0;
        else if (_currentMatchIndex < 0)
            _currentMatchIndex = _searchMatches.Count - 1;
        ScrollToMatch(_currentMatchIndex);
        _search.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
    }

    public void ReplaceCurrent(string replacement)
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;

        var match = _searchMatches[_currentMatchIndex];

        _canvas.SealAndStopTimer();
        _doc.Document.BeginUndoGroup();
        _doc.Document.RemoveTextAt(match.Block, match.Offset, match.Length);
        _doc.Document.InsertTextAt(match.Block, match.Offset, replacement);
        _doc.Document.CursorBlock = match.Block;
        _doc.Document.CursorOffset = match.Offset + replacement.Length;
        _doc.Document.CollapseSelection();
        _doc.Document.SealUndoGroup();

        int savedIndex = _currentMatchIndex;
        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        if (_searchMatches.Count > 0)
        {
            _currentMatchIndex = Math.Min(savedIndex, _searchMatches.Count - 1);
            ScrollToMatch(_currentMatchIndex);
            _search.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
        }

        _layout.InvalidateLayout();
    }

    public void ReplaceAll(string replacement)
    {
        if (_searchMatches.Count == 0) return;

        _canvas.SealAndStopTimer();
        _doc.Document.BeginUndoGroup();

        for (int i = _searchMatches.Count - 1; i >= 0; i--)
        {
            var match = _searchMatches[i];
            _doc.Document.RemoveTextAt(match.Block, match.Offset, match.Length);
            _doc.Document.InsertTextAt(match.Block, match.Offset, replacement);
        }

        _doc.Document.SealUndoGroup();

        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        _layout.InvalidateLayout();
    }

    public void ClearMatches()
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        _rendering.InvalidateVisual();
    }

    // --- Internal API ---

    internal void InvalidateSearchOnContentChange()
    {
        if (_searchMatches.Count > 0 && !string.IsNullOrEmpty(_lastSearchQuery))
            _searchDirty = true;
    }

    internal void DrawSearchHighlights(DrawingContext dc, double effectiveScroll)
    {
        if (_searchDirty)
        {
            _searchDirty = false;
            ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        }

        if (_searchMatches.Count == 0) return;

        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + _rendering.ActualHeight;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int mi = 0; mi < _searchMatches.Count; mi++)
            {
                bool isCurrent = mi == _currentMatchIndex;
                if (pass == 0 && isCurrent) continue;
                if (pass == 1 && !isCurrent) continue;

                var match = _searchMatches[mi];
                var brush = isCurrent ? _rendering.Palette.CurrentSearchMatch : _rendering.Palette.SearchMatch;
                DrawMatchOnVisualLines(dc, match, brush, effectiveScroll, viewTop, viewBottom);
            }
        }
    }

    // --- Private helpers ---

    private void ScrollToMatch(int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= _searchMatches.Count) return;
        var match = _searchMatches[matchIndex];

        _doc.Document.AnchorBlock = match.Block;
        _doc.Document.AnchorOffset = match.Offset;
        _doc.Document.CursorBlock = match.Block;
        _doc.Document.CursorOffset = match.Offset + match.Length;

        _layout.ComputeLayout();
        _scroll.EnsureCursorVisible();
        _rendering.InvalidateVisual();
    }

    private void DrawMatchOnVisualLines(DrawingContext dc, SearchMatch match, Brush brush,
        double effectiveScroll, double viewTop, double viewBottom)
    {
        int matchEnd = match.Offset + match.Length;

        for (int i = 0; i < _layout.VisualLines.Count; i++)
        {
            var vl = _layout.VisualLines[i];
            double lineH = _layout.GetEffectiveLineHeight(vl);
            double lineY = _layout.LineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Group != null)
            {
                DrawMatchOnJoinedLine(dc, i, match, brush, lineY, lineH, effectiveScroll);
                continue;
            }

            if (vl.BlockIndex != match.Block) continue;

            int vlEnd = vl.StartOffset + vl.Length;
            if (match.Offset >= vlEnd || matchEnd <= vl.StartOffset) continue;

            int hlStart = Math.Max(match.Offset, vl.StartOffset);
            int hlEnd = Math.Min(matchEnd, vlEnd);

            string blockText = _doc.GetBlockText(vl.BlockIndex);
            var parsed = _content.ParsedBlocks![vl.BlockIndex];
            var map = _visual.IsVisual ? _content.VisualMaps?[vl.BlockIndex] : null;

            double x1, x2;
            if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null)
            {
                if (_table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                {
                    x1 = _table.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                    x2 = _table.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                }
                else continue;
            }
            else
            {
                x1 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                x2 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                    x1 += prefixW;
                    x2 += prefixW;
                }
            }

            double w = Math.Max(0, x2 - x1);
            if (w > 0)
                dc.DrawRectangle(brush, null,
                    new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, w, lineH));
        }
    }

    private void DrawMatchOnJoinedLine(DrawingContext dc, int visualLineIndex,
        SearchMatch match, Brush brush, double lineY, double lineH, double effectiveScroll)
    {
        var vl = _layout.VisualLines[visualLineIndex];
        var group = vl.Group!;
        int matchStartJoined = group.SourceToJoined(match.Block, match.Offset);
        int matchEndJoined = group.SourceToJoined(match.Block, match.Offset + match.Length);
        if (matchStartJoined < 0 || matchEndJoined < 0) return;

        int vlStart = vl.StartOffset;
        int vlEnd = vl.StartOffset + vl.Length;

        if (vlEnd <= matchStartJoined || vlStart >= matchEndJoined) return;

        int hlStart = Math.Max(vlStart, matchStartJoined);
        int hlEnd = Math.Min(vlEnd, matchEndJoined);

        double x1 = _rendering.MeasureJoinedRange(group, vlStart, hlStart - vlStart);
        double x2 = _rendering.MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

        double w = Math.Max(0, x2 - x1);
        if (w > 0)
            dc.DrawRectangle(brush, null,
                new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, w, lineH));
    }

    // --- Test hooks ---

    internal int TestSearchMatchCount => _searchMatches.Count;
    internal int TestCurrentMatchIndex => _currentMatchIndex;
    internal void TestExecuteSearch(string query, bool caseSensitive) => ExecuteSearch(query, caseSensitive);
}
