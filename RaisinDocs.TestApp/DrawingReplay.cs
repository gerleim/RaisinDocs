using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
// WPF and DirectWrite/Direct2D both define these. The WPF ones win by default here,
// because this file is mostly reading a WPF drawing; the D2D ones are qualified.
using D2DRect = Vortice.Mathematics.Rect;
using DWriteGlyphRun = Vortice.DirectWrite.GlyphRun;
using GlyphRun = System.Windows.Media.GlyphRun;
using Rect = System.Windows.Rect;

namespace RaisinDocs.TestApp;

/// <summary>
/// Replays a WPF <see cref="Drawing"/> through a Direct2D device context.
/// </summary>
/// <remarks>
/// The presenter has to draw what the canvas draws, exactly, or the handoff at each end of a
/// gesture shows a jump. Reproducing that from the document would mean a second implementation
/// of wrapping, block fonts, inline styles, colour spans, tables and images - the whole
/// renderer - and every one of those is a chance for the two to disagree.
///
/// They do not have to be reproduced. Every visual line is already rendered into a
/// DrawingVisual, and a DrawingVisual keeps what was drawn into it: glyph runs carrying the
/// glyph indices and advances WPF resolved, geometries with their brushes, images with their
/// rectangles. Replaying that list is drawing the same thing by construction rather than by
/// agreement, and it needs no knowledge of markdown at all.
///
/// It is also the shape that would survive WPF being dropped from the render path: whatever
/// produces the display list, this consumes it.
/// </remarks>
internal sealed class DrawingReplay : IDisposable
{
    private readonly IDWriteFactory _dwrite;
    private readonly ID2D1DeviceContext _d2d;

    /// <summary>Font faces by file, since a document uses very few and creating them is dear.</summary>
    private readonly Dictionary<string, IDWriteFontFace> _faces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Brushes by colour, for the same reason.</summary>
    private readonly Dictionary<uint, ID2D1SolidColorBrush> _brushes = new();

    public DrawingReplay(ID2D1DeviceContext d2d, IDWriteFactory dwrite)
    {
        _d2d = d2d;
        _dwrite = dwrite;
    }

