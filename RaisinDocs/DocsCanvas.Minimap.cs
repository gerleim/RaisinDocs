using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

internal readonly record struct MinimapTableCell(string Text, double XOffset, int RawStart);

public partial class DocsCanvas
{
    // --- Minimap support ---

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
}
