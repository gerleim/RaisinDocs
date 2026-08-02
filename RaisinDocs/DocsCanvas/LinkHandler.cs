using System.Windows;
using System.Windows.Controls;

namespace RaisinDocs;

/// <summary>
/// Manages link UI operations including hover tooltips, link clicking, and link insertion.
/// Delegates popup management to LinkPopupController while handling link detection and display.
/// </summary>
internal class LinkHandler
{
    private readonly DocsCanvas _canvas;
    private string? _hoveredLinkUrl;
    private readonly ToolTip _linkToolTip = new()
    {
        Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
    };

    public LinkHandler(DocsCanvas canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Attempts to open a link at the clicked position (Ctrl+Click).
    /// Opens http://, https://, and file:// URLs in the default application.
    /// </summary>
    public bool TryOpenLinkAtClick(Point pos)
    {
        if (_canvas._parsedBlocks == null) return false;

        _canvas.ComputeLayout();
        _canvas.HitTestToPosition(pos, out int block, out int offset);
        if (block >= _canvas._parsedBlocks.Count) return false;

        var parsed = _canvas._parsedBlocks[block];
        if (parsed.Links == null) return false;

        foreach (var link in parsed.Links)
        {
            if (IsLinkHit(link, offset))
            {
                var url = link.Url;
                if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://"))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the link at the given position, or null if no link is found at that position.
    /// Used for tooltip display on hover.
    /// </summary>
    public InlineLink? GetLinkAtPosition(Point pos)
    {
        if (_canvas._parsedBlocks == null) return null;

        _canvas.HitTestToPosition(pos, out int block, out int offset);
        if (block >= _canvas._parsedBlocks.Count) return null;

        var parsed = _canvas._parsedBlocks[block];
        if (parsed.Links == null) return null;

        foreach (var link in parsed.Links)
        {
            if (IsLinkHit(link, offset))
                return link;
        }
        return null;
    }

    /// <summary>
    /// Updates link tooltip on mouse move. Shows the URL when hovering over a link.
    /// </summary>
    public void UpdateLinkTooltip(Point pos)
    {
        var hoverLink = GetLinkAtPosition(pos);
        if (hoverLink != null)
        {
            var url = hoverLink.Value.Url;
            if (_hoveredLinkUrl != url)
            {
                _hoveredLinkUrl = url;
                double effectiveScroll = _canvas._scroll.EffectiveOffset;
                int vli = _canvas.HitTestVisualLine(pos.Y + effectiveScroll);
                double lineY = _canvas._lineYPositions[vli] - effectiveScroll;
                double lineH = _canvas.GetEffectiveLineHeight(_canvas._visualLines[vli]);
                _linkToolTip.Content = url;
                _linkToolTip.PlacementTarget = _canvas;
                _linkToolTip.HorizontalOffset = DocsCanvas._padding;
                _linkToolTip.VerticalOffset = lineY + lineH;
                _linkToolTip.IsOpen = true;
            }
        }
        else
        {
            if (_hoveredLinkUrl != null)
            {
                _hoveredLinkUrl = null;
                _linkToolTip.IsOpen = false;
            }
        }
    }

    /// <summary>
    /// Hides the link tooltip. Called when mouse leaves the canvas.
    /// </summary>
    public void HideLinkTooltip()
    {
        if (_hoveredLinkUrl != null)
        {
            _hoveredLinkUrl = null;
            _linkToolTip.IsOpen = false;
        }
    }

    /// <summary>
    /// Gets the link at cursor position, used for editing existing links.
    /// </summary>
    public InlineLink? GetLinkAtCursor()
    {
        if (_canvas._parsedBlocks == null || _canvas._doc.CursorBlock >= _canvas._parsedBlocks.Count)
            return null;

        var parsed = _canvas._parsedBlocks[_canvas._doc.CursorBlock];
        if (parsed.Links == null) return null;

        int offset = _canvas._doc.CursorOffset;
        foreach (var link in parsed.Links)
        {
            if (offset >= link.Start && offset < link.Start + link.Length)
                return link;
        }
        return null;
    }

    /// <summary>
    /// Determines if cursor is hovering over a link at the given offset.
    /// Handles both visual and source mode cursor positioning.
    /// </summary>
    private bool IsLinkHit(InlineLink link, int offset)
    {
        if (_canvas.IsVisual)
        {
            GetLinkTextRange(link, out int textStart, out int textEnd);
            return offset >= textStart && offset < textEnd;
        }
        return offset >= link.Start && offset < link.Start + link.Length;
    }

    /// <summary>
    /// Gets the visual text range of a link, accounting for markdown syntax.
    /// In visual mode, the link text display excludes the bracket syntax.
    /// </summary>
    private static void GetLinkTextRange(InlineLink link, out int textStart, out int textEnd)
    {
        bool isAutolink = link.Text == link.Url;
        textStart = isAutolink ? link.Start : link.Start + 1;
        textEnd = textStart + link.Text.Length;
    }
}
