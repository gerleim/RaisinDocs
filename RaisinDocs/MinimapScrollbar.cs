namespace RaisinDocs;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public class MinimapScrollbar : FrameworkElement
{
    private const double CharHeight = 4.0;
    private const int GlyphH = 3;

    private struct GlyphInfo
    {
        public byte Width;
        public float[] Alphas;
    }

    private static GlyphInfo[]? s_propGlyphs;
    private static GlyphInfo[]? s_monoGlyphs;
    private static double s_propCellW;
    private static double s_monoCellW;
    private static Typeface s_propTypeface = null!;
    private static Typeface s_monoTypeface = null!;
    private static Dictionary<int, GlyphInfo> s_propExtended = null!;
    private static Dictionary<int, GlyphInfo> s_monoExtended = null!;
    private const int FirstPrintable = 32;
    private const int LastPrintable = 126;

    private static readonly SolidColorBrush s_viewportFill;
    private static readonly Pen s_viewportPen;
    private static readonly SolidColorBrush s_hoverBrush;

    static MinimapScrollbar()
    {
        s_viewportFill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        s_viewportFill.Freeze();
        var borderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        borderBrush.Freeze();
        s_viewportPen = new Pen(borderBrush, 1);
        s_viewportPen.Freeze();
        s_hoverBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        s_hoverBrush.Freeze();
    }

    private WriteableBitmap? _bitmap;
    private byte[] _pixelBuf = Array.Empty<byte>();
    private int _cachedVersion;
    private int _cachedFirstLine;
    private int _cachedVisibleCount;
    private Color _cachedBg, _cachedFg, _cachedCodeBg;
    private double[]? _lineYPos;
    private double _totalMinimapH;
    private int _heightTableVersion = -1;
    private readonly List<MinimapTableCell> _tableCells = new();

    internal DocsCanvas? Canvas { get; set; }

    private double _vpTop, _vpHeight;
    private int _totalLines;
    private double _minimapScroll;

    private bool _isDragging;
    private bool _isHovering;
    private double _hoverY;
    private double _dragStartY;
    private double _dragStartScroll;

    internal event Action<double>? ScrollRequested;
    internal event Action<double>? SmoothScrollRequested;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1 || Canvas == null) return;

        EnsureGlyphTables();

        Color bg = Canvas.MinimapBackground;
        Color fg = Canvas.MinimapForeground;
        Color codeBg = Canvas.MinimapCodeBackground;

        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));

        int totalLines = Canvas.MinimapLineCount;
        _totalLines = totalLines;
        if (totalLines == 0) return;

        int version = Canvas.MinimapLayoutVersion;
        RebuildHeightTable(totalLines, version);

        double totalMinimapH = _totalMinimapH;
        double effectiveScroll = Canvas.MinimapScrollOffset;
        double totalContentH = Canvas.MinimapTotalHeight;
        double canvasH = Canvas.ActualHeight;
        double maxScroll = Math.Max(0, totalContentH - canvasH);

        double scrollFrac = maxScroll > 0 ? Math.Clamp(effectiveScroll / maxScroll, 0, 1) : 0;
        double vpFrac = totalContentH > 0 ? canvasH / totalContentH : 1;

        _vpHeight = Math.Max(CharHeight, vpFrac * totalMinimapH);
        double vpContentTop = scrollFrac * (totalMinimapH - _vpHeight);

        int firstLine;
        int visibleCount;
        double subPixelOff = 0;

        if (totalMinimapH <= h)
        {
            _minimapScroll = 0;
            firstLine = 0;
            visibleCount = totalLines;
            _vpTop = vpContentTop;
        }
        else
        {
            double viewableRange = totalMinimapH - h;
            _minimapScroll = scrollFrac * viewableRange;
            _vpTop = vpContentTop - _minimapScroll;
            firstLine = FindFirstLine(_minimapScroll);
            subPixelOff = _minimapScroll - _lineYPos![firstLine];

            double yEnd = _minimapScroll + h;
            visibleCount = 0;
            for (int i = firstLine; i < totalLines; i++)
            {
                if (_lineYPos[i] >= yEnd) break;
                visibleCount++;
            }
            visibleCount = Math.Min(visibleCount + 1, totalLines - firstLine);
        }

        double canvasTextWidth = Canvas.MinimapCanvasTextWidth;

        if (_bitmap == null
            || _bitmap.PixelWidth != (int)w || _bitmap.PixelHeight != (int)h
            || version != _cachedVersion
            || firstLine != _cachedFirstLine
            || visibleCount != _cachedVisibleCount
            || bg != _cachedBg || fg != _cachedFg || codeBg != _cachedCodeBg)
        {
            RebuildBitmap((int)w, (int)h, firstLine, visibleCount, subPixelOff, bg, fg, codeBg, canvasTextWidth);
            _cachedVersion = version;
            _cachedFirstLine = firstLine;
            _cachedVisibleCount = visibleCount;
            _cachedBg = bg;
            _cachedFg = fg;
            _cachedCodeBg = codeBg;
        }

        if (_bitmap != null)
            dc.DrawImage(_bitmap, new Rect(0, 0, w, h));

        if (_isHovering && !_isDragging)
        {
            double bandH = Math.Max(_vpHeight, CharHeight * 3);
            double bandTop = Math.Clamp(_hoverY - bandH / 2, 0, h - bandH);
            dc.DrawRectangle(s_hoverBrush, null, new Rect(0, bandTop, w, bandH));
        }

        dc.DrawRectangle(s_viewportFill, s_viewportPen, new Rect(0, _vpTop, w, _vpHeight));
    }

    private void RebuildBitmap(int w, int h, int firstLine, int lineCount,
        double subPixelOff, Color bg, Color fg, Color codeBg, double canvasTextWidth)
    {
        if (w <= 0 || h <= 0 || lineCount <= 0) return;

        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
        {
            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            _pixelBuf = new byte[w * h * 4];
        }

        byte bB = bg.B, bG = bg.G, bR = bg.R;
        double codeAlpha = codeBg.A / 255.0;
        byte cbB = (byte)(codeBg.B * codeAlpha + bg.B * (1 - codeAlpha));
        byte cbG = (byte)(codeBg.G * codeAlpha + bg.G * (1 - codeAlpha));
        byte cbR = (byte)(codeBg.R * codeAlpha + bg.R * (1 - codeAlpha));
        for (int i = 0; i < _pixelBuf.Length; i += 4)
        {
            _pixelBuf[i] = bB;
            _pixelBuf[i + 1] = bG;
            _pixelBuf[i + 2] = bR;
            _pixelBuf[i + 3] = 255;
        }

        var propGlyphs = s_propGlyphs!;
        var monoGlyphs = s_monoGlyphs!;
        double xScale = (w - 2.0) / canvasTextWidth;
        double firstLineYPos = _lineYPos![firstLine];

        for (int li = 0; li < lineCount; li++)
        {
            int lineIdx = firstLine + li;
            if (lineIdx >= _totalLines) break;

            Canvas!.GetMinimapLineInfo(lineIdx, out string text, out BlockKind kind);

            if (kind == BlockKind.ThematicBreak)
            {
                double lineH2 = _lineYPos[lineIdx + 1] - _lineYPos[lineIdx];
                double lineY2 = (_lineYPos[lineIdx] - firstLineYPos) - subPixelOff;
                int ruleY = Math.Clamp((int)(lineY2 + lineH2 / 2), 0, h - 1);
                int ruleX0 = 1;
                int ruleX1 = w - 1;
                float ruleAlpha = 0.4f;
                for (int px = ruleX0; px < ruleX1; px++)
                {
                    int off = (ruleY * w + px) * 4;
                    _pixelBuf[off] = (byte)(fg.B * ruleAlpha + _pixelBuf[off] * (1 - ruleAlpha));
                    _pixelBuf[off + 1] = (byte)(fg.G * ruleAlpha + _pixelBuf[off + 1] * (1 - ruleAlpha));
                    _pixelBuf[off + 2] = (byte)(fg.R * ruleAlpha + _pixelBuf[off + 2] * (1 - ruleAlpha));
                }
                continue;
            }

            double lineH = _lineYPos[lineIdx + 1] - _lineYPos[lineIdx];
            double scale = lineH / CharHeight;
            double lineY = (_lineYPos[lineIdx] - firstLineYPos) - subPixelOff;
            int py0 = Math.Max(0, (int)lineY);
            int pyEnd = Math.Min(h, (int)(lineY + lineH));

            if (kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                _tableCells.Clear();
                if (Canvas!.GetMinimapTableRowInfo(lineIdx, _tableCells,
                    out bool isHeader, out double tableWidth,
                    out var tableColorSpans))
                {
                    double propBaseAdvance = s_propCellW * 2 * (16.0 / 24.0) * xScale;
                    int bgPxEnd = Math.Min(w, (int)(1 + tableWidth * xScale));

                    Color tableBg = Canvas.MinimapTableBackground;
                    BlendRect(0, bgPxEnd, py0, pyEnd,
                        tableBg.B, tableBg.G, tableBg.R, tableBg.A / 255.0, w);

                    if (isHeader)
                    {
                        Color hdrBg = Canvas.MinimapTableHeaderBackground;
                        BlendRect(0, bgPxEnd, py0, pyEnd,
                            hdrBg.B, hdrBg.G, hdrBg.R, hdrBg.A / 255.0, w);
                    }

                    foreach (var cell in _tableCells)
                    {
                        RenderTextGlyphs(cell.Text, 1 + cell.XOffset * xScale,
                            lineY, scale, propBaseAdvance, false,
                            fg.B, fg.G, fg.R, tableColorSpans, cell.RawStart,
                            py0, pyEnd, w, h);
                    }
                    continue;
                }
            }

            bool isCode = kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;
            var glyphs = isCode ? monoGlyphs : propGlyphs;
            double cellW = isCode ? s_monoCellW : s_propCellW;
            double baseAdvance = cellW * 2 * (16.0 / 24.0) * xScale;

            byte lineFgB = fg.B, lineFgG = fg.G, lineFgR = fg.R;
            IReadOnlyList<ColorSpan>? colorSpans = null;
            int spanBaseOffset = 0;

            if (isCode)
            {
                for (int py = py0; py < pyEnd; py++)
                    for (int px = 0; px < w; px++)
                    {
                        int off = (py * w + px) * 4;
                        _pixelBuf[off] = cbB;
                        _pixelBuf[off + 1] = cbG;
                        _pixelBuf[off + 2] = cbR;
                    }
            }
            else
            {
                Canvas!.GetMinimapLineColorInfo(lineIdx, out var blockFg, out var blockBg,
                    out colorSpans, out spanBaseOffset);

                if (blockFg != null)
                {
                    lineFgB = blockFg.Value.B;
                    lineFgG = blockFg.Value.G;
                    lineFgR = blockFg.Value.R;
                }

                if (blockBg != null)
                {
                    var bgc = blockBg.Value;
                    BlendRect(0, w, py0, pyEnd, bgc.B, bgc.G, bgc.R, 40.0 / 255.0, w);
                }
            }

            RenderTextGlyphs(text, 1, lineY, scale, baseAdvance, isCode,
                lineFgB, lineFgG, lineFgR, colorSpans, spanBaseOffset,
                py0, pyEnd, w, h);
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixelBuf, w * 4, 0);
    }

    private void BlendRect(int px0, int px1, int py0, int pyEnd, byte cB, byte cG, byte cR, double alpha, int w)
    {
        for (int py = py0; py < pyEnd; py++)
            for (int px = px0; px < px1; px++)
            {
                int off = (py * w + px) * 4;
                _pixelBuf[off] = (byte)(cB * alpha + _pixelBuf[off] * (1 - alpha));
                _pixelBuf[off + 1] = (byte)(cG * alpha + _pixelBuf[off + 1] * (1 - alpha));
                _pixelBuf[off + 2] = (byte)(cR * alpha + _pixelBuf[off + 2] * (1 - alpha));
            }
    }

    private void RenderTextGlyphs(string text, double startX, double lineY, double scale,
        double baseAdvance, bool isCode, byte fgB, byte fgG, byte fgR,
        IReadOnlyList<ColorSpan>? colorSpans, int spanRawBase,
        int py0, int pyEnd, int w, int h)
    {
        var glyphs = isCode ? s_monoGlyphs! : s_propGlyphs!;
        double x = startX;

        for (int ci = 0; ci < text.Length; ci++)
        {
            int ch = text[ci];
            GlyphInfo glyph;
            if (ch >= FirstPrintable && ch <= LastPrintable)
                glyph = glyphs[ch - FirstPrintable];
            else if (ch > LastPrintable)
                glyph = GetExtendedGlyph(ch, isCode);
            else
            {
                x += baseAdvance;
                if (x >= w) break;
                continue;
            }

            int gw = glyph.Width;
            double advance = gw * baseAdvance / 2.0;

            byte cB = fgB, cG = fgG, cR = fgR;
            if (colorSpans != null)
            {
                int rawIdx = spanRawBase + ci;
                foreach (var cs in colorSpans)
                {
                    if (rawIdx >= cs.Start && rawIdx < cs.Start + cs.Length)
                    {
                        if (cs.Background != null)
                        {
                            var bgc = cs.Background.Value;
                            BlendRect(Math.Max(0, (int)x), Math.Min(w, (int)(x + advance)),
                                py0, pyEnd, bgc.B, bgc.G, bgc.R, 40.0 / 255.0, w);
                        }
                        if (cs.Foreground != null)
                        {
                            cB = cs.Foreground.Value.B;
                            cG = cs.Foreground.Value.G;
                            cR = cs.Foreground.Value.R;
                        }
                        break;
                    }
                }
            }

            if (glyph.Alphas != null)
            {
                for (int gy = 0; gy < GlyphH; gy++)
                {
                    int pyStart = Math.Max(0, (int)(lineY + gy * scale));
                    int pyEndG = Math.Min(h, (int)(lineY + (gy + 1) * scale));

                    for (int pyR = pyStart; pyR < pyEndG; pyR++)
                    {
                        for (int gx = 0; gx < gw; gx++)
                        {
                            int pxStart = (int)(x + gx * advance / gw);
                            int pxEnd = Math.Max(pxStart + 1, (int)(x + (gx + 1) * advance / gw));

                            float a = glyph.Alphas[gy * gw + gx];
                            if (a < 0.01f) continue;

                            for (int pxR = pxStart; pxR < pxEnd; pxR++)
                            {
                                if (pxR < 0 || pxR >= w) continue;
                                int off = (pyR * w + pxR) * 4;
                                _pixelBuf[off] = (byte)(cB * a + _pixelBuf[off] * (1 - a));
                                _pixelBuf[off + 1] = (byte)(cG * a + _pixelBuf[off + 1] * (1 - a));
                                _pixelBuf[off + 2] = (byte)(cR * a + _pixelBuf[off + 2] * (1 - a));
                            }
                        }
                    }
                }
            }

            x += advance;
            if (x >= w) break;
        }
    }

    private static double GetLineHeight(BlockKind kind) => kind switch
    {
        BlockKind.Heading1 => CharHeight * 2.0,
        BlockKind.Heading2 => CharHeight * 1.625,
        BlockKind.Heading3 => CharHeight * 1.375,
        BlockKind.Heading4 => CharHeight * 1.125,
        _ => CharHeight,
    };

    private void RebuildHeightTable(int totalLines, int version)
    {
        if (_lineYPos != null && _heightTableVersion == version && _lineYPos.Length == totalLines + 1)
            return;

        _lineYPos = new double[totalLines + 1];
        double y = 0;
        for (int i = 0; i < totalLines; i++)
        {
            _lineYPos[i] = y;
            y += GetLineHeight(Canvas!.GetMinimapLineKind(i));
        }
        _lineYPos[totalLines] = y;
        _totalMinimapH = y;
        _heightTableVersion = version;
    }

    private int FindFirstLine(double scrollOffset)
    {
        if (_lineYPos == null || _lineYPos.Length < 2) return 0;
        int lo = 0, hi = _lineYPos.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_lineYPos[mid] <= scrollOffset)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private static void EnsureGlyphTables()
    {
        if (s_propGlyphs != null) return;
        s_propTypeface = new Typeface("Segoe UI");
        s_monoTypeface = new Typeface("Cascadia Mono");
        s_propGlyphs = BuildGlyphTable(s_propTypeface, out s_propCellW);
        s_monoGlyphs = BuildGlyphTable(s_monoTypeface, out s_monoCellW);
        s_propExtended = new Dictionary<int, GlyphInfo>();
        s_monoExtended = new Dictionary<int, GlyphInfo>();
    }

    private static GlyphInfo[] BuildGlyphTable(Typeface typeface, out double cellWidth)
    {
        const double size = 24.0;
        int count = LastPrintable - FirstPrintable + 1;

        typeface.TryGetGlyphTypeface(out var gt);

        double totalAdv = 0;
        var advances = new double[count];
        for (int c = FirstPrintable; c <= LastPrintable; c++)
        {
            double adv;
            if (gt != null && gt.CharacterToGlyphMap.TryGetValue(c, out var gi))
                adv = gt.AdvanceWidths[gi] * size;
            else
            {
                var ft = new FormattedText(((char)c).ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
                adv = ft.WidthIncludingTrailingWhitespace;
            }
            advances[c - FirstPrintable] = adv;
            totalAdv += adv;
        }
        cellWidth = totalAdv / count / 2.0;

        var result = new GlyphInfo[count];
        for (int c = FirstPrintable; c <= LastPrintable; c++)
        {
            int i = c - FirstPrintable;
            int pw = Math.Clamp((int)Math.Round(advances[i] / cellWidth), 1, 4);

            if (c == ' ')
            {
                result[i] = new GlyphInfo { Width = (byte)pw, Alphas = new float[pw * GlyphH] };
                continue;
            }

            var ft = new FormattedText(((char)c).ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);

            int bw = Math.Max(1, (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 2);
            int bh = Math.Max(1, (int)Math.Ceiling(ft.Height) + 2);

            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
                ctx.DrawText(ft, new Point(1, 0));
            var rtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var pix = new byte[bw * bh * 4];
            rtb.CopyPixels(pix, bw * 4, 0);

            var alphas = new float[pw * GlyphH];
            double cw = (double)bw / pw;
            double ch = (double)bh / GlyphH;

            for (int gy = 0; gy < GlyphH; gy++)
            {
                int py0 = (int)(gy * ch);
                int py1 = Math.Min(bh, Math.Max(py0 + 1, (int)((gy + 1) * ch)));
                for (int gx = 0; gx < pw; gx++)
                {
                    int px0 = (int)(gx * cw);
                    int px1 = Math.Min(bw, Math.Max(px0 + 1, (int)((gx + 1) * cw)));

                    double sum = 0;
                    int cnt = 0;
                    for (int py = py0; py < py1; py++)
                        for (int px = px0; px < px1; px++)
                        {
                            sum += pix[(py * bw + px) * 4 + 3];
                            cnt++;
                        }

                    float alpha = (float)(sum / cnt / 255.0 * 1.8);
                    alphas[gy * pw + gx] = Math.Min(1f, alpha);
                }
            }

            result[i] = new GlyphInfo { Width = (byte)pw, Alphas = alphas };
        }

        return result;
    }

    private static GlyphInfo GetExtendedGlyph(int ch, bool isMono)
    {
        var cache = isMono ? s_monoExtended : s_propExtended;
        if (cache.TryGetValue(ch, out var cached))
            return cached;

        var typeface = isMono ? s_monoTypeface : s_propTypeface;
        double cellW = isMono ? s_monoCellW : s_propCellW;
        var glyph = RenderGlyph(ch, typeface, cellW);
        cache[ch] = glyph;
        return glyph;
    }

    private static GlyphInfo RenderGlyph(int codePoint, Typeface typeface, double cellWidth)
    {
        const double size = 24.0;
        string text = char.ConvertFromUtf32(codePoint);

        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
        double adv = ft.WidthIncludingTrailingWhitespace;
        int pw = Math.Clamp((int)Math.Round(adv / cellWidth), 1, 4);

        int bw = Math.Max(1, (int)Math.Ceiling(adv) + 2);
        int bh = Math.Max(1, (int)Math.Ceiling(ft.Height) + 2);

        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
            ctx.DrawText(ft, new Point(1, 0));
        var rtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var pix = new byte[bw * bh * 4];
        rtb.CopyPixels(pix, bw * 4, 0);

        var alphas = new float[pw * GlyphH];
        double cw = (double)bw / pw;
        double ch = (double)bh / GlyphH;

        for (int gy = 0; gy < GlyphH; gy++)
        {
            int py0 = (int)(gy * ch);
            int py1 = Math.Min(bh, Math.Max(py0 + 1, (int)((gy + 1) * ch)));
            for (int gx = 0; gx < pw; gx++)
            {
                int px0 = (int)(gx * cw);
                int px1 = Math.Min(bw, Math.Max(px0 + 1, (int)((gx + 1) * cw)));

                double sum = 0;
                int cnt = 0;
                for (int py = py0; py < py1; py++)
                    for (int px = px0; px < px1; px++)
                    {
                        sum += pix[(py * bw + px) * 4 + 3];
                        cnt++;
                    }

                float alpha = (float)(sum / cnt / 255.0 * 1.8);
                alphas[gy * pw + gx] = Math.Min(1f, alpha);
            }
        }

        return new GlyphInfo { Width = (byte)pw, Alphas = alphas };
    }

    internal static int FoldToAscii(int ch)
    {
        if (ch < 0x80) return ch;
        if (ch < 0xC0) return 0;
        return ch switch
        {
            >= 0xC0 and <= 0xC5 => 'A',
            0xC6 => 'A',
            0xC7 => 'C',
            >= 0xC8 and <= 0xCB => 'E',
            >= 0xCC and <= 0xCF => 'I',
            0xD0 => 'D',
            0xD1 => 'N',
            >= 0xD2 and <= 0xD6 => 'O',
            0xD8 => 'O',
            >= 0xD9 and <= 0xDC => 'U',
            0xDD => 'Y',
            >= 0xE0 and <= 0xE5 => 'a',
            0xE6 => 'a',
            0xE7 => 'c',
            >= 0xE8 and <= 0xEB => 'e',
            >= 0xEC and <= 0xEF => 'i',
            0xF0 => 'd',
            0xF1 => 'n',
            >= 0xF2 and <= 0xF6 => 'o',
            0xF8 => 'o',
            >= 0xF9 and <= 0xFC => 'u',
            0xFD or 0xFF => 'y',
            _ => 0
        };
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _isHovering = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _isHovering = false;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        double y = e.GetPosition(this).Y;

        if (_isDragging)
        {
            double deltaY = y - _dragStartY;
            double h = ActualHeight;
            double range = h - _vpHeight;
            if (range <= 0) return;

            double maxScroll = Canvas != null
                ? Math.Max(0, Canvas.MinimapTotalHeight - Canvas.ActualHeight)
                : 0;
            double newOffset = _dragStartScroll + deltaY / range * maxScroll;
            ScrollRequested?.Invoke(newOffset);
            return;
        }

        _hoverY = y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        double y = e.GetPosition(this).Y;

        if (y >= _vpTop && y <= _vpTop + _vpHeight)
        {
            _isDragging = true;
            _dragStartY = y;
            _dragStartScroll = Canvas?.MinimapScrollOffset ?? 0;
            CaptureMouse();
            e.Handled = true;
        }
        else if (Canvas != null)
        {
            double totalMinimapH = _totalMinimapH;
            double clickContent = y + _minimapScroll;
            double targetFrac = totalMinimapH > _vpHeight
                ? (clickContent - _vpHeight / 2) / (totalMinimapH - _vpHeight)
                : 0;
            targetFrac = Math.Clamp(targetFrac, 0, 1);
            double maxScroll = Math.Max(0, Canvas.MinimapTotalHeight - Canvas.ActualHeight);
            SmoothScrollRequested?.Invoke(targetFrac * maxScroll);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}
