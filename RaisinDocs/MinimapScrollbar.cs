namespace RaisinDocs;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public class MinimapScrollbar : FrameworkElement, IMinimapDataProvider
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
    private Color _cachedBg, _cachedFg, _cachedCodeBg;
    private double[]? _lineYPos;
    private double _totalMinimapH;
    private int _heightTableVersion = -1;
    private readonly List<MinimapTableCell> _tableCells = new();
    private readonly Dictionary<BitmapSource, (byte[] Pixels, int Width, int Height)> _thumbCache = new();

    // Incremental rendering state
    private int _cachedBitmapFirstLine;
    private int _cachedBitmapLineCount;
    private double _cachedBitmapStartY;
    private bool _needsFullRebuild = true;

    internal DocsCanvas? Canvas { get; set; }

    private double _vpTop, _vpHeight;
    private int _totalLines;
    private double _minimapScroll;

    private bool _isDragging;
    private bool _isHovering;
    private double _hoverY;
    private double _dragStartY;
    private double _dragStartScroll;
    private double _dragPixelToScroll;

    internal event Action<double>? ScrollRequested;
    internal event Action<double>? SmoothScrollRequested;

    internal double TestVpTop => _vpTop;
    internal double TestVpHeight => _vpHeight;
    internal double TestTotalMinimapH => _totalMinimapH;
    internal double TestMinimapScroll => _minimapScroll;

    internal void TestUpdateViewport()
    {
        if (Canvas == null) return;
        int totalLines = Canvas.MinimapLineCount;
        _totalLines = totalLines;
        if (totalLines == 0) return;
        int version = Canvas.MinimapLayoutVersion;
        RebuildHeightTable(totalLines, version);
        ComputeViewport(ActualHeight);
    }

    private void ComputeViewport(double h)
    {
        if (Canvas == null || _totalLines == 0) return;

        double totalMinimapH = _totalMinimapH;
        double effectiveScroll = Canvas.MinimapScrollOffset;
        double totalContentH = Canvas.MinimapTotalHeight;
        double canvasH = Canvas.ActualHeight;
        double maxScroll = Math.Max(0, totalContentH - canvasH);

        double scale = totalContentH > 0 ? totalMinimapH / totalContentH : 1;
        double vpContentTop = effectiveScroll * scale;
        _vpHeight = Math.Max(CharHeight, canvasH * scale);

        double scrollFrac = maxScroll > 0 ? Math.Clamp(effectiveScroll / maxScroll, 0, 1) : 0;

        if (totalMinimapH <= h)
        {
            _minimapScroll = 0;
            _vpTop = vpContentTop;
        }
        else
        {
            double viewableRange = totalMinimapH - h;
            _minimapScroll = scrollFrac * viewableRange;
            _vpTop = vpContentTop - _minimapScroll;
        }
    }

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

        ComputeViewport(h);

        double totalMinimapH = _totalMinimapH;
        double effectiveScroll = Canvas.MinimapScrollOffset;
        double totalContentH = Canvas.MinimapTotalHeight;
        double canvasH = Canvas.ActualHeight;
        double maxScroll = Math.Max(0, totalContentH - canvasH);

        double scrollFrac = maxScroll > 0 ? Math.Clamp(effectiveScroll / maxScroll, 0, 1) : 0;

        int firstLine;
        int visibleCount;
        double subPixelOff = 0;

        if (totalMinimapH <= h)
        {
            firstLine = 0;
            visibleCount = totalLines;
        }
        else
        {
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

        // Incremental rendering: build larger cache to avoid frequent rebuilds
        // Use 3x height multiplier with significant overlap to hide cache boundaries
        const double CacheHeightMultiplier = 3.0;
        double cacheHeight = Math.Max(h * CacheHeightMultiplier, h + CharHeight * 30);
        double cacheOverlapFraction = 0.4; // 40% overlap on each side

        int cachePadLineCount = (int)Math.Ceiling(cacheHeight / CharHeight * cacheOverlapFraction);
        int cacheFirstLine = Math.Max(0, firstLine - cachePadLineCount);
        int cacheLastLine = Math.Min(totalLines - 1, firstLine + visibleCount + cachePadLineCount);
        int cacheLineCount = cacheLastLine - cacheFirstLine + 1;
        int cacheBitmapH = Math.Max((int)cacheHeight, (int)h);

        bool needsRebuild = _needsFullRebuild
            || _bitmap == null
            || _bitmap.PixelWidth != (int)w || _bitmap.PixelHeight != cacheBitmapH
            || version != _cachedVersion
            || bg != _cachedBg || fg != _cachedFg || codeBg != _cachedCodeBg
            || cacheFirstLine < _cachedBitmapFirstLine || cacheLastLine > _cachedBitmapFirstLine + _cachedBitmapLineCount - 1;

        if (needsRebuild)
        {
            // Direct call when diagnostics are off, so the captured locals below cost nothing
            // in a normal run.
            if (!ScrollDiag.Enabled)
                RebuildBitmap((int)w, cacheBitmapH, cacheFirstLine, cacheLineCount, 0, bg, fg, codeBg, canvasTextWidth);
            else
                ScrollDiag.Time("minimap-rebuild", () =>
                    RebuildBitmap((int)w, cacheBitmapH, cacheFirstLine, cacheLineCount, 0, bg, fg, codeBg, canvasTextWidth));
            _cachedVersion = version;
            _cachedBitmapFirstLine = cacheFirstLine;
            _cachedBitmapLineCount = cacheLineCount;
            _cachedBitmapStartY = cacheFirstLine > 0 ? _lineYPos![cacheFirstLine] : 0;
            _cachedBg = bg;
            _cachedFg = fg;
            _cachedCodeBg = codeBg;
            _needsFullRebuild = false;
        }

        if (_bitmap != null)
        {
            double viewportY = _minimapScroll;
            double offsetFromCacheStart = viewportY - _cachedBitmapStartY;
            double destY = -offsetFromCacheStart;

            // Clip drawing to control bounds to prevent overflow
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

            dc.DrawImage(_bitmap, new Rect(0, destY, w, _bitmap.PixelHeight));

            dc.Pop();

            if (destY > 0)
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, destY));
            }
            if (destY + _bitmap.PixelHeight < h)
            {
                double fillY = destY + _bitmap.PixelHeight;
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, fillY, w, h - fillY));
            }
        }

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

            double textOnlyH = 0;
            var imageInfo = Canvas!.GetMinimapLineImage(lineIdx);
            if (imageInfo != null)
            {
                var (bmpSrc, imgW, imgH, yOffset) = imageInfo.Value;
                double lineH3 = _lineYPos[lineIdx + 1] - _lineYPos[lineIdx];
                double lineY3 = (_lineYPos[lineIdx] - firstLineYPos) - subPixelOff;
                double baseLineH = Canvas.MinimapBaseLineHeight;
                double mmScale = baseLineH > 0 ? CharHeight / baseLineH : 1;
                double imgOffsetMm = yOffset * mmScale;
                int imgPy0 = (int)(lineY3 + imgOffsetMm);
                int imgPyEnd = (int)(lineY3 + lineH3);
                if (imgPyEnd > 0 && imgPy0 < h)
                {
                    double imgXScale = (w - 2.0) / canvasTextWidth;
                    int imgPxEnd = Math.Min(w, (int)(1 + imgW * imgXScale));
                    RenderImageThumbnail(bmpSrc, 1, imgPxEnd, imgPy0, imgPyEnd, w, h);
                }
                if (yOffset > 0) textOnlyH = imgOffsetMm;
                else continue;
            }

            double lineH = textOnlyH > 0 ? textOnlyH : _lineYPos[lineIdx + 1] - _lineYPos[lineIdx];
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

    private void RenderImageThumbnail(BitmapSource src, int px0, int px1, int py0, int pyEnd, int w, int h)
    {
        int destW = px1 - px0;
        int destH = pyEnd - py0;
        if (destW <= 0 || destH <= 0) return;

        if (!_thumbCache.TryGetValue(src, out var thumb))
        {
            int srcW = src.PixelWidth;
            int srcH = src.PixelHeight;
            if (srcW <= 0 || srcH <= 0) return;

            const int maxThumbW = 120;
            const int maxThumbH = 200;
            BitmapSource readable = src;
            if (srcW > maxThumbW || srcH > maxThumbH)
            {
                double scale = Math.Min((double)maxThumbW / srcW, (double)maxThumbH / srcH);
                var scaled = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                scaled.Freeze();
                readable = scaled;
            }

            if (readable.Format != PixelFormats.Bgra32 && readable.Format != PixelFormats.Pbgra32)
            {
                var converted = new FormatConvertedBitmap(readable, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                readable = converted;
            }

            int tw = readable.PixelWidth;
            int th = readable.PixelHeight;
            var pixels = new byte[tw * th * 4];
            readable.CopyPixels(pixels, tw * 4, 0);
            thumb = (pixels, tw, th);
            _thumbCache[src] = thumb;
        }

        int thumbW = thumb.Width;
        int thumbH = thumb.Height;
        var srcPixels = thumb.Pixels;
        int srcStride = thumbW * 4;

        for (int dy = 0; dy < destH; dy++)
        {
            int outY = py0 + dy;
            if (outY < 0 || outY >= h) continue;
            int sy = (int)((double)dy / destH * thumbH);
            if (sy >= thumbH) sy = thumbH - 1;

            for (int dx = 0; dx < destW; dx++)
            {
                int outX = px0 + dx;
                if (outX < 0 || outX >= w) continue;
                int sx = (int)((double)dx / destW * thumbW);
                if (sx >= thumbW) sx = thumbW - 1;

                int srcOff = sy * srcStride + sx * 4;
                byte sB = srcPixels[srcOff];
                byte sG = srcPixels[srcOff + 1];
                byte sR = srcPixels[srcOff + 2];
                byte sA = srcPixels[srcOff + 3];

                if (sA == 0) continue;

                int dstOff = (outY * w + outX) * 4;
                if (sA == 255)
                {
                    _pixelBuf[dstOff] = sB;
                    _pixelBuf[dstOff + 1] = sG;
                    _pixelBuf[dstOff + 2] = sR;
                }
                else
                {
                    double a = sA / 255.0;
                    _pixelBuf[dstOff] = (byte)(sB * a + _pixelBuf[dstOff] * (1 - a));
                    _pixelBuf[dstOff + 1] = (byte)(sG * a + _pixelBuf[dstOff + 1] * (1 - a));
                    _pixelBuf[dstOff + 2] = (byte)(sR * a + _pixelBuf[dstOff + 2] * (1 - a));
                }
            }
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
            int codePoint;

            // Handle UTF-16 surrogate pairs (emoji, other non-BMP characters)
            if (char.IsHighSurrogate(text, ci) && ci + 1 < text.Length && char.IsLowSurrogate(text, ci + 1))
            {
                codePoint = char.ConvertToUtf32(text, ci);
                ci++;  // Skip the low surrogate
            }
            else
            {
                codePoint = text[ci];
            }

            GlyphInfo glyph;
            if (codePoint >= FirstPrintable && codePoint <= LastPrintable)
                glyph = glyphs[codePoint - FirstPrintable];
            else if (codePoint > LastPrintable)
                glyph = GetExtendedGlyph(codePoint, isCode);
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

    private void RebuildHeightTable(int totalLines, int version)
    {
        if (_lineYPos != null && _heightTableVersion == version && _lineYPos.Length == totalLines + 1)
            return;

        _thumbCache.Clear();
        _needsFullRebuild = true;
        var canvasLineYs = Canvas!.MinimapCanvasLineYPositions;
        double totalContentH = Canvas.MinimapTotalHeight;
        double baseLineH = Canvas.MinimapBaseLineHeight;
        double scale = baseLineH > 0 ? CharHeight / baseLineH : 1;

        _lineYPos = new double[totalLines + 1];
        for (int i = 0; i < totalLines && i < canvasLineYs.Count; i++)
            _lineYPos[i] = canvasLineYs[i] * scale;
        _lineYPos[totalLines] = totalContentH * scale;
        _totalMinimapH = totalContentH * scale;
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
        RenderOptions.SetEdgeMode(dv, EdgeMode.Aliased);  // Disable anti-aliasing for sharp rendering at scale
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
            double newOffset = _dragStartScroll + deltaY * _dragPixelToScroll;
            ScrollRequested?.Invoke(newOffset);
            InvalidateVisual();
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
            double screenRange = ActualHeight - _vpHeight;
            double maxScroll = Canvas != null
                ? Math.Max(0, Canvas.MinimapTotalHeight - Canvas.ActualHeight)
                : 0;
            _dragPixelToScroll = screenRange > 0 ? maxScroll / screenRange : 0;
            CaptureMouse();
            e.Handled = true;
        }
        else if (Canvas != null)
        {
            double totalContentH = Canvas.MinimapTotalHeight;
            double scale = totalContentH > 0 ? _totalMinimapH / totalContentH : 1;
            double minimapContentY = y + _minimapScroll - _vpHeight / 2;
            double canvasY = scale > 0 ? minimapContentY / scale : 0;
            SmoothScrollRequested?.Invoke(canvasY);
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

    // ====== IMinimapDataProvider Implementation ======

    List<DocsCanvas.VisualLine> IMinimapDataProvider.GetVisualLines()
    {
        return Canvas?._visualLines ?? new List<DocsCanvas.VisualLine>();
    }

    List<double> IMinimapDataProvider.GetLineYPositions()
    {
        return Canvas?._lineYPositions ?? new List<double>();
    }

    double IMinimapDataProvider.GetTotalContentHeight()
    {
        return Canvas?.TotalContentHeight ?? 0;
    }

    double IMinimapDataProvider.GetViewportHeight()
    {
        return Canvas?.ActualHeight ?? 0;
    }

    List<BlockVisualMap>? IMinimapDataProvider.GetVisualMaps()
    {
        return Canvas?._visualMaps;
    }
}
