using System.Collections.Generic;

namespace RaisinDocs;

internal readonly record struct TocEntry(int BlockIndex, int HeadingLevel, string Text);

public partial class DocsCanvas
{
    internal TocPanel? TocPanel { get; set; }
    internal bool IsTocVisible { get; set; }

    internal void InitTocTheme()
    {
        TocPanel?.ApplyTheme(_palette.Background, _palette.Foreground, _palette.Syntax, _palette.CodeBackground);
    }

    public void ToggleToc()
    {
        var editor = FindParentEditor();
        if (editor != null)
            editor.ShowToc = !editor.ShowToc;
    }

    internal List<TocEntry> GetTocEntries()
    {
        ComputeLayout();
        var entries = new List<TocEntry>();
        if (_parsedBlocks == null) return entries;
        for (int bi = 0; bi < _parsedBlocks.Count; bi++)
        {
            var kind = _parsedBlocks[bi].Kind;
            if (kind >= BlockKind.Heading1 && kind <= BlockKind.Heading6)
            {
                int level = kind - BlockKind.Heading1 + 1;
                string raw = _doc.GetBlockText(bi);
                entries.Add(new TocEntry(bi, level, StripHeadingPrefix(raw, level)));
            }
        }
        return entries;
    }

    internal int GetCurrentHeadingBlock()
    {
        ComputeLayout();
        int cursorBlock = _doc.CursorBlock;
        if (_parsedBlocks == null) return -1;
        for (int bi = Math.Min(cursorBlock, _parsedBlocks.Count - 1); bi >= 0; bi--)
        {
            var kind = _parsedBlocks[bi].Kind;
            if (kind >= BlockKind.Heading1 && kind <= BlockKind.Heading6)
                return bi;
        }
        return -1;
    }

    internal void NavigateToBlock(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= _doc.BlockCount) return;
        _doc.CursorBlock = blockIndex;
        _doc.CursorOffset = 0;
        _doc.CollapseSelection();
        ComputeLayout();
        ScrollBlockToTop(blockIndex);
        InvalidateVisual();
    }

    private void ScrollBlockToTop(int blockIndex)
    {
        _scroll.StopWheelCoast();
        _scroll.CancelSmooth();
        if (_visualLines.Count == 0) return;
        int vli = CursorToVisualLineIndex();
        _scroll.Offset = _lineYPositions[vli] - _padding;
        _scroll.Clamp();
    }

    private static string StripHeadingPrefix(string text, int level)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        int hashEnd = i + level;
        if (hashEnd < text.Length && text[hashEnd] == ' ')
            hashEnd++;
        return hashEnd <= text.Length ? text[hashEnd..].TrimEnd() : text.TrimEnd();
    }
}
