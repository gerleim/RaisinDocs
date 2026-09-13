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
        int offset = vl.Group != null
            ? vl.Group.SourceToJoined(_doc.Document.CursorBlock, _doc.Document.CursorOffset)
            : _doc.Document.CursorOffset;

        return XInVisualLine(vlIndex, offset);
    }

    /// <summary>
    /// The caret's box on visual line <paramref name="vlIndex"/>: its X, relative to the left
    /// padding, and the top and height of the part of the line it is drawn across.
    /// </summary>
    internal (double X, double Top, double Height) CaretBox(int vlIndex)
    {
        var vl = _layout.VisualLines[vlIndex];
        int offset = vl.Group != null
            ? vl.Group.SourceToJoined(_doc.Document.CursorBlock, _doc.Document.CursorOffset)
            : _doc.Document.CursorOffset;

        return PositionInVisualLine(vlIndex, offset);
    }

    /// <summary>
    /// Where <paramref name="offset"/> is drawn on visual line <paramref name="vlIndex"/>: its X as
    /// <see cref="XInVisualLine"/> gives it, and the top and height, within the line, of the text
    /// line it is on.
    /// </summary>
    /// <remarks>
    /// Every line is one text line tall except a table row whose cells wrap, which is as many as
    /// its tallest cell: there the answer is the one line of the cell the offset is on. Everything
    /// drawn at an offset - the caret, and the scroll that keeps it in view - takes its Y from here.
    /// </remarks>
    internal (double X, double Top, double Height) PositionInVisualLine(int vlIndex, int offset)
    {
        var vl = _layout.VisualLines[vlIndex];
        if (_visual.IsVisual && vl.Group == null && vl.TableLayout != null
            && _content.ParsedBlocks![vl.BlockIndex] is { TableRow: not null, Table: { } table } parsed
            && _table.TableColumnWidths.TryGetValue(table, out var colWidths))
        {
            var pos = _table.PositionInTableRow(vlIndex, vl, parsed, colWidths, offset);
            double baseH = _rendering.Measure.GetLineHeight(vl.BlockKind);
            return (pos.X, pos.SubLine * baseH, baseH);
        }

        return (XInVisualLine(vlIndex, offset), 0, _layout.GetEffectiveLineHeight(vl));
    }

    /// <summary>
    /// The X of <paramref name="offset"/> on visual line <paramref name="vlIndex"/>, relative to
    /// the left padding - so the thing drawn there goes at <c>_padding + x</c>. For a line in a
    /// joined paragraph group the offset is in the group's joined text; otherwise it is an offset
    /// in the line's own block.
    /// </summary>
    /// <remarks>
    /// The one place that answers this question. The caret, both ends of the selection, the search
    /// highlights and the spelling squiggles all come through here, because anything that measures
    /// the same span its own way ends up drawing beside the text rather than over it: the earlier
    /// highlight code measured from the left margin and added the width of the list marker's
    /// replacement text, which is not the marker column the text is actually laid out on, and is
    /// not there at all on a wrapped line. Within the line the positions come off the glyphs WPF
    /// actually drew, because summed advance widths miss kerning.
    /// </remarks>
    internal double XInVisualLine(int vlIndex, int offset)
    {
        var vl = _layout.VisualLines[vlIndex];

        if (vl.Group != null)
        {
            int localOffset = Math.Clamp(offset - vl.StartOffset, 0, vl.Length);
            if (localOffset == 0) return 0;
            if (_rendering.LaidOutX(vlIndex, offset) is { } joinedX) return joinedX;
            return _rendering.MeasureJoinedRange(vl.Group, vl.StartOffset, localOffset);
        }

        int localOff = Math.Clamp(offset - vl.StartOffset, 0, vl.Length);
        var map = _visual.IsVisual ? _visual.VisualMaps?[vl.BlockIndex] : null;

        var parsed = _content.ParsedBlocks![vl.BlockIndex];
        if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null
            && _table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return _table.PositionInTableRow(vlIndex, vl, parsed, colWidths, localOff).X;
        }

        string blockText = _doc.GetBlockText(vl.BlockIndex);

        // The text column the line is laid out on: the marker column for a list item, the
        // indent for a blockquote or a nested block, and the same on the block's wrapped lines
        // as on its first. Relative to the control's left edge, so drop the padding back off.
        double x = _layout.GetTextStartXForVisualLine(vl, vlIndex) - DocsCanvas._padding;

        if (localOff == 0) return x;

        // Off the glyphs WPF drew, whenever the line is drawn as one run of text. The sums below
        // are what is left for the lines that are not - a line carrying an image.
        if (_rendering.LaidOutX(vlIndex, vl.StartOffset + localOff) is { } laidOutX)
            return x + laidOutX;

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

    /// <param name="localY">
    /// How far below the line's top the point is. Only a table row with wrapped cells reads it, to
    /// pick the line of the cell.
    /// </param>
    internal int HitTestInVisualLineProper(int vlIndex, double clickX, double localY = 0)
    {
        var vl = _layout.VisualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        // Joined paragraph groups have their own text/parse/map and their offsets are
        // relative to the joined text, not to the source block.
        if (vl.Group != null)
            return HitTestInJoinedLine(vlIndex, vl, clickX);

        var parsed = _content.ParsedBlocks![vl.BlockIndex];

        // Table rows are laid out in padded columns, not as a run of raw text, so they need
        // the table-aware hit test (mirrors CursorXInVisualLine).
        if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null
            && _table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return _table.HitTestInTableRow(vlIndex, vl, parsed, colWidths, clickX, localY);
        }

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
        bool laidOut = _rendering.LaidOutX(vlIndex, vl.StartOffset) != null;

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

            // Where the glyph really is - the same stops the caret is drawn at - so a click lands
            // on the character under the pointer rather than one kerned pair to the side of it.
            if (laidOut)
            {
                charStart = _rendering.LaidOutX(vlIndex, i) ?? charStart;
                charEnd = _rendering.LaidOutX(vlIndex, i + 1) ?? charEnd;
            }

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

    internal int HitTestInVisualLine(int vlIndex, double x, double localY = 0)
    {
        var vl = _layout.VisualLines[vlIndex];
        if (vl.Length == 0) return vl.StartOffset;

        if (vl.Group != null)
            return HitTestInJoinedLine(vlIndex, vl, x);

        var parsed = _content.ParsedBlocks![vl.BlockIndex];
        if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null
            && _table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
        {
            return _table.HitTestInTableRow(vlIndex, vl, parsed, colWidths, x, localY);
        }

        var map = _visual.IsVisual ? _visual.VisualMaps?[vl.BlockIndex] : null;
        string blockText = _doc.GetBlockText(vl.BlockIndex);

        double accum = 0;

        if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
        {
            double prefixW = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
            if (x < prefixW)
            {
                return vl.StartOffset;
            }
            accum = prefixW;
        }

        // Source mode only: in visual mode this measures from the margin plus the prefix, which
        // is not the column the laid-out stops are relative to.
        bool laidOut = map == null && _rendering.LaidOutX(vlIndex, vl.StartOffset) != null;

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
            if (laidOut)
            {
                double start = _rendering.LaidOutX(vlIndex, offset) ?? accum;
                double end = _rendering.LaidOutX(vlIndex, offset + 1) ?? accum + charW;
                if (x < (start + end) / 2) return offset;
                accum = end;
                continue;
            }
            if (x < accum + charW / 2)
            {
                return offset;
            }
            accum += charW;
        }
        return vl.StartOffset + vl.Length;
    }

    internal int HitTestInJoinedLine(int vlIndex, VisualLine vl, double x)
    {
        var group = vl.Group!;
        var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
        double accum = 0;
        int runIdx = 0;
        bool laidOut = _rendering.LaidOutX(vlIndex, vl.StartOffset) != null;

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

            if (laidOut)
            {
                // The next offset's stop already sits past a soft break's visual space.
                double start = _rendering.LaidOutX(vlIndex, offset) ?? accum;
                double end = _rendering.LaidOutX(vlIndex, offset + 1) ?? accum + testWidth;
                if (x < (start + end) / 2) return offset;
                accum = end;
                continue;
            }

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
        double localY = pos.Y + effectiveScroll - _layout.LineYPositions[vli];

        int rawOffset = _visual.IsVisual
            ? HitTestInVisualLineProper(vli, xForHitTest, localY)
            : HitTestInVisualLine(vli, xForHitTest, localY);

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
    }

    // --- Key handlers (navigation) ---

    /// <summary>
    /// Ends the current undo group, then makes sure the layout the caller is about to read is
    /// current. Sealing raises <c>ContentChanged</c>, which invalidates the layout and drops the
    /// parsed blocks; a handler that sealed and then read the visual lines would be reading lines
    /// left over from before the seal, with no parse behind them.
    /// </summary>
    private void SealAndEnsureLayout()
    {
        _canvas.SealAndStopTimer();
        _layout.ComputeLayout();
    }

    internal void HandleLeft(bool shift, bool ctrl = false)
    {
        SealAndEnsureLayout();
        if (ctrl)
        {
            if (!shift && _doc.Document.HasSelection)
            {
                var (sb, so, _, _) = _doc.Document.GetOrderedSelection();
                _doc.Document.CursorBlock = sb;
                _doc.Document.CursorOffset = so;
                _doc.Document.CollapseSelection();
            }
            else if (!(_visual.IsVisual && VisualModeManager?.TryMoveToAdjacentTableCell(forward: false) == true))
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
        SealAndEnsureLayout();
        if (ctrl)
        {
            if (!shift && _doc.Document.HasSelection)
            {
                var (_, _, eb, eo) = _doc.Document.GetOrderedSelection();
                _doc.Document.CursorBlock = eb;
                _doc.Document.CursorOffset = eo;
                _doc.Document.CollapseSelection();
            }
            else if (!(_visual.IsVisual && VisualModeManager?.TryMoveToAdjacentTableCell(forward: true) == true))
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
        SealAndEnsureLayout();
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
        SealAndEnsureLayout();
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

    // --- Vertical goal column ---
    //
    // A run of Up/Down keeps aiming at the x the run started from, so passing through a short or
    // empty line does not truncate the column: from "fir|st paragraph", Down lands on the empty
    // line (x 0 is all it has), and the next Down still arrives at "sec|ond paragraph".
    //
    // The goal is tagged with the cursor position it was left at. Any other cursor movement or
    // edit moves the cursor off that position, so the goal is discarded without every other code
    // path having to know about it.
    private double _goalX;
    private int _goalBlock = -1;
    private int _goalOffset = -1;

    private double VerticalGoalX(int vli) =>
        _goalBlock == _doc.Document.CursorBlock && _goalOffset == _doc.Document.CursorOffset
            ? _goalX
            : CursorXInVisualLine(vli);

    private void RememberVerticalGoal(double x)
    {
        _goalX = x;
        _goalBlock = _doc.Document.CursorBlock;
        _goalOffset = _doc.Document.CursorOffset;
    }

    internal void HandleUp(bool shift)
    {
        SealAndEnsureLayout();
        int vli = CursorToVisualLineIndex();
        double x = VerticalGoalX(vli);
        if (vli > 0)
        {
            vli--;
            SetCursorFromVisualLine(vli, x);
        }
        if (_visual.IsVisual) VisualModeManager?.HandleUpVisual();
        if (!shift) _doc.Document.CollapseSelection();
        RememberVerticalGoal(x);
    }

    internal void HandleDown(bool shift)
    {
        SealAndEnsureLayout();
        int vli = CursorToVisualLineIndex();
        double x = VerticalGoalX(vli);
        if (vli < _layout.VisualLines.Count - 1)
        {
            vli++;
            SetCursorFromVisualLine(vli, x);
        }
        if (_visual.IsVisual) VisualModeManager?.HandleDownVisual();
        if (!shift) _doc.Document.CollapseSelection();
        RememberVerticalGoal(x);
    }

    internal void HandlePageUp(bool shift)
    {
        SealAndEnsureLayout();
        int vli = CursorToVisualLineIndex();
        double x = VerticalGoalX(vli);
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
        RememberVerticalGoal(x);
    }

    internal void HandlePageDown(bool shift)
    {
        SealAndEnsureLayout();
        int vli = CursorToVisualLineIndex();
        double x = VerticalGoalX(vli);
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
        RememberVerticalGoal(x);
    }

    private void SetCursorFromVisualLine(int vli, double x)
    {
        var vl = _layout.VisualLines[vli];
        // x came from CursorXInVisualLine, which measures from the line's text column. Only the
        // Proper hit test reads that same column back; the plain one starts at the replacement
        // prefix's width instead, and the two disagree by however far the marker column sits
        // from the prefix - enough to drop the cursor a character or two off the goal.
        int rawOffset = _visual.IsVisual
            ? HitTestInVisualLineProper(vli, x)
            : HitTestInVisualLine(vli, x);
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
