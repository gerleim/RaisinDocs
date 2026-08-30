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
    private readonly ILayoutDataServices _layout;
    private readonly IDocumentServices _doc;
    private readonly IVisualModeServices _visual;
    private readonly ITableServices _table;
    private readonly IRenderingServices _rendering;
    private readonly IParsedContentServices _content;
    private readonly ILoggingServices _logging;
    private readonly IImageServices _image;
    private readonly IScrollServices _scroll;
    private readonly ICanvasOperations _canvas;
    private readonly INavigationServices _nav;

    // Set by DocsCanvas after construction
    internal VisualModeManager? VisualModeManager { get; set; }

    public CursorNavigationEngine(
        ILayoutDataServices layout,
        IDocumentServices doc,
        IVisualModeServices visual,
        ITableServices table,
        IRenderingServices rendering,
        IParsedContentServices content,
        ILoggingServices logging,
        IImageServices image,
        IScrollServices scroll,
        ICanvasOperations canvas,
        INavigationServices nav)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
    }

    // --- Cursor ↔ visual line mapping ---

    internal int CursorToVisualLineIndex()
    {
        for (int i = _layout.VisualLines.Count - 1; i >= 0; i--)
        {
            var vl = _layout.VisualLines[i];
            if (vl.Group != null)
            {
                int joined = vl.Group.SourceToJoined(_doc.Document.CursorBlock, _doc.Document.CursorOffset);
                if (joined >= 0 && joined >= vl.StartOffset && joined <= vl.StartOffset + vl.Length)
                {
                    if (_canvas.CursorAtLineEnd && joined == vl.StartOffset && i > 0
                        && _layout.VisualLines[i - 1].Group == vl.Group)
                        continue;
                    return i;
                }
            }
            else if (vl.BlockIndex == _doc.Document.CursorBlock && vl.StartOffset <= _doc.Document.CursorOffset)
            {
                if (_canvas.CursorAtLineEnd && vl.StartOffset == _doc.Document.CursorOffset && i > 0
                    && _layout.VisualLines[i - 1].BlockIndex == vl.BlockIndex)
                    continue;
                return i;
            }
        }
        return 0;
    }

    internal BlockVisualSpacing? GetVisualLineSpacing(VisualLine vl)
    {
        if (!_visual.IsVisual || _layout.VisualLineSpacings == null || vl.BlockIndex < 0)
            return null;

        // Find the index of this VisualLine
        int vlIndex = -1;
        for (int i = 0; i < _layout.VisualLines.Count; i++)
        {
            if (_layout.VisualLines[i] == vl)
            {
                vlIndex = i;
                break;
            }
        }

        if (vlIndex < 0 || vlIndex >= _layout.VisualLineSpacings.Count)
            return null;

        return _layout.VisualLineSpacings[vlIndex];
    }

    internal double CursorXInVisualLine(int vlIndex)
    {
        var vl = _layout.VisualLines[vlIndex];

        if (vl.Group != null)
        {
            int joinedOffset = vl.Group.SourceToJoined(_doc.Document.CursorBlock, _doc.Document.CursorOffset);
            int localOffset = Math.Clamp(joinedOffset - vl.StartOffset, 0, vl.Length);
            if (localOffset == 0) return 0;
            return _rendering.MeasureJoinedRange(vl.Group, vl.StartOffset, localOffset);
        }

        int localOff = Math.Clamp(_doc.Document.CursorOffset - vl.StartOffset, 0, vl.Length);
        var map = _visual.IsVisual ? _visual.VisualMaps?[vl.BlockIndex] : null;

        var parsed = _content.ParsedBlocks![vl.BlockIndex];
        if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null
            && _table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return _table.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, localOff);
        }

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        double x = _layout.GetTextStartXForVisualLine(vl);

        // Subtract padding since we're returning cursor x relative to control left edge
        // (ContentStartX from cache already accounts for ReplacementPrefix width)
        x -= DocsCanvas._padding;

        if (localOff == 0) return x;

        if (map == null)
        {
            string lineText = blockText.Substring(vl.StartOffset, vl.Length);
            var ft = new System.Windows.Media.FormattedText(lineText, System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, TextMeasurer.GetBlockBaseTypeface(vl.BlockKind),
                _rendering.Measure.GetBlockFontSize(vl.BlockKind), _rendering.Palette.Foreground, _rendering.Measure.DpiScale);
            _nav.ApplyInlineStyles(ft, vl, parsed, blockText);
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
                    var (imgW, _) = _image.GetImageSize(img.Value, _layout.LayoutMaxWidth);
                    x += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            x += _rendering.Measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
        }
        return x;
    }

    internal double MeasureJoinedRange(ParagraphGroup group, int start, int length)
    {
        double width = _rendering.MeasureRangeWidth(group.JoinedText, start, length,
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
                double spaceW = _rendering.Measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);
                width += spaceW;
            }
        }

        return width;
    }

    internal int HitTestInVisualLineProper(int vlIndex, double clickX)
    {
        var vl = _layout.VisualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        // Joined paragraph groups have their own text/parse/map and their offsets are
        // relative to the joined text, not to the source block.
        if (vl.Group != null)
            return HitTestInJoinedLine(vl, clickX);

        var parsed = _content.ParsedBlocks![vl.BlockIndex];
        var map = _visual.IsVisual ? _visual.VisualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);

        // Account for where text actually starts on screen
        double textStartX = _layout.GetTextStartXForVisualLine(vl);

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
                    var (imgW, _) = _image.GetImageSize(img.Value, _layout.LayoutMaxWidth);
                    accum += imgW;
                }
                continue;
            }

            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
            double charW = _rendering.Measure.MeasureCharWidth(blockText[i], parsed.Kind, style);
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
        var vl = _layout.VisualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        if (vl.Group != null)
            return HitTestInJoinedLine(vl, x);

        var parsed = _content.ParsedBlocks![vl.BlockIndex];
        if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null
            && _table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return _table.HitTestInTableRow(vl, parsed, colWidths, x);
        }

        var map = _visual.IsVisual ? _visual.VisualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);

        double accum = 0;

        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            double prefixW = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
            _logging.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} has replacement prefix, prefixW={prefixW}, x={x}");
            if (x < prefixW)
            {
                _logging.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Click in prefix area, returning StartOffset={vl.StartOffset}");
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
                    var (imgW, _) = _image.GetImageSize(img.Value, _layout.LayoutMaxWidth);
                    if (x < accum + imgW / 2)
                        return offset;
                    accum += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, offset, ref runIdx);
            double charW = _rendering.Measure.MeasureCharWidth(blockText[offset], parsed.Kind, style);
            if (x < accum + charW / 2)
            {
                _logging.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} matched char at offset {offset} (accum={accum}, charW={charW})");
                return offset;
            }
            accum += charW;
        }
        _logging.Logger?.Log(DocsLogLevel.Debug, $"HitTestInVisualLine: Block {vl.BlockIndex} past all chars, returning end offset {vl.StartOffset + vl.Length} (accum={accum}, x={x})");
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
                    var (imgW, _) = _image.GetImageSize(img.Value, _layout.LayoutMaxWidth);
                    if (x < accum + imgW / 2)
                        return offset;
                    accum += imgW;
                    i += img.Value.Length - 1;
                }
                continue;
            }
            var style = TextMeasurer.GetStyleAtOffset(group.JoinedParsed.Runs, offset, ref runIdx);
            double charW = _rendering.Measure.MeasureCharWidth(group.JoinedText[offset], BlockKind.Paragraph, style);

            // A soft break renders as pilcrow + visual space, so it occupies both widths
            double testWidth = charW;
            if (softBreaks.Contains(offset) && group.JoinedText[offset] == '¶')
                testWidth += _rendering.Measure.MeasureCharWidth(' ', BlockKind.Paragraph, style);

            // Check if click is in this character's area
            if (x < accum + testWidth / 2)
                return offset;

            // Advance by the full rendered width, otherwise every soft break on the line
            // shifts all following hit-test results to the right.
            accum += testWidth;
        }
        return vl.StartOffset + vl.Length;
    }

    internal int HitTestVisualLine(double y)
    {
        if (_layout.VisualLines.Count == 0) return 0;
        for (int i = 0; i < _layout.VisualLines.Count; i++)
        {
            double lineH = _layout.GetEffectiveLineHeight(_layout.VisualLines[i]);
            if (y < _layout.LineYPositions[i] + lineH)
                return i;
        }
        return _layout.VisualLines.Count - 1;
    }

    internal void HitTestToPosition(Point pos, out int blockIndex, out int charOffset)
    {
        if (_layout.VisualLines.Count == 0) { blockIndex = 0; charOffset = 0; return; }
        double effectiveScroll = _scroll.Scroll.EffectiveOffset;
        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
        var vl = _layout.VisualLines[vli];
        double xForHitTest = pos.X - DocsCanvas._padding;

        int rawOffset = _visual.IsVisual ? HitTestInVisualLineProper(vli, xForHitTest) : HitTestInVisualLine(vli, xForHitTest);

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
        _logging.Logger?.Log(DocsLogLevel.Debug, $"HitTestToPosition: Click at ({pos.X}, {pos.Y}) -> Block {blockIndex}, Offset {charOffset}");
    }

    // --- Key handlers (navigation) ---

    internal void HandleLeft(bool shift, bool ctrl = false)
    {
        _canvas.SealAndStopTimer();
        if (ctrl)
        {
            if (!shift && _doc.Document.HasSelection)
            {
                var (sb, so, _, _) = _doc.Document.GetOrderedSelection();
                _doc.Document.CursorBlock = sb;
                _doc.Document.CursorOffset = so;
                _doc.Document.CollapseSelection();
            }
            else
            {
                _doc.Document.MoveWordLeft();
            }
            if (_visual.IsVisual)
            {
                if (_content.ParsedBlocks != null && DocsCanvas.IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                    VisualModeManager?.ClampCursorToTableCell();
                else
                    _visual.SkipCursorOverHiddenRanges(forward: false);
            }
            if (!shift) _doc.Document.CollapseSelection();
        }
        else
        {
            if (_visual.IsVisual) VisualModeManager?.HandleLeftVisual(shift);
            else HandleLeftSource(shift);
            if (!shift) _doc.Document.CollapseSelection();
        }
    }

    internal void HandleRight(bool shift, bool ctrl = false)
    {
        _canvas.SealAndStopTimer();
        if (ctrl)
        {
            if (!shift && _doc.Document.HasSelection)
            {
                var (_, _, eb, eo) = _doc.Document.GetOrderedSelection();
                _doc.Document.CursorBlock = eb;
                _doc.Document.CursorOffset = eo;
                _doc.Document.CollapseSelection();
            }
            else
            {
                _doc.Document.MoveWordRight();
            }
            if (_visual.IsVisual)
            {
                if (_content.ParsedBlocks != null && DocsCanvas.IsTableRow(_content.ParsedBlocks[_doc.Document.CursorBlock]))
                    VisualModeManager?.ClampCursorToTableCell();
                else
                    _visual.SkipCursorOverHiddenRanges(forward: true);
            }
            if (!shift) _doc.Document.CollapseSelection();
        }
        else
        {
            if (_visual.IsVisual) VisualModeManager?.HandleRightVisual(shift);
            else HandleRightSource(shift);
            if (!shift) _doc.Document.CollapseSelection();
        }
    }

    internal void HandleHome(bool shift, bool ctrl)
    {
        _canvas.SealAndStopTimer();
        if (ctrl)
        {
            _doc.Document.CursorBlock = 0;
            _doc.Document.CursorOffset = 0;
        }
        else
        {
            int vli = CursorToVisualLineIndex();
            var vl = _layout.VisualLines[vli];
            if (vl.Group != null)
            {
                var (targetBi, targetBo) = vl.Group.JoinedToSource(vl.StartOffset);
                if (_doc.Document.CursorBlock == targetBi && _doc.Document.CursorOffset == targetBo
                    && vli > 0 && _layout.VisualLines[vli - 1].Group == vl.Group)
                {
                    var (firstBi, firstBo) = vl.Group.JoinedToSource(0);
                    _doc.Document.CursorBlock = firstBi;
                    _doc.Document.CursorOffset = firstBo;
                }
                else
                {
                    _doc.Document.CursorBlock = targetBi;
                    _doc.Document.CursorOffset = targetBo;
                }
            }
            else
            {
                if (_doc.Document.CursorOffset == vl.StartOffset
                    && vli > 0 && _layout.VisualLines[vli - 1].BlockIndex == vl.BlockIndex)
                {
                    _doc.Document.CursorOffset = 0;
                }
                else
                {
                    _doc.Document.CursorOffset = vl.StartOffset;
                }
            }
        }
        if (_visual.IsVisual) VisualModeManager?.HandleHomeVisual();
        if (!shift) _doc.Document.CollapseSelection();
    }

    internal void HandleEnd(bool shift, bool ctrl)
    {
        _canvas.SealAndStopTimer();
        _canvas.CursorAtLineEnd = false;
        if (ctrl)
        {
            _doc.Document.CursorBlock = _doc.BlockCount - 1;
            _doc.Document.CursorOffset = _doc.GetBlockLength(_doc.Document.CursorBlock);
        }
        else
        {
            int vli = CursorToVisualLineIndex();
            var vl = _layout.VisualLines[vli];
            int endOffset = vl.StartOffset + vl.Length;
            if (vl.Group != null)
            {
                bool isWrap = vli + 1 < _layout.VisualLines.Count
                    && _layout.VisualLines[vli + 1].Group == vl.Group;
                if (isWrap)
                {
                    string text = vl.Group.JoinedText;
                    while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                        endOffset--;
                }
                var (targetBi, targetBo) = vl.Group.JoinedToSource(endOffset);
                if (_doc.Document.CursorBlock == targetBi && _doc.Document.CursorOffset == targetBo && isWrap)
                {
                    var last = vl.Group.Segments[^1];
                    _doc.Document.CursorBlock = last.BlockIndex;
                    _doc.Document.CursorOffset = last.Length;
                }
                else
                {
                    _doc.Document.CursorBlock = targetBi;
                    _doc.Document.CursorOffset = targetBo;
                }
            }
            else
            {
                bool isWrap = vli + 1 < _layout.VisualLines.Count
                    && _layout.VisualLines[vli + 1].BlockIndex == vl.BlockIndex;
                if (isWrap)
                {
                    string text = _doc.GetBlockText(vl.BlockIndex);
                    while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                        endOffset--;
                }
                if (_doc.Document.CursorOffset == endOffset && isWrap)
                {
                    _doc.Document.CursorOffset = _doc.GetBlockLength(vl.BlockIndex);
                }
                else
                {
                    _doc.Document.CursorOffset = endOffset;
                }
            }
            _canvas.CursorAtLineEnd = true;
        }
        if (_visual.IsVisual) VisualModeManager?.HandleEndVisual();
        if (!shift) _doc.Document.CollapseSelection();
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
        if (_visual.IsVisual) VisualModeManager?.HandleUpVisual();
        if (!shift) _doc.Document.CollapseSelection();
    }

    internal void HandleDown(bool shift)
    {
        _canvas.SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        if (vli < _layout.VisualLines.Count - 1)
        {
            double x = CursorXInVisualLine(vli);
            vli++;
            SetCursorFromVisualLine(vli, x);
        }
        if (_visual.IsVisual) VisualModeManager?.HandleDownVisual();
        if (!shift) _doc.Document.CollapseSelection();
    }

    internal void HandlePageUp(bool shift)
    {
        _canvas.SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        double x = CursorXInVisualLine(vli);
        double cursorY = _layout.LineYPositions[vli];
        double relativeY = cursorY - _scroll.Scroll.Offset;
        double lineH = _layout.GetEffectiveLineHeight(_layout.VisualLines[vli]);
        double pageAmount = Math.Max(lineH, _rendering.ActualHeight - 3 * lineH);

        _scroll.Scroll.Offset -= pageAmount;
        _scroll.Scroll.Clamp();

        int targetVli = HitTestVisualLine(_scroll.Scroll.Offset + relativeY);
        SetCursorFromVisualLine(targetVli, x);
        if (_visual.IsVisual) VisualModeManager?.HandleUpVisual();
        if (!shift) _doc.Document.CollapseSelection();
    }

    internal void HandlePageDown(bool shift)
    {
        _canvas.SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        double x = CursorXInVisualLine(vli);
        double cursorY = _layout.LineYPositions[vli];
        double relativeY = cursorY - _scroll.Scroll.Offset;
        double lineH = _layout.GetEffectiveLineHeight(_layout.VisualLines[vli]);
        double pageAmount = Math.Max(lineH, _rendering.ActualHeight - 3 * lineH);

        _scroll.Scroll.Offset += pageAmount;
        _scroll.Scroll.Clamp();

        int targetVli = HitTestVisualLine(_scroll.Scroll.Offset + relativeY);
        SetCursorFromVisualLine(targetVli, x);
        if (_visual.IsVisual) VisualModeManager?.HandleDownVisual();
        if (!shift) _doc.Document.CollapseSelection();
    }

    private void SetCursorFromVisualLine(int vli, double x)
    {
        var vl = _layout.VisualLines[vli];
        int rawOffset = HitTestInVisualLine(vli, x);
        if (vl.Group != null)
        {
            var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
            _doc.Document.CursorBlock = bi;
            _doc.Document.CursorOffset = bo;
        }
        else
        {
            _doc.Document.CursorBlock = vl.BlockIndex;
            _doc.Document.CursorOffset = rawOffset;
        }
    }

    // --- Source mode handlers ---

    private bool HandleLeftSource(bool shift)
    {
        if (!shift && _doc.Document.HasSelection)
        {
            var (sb, so, _, _) = _doc.Document.GetOrderedSelection();
            _doc.Document.CursorBlock = sb;
            _doc.Document.CursorOffset = so;
            _doc.Document.CollapseSelection();
        }
        else
        {
            _doc.Document.MoveLeft();
            if (!shift) _doc.Document.CollapseSelection();
        }
        return true;
    }

    private bool HandleRightSource(bool shift)
    {
        if (!shift && _doc.Document.HasSelection)
        {
            var (_, _, eb, eo) = _doc.Document.GetOrderedSelection();
            _doc.Document.CursorBlock = eb;
            _doc.Document.CursorOffset = eo;
            _doc.Document.CollapseSelection();
        }
        else
        {
            _doc.Document.MoveRight();
            if (!shift) _doc.Document.CollapseSelection();
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
