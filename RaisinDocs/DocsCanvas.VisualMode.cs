using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
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
        if (!_parsedBlocks[_doc.CursorBlock].IsFenceDelimiter) return;

        bool forward = preferForward ?? true;

        if (forward)
        {
            for (int i = _doc.CursorBlock + 1; i < _doc.BlockCount; i++)
            {
                if (!_parsedBlocks[i].IsFenceDelimiter)
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
                if (!_parsedBlocks[i].IsFenceDelimiter)
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
                if (!_parsedBlocks[i].IsFenceDelimiter)
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
                if (!_parsedBlocks[i].IsFenceDelimiter)
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
        if (_doc.CursorOffset == 0 && _doc.CursorBlock > 0 &&
            _parsedBlocks != null && _parsedBlocks[_doc.CursorBlock - 1].IsFenceDelimiter)
        {
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
            _doc.CursorBlock < _doc.BlockCount - 1 &&
            _parsedBlocks != null && _parsedBlocks[_doc.CursorBlock + 1].IsFenceDelimiter)
        {
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
        else
        {
            int origBlock = _doc.CursorBlock;
            int origOffset = _doc.CursorOffset;
            _doc.MoveLeft();
            if (!shift) _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: false);
            if (_parsedBlocks != null && _parsedBlocks[_doc.CursorBlock].IsFenceDelimiter)
            {
                _doc.CursorBlock = origBlock;
                _doc.CursorOffset = origOffset;
            }
            SkipCursorOverHiddenRanges(forward: false);
            CrossToPreviousBlockIfHiddenStart();
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
        else
        {
            int origBlock = _doc.CursorBlock;
            int origOffset = _doc.CursorOffset;
            _doc.MoveRight();
            if (!shift) _doc.CollapseSelection();
            EnsureCursorOnVisibleBlock(preferForward: true);
            if (_parsedBlocks != null && _parsedBlocks[_doc.CursorBlock].IsFenceDelimiter)
            {
                _doc.CursorBlock = origBlock;
                _doc.CursorOffset = origOffset;
            }
            SkipCursorOverHiddenRanges(forward: true);
            CrossToNextBlockIfHiddenEnd();
        }
    }

    private void CrossToPreviousBlockIfHiddenStart()
    {
        if (_doc.CursorOffset != 0 || _doc.CursorBlock == 0) return;
        if (_visualMaps == null || _doc.CursorBlock >= _visualMaps.Count) return;
        if (!_visualMaps[_doc.CursorBlock].IsHidden(0)) return;

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
        SkipCursorOverHiddenRanges(forward: false);
    }

    private void HandleDownVisual()
    {
        EnsureCursorOnVisibleBlock(preferForward: true);
        SkipCursorOverHiddenRanges(forward: true);
    }

    // --- Visual mode: rendering ---

    private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal) continue;
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
            }
        }
    }
}
