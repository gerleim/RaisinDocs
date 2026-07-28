using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    // --- Visual mode: task list checkbox toggle ---

    private static void GetLinkTextRange(InlineLink link, out int textStart, out int textEnd)
    {
        bool isAutolink = link.Text == link.Url;
        textStart = isAutolink ? link.Start : link.Start + 1;
        textEnd = textStart + link.Text.Length;
    }

    private bool IsLinkHit(InlineLink link, int offset)
    {
        if (IsVisual)
        {
            GetLinkTextRange(link, out int textStart, out int textEnd);
            return offset >= textStart && offset < textEnd;
        }
        return offset >= link.Start && offset < link.Start + link.Length;
    }

    private bool TryOpenLinkAtClick(Point pos)
    {
        if (_parsedBlocks == null) return false;

        ComputeLayout();
        HitTestToPosition(pos, out int block, out int offset);
        if (block >= _parsedBlocks.Count) return false;

        var parsed = _parsedBlocks[block];
        if (parsed.Links == null) return false;

        foreach (var link in parsed.Links)
        {
            if (IsLinkHit(link, offset))
            {
                var url = link.Url;
                if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://"))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                }
                return true;
            }
        }
        return false;
    }

    private InlineLink? GetLinkAtPosition(Point pos)
    {
        if (_parsedBlocks == null) return null;

        HitTestToPosition(pos, out int block, out int offset);
        if (block >= _parsedBlocks.Count) return null;

        var parsed = _parsedBlocks[block];
        if (parsed.Links == null) return null;

        foreach (var link in parsed.Links)
        {
            if (IsLinkHit(link, offset))
                return link;
        }
        return null;
    }

    private bool TryToggleTaskListCheckbox(Point pos)
    {
        if (_parsedBlocks == null) return false;

        double effectiveScroll = _scroll.EffectiveOffset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _visualLines[vli];
        if (vl.StartOffset != 0) return false;

        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Kind is not (BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked))
            return false;

        double nestOff = parsed.ListNestingLevel * BlockVisualMap.SpacesPerNestingLevel
            * _measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);
        if (pos.X > _padding + nestOff + _measure.ListIndent)
            return false;

        SealAndStopTimer();
        _doc.BeginUndoGroup();
        char newChar = parsed.Kind == BlockKind.TaskListItemChecked ? ' ' : 'x';
        int checkCharOffset = parsed.LeadingSpaces + 3;
        _doc.RemoveTextAt(vl.BlockIndex, checkCharOffset, 1);
        _doc.InsertTextAt(vl.BlockIndex, checkCharOffset, newChar.ToString());
        _doc.SealUndoGroup();

        IsDirty = !_doc.IsClean;
        InvalidateLayout();
        return true;
    }

    // --- Visual mode: cursor helpers ---

    private void SkipCursorOverHiddenRanges(bool forward)
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        var map = _visualMaps[_doc.CursorBlock];
        int offset = _doc.CursorOffset;
        if (forward)
        {
            int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
            offset = map.SkipHidden(offset, true);
            while (offset < blockLen && map.IsHidden(offset))
                offset++;
        }
        else
        {
            offset = map.SkipHidden(offset, false);
            while (offset > 0 && map.IsHidden(offset))
                offset--;
        }
        _doc.CursorOffset = offset;
    }

    private void SkipCursorToVisible(bool forward)
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        var map = _visualMaps[_doc.CursorBlock];
        int offset = _doc.CursorOffset;
        if (forward)
        {
            int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
            while (offset < blockLen && map.IsHidden(offset)) offset++;
        }
        else
        {
            while (offset > 0 && map.IsHidden(offset - 1)) offset--;
        }
        _doc.CursorOffset = offset;
    }

    private void ClampCursorBeforeTrailingHidden()
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        if (_parsedBlocks != null && _doc.CursorBlock < _parsedBlocks.Count
            && IsTableRow(_parsedBlocks[_doc.CursorBlock])) return;
        var map = _visualMaps[_doc.CursorBlock];
        int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
        if (blockLen == 0 || !map.IsHidden(blockLen - 1)) return;
        int offset = _doc.CursorOffset;
        int minOffset = 0;
        if (map.HiddenRanges.Count > 0 && map.HiddenRanges[0].Start == 0)
            minOffset = map.HiddenRanges[0].Length;
        while (offset > minOffset && map.IsHidden(offset - 1))
            offset--;
        _doc.CursorOffset = offset;
    }

    private void SkipBackspacePastHiddenVisual()
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        var map = _visualMaps[_doc.CursorBlock];
        int pos = _doc.CursorOffset - 1;
        while (pos >= 0 && map.IsHidden(pos)) pos--;
        if (pos >= 0)
            _doc.CursorOffset = pos + 1;
    }

    private void SkipDeletePastHiddenVisual()
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        var map = _visualMaps[_doc.CursorBlock];
        int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
        int pos = _doc.CursorOffset;
        while (pos < blockLen && map.IsHidden(pos)) pos++;
        _doc.CursorOffset = pos;
    }

    private void EnsureCursorOnVisibleBlock(bool? preferForward = null)
    {
        if (_parsedBlocks == null || _doc.CursorBlock >= _parsedBlocks.Count) return;
        if (!_parsedBlocks[_doc.CursorBlock].IsSkippedInVisual) return;

        bool forward = preferForward ?? true;
        int limit = Math.Min(_doc.BlockCount, _parsedBlocks.Count);

        if (forward)
        {
            for (int i = _doc.CursorBlock + 1; i < limit; i++)
            {
                if (!_parsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.CursorBlock = i;
                    _doc.CursorOffset = 0;
                    return;
                }
            }
        }
        else
        {
            for (int i = _doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!_parsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.CursorBlock = i;
                    _doc.CursorOffset = _doc.GetBlockLength(i);
                    return;
                }
            }
        }

        if (preferForward != null) return;

        if (forward)
        {
            for (int i = _doc.CursorBlock - 1; i >= 0; i--)
            {
                if (!_parsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.CursorBlock = i;
                    _doc.CursorOffset = _doc.GetBlockLength(i);
                    return;
                }
            }
        }
        else
        {
            for (int i = _doc.CursorBlock + 1; i < limit; i++)
            {
                if (!_parsedBlocks[i].IsSkippedInVisual)
                {
                    _doc.CursorBlock = i;
                    _doc.CursorOffset = 0;
                    return;
                }
            }
        }
    }

    // --- Visual mode: key handlers ---

    private bool HandleBackVisual()
    {
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
        {
            var parsed = _parsedBlocks[_doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _doc.GetBlockText(_doc.CursorBlock);
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_doc.CursorOffset > s && _doc.CursorOffset <= e)
                        break;
                    if (_doc.CursorOffset <= s)
                        return false;
                }
            }
        }

        SkipBackspacePastHiddenVisual();
        if (_doc.CursorOffset == 0 && _doc.CursorBlock > 0 && _parsedBlocks != null)
        {
            if (_parsedBlocks[_doc.CursorBlock - 1].IsSkippedInVisual)
                return false;
            if (IsTableRow(_parsedBlocks[_doc.CursorBlock]) || IsTableRow(_parsedBlocks[_doc.CursorBlock - 1]))
                return false;
        }

        int prevBlock = _doc.CursorBlock;
        int prevOffset = _doc.CursorOffset;
        _doc.Backspace();
        bool changed = _doc.CursorBlock != prevBlock || _doc.CursorOffset != prevOffset;
        if (changed) _doc.CollapseSelection();

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: false);
        return changed;
    }

    private bool HandleDeleteVisual()
    {
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
        {
            var parsed = _parsedBlocks[_doc.CursorBlock];
            if (parsed.TableRow != null)
            {
                string blockText = _doc.GetBlockText(_doc.CursorBlock);
                bool canDelete = false;
                foreach (var cell in parsed.TableRow.Cells)
                {
                    var (s, e) = cell.TrimContent(blockText);
                    if (_doc.CursorOffset >= s && _doc.CursorOffset < e)
                    { canDelete = true; break; }
                }
                if (!canDelete) return false;
            }
        }

        SkipDeletePastHiddenVisual();
        if (_doc.CursorOffset >= _doc.GetBlockLength(_doc.CursorBlock) &&
            _doc.CursorBlock < _doc.BlockCount - 1 && _parsedBlocks != null)
        {
            if (_parsedBlocks[_doc.CursorBlock + 1].IsSkippedInVisual)
                return false;
            if (IsTableRow(_parsedBlocks[_doc.CursorBlock]) || IsTableRow(_parsedBlocks[_doc.CursorBlock + 1]))
                return false;
        }

        int prevBlocks = _doc.BlockCount;
        int prevLen = _doc.GetBlockLength(_doc.CursorBlock);
        _doc.Delete();
        bool changed = _doc.BlockCount != prevBlocks ||
                       _doc.GetBlockLength(_doc.CursorBlock) != prevLen;

        EnsureCursorOnVisibleBlock();
        SkipCursorOverHiddenRanges(forward: true);
        return changed;
    }

    private void HandleLeftVisual(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (sb, so, _, _) = _doc.GetOrderedSelection();
            _doc.CursorBlock = sb;
            _doc.CursorOffset = so;
            _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: false);
        }
        else if (_parsedBlocks != null && HandleTableArrow(_parsedBlocks[_doc.CursorBlock], forward: false))
        {
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            int origBlock = _doc.CursorBlock;
            int origOffset = _doc.CursorOffset;
            _doc.MoveLeft();
            if (!shift) _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_parsedBlocks != null && _parsedBlocks[_doc.CursorBlock].IsSkippedInVisual)
            {
                _doc.CursorBlock = origBlock;
                _doc.CursorOffset = origOffset;
            }
            if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                ClampCursorToTableCell();
            else
            {
                SkipCursorOverHiddenRanges(forward: false);
                CrossToPreviousBlockIfHiddenStart();
            }
        }
    }

    private void HandleRightVisual(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (_, _, eb, eo) = _doc.GetOrderedSelection();
            _doc.CursorBlock = eb;
            _doc.CursorOffset = eo;
            _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                ClampCursorToTableCell();
            else
                SkipCursorOverHiddenRanges(forward: true);
        }
        else if (_parsedBlocks != null && HandleTableArrow(_parsedBlocks[_doc.CursorBlock], forward: true))
        {
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            int origBlock = _doc.CursorBlock;
            int origOffset = _doc.CursorOffset;
            _doc.MoveRight();
            if (!shift) _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_parsedBlocks != null && _parsedBlocks[_doc.CursorBlock].IsSkippedInVisual)
            {
                _doc.CursorBlock = origBlock;
                _doc.CursorOffset = origOffset;
            }
            if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                ClampCursorToTableCell();
            else
            {
                SkipCursorOverHiddenRanges(forward: true);
                CrossToNextBlockIfHiddenEnd();
            }
        }
    }

    private bool HandleTableArrow(ParsedBlock parsed, bool forward)
    {
        if (parsed.TableRow == null) return false;
        string blockText = _doc.GetBlockText(_doc.CursorBlock);
        var cells = parsed.TableRow.Cells;

        // find the trimmed content range for each cell
        var cellRanges = new List<(int Start, int End)>();
        foreach (var cell in cells)
            cellRanges.Add(cell.TrimContent(blockText));

        int offset = _doc.CursorOffset;

        if (forward)
        {
            // find which cell the cursor is in or between
            for (int c = 0; c < cellRanges.Count; c++)
            {
                var (cs, ce) = cellRanges[c];
                if (offset < ce)
                {
                    // cursor is within this cell's content — move right by 1
                    _doc.CursorOffset = offset + 1;
                    return true;
                }
                if (offset == ce)
                {
                    // cursor is at end of this cell — jump to start of next cell
                    if (c + 1 < cellRanges.Count)
                    {
                        _doc.CursorOffset = cellRanges[c + 1].Start;
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
                _doc.CursorOffset = cellRanges[^1].End;
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
                    _doc.CursorOffset = offset - 1;
                    return true;
                }
                if (offset == cs)
                {
                    // cursor is at start of this cell — jump to end of previous cell
                    if (c > 0)
                    {
                        _doc.CursorOffset = cellRanges[c - 1].End;
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
                _doc.CursorOffset = cellRanges[0].Start;
            return true;
        }
    }

    private bool MoveToAdjacentTableRow(ParsedBlock parsed, bool forward)
    {
        if (_parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _doc.CursorBlock + 1; b < _doc.BlockCount; b++)
            {
                var p = _parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _doc.CursorBlock = b;
                    string text = _doc.GetBlockText(b);
                    var firstCell = p.TableRow.Cells[0];
                    int s = firstCell.Start;
                    while (s < firstCell.Start + firstCell.Length && text[s] == ' ') s++;
                    _doc.CursorOffset = s;
                    return true;
                }
            }
        }
        else
        {
            for (int b = _doc.CursorBlock - 1; b >= 0; b--)
            {
                var p = _parsedBlocks[b];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator) continue;
                if (p.TableRow != null)
                {
                    _doc.CursorBlock = b;
                    string text = _doc.GetBlockText(b);
                    var lastCell = p.TableRow.Cells[^1];
                    int e = lastCell.Start + lastCell.Length;
                    while (e > lastCell.Start && text[e - 1] == ' ') e--;
                    _doc.CursorOffset = e;
                    return true;
                }
            }
        }
        return false;
    }

    private bool MoveOutOfTable(ParsedBlock parsed, bool forward)
    {
        if (_parsedBlocks == null || parsed.Table == null) return false;

        if (forward)
        {
            for (int b = _doc.CursorBlock + 1; b < _doc.BlockCount; b++)
            {
                if (_parsedBlocks[b].Table != parsed.Table)
                {
                    _doc.CursorBlock = b;
                    _doc.CursorOffset = 0;
                    SkipCursorOverHiddenRanges(forward: true);
                    return true;
                }
            }
        }
        else
        {
            for (int b = _doc.CursorBlock - 1; b >= 0; b--)
            {
                if (_parsedBlocks[b].Table != parsed.Table)
                {
                    _doc.CursorBlock = b;
                    _doc.CursorOffset = _doc.GetBlockLength(b);
                    SkipCursorOverHiddenRanges(forward: false);
                    return true;
                }
            }
        }
        return false;
    }

    private void CrossToPreviousBlockIfHiddenStart()
    {
        if (_doc.CursorOffset != 0 || _doc.CursorBlock == 0) return;
        if (_visualMaps == null || _doc.CursorBlock >= _visualMaps.Count) return;
        if (!_visualMaps[_doc.CursorBlock].IsHidden(0)) return;
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock])) return;

        _doc.CursorBlock--;
        _doc.CursorOffset = _doc.GetBlockLength(_doc.CursorBlock);
        EnsureCursorOnVisibleBlock(preferForward: false);
        SkipCursorOverHiddenRanges(forward: false);
    }

    private void CrossToNextBlockIfHiddenEnd()
    {
        int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
        if (_doc.CursorOffset != blockLen || blockLen == 0) return;
        if (_doc.CursorBlock >= _doc.BlockCount - 1) return;
        if (_visualMaps == null || _doc.CursorBlock >= _visualMaps.Count) return;
        if (!_visualMaps[_doc.CursorBlock].IsHidden(blockLen - 1)) return;
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock])) return;

        _doc.CursorBlock++;
        _doc.CursorOffset = 0;
        EnsureCursorOnVisibleBlock(preferForward: true);
        SkipCursorOverHiddenRanges(forward: true);
    }

    private void HandleHomeVisual()
    {
        EnsureCursorOnVisibleBlock();
        SkipCursorToVisible(forward: true);
    }

    private void HandleEndVisual()
    {
        EnsureCursorOnVisibleBlock();
        SkipCursorToVisible(forward: false);
    }

    private void HandleUpVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: false);
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: false);
    }

    private void HandleDownVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: true);
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
            ClampCursorToTableCell();
        else
            SkipCursorOverHiddenRanges(forward: true);
    }

    // --- Visual mode: rectangular table selection ---

    private void DrawTableRectSelection(DrawingContext dc, double effectiveScroll,
        int startCol, int endCol, int startBlock, int endBlock, TableInfo table)
    {
        if (!_tableColumnWidths.TryGetValue(table, out var colWidths)) return;

        double xStart = 0;
        for (int c = 0; c < startCol && c < colWidths.Length; c++)
            xStart += colWidths[c];
        double xEnd = xStart;
        for (int c = startCol; c <= endCol && c < colWidths.Length; c++)
            xEnd += colWidths[c];

        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.BlockIndex < startBlock || vl.BlockIndex > endBlock) continue;
            var parsed = _parsedBlocks![vl.BlockIndex];
            if (parsed.IsTableSeparator) continue;

            double lineY = _lineYPositions[i];
            double lineH = GetEffectiveLineHeight(vl);
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            dc.DrawRectangle(_palette.Selection, null,
                new Rect(_padding + xStart, lineY - effectiveScroll, xEnd - xStart, lineH));
        }
    }

    private string GetTableRectSelectedText(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        var lines = new List<string>();
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;

            string blockText = _doc.GetBlockText(b);
            var cells = parsed.TableRow.Cells;
            var cellTexts = new List<string>();
            for (int c = rect.StartCol; c <= rect.EndCol && c < cells.Count; c++)
            {
                var cell = cells[c];
                cellTexts.Add(blockText.Substring(cell.Start, cell.Length).Trim());
            }
            lines.Add("| " + string.Join(" | ", cellTexts) + " |");
        }
        return string.Join("\r\n", lines);
    }

    private void ClearTableRectCells(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;

            var cells = parsed.TableRow.Cells;
            for (int c = Math.Min(rect.EndCol, cells.Count - 1); c >= rect.StartCol; c--)
            {
                var cell = cells[c];
                _doc.RemoveTextAt(b, cell.Start, cell.Length);
                _doc.InsertTextAt(b, cell.Start, "  ");
            }
        }
        _doc.CollapseSelection();
    }

    private void MoveCursorToRectStart(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;
            if (rect.StartCol < parsed.TableRow.Cells.Count)
            {
                var cell = parsed.TableRow.Cells[rect.StartCol];
                string blockText = _doc.GetBlockText(b);
                var (trimStart, _) = cell.TrimContent(blockText);
                _doc.CursorBlock = b;
                _doc.CursorOffset = trimStart;
                _doc.CollapseSelection();
                return;
            }
        }
    }

    private bool TryPasteIntoTableCells(string pasteText)
    {
        if (!IsVisual || _parsedBlocks == null) return false;

        var cursorParsed = _parsedBlocks[_doc.CursorBlock];
        if (cursorParsed.Table == null || cursorParsed.TableRow == null) return false;

        var pasteLines = pasteText.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (pasteLines.Length == 0) return false;

        foreach (var line in pasteLines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
                return false;
        }

        int startCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.CursorOffset);
        int destBlock = _doc.CursorBlock;
        int lastBlock = destBlock;
        int lastOffset = _doc.CursorOffset;

        foreach (var line in pasteLines)
        {
            while (destBlock < _doc.BlockCount)
            {
                var dp = _parsedBlocks[destBlock];
                if (dp.Table == cursorParsed.Table && dp.TableRow != null && !dp.IsTableSeparator)
                    break;
                destBlock++;
            }
            if (destBlock >= _doc.BlockCount) break;

            var destParsed = _parsedBlocks[destBlock];
            if (destParsed.Table != cursorParsed.Table || destParsed.TableRow == null) break;

            var srcCells = ParseTableLineCells(line);
            var destCells = destParsed.TableRow.Cells;

            int lastColThisRow = -1;
            for (int i = srcCells.Count - 1; i >= 0; i--)
            {
                int destCol = startCol + i;
                if (destCol >= destCells.Count) continue;

                var dc = destCells[destCol];
                string replacement = " " + srcCells[i] + " ";
                _doc.RemoveTextAt(destBlock, dc.Start, dc.Length);
                _doc.InsertTextAt(destBlock, dc.Start, replacement);

                if (lastColThisRow < 0)
                {
                    lastColThisRow = destCol;
                    lastBlock = destBlock;
                }
            }
            if (lastColThisRow >= 0)
            {
                string updated = _doc.GetBlockText(destBlock);
                int pipe = 0;
                int pos = 0;
                while (pos < updated.Length && pipe <= lastColThisRow)
                {
                    if (updated[pos] == '|') pipe++;
                    pos++;
                }
                int cellEnd = updated.IndexOf('|', pos);
                lastOffset = cellEnd >= 0 ? cellEnd : updated.Length;
            }

            destBlock++;
        }

        _doc.CursorBlock = lastBlock;
        _doc.CursorOffset = lastOffset;
        _doc.CollapseSelection();
        return true;
    }

    private static List<string> ParseTableLineCells(string line)
    {
        var cells = new List<string>();
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        foreach (var part in trimmed.Split('|'))
            cells.Add(part.Trim());
        return cells;
    }

    private static int FindCellIndexAtOffset(IReadOnlyList<TableCellInfo> cells, int offset)
    {
        for (int c = 0; c < cells.Count; c++)
        {
            if (offset <= cells[c].Start + cells[c].Length)
                return c;
        }
        return cells.Count - 1;
    }

    private (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)?
        TryGetTableRectSelection()
    {
        if (!IsVisual || _parsedBlocks == null || !_doc.HasSelection) return null;

        var anchorParsed = _parsedBlocks[_doc.AnchorBlock];
        var cursorParsed = _parsedBlocks[_doc.CursorBlock];

        if (anchorParsed.Table == null || cursorParsed.Table == null) return null;
        if (anchorParsed.Table != cursorParsed.Table) return null;
        if (anchorParsed.TableRow == null || cursorParsed.TableRow == null) return null;

        int anchorCol = FindCellIndexAtOffset(anchorParsed.TableRow.Cells, _doc.AnchorOffset);
        int cursorCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.CursorOffset);

        if (_doc.AnchorBlock == _doc.CursorBlock && anchorCol == cursorCol)
            return null;

        return (
            Math.Min(anchorCol, cursorCol),
            Math.Max(anchorCol, cursorCol),
            Math.Min(_doc.AnchorBlock, _doc.CursorBlock),
            Math.Max(_doc.AnchorBlock, _doc.CursorBlock),
            anchorParsed.Table
        );
    }

    private double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = _doc.GetBlockText(blockIndex);
        BlockVisualMap? map = (_visualMaps != null && blockIndex < _visualMaps.Count) ? _visualMaps[blockIndex] : null;

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
                double fontSize = _measure.GetBlockFontSize(parsed.Kind);
                var cellTypeface = isHeader ? TextMeasurer.BoldTypeface : TextMeasurer.GetBlockBaseTypeface(parsed.Kind);

                var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, cellTypeface, fontSize,
                    _palette.Foreground, _measure.DpiScale);

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
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - ft.Width) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - ft.Width),
                    _ => 0,
                };
                return x + _tableCellPadding + alignOffset + textW;
            }
            x += colWidths[c];
        }
        return x;
    }

    private int HitTestInTableRow(VisualLine vl, ParsedBlock parsed, double[] colWidths, double x)
    {
        var cells = parsed.TableRow!.Cells;
        string blockText = _doc.GetBlockText(vl.BlockIndex);
        BlockVisualMap? map = (_visualMaps != null && vl.BlockIndex < _visualMaps.Count) ? _visualMaps[vl.BlockIndex] : null;
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
                        fullTextW += _measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
                    }
                }
                else
                {
                    string cellContent = blockText.Substring(trimStart, trimEnd - trimStart);
                    fullTextW = _measure.MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, trimStart);
                }

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
                    _ => 0,
                };

                double localX = x - cx - _tableCellPadding - alignOffset;
                double accum = 0;
                int runIdx = 0;

                if (map != null)
                {
                    for (int rawI = trimStart; rawI < trimEnd; rawI++)
                    {
                        if (map.IsHidden(rawI)) continue;
                        var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawI, ref runIdx);
                        double charW = _measure.MeasureCharWidth(blockText[rawI], parsed.Kind, style);
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
                        double charW = _measure.MeasureCharWidth(cellContent[i], parsed.Kind, style);
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

    private void ComputeAllTableColumnWidths(double maxWidth)
    {
        var seen = new HashSet<TableInfo>();
        for (int bi = 0; bi < _doc.BlockCount; bi++)
        {
            var parsed = _parsedBlocks![bi];
            if (parsed.Table == null || parsed.TableRow == null) continue;
            if (!seen.Add(parsed.Table)) continue;

            int colCount = parsed.Table.ColumnCount;
            var widths = new double[colCount];

            for (int bj = bi; bj < _doc.BlockCount; bj++)
            {
                var p = _parsedBlocks[bj];
                if (p.Table != parsed.Table) break;
                if (p.IsTableSeparator || p.TableRow == null) continue;

                string text = _doc.GetBlockText(bj);
                BlockVisualMap? map = (_visualMaps != null && bj < _visualMaps.Count) ? _visualMaps[bj] : null;
                for (int c = 0; c < Math.Min(p.TableRow.Cells.Count, colCount); c++)
                {
                    var cell = p.TableRow.Cells[c];
                    int s = cell.Start;
                    int e = s + cell.Length;
                    while (s < e && text[s] == ' ') s++;
                    while (e > s && text[e - 1] == ' ') e--;
                    string cellText = map != null
                        ? map.BuildDisplayString(text, s, e - s)
                        : text.Substring(s, e - s);
                    double w = _measure.MeasureStringWidth(cellText, p.Kind, p.Runs, s);
                    if (w > widths[c]) widths[c] = w;
                }
            }

            for (int c = 0; c < colCount; c++)
                widths[c] += _tableCellPadding * 2;

            _tableColumnWidths[parsed.Table] = widths;
        }
    }

    private void DrawTableBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        int i = 0;
        while (i < _visualLines.Count)
        {
            var vl = _visualLines[i];
            // Safety check: skip if block index is out of range (can happen after merging)
            if (_parsedBlocks == null || vl.BlockIndex >= _parsedBlocks.Count)
            {
                i++;
                continue;
            }
            var parsed = _parsedBlocks[vl.BlockIndex];
            if (parsed.Table == null || parsed.Kind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
            {
                i++;
                continue;
            }

            var tableInfo = parsed.Table;
            int tableStart = i;
            int tableEnd = i;
            while (tableEnd < _visualLines.Count)
            {
                var p = _parsedBlocks[_visualLines[tableEnd].BlockIndex];
                if (p.Table != tableInfo) break;
                tableEnd++;
            }

            double tableY = _lineYPositions[tableStart];
            double tableBottom = tableEnd > 0
                ? _lineYPositions[tableEnd - 1] + GetEffectiveLineHeight(_visualLines[tableEnd - 1])
                : tableY;

            if (tableBottom >= viewTop && tableY <= viewBottom
                && _tableColumnWidths.TryGetValue(tableInfo, out var colWidths))
            {
                double tableWidth = 0;
                foreach (var w in colWidths) tableWidth += w;
                double tableX = _padding;
                double yTop = tableY - effectiveScroll;
                double tableH = tableBottom - tableY;

                dc.DrawRectangle(_palette.TableBackground, null,
                    new Rect(tableX, yTop, tableWidth, tableH));

                double headerH = _measure.GetLineHeight(_visualLines[tableStart].BlockKind);
                dc.DrawRectangle(_palette.TableHeaderBackground, null,
                    new Rect(tableX, yTop, tableWidth, headerH));

                dc.DrawRectangle(null, _palette.TableBorderPen,
                    new Rect(tableX, yTop, tableWidth, tableH));

                for (int row = tableStart; row < tableEnd; row++)
                {
                    double rowY = _lineYPositions[row] - effectiveScroll;
                    if (row > tableStart)
                        dc.DrawLine(_palette.TableBorderPen,
                            new Point(tableX, rowY), new Point(tableX + tableWidth, rowY));
                }

                double cx = tableX;
                for (int c = 0; c < colWidths.Length - 1; c++)
                {
                    cx += colWidths[c];
                    dc.DrawLine(_palette.TableBorderPen,
                        new Point(cx, yTop), new Point(cx, yTop + tableH));
                }
            }

            i = tableEnd;
        }
    }

    private void DrawTableRow(DrawingContext dc, VisualLine vl, string blockText,
        ParsedBlock parsed, double lineY, double effectiveScroll,
        double fontSize, Typeface baseTypeface)
    {
        if (parsed.TableRow == null || parsed.Table == null) return;
        if (!_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths)) return;

        BlockVisualMap? map = null;
        if (_visualMaps != null && vl.BlockIndex < _visualMaps.Count)
            map = _visualMaps[vl.BlockIndex];

        double x = _padding;
        double y = lineY - effectiveScroll;
        double lineH = _measure.GetLineHeight(vl.BlockKind);
        bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;

        for (int c = 0; c < Math.Min(parsed.TableRow.Cells.Count, colWidths.Length); c++)
        {
            var cell = parsed.TableRow.Cells[c];
            var (s, e) = cell.TrimContent(blockText);

            string cellText = map != null
                ? map.BuildDisplayString(blockText, s, e - s)
                : blockText.Substring(s, e - s);
            if (cellText.Length == 0) { x += colWidths[c]; continue; }

            var cellTypeface = isHeader ? TextMeasurer.BoldTypeface : baseTypeface;
            var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, cellTypeface, fontSize,
                _palette.Foreground, _measure.DpiScale);

            if (map != null)
                ApplyInlineStylesForCell(ft, parsed, map, s, e);
            else
                ApplyInlineStylesForCellRaw(ft, cellText, parsed, s, e);

            var align = parsed.Table.Alignments[c];
            double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
            double textX;
            if (align == ColumnAlignment.Center)
                textX = x + _tableCellPadding + Math.Max(0, (cellContentWidth - ft.Width) / 2);
            else if (align == ColumnAlignment.Right)
                textX = x + _tableCellPadding + Math.Max(0, cellContentWidth - ft.Width);
            else
                textX = x + _tableCellPadding;

            var clipRect = new Rect(x, y, colWidths[c], lineH);
            dc.PushClip(new RectangleGeometry(clipRect));

            if (map?.ColorSpans != null)
            {
                foreach (var cs in map.ColorSpans)
                {
                    if (cs.Background == null) continue;
                    int csEnd = cs.Start + cs.Length;
                    if (csEnd <= s || cs.Start >= e) continue;

                    int rawStart = Math.Max(cs.Start, s);
                    int rawEnd = Math.Min(csEnd, e);
                    double bgX1 = MeasureRangeWidth(blockText, s, rawStart - s, parsed.Runs, parsed.Kind, map);
                    double bgX2 = MeasureRangeWidth(blockText, s, rawEnd - s, parsed.Runs, parsed.Kind, map);
                    if (bgX2 <= bgX1) continue;

                    var bg = cs.Background.Value;
                    var brush = new SolidColorBrush(Color.FromArgb(40, bg.R, bg.G, bg.B));
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, new Rect(textX + bgX1, y, bgX2 - bgX1, lineH));
                }
            }

            dc.DrawText(ft, new Point(textX, y));
            dc.Pop();

            x += colWidths[c];
        }
    }

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
                ft.SetForegroundBrush(_checkboxCheckedBrush, visStart, count);
                ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
            }
        }

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            if (ftLen > 0) ft.SetForegroundBrush(GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, ftLen);
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
                    ft.SetForegroundBrush(GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
                }
            }
        }
    }

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

    private void ClampCursorToTableCell()
    {
        if (_parsedBlocks == null) return;
        var parsed = _parsedBlocks[_doc.CursorBlock];
        if (parsed.TableRow == null) return;
        string blockText = _doc.GetBlockText(_doc.CursorBlock);
        int offset = _doc.CursorOffset;

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
            if (Math.Abs(offset - s) < bestDist) { best = s; bestDist = Math.Abs(offset - s); }
            if (Math.Abs(offset - e) < bestDist) { best = e; bestDist = Math.Abs(offset - e); }
        }
        _doc.CursorOffset = best;
    }

    // --- Visual mode: rendering ---

    private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        if (parsed.SyntaxTokens != null)
        {
            ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens, map);
            return;
        }

        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

            int rawStart = Math.Max(run.Start, vl.StartOffset);
            int rawEnd = Math.Min(runEnd, vlEnd);
            int visStart = map.RawToVisual(rawStart) - map.RawToVisual(vl.StartOffset);
            int visEnd = map.RawToVisual(rawEnd) - map.RawToVisual(vl.StartOffset);
            int count = visEnd - visStart;
            if (count <= 0) continue;

            switch (run.Style)
            {
                case InlineStyle.Bold:
                    ft.SetFontWeight(FontWeights.Bold, visStart, count);
                    break;
                case InlineStyle.Italic:
                    ft.SetFontStyle(FontStyles.Italic, visStart, count);
                    break;
                case InlineStyle.BoldItalic:
                    ft.SetFontWeight(FontWeights.Bold, visStart, count);
                    ft.SetFontStyle(FontStyles.Italic, visStart, count);
                    break;
                case InlineStyle.Code:
                    ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visStart, count);
                    break;
                case InlineStyle.Strikethrough:
                    ft.SetTextDecorations(TextDecorations.Strikethrough, visStart, count);
                    break;
                case InlineStyle.Link:
                    ft.SetForegroundBrush(_checkboxCheckedBrush, visStart, count);
                    ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
                    break;
            }
        }

        ApplyColorSpansVisual(ft, vl, parsed, map);
    }

    private void ApplyColorSpansVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
        int ftLen = ft.Text.Length;

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            int vlVisLen = Math.Min(ftLen, map.RawToVisual(vl.StartOffset + vl.Length) - map.RawToVisual(vl.StartOffset));
            if (vlVisLen > 0)
                ft.SetForegroundBrush(GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, vlVisLen);
        }

        var colorSpans = map.ColorSpans;
        if (colorSpans == null) return;

        int vlEnd = vl.StartOffset + vl.Length;
        int vlVisBase = map.RawToVisual(vl.StartOffset);

        foreach (var cs in colorSpans)
        {
            int csEnd = cs.Start + cs.Length;
            if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

            int rawStart = Math.Max(cs.Start, vl.StartOffset);
            int rawEnd = Math.Min(csEnd, vlEnd);
            int visStart = map.RawToVisual(rawStart) - vlVisBase;
            int visEnd = map.RawToVisual(rawEnd) - vlVisBase;
            visEnd = Math.Min(visEnd, ftLen);
            int count = visEnd - visStart;
            if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

            if (cs.Foreground is { } fg)
            {
                ft.SetForegroundBrush(GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
            }
        }
    }

    private bool HasImagesOnLine(VisualLine vl, BlockVisualMap map)
    {
        if (map.Images == null) return false;
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var img in map.Images)
        {
            if (img.Start >= vl.StartOffset && img.Start < vlEnd) return true;
            if (img.Start >= vlEnd) break;
        }
        return false;
    }

    private void DrawVisualLineWithImages(DrawingContext dc, VisualLine vl,
        string blockText, ParsedBlock parsed, BlockVisualMap map,
        double lineY, double effectiveScroll, double fontSize, Typeface baseTypeface)
    {
        if (map.Images == null) return;

        double x = _padding;
        double screenY = lineY - effectiveScroll;
        double textLineH = _measure.GetLineHeight(vl.BlockKind);
        double totalLineH = vl.OverrideHeight > textLineH ? vl.OverrideHeight : textLineH;

        if (map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            {
                double nestOff = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind)
                    - _measure.ListIndent;
                x += DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                    _padding, screenY, parsed.Kind, nestOff);
            }
            else if (parsed.Kind == BlockKind.UnorderedListItem)
            {
                double nestOff = _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind)
                    - _measure.ListIndent;
                x += DrawListBullet(dc, _padding, screenY,
                    parsed.Kind, parsed.ListNestingLevel, nestOff);
            }
            else if (map.IsContinuationIndent)
            {
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
            else
            {
                var prefixFt = new FormattedText(map.ReplacementPrefix,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    TextMeasurer.NormalTypeface, fontSize, _palette.Syntax, _measure.DpiScale);
                dc.DrawText(prefixFt, new Point(_padding, screenY));
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
        }

        int vlEnd = vl.StartOffset + vl.Length;
        int segStart = vl.StartOffset;

        foreach (var img in map.Images)
        {
            if (img.Start >= vlEnd) break;
            if (img.Start + img.Length <= vl.StartOffset) continue;

            if (segStart < img.Start)
                x = DrawTextSegment(dc, blockText, segStart, img.Start, map, parsed, fontSize, baseTypeface, x, screenY);

            var (imgW, imgH) = GetImageSize(img, _layoutMaxWidth);
            var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
            double imgY = screenY + totalLineH - imgH;
            if (cached != null)
            {
                dc.DrawImage(cached.Value.Image, new Rect(x, imgY, imgW, imgH));
            }
            else
            {
                DrawImagePlaceholder(dc, x, imgY, imgW, imgH, img.AltText);
            }
            x += imgW;

            segStart = img.Start + img.Length;
        }

        if (segStart < vlEnd)
            DrawTextSegment(dc, blockText, segStart, vlEnd, map, parsed, fontSize, baseTypeface, x, screenY);
    }

    private double DrawTaskListCheckbox(DrawingContext dc, bool isChecked, double x, double screenY,
        BlockKind blockKind, double nestingOffset = 0)
    {
        double lineH = _measure.GetLineHeight(blockKind);
        double boxSize = Math.Round(lineH * 0.65);
        double yOffset = Math.Round((lineH - boxSize) / 2);

        var aligner = new ContentBlockAligner(x, _measure.ListIndent);
        double checkboxX = aligner.CalculateMarkerXForSize(boxSize, nestingOffset);
        double checkboxY = screenY + yOffset;
        var rect = new Rect(checkboxX, checkboxY, boxSize, boxSize);
        double radius = 2.5;

        if (isChecked)
        {
            dc.DrawRoundedRectangle(_checkboxCheckedBrush, null, rect, radius, radius);
            var pen = new Pen(_palette.Background, 1.6);
            pen.Freeze();
            double cx = checkboxX, cy = checkboxY, s = boxSize;
            dc.DrawLine(pen,
                new Point(cx + s * 0.22, cy + s * 0.52),
                new Point(cx + s * 0.42, cy + s * 0.72));
            dc.DrawLine(pen,
                new Point(cx + s * 0.42, cy + s * 0.72),
                new Point(cx + s * 0.78, cy + s * 0.28));
        }
        else
        {
            var pen = new Pen(_palette.Syntax, 1.2);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
        }

        return aligner.CalculateContentStartX(nestingOffset) - x;
    }

    private double DrawListBullet(DrawingContext dc, double x, double screenY,
        BlockKind blockKind, int nestingLevel, double nestingOffset)
    {
        double lineH = _measure.GetLineHeight(blockKind);
        double baseline = _measure.GetBaseline(blockKind);
        double fontSize = _measure.GetBlockFontSize(blockKind);
        double capHeight = fontSize * _measure.CapsHeightRatio;
        double bulletSize = Math.Round(lineH * 0.32);

        var aligner = new ContentBlockAligner(x, _measure.ListIndent);
        double bulletX = aligner.CalculateMarkerXForSize(bulletSize, nestingOffset);
        double bulletCenterY = screenY + baseline - capHeight / 2;
        double bulletY = Math.Round(bulletCenterY - bulletSize / 2);

        int shape = nestingLevel % 3;
        if (shape == 0)
        {
            dc.DrawEllipse(_palette.Syntax, null, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                bulletSize / 2, bulletSize / 2);
        }
        else if (shape == 1)
        {
            var pen = new Pen(_palette.Syntax, 1.2);
            pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                bulletSize / 2, bulletSize / 2);
        }
        else
        {
            dc.DrawRectangle(_palette.Syntax, null, new Rect(bulletX, bulletY, bulletSize, bulletSize));
        }

        return aligner.CalculateContentStartX(nestingOffset) - x;
    }

    private double DrawOrderedListNumber(DrawingContext dc, double x, double screenY,
        string replacementPrefix, double fontSize, int nestingLevel)
    {
        string trimmed = replacementPrefix.TrimStart();
        string numberText = trimmed.TrimEnd();

        var aligner = new ContentBlockAligner(x, _measure.ListIndent);
        double nestingOffset = nestingLevel * _measure.ListIndent;

        int delimiterPos = numberText.IndexOfAny(new[] { '.', ')' });
        string numberOnly = delimiterPos > 0 ? numberText.Substring(0, delimiterPos) : numberText;

        var ftNumberOnly = new FormattedText(numberOnly, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
            _palette.Syntax, _measure.DpiScale);

        var ftFullNumber = new FormattedText(numberText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
            _palette.Syntax, _measure.DpiScale);

        double numberX = aligner.CalculateMarkerXForSize(ftNumberOnly.WidthIncludingTrailingWhitespace, nestingOffset);
        dc.DrawText(ftFullNumber, new Point(numberX, screenY));

        double textStartX = aligner.CalculateContentStartXForWidth(ftNumberOnly.WidthIncludingTrailingWhitespace, nestingOffset);
        return textStartX - x;
    }

    private double DrawTextSegment(DrawingContext dc, string blockText,
        int rawStart, int rawEnd, BlockVisualMap map, ParsedBlock parsed,
        double fontSize, Typeface baseTypeface, double x, double screenY)
    {
        string displayText = map.BuildDisplayString(blockText, rawStart, rawEnd - rawStart);
        if (displayText.Length == 0) return x;

        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, baseTypeface, fontSize,
            _palette.Foreground, _measure.DpiScale);

        int visBase = 0;
        int runIdx = 0;
        for (int r = rawStart; r < rawEnd; r++)
        {
            if (map.IsHidden(r)) continue;
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, r, ref runIdx);
            if (style != InlineStyle.Normal && style != InlineStyle.Image && visBase < displayText.Length)
            {
                switch (style)
                {
                    case InlineStyle.Bold:
                        ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                        break;
                    case InlineStyle.Italic:
                        ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                        break;
                    case InlineStyle.BoldItalic:
                        ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                        ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                        break;
                    case InlineStyle.Code:
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visBase, 1);
                        break;
                    case InlineStyle.Strikethrough:
                        ft.SetTextDecorations(TextDecorations.Strikethrough, visBase, 1);
                        break;
                }
            }
            visBase++;
        }

        dc.DrawText(ft, new Point(x, screenY));
        return x + ft.WidthIncludingTrailingWhitespace;
    }
}
