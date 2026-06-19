using System.Text;

namespace RaisinDocs;

public class Document
{
    private readonly List<StringBuilder> _blocks = [new()];

    public int CursorBlock { get; set; }
    public int CursorOffset { get; set; }
    public int AnchorBlock { get; set; }
    public int AnchorOffset { get; set; }

    public bool HasSelection => CursorBlock != AnchorBlock || CursorOffset != AnchorOffset;
    public int BlockCount => _blocks.Count;

    // --- Undo/Redo ---

    private const int MaxUndoDepth = 200;

    private record DocumentSnapshot(string[] Blocks, int CursorBlock, int CursorOffset, int AnchorBlock, int AnchorOffset);

    private readonly Stack<DocumentSnapshot> _undoStack = new();
    private readonly Stack<DocumentSnapshot> _redoStack = new();
    private DocumentSnapshot? _currentGroupStart;

    public bool CanUndo => _currentGroupStart != null || _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private DocumentSnapshot CaptureSnapshot()
    {
        var blocks = new string[_blocks.Count];
        for (int i = 0; i < _blocks.Count; i++)
            blocks[i] = _blocks[i].ToString();
        return new DocumentSnapshot(blocks, CursorBlock, CursorOffset, AnchorBlock, AnchorOffset);
    }

    private void RestoreSnapshot(DocumentSnapshot snapshot)
    {
        _blocks.Clear();
        foreach (var block in snapshot.Blocks)
            _blocks.Add(new StringBuilder(block));
        CursorBlock = snapshot.CursorBlock;
        CursorOffset = snapshot.CursorOffset;
        AnchorBlock = snapshot.AnchorBlock;
        AnchorOffset = snapshot.AnchorOffset;
    }

