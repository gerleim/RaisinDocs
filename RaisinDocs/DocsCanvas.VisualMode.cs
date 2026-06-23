using System.Globalization;
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
            if (_parsedBlocks != null && _parsedBlocks[_doc.CursorBlock].IsSkippedInVisual)
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
            var prefixFt = new FormattedText(map.ReplacementPrefix,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                _normalTypeface, fontSize, _palette.Syntax, _dpiScale);
            dc.DrawText(prefixFt, new Point(_padding, screenY));
            x += MeasureReplacementPrefix(map.ReplacementPrefix, parsed.Kind);
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
