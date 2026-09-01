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

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _linkHandler.TryOpenLinkAtClick(pos))
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
            ComputeLayout();
            _linkHandler.UpdateLinkTooltip(pos);
            var hoverLink = _linkHandler.GetLinkAtPosition(pos);
            Cursor = hoverLink != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? Cursors.Hand
                : Cursors.IBeam;
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
        _linkHandler.HideLinkTooltip();
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
            if (IsVisual && _doc.CursorOffset == _doc.GetBlockLength(_doc.CursorBlock))
            {
                // Ensure layout is current before using visual maps
                ComputeLayout();
                ClampCursorBeforeTrailingHidden();
            }

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
        => _navigationEngine.HandleLeft(shift, ctrl);

    private void HandleRight(bool shift, bool ctrl = false)
        => _navigationEngine.HandleRight(shift, ctrl);

    private void HandleHome(bool shift, bool ctrl)
        => _navigationEngine.HandleHome(shift, ctrl);

    private void HandleEnd(bool shift, bool ctrl)
        => _navigationEngine.HandleEnd(shift, ctrl);

    private void HandleUp(bool shift)
        => _navigationEngine.HandleUp(shift);

    private void HandleDown(bool shift)
        => _navigationEngine.HandleDown(shift);

    private void HandlePageUp(bool shift)
        => _navigationEngine.HandlePageUp(shift);

    private void HandlePageDown(bool shift)
        => _navigationEngine.HandlePageDown(shift);




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
                if (_tableInputHandler.HandleTableTab(shift, out textChanged))
                    break;
                HandleTabIndent(shift);
                textChanged = true;
                break;

            case Key.Return:
                if (IsReadOnly) { handled = false; break; }
                SealAndStopTimer();
                if (_tableInputHandler.HandleTableEnter(out textChanged))
                    break;
                _listFormattingHandler.HandleEnter(shift, ctrl);
                textChanged = true;
                break;

            case Key.Back:
                if (_editingKeysHandler.TryHandleEditingKey(e.Key, ctrl, shift, IsReadOnly, out textChanged))
                    break;
                handled = false;
                break;

            case Key.Delete:
                if (_editingKeysHandler.TryHandleEditingKey(e.Key, ctrl, shift, IsReadOnly, out textChanged))
                    break;
                handled = false;
                break;

            case Key.Left:
                if (_navigationKeysHandler.TryHandleNavigationKey(e.Key, shift, ctrl))
                    break;
                HandleLeft(shift, ctrl: false);
                break;

            case Key.Right:
                if (_navigationKeysHandler.TryHandleNavigationKey(e.Key, shift, ctrl))
                    break;
                HandleRight(shift, ctrl: false);
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
                if (_navigationKeysHandler.TryHandleNavigationKey(e.Key, shift, ctrl))
                    break;
                HandleHome(shift, ctrl: false);
                break;

            case Key.End:
                if (_navigationKeysHandler.TryHandleNavigationKey(e.Key, shift, ctrl))
                    break;
                HandleEnd(shift, ctrl: false);
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
                    PerformCopy();
                }
                else handled = false;
                break;

            case Key.X:
                if (IsReadOnly) { handled = false; break; }
                if (ctrl && _doc.HasSelection)
                {
                    SealAndStopTimer();
                    var rectX = TryGetTableRectSelection();
                    SetClipboardFromSelection();
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
                        {
                            var settings = new MarkdownOutputSettings { PreserveColors = true };
                            pasteText = HtmlBlockModelParser.ConvertHtmlToMarkdown(html, settings);
                        }
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
                if (ctrl && _editingKeysHandler.TryHandleEditingKey(e.Key, ctrl, shift, IsReadOnly, out textChanged))
                    break;
                handled = false;
                break;

            case Key.Y:
                if (ctrl && _editingKeysHandler.TryHandleEditingKey(e.Key, ctrl, shift, IsReadOnly, out textChanged))
                    break;
                handled = false;
                break;

            case Key.B:
            case Key.I:
            case Key.K:
                if (!_formattingKeysHandler.TryHandleFormattingKey(e.Key, ctrl, IsReadOnly))
                    handled = false;
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
                if (FindAndReplace.TestSearchMatchCount > 0) NavigateMatch(shift ? -1 : 1);
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

    private bool IsWithinSelection(int block, int offset)
    {
        if (!_doc.HasSelection) return false;
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        if (block < sb || block > eb) return false;
        if (block == sb && offset < so) return false;
        if (block == eb && offset >= eo) return false;
        return true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        _contextMenuHandler.ShowContextMenu(e.GetPosition(this));
        e.Handled = true;
    }

    internal void ApplyMenuItemStyle(MenuItem item)
    {
        _contextMenuHandler.ApplyMenuItemStyle(item);
    }

    void ICanvasOperations.StyleMenuItem(MenuItem item) => ApplyMenuItemStyle(item);
    void ICanvasOperations.FocusCanvas() => Focus();
}
