using System.Collections.Generic;

namespace RaisinDocs;

/// <summary>
/// Manages visual mode specific cursor navigation and key handling.
/// Extracts visual mode cursor logic from DocsCanvas to reduce its size.
/// </summary>
internal class VisualModeManager
{
    private readonly IVisualModeServices _visual;
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _content;
    private readonly ILoggingServices _logging;

    public VisualModeManager(IVisualModeServices visual, IDocumentServices doc, IParsedContentServices content, ILoggingServices logging)
    {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));
    }

    // --- Visual mode: cursor helpers ---

    internal void SkipCursorOverHiddenRanges(bool forward)
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int offset = _doc.Document.CursorOffset;
        int originalOffset = offset;

        if (map.IsHidden(offset))
        {
            if (_logging.Logger?.IsDebugEnabled ?? false)
                _logging.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {_doc.Document.CursorBlock} offset {originalOffset} is hidden. Ranges: {string.Join(", ", map.HiddenRanges.Select(r => $"[{r.Start},{r.Length})"))}");
            if (forward)
            {
                int blockLen = _doc.GetBlockLength(_doc.Document.CursorBlock);
                offset = map.SkipHidden(offset, true);
                while (offset < blockLen && map.IsHidden(offset))
                    offset++;
                if (_logging.Logger?.IsDebugEnabled ?? false)
                    _logging.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Forward skip {originalOffset} -> {offset}");
            }
            else
            {
                offset = map.SkipHidden(offset, false);
                while (offset > 0 && map.IsHidden(offset))
                    offset--;
                if (offset == 0 && map.IsHidden(0))
                {
                    int blockLen = _doc.GetBlockLength(_doc.Document.CursorBlock);
                    offset = map.SkipHidden(0, true);
                    while (offset < blockLen && map.IsHidden(offset))
                        offset++;
                    if (_logging.Logger?.IsDebugEnabled ?? false)
                        _logging.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip (at start) {originalOffset} -> {offset}");
                }
                else
                {
                    if (_logging.Logger?.IsDebugEnabled ?? false)
                        _logging.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip {originalOffset} -> {offset}");
                }
            }
        }
        else
        {
            if (_logging.Logger?.IsDebugEnabled ?? false)
                _logging.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {_doc.Document.CursorBlock} offset {originalOffset} is NOT hidden");
        }
        _doc.Document.CursorOffset = offset;
    }

    internal void SkipCursorToVisible(bool forward)
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int offset = _doc.Document.CursorOffset;
        if (forward)
        {
            int blockLen = _doc.GetBlockLength(_doc.Document.CursorBlock);
            while (offset < blockLen && map.IsHidden(offset)) offset++;
        }
        else
        {
            while (offset > 0 && map.IsHidden(offset - 1)) offset--;
        }
        _doc.Document.CursorOffset = offset;
    }

    internal void ClampCursorAwayFromHidden()
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count
            && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock])) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int offset = _doc.Document.CursorOffset;

        if (map.IsHidden(offset))
        {
            int minOffset = 0;
            if (map.HiddenRanges.Count > 0 && map.HiddenRanges[0].Start == 0)
                minOffset = map.HiddenRanges[0].Length;
            if (offset < minOffset)
                offset = minOffset;
            else
            {
                while (offset > 0 && map.IsHidden(offset))
                    offset--;
                offset++;
            }
            _doc.Document.CursorOffset = offset;
        }
    }

    internal void ClampCursorBeforeTrailingHidden()
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count
            && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock])) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int blockLen = _doc.GetBlockLength(_doc.Document.CursorBlock);
        if (blockLen == 0 || !map.IsHidden(blockLen - 1)) return;
        int offset = _doc.Document.CursorOffset;
        offset = System.Math.Min(offset, blockLen);
        int minOffset = 0;
        if (map.HiddenRanges.Count > 0 && map.HiddenRanges[0].Start == 0)
            minOffset = map.HiddenRanges[0].Length;
        while (offset > minOffset && map.IsHidden(offset - 1))
            offset--;
        _doc.Document.CursorOffset = offset;
    }

    internal void SkipBackspacePastHiddenVisual()
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int pos = _doc.Document.CursorOffset - 1;
        while (pos >= 0 && map.IsHidden(pos)) pos--;
        if (pos >= 0)
            _doc.Document.CursorOffset = pos + 1;
    }

    internal void SkipDeletePastHiddenVisual()
    {
        if (_visual.VisualMaps == null) return;
        if (_doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        var map = _visual.VisualMaps[_doc.Document.CursorBlock];
        int blockLen = _doc.GetBlockLength(_doc.Document.CursorBlock);
        int pos = _doc.Document.CursorOffset;
        while (pos < blockLen && map.IsHidden(pos)) pos++;
        _doc.Document.CursorOffset = pos;
    }

    internal void EnsureCursorOnVisibleBlock(bool? preferForward = null)
    {
        if (_content.ParsedBlocks == null || _doc.Document.CursorBlock >= _content.ParsedBlocks.Count) return;
        if (!_content.ParsedBlocks[_doc.Document.CursorBlock].IsSkippedInVisual) return;

        bool forward = preferForward ?? true;
        int limit = System.Math.Min(_doc.Document.BlockCount, _content.ParsedBlocks.Count);

        if (forward)
        {
            for (int i = _doc.Document.CursorBlock + 1; i < limit; i++)
            {
                if (!_content.ParsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.Document.CursorBlock = i;
                    _doc.Document.CursorOffset = 0;
                    return;
                }
            }
        }
        else
        {
            for (int i = _doc.Document.CursorBlock - 1; i >= 0; i--)
            {
                if (!_content.ParsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.Document.CursorBlock = i;
                    _doc.Document.CursorOffset = _doc.Document.GetBlockLength(i);
                    return;
                }
            }
        }

        if (preferForward != null) return;

        if (forward)
        {
            for (int i = _doc.Document.CursorBlock - 1; i >= 0; i--)
            {
                if (!_content.ParsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.Document.CursorBlock = i;
                    _doc.Document.CursorOffset = _doc.Document.GetBlockLength(i);
                    return;
                }
            }
        }
        else
        {
            for (int i = _doc.Document.CursorBlock + 1; i < limit; i++)
            {
                if (!_content.ParsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.Document.CursorBlock = i;
                    _doc.Document.CursorOffset = 0;
                    return;
                }
            }
        }
    }

    // --- Visual mode: key handlers ---

    internal bool HandleBackVisual()
    {
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
        {
            var parsed = _content.ParsedBlocks[_doc.Document.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _doc.Document.GetBlockText(_doc.Document.CursorBlock);
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_doc.Document.CursorOffset > s && _doc.Document.CursorOffset <= e)
                        break;
                    if (_doc.Document.CursorOffset <= s)
                        return false;
                }
            }
        }

        SkipBackspacePastHiddenVisual();
        if (_doc.Document.CursorOffset == 0 && _doc.Document.CursorBlock > 0 && _content.ParsedBlocks != null)
        {
            if (_doc.Document.CursorBlock - 1 < _content.ParsedBlocks.Count && _content.ParsedBlocks[_doc.Document.CursorBlock - 1].IsSkippedInVisual)
                return false;
            if (_doc.Document.CursorBlock < _content.ParsedBlocks.Count && (IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]) || ((_doc.Document.CursorBlock - 1 >= 0) && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock - 1]))))
                return false;
        }

        int prevBlock = _doc.Document.CursorBlock;
        int prevOffset = _doc.Document.CursorOffset;
        _doc.Document.Backspace();
        bool changed = _doc.Document.CursorBlock != prevBlock || _doc.Document.CursorOffset != prevOffset;
        if (changed) _doc.Document.CollapseSelection();

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: false);
        return changed;
    }

    internal bool HandleDeleteVisual()
    {
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
        {
            var parsed = _content.ParsedBlocks[_doc.Document.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _doc.Document.GetBlockText(_doc.Document.CursorBlock);
                bool canDelete = false;
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_doc.Document.CursorOffset >= s && _doc.Document.CursorOffset < e)
                    { canDelete = true; break; }
                }
                if (!canDelete) return false;
            }
        }

        SkipDeletePastHiddenVisual();
        if (_doc.Document.CursorOffset >= _doc.Document.GetBlockLength(_doc.Document.CursorBlock) &&
            _doc.Document.CursorBlock < _doc.Document.BlockCount - 1 && _content.ParsedBlocks != null)
        {
            if (_doc.Document.CursorBlock + 1 < _content.ParsedBlocks.Count && _content.ParsedBlocks[_doc.Document.CursorBlock + 1].IsSkippedInVisual)
                return false;
            if (_doc.Document.CursorBlock < _content.ParsedBlocks.Count && (IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]) || ((_doc.Document.CursorBlock + 1 < _content.ParsedBlocks.Count) && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock + 1]))))
                return false;
        }

        int prevBlocks = _doc.Document.BlockCount;
        int prevLen = _doc.Document.GetBlockLength(_doc.Document.CursorBlock);
        _doc.Document.Delete();
        bool changed = _doc.Document.BlockCount != prevBlocks ||
                       _doc.Document.GetBlockLength(_doc.Document.CursorBlock) != prevLen;

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: true);
        return changed;
    }

    internal void HandleLeftVisual(bool shift)
    {
        if (!shift && _doc.Document.HasSelection)
        {
            var (sb, so, _, _) = _doc.Document.GetOrderedSelection();
            _doc.Document.CursorBlock = sb;
            _doc.Document.CursorOffset = so;
            _doc.Document.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: false);
        }
        else if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && HandleTableArrow(_content.ParsedBlocks[_doc.Document.CursorBlock], forward: false))
        {
            if (!shift) _doc.Document.CollapseSelection();
        }
        else
        {
            int origBlock = _doc.Document.CursorBlock;
            int origOffset = _doc.Document.CursorOffset;
            _doc.Document.MoveLeft();
            if (!shift) _doc.Document.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && _content.ParsedBlocks[_doc.Document.CursorBlock].IsSkippedInVisual)
            {
                _doc.Document.CursorBlock = origBlock;
                _doc.Document.CursorOffset = origOffset;
            }
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                ClampCursorToTableCell();
            else
            {
                SkipCursorOverHiddenRanges(forward: false);
                CrossToPreviousBlockIfHiddenStart();
            }
        }
    }

    internal void HandleRightVisual(bool shift)
    {
        if (!shift && _doc.Document.HasSelection)
        {
            var (_, _, eb, eo) = _doc.Document.GetOrderedSelection();
            _doc.Document.CursorBlock = eb;
            _doc.Document.CursorOffset = eo;
            _doc.Document.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: true);
        }
        else if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && HandleTableArrow(_content.ParsedBlocks[_doc.Document.CursorBlock], forward: true))
        {
            if (!shift) _doc.Document.CollapseSelection();
        }
        else
        {
            int origBlock = _doc.Document.CursorBlock;
            int origOffset = _doc.Document.CursorOffset;
            _doc.Document.MoveRight();
            if (!shift) _doc.Document.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && _content.ParsedBlocks[_doc.Document.CursorBlock].IsSkippedInVisual)
            {
                _doc.Document.CursorBlock = origBlock;
                _doc.Document.CursorOffset = origOffset;
            }
            if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                ClampCursorToTableCell();
            else
            {
                SkipCursorOverHiddenRanges(forward: true);
                CrossToNextBlockIfHiddenEnd();
            }
        }
    }

    internal bool HandleTableArrow(ParsedBlock parsed, bool forward)
    {
        if (parsed.TableRow == null) return false;
        string blockText = _doc.Document.GetBlockText(_doc.Document.CursorBlock);
        var cells = parsed.TableRow.Cells;

        // find the trimmed content range for each cell
        var cellRanges = new List<(int Start, int End)>();
        foreach (var cell in cells)
            cellRanges.Add(cell.TrimContent(blockText));

        int offset = _doc.Document.CursorOffset;

        if (forward)
        {
            // find which cell the cursor is in or between
            for (int c = 0; c < cellRanges.Count; c++)
            {
                var (cs, ce) = cellRanges[c];
                if (offset < ce)
                {
                    // cursor is within this cell's content — move right by 1
                    _doc.Document.CursorOffset = offset + 1;
                    return true;
                }
                if (offset == ce)
                {
                    // cursor is at end of this cell — jump to start of next cell
                    if (c + 1 < cellRanges.Count)
                    {
                        _doc.Document.CursorOffset = cellRanges[c + 1].Start;
                        return true;
                    }
                    // at end of last cell — cross to next row or leave table
                    if (MoveToAdjacentTableRow(parsed, forward: true))
                        return true;
                    return MoveOutOfTable(parsed, forward: true);
                }
            }
            // cursor is past all cells — clamp to end of last cell
            if (cellRanges.Count > 0)
                _doc.Document.CursorOffset = cellRanges[^1].End;
            return true;
        }
        else
        {
            for (int c = cellRanges.Count - 1; c >= 0; c--)
            {
                var (cs, ce) = cellRanges[c];
                if (offset > cs)
                {
                    // cursor is within this cell's content — move left by 1
                    _doc.Document.CursorOffset = offset - 1;
                    return true;
                }
                if (offset == cs)
                {
                    // cursor is at start of this cell — jump to end of previous cell
                    if (c > 0)
                    {
                        _doc.Document.CursorOffset = cellRanges[c - 1].End;
                        return true;
                    }
                    // at start of first cell — cross to previous row or leave table
                    if (MoveToAdjacentTableRow(parsed, forward: false))
                        return true;
                    return MoveOutOfTable(parsed, forward: false);
                }
            }
            // cursor is before all cells — clamp to start of first cell
            if (cellRanges.Count > 0)
                _doc.Document.CursorOffset = cellRanges[0].Start;
            return true;
        }
    }

    private bool MoveToAdjacentTableRow(ParsedBlock parsed, bool forward)
    {
        if (_content.ParsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _doc.Document.CursorBlock + 1; b < _doc.Document.BlockCount; b++)
            {
                var p = _content.ParsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _doc.Document.CursorBlock = b;
                    string text = _doc.Document.GetBlockText(b);
                    var firstCell = p.TableRow.Cells[0];
                    int s = firstCell.Start;
                    while (s < firstCell.Start + firstCell.Length && text[s] == ' ') s++;
                    _doc.Document.CursorOffset = s;
                    return true;
                }
            }
        }
        else
        {
            for (int b = _doc.Document.CursorBlock - 1; b >= 0; b--)
            {
                var p = _content.ParsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _doc.Document.CursorBlock = b;
                    string text = _doc.Document.GetBlockText(b);
                    var lastCell = p.TableRow.Cells[^1];
                    int e = lastCell.Start + lastCell.Length;
                    while (e > lastCell.Start && text[e - 1] == ' ') e--;
                    _doc.Document.CursorOffset = e;
                    return true;
                }
            }
        }
        return false;
    }

    private bool MoveOutOfTable(ParsedBlock parsed, bool forward)
    {
        if (_content.ParsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _doc.Document.CursorBlock + 1; b < _doc.Document.BlockCount; b++)
            {
                if (_content.ParsedBlocks[b].Table != parsed.Table)
                {
                    _doc.Document.CursorBlock = b;
                    _doc.Document.CursorOffset = 0;
                    SkipCursorOverHiddenRanges(forward: true);
                    return true;
                }
            }
        }
        else
        {
            for (int b = _doc.Document.CursorBlock - 1; b >= 0; b--)
            {
                if (_content.ParsedBlocks[b].Table != parsed.Table)
                {
                    _doc.Document.CursorBlock = b;
                    _doc.Document.CursorOffset = _doc.Document.GetBlockLength(b);
                    SkipCursorOverHiddenRanges(forward: false);
                    return true;
                }
            }
        }
        return false;
    }

    private void CrossToPreviousBlockIfHiddenStart()
    {
        if (_doc.Document.CursorOffset != 0 || _doc.Document.CursorBlock == 0) return;
        if (_visual.VisualMaps == null || _doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        if (!_visual.VisualMaps[_doc.Document.CursorBlock].IsHidden(0)) return;
        if (_content.ParsedBlocks != null && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock])) return;

        _doc.Document.CursorBlock--;
        _doc.Document.CursorOffset = _doc.Document.GetBlockLength(_doc.Document.CursorBlock);
        EnsureCursorOnVisibleBlock(preferForward: false);
        SkipCursorOverHiddenRanges(forward: false);
    }

    private void CrossToNextBlockIfHiddenEnd()
    {
        int blockLen = _doc.Document.GetBlockLength(_doc.Document.CursorBlock);
        if (_doc.Document.CursorOffset != blockLen || blockLen == 0) return;
        if (_doc.Document.CursorBlock >= _doc.Document.BlockCount - 1) return;
        if (_visual.VisualMaps == null || _doc.Document.CursorBlock >= _visual.VisualMaps.Count) return;
        if (!_visual.VisualMaps[_doc.Document.CursorBlock].IsHidden(blockLen - 1)) return;
        if (_content.ParsedBlocks != null && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock])) return;

        _doc.Document.CursorBlock++;
        _doc.Document.CursorOffset = 0;
        EnsureCursorOnVisibleBlock(preferForward: true);
        SkipCursorOverHiddenRanges(forward: true);
    }

    internal void HandleHomeVisual()
    {
        EnsureCursorOnVisibleBlock();
        SkipCursorToVisible(forward: true);
    }

    internal void HandleEndVisual()
    {
        EnsureCursorOnVisibleBlock();
        SkipCursorToVisible(forward: false);
    }

    internal void HandleUpVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: false);
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: false);
    }

    internal void HandleDownVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: true);
        if (_content.ParsedBlocks != null && _doc.Document.CursorBlock < _content.ParsedBlocks.Count && IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: true);
    }

    internal void ClampCursorToTableCell()
    {
        if (_content.ParsedBlocks == null) return;
        var parsed = _content.ParsedBlocks[_doc.Document.CursorBlock];
        if (parsed.TableRow == null) return;
        string blockText = _doc.Document.GetBlockText(_doc.Document.CursorBlock);
        int offset = _doc.Document.CursorOffset;

        foreach (var cell in parsed.TableRow.Cells)
        {
            var (s, e) = cell.TrimContent(blockText);
            if (offset >= s && offset <= e) return;
        }

        // cursor is in a hidden region — find nearest cell boundary
        int best = 0;
        int bestDist = int.MaxValue;
        foreach (var cell in parsed.TableRow.Cells)
        {
            var (s, e) = cell.TrimContent(blockText);
            if (System.Math.Abs(offset - s) < bestDist) { best = s; bestDist = System.Math.Abs(offset - s); }
            if (System.Math.Abs(offset - e) < bestDist) { best = e; bestDist = System.Math.Abs(offset - e); }
        }
        _doc.Document.CursorOffset = best;
    }

    // --- Helper ---

    private bool IsTableRow(ParsedBlock parsed)
        => parsed.IsTableSeparator || parsed.TableRow != null;
}