    private bool HasContentChanged(DocumentSnapshot snapshot)
    {
        if (_blocks.Count != snapshot.Blocks.Length) return true;
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].Length != snapshot.Blocks[i].Length) return true;
            if (!_blocks[i].ToString().Equals(snapshot.Blocks[i], StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public void BeginUndoGroup()
    {
        if (_currentGroupStart != null) return;
        _currentGroupStart = CaptureSnapshot();
        _redoStack.Clear();
    }

    public void SealUndoGroup()
    {
        if (_currentGroupStart == null) return;
        if (HasContentChanged(_currentGroupStart))
        {
            _undoStack.Push(_currentGroupStart);
            if (_undoStack.Count > MaxUndoDepth)
            {
                var keep = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = MaxUndoDepth - 1; i >= 0; i--)
                    _undoStack.Push(keep[i]);
            }
        }
        _currentGroupStart = null;
    }

    public bool Undo()
    {
        SealUndoGroup();
        if (_undoStack.Count == 0) return false;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        return true;
    }

    public bool Redo()
    {
        SealUndoGroup();
        if (_redoStack.Count == 0) return false;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        return true;
    }

    public string GetBlockText(int index) => _blocks[index].ToString();
    public int GetBlockLength(int index) => _blocks[index].Length;

    public void CollapseSelection()
    {
        AnchorBlock = CursorBlock;
        AnchorOffset = CursorOffset;
    }

    public void SelectAll()
    {
        AnchorBlock = 0;
        AnchorOffset = 0;
        CursorBlock = _blocks.Count - 1;
        CursorOffset = _blocks[CursorBlock].Length;
    }

    public static int ComparePositions(int block1, int offset1, int block2, int offset2)
    {
        if (block1 != block2) return block1.CompareTo(block2);
        return offset1.CompareTo(offset2);
    }

    public (int startBlock, int startOffset, int endBlock, int endOffset) GetOrderedSelection()
    {
        if (ComparePositions(AnchorBlock, AnchorOffset, CursorBlock, CursorOffset) <= 0)
            return (AnchorBlock, AnchorOffset, CursorBlock, CursorOffset);
        return (CursorBlock, CursorOffset, AnchorBlock, AnchorOffset);
    }

    public string GetSelectedText()
    {
        var (sb, so, eb, eo) = GetOrderedSelection();
        var result = new StringBuilder();
        for (int i = sb; i <= eb; i++)
        {
            if (i > sb) result.Append("\r\n");
            string blockText = _blocks[i].ToString();
            int start = (i == sb) ? so : 0;
            int end = (i == eb) ? eo : blockText.Length;
            string segment = blockText.Substring(start, end - start);
            result.Append(segment.Replace("\n", "\r\n"));
        }
        return result.ToString();
    }

    public void DeleteSelection()
    {
        if (!HasSelection) return;
        var (sb, so, eb, eo) = GetOrderedSelection();
        if (sb == eb)
        {
            _blocks[sb].Remove(so, eo - so);
        }
        else
        {
            _blocks[sb].Remove(so, _blocks[sb].Length - so);
            _blocks[sb].Append(_blocks[eb].ToString().Substring(eo));
            _blocks.RemoveRange(sb + 1, eb - sb);
        }
        CursorBlock = sb;
        CursorOffset = so;
        CollapseSelection();
    }

    public void Insert(char c)
    {
        _blocks[CursorBlock].Insert(CursorOffset, c);
        CursorOffset++;
    }

    public void InsertTextAt(int block, int offset, string text)
    {
        _blocks[block].Insert(offset, text);
    }

    public void RemoveTextAt(int block, int offset, int length)
    {
        _blocks[block].Remove(offset, length);
    }

    public void InsertHardBreak()
    {
        _blocks[CursorBlock].Insert(CursorOffset, '\n');
        CursorOffset++;
    }

    public void InsertParagraphBreak()
    {
        var block = _blocks[CursorBlock];
        string after = block.ToString(CursorOffset, block.Length - CursorOffset);
        block.Remove(CursorOffset, block.Length - CursorOffset);
        CursorBlock++;
        _blocks.Insert(CursorBlock, new StringBuilder(after));
        CursorOffset = 0;
    }

    public void Backspace()
    {
        if (CursorOffset > 0)
        {
            _blocks[CursorBlock].Remove(CursorOffset - 1, 1);
            CursorOffset--;
        }
        else if (CursorBlock > 0)
        {
            var prev = _blocks[CursorBlock - 1];
            int newOffset = prev.Length;
            prev.Append(_blocks[CursorBlock]);
            _blocks.RemoveAt(CursorBlock);
            CursorBlock--;
            CursorOffset = newOffset;
        }
    }

    public void Delete()
    {
        if (CursorOffset < _blocks[CursorBlock].Length)
        {
            _blocks[CursorBlock].Remove(CursorOffset, 1);
        }
        else if (CursorBlock < _blocks.Count - 1)
        {
            _blocks[CursorBlock].Append(_blocks[CursorBlock + 1]);
            _blocks.RemoveAt(CursorBlock + 1);
        }
    }

    public void MoveLeft()
    {
        if (CursorOffset > 0)
            CursorOffset--;
        else if (CursorBlock > 0)
        {
            CursorBlock--;
            CursorOffset = _blocks[CursorBlock].Length;
        }
    }

    public void MoveRight()
    {
        if (CursorOffset < _blocks[CursorBlock].Length)
            CursorOffset++;
        else if (CursorBlock < _blocks.Count - 1)
        {
            CursorBlock++;
            CursorOffset = 0;
        }
    }

    public void Paste(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        var block = _blocks[CursorBlock];
        string afterCursor = block.ToString(CursorOffset, block.Length - CursorOffset);
        block.Remove(CursorOffset, block.Length - CursorOffset);

        block.Append(lines[0]);
        CursorOffset += lines[0].Length;

        for (int i = 1; i < lines.Length; i++)
        {
            CursorBlock++;
            _blocks.Insert(CursorBlock, new StringBuilder(lines[i]));
            CursorOffset = lines[i].Length;
        }

        _blocks[CursorBlock].Append(afterCursor);
        CollapseSelection();
    }
}
