using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

internal class TextMeasurer
{
    internal static readonly Typeface NormalTypeface = new("Segoe UI");
    internal static readonly Typeface BoldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    internal static readonly Typeface MonoTypeface = new("Cascadia Mono");
    private static readonly Typeface _italicTypeface = new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface _boldItalicTypeface = new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
    private static readonly double[] _headingFontSizes = [32, 26, 22, 18, 16, 14];
    internal const double BaseFontSize = 16;
    private const double _codeFontSize = 14;
    internal const double ListIndent = 20;

    private double _dpiScale = 1.0;
    internal double DpiScale => _dpiScale;
    private bool _measured;
    internal bool IsMeasured => _measured;
    private GlyphTypeface? _normalGlyph;
    private GlyphTypeface? _boldGlyph;
    private GlyphTypeface? _italicGlyph;
    private GlyphTypeface? _boldItalicGlyph;
    private GlyphTypeface? _monoGlyph;
    private readonly Dictionary<(char, int), double> _charWidthCache = new();
    private readonly Dictionary<BlockKind, double> _lineHeights = new();

    internal void EnsureMeasured(Visual visual)
    {
        if (_measured) return;
        if (PresentationSource.FromVisual(visual) != null)
            _dpiScale = VisualTreeHelper.GetDpi(visual).PixelsPerDip;

        NormalTypeface.TryGetGlyphTypeface(out _normalGlyph);
        BoldTypeface.TryGetGlyphTypeface(out _boldGlyph);
        _italicTypeface.TryGetGlyphTypeface(out _italicGlyph);
        _boldItalicTypeface.TryGetGlyphTypeface(out _boldItalicGlyph);
        MonoTypeface.TryGetGlyphTypeface(out _monoGlyph);

        foreach (BlockKind kind in Enum.GetValues<BlockKind>())
        {
            double fontSize = GetBlockFontSize(kind);
            var ft = new FormattedText("M", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GetBlockBaseTypeface(kind), fontSize,
                Brushes.Black, _dpiScale);
            _lineHeights[kind] = ft.Height;
        }

        _measured = true;
    }

    internal static double GetBlockFontSize(BlockKind kind) => kind switch
    {
        BlockKind.Heading1 => _headingFontSizes[0],
        BlockKind.Heading2 => _headingFontSizes[1],
        BlockKind.Heading3 => _headingFontSizes[2],
        BlockKind.Heading4 => _headingFontSizes[3],
        BlockKind.Heading5 => _headingFontSizes[4],
        BlockKind.Heading6 => _headingFontSizes[5],
        BlockKind.FencedCodeLine => _codeFontSize,
        _ => BaseFontSize,
    };

    internal static Typeface GetBlockBaseTypeface(BlockKind kind) => kind switch
    {
        BlockKind.FencedCodeLine => MonoTypeface,
        _ => NormalTypeface,
    };

    internal static Typeface GetInlineTypeface(BlockKind blockKind, InlineStyle style) => blockKind switch
    {
        BlockKind.FencedCodeLine => MonoTypeface,
        BlockKind.TableHeaderRow => style switch
        {
            InlineStyle.Italic or InlineStyle.BoldItalic => _boldItalicTypeface,
            InlineStyle.Code => MonoTypeface,
            _ => BoldTypeface,
        },
        _ => style switch
        {
            InlineStyle.Bold => BoldTypeface,
            InlineStyle.Italic => _italicTypeface,
            InlineStyle.BoldItalic => _boldItalicTypeface,
            InlineStyle.Code => MonoTypeface,
            _ => NormalTypeface,
        },
    };

    internal GlyphTypeface? GetInlineGlyph(BlockKind blockKind, InlineStyle style) => blockKind switch
    {
        BlockKind.FencedCodeLine => _monoGlyph,
        BlockKind.TableHeaderRow => style switch
        {
            InlineStyle.Italic or InlineStyle.BoldItalic => _boldItalicGlyph,
            InlineStyle.Code => _monoGlyph,
            _ => _boldGlyph,
        },
        _ => style switch
        {
            InlineStyle.Bold => _boldGlyph,
            InlineStyle.Italic => _italicGlyph,
            InlineStyle.BoldItalic => _boldItalicGlyph,
            InlineStyle.Code => _monoGlyph,
            _ => _normalGlyph,
        },
    };

    internal static int GetStyleKey(BlockKind blockKind, InlineStyle style)
    {
        int fontId = blockKind == BlockKind.FencedCodeLine || style == InlineStyle.Code ? 1 : 0;
        if (style == InlineStyle.Bold) fontId = 2;
        else if (style == InlineStyle.Italic) fontId = 3;
        else if (style == InlineStyle.BoldItalic) fontId = 4;
        if (blockKind == BlockKind.FencedCodeLine) fontId = 1;
        if (blockKind == BlockKind.TableHeaderRow && fontId == 0) fontId = 2;
        else if (blockKind == BlockKind.TableHeaderRow && fontId == 3) fontId = 4;
        int sizeKey = (int)GetBlockFontSize(blockKind);
        return fontId * 100 + sizeKey;
    }

    internal double GetLineHeight(BlockKind kind)
    {
        return _lineHeights.TryGetValue(kind, out double h) ? h : _lineHeights[BlockKind.Paragraph];
    }

    internal double MeasureCharWidth(char ch, BlockKind blockKind, InlineStyle style)
    {
        int key = GetStyleKey(blockKind, style);
        if (!_charWidthCache.TryGetValue((ch, key), out double w))
        {
            double fontSize = GetBlockFontSize(blockKind);
            var glyph = GetInlineGlyph(blockKind, style);
            if (glyph != null && glyph.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIndex))
            {
                w = glyph.AdvanceWidths[glyphIndex] * fontSize;
            }
            else
            {
                var typeface = GetInlineTypeface(blockKind, style);
                var ft = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    Brushes.Black, _dpiScale);
                w = ft.WidthIncludingTrailingWhitespace;
            }
            _charWidthCache[(ch, key)] = w;
        }
        return w;
    }

    internal static InlineStyle GetStyleAtOffset(IReadOnlyList<StyledRun> runs, int offset, ref int runHint)
    {
        while (runHint < runs.Count - 1 && offset >= runs[runHint].Start + runs[runHint].Length)
            runHint++;
        return runs[runHint].Style;
    }

    internal double MeasureStringWidth(string text, BlockKind kind, IReadOnlyList<StyledRun> runs, int blockOffset)
    {
        double w = 0;
        int runIdx = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var style = GetStyleAtOffset(runs, blockOffset + i, ref runIdx);
            w += MeasureCharWidth(text[i], kind, style);
        }
        return w;
    }

    internal double MeasureReplacementPrefix(string prefix, BlockKind blockKind)
    {
        if (blockKind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            return ListIndent;
        double total = 0;
        for (int i = 0; i < prefix.Length; i++)
            total += MeasureCharWidth(prefix[i], blockKind, InlineStyle.Normal);
        return total;
    }
}
