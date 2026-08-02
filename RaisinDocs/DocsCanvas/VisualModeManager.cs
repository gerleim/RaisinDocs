using System.Collections.Generic;

namespace RaisinDocs;

/// <summary>
/// Manages visual mode specific cursor navigation and key handling.
/// Extracts visual mode cursor logic from DocsCanvas to reduce its size.
/// </summary>
internal class VisualModeManager
{
    private readonly DocsCanvas _canvas;

    public VisualModeManager(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    // --- Visual mode: cursor helpers ---

    internal void SkipCursorOverHiddenRanges(bool forward)
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int offset = _canvas._doc.CursorOffset;
        int originalOffset = offset;

        if (map.IsHidden(offset))
        {
            if (_canvas.Logger?.IsDebugEnabled ?? false)
                _canvas.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {_canvas._doc.CursorBlock} offset {originalOffset} is hidden. Ranges: {string.Join(", ", map.HiddenRanges.Select(r => $"[{r.Start},{r.Length})"))}");
            if (forward)
            {
                int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
                offset = map.SkipHidden(offset, true);
                while (offset < blockLen && map.IsHidden(offset))
                    offset++;
                if (_canvas.Logger?.IsDebugEnabled ?? false)
                    _canvas.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Forward skip {originalOffset} -> {offset}");
            }
            else
            {
                offset = map.SkipHidden(offset, false);
                while (offset > 0 && map.IsHidden(offset))
                    offset--;
                if (offset == 0 && map.IsHidden(0))
                {
                    int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
                    offset = map.SkipHidden(0, true);
                    while (offset < blockLen && map.IsHidden(offset))
                        offset++;
                    if (_canvas.Logger?.IsDebugEnabled ?? false)
                        _canvas.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip (at start) {originalOffset} -> {offset}");
                }
                else
                {
                    if (_canvas.Logger?.IsDebugEnabled ?? false)
                        _canvas.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip {originalOffset} -> {offset}");
                }
            }
        }
        else
        {
            if (_canvas.Logger?.IsDebugEnabled ?? false)
                _canvas.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {_canvas._doc.CursorBlock} offset {originalOffset} is NOT hidden");
        }
        _canvas._doc.CursorOffset = offset;
    }

