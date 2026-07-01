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

        double effectiveScroll = _scrollOffset + _smoother.Offset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _visualLines[vli];
        if (vl.StartOffset != 0) return false;

        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Kind is not (BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked))
            return false;

        if (pos.X > _padding + _listIndent)
            return false;

        SealAndStopTimer();
        _doc.BeginUndoGroup();
        char newChar = parsed.Kind == BlockKind.TaskListItemChecked ? ' ' : 'x';
        _doc.RemoveTextAt(vl.BlockIndex, 3, 1);
        _doc.InsertTextAt(vl.BlockIndex, 3, newChar.ToString());
        _doc.SealUndoGroup();

        IsDirty = true;
        InvalidateLayout();
        return true;
    }

    // --- Visual mode: cursor helpers ---

    private void SkipCursorOverHiddenRanges(bool forward)
    {
        if (_visualMaps == null) return;
        if (_doc.CursorBlock >= _visualMaps.Count) return;
        var map = _visualMaps[_doc.CursorBlock];
        int offset = map.SkipHidden(_doc.CursorOffset, forward);
        if (forward)
        {
            int blockLen = _doc.GetBlockLength(_doc.CursorBlock);
            while (offset < blockLen && map.IsHidden(offset))
                offset++;
        }
        else
        {
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
        if (_parsedBlocks == null) return;
        if (!_parsedBlocks[_doc.CursorBlock].IsSkippedInVisual) return;

        bool forward = preferForward ?? true;

        if (forward)
        {
            for (int i = _doc.CursorBlock + 1; i < _doc.BlockCount; i++)
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
            for (int i = _doc.CursorBlock + 1; i < _doc.BlockCount; i++)
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
        {
            int s = cell.Start, e = cell.Start + cell.Length;
            while (s < e && blockText[s] == ' ') s++;
            while (e > s && blockText[e - 1] == ' ') e--;
            cellRanges.Add((s, e));
        }

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
                int s = cell.Start;
                while (s < cell.Start + cell.Length && blockText[s] == ' ') s++;
                _doc.CursorBlock = b;
                _doc.CursorOffset = s;
                _doc.CollapseSelection();
                return;
            }
        }
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

        double x = 0;
        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            var cell = cells[c];
            int cellEnd = cell.Start + cell.Length;
            if (cursorOffset >= cell.Start && cursorOffset <= cellEnd)
            {
                string cellContent = blockText.Substring(cell.Start, cell.Length).Trim();
                int leadTrim = 0;
                while (cell.Start + leadTrim < cellEnd && blockText[cell.Start + leadTrim] == ' ')
                    leadTrim++;

                int offsetInContent = Math.Clamp(cursorOffset - cell.Start - leadTrim, 0, cellContent.Length);
                int runIdx = 0;
                double textW = 0;
                for (int i = 0; i < offsetInContent; i++)
                {
                    var style = GetStyleAtOffset(parsed.Runs, cell.Start + leadTrim + i, ref runIdx);
                    textW += MeasureCharWidth(cellContent[i], parsed.Kind, style);
                }

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double fullTextW = MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, cell.Start + leadTrim);
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
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
        double cx = 0;

        for (int c = 0; c < cells.Count && c < colWidths.Length; c++)
        {
            if (x < cx + colWidths[c] || c == cells.Count - 1 || c == colWidths.Length - 1)
            {
                var cell = cells[c];
                string cellContent = blockText.Substring(cell.Start, cell.Length).Trim();
                int leadTrim = 0;
                while (cell.Start + leadTrim < cell.Start + cell.Length && blockText[cell.Start + leadTrim] == ' ')
                    leadTrim++;

                var align = parsed.Table!.Alignments[c];
                double cellContentWidth = colWidths[c] - _tableCellPadding * 2;
                double fullTextW = MeasureStringWidth(cellContent, parsed.Kind, parsed.Runs, cell.Start + leadTrim);
                double alignOffset = align switch
                {
                    ColumnAlignment.Center => Math.Max(0, (cellContentWidth - fullTextW) / 2),
                    ColumnAlignment.Right => Math.Max(0, cellContentWidth - fullTextW),
                    _ => 0,
                };

                double localX = x - cx - _tableCellPadding - alignOffset;
                double accum = 0;
                int runIdx = 0;
                for (int i = 0; i < cellContent.Length; i++)
                {
                    var style = GetStyleAtOffset(parsed.Runs, cell.Start + leadTrim + i, ref runIdx);
                    double charW = MeasureCharWidth(cellContent[i], parsed.Kind, style);
                    if (localX < accum + charW / 2)
                        return cell.Start + leadTrim + i;
                    accum += charW;
                }
                return cell.Start + leadTrim + cellContent.Length;
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
                for (int c = 0; c < Math.Min(p.TableRow.Cells.Count, colCount); c++)
                {
                    var cell = p.TableRow.Cells[c];
                    string cellText = text.Substring(cell.Start, cell.Length).Trim();
                    double w = MeasureStringWidth(cellText, p.Kind, p.Runs, cell.Start);
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
            var parsed = _parsedBlocks![vl.BlockIndex];
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

                double headerH = GetLineHeight(_visualLines[tableStart].BlockKind);
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

        double x = _padding;
        double y = lineY - effectiveScroll;
        double lineH = GetLineHeight(vl.BlockKind);
        bool isHeader = parsed.Kind == BlockKind.TableHeaderRow;

        for (int c = 0; c < Math.Min(parsed.TableRow.Cells.Count, colWidths.Length); c++)
        {
            var cell = parsed.TableRow.Cells[c];
            string cellText = blockText.Substring(cell.Start, cell.Length).Trim();
            if (cellText.Length == 0) { x += colWidths[c]; continue; }

            var cellTypeface = isHeader ? _boldTypeface : baseTypeface;
            var ft = new FormattedText(cellText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, cellTypeface, fontSize,
                _palette.Foreground, _dpiScale);

            ApplyInlineStylesForCell(ft, cellText, parsed, cell, blockText);

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
            dc.DrawText(ft, new Point(textX, y));
            dc.Pop();

            x += colWidths[c];
        }
    }

    private static void ApplyInlineStylesForCell(FormattedText ft, string cellText,
        ParsedBlock parsed, TableCellInfo cell, string blockText)
    {
        if (parsed.Runs.Count <= 1) return;

        int rawStart = cell.Start;
        int rawEnd = cell.Start + cell.Length;
        int leadingTrim = 0;
        while (rawStart + leadingTrim < rawEnd && blockText[rawStart + leadingTrim] == ' ')
            leadingTrim++;
        int contentStart = rawStart + leadingTrim;

        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= contentStart || run.Start >= rawEnd) continue;

            int overlapStart = Math.Max(run.Start, contentStart) - contentStart;
            int overlapEnd = Math.Min(runEnd, rawEnd) - contentStart;
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
            int s = cell.Start, e = cell.Start + cell.Length;
            while (s < e && blockText[s] == ' ') s++;
            while (e > s && blockText[e - 1] == ' ') e--;
            if (offset >= s && offset <= e) return;
        }

        // cursor is in a hidden region — find nearest cell boundary
        int best = 0;
        int bestDist = int.MaxValue;
        foreach (var cell in parsed.TableRow.Cells)
        {
            int s = cell.Start, e = cell.Start + cell.Length;
            while (s < e && blockText[s] == ' ') s++;
            while (e > s && blockText[e - 1] == ' ') e--;
            if (Math.Abs(offset - s) < bestDist) { best = s; bestDist = Math.Abs(offset - s); }
            if (Math.Abs(offset - e) < bestDist) { best = e; bestDist = Math.Abs(offset - e); }
        }
        _doc.CursorOffset = best;
    }

    // --- Visual mode: rendering ---

    private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;
            if (parsed.Kind == BlockKind.FencedCodeLine) continue;

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
                    ft.SetFontFamily(_monoTypeface.FontFamily, visStart, count);
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
        int ftLen = ft.Text.Length;

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            var brush = new SolidColorBrush(Color.FromRgb(blockFg.R, blockFg.G, blockFg.B));
            brush.Freeze();
            int vlVisLen = Math.Min(ftLen, map.RawToVisual(vl.StartOffset + vl.Length) - map.RawToVisual(vl.StartOffset));
            if (vlVisLen > 0)
                ft.SetForegroundBrush(brush, 0, vlVisLen);
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
                var brush = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
                brush.Freeze();
                ft.SetForegroundBrush(brush, visStart, count);
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
        double x = _padding;
        double screenY = lineY - effectiveScroll;
        double textLineH = GetLineHeight(vl.BlockKind);
        double totalLineH = vl.OverrideHeight > textLineH ? vl.OverrideHeight : textLineH;

        if (map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            {
                x += DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                    _padding, screenY, parsed.Kind);
            }
            else
            {
                var prefixFt = new FormattedText(map.ReplacementPrefix,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _normalTypeface, fontSize, _palette.Syntax, _dpiScale);
                dc.DrawText(prefixFt, new Point(_padding, screenY));
                x += MeasureReplacementPrefix(map.ReplacementPrefix, parsed.Kind);
            }
        }

        int vlEnd = vl.StartOffset + vl.Length;
        int segStart = vl.StartOffset;

        foreach (var img in map.Images!)
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
                var placeholderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
                placeholderBrush.Freeze();
                dc.DrawRectangle(placeholderBrush, null, new Rect(x, imgY, imgW, imgH));

                if (!string.IsNullOrEmpty(img.AltText))
                {
                    var altFt = new FormattedText(img.AltText,
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        _normalTypeface, 11, _palette.Syntax, _dpiScale);
                    altFt.MaxTextWidth = Math.Max(1, imgW);
                    altFt.MaxTextHeight = Math.Max(1, imgH);
                    dc.DrawText(altFt, new Point(x + 2, imgY + 2));
                }
            }
            x += imgW;

            segStart = img.Start + img.Length;
        }

        if (segStart < vlEnd)
            DrawTextSegment(dc, blockText, segStart, vlEnd, map, parsed, fontSize, baseTypeface, x, screenY);
    }

    private double DrawTaskListCheckbox(DrawingContext dc, bool isChecked, double x, double screenY, BlockKind blockKind)
    {
        double lineH = GetLineHeight(blockKind);
        double boxSize = Math.Round(lineH * 0.65);
        double yOffset = Math.Round((lineH - boxSize) / 2);
        double checkboxX = x + _listIndent - boxSize - 4;
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

        return _listIndent;
    }

    private double DrawTextSegment(DrawingContext dc, string blockText,
        int rawStart, int rawEnd, BlockVisualMap map, ParsedBlock parsed,
        double fontSize, Typeface baseTypeface, double x, double screenY)
    {
        string displayText = map.BuildDisplayString(blockText, rawStart, rawEnd - rawStart);
        if (displayText.Length == 0) return x;

        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, baseTypeface, fontSize,
            _palette.Foreground, _dpiScale);

        int visBase = 0;
        int runIdx = 0;
        for (int r = rawStart; r < rawEnd; r++)
        {
            if (map.IsHidden(r)) continue;
            var style = GetStyleAtOffset(parsed.Runs, r, ref runIdx);
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
                        ft.SetFontFamily(_monoTypeface.FontFamily, visBase, 1);
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
