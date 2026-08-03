namespace RaisinDocs;

public partial class DocsCanvas
{
    // --- Public formatting API ---

    public void ToggleBold()
    {
        SealAndStopTimer();
        ToggleInlineStyle("**", InlineStyle.Bold);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleItalic()
    {
        SealAndStopTimer();
        ToggleInlineStyle("*", InlineStyle.Italic);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleCodeSpan()
    {
        SealAndStopTimer();
        ToggleInlineStyle("`", InlineStyle.Code);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleStrikethrough()
    {
        SealAndStopTimer();
        ToggleInlineStyle("~~", InlineStyle.Strikethrough);
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleHeading(int level)
    {
        if (level < 1 || level > 6) return;
        ToggleBlockPrefixForSelection(new string('#', level) + " ");
    }

    public void ToggleBulletList()
    {
        ToggleBlockPrefixForSelection("- ");
    }

    public void ToggleOrderedList()
    {
        SealAndStopTimer();
        bool hadSelection = _doc.HasSelection;
        var (sb, _, eb, _) = hadSelection
            ? _doc.GetOrderedSelection()
            : (_doc.CursorBlock, 0, _doc.CursorBlock, 0);

        bool allOrdered = true;
        for (int b = sb; b <= eb; b++)
        {
            var kind = MarkdownParser.ClassifyBlock(_doc.GetBlockText(b));
            if (kind != BlockKind.OrderedListItem)
            {
                allOrdered = false;
                break;
            }
        }

        _doc.BeginUndoGroup();
        for (int b = sb; b <= eb; b++)
        {
            if (allOrdered)
            {
                string text = _doc.GetBlockText(b);
                var prefix = Document.GetBlockPrefix(text);
                if (prefix != null)
                    _doc.ToggleBlockPrefix(b, prefix);
            }
            else
            {
                string text = _doc.GetBlockText(b);
                if (MarkdownParser.ClassifyBlock(text) != BlockKind.OrderedListItem)
                    _doc.ToggleBlockPrefix(b, (b - sb + 1) + ". ");
            }
        }
        if (hadSelection)
        {
            _doc.AnchorBlock = sb;
            _doc.AnchorOffset = 0;
            _doc.CursorBlock = eb;
            _doc.CursorOffset = _doc.GetBlockLength(eb);
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void ToggleTaskList()
    {
        ToggleBlockPrefixForSelection("- [ ] ");
    }

    public void ToggleBlockquote()
    {
        ToggleBlockPrefixForSelection("> ");
    }

    public void Reflow()
    {
        if (!_doc.HasSelection)
            return;
        SealAndStopTimer();
        var sel = _doc.GetOrderedSelection();
        int sb = sel.startBlock;
        int eb = sel.endBlock;
        _doc.BeginUndoGroup();
        bool changed = _doc.SplitInlineColorDivs(sb, ref eb,
            MarkdownParser.FindInlineColorOpenEnd,
            MarkdownParser.FindInlineColorCloseStart,
            MarkdownParser.InlineOpenToDivOpen);
        changed |= _doc.Reflow(sb, eb, IsMergeableParagraph, MarkdownParser.GetFenceBacktickCount);
        eb = Math.Min(eb, _doc.BlockCount - 1);
        changed |= _doc.NormalizeAdjacentMarkers(sb, eb, MarkdownParser.NormalizeAdjacentMarkers, MarkdownParser.GetFenceBacktickCount);
        changed |= _doc.RenumberOrderedLists(sb, eb, MarkdownParser.GetOrderedListPrefixLength, MarkdownParser.GetFenceBacktickCount);
        if (!changed)
            _doc.TrimWhitespace(sb, eb, MarkdownParser.GetFenceBacktickCount, MarkdownParser.HasIndentedContent);
        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public ReformatActions GetReformatActions()
    {
        if (!_doc.HasSelection)
            return ReformatActions.None;
        var sel = _doc.GetOrderedSelection();
        return GetReformatActionsForRange(sel.startBlock, sel.endBlock);
    }

    public bool CanReformat => GetReformatActions() != ReformatActions.None;

    public void ReflowAll()
    {
        if (_doc.BlockCount == 0) return;
        SealAndStopTimer();
        int sb = 0, eb = _doc.BlockCount - 1;
        _doc.BeginUndoGroup();
        bool changed = _doc.SplitInlineColorDivs(sb, ref eb,
            MarkdownParser.FindInlineColorOpenEnd,
            MarkdownParser.FindInlineColorCloseStart,
            MarkdownParser.InlineOpenToDivOpen);
        changed |= _doc.Reflow(sb, eb, IsMergeableParagraph, MarkdownParser.GetFenceBacktickCount);
        eb = Math.Min(eb, _doc.BlockCount - 1);
        changed |= _doc.NormalizeAdjacentMarkers(sb, eb, MarkdownParser.NormalizeAdjacentMarkers, MarkdownParser.GetFenceBacktickCount);
        changed |= _doc.RenumberOrderedLists(sb, eb, MarkdownParser.GetOrderedListPrefixLength, MarkdownParser.GetFenceBacktickCount);
        if (!changed)
            _doc.TrimWhitespace(sb, eb, MarkdownParser.GetFenceBacktickCount, MarkdownParser.HasIndentedContent);
        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    private bool CanReformatAll
    {
        get
        {
            if (_doc.BlockCount == 0) return false;
            int sb = 0, eb = _doc.BlockCount - 1;
            return GetReformatActionsForRange(sb, eb) != ReformatActions.None;
        }
    }

    private ReformatActions GetReformatActionsForRange(int sb, int eb)
    {
        var actions = ReformatActions.None;
        if (_doc.HasBoxDrawingTable(sb, eb))
            actions |= ReformatActions.ConvertBoxTable;
        if (_doc.HasMergeableParagraphs(sb, eb, IsMergeableParagraph, MarkdownParser.GetFenceBacktickCount))
            actions |= ReformatActions.MergeParagraphs;
        if (_doc.HasConsecutiveBlankLines(sb, eb, MarkdownParser.GetFenceBacktickCount))
            actions |= ReformatActions.CollapseBlankLines;
        if (_doc.HasAdjacentMarkers(sb, eb, MarkdownParser.HasAdjacentMarkers, MarkdownParser.GetFenceBacktickCount))
            actions |= ReformatActions.NormalizeMarkers;
        if (_doc.HasTrimmableWhitespace(sb, eb, MarkdownParser.GetFenceBacktickCount, MarkdownParser.HasIndentedContent))
            actions |= ReformatActions.TrimWhitespace;
        if (_doc.HasMisnumberedOrderedList(sb, eb, MarkdownParser.GetOrderedListPrefixLength, MarkdownParser.GetFenceBacktickCount))
            actions |= ReformatActions.RenumberOrderedList;
        return actions;
    }

    private static bool IsMergeableParagraph(string text) =>
        text.Length > 0
        && MarkdownParser.ClassifyBlock(text) == BlockKind.Paragraph
        && !text.StartsWith('|')
        && !text.EndsWith('\\')
        && !text.EndsWith("  ")
        && !MarkdownParser.TryExtractDivOpen(text, out _)
        && !MarkdownParser.TryExtractDivClose(text, out _)
        && !MarkdownParser.IsThemeBlock(text)
        && !MarkdownParser.IsThemeBlockStart(text)
        && !MarkdownParser.TryParseLinkDefinition(text, out _, out _, out _);

    public void ConvertToHardBreaks()
    {
        SealAndStopTimer();
        int sb, eb;
        if (_doc.HasSelection)
        {
            var sel = _doc.GetOrderedSelection();
            sb = sel.startBlock;
            eb = sel.endBlock;
        }
        else
        {
            sb = 0;
            eb = _doc.BlockCount - 1;
        }
        string marker = _hardBreak == HardBreakStyle.Backslash ? "\\" : "  ";
        _doc.BeginUndoGroup();
        _doc.AddHardBreaks(sb, eb, marker, MarkdownParser.GetFenceBacktickCount);
        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public bool CanConvertToHardBreaks
    {
        get
        {
            int sb, eb;
            if (_doc.HasSelection)
            {
                var sel = _doc.GetOrderedSelection();
                sb = sel.startBlock;
                eb = sel.endBlock;
            }
            else
            {
                sb = 0;
                eb = _doc.BlockCount - 1;
            }
            return _doc.HasSoftBreaks(sb, eb, MarkdownParser.GetFenceBacktickCount);
        }
    }

    public void InsertLink()
    {
        if (_linkPopup.IsOpen)
        {
            _linkPopup.Cancel();
            return;
        }

        SealAndStopTimer();
        ComputeLayout();

        var existingLink = _linkHandler.GetLinkAtCursor();
        string? selText = null;
        int selStart = 0, selEnd = 0;
        if (existingLink == null && _doc.HasSelection)
        {
            selText = GetSelectedText();
            if (selText != null)
            {
                var (_, so, _, eo) = _doc.GetOrderedSelection();
                selStart = so;
                selEnd = eo;
            }
        }

        _linkPopup.Show(existingLink, selText, selStart, selEnd);
        _linkPopup.ApplyTheme(_palette.Background, _palette.Foreground, _palette.Syntax, _palette.CodeBackground);

        int vli = CursorToVisualLineIndex();
        double effectiveScroll = _scroll.EffectiveOffset;
        double lineY = _lineYPositions[vli] - effectiveScroll;
        double lineH = GetEffectiveLineHeight(_visualLines[vli]);
        _linkPopup.SetPopupPosition(_padding, lineY + lineH + 4);
    }

    public void InsertFgColor(string colorName)
    {
        _colorFormatter.InsertFgColor(colorName);
    }

    public void InsertBgColor(string colorName)
    {
        _colorFormatter.InsertBgColor(colorName);
    }

    internal bool SelectionHasBackground()
    {
        return _colorFormatter.SelectionHasBackground();
    }

    internal bool CursorHasBackground()
    {
        return _colorFormatter.CursorHasBackground();
    }

    public void RemoveBackgroundAtCursor()
    {
        _colorFormatter.RemoveBackgroundAtCursor();
    }

    public void RemoveBackgroundFromSelection()
    {
        _colorFormatter.RemoveBackgroundFromSelection();
    }


    private string? GetSelectedText()
    {
        if (!_doc.HasSelection) return null;
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        if (sb != eb) return null;
        return _doc.GetBlockText(sb).Substring(so, eo - so);
    }

    public void ToggleFencedCode()
    {
        SealAndStopTimer();
        var (sb, _, eb, _) = _doc.HasSelection
            ? _doc.GetOrderedSelection()
            : (_doc.CursorBlock, 0, _doc.CursorBlock, 0);

        ComputeLayout();

        bool allFenced = true;
        for (int b = sb; b <= eb; b++)
        {
            if (_parsedBlocks![b].Kind != BlockKind.FencedCodeLine)
            {
                allFenced = false;
                break;
            }
        }

        _doc.BeginUndoGroup();

        if (allFenced)
        {
            int openDelim = -1;
            for (int b = sb; b >= 0; b--)
            {
                if (_parsedBlocks![b].IsFenceDelimiter) { openDelim = b; break; }
            }
            int closeDelim = -1;
            for (int b = eb; b < _doc.BlockCount; b++)
            {
                if (_parsedBlocks![b].IsFenceDelimiter) { closeDelim = b; break; }
            }

            if (openDelim >= 0 && closeDelim >= 0)
            {
                _doc.RemoveBlockAt(closeDelim);
                _doc.RemoveBlockAt(openDelim);
            }
        }
        else
        {
            _doc.InsertBlockAt(eb + 1, "```");
            _doc.InsertBlockAt(sb, "```");
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    public void InsertTable(int columns, int rows)
    {
        SealAndStopTimer();
        _doc.BeginUndoGroup();
        if (_doc.HasSelection) _doc.DeleteSelection();

        string header = "| " + string.Join(" | ", Enumerable.Range(1, columns).Select(c => $"Header {c}")) + " |";
        string separator = "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |";
        var lines = new List<string> { header, separator };
        for (int r = 0; r < rows; r++)
            lines.Add("|" + string.Concat(Enumerable.Repeat("  |", columns)));

        if (_doc.CursorOffset > 0)
            _doc.InsertParagraphBreak();

        _doc.Paste(string.Join("\n", lines));
        _doc.CursorBlock -= lines.Count - 1;
        _doc.CursorOffset = 2;
        _doc.CollapseSelection();
        _doc.SealUndoGroup();
        InvalidateLayout();
        InvalidateVisual();
        EnsureCursorVisible();
    }

    private void ToggleBlockPrefixForSelection(string prefix)
    {
        SealAndStopTimer();
        bool hadSelection = _doc.HasSelection;
        var (sb, _, eb, _) = hadSelection
            ? _doc.GetOrderedSelection()
            : (_doc.CursorBlock, 0, _doc.CursorBlock, 0);

        bool allHavePrefix = true;
        for (int b = sb; b <= eb; b++)
        {
            if (!_doc.GetBlockText(b).StartsWith(prefix))
            {
                allHavePrefix = false;
                break;
            }
        }

        _doc.BeginUndoGroup();

        if (allHavePrefix)
        {
            for (int b = sb; b <= eb; b++)
                _doc.ToggleBlockPrefix(b, prefix);
        }
        else
        {
            for (int b = sb; b <= eb; b++)
            {
                if (!_doc.GetBlockText(b).StartsWith(prefix))
                    _doc.ToggleBlockPrefix(b, prefix);
            }
        }

        if (hadSelection)
        {
            _doc.AnchorBlock = sb;
            _doc.AnchorOffset = 0;
            _doc.CursorBlock = eb;
            _doc.CursorOffset = _doc.GetBlockLength(eb);
        }

        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
        RaiseFormattingChanged();
    }

    // --- Formatting query properties ---

    public BlockKind CurrentBlockKind
    {
        get
        {
            if (!_measure.IsMeasured) return BlockKind.Paragraph;
            ComputeLayout();
            return _parsedBlocks![_doc.CursorBlock].Kind;
        }
    }

    private bool IsInFencedCode => CurrentBlockKind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;

    public bool SelectionIsBold => SelectionHasStyle(InlineStyle.Bold);
    public bool SelectionIsItalic => SelectionHasStyle(InlineStyle.Italic);
    public bool SelectionIsCode => SelectionHasStyle(InlineStyle.Code);
    public bool SelectionIsStrikethrough => SelectionHasStyle(InlineStyle.Strikethrough);

    private bool SelectionHasStyle(InlineStyle targetStyle)
    {
        if (!_measure.IsMeasured) return false;
        if (!_doc.HasSelection)
        {
            if (_pendingStyleOff?.Style == targetStyle)
                return false;
            ComputeLayout();
            return _parsedBlocks![_doc.CursorBlock].HasStyleAt(_doc.CursorOffset, targetStyle);
        }
        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        so = Math.Min(so, _doc.GetBlockLength(sb));
        eo = Math.Min(eo, _doc.GetBlockLength(eb));

        ComputeLayout();

        bool anyRunChecked = false;
        for (int b = sb; b <= eb; b++)
        {
            int blockSelStart = (b == sb) ? so : 0;
            int blockSelEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (blockSelStart >= blockSelEnd) continue;

            var parsed = _parsedBlocks![b];
            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                if (runEnd <= blockSelStart || run.Start >= blockSelEnd) continue;
                anyRunChecked = true;
                if (run.Style != targetStyle && run.Style != InlineStyle.BoldItalic)
                    return false;
                if (run.Style == InlineStyle.BoldItalic &&
                    targetStyle != InlineStyle.Bold && targetStyle != InlineStyle.Italic)
                    return false;
            }
        }
        return anyRunChecked;
    }

    internal void RaiseFormattingChanged()
    {
        FormattingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleInlineStyle(string marker, InlineStyle targetStyle)
    {
        if (!_doc.HasSelection)
        {
            ComputeLayout();
            bool insideStyle = _parsedBlocks![_doc.CursorBlock]
                .HasStyleAt(_doc.CursorOffset, targetStyle);

            if (insideStyle)
            {
                if (_pendingStyleOff?.Style == targetStyle)
                    _pendingStyleOff = null;
                else
                    _pendingStyleOff = (marker, targetStyle);
                return;
            }

            _pendingStyleOff = null;
            int block = _doc.CursorBlock;
            int offset = _doc.CursorOffset;
            _doc.BeginUndoGroup();
            _doc.InsertTextAt(block, offset, marker + marker);
            _doc.CursorOffset = offset + marker.Length;
            _doc.AnchorBlock = block;
            _doc.AnchorOffset = _doc.CursorOffset;
            _doc.SealUndoGroup();
            return;
        }

        var (sb, so, eb, eo) = _doc.GetOrderedSelection();
        so = Math.Min(so, _doc.GetBlockLength(sb));
        eo = Math.Min(eo, _doc.GetBlockLength(eb));

        ComputeLayout();

        int markerLen = marker.Length;
        int styleMarkerLen = MarkdownParser.GetMarkerLength(targetStyle);

        bool allStyled = true;
        for (int b = sb; b <= eb; b++)
        {
            int bStart = (b == sb) ? so : 0;
            int bEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (bStart >= bEnd) continue;

            var parsed = _parsedBlocks![b];
            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                if (runEnd <= bStart || run.Start >= bEnd) continue;

                int contentStart = run.Start + styleMarkerLen;
                int contentEnd = runEnd - styleMarkerLen;
                int overlapStart = Math.Max(bStart, contentStart);
                int overlapEnd = Math.Min(bEnd, contentEnd);
                bool hasTargetStyle = run.Style == targetStyle
                    || (run.Style == InlineStyle.BoldItalic
                        && (targetStyle == InlineStyle.Bold || targetStyle == InlineStyle.Italic));
                if (overlapStart < overlapEnd && hasTargetStyle) continue;

                allStyled = false;
                break;
            }
            if (!allStyled) break;
        }

        _doc.BeginUndoGroup();

        int newSo = so, newEo = eo;

        for (int b = eb; b >= sb; b--)
        {
            int bStart = (b == sb) ? so : 0;
            int bEnd = (b == eb) ? eo : _doc.GetBlockLength(b);
            if (bStart >= bEnd) continue;

            if (allStyled)
            {
                var parsed = _parsedBlocks![b];
                foreach (var run in parsed.Runs)
                {
                    int runEnd = run.Start + run.Length;
                    if (runEnd <= bStart || run.Start >= bEnd) continue;
                    if (run.Style != targetStyle
                        && !(run.Style == InlineStyle.BoldItalic
                            && (targetStyle == InlineStyle.Bold || targetStyle == InlineStyle.Italic)))
                        continue;

                    _doc.RemoveTextAt(b, runEnd - markerLen, markerLen);
                    _doc.RemoveTextAt(b, run.Start, markerLen);

                    if (b == eb)
                    {
                        newEo = eo - markerLen;
                        if (eo > runEnd - markerLen) newEo -= markerLen;
                    }
                    if (b == sb)
                        newSo = so - markerLen;
                    break;
                }
            }
            else
            {
                _doc.InsertTextAt(b, bEnd, marker);
                _doc.InsertTextAt(b, bStart, marker);
                if (b == sb) newSo = so + markerLen;
                if (b == eb) newEo = eo + markerLen;
            }
        }

        _doc.AnchorBlock = sb;
        _doc.AnchorOffset = newSo;
        _doc.CursorBlock = eb;
        _doc.CursorOffset = newEo;
        _doc.SealUndoGroup();
    }
}
