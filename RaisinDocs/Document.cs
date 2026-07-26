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
    private DocumentSnapshot? _cleanSnapshot;

    public bool CanUndo => _currentGroupStart != null || _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action? ContentChanged;

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

    public void MarkClean() => _cleanSnapshot = CaptureSnapshot();

    public bool IsClean => _cleanSnapshot != null && !HasContentChanged(_cleanSnapshot);

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
            ContentChanged?.Invoke();
        }
        _currentGroupStart = null;
    }

    public bool Undo()
    {
        SealUndoGroup();
        if (_undoStack.Count == 0) return false;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        ContentChanged?.Invoke();
        return true;
    }

    public bool Redo()
    {
        SealUndoGroup();
        if (_redoStack.Count == 0) return false;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        ContentChanged?.Invoke();
        return true;
    }

    public string GetBlockText(int index) => _blocks[index].ToString();
    public int GetBlockLength(int index) => _blocks[index].Length;

    public string GetText()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (i > 0) sb.Append("\r\n");
            sb.Append(_blocks[i]);
        }
        return sb.ToString();
    }

    public void SetText(string text)
    {
        _blocks.Clear();
        _blocks.Add(new StringBuilder());
        CursorBlock = 0;
        CursorOffset = 0;
        CollapseSelection();
        _undoStack.Clear();
        _redoStack.Clear();
        _currentGroupStart = null;
        Paste(text);
        CursorBlock = 0;
        CursorOffset = 0;
        CollapseSelection();
    }

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

    public (int Start, int End) GetWordBoundaries(int block, int offset)
    {
        string text = _blocks[block].ToString();
        if (text.Length == 0) return (0, 0);

        offset = Math.Min(offset, text.Length - 1);
        if (offset < 0) return (0, 0);

        bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        bool wordChar = IsWord(text[offset]);

        int start = offset;
        int end = offset;

        if (wordChar)
        {
            while (start > 0 && IsWord(text[start - 1])) start--;
            while (end < text.Length - 1 && IsWord(text[end + 1])) end++;
        }
        else
        {
            while (start > 0 && !IsWord(text[start - 1]) && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length - 1 && !IsWord(text[end + 1]) && !char.IsWhiteSpace(text[end + 1])) end++;
        }

        return (start, end + 1);
    }

    public void SelectWord(int block, int offset)
    {
        var (start, end) = GetWordBoundaries(block, offset);
        if (start == end && _blocks[block].Length == 0) return;

        AnchorBlock = block;
        AnchorOffset = start;
        CursorBlock = block;
        CursorOffset = end;
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
        so = Math.Min(so, _blocks[sb].Length);
        eo = Math.Min(eo, _blocks[eb].Length);
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
        so = Math.Min(so, _blocks[sb].Length);
        eo = Math.Min(eo, _blocks[eb].Length);
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
        if (CursorBlock < 0 || CursorBlock >= _blocks.Count)
            throw new InvalidOperationException(
                $"Invalid cursor block: {CursorBlock} (valid range: 0-{_blocks.Count - 1})");

        var block = _blocks[CursorBlock];
        if (CursorOffset < 0 || CursorOffset > block.Length)
            throw new InvalidOperationException(
                $"Invalid cursor offset: {CursorOffset} in block {CursorBlock} with length {block.Length}. " +
                $"Block content preview: {block.ToString().Substring(0, Math.Min(50, block.Length))}");

        block.Insert(CursorOffset, c);
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

    public void InsertBlockAt(int index, string text)
    {
        _blocks.Insert(index, new StringBuilder(text));
        if (CursorBlock >= index) CursorBlock++;
        if (AnchorBlock >= index) AnchorBlock++;
    }

    public void RemoveBlockAt(int index)
    {
        _blocks.RemoveAt(index);
        if (CursorBlock > index) CursorBlock--;
        else if (CursorBlock == index) { CursorBlock = Math.Max(0, index - 1); CursorOffset = _blocks[CursorBlock].Length; }
        if (AnchorBlock > index) AnchorBlock--;
        else if (AnchorBlock == index) { AnchorBlock = Math.Max(0, index - 1); AnchorOffset = _blocks[AnchorBlock].Length; }
    }

    public void InsertParagraphBreak()
    {
        var block = _blocks[CursorBlock];
        CursorOffset = Math.Min(CursorOffset, block.Length);
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

    public void ToggleBlockPrefix(int blockIndex, string prefix)
    {
        var text = _blocks[blockIndex].ToString();
        if (text.StartsWith(prefix))
        {
            _blocks[blockIndex].Remove(0, prefix.Length);
            AdjustPositionsAfterPrefixChange(blockIndex, -prefix.Length);
        }
        else
        {
            var existingPrefix = GetBlockPrefix(text);
            if (existingPrefix != null)
            {
                _blocks[blockIndex].Remove(0, existingPrefix.Length);
                AdjustPositionsAfterPrefixChange(blockIndex, -existingPrefix.Length);
            }
            _blocks[blockIndex].Insert(0, prefix);
            AdjustPositionsAfterPrefixChange(blockIndex, prefix.Length);
        }
    }

    internal static string? GetBlockPrefix(string text)
    {
        int ls = 0;
        while (ls < text.Length && ls < 3 && text[ls] == ' ') ls++;
        string stripped = ls > 0 ? text[ls..] : text;

        for (int h = 6; h >= 1; h--)
        {
            var hp = new string('#', h) + " ";
            if (stripped.StartsWith(hp)) return text.Substring(0, ls + hp.Length);
        }
        if (stripped.Length >= 6 && (stripped.StartsWith("- ") || stripped.StartsWith("* "))
            && stripped[2] == '[' && (stripped[3] == ' ' || stripped[3] == 'x' || stripped[3] == 'X') && stripped[4] == ']' && stripped[5] == ' ')
            return text.Substring(0, ls + 6);
        if (stripped.StartsWith("- ")) return text.Substring(0, ls + 2);
        if (stripped.StartsWith("* ")) return text.Substring(0, ls + 2);
        int olLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
        if (olLen > 0) return text.Substring(0, ls + olLen);
        if (stripped.StartsWith("> ")) return text.Substring(0, ls + 2);
        return null;
    }

    private void AdjustPositionsAfterPrefixChange(int blockIndex, int delta)
    {
        if (CursorBlock == blockIndex)
            CursorOffset = Math.Max(0, CursorOffset + delta);
        if (AnchorBlock == blockIndex)
            AnchorOffset = Math.Max(0, AnchorOffset + delta);
    }

    public void IndentLines(int startBlock, int endBlock, int spaces)
    {
        var indent = new string(' ', spaces);
        for (int b = startBlock; b <= endBlock; b++)
        {
            _blocks[b].Insert(0, indent);
            AdjustPositionsAfterPrefixChange(b, spaces);
        }
    }

    public void OutdentLines(int startBlock, int endBlock, int maxSpaces)
    {
        for (int b = startBlock; b <= endBlock; b++)
        {
            var text = _blocks[b].ToString();
            int remove = 0;
            while (remove < maxSpaces && remove < text.Length && text[remove] == ' ')
                remove++;
            if (remove > 0)
            {
                _blocks[b].Remove(0, remove);
                AdjustPositionsAfterPrefixChange(b, -remove);
            }
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

    static int CharGroup(char c) =>
        char.IsLetterOrDigit(c) || c == '_' ? 1 :
        char.IsWhiteSpace(c) ? 0 : 2;

    public void MoveWordLeft()
    {
        var text = _blocks[CursorBlock].ToString();
        int pos = Math.Min(CursorOffset, text.Length);
        if (pos == 0)
        {
            MoveLeft();
            return;
        }
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1])) pos--;
        if (pos > 0)
        {
            int group = CharGroup(text[pos - 1]);
            while (pos > 0 && CharGroup(text[pos - 1]) == group) pos--;
        }
        CursorOffset = pos;
    }

    public void MoveWordRight()
    {
        var text = _blocks[CursorBlock].ToString();
        int pos = CursorOffset;
        int len = text.Length;
        if (pos >= len)
        {
            MoveRight();
            return;
        }
        int group = CharGroup(text[pos]);
        while (pos < len && CharGroup(text[pos]) == group) pos++;
        while (pos < len && char.IsWhiteSpace(text[pos])) pos++;
        CursorOffset = pos;
    }

    public int ReflowBoxTable(int startBlock, int endBlock)
    {
        int i = endBlock;
        while (i >= startBlock)
        {
            if (!IsBoxDrawingLine(_blocks[i].ToString()))
            {
                i--;
                continue;
            }

            int tableEnd = i;
            while (i >= startBlock && IsBoxDrawingLine(_blocks[i].ToString()))
                i--;
            int tableStart = i + 1;

            var dataRows = new List<string[]>();
            string[]? pendingRow = null;
            for (int j = tableStart; j <= tableEnd; j++)
            {
                string line = _blocks[j].ToString();
                char sep = line.Contains('│') ? '│' : line.Contains('║') ? '║' : '\0';
                if (sep == '\0')
                {
                    if (pendingRow != null)
                    {
                        dataRows.Add(pendingRow);
                        pendingRow = null;
                    }
                    continue;
                }

                var parts = line.Split(sep);
                var cells = new List<string>();
                for (int k = 1; k < parts.Length - 1; k++)
                    cells.Add(parts[k].Trim());
                if (cells.Count == 0) continue;

                if (pendingRow != null && pendingRow.Length == cells.Count)
                {
                    for (int k = 0; k < cells.Count; k++)
                    {
                        if (cells[k].Length > 0)
                            pendingRow[k] = pendingRow[k].Length > 0
                                ? pendingRow[k] + " " + cells[k]
                                : cells[k];
                    }
                }
                else
                {
                    if (pendingRow != null)
                        dataRows.Add(pendingRow);
                    pendingRow = cells.ToArray();
                }
            }
            if (pendingRow != null)
                dataRows.Add(pendingRow);

            if (dataRows.Count == 0) continue;

            int maxCols = 0;
            foreach (var row in dataRows)
                if (row.Length > maxCols) maxCols = row.Length;

            for (int j = 0; j < dataRows.Count; j++)
            {
                if (dataRows[j].Length < maxCols)
                {
                    var padded = new string[maxCols];
                    Array.Copy(dataRows[j], padded, dataRows[j].Length);
                    for (int k = dataRows[j].Length; k < maxCols; k++)
                        padded[k] = "";
                    dataRows[j] = padded;
                }
            }

            var mdLines = new List<string>();
            mdLines.Add("| " + string.Join(" | ", dataRows[0]) + " |");
            var seps = new string[maxCols];
            for (int j = 0; j < maxCols; j++) seps[j] = "---";
            mdLines.Add("| " + string.Join(" | ", seps) + " |");
            for (int j = 1; j < dataRows.Count; j++)
                mdLines.Add("| " + string.Join(" | ", dataRows[j]) + " |");

            int oldCount = tableEnd - tableStart + 1;
            int newCount = mdLines.Count;
            int delta = newCount - oldCount;

            _blocks.RemoveRange(tableStart, oldCount);
            for (int j = 0; j < newCount; j++)
                _blocks.Insert(tableStart + j, new StringBuilder(mdLines[j]));

            if (CursorBlock >= tableStart && CursorBlock <= tableEnd)
            {
                CursorBlock = tableStart;
                CursorOffset = 0;
            }
            else if (CursorBlock > tableEnd)
            {
                CursorBlock += delta;
            }

            if (AnchorBlock >= tableStart && AnchorBlock <= tableEnd)
            {
                AnchorBlock = tableStart;
                AnchorOffset = 0;
            }
            else if (AnchorBlock > tableEnd)
            {
                AnchorBlock += delta;
            }

            endBlock += delta;
        }

        return endBlock;
    }

    private static bool IsBoxDrawingLine(string line)
    {
        foreach (char c in line)
            if (c >= '─' && c <= '╿')
                return true;
        return false;
    }

    public bool SplitInlineColorDivs(int startBlock, ref int endBlock,
        Func<string, int> findOpenEnd, Func<string, int> findCloseStart,
        Func<string, string> openToDiv)
    {
        bool changed = false;
        for (int i = startBlock; i <= endBlock; i++)
        {
            string text = _blocks[i].ToString();
            int openEnd = findOpenEnd(text);
            if (openEnd < 0 || openEnd >= text.Length) continue;

            string tag = text[..openEnd];
            string after = text[openEnd..];

            int closeBlock = -1;
            int closeStart = -1;
            for (int j = i; j <= endBlock; j++)
            {
                int cs = findCloseStart(_blocks[j].ToString());
                if (cs >= 0)
                {
                    closeBlock = j;
                    closeStart = cs;
                    break;
                }
            }

            if (closeBlock < 0) continue;
            if (closeBlock == i) continue;

            string closeText = _blocks[closeBlock].ToString();
            string beforeClose = closeText[..closeStart];
            _blocks[closeBlock] = new StringBuilder(beforeClose);
            if (beforeClose.Length == 0)
            {
                _blocks.RemoveAt(closeBlock);
                AdjustPositionAfterRemove(closeBlock);
                endBlock--;
            }

            string divOpen = openToDiv(tag);
            string content = closeBlock == i ? after[..(closeStart - openEnd)] : after;
            _blocks[i] = new StringBuilder(content);
            _blocks.Insert(i, new StringBuilder(divOpen));
            AdjustPositionAfterInsert(i);
            endBlock++;
            if (closeBlock >= i) closeBlock++;
            i++;

            int divCloseIdx = closeBlock + 1;
            _blocks.Insert(divCloseIdx, new StringBuilder("<!--/@div-->"));
            AdjustPositionAfterInsert(divCloseIdx);
            endBlock++;

            changed = true;
        }
        return changed;
    }

    private void AdjustPositionAfterInsert(int index)
    {
        if (CursorBlock >= index) CursorBlock++;
        if (AnchorBlock >= index) AnchorBlock++;
    }

    private void AdjustPositionAfterRemove(int index)
    {
        if (CursorBlock > index) CursorBlock--;
        else if (CursorBlock == index) { CursorBlock = Math.Max(0, index - 1); CursorOffset = _blocks[CursorBlock].Length; }
        if (AnchorBlock > index) AnchorBlock--;
        else if (AnchorBlock == index) { AnchorBlock = Math.Max(0, index - 1); AnchorOffset = _blocks[AnchorBlock].Length; }
    }

    public bool Reflow(int startBlock, int endBlock, Func<string, bool> isMergeableBlock, Func<string, int>? isFenceLine = null)
    {
        int countBefore = _blocks.Count;
        endBlock = ReflowBoxTable(startBlock, endBlock);

        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);

        if (HasSelection)
        {
            for (int i = endBlock; i >= startBlock; i--)
            {
                if (insideFence != null && insideFence.Contains(i))
                    continue;
                if (_blocks[i].Length == 0)
                {
                    bool prevEmpty = i > startBlock && _blocks[i - 1].Length == 0
                        && (insideFence == null || !insideFence.Contains(i - 1));
                    bool nextEmpty = i < endBlock && _blocks[i + 1].Length == 0
                        && (insideFence == null || !insideFence.Contains(i + 1));
                    if (prevEmpty || nextEmpty)
                    {
                        RemoveBlockForReflow(i, 0);
                        endBlock--;
                    }
                }
            }
            insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        }

        for (int i = endBlock; i > startBlock; i--)
        {
            if (insideFence != null && (insideFence.Contains(i) || insideFence.Contains(i - 1)))
                continue;

            string curr = _blocks[i].ToString();
            int prevLen = _blocks[i - 1].Length;
            if (curr.Length > 0 && prevLen > 0
                && isMergeableBlock(curr) && isMergeableBlock(_blocks[i - 1].ToString()))
            {
                _blocks[i - 1].Append(' ').Append(curr);
                RemoveBlockForReflow(i, prevLen);
                endBlock--;
            }
        }

        return _blocks.Count != countBefore;
    }

    private void RemoveBlockForReflow(int i, int prevLenBeforeMerge)
    {
        _blocks.RemoveAt(i);
        if (CursorBlock == i)
        {
            CursorBlock = Math.Max(0, i - 1);
            CursorOffset = prevLenBeforeMerge > 0 ? prevLenBeforeMerge + 1 + CursorOffset : 0;
        }
        else if (CursorBlock > i)
        {
            CursorBlock--;
        }
        if (AnchorBlock == i)
        {
            AnchorBlock = Math.Max(0, i - 1);
            AnchorOffset = prevLenBeforeMerge > 0 ? prevLenBeforeMerge + 1 + AnchorOffset : 0;
        }
        else if (AnchorBlock > i)
        {
            AnchorBlock--;
        }
    }

    private HashSet<int>? BuildFenceMap(int startBlock, int endBlock, Func<string, int>? getFenceLength)
    {
        if (getFenceLength == null) return null;

        var fenced = new HashSet<int>();
        int fenceLen = 0;
        for (int i = 0; i <= endBlock && i < _blocks.Count; i++)
        {
            string text = _blocks[i].ToString();
            int backticks = getFenceLength(text);
            if (fenceLen == 0 && backticks > 0)
            {
                fenceLen = backticks;
                if (i >= startBlock) fenced.Add(i);
            }
            else if (fenceLen > 0)
            {
                if (i >= startBlock) fenced.Add(i);
                if (backticks >= fenceLen) fenceLen = 0;
            }
        }
        return fenced.Count > 0 ? fenced : null;
    }

    public bool TrimWhitespace(int startBlock, int endBlock, Func<string, int>? isFenceLine = null,
        Func<string, bool>? hasSignificantLeading = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        bool changed = false;
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            string text = _blocks[i].ToString();

            int leadingRemoved = 0;
            string result;
            if (hasSignificantLeading != null && hasSignificantLeading(text))
            {
                result = text;
            }
            else
            {
                result = text.TrimStart();
                leadingRemoved = text.Length - result.Length;
            }

            string trimmedEnd = result.TrimEnd();
            int trailingCount = result.Length - trimmedEnd.Length;
            if (trailingCount >= 2)
                result = trimmedEnd + "  ";
            else
                result = trimmedEnd;

            if (result == text) continue;

            _blocks[i] = new StringBuilder(result);
            changed = true;

            if (CursorBlock == i)
                CursorOffset = Math.Max(0, Math.Min(CursorOffset - leadingRemoved, result.Length));
            if (AnchorBlock == i)
                AnchorOffset = Math.Max(0, Math.Min(AnchorOffset - leadingRemoved, result.Length));
        }
        return changed;
    }

    public bool NormalizeAdjacentMarkers(int startBlock, int endBlock, Func<string, string> normalize, Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        bool changed = false;
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            string text = _blocks[i].ToString();
            string result = normalize(text);
            if (result == text) continue;

            _blocks[i] = new StringBuilder(result);
            changed = true;

            if (CursorBlock == i)
                CursorOffset = Math.Min(CursorOffset, result.Length);
            if (AnchorBlock == i)
                AnchorOffset = Math.Min(AnchorOffset, result.Length);
        }
        return changed;
    }

    public bool HasAdjacentMarkers(int startBlock, int endBlock, Func<string, bool> hasMarkers, Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            if (hasMarkers(_blocks[i].ToString()))
                return true;
        }
        return false;
    }

    public bool HasBoxDrawingTable(int startBlock, int endBlock)
    {
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (IsBoxDrawingLine(_blocks[i].ToString()))
                return true;
        }
        return false;
    }

    public bool HasMergeableParagraphs(int startBlock, int endBlock, Func<string, bool> isMergeableBlock, Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        for (int i = endBlock; i > startBlock; i--)
        {
            if (insideFence != null && (insideFence.Contains(i) || insideFence.Contains(i - 1)))
                continue;
            string curr = _blocks[i].ToString();
            string prev = _blocks[i - 1].ToString();
            if (curr.Length > 0 && prev.Length > 0
                && isMergeableBlock(curr) && isMergeableBlock(prev))
                return true;
        }
        return false;
    }

    public bool HasConsecutiveBlankLines(int startBlock, int endBlock, Func<string, int>? isFenceLine = null)
    {
        if (!HasSelection) return false;
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        for (int i = startBlock + 1; i <= endBlock; i++)
        {
            if (insideFence != null && (insideFence.Contains(i) || insideFence.Contains(i - 1)))
                continue;
            if (_blocks[i].Length == 0 && _blocks[i - 1].Length == 0)
                return true;
        }
        return false;
    }

    public bool HasTrimmableWhitespace(int startBlock, int endBlock, Func<string, int>? isFenceLine = null,
        Func<string, bool>? hasSignificantLeading = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            string text = _blocks[i].ToString();
            if (text.Length > 0 && (text[0] == ' ' || text[0] == '\t')
                && (hasSignificantLeading == null || !hasSignificantLeading(text)))
                return true;
            if (text.Length > 0 && text[^1] == ' ')
            {
                string trimmedEnd = text.TrimEnd();
                int trailing = text.Length - trimmedEnd.Length;
                if (trailing != 2)
                    return true;
            }
        }
        return false;
    }

    public bool HasMisnumberedOrderedList(int startBlock, int endBlock, Func<string, int>? getOrderedPrefixLength, Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        return HasMisnumberedOrderedList(startBlock, endBlock, insideFence, getOrderedPrefixLength);
    }

    private bool HasMisnumberedOrderedList(int startBlock, int endBlock, HashSet<int>? insideFence, Func<string, int>? getOrderedPrefixLength)
    {
        if (getOrderedPrefixLength == null) return false;
        int expected = -1;
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
            {
                expected = -1;
                continue;
            }
            string text = _blocks[i].ToString();
            int pl = getOrderedPrefixLength(text);
            if (pl > 0)
            {
                string numStr = text[..(pl - 2)];
                if (int.TryParse(numStr, out int num))
                {
                    if (expected < 0)
                        expected = num + 1;
                    else if (num != expected)
                        return true;
                    else
                        expected++;
                }
            }
            else
            {
                expected = -1;
            }
        }
        return false;
    }

    public bool RenumberOrderedLists(int startBlock, int endBlock, Func<string, int> getOrderedPrefixLength,
        Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        bool changed = false;
        int i = startBlock;
        while (i <= endBlock)
        {
            if (insideFence != null && insideFence.Contains(i))
            {
                i++;
                continue;
            }
            string text = _blocks[i].ToString();
            int pl = getOrderedPrefixLength(text);
            if (pl == 0)
            {
                i++;
                continue;
            }

            int runStart = i;
            string firstNumStr = text[..(pl - 2)];
            char delim = text[pl - 2];
            int expected = int.TryParse(firstNumStr, out int firstNum) ? firstNum : 1;

            for (int j = runStart; j <= endBlock; j++)
            {
                if (insideFence != null && insideFence.Contains(j))
                    break;
                string jText = _blocks[j].ToString();
                int jPl = getOrderedPrefixLength(jText);
                if (jPl == 0) break;

                string newPrefix = expected.ToString() + delim + " ";
                if (jPl != newPrefix.Length || !jText.StartsWith(newPrefix))
                {
                    _blocks[j].Remove(0, jPl);
                    _blocks[j].Insert(0, newPrefix);
                    int delta = newPrefix.Length - jPl;
                    if (CursorBlock == j)
                        CursorOffset = Math.Max(0, CursorOffset + delta);
                    if (AnchorBlock == j)
                        AnchorOffset = Math.Max(0, AnchorOffset + delta);
                    changed = true;
                }
                expected++;
                i = j + 1;
            }
        }
        return changed;
    }

    public bool AddHardBreaks(int startBlock, int endBlock, string marker,
        Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        bool changed = false;
        for (int i = startBlock; i < endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            string text = _blocks[i].ToString();
            if (text.Length == 0) continue;
            string next = _blocks[i + 1].ToString();
            if (next.Length == 0) continue;
            if (text.EndsWith('\\') || text.EndsWith("  "))
                continue;
            _blocks[i].Append(marker);
            changed = true;
        }
        return changed;
    }

    public bool HasSoftBreaks(int startBlock, int endBlock,
        Func<string, int>? isFenceLine = null)
    {
        var insideFence = BuildFenceMap(startBlock, endBlock, isFenceLine);
        for (int i = startBlock; i < endBlock; i++)
        {
            if (insideFence != null && insideFence.Contains(i))
                continue;
            string text = _blocks[i].ToString();
            if (text.Length == 0) continue;
            string next = _blocks[i + 1].ToString();
            if (next.Length == 0) continue;
            if (text.EndsWith('\\') || text.EndsWith("  "))
                continue;
            return true;
        }
        return false;
    }

    public void Paste(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        var block = _blocks[CursorBlock];
        CursorOffset = Math.Min(CursorOffset, block.Length);
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

    // Merge paragraph continuation blocks
    internal void MergeParagraphContinuations(List<ParsedBlock> parsedBlocks)
    {
        // Process in reverse to avoid index shifting issues when removing
        int i = parsedBlocks.Count - 1;
        while (i >= 0)
        {
            var parsed = parsedBlocks[i];
            if (parsed.Kind != BlockKind.Paragraph || parsed.Children == null)
            {
                i--;
                continue;
            }

            // Find which parsed blocks are continuations (they come right after in the list)
            var continuationIndices = new List<int>();
            for (int j = i + 1; j < parsedBlocks.Count && j <= i + parsed.Children.Count; j++)
            {
                if (parsed.Children.Contains(parsedBlocks[j]))
                    continuationIndices.Add(j);
            }

            if (continuationIndices.Count > 0)
            {
                // Calculate offset adjustments BEFORE merging
                int parentBlockLen = _blocks[i].Length;
                var offsetAdjustments = new Dictionary<int, int>();
                int accumulatedLen = parentBlockLen + 1; // +1 for newline

                for (int j = 0; j < continuationIndices.Count; j++)
                {
                    int contIdx = continuationIndices[j];
                    offsetAdjustments[contIdx] = accumulatedLen;
                    accumulatedLen += _blocks[contIdx].Length + 1; // +1 for the newline
                }

                // Merge block text: combine i with all continuations after it
                var parentText = _blocks[i].ToString();
                for (int j = 0; j < continuationIndices.Count; j++)
                {
                    int contIdx = continuationIndices[j];
                    string contText = _blocks[contIdx].ToString();
                    parentText += "\n" + contText;
                }
                _blocks[i] = new StringBuilder(parentText);

                // Remove continuation blocks from _blocks and parsedBlocks (in reverse order to preserve indices)
                for (int j = continuationIndices.Count - 1; j >= 0; j--)
                {
                    int idxToRemove = continuationIndices[j];

                    // Adjust cursor if it's at or after the removed block
                    if (CursorBlock == idxToRemove)
                    {
                        // Cursor is in a continuation block - move it to the parent block
                        CursorBlock = i;
                        CursorOffset = offsetAdjustments[idxToRemove] + CursorOffset;
                    }
                    else if (CursorBlock > idxToRemove)
                    {
                        // Cursor is after the removed block - shift it back
                        CursorBlock--;
                    }

                    // Same for anchor
                    if (AnchorBlock == idxToRemove)
                    {
                        AnchorBlock = i;
                        AnchorOffset = offsetAdjustments[idxToRemove] + AnchorOffset;
                    }
                    else if (AnchorBlock > idxToRemove)
                    {
                        AnchorBlock--;
                    }

                    _blocks.RemoveAt(idxToRemove);
                    parsedBlocks.RemoveAt(idxToRemove);
                }

                // Update the parent block to clear Children since they're merged
                parsedBlocks[i] = parsed with { Children = null };
            }

            i--;
        }
    }
}
