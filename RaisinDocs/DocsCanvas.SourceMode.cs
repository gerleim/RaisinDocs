namespace RaisinDocs;

public partial class DocsCanvas
{
    private bool HandleBackSource()
    {
        int prevBlock = _doc.CursorBlock;
        int prevOffset = _doc.CursorOffset;
        _doc.Backspace();
        bool changed = _doc.CursorBlock != prevBlock || _doc.CursorOffset != prevOffset;
        if (changed) _doc.CollapseSelection();
        return changed;
    }

    private bool HandleDeleteSource()
    {
        int prevBlocks = _doc.BlockCount;
        int prevLen = _doc.GetBlockLength(_doc.CursorBlock);
        _doc.Delete();
        return _doc.BlockCount != prevBlocks ||
               _doc.GetBlockLength(_doc.CursorBlock) != prevLen;
    }

    private void HandleLeftSource(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (sb, so, _, _) = _doc.GetOrderedSelection();
            _doc.CursorBlock = sb;
            _doc.CursorOffset = so;
            _doc.CollapseSelection();
        }
        else
        {
            _doc.MoveLeft();
            if (!shift) _doc.CollapseSelection();
        }
    }

    private void HandleRightSource(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (_, _, eb, eo) = _doc.GetOrderedSelection();
            _doc.CursorBlock = eb;
            _doc.CursorOffset = eo;
            _doc.CollapseSelection();
        }
        else
        {
            _doc.MoveRight();
            if (!shift) _doc.CollapseSelection();
        }
    }
}