    internal void SkipCursorToVisible(bool forward)
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int offset = _canvas._doc.CursorOffset;
        if (forward)
        {
            int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
            while (offset < blockLen && map.IsHidden(offset)) offset++;
        }
        else
        {
            while (offset > 0 && map.IsHidden(offset - 1)) offset--;
        }
        _canvas._doc.CursorOffset = offset;
    }

    internal void ClampCursorAwayFromHidden()
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count
            && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock])) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int offset = _canvas._doc.CursorOffset;

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
            _canvas._doc.CursorOffset = offset;
        }
    }

    internal void ClampCursorBeforeTrailingHidden()
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count
            && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock])) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
        if (blockLen == 0 || !map.IsHidden(blockLen - 1)) return;
        int offset = _canvas._doc.CursorOffset;
        offset = System.Math.Min(offset, blockLen);
        int minOffset = 0;
        if (map.HiddenRanges.Count > 0 && map.HiddenRanges[0].Start == 0)
            minOffset = map.HiddenRanges[0].Length;
        while (offset > minOffset && map.IsHidden(offset - 1))
            offset--;
        _canvas._doc.CursorOffset = offset;
    }

    internal void SkipBackspacePastHiddenVisual()
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int pos = _canvas._doc.CursorOffset - 1;
        while (pos >= 0 && map.IsHidden(pos)) pos--;
        if (pos >= 0)
            _canvas._doc.CursorOffset = pos + 1;
    }

    internal void SkipDeletePastHiddenVisual()
    {
        if (_canvas._visualMaps == null) return;
        if (_canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        var map = _canvas._visualMaps[_canvas._doc.CursorBlock];
        int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
        int pos = _canvas._doc.CursorOffset;
        while (pos < blockLen && map.IsHidden(pos)) pos++;
        _canvas._doc.CursorOffset = pos;
    }

    internal void EnsureCursorOnVisibleBlock(bool? preferForward = null)
    {
        if (_canvas._parsedBlocks == null || _canvas._doc.CursorBlock >= _canvas._parsedBlocks.Count) return;
        if (!_canvas._parsedBlocks[_canvas._doc.CursorBlock].IsSkippedInVisual) return;

        bool forward = preferForward ?? true;
        int limit = System.Math.Min(_canvas._doc.BlockCount, _canvas._parsedBlocks.Count);

        if (forward)
        {
            for (int i = _canvas._doc.CursorBlock + 1; i < limit; i++)
            {
                if (!_canvas._parsedBlocks[i].IsSkippedInVisual)
                {
                    _canvas._doc.CursorBlock = i;
                    _canvas._doc.CursorOffset = 0;
                    return;
                }
            }
        }
        else
        {
            for (int i = _canvas._doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!_canvas._parsedBlocks[i].IsSkippedInVisual)
                {
                    _canvas._doc.CursorBlock = i;
                    _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(i);
                    return;
                }
            }
        }

        if (preferForward != null) return;

        if (forward)
        {
            for (int i = _canvas._doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!_canvas._parsedBlocks[i].IsSkippedInVisual)
                {
                    _canvas._doc.CursorBlock = i;
                    _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(i);
                    return;
                }
            }
        }
        else
        {
            for (int i = _canvas._doc.CursorBlock + 1; i < limit; i++)
            {
                if (!_canvas._parsedBlocks[i].IsSkippedInVisual)
                {
                    _canvas._doc.CursorBlock = i;
                    _canvas._doc.CursorOffset = 0;
                    return;
                }
            }
        }
    }

    // --- Visual mode: key handlers ---

    internal bool HandleBackVisual()
    {
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
        {
            var parsed = _canvas._parsedBlocks[_canvas._doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _canvas._doc.GetBlockText(_canvas._doc.CursorBlock);
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_canvas._doc.CursorOffset > s && _canvas._doc.CursorOffset <= e)
                        break;
                    if (_canvas._doc.CursorOffset <= s)
                        return false;
                }
            }
        }

        SkipBackspacePastHiddenVisual();
        if (_canvas._doc.CursorOffset == 0 && _canvas._doc.CursorBlock > 0 && _canvas._parsedBlocks != null)
        {
            if (_canvas._doc.CursorBlock - 1 < _canvas._parsedBlocks.Count && _canvas._parsedBlocks[_canvas._doc.CursorBlock - 1].IsSkippedInVisual)
                return false;
            if (_canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && (IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]) || ((_canvas._doc.CursorBlock - 1 >= 0) && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock - 1]))))
                return false;
        }

        int prevBlock = _canvas._doc.CursorBlock;
        int prevOffset = _canvas._doc.CursorOffset;
        _canvas._doc.Backspace();
        bool changed = _canvas._doc.CursorBlock != prevBlock || _canvas._doc.CursorOffset != prevOffset;
        if (changed) _canvas._doc.CollapseSelection();

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: false);
        return changed;
    }

    internal bool HandleDeleteVisual()
    {
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
        {
            var parsed = _canvas._parsedBlocks[_canvas._doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _canvas._doc.GetBlockText(_canvas._doc.CursorBlock);
                bool canDelete = false;
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_canvas._doc.CursorOffset >= s && _canvas._doc.CursorOffset < e)
                    { canDelete = true; break; }
                }
                if (!canDelete) return false;
            }
        }

        SkipDeletePastHiddenVisual();
        if (_canvas._doc.CursorOffset >= _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock) &&
            _canvas._doc.CursorBlock < _canvas._doc.BlockCount - 1 && _canvas._parsedBlocks != null)
        {
            if (_canvas._doc.CursorBlock + 1 < _canvas._parsedBlocks.Count && _canvas._parsedBlocks[_canvas._doc.CursorBlock + 1].IsSkippedInVisual)
                return false;
            if (_canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && (IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]) || ((_canvas._doc.CursorBlock + 1 < _canvas._parsedBlocks.Count) && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock + 1]))))
                return false;
        }

        int prevBlocks = _canvas._doc.BlockCount;
        int prevLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
        _canvas._doc.Delete();
        bool changed = _canvas._doc.BlockCount != prevBlocks ||
                       _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock) != prevLen;

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: true);
        return changed;
    }

    internal void HandleLeftVisual(bool shift)
    {
        if (!shift && _canvas._doc.HasSelection)
        {
            var (sb, so, _, _) = _canvas._doc.GetOrderedSelection();
            _canvas._doc.CursorBlock = sb;
            _canvas._doc.CursorOffset = so;
            _canvas._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: false);
        }
        else if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && HandleTableArrow(_canvas._parsedBlocks[_canvas._doc.CursorBlock], forward: false))
        {
            if (!shift) _canvas._doc.CollapseSelection();
        }
        else
        {
            int origBlock = _canvas._doc.CursorBlock;
            int origOffset = _canvas._doc.CursorOffset;
            _canvas._doc.MoveLeft();
            if (!shift) _canvas._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && _canvas._parsedBlocks[_canvas._doc.CursorBlock].IsSkippedInVisual)
            {
                _canvas._doc.CursorBlock = origBlock;
                _canvas._doc.CursorOffset = origOffset;
            }
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
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
        if (!shift && _canvas._doc.HasSelection)
        {
            var (_, _, eb, eo) = _canvas._doc.GetOrderedSelection();
            _canvas._doc.CursorBlock = eb;
            _canvas._doc.CursorOffset = eo;
            _canvas._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: true);
        }
        else if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && HandleTableArrow(_canvas._parsedBlocks[_canvas._doc.CursorBlock], forward: true))
        {
            if (!shift) _canvas._doc.CollapseSelection();
        }
        else
        {
            int origBlock = _canvas._doc.CursorBlock;
            int origOffset = _canvas._doc.CursorOffset;
            _canvas._doc.MoveRight();
            if (!shift) _canvas._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && _canvas._parsedBlocks[_canvas._doc.CursorBlock].IsSkippedInVisual)
            {
                _canvas._doc.CursorBlock = origBlock;
                _canvas._doc.CursorOffset = origOffset;
            }
            if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
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
        string blockText = _canvas._doc.GetBlockText(_canvas._doc.CursorBlock);
        var cells = parsed.TableRow.Cells;

        // find the trimmed content range for each cell
        var cellRanges = new List<(int Start, int End)>();
        foreach (var cell in cells)
            cellRanges.Add(cell.TrimContent(blockText));

        int offset = _canvas._doc.CursorOffset;

        if (forward)
        {
            // find which cell the cursor is in or between
            for (int c = 0; c < cellRanges.Count; c++)
            {
                var (cs, ce) = cellRanges[c];
                if (offset < ce)
                {
                    // cursor is within this cell's content — move right by 1
                    _canvas._doc.CursorOffset = offset + 1;
                    return true;
                }
                if (offset == ce)
                {
                    // cursor is at end of this cell — jump to start of next cell
                    if (c + 1 < cellRanges.Count)
                    {
                        _canvas._doc.CursorOffset = cellRanges[c + 1].Start;
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
                _canvas._doc.CursorOffset = cellRanges[^1].End;
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
                    _canvas._doc.CursorOffset = offset - 1;
                    return true;
                }
                if (offset == cs)
                {
                    // cursor is at start of this cell — jump to end of previous cell
                    if (c > 0)
                    {
                        _canvas._doc.CursorOffset = cellRanges[c - 1].End;
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
                _canvas._doc.CursorOffset = cellRanges[0].Start;
            return true;
        }
    }

    private bool MoveToAdjacentTableRow(ParsedBlock parsed, bool forward)
    {
        if (_canvas._parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _canvas._doc.CursorBlock + 1; b < _canvas._doc.BlockCount; b++)
            {
                var p = _canvas._parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _canvas._doc.CursorBlock = b;
                    string text = _canvas._doc.GetBlockText(b);
                    var firstCell = p.TableRow.Cells[0];
                    int s = firstCell.Start;
                    while (s < firstCell.Start + firstCell.Length && text[s] == ' ') s++;
                    _canvas._doc.CursorOffset = s;
                    return true;
                }
            }
        }
        else
        {
            for (int b = _canvas._doc.CursorBlock - 1; b >= 0; b--)
            {
                var p = _canvas._parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _canvas._doc.CursorBlock = b;
                    string text = _canvas._doc.GetBlockText(b);
                    var lastCell = p.TableRow.Cells[^1];
                    int e = lastCell.Start + lastCell.Length;
                    while (e > lastCell.Start && text[e - 1] == ' ') e--;
                    _canvas._doc.CursorOffset = e;
                    return true;
                }
            }
        }
        return false;
    }

    private bool MoveOutOfTable(ParsedBlock parsed, bool forward)
    {
        if (_canvas._parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _canvas._doc.CursorBlock + 1; b < _canvas._doc.BlockCount; b++)
            {
                if (_canvas._parsedBlocks[b].Table != parsed.Table)
                {
                    _canvas._doc.CursorBlock = b;
                    _canvas._doc.CursorOffset = 0;
                    SkipCursorOverHiddenRanges(forward: true);
                    return true;
                }
            }
        }
        else
        {
            for (int b = _canvas._doc.CursorBlock - 1; b >= 0; b--)
            {
                if (_canvas._parsedBlocks[b].Table != parsed.Table)
                {
                    _canvas._doc.CursorBlock = b;
                    _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(b);
                    SkipCursorOverHiddenRanges(forward: false);
                    return true;
                }
            }
        }
        return false;
    }

    private void CrossToPreviousBlockIfHiddenStart()
    {
        if (_canvas._doc.CursorOffset != 0 || _canvas._doc.CursorBlock == 0) return;
        if (_canvas._visualMaps == null || _canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        if (!_canvas._visualMaps[_canvas._doc.CursorBlock].IsHidden(0)) return;
        if (_canvas._parsedBlocks != null && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock])) return;

        _canvas._doc.CursorBlock--;
        _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
        EnsureCursorOnVisibleBlock(preferForward: false);
        SkipCursorOverHiddenRanges(forward: false);
    }

    private void CrossToNextBlockIfHiddenEnd()
    {
        int blockLen = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
        if (_canvas._doc.CursorOffset != blockLen || blockLen == 0) return;
        if (_canvas._doc.CursorBlock >= _canvas._doc.BlockCount - 1) return;
        if (_canvas._visualMaps == null || _canvas._doc.CursorBlock >= _canvas._visualMaps.Count) return;
        if (!_canvas._visualMaps[_canvas._doc.CursorBlock].IsHidden(blockLen - 1)) return;
        if (_canvas._parsedBlocks != null && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock])) return;

        _canvas._doc.CursorBlock++;
        _canvas._doc.CursorOffset = 0;
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
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: false);
    }

    internal void HandleDownVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: true);
        if (_canvas._parsedBlocks != null && _canvas._doc.CursorBlock < _canvas._parsedBlocks.Count && IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: true);
    }

    internal void ClampCursorToTableCell()
    {
        if (_canvas._parsedBlocks == null) return;
        var parsed = _canvas._parsedBlocks[_canvas._doc.CursorBlock];
        if (parsed.TableRow == null) return;
        string blockText = _canvas._doc.GetBlockText(_canvas._doc.CursorBlock);
        int offset = _canvas._doc.CursorOffset;

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
        _canvas._doc.CursorOffset = best;
    }

    // --- Helper ---

    private bool IsTableRow(ParsedBlock parsed)
        => parsed.IsTableSeparator || parsed.TableRow != null;
}
