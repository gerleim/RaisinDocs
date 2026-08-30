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

    // Where a search starts looking. Not the caret: making a match current also selects it,
    // which leaves the caret at the match *end*, so a caret-relative search skips the very
    // match the user is looking at as soon as they type another letter - "n" lands on a hit,
    // then "no" jumps past it even when that hit still starts with "no". Anchoring to the
    // current match's start keeps it selected for as long as it keeps matching.
    // -1 means "not captured yet"; the next search takes it from the caret/selection.
    private int _searchOriginBlock = -1;
    private int _searchOriginOffset;

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
        _lastSearchQuery = query;
        _lastSearchCaseSensitive = caseSensitive;
        RecomputeMatches();

        if (string.IsNullOrEmpty(query))
        {
            _currentMatchIndex = -1;
            _search.FindBar?.UpdateMatchInfo(-1, 0);
            _rendering.InvalidateVisual();
            return;
        }

        if (_searchOriginBlock < 0)
            CaptureSearchOrigin();

        _currentMatchIndex = -1;
        for (int i = 0; i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (Document.ComparePositions(m.Block, m.Offset, _searchOriginBlock, _searchOriginOffset) >= 0)
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

    /// <summary>
    /// Rebuilds <see cref="_searchMatches"/> from the last query. Pure: touches no cursor,
    /// selection, layout or scroll state, so it is safe to call from the render pass.
    /// </summary>
    private void RecomputeMatches()
    {
        _searchMatches.Clear();
        _searchDirty = false;

        if (string.IsNullOrEmpty(_lastSearchQuery)) return;

        var comparison = _lastSearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int b = 0; b < _doc.BlockCount; b++)
        {
            string blockText = _doc.GetBlockText(b);
            int pos = 0;
            while (pos <= blockText.Length - _lastSearchQuery.Length)
            {
                int found = blockText.IndexOf(_lastSearchQuery, pos, comparison);
                if (found < 0) break;
                _searchMatches.Add(new SearchMatch(b, found, _lastSearchQuery.Length));
                pos = found + _lastSearchQuery.Length;
            }
        }
    }

    /// <summary>
    /// Re-runs the search after the document changed, without navigating. ExecuteSearch ends
    /// in ScrollToMatch, which moves the cursor and selects the match - fine when the user
    /// asked to search, but it would drag the caret to the next match on every keystroke if
    /// used here. The find bar is updated off the dispatcher so no UI is touched mid-render.
    /// </summary>
    private void RefreshMatchesAfterEdit()
    {
        int previousIndex = _currentMatchIndex;
        RecomputeMatches();

        _currentMatchIndex = _searchMatches.Count == 0
            ? -1
            : Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, _searchMatches.Count - 1);

        if (_search.FindBar is { } bar)
        {
            int index = _currentMatchIndex, count = _searchMatches.Count;
            _canvas.Dispatcher.BeginInvoke(() => bar.UpdateMatchInfo(index, count));
        }
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
        // Invalidate before searching, not after: InvalidateLayout marks the search dirty,
        // and the search that follows is authoritative - it must not be left pending.
        _layout.InvalidateLayout();
        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
        if (_searchMatches.Count > 0)
        {
            _currentMatchIndex = Math.Min(savedIndex, _searchMatches.Count - 1);
            ScrollToMatch(_currentMatchIndex);
            _search.FindBar?.UpdateMatchInfo(_currentMatchIndex, _searchMatches.Count);
        }
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

        // Invalidate first - see ReplaceCurrent.
        _layout.InvalidateLayout();
        ExecuteSearch(_lastSearchQuery, _lastSearchCaseSensitive);
    }

    /// <summary>
    /// Drops the search origin so the next search starts from wherever the caret is now.
    /// Called when Find opens: the user may have clicked elsewhere since the last search.
    /// </summary>
    public void ResetSearchOrigin() => _searchOriginBlock = -1;

    public void ClearMatches()
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        _searchOriginBlock = -1;
        // Closing Find must not leave a pending refresh that would re-open the render gate.
        _searchDirty = false;
        _rendering.InvalidateVisual();
    }

    // --- Internal API ---

    /// <summary>
    /// True when there is something to paint, or a pending refresh that may produce some.
    /// Gates the render pass. The dirty case matters: an edit can create the first match of
    /// a search that previously found nothing, and only the render pass re-runs the search.
    /// </summary>
    internal bool HasHighlights => _searchMatches.Count > 0 || _searchDirty;

    internal void InvalidateSearchOnContentChange()
    {
        // Not conditional on having matches already: typing can create the first match of a
        // search that found nothing, and that has to light up too.
        if (!string.IsNullOrEmpty(_lastSearchQuery))
            _searchDirty = true;
    }

    internal void DrawSearchHighlights(DrawingContext dc, double effectiveScroll)
    {
        if (_searchDirty)
            RefreshMatchesAfterEdit();

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

    /// <summary>
    /// Takes the origin from the caret, or from the start of the selection when there is one -
    /// Find seeds its box with the selected text, and the occurrence the user selected has to
    /// be the one that comes up as the current match.
    /// </summary>
    private void CaptureSearchOrigin()
    {
        var doc = _doc.Document;
        if (doc.HasSelection)
        {
            var (startBlock, startOffset, _, _) = doc.GetOrderedSelection();
            _searchOriginBlock = startBlock;
            _searchOriginOffset = startOffset;
        }
        else
        {
            _searchOriginBlock = doc.CursorBlock;
            _searchOriginOffset = doc.CursorOffset;
        }
    }

    private void ScrollToMatch(int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= _searchMatches.Count) return;
        var match = _searchMatches[matchIndex];

        // The match the user is on is where the next search resumes from.
        _searchOriginBlock = match.Block;
        _searchOriginOffset = match.Offset;

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
