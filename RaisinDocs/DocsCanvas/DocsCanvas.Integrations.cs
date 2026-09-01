using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RaisinDocs;

/// <summary>
/// Record structs for integration data.
/// </summary>
internal readonly record struct TocEntry(int BlockIndex, int HeadingLevel, string Text);
internal readonly record struct MinimapTableCell(string Text, double XOffset, int RawStart);

/// <summary>
/// Partial class containing Find/Replace, Table of Contents, Spell Check, and Minimap integrations.
/// </summary>
public partial class DocsCanvas
{
    #region Find and Replace

    private FindAndReplaceController? _findAndReplaceController;

    internal FindAndReplaceController FindAndReplace =>
        _findAndReplaceController ??= new FindAndReplaceController(
            (ISearchServices)this,
            (IDocumentServices)this,
            (ICanvasOperations)this,
            (IRenderingServices)this,
            (ILayoutDataServices)this,
            (IScrollServices)this,
            (IParsedContentServices)this,
            (IVisualModeServices)this,
            (ITableServices)this);

    internal void OpenFind(bool showReplace)
    {
        FindAndReplace.ResetSearchOrigin();

        string? initialText = null;
        if (_doc.HasSelection)
        {
            var (sb, so, eb, eo) = _doc.GetOrderedSelection();
            if (sb == eb)
                initialText = _doc.GetBlockText(sb).Substring(so, eo - so);
        }
        FindBar?.Open(showReplace, initialText);
        FindBar?.ApplyTheme(_palette.Background, _palette.Foreground, _palette.Syntax, _palette.CodeBackground);
    }

    internal void CloseFind()
    {
        FindAndReplace.ClearMatches();
        FindBar?.Close();
        InvalidateVisual();
    }

    internal void ExecuteSearch(string query, bool caseSensitive) =>
        FindAndReplace.ExecuteSearch(query, caseSensitive);

    internal void NavigateMatch(int direction) =>
        FindAndReplace.NavigateMatch(direction);

    internal void ReplaceCurrent(string replacement) =>
        FindAndReplace.ReplaceCurrent(replacement);

    internal void ReplaceAll(string replacement) =>
        FindAndReplace.ReplaceAll(replacement);

    private void InvalidateSearchOnContentChange() =>
        FindAndReplace.InvalidateSearchOnContentChange();

    private void DrawSearchHighlights(DrawingContext dc, double effectiveScroll) =>
        FindAndReplace.DrawSearchHighlights(dc, effectiveScroll);

    // Test hooks
    internal int TestSearchMatchCount => FindAndReplace.TestSearchMatchCount;
    internal int TestCurrentMatchIndex => FindAndReplace.TestCurrentMatchIndex;
    internal void TestExecuteSearch(string query, bool caseSensitive) => FindAndReplace.TestExecuteSearch(query, caseSensitive);

    #endregion

    #region Table of Contents

    internal TocPanel? TocPanel { get; set; }
    internal bool IsTocVisible { get; set; }

    internal void InitTocTheme()
    {
        TocPanel?.ApplyTheme(_palette.Background, _palette.Foreground, _palette.Syntax, _palette.CodeBackground);
    }

    public void ToggleToc()
    {
        var editor = FindParentEditor();
        if (editor != null)
            editor.ShowToc = !editor.ShowToc;
    }

    internal List<TocEntry> GetTocEntries()
    {
        ComputeLayout();
        var entries = new List<TocEntry>();
        if (_parsedBlocks == null) return entries;
        for (int bi = 0; bi < _parsedBlocks.Count; bi++)
        {
            var kind = _parsedBlocks[bi].Kind;
            if (kind >= BlockKind.Heading1 && kind <= BlockKind.Heading6)
            {
                int level = kind - BlockKind.Heading1 + 1;
                string raw = _doc.GetBlockText(bi);
                entries.Add(new TocEntry(bi, level, StripHeadingPrefix(raw, level)));
            }
        }
        return entries;
    }

    internal int GetCurrentHeadingBlock()
    {
        ComputeLayout();
        int cursorBlock = _doc.CursorBlock;
        if (_parsedBlocks == null) return -1;
        for (int bi = Math.Min(cursorBlock, _parsedBlocks.Count - 1); bi >= 0; bi--)
        {
            var kind = _parsedBlocks[bi].Kind;
            if (kind >= BlockKind.Heading1 && kind <= BlockKind.Heading6)
                return bi;
        }
        return -1;
    }

