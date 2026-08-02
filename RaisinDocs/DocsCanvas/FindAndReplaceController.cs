using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

internal class FindAndReplaceController
{
    private readonly IDocsCanvasServices _services;
    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";
    private bool _lastSearchCaseSensitive;
    private bool _searchDirty;

    internal record struct SearchMatch(int Block, int Offset, int Length);

    public FindAndReplaceController(IDocsCanvasServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
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
            ((DocsCanvas)_services).FindBar?.UpdateMatchInfo(-1, 0);
            ((DocsCanvas)_services).InvalidateVisual();
            return;
        }

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int b = 0; b < ((DocsCanvas)_services)._doc.BlockCount; b++)
        {
            string blockText = ((DocsCanvas)_services)._doc.GetBlockText(b);
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
            if (Document.ComparePositions(m.Block, m.Offset, ((DocsCanvas)_services)._doc.CursorBlock, ((DocsCanvas)_services)._doc.CursorOffset) >= 0)
            {
                _currentMatchIndex = i;
                break;
            }
        }
        if (_currentMatchIndex == -1 && _searchMatches.Count > 0)
            _currentMatchIndex = 0;

        ((DocsCanvas)_services).FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);

        if (_currentMatchIndex >= 0)
            ScrollToMatch(_currentMatchIndex);
        else
            ((DocsCanvas)_services).InvalidateVisual();
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
        ((DocsCanvas)_services).FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
    }

    public void ReplaceCurrent(string replacement)
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;

        var match = _searchMatches[_currentMatchIndex];

        ((DocsCanvas)_services).SealAndStopTimer();
        ((DocsCanvas)_services)._doc.BeginUndoGroup();
        ((DocsCanvas)_services)._doc.RemoveTextAt(match.Block, match.Offset, match.Length);
        ((DocsCanvas)_services)._doc.InsertTextAt(match.Block, match.Offset, replacement);
        ((DocsCanvas)_services)._doc.CursorBlock = match.Block;
        ((DocsCanvas)_services)._doc.CursorOffset = match.Offset + replacement.Length;
        ((DocsCanvas)_services)._doc.CollapseSelection();
        ((DocsCanvas)_services)._doc.SealUndoGroup();

        int savedIndex = _currentMatchIndex;
        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        if (_searchMatches.Count > 0)
        {
            _currentMatchIndex = Math.Min(savedIndex, _searchMatches.Count - 1);
            ScrollToMatch(_currentMatchIndex);
            ((DocsCanvas)_services).FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
        }

        ((DocsCanvas)_services).InvalidateLayout();
    }

    public void ReplaceAll(string replacement)
    {
        if (_searchMatches.Count == 0) return;

        ((DocsCanvas)_services).SealAndStopTimer();
        ((DocsCanvas)_services)._doc.BeginUndoGroup();

        for (int i = _searchMatches.Count - 1; i >= 0; i--)
        {
            var match = _searchMatches[i];
            ((DocsCanvas)_services)._doc.RemoveTextAt(match.Block, match.Offset, match.Length);
            ((DocsCanvas)_services)._doc.InsertTextAt(match.Block, match.Offset, replacement);
        }

        ((DocsCanvas)_services)._doc.SealUndoGroup();

        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        ((DocsCanvas)_services).InvalidateLayout();
    }

    public void ClearMatches()
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        ((DocsCanvas)_services).InvalidateVisual();
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
        double viewBottom = effectiveScroll + ((DocsCanvas)_services).ActualHeight;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int mi = 0; mi < _searchMatches.Count; mi++)
            {
                bool isCurrent = mi == _currentMatchIndex;
                if (pass == 0 && isCurrent) continue;
                if (pass == 1 && !isCurrent) continue;

                var match = _searchMatches[mi];
                var brush = isCurrent ? ((DocsCanvas)_services)._palette.CurrentSearchMatch : ((DocsCanvas)_services)._palette.SearchMatch;
                DrawMatchOnVisualLines(dc, match, brush, effectiveScroll, viewTop, viewBottom);
            }
        }
    }

    // --- Private helpers ---

    private void ScrollToMatch(int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= _searchMatches.Count) return;
        var match = _searchMatches[matchIndex];

        ((DocsCanvas)_services)._doc.AnchorBlock = match.Block;
        ((DocsCanvas)_services)._doc.AnchorOffset = match.Offset;
        ((DocsCanvas)_services)._doc.CursorBlock = match.Block;
        ((DocsCanvas)_services)._doc.CursorOffset = match.Offset + match.Length;

        ((DocsCanvas)_services).ComputeLayout();
        ((DocsCanvas)_services).EnsureCursorVisible();
        ((DocsCanvas)_services).InvalidateVisual();
    }

    private void DrawMatchOnVisualLines(DrawingContext dc, SearchMatch match, Brush brush,
        double effectiveScroll, double viewTop, double viewBottom)
    {
        int matchEnd = match.Offset + match.Length;

        for (int i = 0; i < ((DocsCanvas)_services)._visualLines.Count; i++)
        {
            var vl = ((DocsCanvas)_services)._visualLines[i];
            double lineH = ((DocsCanvas)_services).GetEffectiveLineHeight(vl);
            double lineY = ((DocsCanvas)_services)._lineYPositions[i];
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

            string blockText = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);
            var parsed = ((DocsCanvas)_services)._parsedBlocks![vl.BlockIndex];
            var map = ((DocsCanvas)_services).IsVisual ? ((DocsCanvas)_services)._visualMaps?[vl.BlockIndex] : null;

            double x1, x2;
            if (((DocsCanvas)_services).IsVisual && parsed.Table != null && parsed.TableRow != null)
            {
                if (((DocsCanvas)_services)._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                {
                    x1 = ((DocsCanvas)_services).CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                    x2 = ((DocsCanvas)_services).CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                }
                else continue;
            }
            else
            {
                x1 = ((DocsCanvas)_services).MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                x2 = ((DocsCanvas)_services).MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
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
        var vl = ((DocsCanvas)_services)._visualLines[visualLineIndex];
        var group = vl.Group!;
        int matchStartJoined = group.SourceToJoined(match.Block, match.Offset);
        int matchEndJoined = group.SourceToJoined(match.Block, match.Offset + match.Length);
        if (matchStartJoined < 0 || matchEndJoined < 0) return;

        int vlStart = vl.StartOffset;
        int vlEnd = vl.StartOffset + vl.Length;

        if (vlEnd <= matchStartJoined || vlStart >= matchEndJoined) return;

        int hlStart = Math.Max(vlStart, matchStartJoined);
        int hlEnd = Math.Min(vlEnd, matchEndJoined);

        double x1 = ((DocsCanvas)_services).MeasureJoinedRange(group, vlStart, hlStart - vlStart);
        double x2 = ((DocsCanvas)_services).MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

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
