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
        private readonly IDocsCanvasServices _services;

        public CursorNavigationEngine(IDocsCanvasServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        // --- Cursor ↔ visual line mapping ---

        internal int CursorToVisualLineIndex()
        {
            for (int i = ((DocsCanvas)_services)._visualLines.Count - 1; i >= 0; i--)
            {
                var vl = ((DocsCanvas)_services)._visualLines[i];
                if (vl.Group != null)
                {
                    int joined = vl.Group.SourceToJoined(((DocsCanvas)_services)._doc.CursorBlock, ((DocsCanvas)_services)._doc.CursorOffset);
                    if (joined >= 0 && joined >= vl.StartOffset && joined <= vl.StartOffset + vl.Length)
                    {
                        if (((DocsCanvas)_services)._cursorAtLineEnd && joined == vl.StartOffset && i > 0
                            && ((DocsCanvas)_services)._visualLines[i - 1].Group == vl.Group)
                            continue;
                        return i;
                    }
                }
                else if (vl.BlockIndex == ((DocsCanvas)_services)._doc.CursorBlock && vl.StartOffset <= ((DocsCanvas)_services)._doc.CursorOffset)
                {
                    if (((DocsCanvas)_services)._cursorAtLineEnd && vl.StartOffset == ((DocsCanvas)_services)._doc.CursorOffset && i > 0
                        && ((DocsCanvas)_services)._visualLines[i - 1].BlockIndex == vl.BlockIndex)
                        continue;
                    return i;
                }
            }
            return 0;
        }

        internal BlockVisualSpacing? GetVisualLineSpacing(VisualLine vl)
        {
            if (!_services.IsVisual || ((DocsCanvas)_services)._visualLineSpacings == null || vl.BlockIndex < 0)
                return null;

            // Find the index of this VisualLine
            int vlIndex = -1;
            for (int i = 0; i < ((DocsCanvas)_services)._visualLines.Count; i++)
            {
                if (((DocsCanvas)_services)._visualLines[i] == vl)
                {
                    vlIndex = i;
                    break;
                }
            }

            if (vlIndex < 0 || vlIndex >= ((DocsCanvas)_services)._visualLineSpacings.Count)
                return null;

            return ((DocsCanvas)_services)._visualLineSpacings[vlIndex];
        }

        internal double CursorXInVisualLine(int vlIndex)
        {
            var vl = ((DocsCanvas)_services)._visualLines[vlIndex];

            if (vl.Group != null)
            {
                int joinedOffset = vl.Group.SourceToJoined(((DocsCanvas)_services)._doc.CursorBlock, ((DocsCanvas)_services)._doc.CursorOffset);
                int localOffset = Math.Clamp(joinedOffset - vl.StartOffset, 0, vl.Length);
                if (localOffset == 0) return 0;
                return MeasureJoinedRange(vl.Group, vl.StartOffset, localOffset);
            }

            int localOff = Math.Clamp(((DocsCanvas)_services)._doc.CursorOffset - vl.StartOffset, 0, vl.Length);
            var map = _services.IsVisual ? ((DocsCanvas)_services)._visualMaps?[vl.BlockIndex] : null;

            var parsed = ((DocsCanvas)_services)._parsedBlocks![vl.BlockIndex];
            if (_services.IsVisual && parsed.Table != null && parsed.TableRow != null
                && ((DocsCanvas)_services)._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            {
                return _services.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, localOff);
            }

            string blockText = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);
            double x = ((DocsCanvas)_services)._layoutEngine.GetTextStartXForVisualLine(vl);

            // Subtract padding since we're returning cursor x relative to control left edge
            // (ContentStartX from cache already accounts for ReplacementPrefix width)
            x -= DocsCanvas._padding;

            if (localOff == 0) return x;

            if (map == null)
            {
                string lineText = blockText.Substring(vl.StartOffset, vl.Length);
                var ft = new System.Windows.Media.FormattedText(lineText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, TextMeasurer.GetBlockBaseTypeface(vl.BlockKind),
                    ((DocsCanvas)_services)._measure.GetBlockFontSize(vl.BlockKind), ((DocsCanvas)_services)._palette.Foreground, ((DocsCanvas)_services)._measure.DpiScale);
                _services.ApplyInlineStyles(ft, vl, parsed, blockText);
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
                        var (imgW, _) = ((DocsCanvas)_services).GetImageSize(img.Value, ((DocsCanvas)_services)._layoutMaxWidth);
                        x += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                x += ((DocsCanvas)_services)._measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
            }
            return x;
        }

        internal double MeasureJoinedRange(ParagraphGroup group, int start, int length)
        {
            double width = _services.MeasureRangeWidth(group.JoinedText, start, length,
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
                    double spaceW = ((DocsCanvas)_services)._measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                    width += spaceW;
                }
            }

            return width;
        }

        internal int HitTestInVisualLineProper(int vlIndex, double clickX)
        {
            var vl = ((DocsCanvas)_services)._visualLines[vlIndex];
            if (vl.Length == 0) return vl.StartOffset;

            var parsed = ((DocsCanvas)_services)._parsedBlocks![vl.BlockIndex];
            var map = _services.IsVisual ? ((DocsCanvas)_services)._visualMaps?[vl.BlockIndex] : null;
            string blockText = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);

            // Account for where text actually starts on screen
            double textStartX = ((DocsCanvas)_services)._layoutEngine.GetTextStartXForVisualLine(vl);

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
                        var (imgW, _) = ((DocsCanvas)_services).GetImageSize(img.Value, ((DocsCanvas)_services)._layoutMaxWidth);
                        accum += imgW;
                    }
                    continue;
                }

                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                double charW = ((DocsCanvas)_services)._measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
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
            var vl = ((DocsCanvas)_services)._visualLines[vlIndex];
            if (vl.Length == 0) return vl.StartOffset;

            if (vl.Group != null)
                return HitTestInJoinedLine(vl, x);

            var parsed = ((DocsCanvas)_services)._parsedBlocks![vl.BlockIndex];
            if (_services.IsVisual && parsed.Table != null && parsed.TableRow != null
                && ((DocsCanvas)_services)._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            {
                return _services.HitTestInTableRow(vl, parsed, colWidths, x);
            }

            var map = _services.IsVisual ? ((DocsCanvas)_services)._visualMaps?[vl.BlockIndex] : null;
            string blockText = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);

            double accum = 0;

            if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
            {
                double prefixW = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                _services.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} has replacement prefix, prefixW={prefixW}, x={x}");
                if (x < prefixW)
                {
                    _services.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Click in prefix area, returning StartOffset={vl.StartOffset}");
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
                        var (imgW, _) = ((DocsCanvas)_services).GetImageSize(img.Value, ((DocsCanvas)_services)._layoutMaxWidth);
                        if (x < accum + imgW / 2)
                            return offset;
                        accum += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
                double charW = ((DocsCanvas)_services)._measure.MeasureCharWidth(blockText[offset], parsed.Kind, style);
                if (x < accum + charW / 2)
                {
                    _services.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} matched char at offset {offset} (accum={accum}, charW={charW})");
                    return offset;
                }
                accum += charW;
            }
            _services.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} past all chars, returning end offset {vl.StartOffset + vl.Length} (accum={accum}, x={x})");
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
                        var (imgW, _) = ((DocsCanvas)_services).GetImageSize(img.Value, ((DocsCanvas)_services)._layoutMaxWidth);
                        if (x < accum + imgW / 2)
                            return offset;
                        accum += imgW;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, offset, ref runIdx);
                double charW = ((DocsCanvas)_services)._measure.MeasureCharWidth(group.JoinedText[offset], BlockKind.Paragraph, style);

                // For soft breaks, account for visual space when hit-testing
                double testWidth = charW;
                if (softBreaks.Contains(offset) && group.JoinedText[offset] == '¶')
                {
                    double spaceW = ((DocsCanvas)_services)._measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
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
            if (((DocsCanvas)_services)._visualLines.Count == 0) return 0;
            for (int i = 0; i < ((DocsCanvas)_services)._visualLines.Count; i++)
            {
                double lineH = _services.GetEffectiveLineHeight(((DocsCanvas)_services)._visualLines[i]);
                if (y < ((DocsCanvas)_services)._lineYPositions[i] + lineH)
                    return i;
            }
            return ((DocsCanvas)_services)._visualLines.Count - 1;
        }

        internal void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
        {
            if (((DocsCanvas)_services)._visualLines.Count == 0) { blockIndex = 0; charOffset = 0; return; }
            double effectiveScroll = ((DocsCanvas)_services)._scroll.EffectiveOffset;
            int vli = HitTestVisualLine(pos.Y + effectiveScroll);
            var vl = ((DocsCanvas)_services)._visualLines[vli];
            double xForHitTest = pos.X - DocsCanvas._padding;

            int rawOffset = _services.IsVisual ? HitTestInVisualLineProper(vli, xForHitTest) : HitTestInVisualLine(vli, xForHitTest);

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
            _services.Logger?.Log(DocsLogLevel.Debug, $"HitTestToPosition: Click at ({pos.X}, {pos.Y}) -> Block {blockIndex}, Offset {charOffset}");
        }

        // --- Key handlers (navigation) ---

        internal void HandleLeft(bool shift, bool ctrl = false)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            if (ctrl)
            {
                if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
                {
                    var (sb, so, _, _) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
                    ((DocsCanvas)_services)._doc.CursorBlock = sb;
                    ((DocsCanvas)_services)._doc.CursorOffset = so;
                    ((DocsCanvas)_services)._doc.CollapseSelection();
                }
                else
                {
                    ((DocsCanvas)_services)._doc.MoveWordLeft();
                }
                if (_services.IsVisual)
                {
                    if (((DocsCanvas)_services)._parsedBlocks != null && DocsCanvas.IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
                        ((DocsCanvas)_services).ClampCursorToTableCell();
                    else
                        _services.SkipCursorOverHiddenRanges(forward: false);
                }
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            }
            else
            {
                if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleLeftVisual(shift);
                else HandleLeftSource(shift);
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            }
        }

        internal void HandleRight(bool shift, bool ctrl = false)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            if (ctrl)
            {
                if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
                {
                    var (_, _, eb, eo) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
                    ((DocsCanvas)_services)._doc.CursorBlock = eb;
                    ((DocsCanvas)_services)._doc.CursorOffset = eo;
                    ((DocsCanvas)_services)._doc.CollapseSelection();
                }
                else
                {
                    ((DocsCanvas)_services)._doc.MoveWordRight();
                }
                if (_services.IsVisual)
                {
                    if (((DocsCanvas)_services)._parsedBlocks != null && DocsCanvas.IsTableRow(((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock]))
                        ((DocsCanvas)_services).ClampCursorToTableCell();
                    else
                        _services.SkipCursorOverHiddenRanges(forward: true);
                }
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            }
            else
            {
                if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleRightVisual(shift);
                else HandleRightSource(shift);
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            }
        }

        internal void HandleHome(bool shift, bool ctrl)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            if (ctrl)
            {
                ((DocsCanvas)_services)._doc.CursorBlock = 0;
                ((DocsCanvas)_services)._doc.CursorOffset = 0;
            }
            else
            {
                int vli = CursorToVisualLineIndex();
                var vl = ((DocsCanvas)_services)._visualLines[vli];
                if (vl.Group != null)
                {
                    var (targetBi, targetBo) = vl.Group.JoinedToSource(vl.StartOffset);
                    if (((DocsCanvas)_services)._doc.CursorBlock == targetBi && ((DocsCanvas)_services)._doc.CursorOffset == targetBo
                        && vli > 0 && ((DocsCanvas)_services)._visualLines[vli - 1].Group == vl.Group)
                    {
                        var (firstBi, firstBo) = vl.Group.JoinedToSource(0);
                        ((DocsCanvas)_services)._doc.CursorBlock = firstBi;
                        ((DocsCanvas)_services)._doc.CursorOffset = firstBo;
                    }
                    else
                    {
                        ((DocsCanvas)_services)._doc.CursorBlock = targetBi;
                        ((DocsCanvas)_services)._doc.CursorOffset = targetBo;
                    }
                }
                else
                {
                    if (((DocsCanvas)_services)._doc.CursorOffset == vl.StartOffset
                        && vli > 0 && ((DocsCanvas)_services)._visualLines[vli - 1].BlockIndex == vl.BlockIndex)
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = 0;
                    }
                    else
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = vl.StartOffset;
                    }
                }
            }
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleHomeVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        internal void HandleEnd(bool shift, bool ctrl)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            ((DocsCanvas)_services)._cursorAtLineEnd = false;
            if (ctrl)
            {
                ((DocsCanvas)_services)._doc.CursorBlock = ((DocsCanvas)_services)._doc.BlockCount - 1;
                ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._doc.CursorBlock);
            }
            else
            {
                int vli = CursorToVisualLineIndex();
                var vl = ((DocsCanvas)_services)._visualLines[vli];
                int endOffset = vl.StartOffset + vl.Length;
                if (vl.Group != null)
                {
                    bool isWrap = vli + 1 < ((DocsCanvas)_services)._visualLines.Count
                        && ((DocsCanvas)_services)._visualLines[vli + 1].Group == vl.Group;
                    if (isWrap)
                    {
                        string text = vl.Group.JoinedText;
                        while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                            endOffset--;
                    }
                    var (targetBi, targetBo) = vl.Group.JoinedToSource(endOffset);
                    if (((DocsCanvas)_services)._doc.CursorBlock == targetBi && ((DocsCanvas)_services)._doc.CursorOffset == targetBo && isWrap)
                    {
                        var last = vl.Group.Segments[^1];
                        ((DocsCanvas)_services)._doc.CursorBlock = last.BlockIndex;
                        ((DocsCanvas)_services)._doc.CursorOffset = last.Length;
                    }
                    else
                    {
                        ((DocsCanvas)_services)._doc.CursorBlock = targetBi;
                        ((DocsCanvas)_services)._doc.CursorOffset = targetBo;
                    }
                }
                else
                {
                    bool isWrap = vli + 1 < ((DocsCanvas)_services)._visualLines.Count
                        && ((DocsCanvas)_services)._visualLines[vli + 1].BlockIndex == vl.BlockIndex;
                    if (isWrap)
                    {
                        string text = ((DocsCanvas)_services)._doc.GetBlockText(vl.BlockIndex);
                        while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                            endOffset--;
                    }
                    if (((DocsCanvas)_services)._doc.CursorOffset == endOffset && isWrap)
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = ((DocsCanvas)_services)._doc.GetBlockLength(vl.BlockIndex);
                    }
                    else
                    {
                        ((DocsCanvas)_services)._doc.CursorOffset = endOffset;
                    }
                }
                ((DocsCanvas)_services)._cursorAtLineEnd = true;
            }
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleEndVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        internal void HandleUp(bool shift)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            if (vli > 0)
            {
                double x = CursorXInVisualLine(vli);
                vli--;
                SetCursorFromVisualLine(vli, x);
            }
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleUpVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        internal void HandleDown(bool shift)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            if (vli < ((DocsCanvas)_services)._visualLines.Count - 1)
            {
                double x = CursorXInVisualLine(vli);
                vli++;
                SetCursorFromVisualLine(vli, x);
            }
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleDownVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        internal void HandlePageUp(bool shift)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            double x = CursorXInVisualLine(vli);
            double cursorY = ((DocsCanvas)_services)._lineYPositions[vli];
            double relativeY = cursorY - ((DocsCanvas)_services)._scroll.Offset;
            double lineH = _services.GetEffectiveLineHeight(((DocsCanvas)_services)._visualLines[vli]);
            double pageAmount = Math.Max(lineH, ((DocsCanvas)_services).ActualHeight - 3 * lineH);

            ((DocsCanvas)_services)._scroll.Offset -= pageAmount;
            ((DocsCanvas)_services)._scroll.Clamp();

            int targetVli = HitTestVisualLine(((DocsCanvas)_services)._scroll.Offset + relativeY);
            SetCursorFromVisualLine(targetVli, x);
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleUpVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        internal void HandlePageDown(bool shift)
        {
            ((DocsCanvas)_services).SealAndStopTimer();
            int vli = CursorToVisualLineIndex();
            double x = CursorXInVisualLine(vli);
            double cursorY = ((DocsCanvas)_services)._lineYPositions[vli];
            double relativeY = cursorY - ((DocsCanvas)_services)._scroll.Offset;
            double lineH = _services.GetEffectiveLineHeight(((DocsCanvas)_services)._visualLines[vli]);
            double pageAmount = Math.Max(lineH, ((DocsCanvas)_services).ActualHeight - 3 * lineH);

            ((DocsCanvas)_services)._scroll.Offset += pageAmount;
            ((DocsCanvas)_services)._scroll.Clamp();

            int targetVli = HitTestVisualLine(((DocsCanvas)_services)._scroll.Offset + relativeY);
            SetCursorFromVisualLine(targetVli, x);
            if (_services.IsVisual) ((DocsCanvas)_services)._visualModeManager.HandleDownVisual();
            if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
        }

        private void SetCursorFromVisualLine(int vli, double x)
        {
            var vl = ((DocsCanvas)_services)._visualLines[vli];
            int rawOffset = HitTestInVisualLine(vli, x);
            if (vl.Group != null)
            {
                var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
                ((DocsCanvas)_services)._doc.CursorBlock = bi;
                ((DocsCanvas)_services)._doc.CursorOffset = bo;
            }
            else
            {
                ((DocsCanvas)_services)._doc.CursorBlock = vl.BlockIndex;
                ((DocsCanvas)_services)._doc.CursorOffset = rawOffset;
            }
        }

        // --- Source mode handlers ---

        private bool HandleLeftSource(bool shift)
        {
            if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
            {
                var (sb, so, _, _) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
                ((DocsCanvas)_services)._doc.CursorBlock = sb;
                ((DocsCanvas)_services)._doc.CursorOffset = so;
                ((DocsCanvas)_services)._doc.CollapseSelection();
            }
            else
            {
                ((DocsCanvas)_services)._doc.MoveLeft();
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
            }
            return true;
        }

        private bool HandleRightSource(bool shift)
        {
            if (!shift && ((DocsCanvas)_services)._doc.HasSelection)
            {
                var (_, _, eb, eo) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
                ((DocsCanvas)_services)._doc.CursorBlock = eb;
                ((DocsCanvas)_services)._doc.CursorOffset = eo;
                ((DocsCanvas)_services)._doc.CollapseSelection();
            }
            else
            {
                ((DocsCanvas)_services)._doc.MoveRight();
                if (!shift) ((DocsCanvas)_services)._doc.CollapseSelection();
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