    public void Dispose()
    {
        foreach (var f in _faces.Values) f.Dispose();
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var i in _images.Values) i?.Dispose();
        _images.Clear();
        _faces.Clear();
        _brushes.Clear();
    }

    /// <summary>Replays <paramref name="drawing"/>, translated by the given offsets.</summary>
    public void Replay(Drawing? drawing, double offsetX, double offsetY)
    {
        if (drawing == null) return;
        Walk(drawing, new TranslateTransform(offsetX, offsetY).Value);
    }

    private void Walk(Drawing drawing, Matrix transform)
    {
        switch (drawing)
        {
            case DrawingGroup group:
            {
                var m = transform;
                if (group.Transform != null)
                {
                    var t = group.Transform.Value;
                    t.Append(m);
                    m = t;
                }

                // Clipping is honoured only for rectangles, which is what the canvas uses -
                // table cells and image bounds. A non-rectangular clip is rare enough that
                // ignoring it is better than failing to draw.
                bool pushed = false;
                if (group.ClipGeometry is RectangleGeometry rg)
                {
                    var r = Transformed(rg.Rect, m);
                    _d2d.PushAxisAlignedClip(
                        new D2DRect((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
                        AntialiasMode.Aliased);
                    pushed = true;
                }

                foreach (var child in group.Children) Walk(child, m);

                if (pushed) _d2d.PopAxisAlignedClip();
                break;
            }

            case GlyphRunDrawing glyphs when glyphs.GlyphRun != null:
                DrawGlyphs(glyphs.GlyphRun, glyphs.ForegroundBrush, transform);
                break;

            case GeometryDrawing geo:
                DrawGeometry(geo, transform);
                break;

            case ImageDrawing img:
                DrawImage(img, transform);
                break;
        }
    }

    /// <summary>Decoded images, by source, since a document reuses them and a copy is dear.</summary>
    private readonly Dictionary<ImageSource, ID2D1Bitmap?> _images = new();

    /// <summary>
    /// Draws a WPF image by copying its pixels to the GPU once and caching the result.
    /// </summary>
    /// <remarks>
    /// The BitmapSource has to be frozen to be read here at all, which it is: the display list
    /// is cloned and frozen before it crosses to the render thread.
    /// </remarks>
    private void DrawImage(ImageDrawing img, Matrix transform)
    {
        if (img.ImageSource == null) return;

        if (!_images.TryGetValue(img.ImageSource, out var bitmap))
        {
            bitmap = CreateBitmap(img.ImageSource as BitmapSource);
            _images[img.ImageSource] = bitmap;
        }
        if (bitmap == null) return;

        var r = Transformed(img.Rect, transform);
        _d2d.DrawBitmap(bitmap, r, 1f, Vortice.Direct2D1.InterpolationMode.Linear, null, null);
    }

    private ID2D1Bitmap? CreateBitmap(BitmapSource? source)
    {
        if (source == null) return null;
        try
        {
            // Premultiplied BGRA is what Direct2D wants, and what the swapchain target uses.
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();

            int w = converted.PixelWidth, h = converted.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            int stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            var props = new BitmapProperties1(new Vortice.DCommon.PixelFormat(
                Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));

            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                return _d2d.CreateBitmap(new Vortice.Mathematics.SizeI(w, h),
                    handle.AddrOfPinnedObject(), (uint)stride, props);
            }
            finally { handle.Free(); }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DrawGlyphs(GlyphRun run, Brush? brush, Matrix transform)
    {
        var face = FaceFor(run.GlyphTypeface);
        if (face == null) return;

        int count = run.GlyphIndices.Count;
        if (count == 0) return;

        var indices = new ushort[count];
        var advances = new float[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = run.GlyphIndices[i];
            advances[i] = (float)run.AdvanceWidths[i];
        }

        GlyphOffset[]? offsets = null;
        if (run.GlyphOffsets is { Count: > 0 })
        {
            offsets = new GlyphOffset[count];
            for (int i = 0; i < count && i < run.GlyphOffsets.Count; i++)
                offsets[i] = new GlyphOffset
                {
                    AdvanceOffset = (float)run.GlyphOffsets[i].X,
                    // WPF measures a glyph offset down-positive and DirectWrite measures the
                    // ascender up-positive, so this one is negated.
                    AscenderOffset = -(float)run.GlyphOffsets[i].Y,
                };
        }

        var origin = transform.Transform(run.BaselineOrigin);

        var dwRun = new DWriteGlyphRun
        {
            FontFace = face,
            FontEmSize = (float)run.FontRenderingEmSize,
            Indices = indices,
            Advances = advances,
            Offsets = offsets,
            IsSideways = run.IsSideways,
            BidiLevel = run.BidiLevel,
        };

        _d2d.DrawGlyphRun(new System.Numerics.Vector2((float)origin.X, (float)origin.Y),
            dwRun, BrushFor(brush), Vortice.DCommon.MeasuringMode.Natural);
    }

    private void DrawGeometry(GeometryDrawing geo, Matrix transform)
    {
        if (geo.Geometry == null) return;

        // Rectangles and lines cover everything the canvas draws as geometry: backgrounds,
        // selection, table borders, rules, bullets and the caret. Anything else is skipped
        // rather than approximated, so a gap is visible rather than subtly wrong.
        switch (geo.Geometry)
        {
            case RectangleGeometry rect:
            {
                var r = Transformed(rect.Rect, transform);
                var d2dRect = new D2DRect((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

                if (geo.Brush != null)
                    _d2d.FillRectangle(d2dRect, BrushFor(geo.Brush));
                if (geo.Pen?.Brush != null)
                    _d2d.DrawRectangle(d2dRect, BrushFor(geo.Pen.Brush), (float)geo.Pen.Thickness);
                break;
            }

            case LineGeometry line:
            {
                if (geo.Pen?.Brush == null) break;
                var a = transform.Transform(line.StartPoint);
                var b = transform.Transform(line.EndPoint);
                _d2d.DrawLine(
                    new System.Numerics.Vector2((float)a.X, (float)a.Y),
                    new System.Numerics.Vector2((float)b.X, (float)b.Y),
                    BrushFor(geo.Pen.Brush), (float)geo.Pen.Thickness);
                break;
            }

            case EllipseGeometry ellipse:
            {
                var c = transform.Transform(ellipse.Center);
                var e = new Ellipse(new System.Numerics.Vector2((float)c.X, (float)c.Y),
                    (float)ellipse.RadiusX, (float)ellipse.RadiusY);
                if (geo.Brush != null) _d2d.FillEllipse(e, BrushFor(geo.Brush));
                if (geo.Pen?.Brush != null)
                    _d2d.DrawEllipse(e, BrushFor(geo.Pen.Brush), (float)geo.Pen.Thickness);
                break;
            }
        }
    }

    private static D2DRect Transformed(Rect r, Matrix m)
    {
        var tl = m.Transform(new Point(r.Left, r.Top));
        var br = m.Transform(new Point(r.Right, r.Bottom));
        return new D2DRect((float)tl.X, (float)tl.Y, (float)(br.X - tl.X), (float)(br.Y - tl.Y));
    }

    /// <summary>
    /// The DirectWrite face for a WPF typeface, found through the font file WPF loaded it from.
    /// </summary>
    /// <remarks>
    /// Going through the file rather than the family name matters: a family name has to be
    /// resolved again, and the resolution can land on a different face - a different weight, or
    /// a fallback for a missing glyph - which is exactly the sort of difference that shows at
    /// the seam. The file and face index are what WPF actually used.
    /// </remarks>
    private IDWriteFontFace? FaceFor(GlyphTypeface typeface)
    {
        string path;
        try
        {
            var uri = typeface.FontUri;
            if (!uri.IsFile) return null;
            path = uri.LocalPath;
            if (!File.Exists(path)) return null;
        }
        catch (Exception) { return null; }

        // A font collection (.ttc) puts the face index in the URI fragment; a plain font
        // file has no fragment and is face zero.
        uint faceIndex = 0;
        try
        {
            string frag = typeface.FontUri.Fragment.TrimStart('#');
            if (frag.Length > 0) uint.TryParse(frag, out faceIndex);
        }
        catch (Exception) { }

        string key = $"{path}#{faceIndex}";
        if (_faces.TryGetValue(key, out var cached)) return cached;

        try
        {
            using var file = _dwrite.CreateFontFileReference(path);
            var face = _dwrite.CreateFontFace(FontFaceType.Truetype, new[] { file },
                faceIndex, FontSimulations.None);
            _faces[key] = face;
            return face;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ID2D1SolidColorBrush BrushFor(Brush? brush)
    {
        var c = brush is SolidColorBrush scb
            ? scb.Color
            : System.Windows.Media.Colors.Transparent;

        double opacity = brush?.Opacity ?? 1.0;
        byte a = (byte)Math.Clamp(c.A * opacity, 0, 255);

        uint key = ((uint)a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (_brushes.TryGetValue(key, out var cached)) return cached;

        var made = _d2d.CreateSolidColorBrush(
            new Color4(c.R / 255f, c.G / 255f, c.B / 255f, a / 255f));
        _brushes[key] = made;
        return made;
    }
}
