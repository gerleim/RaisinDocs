namespace RaisinDocs;

/// <summary>
/// Manages table-specific keyboard input handling: Tab navigation, Enter to insert rows.
/// Extracted from DocsCanvas.Input to reduce its size and improve separation of concerns.
/// </summary>
internal class TableInputHandler
{
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _content;
    private readonly ICanvasOperations _canvas;

    public TableInputHandler(IDocumentServices doc, IParsedContentServices content, ICanvasOperations canvas)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    /// <summary>
    /// Handles Tab key in table cells - navigates between cells within a row,
    /// or moves to the next/previous table row.
    /// </summary>
    public bool HandleTableTab(bool shift, out bool textChanged)
    {
        textChanged = false;
        if (_content.ParsedBlocks == null) return false;
        var parsed = _content.ParsedBlocks[_doc.Document.CursorBlock];
        if (parsed.TableRow == null || parsed.Table == null) return false;

        _canvas.SealAndStopTimer();
        var cells = parsed.TableRow.Cells;
        string blockText = _doc.GetBlockText(_doc.Document.CursorBlock);
        int colCount = parsed.Table.ColumnCount;

        int curCell = -1;
        for (int c = 0; c < cells.Count; c++)
        {
            if (_doc.Document.CursorOffset >= cells[c].Start &&
                _doc.Document.CursorOffset <= cells[c].Start + cells[c].Length)
            { curCell = c; break; }
        }
        if (curCell < 0) curCell = 0;

        if (!shift)
        {
            if (curCell + 1 < cells.Count)
            {
                var next = cells[curCell + 1];
                MoveCursorToCell(next, blockText);
            }
            else
            {
                for (int b = _doc.Document.CursorBlock + 1; b < _doc.BlockCount; b++)
                {
                    var p = _content.ParsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.Document.CursorBlock = b;
                        var nextBlockText = _doc.GetBlockText(b);
                        MoveCursorToCell(p.TableRow.Cells[0], nextBlockText);
                        break;
                    }
                    break;
                }
            }
        }
        else
        {
            if (curCell > 0)
            {
                var prev = cells[curCell - 1];
                MoveCursorToCell(prev, blockText);
            }
            else
            {
                for (int b = _doc.Document.CursorBlock - 1; b >= 0; b--)
                {
                    var p = _content.ParsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.Document.CursorBlock = b;
                        var prevBlockText = _doc.GetBlockText(b);
                        var lastCell = p.TableRow.Cells[^1];
                        MoveCursorToCell(lastCell, prevBlockText);
                        break;
                    }
                    break;
                }
            }
        }

        _doc.Document.CollapseSelection();
        return true;
    }

    /// <summary>
    /// Handles Enter key in a table row - inserts a new table row.
    /// </summary>
    public bool HandleTableEnter(out bool textChanged)
    {
        textChanged = false;
        if (_content.ParsedBlocks == null) return false;
        var parsed = _content.ParsedBlocks[_doc.Document.CursorBlock];
        if (parsed.Table == null) return false;

        int colCount = parsed.Table.ColumnCount;
        string newRow = "|" + string.Concat(Enumerable.Repeat("  |", colCount));

        _doc.Document.BeginUndoGroup();
        if (_doc.Document.HasSelection) _doc.Document.DeleteSelection();
        _doc.Document.CollapseSelection();

        int insertAfter = _doc.Document.CursorBlock;
        if (parsed.Kind == BlockKind.TableHeaderRow || parsed.Kind == BlockKind.TableSeparatorRow)
        {
            for (int b = insertAfter + 1; b < _doc.BlockCount; b++)
            {
                if (_content.ParsedBlocks[b].Kind == BlockKind.TableSeparatorRow) { insertAfter = b; continue; }
                break;
            }
        }

        _doc.Document.CursorBlock = insertAfter;
        _doc.Document.CursorOffset = _doc.GetBlockLength(insertAfter);
        _doc.Document.InsertParagraphBreak();
        _doc.Document.Paste(newRow);
        _doc.Document.CursorOffset = 2;
        _doc.Document.CollapseSelection();
        _doc.Document.SealUndoGroup();
        textChanged = true;
        return true;
    }

    /// <summary>
    /// Moves the cursor to a specific table cell, positioning between non-whitespace content.
    /// Sets the selection to the entire cell content.
    /// </summary>
    private void MoveCursorToCell(TableCellInfo cell, string blockText)
    {
        int start = cell.Start;
        int end = cell.Start + cell.Length;
        while (start < end && blockText[start] == ' ') start++;
        while (end > start && blockText[end - 1] == ' ') end--;
        _doc.Document.CursorOffset = start;
        _doc.Document.AnchorBlock = _doc.Document.CursorBlock;
        _doc.Document.AnchorOffset = end;
    }
}
