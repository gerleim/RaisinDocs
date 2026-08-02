using System.Collections.Generic;

namespace RaisinDocs;

/// <summary>
/// Manages visual mode specific cursor navigation and key handling.
/// Extracts visual mode cursor logic from DocsCanvas to reduce its size.
/// </summary>
internal class VisualModeManager
{
    private readonly IDocsCanvasServices _services;

    public VisualModeManager(IDocsCanvasServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    // --- Visual mode: cursor helpers ---

    internal void SkipCursorOverHiddenRanges(bool forward)
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int offset = ((DocsCanvas)_services)._doc.CursorOffset;
        int originalOffset = offset;

        if (map.IsHidden(offset))
        {
            if (_services.Logger?.IsDebugEnabled ?? false)
                _services.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {((DocsCanvas)_services)._doc.CursorBlock} offset {originalOffset} is hidden. Ranges: {string.Join(", ", map.HiddenRanges.Select(r => $"[{r.Start},{r.Length})"))}");
            if (forward)
            {
                int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
                offset = map.SkipHidden(offset, true);
                while (offset < blockLen && map.IsHidden(offset))
                    offset++;
                if (_services.Logger?.IsDebugEnabled ?? false)
                    _services.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Forward skip {originalOffset} -> {offset}");
            }
            else
            {
                offset = map.SkipHidden(offset, false);
                while (offset > 0 && map.IsHidden(offset))
                    offset--;
                if (offset == 0 && map.IsHidden(0))
                {
                    int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
                    offset = map.SkipHidden(0, true);
                    while (offset < blockLen && map.IsHidden(offset))
                        offset++;
                    if (_services.Logger?.IsDebugEnabled ?? false)
                        _services.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip (at start) {originalOffset} -> {offset}");
                }
                else
                {
                    if (_services.Logger?.IsDebugEnabled ?? false)
                        _services.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Backward skip {originalOffset} -> {offset}");
                }
            }
        }
        else
        {
            if (_services.Logger?.IsDebugEnabled ?? false)
                _services.Logger.Log(DocsLogLevel.Debug, $"SkipCursorOverHiddenRanges: Block {((DocsCanvas)_services)._doc.CursorBlock} offset {originalOffset} is NOT hidden");
        }
        ((DocsCanvas)_services)._doc.CursorOffset = offset;
    }

