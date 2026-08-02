using System.Windows;
using System.Windows.Input;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles all cursor navigation, positioning, and hit-testing for DocsCanvas.
    /// Encapsulates cursor movement logic (arrows, page up/down, home/end), visual line mapping,
    /// hit-testing for mouse clicks, and table navigation. Supports both source and visual modes.
    /// </summary>
    internal class CursorNavigationEngine
    {
        private readonly DocsCanvas _canvas;

        public CursorNavigationEngine(DocsCanvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        // --- Cursor ↔ visual line mapping ---

        internal int CursorToVisualLineIndex()
        {
            for (int i = _canvas._visualLines.Count - 1; i >= 0; i--)
            {
                var vl = _canvas._visualLines[i];
                if (vl.Group != null)
                {
                    int joined = vl.Group.SourceToJoined(_canvas._doc.CursorBlock, _canvas._doc.CursorOffset);
                    if (joined >= 0 && joined >= vl.StartOffset && joined <= vl.StartOffset + vl.Length)
                    {
                        if (_canvas._cursorAtLineEnd && joined == vl.StartOffset && i > 0
                            && _canvas._visualLines[i - 1].Group == vl.Group)
                            continue;
                        return i;
                    }
                }
                else if (vl.BlockIndex == _canvas._doc.CursorBlock && vl.StartOffset <= _canvas._doc.CursorOffset)
                {
                    if (_canvas._cursorAtLineEnd && vl.StartOffset == _canvas._doc.CursorOffset && i > 0
                        && _canvas._visualLines[i - 1].BlockIndex == vl.BlockIndex)
                        continue;
                    return i;
                }
            }
            return 0;
        }

        internal BlockVisualSpacing? GetVisualLineSpacing(VisualLine vl)
        {
            if (!_canvas.IsVisual || _canvas._visualLineSpacings == null || vl.BlockIndex < 0)
                return null;

            // Find the index of this VisualLine
            int vlIndex = -1;
            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                if (_canvas._visualLines[i] == vl)
                {
                    vlIndex = i;
                    break;
                }
            }

            if (vlIndex < 0 || vlIndex >= _canvas._visualLineSpacings.Count)
                return null;

            return _canvas._visualLineSpacings[vlIndex];
        }

        internal double CursorXInVisualLine(int vlIndex)
        {
            var vl = _canvas._visualLines[vlIndex];

            if (vl.Group != null)
            {
                int joinedOffset = vl.Group.SourceToJoined(_canvas._doc.CursorBlock, _canvas._doc.CursorOffset);
                int localOffset = Math.Clamp(joinedOffset - vl.StartOffset, 0, vl.Length);
                if (localOffset == 0) return 0;
                return MeasureJoinedRange(vl.Group, vl.StartOffset, localOffset);
            }

            int localOff = Math.Clamp(_canvas._doc.CursorOffset - vl.StartOffset, 0, vl.Length);
            var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;

            var parsed = _canvas._parsedBlocks![vl.BlockIndex];
            if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null
                && _canvas._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            {
                return _canvas.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, localOff);
            }

            string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
            double x = _canvas._layoutEngine.GetTextStartXForVisualLine(vl);

            // Subtract padding since we're returning cursor x relative to control left edge
            // (ContentStartX from cache already accounts for ReplacementPrefix width)
            x -= DocsCanvas._padding;

            if (localOff == 0) return x;

            if (map == null)
            {
                string lineText = blockText.Substring(vl.StartOffset, vl.Length);
                var ft = new System.Windows.Media.FormattedText(lineText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, TextMeasurer.GetBlockBaseTypeface(vl.BlockKind),
                    _canvas._measure.GetBlockFontSize(vl.BlockKind), _canvas._palette.Foreground, _canvas._measure.DpiScale);
                _canvas.ApplyInlineStyles(ft, vl, parsed, blockText);
                var geom = ft.BuildHighlightGeometry(new Point(0, 0), 0, localOff);
                return x + (geom != null ? geom.Bounds.Right : ft.WidthIncludingTrailingWhitespace);
            }

            int runIdx = 0;
            for (int i = vl.StartOffset; i < vl.StartOffset + localOff; i++)
            {
                if (map.IsHidden(i))
                {
                    var img = FindImageAtRawOffset(map.Images, i);
                    if (img != null)
                    {
                        var (imgW, _) = _canvas.GetImageSize(img.Value, _canvas._layoutMaxWidth);
                        x += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                x += _canvas._measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
            }
            return x;
        }

        internal double MeasureJoinedRange(ParagraphGroup group, int start, int length)
        {
            double width = _canvas.MeasureRangeWidth(group.JoinedText, start, length,
                group.JoinedParsed.Runs, BlockKind.Paragraph, group.JoinedMap);

            // Add visual space width for soft breaks that fall within the range
            var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
            int runIdx = 0;
            for (int i = start; i < start + length; i++)
            {
                if (softBreaks.Contains(i) && i < group.JoinedText.Length && group.JoinedText[i] == '¶')
                {
                    // Add visual space width after each pilcrow
                    var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, i, ref runIdx);
                    double spaceW = _canvas._measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                    width += spaceW;
                }
            }

            return width;
        }

        internal int HitTestInVisualLineProper(int vlIndex, double clickX)
        {
            var vl = _canvas._visualLines[vlIndex];
            if (vl.Length == 0) return vl.StartOffset;

            var parsed = _canvas._parsedBlocks![vl.BlockIndex];
            var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;
            string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);

            // Account for where text actually starts on screen
            double textStartX = _canvas._layoutEngine.GetTextStartXForVisualLine(vl);

            // clickX is already adjusted by _padding, so adjust textStartX to match
            // (textStartX is in screen coordinates, so we need to remove padding to match clickX)
            double offsetFromTextStart = clickX - (textStartX - DocsCanvas._padding);

            // Measure x position for each visible character and find closest to offsetFromTextStart
            // Start at 0 since offsetFromTextStart is already relative to where text starts
            double accum = 0;

            int runIdx = 0;
            double closestDist = double.MaxValue;
            int closestOffset = vl.StartOffset;

            for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
            {
                double charStart = accum;

                if (map != null && map.IsHidden(i))
                {
                    var img = FindImageAtRawOffset(map.Images, i);
                    if (img != null)
                    {
                        var (imgW, _) = _canvas.GetImageSize(img.Value, _canvas._layoutMaxWidth);
                        accum += imgW;
                    }
                    continue;
                }

                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                double charW = _canvas._measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
                double charEnd = accum + charW;

                // Check if click is closer to this char's start or end
                double distToStart = Math.Abs(offsetFromTextStart - charStart);
                double distToEnd = Math.Abs(offsetFromTextStart - charEnd);
                double minDist = Math.Min(distToStart, distToEnd);

                if (minDist < closestDist)
                {
                    closestDist = minDist;
                    closestOffset = i + (distToEnd < distToStart ? 1 : 0);
                }

                accum = charEnd;
            }

            return Math.Min(closestOffset, vl.StartOffset + vl.Length);
        }

        internal int HitTestInVisualLine(int vlIndex, double x)
        {
            var vl = _canvas._visualLines[vlIndex];
            if (vl.Length == 0) return vl.StartOffset;

            if (vl.Group != null)
                return HitTestInJoinedLine(vl, x);

            var parsed = _canvas._parsedBlocks![vl.BlockIndex];
            if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null
                && _canvas._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            {
                return _canvas.HitTestInTableRow(vl, parsed, colWidths, x);
            }

            var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;
            string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);

            double accum = 0;

            if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            {
                double prefixW = _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                _canvas.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} has replacement prefix, prefixW={prefixW}, x={x}");
                if (x < prefixW)
                {
                    _canvas.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Click in prefix area, returning StartOffset={vl.StartOffset}");
                    return vl.StartOffset;
                }
                accum = prefixW;
            }

            int runIdx = 0;
            for (int i = 0; i < vl.Length; i++)
            {
                int offset = vl.StartOffset + i;
                if (map != null && map.IsHidden(offset))
                {
                    var img = FindImageAtRawOffset(map.Images, offset);
                    if (img != null)
                    {
                        var (imgW, _) = _canvas.GetImageSize(img.Value, _canvas._layoutMaxWidth);
                        if (x < accum + imgW / 2)
                            return offset;
                        accum += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
                double charW = _canvas._measure.MeasureCharWidth(blockText[offset], parsed.Kind, style);
                if (x < accum + charW / 2)
                {
                    _canvas.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} matched char at offset {offset} (accum={accum}, charW={charW})");
                    return offset;
                }
                accum += charW;
            }
            _canvas.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} past all chars, returning end offset {vl.StartOffset + vl.Length} (accum={accum}, x={x})");
            return vl.StartOffset + vl.Length;
        }

        internal int HitTestInJoinedLine(VisualLine vl, double x)
        {
            var group = vl.Group!;
            var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
            double accum = 0;
            int runIdx = 0;

            for (int i = 0; i < vl.Length; i++)
            {
                int offset = vl.StartOffset + i;
                if (group.JoinedMap.IsHidden(offset))
                {
                    var img = FindImageAtRawOffset(group.JoinedMap.Images, offset);
                    if (img != null)
                    {
                        var (imgW, _) = _canvas.GetImageSize(img.Value, _canvas._layoutMaxWidth);
                        if (x < accum + imgW / 2)
                            return offset;
                        accum += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, offset, ref runIdx);
                double charW = _canvas._measure.MeasureCharWidth(group.JoinedText[offset], BlockKind.Paragraph, style);

                // For soft breaks, account for visual space when hit-testing
                double testWidth = charW;
                if (softBreaks.Contains(offset) && group.JoinedText[offset] == '¶')
                {
                    double spaceW = _canvas._measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                    testWidth += spaceW;  // Use full visual width for hit-testing
                }

                // Check if click is in this character's area
                if (x < accum + testWidth / 2)
                    return offset;

                // Advance by character width only (not visual space - that's rendering-only)
                accum += charW;
            }
            return vl.StartOffset + vl.Length;
        }

        internal int HitTestVisualLine(double y)
        {
            if (_canvas._visualLines.Count == 0) return 0;
            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                double lineH = _canvas.GetEffectiveLineHeight(_canvas._visualLines[i]);
                if (y < _canvas._lineYPositions[i] + lineH)
                    return i;
            }
            return _canvas._visualLines.Count - 1;
        }

        internal void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
        {
            if (_canvas._visualLines.Count == 0) { blockIndex = 0; charOffset = 0; return; }
            double effectiveScroll = _canvas._scroll.EffectiveOffset;
            int vli = HitTestVisualLine(pos.Y + effectiveScroll);
            var vl = _canvas._visualLines[vli];
            double xForHitTest = pos.X - DocsCanvas._padding;

            int rawOffset = _canvas.IsVisual ? HitTestInVisualLineProper(vli, xForHitTest) : HitTestInVisualLine(vli, xForHitTest);

            if (vl.Group != null)
            {
                var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
                blockIndex = bi;
                charOffset = bo;
            }
            else
            {
                blockIndex = vl.BlockIndex;
                charOffset = rawOffset;
            }
            _canvas.Logger?.Log(DocsLogLevel.Debug, $"HitTestToPosition: Click at ({pos.X}, {pos.Y}) -> Block {blockIndex}, Offset {charOffset}");
        }

        // --- Key handlers (navigation) ---

        internal void HandleLeft(bool shift, bool ctrl = false)
        {
            _canvas.SealAndStopTimer();
            if (ctrl)
            {
                if (!shift && _canvas._doc.HasSelection)
                {
                    var (sb, so, _, _) = _canvas._doc.GetOrderedSelection();
                    _canvas._doc.CursorBlock = sb;
                    _canvas._doc.CursorOffset = so;
                    _canvas._doc.CollapseSelection();
                }
                else
                {
                    _canvas._doc.MoveWordLeft();
                }
                if (_canvas.IsVisual)
                {
                    if (_canvas._parsedBlocks != null && DocsCanvas.IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
                        _canvas.ClampCursorToTableCell();
                    else
                        _canvas.SkipCursorOverHiddenRanges(forward: false);
                }
                if (!shift) _canvas._doc.CollapseSelection();
            }
            else
            {
                if (_canvas.IsVisual) _canvas._visualModeManager.HandleLeftVisual(shift);
                else HandleLeftSource(shift);
                if (!shift) _canvas._doc.CollapseSelection();
            }
        }

        internal void HandleRight(bool shift, bool ctrl = false)
        {
            _canvas.SealAndStopTimer();
            if (ctrl)
            {
                if (!shift && _canvas._doc.HasSelection)
                {
                    var (_, _, eb, eo) = _canvas._doc.GetOrderedSelection();
                    _canvas._doc.CursorBlock = eb;
                    _canvas._doc.CursorOffset = eo;
                    _canvas._doc.CollapseSelection();
                }
                else
                {
                    _canvas._doc.MoveWordRight();
                }
                if (_canvas.IsVisual)
                {
                    if (_canvas._parsedBlocks != null && DocsCanvas.IsTableRow(_canvas._parsedBlocks[_canvas._doc.CursorBlock]))
                        _canvas.ClampCursorToTableCell();
                    else
                        _canvas.SkipCursorOverHiddenRanges(forward: true);
                }
                if (!shift) _canvas._doc.CollapseSelection();
            }
            else
            {
                if (_canvas.IsVisual) _canvas._visualModeManager.HandleRightVisual(shift);
                else HandleRightSource(shift);
                if (!shift) _canvas._doc.CollapseSelection();
            }
        }

        internal void HandleHome(bool shift, bool ctrl)
        {
            _canvas.SealAndStopTimer();
            if (ctrl)
            {
                _canvas._doc.CursorBlock = 0;
                _canvas._doc.CursorOffset = 0;
            }
            else
            {
                int vli = CursorToVisualLineIndex();
                var vl = _canvas._visualLines[vli];
                if (vl.Group != null)
                {
                    var (targetBi, targetBo) = vl.Group.JoinedToSource(vl.StartOffset);
                    if (_canvas._doc.CursorBlock == targetBi && _canvas._doc.CursorOffset == targetBo
                        && vli > 0 && _canvas._visualLines[vli - 1].Group == vl.Group)
                    {
                        var (firstBi, firstBo) = vl.Group.JoinedToSource(0);
                        _canvas._doc.CursorBlock = firstBi;
                        _canvas._doc.CursorOffset = firstBo;
                    }
                    else
                    {
                        _canvas._doc.CursorBlock = targetBi;
                        _canvas._doc.CursorOffset = targetBo;
                    }
                }
                else
                {
                    if (_canvas._doc.CursorOffset == vl.StartOffset
                        && vli > 0 && _canvas._visualLines[vli - 1].BlockIndex == vl.BlockIndex)
                    {
                        _canvas._doc.CursorOffset = 0;
                    }
                    else
                    {
                        _canvas._doc.CursorOffset = vl.StartOffset;
                    }
                }
            }
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleHomeVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        internal void HandleEnd(bool shift, bool ctrl)
        {
            _canvas.SealAndStopTimer();
            _canvas._cursorAtLineEnd = false;
            if (ctrl)
            {
                _canvas._doc.CursorBlock = _canvas._doc.BlockCount - 1;
                _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(_canvas._doc.CursorBlock);
            }
            else
            {
                int vli = CursorToVisualLineIndex();
                var vl = _canvas._visualLines[vli];
                int endOffset = vl.StartOffset + vl.Length;
                if (vl.Group != null)
                {
                    bool isWrap = vli + 1 < _canvas._visualLines.Count
                        && _canvas._visualLines[vli + 1].Group == vl.Group;
                    if (isWrap)
                    {
                        string text = vl.Group.JoinedText;
                        while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                            endOffset--;
                    }
                    var (targetBi, targetBo) = vl.Group.JoinedToSource(endOffset);
                    if (_canvas._doc.CursorBlock == targetBi && _canvas._doc.CursorOffset == targetBo && isWrap)
                    {
                        var last = vl.Group.Segments[^1];
                        _canvas._doc.CursorBlock = last.BlockIndex;
                        _canvas._doc.CursorOffset = last.Length;
                    }
                    else
                    {
                        _canvas._doc.CursorBlock = targetBi;
                        _canvas._doc.CursorOffset = targetBo;
                    }
                }
                else
                {
                    bool isWrap = vli + 1 < _canvas._visualLines.Count
                        && _canvas._visualLines[vli + 1].BlockIndex == vl.BlockIndex;
                    if (isWrap)
                    {
                        string text = _canvas._doc.GetBlockText(vl.BlockIndex);
                        while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                            endOffset--;
                    }
                    if (_canvas._doc.CursorOffset == endOffset && isWrap)
                    {
                        _canvas._doc.CursorOffset = _canvas._doc.GetBlockLength(vl.BlockIndex);
                    }
                    else
                    {
                        _canvas._doc.CursorOffset = endOffset;
                    }
                }
                _canvas._cursorAtLineEnd = true;
            }
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleEndVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        internal void HandleUp(bool shift)
        {
            _canvas.SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            if (vli > 0)
            {
                double x = CursorXInVisualLine(vli);
                vli--;
                SetCursorFromVisualLine(vli, x);
            }
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleUpVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        internal void HandleDown(bool shift)
        {
            _canvas.SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            if (vli < _canvas._visualLines.Count - 1)
            {
                double x = CursorXInVisualLine(vli);
                vli++;
                SetCursorFromVisualLine(vli, x);
            }
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleDownVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        internal void HandlePageUp(bool shift)
        {
            _canvas.SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            double x = CursorXInVisualLine(vli);
            double cursorY = _canvas._lineYPositions[vli];
            double relativeY = cursorY - _canvas._scroll.Offset;
            double lineH = _canvas.GetEffectiveLineHeight(_canvas._visualLines[vli]);
            double pageAmount = Math.Max(lineH, _canvas.ActualHeight - 3 * lineH);

            _canvas._scroll.Offset -= pageAmount;
            _canvas._scroll.Clamp();

            int targetVli = HitTestVisualLine(_canvas._scroll.Offset + relativeY);
            SetCursorFromVisualLine(targetVli, x);
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleUpVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        internal void HandlePageDown(bool shift)
        {
            _canvas.SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            double x = CursorXInVisualLine(vli);
            double cursorY = _canvas._lineYPositions[vli];
            double relativeY = cursorY - _canvas._scroll.Offset;
            double lineH = _canvas.GetEffectiveLineHeight(_canvas._visualLines[vli]);
            double pageAmount = Math.Max(lineH, _canvas.ActualHeight - 3 * lineH);

            _canvas._scroll.Offset += pageAmount;
            _canvas._scroll.Clamp();

            int targetVli = HitTestVisualLine(_canvas._scroll.Offset + relativeY);
            SetCursorFromVisualLine(targetVli, x);
            if (_canvas.IsVisual) _canvas._visualModeManager.HandleDownVisual();
            if (!shift) _canvas._doc.CollapseSelection();
        }

        private void SetCursorFromVisualLine(int vli, double x)
        {
            var vl = _canvas._visualLines[vli];
            int rawOffset = HitTestInVisualLine(vli, x);
            if (vl.Group != null)
            {
                var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
                _canvas._doc.CursorBlock = bi;
                _canvas._doc.CursorOffset = bo;
            }
            else
            {
                _canvas._doc.CursorBlock = vl.BlockIndex;
                _canvas._doc.CursorOffset = rawOffset;
            }
        }

        // --- Source mode handlers ---

        private bool HandleLeftSource(bool shift)
        {
            if (!shift && _canvas._doc.HasSelection)
            {
                var (sb, so, _, _) = _canvas._doc.GetOrderedSelection();
                _canvas._doc.CursorBlock = sb;
                _canvas._doc.CursorOffset = so;
                _canvas._doc.CollapseSelection();
            }
            else
            {
                _canvas._doc.MoveLeft();
                if (!shift) _canvas._doc.CollapseSelection();
            }
            return true;
        }

        private bool HandleRightSource(bool shift)
        {
            if (!shift && _canvas._doc.HasSelection)
            {
                var (_, _, eb, eo) = _canvas._doc.GetOrderedSelection();
                _canvas._doc.CursorBlock = eb;
                _canvas._doc.CursorOffset = eo;
                _canvas._doc.CollapseSelection();
            }
            else
            {
                _canvas._doc.MoveRight();
                if (!shift) _canvas._doc.CollapseSelection();
            }
            return true;
        }

        // --- Helper methods ---

        private static InlineImage? FindImageAtRawOffset(IReadOnlyList<InlineImage>? images, int rawOffset)
        {
            if (images == null) return null;
            foreach (var img in images)
            {
                if (img.Start == rawOffset) return img;
                if (img.Start > rawOffset) break;
            }
            return null;
        }
    }
}
