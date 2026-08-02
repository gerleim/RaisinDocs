using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    // --- Visual mode: task list checkbox toggle ---

    private bool TryToggleTaskListCheckbox(Point pos)
    {
        if (_parsedBlocks == null) return false;

        double effectiveScroll = _scroll.EffectiveOffset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _visualLines[vli];
        if (vl.StartOffset != 0) return false;

        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Kind is not (BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked))
            return false;

        double nestOff = parsed.ListNestingLevel * BlockVisualMap.SpacesPerNestingLevel
            * _measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);
        if (pos.X > _padding + nestOff + _measure.ListIndent)
            return false;

        SealAndStopTimer();
        _doc.BeginUndoGroup();
        char newChar = parsed.Kind == BlockKind.TaskListItemChecked ? ' ' : 'x';
        int checkCharOffset = parsed.LeadingSpaces + 3;
        _doc.RemoveTextAt(vl.BlockIndex, checkCharOffset, 1);
        _doc.InsertTextAt(vl.BlockIndex, checkCharOffset, newChar.ToString());
        _doc.SealUndoGroup();

        IsDirty = !_doc.IsClean;
        InvalidateLayout();
        return true;
    }

    // --- Visual mode: cursor helpers (delegated to VisualModeManager) ---

    private void SkipCursorOverHiddenRanges(bool forward)
        => _visualModeManager.SkipCursorOverHiddenRanges(forward);

    private void SkipCursorToVisible(bool forward)
        => _visualModeManager.SkipCursorToVisible(forward);

    private void ClampCursorAwayFromHidden()
        => _visualModeManager.ClampCursorAwayFromHidden();

    private void ClampCursorBeforeTrailingHidden()
        => _visualModeManager.ClampCursorBeforeTrailingHidden();

    private void SkipBackspacePastHiddenVisual()
        => _visualModeManager.SkipBackspacePastHiddenVisual();

    private void SkipDeletePastHiddenVisual()
        => _visualModeManager.SkipDeletePastHiddenVisual();

    private void EnsureCursorOnVisibleBlock(bool? preferForward = null)
        => _visualModeManager.EnsureCursorOnVisibleBlock(preferForward);

    // --- Visual mode: key handlers (delegated to VisualModeManager) ---

    private bool HandleBackVisual()
        => _visualModeManager.HandleBackVisual();

    private bool HandleDeleteVisual()
        => _visualModeManager.HandleDeleteVisual();

    private void HandleLeftVisual(bool shift)
        => _visualModeManager.HandleLeftVisual(shift);

    private void HandleRightVisual(bool shift)
        => _visualModeManager.HandleRightVisual(shift);

    private bool HandleTableArrow(ParsedBlock parsed, bool forward)
        => _visualModeManager.HandleTableArrow(parsed, forward);

    private void HandleHomeVisual()
        => _visualModeManager.HandleHomeVisual();

    private void HandleEndVisual()
        => _visualModeManager.HandleEndVisual();

    private void HandleUpVisual()
        => _visualModeManager.HandleUpVisual();

    private void HandleDownVisual()
        => _visualModeManager.HandleDownVisual();

    // --- Visual mode: rectangular table selection ---

    private void DrawTableRectSelection(DrawingContext dc, double effectiveScroll,
        int startCol, int endCol, int startBlock, int endBlock, TableInfo table)
    {
        if (!_tableColumnWidths.TryGetValue(table, out var colWidths)) return;

        double xStart = 0;
        for (int c = 0; c < startCol && c < colWidths.Length; c++)
            xStart += colWidths[c];
        double xEnd = xStart;
        for (int c = startCol; c <= endCol && c < colWidths.Length; c++)
            xEnd += colWidths[c];

        double viewTop = effectiveScroll;
        double viewBottom = effectiveScroll + ActualHeight;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            if (vl.BlockIndex < startBlock || vl.BlockIndex > endBlock) continue;
            var parsed = _parsedBlocks![vl.BlockIndex];
            if (parsed.IsTableSeparator) continue;

            double lineY = _lineYPositions[i];
            double lineH = GetEffectiveLineHeight(vl);
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            dc.DrawRectangle(_palette.Selection, null,
                new Rect(_padding + xStart, lineY - effectiveScroll, xEnd - xStart, lineH));
        }
    }

    private string GetTableRectSelectedText(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        var lines = new List<string>();
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
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

    private void ClearTableRectCells(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;

            var cells = parsed.TableRow.Cells;
            for (int c = Math.Min(rect.EndCol, cells.Count - 1); c >= rect.StartCol; c--)
            {
                var cell = cells[c];
                _doc.RemoveTextAt(b, cell.Start, cell.Length);
                _doc.InsertTextAt(b, cell.Start, "  ");
            }
        }
        _doc.CollapseSelection();
    }

    private void MoveCursorToRectStart(
        (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) rect)
    {
        for (int b = rect.StartBlock; b <= rect.EndBlock; b++)
        {
            var parsed = _parsedBlocks![b];
            if (parsed.IsTableSeparator || parsed.TableRow == null) continue;
            if (rect.StartCol < parsed.TableRow.Cells.Count)
            {
                var cell = parsed.TableRow.Cells[rect.StartCol];
                string blockText = _doc.GetBlockText(b);
                var (trimStart, _) = cell.TrimContent(blockText);
                _doc.CursorBlock = b;
                _doc.CursorOffset = trimStart;
                _doc.CollapseSelection();
                return;
            }
        }
    }

    private bool TryPasteIntoTableCells(string pasteText)
    {
        if (!IsVisual || _parsedBlocks == null) return false;

        var cursorParsed = _parsedBlocks[_doc.CursorBlock];
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

        int startCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.CursorOffset);
        int destBlock = _doc.CursorBlock;
        int lastBlock = destBlock;
        int lastOffset = _doc.CursorOffset;

        foreach (var line in pasteLines)
        {
            while (destBlock < _doc.BlockCount)
            {
                var dp = _parsedBlocks[destBlock];
                if (dp.Table == cursorParsed.Table && dp.TableRow != null && !dp.IsTableSeparator)
                    break;
                destBlock++;
            }
            if (destBlock >= _doc.BlockCount) break;

            var destParsed = _parsedBlocks[destBlock];
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
                _doc.RemoveTextAt(destBlock, dc.Start, dc.Length);
                _doc.InsertTextAt(destBlock, dc.Start, replacement);

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

        _doc.CursorBlock = lastBlock;
        _doc.CursorOffset = lastOffset;
        _doc.CollapseSelection();
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

    internal (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table)?
        TryGetTableRectSelection()
    {
        if (!IsVisual || _parsedBlocks == null || !_doc.HasSelection) return null;

        var anchorParsed = _parsedBlocks[_doc.AnchorBlock];
        var cursorParsed = _parsedBlocks[_doc.CursorBlock];

        if (anchorParsed.Table == null || cursorParsed.Table == null) return null;
        if (anchorParsed.Table != cursorParsed.Table) return null;
        if (anchorParsed.TableRow == null || cursorParsed.TableRow == null) return null;

        int anchorCol = FindCellIndexAtOffset(anchorParsed.TableRow.Cells, _doc.AnchorOffset);
        int cursorCol = FindCellIndexAtOffset(cursorParsed.TableRow.Cells, _doc.CursorOffset);

        if (_doc.AnchorBlock == _doc.CursorBlock && anchorCol == cursorCol)
            return null;

        return (
            Math.Min(anchorCol, cursorCol),
            Math.Max(anchorCol, cursorCol),
            Math.Min(_doc.AnchorBlock, _doc.CursorBlock),
            Math.Max(_doc.AnchorBlock, _doc.CursorBlock),
            anchorParsed.Table
        );
    }

    internal double CursorXInTableRow(int blockIndex, ParsedBlock parsed, double[] colWidths, int cursorOffset)
        => _tableRenderer.CursorXInTableRow(blockIndex, parsed, colWidths, cursorOffset);

    private int HitTestInTableRow(VisualLine vl, ParsedBlock parsed, double[] colWidths, double x)
        => _tableRenderer.HitTestInTableRow(vl, parsed, colWidths, x);

    private void ComputeAllTableColumnWidths(double maxWidth)
        => _tableRenderer.ComputeAllTableColumnWidths(maxWidth);

    private void DrawTableBackgrounds(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
        => _tableRenderer.DrawTableBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

    private void DrawTableRow(DrawingContext dc, VisualLine vl, string blockText,
        ParsedBlock parsed, double lineY, double effectiveScroll,
        double fontSize, Typeface baseTypeface)
        => _tableRenderer.DrawTableRow(dc, vl, blockText, parsed, lineY, effectiveScroll, fontSize, baseTypeface);


    private void ClampCursorToTableCell()
        => _visualModeManager.ClampCursorToTableCell();

    // --- Visual mode: rendering ---

    private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        if (parsed.SyntaxTokens != null)
        {
            ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens, map);
            return;
        }

        int vlEnd = vl.StartOffset + vl.Length;
        foreach (var run in parsed.Runs)
        {
            if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
            int runEnd = run.Start + run.Length;
            if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

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
                    ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visStart, count);
                    break;
                case InlineStyle.Strikethrough:
                    ft.SetTextDecorations(TextDecorations.Strikethrough, visStart, count);
                    break;
                case InlineStyle.Link:
                    ft.SetForegroundBrush(_checkboxCheckedBrush, visStart, count);
                    ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
                    break;
            }
        }

        ApplyColorSpansVisual(ft, vl, parsed, map);
    }

    private void ApplyColorSpansVisual(FormattedText ft, VisualLine vl,
        ParsedBlock parsed, BlockVisualMap map)
    {
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
        int ftLen = ft.Text.Length;

        if (parsed.BlockColor?.Foreground is { } blockFg)
        {
            int vlVisLen = Math.Min(ftLen, map.RawToVisual(vl.StartOffset + vl.Length) - map.RawToVisual(vl.StartOffset));
            if (vlVisLen > 0)
                ft.SetForegroundBrush(GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, vlVisLen);
        }

        var colorSpans = map.ColorSpans;
        if (colorSpans == null) return;

        int vlEnd = vl.StartOffset + vl.Length;
        int vlVisBase = map.RawToVisual(vl.StartOffset);

        foreach (var cs in colorSpans)
        {
            int csEnd = cs.Start + cs.Length;
            if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

            int rawStart = Math.Max(cs.Start, vl.StartOffset);
            int rawEnd = Math.Min(csEnd, vlEnd);
            int visStart = map.RawToVisual(rawStart) - vlVisBase;
            int visEnd = map.RawToVisual(rawEnd) - vlVisBase;
            visEnd = Math.Min(visEnd, ftLen);
            int count = visEnd - visStart;
            if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

            if (cs.Foreground is { } fg)
            {
                ft.SetForegroundBrush(GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
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
        if (map.Images == null) return;

        double x = _padding;
        double screenY = lineY - effectiveScroll;
        double textLineH = _measure.GetLineHeight(vl.BlockKind);
        double totalLineH = vl.OverrideHeight > textLineH ? vl.OverrideHeight : textLineH;

        if (map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            {
                var spacing = GetVisualLineSpacing(vl);
                if (spacing != null)
                {
                    DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                        new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY), parsed.Kind);
                }
            }
            else if (parsed.Kind == BlockKind.UnorderedListItem)
            {
                var spacing = GetVisualLineSpacing(vl);
                if (spacing != null)
                {
                    DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                        parsed.Kind, parsed.ListNestingLevel);
                }
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
            else if (parsed.Kind == BlockKind.OrderedListItem)
            {
                var spacing = GetVisualLineSpacing(vl);
                if (spacing != null)
                {
                    DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                        map.ReplacementPrefix, fontSize, parsed.ListNestingLevel);
                }
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
            else if (map.IsContinuationIndent)
            {
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
            else
            {
                var prefixFt = new FormattedText(map.ReplacementPrefix,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    TextMeasurer.NormalTypeface, fontSize, _palette.Syntax, _measure.DpiScale);
                dc.DrawText(prefixFt, new Point(_padding, screenY));
                x += _measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
            }
        }

        int vlEnd = vl.StartOffset + vl.Length;
        int segStart = vl.StartOffset;

        foreach (var img in map.Images)
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
                DrawImagePlaceholder(dc, x, imgY, imgW, imgH, img.AltText);
            }
            x += imgW;

            segStart = img.Start + img.Length;
        }

        if (segStart < vlEnd)
            DrawTextSegment(dc, blockText, segStart, vlEnd, map, parsed, fontSize, baseTypeface, x, screenY);
    }

    private void DrawTaskListCheckbox(DrawingContext dc, bool isChecked, AbsoluteX markerCenterX, AbsoluteY screenY,
        BlockKind blockKind)
    {
        double lineH = _measure.GetLineHeight(blockKind);
        double baseline = _measure.GetBaseline(blockKind);
        double fontSize = _measure.GetBlockFontSize(blockKind);
        double capHeight = fontSize * _measure.CapsHeightRatio;
        double boxSize = Math.Round(lineH * 0.65);

        // Align checkbox with text baseline, same as bullets
        double checkboxCenterY = screenY.Value + baseline - capHeight / 2;
        double checkboxX = markerCenterX.Value - boxSize / 2;
        double checkboxY = Math.Round(checkboxCenterY - boxSize / 2);
        var rect = new Rect(checkboxX, checkboxY, boxSize, boxSize);
        double radius = 2.5;

        if (isChecked)
        {
            dc.DrawRoundedRectangle(_checkboxCheckedBrush, null, rect, radius, radius);
            var pen = new Pen(_palette.Background, 1.6);
            pen.Freeze();
            double cx = checkboxX, cy = checkboxY, s = boxSize;
            dc.DrawLine(pen,
                new Point(cx + s * 0.22, cy + s * 0.52),
                new Point(cx + s * 0.42, cy + s * 0.72));
            dc.DrawLine(pen,
                new Point(cx + s * 0.42, cy + s * 0.72),
                new Point(cx + s * 0.78, cy + s * 0.28));
        }
        else
        {
            var pen = new Pen(_palette.Syntax, 1.2);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
        }
    }

    private void DrawListBullet(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
        BlockKind blockKind, int nestingLevel)
    {
        double lineH = _measure.GetLineHeight(blockKind);
        double baseline = _measure.GetBaseline(blockKind);
        double fontSize = _measure.GetBlockFontSize(blockKind);
        double capHeight = fontSize * _measure.CapsHeightRatio;
        double bulletSize = Math.Round(lineH * 0.32);

        // markerCenterX is the center of the marker area; adjust to draw position
        double bulletX = markerCenterX.Value - bulletSize / 2;
        double bulletCenterY = screenY.Value + baseline - capHeight / 2;
        double bulletY = Math.Round(bulletCenterY - bulletSize / 2);

        DrawBulletAtPosition(dc, bulletX, bulletY, bulletSize, nestingLevel);
    }

    // Overload for backward compatibility with Print code
    private void DrawListBullet(DrawingContext dc, AbsoluteX baseX, AbsoluteY screenY,
        BlockKind blockKind, int nestingLevel, double nestingOffset)
    {
        double lineH = _measure.GetLineHeight(blockKind);
        double baseline = _measure.GetBaseline(blockKind);
        double fontSize = _measure.GetBlockFontSize(blockKind);
        double capHeight = fontSize * _measure.CapsHeightRatio;
        double bulletSize = Math.Round(lineH * 0.32);

        var aligner = new ContentBlockAligner(baseX.Value, _measure.ListIndent);
        double bulletX = aligner.CalculateMarkerXForSize(bulletSize, nestingOffset);
        double bulletCenterY = screenY.Value + baseline - capHeight / 2;
        double bulletY = Math.Round(bulletCenterY - bulletSize / 2);

        DrawBulletAtPosition(dc, bulletX, bulletY, bulletSize, nestingLevel);
    }

    private void DrawBulletAtPosition(DrawingContext dc, double bulletX, double bulletY, double bulletSize, int nestingLevel)
    {
        int shape = nestingLevel % 3;
        if (shape == 0)
        {
            dc.DrawEllipse(_palette.Syntax, null, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                bulletSize / 2, bulletSize / 2);
        }
        else if (shape == 1)
        {
            var pen = new Pen(_palette.Syntax, 1.2);
            pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                bulletSize / 2, bulletSize / 2);
        }
        else
        {
            dc.DrawRectangle(_palette.Syntax, null, new Rect(bulletX, bulletY, bulletSize, bulletSize));
        }
    }

    private void DrawOrderedListNumber(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
        string replacementPrefix, double fontSize, int nestingLevel)
    {
        string trimmed = replacementPrefix.TrimStart();
        string numberText = trimmed.TrimEnd();

        int delimiterPos = numberText.IndexOfAny(new[] { '.', ')' });
        string numberOnly = delimiterPos > 0 ? numberText.Substring(0, delimiterPos) : numberText;

        var ftNumberOnly = new FormattedText(numberOnly, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
            _palette.Syntax, _measure.DpiScale);

        // Center number at marker center position (adjusted for width)
        double numberX = markerCenterX.Value - ftNumberOnly.WidthIncludingTrailingWhitespace / 2;
        dc.DrawText(ftNumberOnly, new Point(numberX, screenY.Value));

        // Draw delimiter after number
        double delimiterX = numberX + ftNumberOnly.WidthIncludingTrailingWhitespace;
        var ftDelimiter = new FormattedText(numberText.Substring(numberOnly.Length), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
            _palette.Syntax, _measure.DpiScale);
        dc.DrawText(ftDelimiter, new Point(delimiterX, screenY.Value));
    }

    private double DrawTextSegment(DrawingContext dc, string blockText,
        int rawStart, int rawEnd, BlockVisualMap map, ParsedBlock parsed,
        double fontSize, Typeface baseTypeface, double x, double screenY)
    {
        string displayText = map.BuildDisplayString(blockText, rawStart, rawEnd - rawStart);
        if (displayText.Length == 0) return x;

        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, baseTypeface, fontSize,
            _palette.Foreground, _measure.DpiScale);

        int visBase = 0;
        int runIdx = 0;
        for (int r = rawStart; r < rawEnd; r++)
        {
            if (map.IsHidden(r)) continue;
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, r, ref runIdx);
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
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visBase, 1);
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

    private void DrawBlockquoteBar(DrawingContext dc, double lineY, double effectiveScroll)
    {
        var aligner = new ContentBlockAligner(_padding, _measure.ListIndent);
        double barX = aligner.GetBlockquoteBarX();
        double barWidth = 3;
        double barY = lineY - effectiveScroll;
        double barHeight = _measure.GetLineHeight(BlockKind.Blockquote);
        var barBrush = new SolidColorBrush(Color.FromArgb(80, 150, 150, 150));
        barBrush.Freeze();
        dc.DrawRectangle(barBrush, null, new Rect(barX, barY, barWidth, barHeight));
    }
}