    internal void SkipCursorToVisible(bool forward)
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int offset = ((DocsCanvas)_services)._doc.CursorOffset;
        if (forward)
        {
            int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
            while (offset < blockLen && map.IsHidden(offset)) offset++;
        }
        else
        {
            while (offset > 0 && map.IsHidden(offset - 1)) offset--;
        }
        ((DocsCanvas)_services)._doc.CursorOffset = offset;
    }

    internal void ClampCursorAwayFromHidden()
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count
            && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock])) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int offset = ((DocsCanvas)_services)._doc.CursorOffset;

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
            ((DocsCanvas)_services)._doc.CursorOffset = offset;
        }
    }

    internal void ClampCursorBeforeTrailingHidden()
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count
            && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock])) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
        if (blockLen == 0 || !map.IsHidden(blockLen - 1)) return;
        int offset = ((DocsCanvas)_services)._doc.CursorOffset;
        offset = System.Math.Min(offset, blockLen);
        int minOffset = 0;
        if (map.HiddenRanges.Count > 0 && map.HiddenRanges[0].Start == 0)
            minOffset = map.HiddenRanges[0].Length;
        while (offset > minOffset && map.IsHidden(offset - 1))
            offset--;
        ((DocsCanvas)_services)._doc.CursorOffset = offset;
    }

    internal void SkipBackspacePastHiddenVisual()
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int pos = ((DocsCanvas)_services)._doc.CursorOffset - 1;
        while (pos >= 0 && map.IsHidden(pos)) pos--;
        if (pos >= 0)
            ((DocsCanvas)_services)._doc.CursorOffset = pos + 1;
    }

    internal void SkipDeletePastHiddenVisual()
    {
        if (((DocsCanvas)_services)._visualMaps == null) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        var map = ((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock];
        int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
        int pos = ((DocsCanvas)_services)._doc.CursorOffset;
        while (pos < blockLen && map.IsHidden(pos)) pos++;
        ((DocsCanvas)_services)._doc.CursorOffset = pos;
    }

    internal void EnsureCursorOnVisibleBlock(bool? preferForward = null)
    {
        if (((DocsCanvas)_services)._parsedBlocks == null || ((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._parsedBlocks.Count) return;
        if (!((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock].IsSkippedInVisual) return;

        bool forward = preferForward ?? true;
        int limit = System.Math.Min(((DocsCanvas)_services)._doc.BlockCount, ((DocsCanvas)_services)._parsedBlocks.Count);

        if (forward)
        {
            for (int i = ((DocsCanvas)_services)._doc.CursorBlock + 1; i < limit; i++)
            {
                if (!((DocsCanvas)_services)._parsedBlocks[i].IsSkippedInVisual)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = i;
                    ((DocsCanvas)_services)._doc.CursorOffset = 0;
                    return;
                }
            }
        }
        else
        {
            for (int i = ((DocsCanvas)_services)._doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!((DocsCanvas)_services)._parsedBlocks[i].IsSkippedInVisual)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = i;
                    ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(i);
                    return;
                }
            }
        }

        if (preferForward != null) return;

        if (forward)
        {
            for (int i = ((DocsCanvas)_services)._doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!((DocsCanvas)_services)._parsedBlocks[i].IsSkippedInVisual)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = i;
                    ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(i);
                    return;
                }
            }
        }
        else
        {
            for (int i = ((DocsCanvas)_services)._doc.CursorBlock + 1; i < limit; i++)
            {
                if (!((DocsCanvas)_services)._parsedBlocks[i].IsSkippedInVisual)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = i;
                    ((DocsCanvas)_services)._doc.CursorOffset = 0;
                    return;
                }
            }
        }
    }

    // --- Visual mode: key handlers ---

    internal bool HandleBackVisual()
    {
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
        {
            var parsed = ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = ((DocsCanvas)_services)._doc.GetBlockText(((DocsCanvas)_services)._doc.CursorBlock);
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (((DocsCanvas)_services)._doc.CursorOffset > s && ((DocsCanvas)_services)._doc.CursorOffset <= e)
                        break;
                    if (((DocsCanvas)_services)._doc.CursorOffset <= s)
                        return false;
                }
            }
        }

        SkipBackspacePastHiddenVisual();
        if (((DocsCanvas)_services)._doc.CursorOffset == 0 && ((DocsCanvas)_services)._doc.CursorBlock > 0 && ((DocsCanvas)_services)._parsedBlocks != null)
        {
            if (((DocsCanvas)_services)._doc.CursorBlock - 1 < ((DocsCanvas)_services)._parsedBlocks.Count && ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock - 1].IsSkippedInVisual)
                return false;
            if (((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && (IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]) || ((((DocsCanvas)_services)._doc.CursorBlock - 1 >= 0) && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock - 1]))))
                return false;
        }

        int prevBlock = ((DocsCanvas)_services)._doc.CursorBlock;
        int prevOffset = ((DocsCanvas)_services)._doc.CursorOffset;
        ((DocsCanvas)_services)._doc.Backspace();
        bool changed = ((DocsCanvas)_services)._doc.CursorBlock != prevBlock || ((DocsCanvas)_services)._doc.CursorOffset != prevOffset;
        if (changed) ((DocsCanvas)_services)._doc.CollapseSelection();

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: false);
        return changed;
    }

    internal bool HandleDeleteVisual()
    {
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
        {
            var parsed = ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = ((DocsCanvas)_services)._doc.GetBlockText(((DocsCanvas)_services)._doc.CursorBlock);
                bool canDelete = false;
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (((DocsCanvas)_services)._doc.CursorOffset >= s && ((DocsCanvas)_services)._doc.CursorOffset < e)
                    { canDelete = true; break; }
                }
                if (!canDelete) return false;
            }
        }

        SkipDeletePastHiddenVisual();
        if (((DocsCanvas)_services)._doc.CursorOffset >= ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock) &&
            ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._doc.BlockCount - 1 && ((DocsCanvas)_services)._parsedBlocks != null)
        {
            if (((DocsCanvas)_services)._doc.CursorBlock + 1 < ((DocsCanvas)_services)._parsedBlocks.Count && ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock + 1].IsSkippedInVisual)
                return false;
            if (((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && (IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]) || ((((DocsCanvas)_services)._doc.CursorBlock + 1 < ((DocsCanvas)_services)._parsedBlocks.Count) && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock + 1]))))
                return false;
        }

        int prevBlocks = ((DocsCanvas)_services)._doc.BlockCount;
        int prevLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
        ((DocsCanvas)_services)._doc.Delete();
        bool changed = ((DocsCanvas)_services)._doc.BlockCount != prevBlocks ||
                       ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock) != prevLen;

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: true);
        return changed;
    }

    internal void HandleLeftVisual(bool shift)
    {
        if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
        {
            var (sb, so, _, _) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
            ((DocsCanvas)_services)._doc.CursorBlock = sb;
            ((DocsCanvas)_services)._doc.CursorOffset = so;
            ((DocsCanvas)_services)._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: false);
        }
        else if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && HandleTableArrow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock], forward: false))
        {
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }
        else
        {
            int origBlock = ((DocsCanvas)_services)._doc.CursorBlock;
            int origOffset = ((DocsCanvas)_services)._doc.CursorOffset;
            ((DocsCanvas)_services)._doc.MoveLeft();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock].IsSkippedInVisual)
            {
                ((DocsCanvas)_services)._doc.CursorBlock = origBlock;
                ((DocsCanvas)_services)._doc.CursorOffset = origOffset;
            }
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
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
        if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
        {
            var (_, _, eb, eo) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
            ((DocsCanvas)_services)._doc.CursorBlock = eb;
            ((DocsCanvas)_services)._doc.CursorOffset = eo;
            ((DocsCanvas)_services)._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: true);
        }
        else if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && HandleTableArrow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock], forward: true))
        {
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }
        else
        {
            int origBlock = ((DocsCanvas)_services)._doc.CursorBlock;
            int origOffset = ((DocsCanvas)_services)._doc.CursorOffset;
            ((DocsCanvas)_services)._doc.MoveRight();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock].IsSkippedInVisual)
            {
                ((DocsCanvas)_services)._doc.CursorBlock = origBlock;
                ((DocsCanvas)_services)._doc.CursorOffset = origOffset;
            }
            if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
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
        string blockText = ((DocsCanvas)_services)._doc.GetBlockText(((DocsCanvas)_services)._doc.CursorBlock);
        var cells = parsed.TableRow.Cells;

        // find the trimmed content range for each cell
        var cellRanges = new List<(int Start, int End)>();
        foreach (var cell in cells)
            cellRanges.Add(cell.TrimContent(blockText));

        int offset = ((DocsCanvas)_services)._doc.CursorOffset;

        if (forward)
        {
            // find which cell the cursor is in or between
            for (int c = 0; c < cellRanges.Count; c++)
            {
                var (cs, ce) = cellRanges[c];
                if (offset < ce)
                {
                    // cursor is within this cell's content — move right by 1
                    ((DocsCanvas)_services)._doc.CursorOffset = offset + 1;
                    return true;
                }
                if (offset == ce)
                {
                    // cursor is at end of this cell — jump to start of next cell
                    if (c + 1 < cellRanges.Count)
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = cellRanges[c + 1].Start;
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
                ((DocsCanvas)_services)._doc.CursorOffset = cellRanges[^1].End;
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
                    ((DocsCanvas)_services)._doc.CursorOffset = offset - 1;
                    return true;
                }
                if (offset == cs)
                {
                    // cursor is at start of this cell — jump to end of previous cell
                    if (c > 0)
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = cellRanges[c - 1].End;
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
                ((DocsCanvas)_services)._doc.CursorOffset = cellRanges[0].Start;
            return true;
        }
    }

    private bool MoveToAdjacentTableRow(ParsedBlock parsed, bool forward)
    {
        if (((DocsCanvas)_services)._parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = ((DocsCanvas)_services)._doc.CursorBlock + 1; b < ((DocsCanvas)_services)._doc.BlockCount; b++)
            {
                var p = ((DocsCanvas)_services)._parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = b;
                    string text = ((DocsCanvas)_services)._doc.GetBlockText(b);
                    var firstCell = p.TableRow.Cells[0];
                    int s = firstCell.Start;
                    while (s < firstCell.Start + firstCell.Length && text[s] == ' ') s++;
                    ((DocsCanvas)_services)._doc.CursorOffset = s;
                    return true;
                }
            }
        }
        else
        {
            for (int b = ((DocsCanvas)_services)._doc.CursorBlock - 1; b >= 0; b--)
            {
                var p = ((DocsCanvas)_services)._parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = b;
                    string text = ((DocsCanvas)_services)._doc.GetBlockText(b);
                    var lastCell = p.TableRow.Cells[^1];
                    int e = lastCell.Start + lastCell.Length;
                    while (e > lastCell.Start && text[e - 1] == ' ') e--;
                    ((DocsCanvas)_services)._doc.CursorOffset = e;
                    return true;
                }
            }
        }
        return false;
    }

    private bool MoveOutOfTable(ParsedBlock parsed, bool forward)
    {
        if (((DocsCanvas)_services)._parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = ((DocsCanvas)_services)._doc.CursorBlock + 1; b < ((DocsCanvas)_services)._doc.BlockCount; b++)
            {
                if (((DocsCanvas)_services)._parsedBlocks[b].Table != parsed.Table)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = b;
                    ((DocsCanvas)_services)._doc.CursorOffset = 0;
                    SkipCursorOverHiddenRanges(forward: true);
                    return true;
                }
            }
        }
        else
        {
            for (int b = ((DocsCanvas)_services)._doc.CursorBlock - 1; b >= 0; b--)
            {
                if (((DocsCanvas)_services)._parsedBlocks[b].Table != parsed.Table)
                {
                    ((DocsCanvas)_services)._doc.CursorBlock = b;
                    ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(b);
                    SkipCursorOverHiddenRanges(forward: false);
                    return true;
                }
            }
        }
        return false;
    }

    private void CrossToPreviousBlockIfHiddenStart()
    {
        if (((DocsCanvas)_services)._doc.CursorOffset != 0 || ((DocsCanvas)_services)._doc.CursorBlock == 0) return;
        if (((DocsCanvas)_services)._visualMaps == null || ((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        if (!((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock].IsHidden(0)) return;
        if (((DocsCanvas)_services)._parsedBlocks != null && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock])) return;

        ((DocsCanvas)_services)._doc.CursorBlock--;
        ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
        EnsureCursorOnVisibleBlock(preferForward: false);
        SkipCursorOverHiddenRanges(forward: false);
    }

    private void CrossToNextBlockIfHiddenEnd()
    {
        int blockLen = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
        if (((DocsCanvas)_services)._doc.CursorOffset != blockLen || blockLen == 0) return;
        if (((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._doc.BlockCount - 1) return;
        if (((DocsCanvas)_services)._visualMaps == null || ((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._visualMaps.Count) return;
        if (!((DocsCanvas)_services)._visualMaps[((DocsCanvas)_services)._doc.CursorBlock].IsHidden(blockLen - 1)) return;
        if (((DocsCanvas)_services)._parsedBlocks != null && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock])) return;

        ((DocsCanvas)_services)._doc.CursorBlock++;
        ((DocsCanvas)_services)._doc.CursorOffset = 0;
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
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: false);
    }

    internal void HandleDownVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: true);
        if (((DocsCanvas)_services)._parsedBlocks != null && ((DocsCanvas)_services)._doc.CursorBlock < ((DocsCanvas)_services)._parsedBlocks.Count && IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: true);
    }

    internal void ClampCursorToTableCell()
    {
        if (((DocsCanvas)_services)._parsedBlocks == null) return;
        var parsed = ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock];
        if (parsed.TableRow == null) return;
        string blockText = ((DocsCanvas)_services)._doc.GetBlockText(((DocsCanvas)_services)._doc.CursorBlock);
        int offset = ((DocsCanvas)_services)._doc.CursorOffset;

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
        ((DocsCanvas)_services)._doc.CursorOffset = best;
    }

    // --- Helper ---

    private bool IsTableRow(ParsedBlock parsed)
        => parsed.IsTableSeparator || parsed.TableRow != null;
}
