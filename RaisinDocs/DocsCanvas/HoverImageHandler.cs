using System.Windows;

namespace RaisinDocs;

/// <summary>
/// Manages image hover preview displaying.
/// Shows inline image previews when the mouse hovers over an image in source mode with OnHover preview enabled.
/// Extracted from DocsCanvas.Input to reduce its complexity and improve separation of concerns.
/// </summary>
internal class HoverImageHandler
{
    private readonly IParsedContentServices _parsed;
    private readonly IImageServices _images;
    private readonly INavigationServices _nav;
    private readonly ILayoutDataServices _layout;
    private readonly IScrollServices _scroll;
    private readonly IRenderingServices _rendering;
    private readonly IDocumentServices _doc;
    private readonly DocsCanvas _canvas;

    private const double _padding = 10;

    public HoverImageHandler(
        IParsedContentServices parsed,
        IImageServices images,
        INavigationServices nav,
        ILayoutDataServices layout,
        IScrollServices scroll,
        IRenderingServices rendering,
        IDocumentServices doc,
        DocsCanvas canvas)
    {
        _parsed = parsed ?? throw new ArgumentNullException(nameof(parsed));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    /// <summary>
    /// Updates the hovered image preview based on mouse position.
    /// Shows/hides the image preview popup when hovering over images in source mode with OnHover enabled.
    /// </summary>
    public void UpdateHoverImage(Point pos)
    {
        if (_parsed.IsVisual || _images.ImagePreview != DocsCanvas.ImagePreviewMode.OnHover || _parsed.ParsedBlocks == null)
        {
            if (_canvas._hoveredImage != null) { _canvas._hoveredImage = null; _rendering.InvalidateVisual(); }
            return;
        }

        _layout.ComputeLayout();
        double effectiveScroll = _scroll.Scroll.EffectiveOffset;
        double hitY = pos.Y + effectiveScroll;
        int vli = _nav.HitTestVisualLine(hitY);
        if (vli < 0 || vli >= _nav.VisualLines.Count)
        {
            if (_canvas._hoveredImage != null) { _canvas._hoveredImage = null; _rendering.InvalidateVisual(); }
            return;
        }

        var vl = _nav.VisualLines[vli];
        var parsed = _parsed.ParsedBlocks[vl.BlockIndex];
        if (parsed.Images == null)
        {
            if (_canvas._hoveredImage != null) { _canvas._hoveredImage = null; _rendering.InvalidateVisual(); }
            return;
        }

        int offset = _canvas.HitTestInVisualLineInternal(vli, pos.X - _padding);
        InlineImage? found = null;
        foreach (var img in parsed.Images)
        {
            if (offset >= img.Start && offset < img.Start + img.Length)
            {
                found = img;
                break;
            }
        }

        if (found != _canvas._hoveredImage)
        {
            _canvas._hoveredImage = found;
            _canvas._hoverPosition = pos;
            _rendering.InvalidateVisual();
        }
    }

    /// <summary>
    /// Clears the hovered image state (called on mouse leave).
    /// </summary>
    internal void Clear()
    {
        _canvas._hoveredImage = null;
    }
}
