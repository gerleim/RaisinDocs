using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RaisinDocs;

public partial class DocsCanvas
{
    // --- Mouse wheel ---

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            double anchorY = e.GetPosition(this).Y;
            if (e.Delta > 0) ZoomIn(anchorY);
            else if (e.Delta < 0) ZoomOut(anchorY);
            e.Handled = true;
            return;
        }
        ComputeLayout();
        double scaledDelta = e.Delta * _measure.ZoomFactor;
        _scroll.HandleWheel(scaledDelta);
        e.Handled = true;
    }

    // --- Mouse ---

    private bool _doubleClickDrag;
    private int _doubleClickBlock;
    private int _doubleClickStart;
    private int _doubleClickEnd;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        _cursorAtLineEnd = false;
        _pendingStyleOff = null;

        if (e.ChangedButton == MouseButton.Right)
        {
            Focus();
            ComputeLayout();
            var rpos = e.GetPosition(this);
            HitTestToPosition(rpos, out int rBlock, out int rOffset);
            if (!IsWithinSelection(rBlock, rOffset))
            {
                _doc.CursorBlock = rBlock;
                _doc.CursorOffset = rOffset;
                if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                    ClampCursorToTableCell();
                else
                {
                    SkipCursorOverHiddenRanges(forward: true);
                    ClampCursorBeforeTrailingHidden();
                }
                _doc.CollapseSelection();
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        Focus();
        ComputeLayout();

        var pos = e.GetPosition(this);

        if (IsVisual && TryToggleTaskListCheckbox(pos))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && TryOpenLinkAtClick(pos))
        {
            e.Handled = true;
            return;
        }

        SealAndStopTimer();

        HitTestToPosition(pos, out int block, out int offset);

        if (e.ClickCount == 2)
        {
            _doc.SelectWord(block, offset);
            var (ws, we) = _doc.GetWordBoundaries(block, offset);
            _doubleClickDrag = true;
            _doubleClickBlock = block;
            _doubleClickStart = ws;
            _doubleClickEnd = we;
            CaptureMouse();
            ResetBlink();
            InvalidateVisual();
            return;
        }

        _doc.CursorBlock = block;
        _doc.CursorOffset = offset;
        if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
            ClampCursorToTableCell();
        else
        {
            SkipCursorOverHiddenRanges(forward: true);
            ClampCursorBeforeTrailingHidden();
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _doc.CollapseSelection();

        CaptureMouse();
        ResetBlink();
        InvalidateVisual();
        RaiseFormattingChanged();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);
        if (!IsMouseCaptured)
        {
            {
                ComputeLayout();
                var hoverLink = GetLinkAtPosition(pos);
                if (hoverLink != null)
                {
                    Cursor = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? Cursors.Hand : Cursors.IBeam;
                    var url = hoverLink.Value.Url;
                    if (_hoveredLinkUrl != url)
                    {
                        _hoveredLinkUrl = url;
                        double effectiveScroll = _scroll.EffectiveOffset;
                        int vli = HitTestVisualLine(pos.Y + effectiveScroll);
                        double lineY = _lineYPositions[vli] - effectiveScroll;
                        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
                        _linkToolTip.Content = url;
                        _linkToolTip.PlacementTarget = this;
                        _linkToolTip.HorizontalOffset = _padding;
                        _linkToolTip.VerticalOffset = lineY + lineH;
                        _linkToolTip.IsOpen = true;
                    }
                }
                else
                {
                    Cursor = Cursors.IBeam;
                    if (_hoveredLinkUrl != null)
                    {
                        _hoveredLinkUrl = null;
                        _linkToolTip.IsOpen = false;
                    }
                }
            }
            UpdateHoverImage(pos);
            return;
        }

        ComputeLayout();
        HitTestToPosition(pos, out int block, out int offset);

        if (_doubleClickDrag)
        {
            var (ws, we) = _doc.GetWordBoundaries(block, Math.Max(0, offset - 1));
            int cmp = Document.ComparePositions(block, offset, _doubleClickBlock, _doubleClickStart);
            if (cmp < 0)
            {
                _doc.AnchorBlock = _doubleClickBlock;
                _doc.AnchorOffset = _doubleClickEnd;
                _doc.CursorBlock = block;
                _doc.CursorOffset = ws;
            }
            else
            {
                _doc.AnchorBlock = _doubleClickBlock;
                _doc.AnchorOffset = _doubleClickStart;
                _doc.CursorBlock = block;
                _doc.CursorOffset = we;
            }
        }
        else
        {
            _doc.CursorBlock = block;
            _doc.CursorOffset = offset;
            if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                ClampCursorToTableCell();
            else
            {
                SkipCursorOverHiddenRanges(forward: true);
                ClampCursorBeforeTrailingHidden();
            }
        }

        ResetBlink();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _doubleClickDrag = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            RaiseFormattingChanged();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredImage != null)
        {
            _hoveredImage = null;
            InvalidateVisual();
        }
        if (_hoveredLinkUrl != null)
        {
            _hoveredLinkUrl = null;
            _linkToolTip.IsOpen = false;
        }
    }

    private void UpdateHoverImage(Point pos)
    {
        if (IsVisual || _imagePreview != ImagePreviewMode.OnHover || _parsedBlocks == null)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        ComputeLayout();
        double effectiveScroll = _scroll.EffectiveOffset;
        double hitY = pos.Y + effectiveScroll;
        int vli = HitTestVisualLine(hitY);
        if (vli < 0 || vli >= _visualLines.Count)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        var vl = _visualLines[vli];
        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Images == null)
        {
            if (_hoveredImage != null) { _hoveredImage = null; InvalidateVisual(); }
            return;
        }

        int offset = HitTestInVisualLine(vli, pos.X - _padding);
        InlineImage? found = null;
        foreach (var img in parsed.Images)
        {
            if (offset >= img.Start && offset < img.Start + img.Length)
            {
                found = img;
                break;
            }
        }

        if (found != _hoveredImage)
        {
            _hoveredImage = found;
            _hoverPosition = pos;
            InvalidateVisual();
        }
    }

    // --- Text input ---

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        if (IsReadOnly) return;
        _cursorAtLineEnd = false;

        if (_lastAction != LastActionKind.Typing)
        {
            _doc.SealUndoGroup();
            _lastAction = LastActionKind.Typing;
        }
        _doc.BeginUndoGroup();

        InsertTextCore(e.Text);
        EnsureCursorVisible();
        e.Handled = true;
    }

    private void InsertTextCore(string text)
    {
        var pendingOff = _pendingStyleOff;
        _pendingStyleOff = null;

        if (_doc.HasSelection)
        {
            pendingOff = null;
            var rect = TryGetTableRectSelection();
            if (rect != null)
            {
                ClearTableRectCells(rect.Value);
                MoveCursorToRectStart(rect.Value);
            }
            else
                _doc.DeleteSelection();
        }

        if (pendingOff is { } p)
        {
            _doc.InsertTextAt(_doc.CursorBlock, _doc.CursorOffset, p.Marker);
            _doc.CursorOffset += p.Marker.Length;
        }

        foreach (char c in text)
        {
            if (c < ' ' && c != '\t') continue;
            _doc.Insert(c);
        }

        if (pendingOff is { } pAfter)
            _doc.InsertTextAt(_doc.CursorBlock, _doc.CursorOffset, pAfter.Marker);

        _doc.CollapseSelection();

        ResetUndoSealTimer();
        ResetBlink();
        InvalidateLayout();
        if (IsVisual)
        {
            ComputeLayout();
            ClampCursorBeforeTrailingHidden();
            _doc.CollapseSelection();
        }
    }

    // --- Key handlers (Source / Visual dispatch) ---

    private void HandleBack(bool shift, out bool textChanged)
    {
        textChanged = false;
        if (_lastAction != LastActionKind.Deleting)
        {
            _doc.SealUndoGroup();
            _lastAction = LastActionKind.Deleting;
        }
        _doc.BeginUndoGroup();
        if (_doc.HasSelection)
        {
            var rect = TryGetTableRectSelection();
            if (rect != null)
                ClearTableRectCells(rect.Value);
            else
                _doc.DeleteSelection();
            textChanged = true;
        }
        else if (IsVisual) textChanged = HandleBackVisual();
        else textChanged = HandleBackSource();
        ResetUndoSealTimer();
    }

    private void HandleDelete(bool shift, out bool textChanged)
    {
        textChanged = false;
        if (_lastAction != LastActionKind.Deleting)
        {
            _doc.SealUndoGroup();
            _lastAction = LastActionKind.Deleting;
        }
        _doc.BeginUndoGroup();
        if (_doc.HasSelection)
        {
            var rect = TryGetTableRectSelection();
            if (rect != null)
                ClearTableRectCells(rect.Value);
            else
                _doc.DeleteSelection();
            textChanged = true;
        }
        else if (IsVisual) textChanged = HandleDeleteVisual();
        else textChanged = HandleDeleteSource();
        ResetUndoSealTimer();
    }

    private void HandleLeft(bool shift, bool ctrl = false)
    {
        SealAndStopTimer();
        if (ctrl)
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
                _doc.MoveWordLeft();
            }
            if (IsVisual)
            {
                if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                    ClampCursorToTableCell();
                else
                    SkipCursorOverHiddenRanges(forward: false);
            }
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            if (IsVisual) HandleLeftVisual(shift);
            else HandleLeftSource(shift);
            if (!shift) _doc.CollapseSelection();
        }
    }

    private void HandleRight(bool shift, bool ctrl = false)
    {
        SealAndStopTimer();
        if (ctrl)
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
                _doc.MoveWordRight();
            }
            if (IsVisual)
            {
                if (_parsedBlocks != null && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                    ClampCursorToTableCell();
                else
                    SkipCursorOverHiddenRanges(forward: true);
            }
            if (!shift) _doc.CollapseSelection();
        }
        else
        {
            if (IsVisual) HandleRightVisual(shift);
            else HandleRightSource(shift);
            if (!shift) _doc.CollapseSelection();
        }
    }

    private void HandleHome(bool shift, bool ctrl)
    {
        SealAndStopTimer();
        if (ctrl)
        {
            _doc.CursorBlock = 0;
            _doc.CursorOffset = 0;
        }
        else
        {
            int vli = CursorToVisualLineIndex();
            var vl = _visualLines[vli];
            if (vl.Group != null)
            {
                var (targetBi, targetBo) = vl.Group.JoinedToSource(vl.StartOffset);
                if (_doc.CursorBlock == targetBi && _doc.CursorOffset == targetBo
                    && vli > 0 && _visualLines[vli - 1].Group == vl.Group)
                {
                    var (firstBi, firstBo) = vl.Group.JoinedToSource(0);
                    _doc.CursorBlock = firstBi;
                    _doc.CursorOffset = firstBo;
                }
                else
                {
                    _doc.CursorBlock = targetBi;
                    _doc.CursorOffset = targetBo;
                }
            }
            else
            {
                if (_doc.CursorOffset == vl.StartOffset
                    && vli > 0 && _visualLines[vli - 1].BlockIndex == vl.BlockIndex)
                {
                    _doc.CursorOffset = 0;
                }
                else
                {
                    _doc.CursorOffset = vl.StartOffset;
                }
            }
        }
        if (IsVisual) HandleHomeVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleEnd(bool shift, bool ctrl)
    {
        SealAndStopTimer();
        _cursorAtLineEnd = false;
        if (ctrl)
        {
            _doc.CursorBlock = _doc.BlockCount - 1;
            _doc.CursorOffset = _doc.GetBlockLength(_doc.CursorBlock);
        }
        else
        {
            int vli = CursorToVisualLineIndex();
            var vl = _visualLines[vli];
            int endOffset = vl.StartOffset + vl.Length;
            if (vl.Group != null)
            {
                bool isWrap = vli + 1 < _visualLines.Count
                    && _visualLines[vli + 1].Group == vl.Group;
                if (isWrap)
                {
                    string text = vl.Group.JoinedText;
                    while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                        endOffset--;
                }
                var (targetBi, targetBo) = vl.Group.JoinedToSource(endOffset);
                if (_doc.CursorBlock == targetBi && _doc.CursorOffset == targetBo && isWrap)
                {
                    var last = vl.Group.Segments[^1];
                    _doc.CursorBlock = last.BlockIndex;
                    _doc.CursorOffset = last.Length;
                }
                else
                {
                    _doc.CursorBlock = targetBi;
                    _doc.CursorOffset = targetBo;
                }
            }
            else
            {
                bool isWrap = vli + 1 < _visualLines.Count
                    && _visualLines[vli + 1].BlockIndex == vl.BlockIndex;
                if (isWrap)
                {
                    string text = _doc.GetBlockText(vl.BlockIndex);
                    while (endOffset > vl.StartOffset && text[endOffset - 1] == ' ')
                        endOffset--;
                }
                if (_doc.CursorOffset == endOffset && isWrap)
                {
                    _doc.CursorOffset = _doc.GetBlockLength(vl.BlockIndex);
                }
                else
                {
                    _doc.CursorOffset = endOffset;
                }
            }
            _cursorAtLineEnd = true;
        }
        if (IsVisual) HandleEndVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleUp(bool shift)
    {
        SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        if (vli > 0)
        {
            double x = CursorXInVisualLine(vli);
            vli--;
            SetCursorFromVisualLine(vli, x);
        }
        if (IsVisual) HandleUpVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandleDown(bool shift)
    {
        SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        if (vli < _visualLines.Count - 1)
        {
            double x = CursorXInVisualLine(vli);
            vli++;
            SetCursorFromVisualLine(vli, x);
        }
        if (IsVisual) HandleDownVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandlePageUp(bool shift)
    {
        SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        double x = CursorXInVisualLine(vli);
        double cursorY = _lineYPositions[vli];
        double relativeY = cursorY - _scroll.Offset;
        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
        double pageAmount = Math.Max(lineH, ActualHeight - 3 * lineH);

        _scroll.Offset -= pageAmount;
        _scroll.Clamp();

        int targetVli = HitTestVisualLine(_scroll.Offset + relativeY);
        SetCursorFromVisualLine(targetVli, x);
        if (IsVisual) HandleUpVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void HandlePageDown(bool shift)
    {
        SealAndStopTimer();
        int vli = CursorToVisualLineIndex();
        double x = CursorXInVisualLine(vli);
        double cursorY = _lineYPositions[vli];
        double relativeY = cursorY - _scroll.Offset;
        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
        double pageAmount = Math.Max(lineH, ActualHeight - 3 * lineH);

        _scroll.Offset += pageAmount;
        _scroll.Clamp();

        int targetVli = HitTestVisualLine(_scroll.Offset + relativeY);
        SetCursorFromVisualLine(targetVli, x);
        if (IsVisual) HandleDownVisual();
        if (!shift) _doc.CollapseSelection();
    }

    private void SetCursorFromVisualLine(int vli, double x)
    {
        var vl = _visualLines[vli];
        int rawOffset = HitTestInVisualLine(vli, x);
        if (vl.Group != null)
        {
            var (bi, bo) = vl.Group.JoinedToSource(rawOffset);
            _doc.CursorBlock = bi;
            _doc.CursorOffset = bo;
        }
        else
        {
            _doc.CursorBlock = vl.BlockIndex;
            _doc.CursorOffset = rawOffset;
        }
    }

    private void HandleEnter(bool shift, bool ctrl)
    {
        _doc.BeginUndoGroup();
        if (_doc.HasSelection) _doc.DeleteSelection();
        if (shift)
        {
            var blockKind = MarkdownParser.ClassifyBlock(_doc.GetBlockText(_doc.CursorBlock));
            bool isHeading = blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6;
            if (!isHeading)
            {
                string marker = _hardBreak == HardBreakStyle.Backslash ? "\\" : "  ";
                string beforeCursor = _doc.GetBlockText(_doc.CursorBlock)[.._doc.CursorOffset];
                if (!beforeCursor.EndsWith(marker))
                    _doc.Paste(marker);
            }
            _doc.InsertParagraphBreak();
        }
        else if (ctrl)
        {
            _doc.InsertParagraphBreak();
        }
        else
        {
            string blockText = _doc.GetBlockText(_doc.CursorBlock);
            var blockKind = MarkdownParser.ClassifyBlock(blockText, out int leadingSpaces);
            if (blockKind == BlockKind.IndentedCodeLine)
            {
                var (chars, _) = MarkdownParser.MeasureLeadingWhitespace(blockText);
                string stripped = chars < blockText.Length ? blockText[chars..] : "";
                var innerKind = MarkdownParser.ClassifyBlock(stripped);
                if (innerKind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                    or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    blockKind = innerKind;
                    leadingSpaces = chars;
                }
            }
            bool isStandalone = (blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6)
                             || MarkdownParser.IsFenceLine(blockText)
                             || blockKind == BlockKind.ThematicBreak;
            StripTrailingHardBreak();

            if (blockKind == BlockKind.OrderedListItem)
            {
                string stripped = leadingSpaces > 0 ? blockText[leadingSpaces..] : blockText;
                int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
                string content = stripped.Substring(prefixLen);
                if (content.Length == 0)
                {
                    _doc.RemoveTextAt(_doc.CursorBlock, 0, blockText.Length);
                    _doc.CursorOffset = 0;
                }
                else
                {
                    string indent = blockText[..leadingSpaces];
                    string number = stripped.Substring(0, prefixLen - 2);
                    char delim = stripped[prefixLen - 2];
                    _doc.InsertParagraphBreak();
                    StripExistingListPrefix();
                    if (int.TryParse(number, out int n))
                    {
                        _doc.Paste(indent + (n + 1).ToString() + delim + " ");
                        RenumberOrderedList(_doc.CursorBlock + 1, n + 2, delim);
                    }
                }
            }
            else if (blockKind is BlockKind.UnorderedListItem
                or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked
                or BlockKind.Blockquote)
            {
                string stripped = leadingSpaces > 0 ? blockText[leadingSpaces..] : blockText;
                string newPrefix;
                int prefixLen;
                if (blockKind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    newPrefix = stripped[..2] + "[ ] ";
                    prefixLen = 6;
                }
                else if (blockKind == BlockKind.Blockquote)
                {
                    newPrefix = "> ";
                    prefixLen = stripped.StartsWith("> ") ? 2 : 1;
                }
                else
                {
                    prefixLen = Math.Min(2, stripped.Length);
                    newPrefix = stripped[..prefixLen];
                }

                if (stripped.Length <= prefixLen || stripped[prefixLen..].Length == 0)
                {
                    _doc.RemoveTextAt(_doc.CursorBlock, 0, blockText.Length);
                    _doc.CursorOffset = 0;
                }
                else
                {
                    string indent = blockText[..leadingSpaces];
                    _doc.InsertParagraphBreak();
                    StripExistingListPrefix();
                    _doc.Paste(indent + newPrefix);
                }
            }
            else
            {
                if (IsVisual && blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6)
                {
                    var headingPrefix = Document.GetBlockPrefix(blockText);
                    if (headingPrefix != null && _doc.CursorOffset <= headingPrefix.Length)
                        _doc.CursorOffset = 0;
                }
                _doc.InsertParagraphBreak();
                if (!isStandalone)
                    _doc.InsertParagraphBreak();
            }
        }
        _doc.CollapseSelection();
        _doc.SealUndoGroup();
    }

    private void StripTrailingHardBreak()
    {
        string text = _doc.GetBlockText(_doc.CursorBlock);
        int end = MarkdownParser.GetContentEnd(text);
        if (end > 0 && text[end - 1] == '\\')
        {
            _doc.RemoveTextAt(_doc.CursorBlock, end - 1, 1);
        }
        else if (end >= 2 && text[end - 1] == ' ' && text[end - 2] == ' ')
        {
            int trailStart = end;
            while (trailStart > 0 && text[trailStart - 1] == ' ') trailStart--;
            _doc.RemoveTextAt(_doc.CursorBlock, trailStart, end - trailStart);
        }
    }

    private void StripExistingListPrefix()
    {
        string text = _doc.GetBlockText(_doc.CursorBlock);
        var kind = MarkdownParser.ClassifyBlock(text, out int leading);
        if (kind == BlockKind.IndentedCodeLine)
        {
            var (chars, _) = MarkdownParser.MeasureLeadingWhitespace(text);
            string inner = chars < text.Length ? text[chars..] : "";
            var innerKind = MarkdownParser.ClassifyBlock(inner);
            if (innerKind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            {
                kind = innerKind;
                leading = chars;
            }
        }
        string stripped = leading > 0 ? text[leading..] : text;
        int stripLen = kind switch
        {
            BlockKind.UnorderedListItem => leading + 2,
            BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked => leading + 6,
            BlockKind.OrderedListItem => leading + MarkdownParser.GetOrderedListPrefixLength(stripped),
            BlockKind.Blockquote => leading + (stripped.StartsWith("> ") ? 2 : 1),
            _ => 0,
        };
        if (stripLen > 0)
            _doc.RemoveTextAt(_doc.CursorBlock, 0, stripLen);
    }

    private void RenumberOrderedList(int startBlock, int nextNumber, char delim)
    {
        for (int i = startBlock; i < _doc.BlockCount; i++)
        {
            string text = _doc.GetBlockText(i);
            var kind = MarkdownParser.ClassifyBlock(text, out int ls);
            if (kind != BlockKind.OrderedListItem) break;
            string stripped = ls > 0 ? text[ls..] : text;
            int oldPl = MarkdownParser.GetOrderedListPrefixLength(stripped);
            if (oldPl == 0) break;
            string newPrefix = nextNumber.ToString() + delim + " ";
            _doc.RemoveTextAt(i, ls, oldPl);
            _doc.InsertTextAt(i, ls, newPrefix);
            nextNumber++;
        }
    }

    private bool HandleTableTab(bool shift, out bool textChanged)
    {
        textChanged = false;
        if (_parsedBlocks == null) return false;
        var parsed = _parsedBlocks[_doc.CursorBlock];
        if (parsed.TableRow == null || parsed.Table == null) return false;

        SealAndStopTimer();
        var cells = parsed.TableRow.Cells;
        string blockText = _doc.GetBlockText(_doc.CursorBlock);
        int colCount = parsed.Table.ColumnCount;

        int curCell = -1;
        for (int c = 0; c < cells.Count; c++)
        {
            if (_doc.CursorOffset >= cells[c].Start &&
                _doc.CursorOffset <= cells[c].Start + cells[c].Length)
            { curCell = c; break; }
        }
        if (curCell < 0) curCell = 0;

        if (!shift)
        {
            if (curCell + 1 < cells.Count)
            {
                var next = cells[curCell + 1];
                MoveCursorToCell(next, blockText);
            }
            else
            {
                for (int b = _doc.CursorBlock + 1; b < _doc.BlockCount; b++)
                {
                    var p = _parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.CursorBlock = b;
                        var nextBlockText = _doc.GetBlockText(b);
                        MoveCursorToCell(p.TableRow.Cells[0], nextBlockText);
                        break;
                    }
                    break;
                }
            }
        }
        else
        {
            if (curCell > 0)
            {
                var prev = cells[curCell - 1];
                MoveCursorToCell(prev, blockText);
            }
            else
            {
                for (int b = _doc.CursorBlock - 1; b >= 0; b--)
                {
                    var p = _parsedBlocks[b];
                    if (p.IsTableSeparator) continue;
                    if (p.TableRow != null && p.Table == parsed.Table)
                    {
                        _doc.CursorBlock = b;
                        var prevBlockText = _doc.GetBlockText(b);
                        var lastCell = p.TableRow.Cells[^1];
                        MoveCursorToCell(lastCell, prevBlockText);
                        break;
                    }
                    break;
                }
            }
        }

        _doc.CollapseSelection();
        return true;
    }

    private void MoveCursorToCell(TableCellInfo cell, string blockText)
    {
        int start = cell.Start;
        int end = cell.Start + cell.Length;
        while (start < end && blockText[start] == ' ') start++;
        while (end > start && blockText[end - 1] == ' ') end--;
        _doc.CursorOffset = start;
        _doc.AnchorBlock = _doc.CursorBlock;
        _doc.AnchorOffset = end;
    }

    private void HandleTabIndent(bool shift)
    {
        SealAndStopTimer();
        _doc.BeginUndoGroup();

        if (_doc.HasSelection)
        {
            var (sb, _, eb, _) = _doc.GetOrderedSelection();
            if (shift)
                _doc.OutdentLines(sb, eb, 4);
            else
                _doc.IndentLines(sb, eb, 4);
            _doc.AnchorBlock = sb;
            _doc.AnchorOffset = 0;
            _doc.CursorBlock = eb;
            _doc.CursorOffset = _doc.GetBlockLength(eb);
        }
        else
        {
            int indentStep = GetIndentStep(_doc.CursorBlock);
            if (indentStep != 4)
            {
                if (shift)
                    _doc.OutdentLines(_doc.CursorBlock, _doc.CursorBlock, indentStep);
                else
                    _doc.IndentLines(_doc.CursorBlock, _doc.CursorBlock, indentStep);
            }
            else
            {
                if (shift)
                {
                    _doc.OutdentLines(_doc.CursorBlock, _doc.CursorBlock, 4);
                }
                else
                {
                    _doc.InsertTextAt(_doc.CursorBlock, _doc.CursorOffset, "    ");
                    _doc.CursorOffset += 4;
                }
            }
            _doc.CollapseSelection();
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
    }

    private int GetIndentStep(int blockIndex)
    {
        if (_parsedBlocks == null) return 4;
        var kind = _parsedBlocks[blockIndex].Kind;
        var text = _doc.GetBlockText(blockIndex);
        if (kind == BlockKind.IndentedCodeLine)
            kind = MarkdownParser.ClassifyBlock(text.TrimStart());
        return kind switch
        {
            BlockKind.UnorderedListItem => 2,
            BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked => 2,
            BlockKind.OrderedListItem => MarkdownParser.GetOrderedListPrefixLength(text.TrimStart()),
            BlockKind.Blockquote => 2,
            _ => 4,
        };
    }

    private bool HandleTableEnter(out bool textChanged)
    {
        textChanged = false;
        if (_parsedBlocks == null) return false;
        var parsed = _parsedBlocks[_doc.CursorBlock];
        if (parsed.Table == null) return false;

        int colCount = parsed.Table.ColumnCount;
        string newRow = "|" + string.Concat(Enumerable.Repeat("  |", colCount));

        _doc.BeginUndoGroup();
        if (_doc.HasSelection) _doc.DeleteSelection();
        _doc.CollapseSelection();

        int insertAfter = _doc.CursorBlock;
        if (parsed.Kind == BlockKind.TableHeaderRow || parsed.Kind == BlockKind.TableSeparatorRow)
        {
            for (int b = insertAfter + 1; b < _doc.BlockCount; b++)
            {
                if (_parsedBlocks[b].Kind == BlockKind.TableSeparatorRow) { insertAfter = b; continue; }
                break;
            }
        }

        _doc.CursorBlock = insertAfter;
        _doc.CursorOffset = _doc.GetBlockLength(insertAfter);
        _doc.InsertParagraphBreak();
        _doc.Paste(newRow);
        _doc.CursorOffset = 2;
        _doc.CollapseSelection();
        _doc.SealUndoGroup();
        textChanged = true;
        return true;
    }

    // --- Keyboard ---

    private bool _altKeyAlone;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool handled = true;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !alt;
        bool textChanged = false;

        var rawKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (rawKey is Key.LeftAlt or Key.RightAlt)
            _altKeyAlone = true;
        else if (alt)
            _altKeyAlone = false;

        ComputeLayout();

        if (e.Key != Key.End)
            _cursorAtLineEnd = false;

        switch (e.Key)
        {
            case Key.F6:
                if (!shift && !ctrl && !alt && FormattingBar?.ActivateKeyboardNavigation() == true)
                {
                    e.Handled = true;
                    return;
                }
                handled = false;
                break;
            case Key.Tab:
                if (IsReadOnly) { handled = false; break; }
                if (HandleTableTab(shift, out textChanged))
                    break;
                HandleTabIndent(shift);
                textChanged = true;
                break;

            case Key.Return:
                if (IsReadOnly) { handled = false; break; }
                SealAndStopTimer();
                if (HandleTableEnter(out textChanged))
                    break;
                HandleEnter(shift, ctrl);
                textChanged = true;
                break;

            case Key.Back:
                if (IsReadOnly) { handled = false; break; }
                HandleBack(shift, out textChanged);
                break;

            case Key.Delete:
                if (IsReadOnly) { handled = false; break; }
                HandleDelete(shift, out textChanged);
                break;

            case Key.Left:
                HandleLeft(shift, ctrl);
                break;

            case Key.Right:
                HandleRight(shift, ctrl);
                break;

            case Key.Up:
                HandleUp(shift);
                break;

            case Key.Down:
                HandleDown(shift);
                break;

            case Key.PageUp:
                HandlePageUp(shift);
                break;

            case Key.PageDown:
                HandlePageDown(shift);
                break;

            case Key.Home:
                HandleHome(shift, ctrl);
                break;

            case Key.End:
                HandleEnd(shift, ctrl);
                break;

            case Key.A:
                if (ctrl)
                {
                    SealAndStopTimer();
                    _doc.SelectAll();
                }
                else handled = false;
                break;

            case Key.C:
                if (ctrl && _doc.HasSelection)
                {
                    var rectC = TryGetTableRectSelection();
                    string copyText = rectC != null
                        ? GetTableRectSelectedText(rectC.Value)
                        : _doc.GetSelectedText();
                    var cfHtml = HtmlColorParser.ConvertToHtmlClipboard(copyText);
                    if (cfHtml != null)
                        ClipboardHelper.SetTextAndHtml(copyText, cfHtml, Logger);
                    else
                        ClipboardHelper.SetText(copyText, Logger);
                }
                else handled = false;
                break;

            case Key.X:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl && _doc.HasSelection)
                {
                    SealAndStopTimer();
                    var rectX = TryGetTableRectSelection();
                    string cutText = rectX != null
                        ? GetTableRectSelectedText(rectX.Value)
                        : _doc.GetSelectedText();
                    ClipboardHelper.SetText(cutText, Logger);
                    _doc.BeginUndoGroup();
                    if (rectX != null)
                        ClearTableRectCells(rectX.Value);
                    else
                        _doc.DeleteSelection();
                    _doc.SealUndoGroup();
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.V:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl)
                {
                    SealAndStopTimer();
                    string? pasteText = null;
                    bool inCodeBlock = _parsedBlocks != null
                        && _parsedBlocks[_doc.CursorBlock].Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;
                    if (!shift && !inCodeBlock)
                    {
                        string? html = ClipboardHelper.GetHtml(Logger);
                        if (html != null)
                            pasteText = HtmlColorParser.ConvertToColoredMarkdown(html);
                    }
                    pasteText ??= ClipboardHelper.GetText(Logger);
                    if (!string.IsNullOrEmpty(pasteText))
                    {
                        _doc.BeginUndoGroup();
                        var rectPaste = TryGetTableRectSelection();
                        if (rectPaste != null)
                        {
                            ClearTableRectCells(rectPaste.Value);
                            MoveCursorToRectStart(rectPaste.Value);
                        }
                        else if (_doc.HasSelection)
                        {
                            _doc.DeleteSelection();
                        }
                        if (!TryPasteIntoTableCells(pasteText))
                            _doc.Paste(pasteText);
                        _doc.SealUndoGroup();
                        textChanged = true;
                    }
                }
                else handled = false;
                break;

            case Key.Z:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl)
                {
                    _undoSealTimer.Stop();
                    _doc.Undo();
                    _lastAction = LastActionKind.None;
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.Y:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl)
                {
                    _undoSealTimer.Stop();
                    _doc.Redo();
                    _lastAction = LastActionKind.None;
                    textChanged = true;
                }
                else handled = false;
                break;

            case Key.B:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl && !IsInFencedCode) ToggleBold();
                else handled = false;
                break;

            case Key.I:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl && !IsInFencedCode) ToggleItalic();
                else handled = false;
                break;

            case Key.K:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl && !IsInFencedCode) InsertLink();
                else handled = false;
                break;

            case Key.F:
                if (ctrl) OpenFind(showReplace: false);
                else handled = false;
                break;

            case Key.H:
                if (ctrl) OpenFind(showReplace: !IsReadOnly);
                else handled = false;
                break;

            case Key.F3:
                if (_searchMatches.Count > 0) NavigateMatch(shift ? -1 : 1);
                else handled = false;
                break;

            case Key.Escape:
                if (FindBar?.IsOpen == true) CloseFind();
                else handled = false;
                break;

            case Key.T:
                if (ctrl)
                {
                    ToggleToc();
                    e.Handled = true;
                    return;
                }
                else handled = false;
                break;

            case Key.M:
                if (ctrl)
                {
                    ToggleEditMode();
                    ResetBlink();
                    InvalidateVisual();
                    e.Handled = true;
                    RaiseFormattingChanged();
                    return;
                }
                else handled = false;
                break;

            case Key.OemPlus:
            case Key.Add:
                if (ctrl) ZoomIn();
                else handled = false;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                if (ctrl) ZoomOut();
                else handled = false;
                break;

            case Key.D0:
            case Key.NumPad0:
                if (ctrl) ZoomReset();
                else handled = false;
                break;

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            if (!(ctrl && e.Key is Key.B or Key.I))
                _pendingStyleOff = null;
            ResetBlink();
            if (textChanged)
            {
                InvalidateLayout();
                if (IsVisual)
                {
                    ComputeLayout();
                    EnsureCursorOnVisibleBlock();
                    if (_parsedBlocks != null && _doc.CursorBlock < _parsedBlocks.Count
                        && IsTableRow(_parsedBlocks[_doc.CursorBlock]))
                        ClampCursorToTableCell();
                    else
                        SkipCursorToVisible(forward: true);
                }
            }
            else
                InvalidateVisual();
            EnsureCursorVisible();
            e.Handled = true;
            RaiseFormattingChanged();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((key is Key.LeftAlt or Key.RightAlt) && _altKeyAlone)
        {
            _altKeyAlone = false;
            if (FormattingBar?.ActivateKeyboardNavigation() == true)
            {
                e.Handled = true;
                return;
            }
        }
        _altKeyAlone = false;
        base.OnKeyUp(e);
    }

    // --- Right-click context menu ---

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        ShowContextMenu(e.GetPosition(this));
        e.Handled = true;
    }

    private bool IsWithinSelection(int block, int offset)
    {
        if (!_doc.HasSelection) return false;
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        if (block < sb || block > eb) return false;
        if (block == sb && offset < so) return false;
        if (block == eb && offset >= eo) return false;
        return true;
    }

    private void ShowContextMenu(Point position)
    {
        var menu = new ContextMenu();
        ApplyContextMenuStyle(menu);

        bool selectionIsMultiWord = _doc.HasSelection
            && _doc.GetSelectedText().AsSpan().IndexOfAny(' ', '\t') >= 0;

        if (_spellCheckEnabled && !selectionIsMultiWord)
            AddSpellCheckMenuItems(menu, position);

        bool hasSelection = _doc.HasSelection;
        bool inCode = IsInFencedCode;

        // Clipboard operations
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        if (!IsReadOnly)
        {
            var cut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X", IsEnabled = hasSelection };
            ApplyMenuItemStyle(cut);
            cut.Click += (_, _) => { PerformCut(); Focus(); };
            menu.Items.Add(cut);
        }

        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = hasSelection };
        ApplyMenuItemStyle(copy);
        copy.Click += (_, _) => { PerformCopy(); Focus(); };
        menu.Items.Add(copy);

        if (!IsReadOnly)
        {
            var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = Clipboard.ContainsText() };
            ApplyMenuItemStyle(paste);
            paste.Click += (_, _) => { PerformPaste(); Focus(); };
            menu.Items.Add(paste);
        }

        var selectAll = new MenuItem { Header = "Select all", InputGestureText = "Ctrl+A" };
        ApplyMenuItemStyle(selectAll);
        selectAll.Click += (_, _) => { PerformSelectAll(); Focus(); };
        menu.Items.Add(selectAll);

        // Inline formatting (only when selection exists and not in code block)
        if (hasSelection && !IsReadOnly && !inCode)
        {
            menu.Items.Add(new Separator());

            var bold = new MenuItem { Header = "Bold", InputGestureText = "Ctrl+B", IsChecked = SelectionIsBold };
            ApplyMenuItemStyle(bold);
            bold.Click += (_, _) => { ToggleBold(); Focus(); };
            menu.Items.Add(bold);

            var italic = new MenuItem { Header = "Italic", InputGestureText = "Ctrl+I", IsChecked = SelectionIsItalic };
            ApplyMenuItemStyle(italic);
            italic.Click += (_, _) => { ToggleItalic(); Focus(); };
            menu.Items.Add(italic);

            var strikethrough = new MenuItem { Header = "Strikethrough", IsChecked = SelectionIsStrikethrough };
            ApplyMenuItemStyle(strikethrough);
            strikethrough.Click += (_, _) => { ToggleStrikethrough(); Focus(); };
            menu.Items.Add(strikethrough);

            var code = new MenuItem { Header = "Code", IsChecked = SelectionIsCode };
            ApplyMenuItemStyle(code);
            code.Click += (_, _) => { ToggleCodeSpan(); Focus(); };
            menu.Items.Add(code);
        }

        // Reformat
        if (!IsReadOnly)
        {
            bool canReformat = hasSelection ? CanReformat : CanReformatAll;
            menu.Items.Add(new Separator());
            var reformat = new MenuItem
            {
                Header = hasSelection ? "Reformat" : "Reformat all",
                IsEnabled = canReformat
            };
            ApplyMenuItemStyle(reformat);
            reformat.Click += (_, _) =>
            {
                if (_doc.HasSelection)
                    Reflow();
                else
                    ReflowAll();
                Focus();
            };
            menu.Items.Add(reformat);
        }

        // Clear background
        bool hasBg = hasSelection ? SelectionHasBackground() : CursorHasBackground();
        if (hasBg)
        {
            menu.Items.Add(new Separator());
            var clearBackground = new MenuItem { Header = "Clear background" };
            ApplyMenuItemStyle(clearBackground);
            clearBackground.Click += (_, _) =>
            {
                if (_doc.HasSelection)
                    RemoveBackgroundFromSelection();
                else
                    RemoveBackgroundAtCursor();
                Focus();
            };
            menu.Items.Add(clearBackground);
        }

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
        menu.PlacementTarget = this;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private Style? _contextMenuStyle;
    private Style? _menuItemStyle;

    private void ApplyContextMenuStyle(ContextMenu menu)
    {
        _contextMenuStyle ??= TryFindResource("DarkContextMenu") as Style;
        if (_contextMenuStyle != null)
            menu.Style = _contextMenuStyle;
    }

    private void ApplyMenuItemStyle(MenuItem item)
    {
        _menuItemStyle ??= TryFindResource("DarkContextMenuItem") as Style;
        if (_menuItemStyle != null)
            item.Style = _menuItemStyle;
    }
}
