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
