namespace RaisinDocs;

/// <summary>
/// Rectangular selection inside a markdown table: reading it, clearing it, and
/// pasting a table-shaped clipboard into it.
///
/// The rectangle is never stored. <see cref="TryGetTableRectSelection"/> derives it
/// fresh from the document's anchor and cursor on every call, which is why callers
/// across rendering, input and editing can ask for it independently without
/// coordinating. Nothing here holds state between calls.
/// </summary>
internal class TableSelectionManager
{
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _content;

    public TableSelectionManager(IDocumentServices doc, IParsedContentServices content)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// The rectangle the current selection spans, or null when the selection is not a
    /// rectangle within one table. Derived from anchor/cursor on each call.
    /// </summary>
    internal (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)?
        TryGetTableRectSelection()
    {
        var parsedBlocks = _content.ParsedBlocks;
        if (!_content.IsVisual || parsedBlocks == null || !_doc.Document.HasSelection) return null;

        var anchorParsed = parsedBlocks[_doc.Document.AnchorBlock];
        var cursorParsed = parsedBlocks[_doc.Document.CursorBlock];

        if (anchorParsed.Table == null || cursorParsed.Table == null) return null;
        if (anchorParsed.Table != cursorParsed.Table) return null;
        if (anchorParsed.TableRow == null || cursorParsed.TableRow == null) return null;

        int anchorCol = FindCellIndexAtOffset(anchorParsed.TableRow.Cells, _doc.Document.AnchorOffset);
        int cursorCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.Document.CursorOffset);

        if (_doc.Document.AnchorBlock == _doc.Document.CursorBlock && anchorCol == cursorCol)
            return null;

        return (
            Math.Min(anchorCol, cursorCol),
            Math.Max(anchorCol, cursorCol),
            Math.Min(_doc.Document.AnchorBlock, _doc.Document.CursorBlock),
            Math.Max(_doc.Document.AnchorBlock, _doc.Document.CursorBlock),
            anchorParsed.Table
        );
    }

    /// <summary>The selected cells rendered back as markdown table rows.</summary>
    internal string GetTableRectSelectedText(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        var parsedBlocks = _content.ParsedBlocks;
        var lines = new List<string>();
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = parsedBlocks![b];
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

    /// <summary>
    /// Empties the selected cells, leaving the two spaces that keep a cell padded.
    /// Walks columns right-to-left so earlier cell offsets stay valid as text shrinks.
    /// </summary>
    internal void ClearTableRectCells(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        var parsedBlocks = _content.ParsedBlocks;
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;

            var cells = parsed.TableRow.Cells;
            for (int c = Math.Min(rect.EndCol, cells.Count - 1); c >= rect.StartCol; c--)
            {
                var cell = cells[c];
                _doc.Document.RemoveTextAt(b, cell.Start, cell.Length);
                _doc.Document.InsertTextAt(b, cell.Start, "  ");
            }
        }
        _doc.Document.CollapseSelection();
    }

    /// <summary>Puts the cursor at the first content character of the rectangle's top-left cell.</summary>
    internal void MoveCursorToRectStart(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        var parsedBlocks = _content.ParsedBlocks;
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;
            if (rect.StartCol < parsed.TableRow.Cells.Count)
            {
                var cell = parsed.TableRow.Cells[rect.StartCol];
                string blockText = _doc.GetBlockText(b);
                var (trimStart, _) = cell.TrimContent(blockText);
                _doc.Document.CursorBlock = b;
                _doc.Document.CursorOffset = trimStart;
                _doc.Document.CollapseSelection();
                return;
            }
        }
    }

    /// <summary>
    /// Pastes table-shaped text cell-by-cell starting at the cursor's cell, rather than
    /// dropping the raw markdown in as one string. Returns false when the cursor is not in
    /// a table or the text is not a table, leaving the caller to paste normally.
    /// </summary>
    internal bool TryPasteIntoTableCells(string pasteText)
    {
        var parsedBlocks = _content.ParsedBlocks;
        if (!_content.IsVisual || parsedBlocks == null) return false;

        var cursorParsed = parsedBlocks[_doc.Document.CursorBlock];
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

        // A pasted markdown table (e.g. converted from an Excel copy) carries an alignment
        // separator that is syntax, not data — it must not land in a destination cell.
        pasteLines = [.. pasteLines.Where(l => !MarkdownParser.IsSeparatorRow(l.Trim(), out _))];
        if (pasteLines.Length == 0) return false;

        int startCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.Document.CursorOffset);
        int destBlock = _doc.Document.CursorBlock;
        int lastBlock = destBlock;
        int lastOffset = _doc.Document.CursorOffset;

        foreach (var line in pasteLines)
        {
            while (destBlock < _doc.BlockCount)
            {
                var dp = parsedBlocks[destBlock];
                if (dp.Table == cursorParsed.Table && dp.TableRow != null && !dp.IsTableSeparator)
                    break;
                destBlock++;
            }
            if (destBlock >= _doc.BlockCount) break;

            var destParsed = parsedBlocks[destBlock];
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
                _doc.Document.RemoveTextAt(destBlock, dc.Start, dc.Length);
                _doc.Document.InsertTextAt(destBlock, dc.Start, replacement);

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

        _doc.Document.CursorBlock = lastBlock;
        _doc.Document.CursorOffset = lastOffset;
        _doc.Document.CollapseSelection();
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
}
