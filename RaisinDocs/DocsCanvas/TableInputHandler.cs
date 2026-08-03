namespace RaisinDocs;

/// <summary>
/// Manages table-specific keyboard input handling: Tab navigation, Enter to insert rows.
/// Extracted from DocsCanvas.Input to reduce its size and improve separation of concerns.
/// </summary>
internal class TableInputHandler
{
    private readonly IDocsCanvasServices _services;

    public TableInputHandler(IDocsCanvasServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Handles Tab key in table cells - navigates between cells within a row,
    /// or moves to the next/previous table row.
    /// </summary>
    public bool HandleTableTab(bool shift, out bool textChanged)
    {
        var canvas = (DocsCanvas)_services;
        textChanged = false;
        if (canvas._parsedBlocks == null) return false;
        var parsed = canvas._parsedBlocks[canvas._doc.CursorBlock];
        if (parsed.TableRow == null || parsed.Table == null) return false;

        canvas.SealAndStopTimer();
        var cells = parsed.TableRow.Cells;
        string blockText = canvas._doc.GetBlockText(canvas._doc.CursorBlock);
        int colCount = parsed.Table.ColumnCount;

        int curCell = -1;
        for (int c = 0; c < cells.Count; c++)
        {
            if (canvas._doc.CursorOffset >= cells[c].Start &&
                canvas._doc.CursorOffset <= cells[c].Start + cells[c].Length)
            { curCell = c; break; }
        }
        if (curCell < 0) curCell = 0;

        if (!shift)
        {
            if (curCell + 1 < cells.Count)
            {
                var next = cells[curCell + 1];
                MoveCursorToCell(next, blockText, canvas);
            }
            else
            {
                for (int b = canvas._doc.CursorBlock + 1; b < canvas._doc.BlockCount; b++)
                {
                    var p = canvas._parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        canvas._doc.CursorBlock = b;
                        var nextBlockText = canvas._doc.GetBlockText(b);
                        MoveCursorToCell(p.TableRow.Cells[0], nextBlockText, canvas);
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
                MoveCursorToCell(prev, blockText, canvas);
            }
            else
            {
                for (int b = canvas._doc.CursorBlock - 1; b >= 0; b--)
                {
                    var p = canvas._parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        canvas._doc.CursorBlock = b;
                        var prevBlockText = canvas._doc.GetBlockText(b);
                        var lastCell = p.TableRow.Cells[^1];
                        MoveCursorToCell(lastCell, prevBlockText, canvas);
                        break;
                    }
                    break;
                }
            }
        }

        canvas._doc.CollapseSelection();
        return true;
    }

    /// <summary>
    /// Handles Enter key in a table row - inserts a new table row.
    /// </summary>
    public bool HandleTableEnter(out bool textChanged)
    {
        var canvas = (DocsCanvas)_services;
        textChanged = false;
        if (canvas._parsedBlocks == null) return false;
        var parsed = canvas._parsedBlocks[canvas._doc.CursorBlock];
        if (parsed.Table == null) return false;

        int colCount = parsed.Table.ColumnCount;
        string newRow = "|" + string.Concat(Enumerable.Repeat("  |", colCount));

        canvas._doc.BeginUndoGroup();
        if (canvas._doc.HasSelection) canvas._doc.DeleteSelection();
        canvas._doc.CollapseSelection();

        int insertAfter = canvas._doc.CursorBlock;
        if (parsed.Kind == BlockKind.TableHeaderRow || parsed.Kind == BlockKind.TableSeparatorRow)
        {
            for (int b = insertAfter + 1; b < canvas._doc.BlockCount; b++)
            {
                if (canvas._parsedBlocks[b].Kind == BlockKind.TableSeparatorRow) { insertAfter = b; continue; }
                break;
            }
        }

        canvas._doc.CursorBlock = insertAfter;
        canvas._doc.CursorOffset = canvas._doc.GetBlockLength(insertAfter);
        canvas._doc.InsertParagraphBreak();
        canvas._doc.Paste(newRow);
        canvas._doc.CursorOffset = 2;
        canvas._doc.CollapseSelection();
        canvas._doc.SealUndoGroup();
        textChanged = true;
        return true;
    }

    /// <summary>
    /// Moves the cursor to a specific table cell, positioning between non-whitespace content.
    /// Sets the selection to the entire cell content.
    /// </summary>
    private static void MoveCursorToCell(TableCellInfo cell, string blockText, DocsCanvas canvas)
    {
        int start = cell.Start;
        int end = cell.Start + cell.Length;
        while (start < end && blockText[start] == ' ') start++;
        while (end > start && blockText[end - 1] == ' ') end--;
        canvas._doc.CursorOffset = start;
        canvas._doc.AnchorBlock = canvas._doc.CursorBlock;
        canvas._doc.AnchorOffset = end;
    }
}