    internal void NavigateToBlock(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= _doc.BlockCount) return;
        _doc.CursorBlock = blockIndex;
        _doc.CursorOffset = 0;
        _doc.CollapseSelection();
        ComputeLayout();
        ScrollBlockToTop(blockIndex);
        InvalidateVisual();
    }

    private void ScrollBlockToTop(int blockIndex)
    {
        _scroll.StopWheelCoast();
        _scroll.CancelSmooth();
        if (_visualLines.Count == 0) return;
        int vli = CursorToVisualLineIndex();
        _scroll.Offset = _lineYPositions[vli] - _padding;
        _scroll.Clamp();
    }

    private static string StripHeadingPrefix(string text, int level)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;
        int hashEnd = i + level;
        if (hashEnd < text.Length && text[hashEnd] == ' ')
            hashEnd++;
        return hashEnd <= text.Length ? text[hashEnd..].TrimEnd() : text.TrimEnd();
    }

    #endregion

    #region Spell Check

    private SpellCheckController? _spellCheckController;

    internal SpellCheckController SpellCheck =>
        _spellCheckController ??= new SpellCheckController(
            (ICanvasOperations)this,
            (IImageServices)this,
            (IDocumentServices)this,
            (IRenderingServices)this,
            (ILayoutDataServices)this,
            (IParsedContentServices)this,
            (ITableServices)this,
            (INavigationServices)this,
            (IVisualModeServices)this,
            (IScrollServices)this);

    public bool SpellCheckEnabled => SpellCheck.SpellCheckEnabled;
    public string? ProjectFolder => SpellCheck.ProjectFolder;

    public void SetSpellCheckEnabled(bool enabled)
    {
        SpellCheck.SetSpellCheckEnabled(enabled);
    }

    private void CleanupSpellCheck()
    {
        _spellCheckController?.Cleanup();
    }

    internal void OnDocumentBasePathChanged()
    {
        _spellCheckController?.OnDocumentBasePathChanged();
    }

    public void SetProjectFolder(string folder)
    {
        SpellCheck.SetProjectFolder(folder);
    }

    private void OnContentChangedForSpellCheck()
    {
        _spellCheckController?.OnContentChanged();
    }

    private void DrawSpellingErrors(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        _spellCheckController?.DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);
    }

    public bool AddSpellCheckMenuItems(ContextMenu menu, Point position)
    {
        return _spellCheckController?.AddSpellCheckMenuItems(menu, position) ?? false;
    }

    public static string? UserDictionaryPath => SpellCheckController.UserDictionaryPath;
    public string? ProjectDictionaryPath => SpellCheck.ProjectDictionaryPath;

    internal SpellCheckService? TestSpellCheckService => _spellCheckController?.TestSpellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _spellCheckController?.TestGetSpellingErrors(blockIndex);

    #endregion

    #region Minimap

    internal MinimapScrollbar? Minimap { get; set; }
    internal bool IsMinimapVisible { get; set; }

    public void ToggleMinimap()
    {
        if (Minimap == null) return;
        var editor = FindParentEditor();
        if (editor != null)
            editor.ShowMinimap = !editor.ShowMinimap;
    }

    private DocsEditor? FindParentEditor()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is DocsEditor editor) return editor;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    internal int MinimapLayoutVersion => _layoutVersion;
    internal int MinimapLineCount => _visualLines.Count;
    internal double MinimapScrollOffset => _scroll.EffectiveOffset;
    internal double MinimapTotalHeight => _totalContentHeight;
    internal IReadOnlyList<double> MinimapCanvasLineYPositions => _lineYPositions;
    internal Color MinimapBackground => ((SolidColorBrush)_palette.Background).Color;
    internal Color MinimapForeground => ((SolidColorBrush)_palette.Foreground).Color;
    internal Color MinimapCodeBackground => ((SolidColorBrush)_palette.CodeBackground).Color;
    internal Color MinimapTableBackground => ((SolidColorBrush)_palette.TableBackground).Color;
    internal Color MinimapTableHeaderBackground => ((SolidColorBrush)_palette.TableHeaderBackground).Color;
    internal double MinimapCanvasTextWidth => Math.Max(1, ActualWidth - _padding * 2);

    internal BlockKind GetMinimapLineKind(int index)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
            return BlockKind.Paragraph;
        return _visualLines[index].BlockKind;
    }

    internal double MinimapBaseLineHeight
    {
        get
        {
            if (_visualLines == null || _visualLines.Count == 0) return 0;
            return _measure.GetLineHeight(BlockKind.Paragraph);
        }
    }

    internal (BitmapSource Image, double Width, double Height, double YOffset)? GetMinimapLineImage(int index)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
            return null;
        var vl = _visualLines[index];
        if (vl.OverrideHeight <= 0) return null;

        BlockVisualMap? map = null;
        if (vl.Group != null)
            map = vl.Group.JoinedMap;
        else if (IsVisual && _visualMaps != null && vl.BlockIndex < _visualMaps.Count)
            map = _visualMaps[vl.BlockIndex];

        if (map?.Images != null)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in map.Images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                {
                    var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
                    if (cached != null)
                        return (cached.Value.Image, cached.Value.Width, cached.Value.Height, 0);
                }
            }
        }

        if (!IsVisual && _imagePreview == ImagePreviewMode.Inline
            && _parsedBlocks != null && vl.BlockIndex < _parsedBlocks.Count)
        {
            var images = _parsedBlocks[vl.BlockIndex].Images;
            if (images != null)
            {
                int vlEnd = vl.StartOffset + vl.Length;
                double textLineH = _measure.GetLineHeight(vl.BlockKind);
                foreach (var img in images)
                {
                    if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                    {
                        var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
                        if (cached != null)
                            return (cached.Value.Image, cached.Value.Width, cached.Value.Height, textLineH);
                    }
                }
            }
        }

        return null;
    }

    internal void GetMinimapLineInfo(int index, out string text, out BlockKind kind)
    {
        if (_visualLines == null || index < 0 || index >= _visualLines.Count)
        {
            text = ""; kind = BlockKind.Paragraph; return;
        }
        var vl = _visualLines[index];
        kind = vl.BlockKind;
        if (vl.Length <= 0) { text = ""; return; }
        string source = vl.Group != null ? vl.Group.JoinedText : _doc.GetBlockText(vl.BlockIndex);
        text = vl.StartOffset + vl.Length <= source.Length
            ? source.Substring(vl.StartOffset, vl.Length)
            : "";
    }

    internal void GetMinimapLineColorInfo(int index, out RgbColor? blockFg, out RgbColor? blockBg,
        out IReadOnlyList<ColorSpan>? colorSpans, out int spanBaseOffset)
    {
        blockFg = null;
        blockBg = null;
        colorSpans = null;
        spanBaseOffset = 0;

        if (_visualLines == null || _parsedBlocks == null || index < 0 || index >= _visualLines.Count)
            return;

        var vl = _visualLines[index];
        spanBaseOffset = vl.StartOffset;

        if (vl.Group != null)
        {
            blockFg = vl.Group.JoinedParsed.BlockColor?.Foreground;
            blockBg = vl.Group.JoinedParsed.BlockColor?.Background;
            colorSpans = vl.Group.JoinedParsed.ColorSpans;
            return;
        }

        if (vl.BlockIndex >= _parsedBlocks.Count) return;
        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
        blockFg = parsed.BlockColor?.Foreground;
        blockBg = parsed.BlockColor?.Background;
        colorSpans = parsed.ColorSpans;
    }

    internal bool GetMinimapTableRowInfo(int index, List<MinimapTableCell> cells,
        out bool isHeader, out double tableWidth,
        out IReadOnlyList<ColorSpan>? colorSpans)
    {
        cells.Clear();
        isHeader = false;
        tableWidth = 0;
        colorSpans = null;

        if (!IsVisual || _visualLines == null || _parsedBlocks == null
            || index < 0 || index >= _visualLines.Count)
            return false;

        var vl = _visualLines[index];
        if (vl.BlockKind is not (BlockKind.TableHeaderRow or BlockKind.TableDataRow))
            return false;
        if (vl.BlockIndex >= _parsedBlocks.Count)
            return false;

        var parsed = _parsedBlocks[vl.BlockIndex];
        if (parsed.Table == null || parsed.TableRow == null)
            return false;
        if (!_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
            return false;

        string blockText = _doc.GetBlockText(vl.BlockIndex);
        BlockVisualMap? map = _visualMaps != null && vl.BlockIndex < _visualMaps.Count
            ? _visualMaps[vl.BlockIndex]
            : null;

        double xOffset = 0;
        int cellCount = Math.Min(parsed.TableRow.Cells.Count, colWidths.Length);
        for (int c = 0; c < cellCount; c++)
        {
            var cell = parsed.TableRow.Cells[c];
            var (s, e) = cell.TrimContent(blockText);

            string cellText = map != null
                ? map.BuildDisplayString(blockText, s, e - s)
                : blockText.Substring(s, e - s);

            cells.Add(new MinimapTableCell(cellText, xOffset + _tableCellPadding, s));
            xOffset += colWidths[c];
        }

        isHeader = parsed.Kind == BlockKind.TableHeaderRow;
        tableWidth = xOffset;
        colorSpans = parsed.ColorSpans;
        return true;
    }

    // --- IMinimapDataProvider implementation ---

    List<VisualLine> IMinimapDataProvider.GetVisualLines() => _visualLines;

    List<double> IMinimapDataProvider.GetLineYPositions() => _lineYPositions;

    double IMinimapDataProvider.GetTotalContentHeight() => _totalContentHeight;

    double IMinimapDataProvider.GetViewportHeight() => ActualHeight;

    List<BlockVisualMap>? IMinimapDataProvider.GetVisualMaps() => _visualMaps;

    #endregion
}
