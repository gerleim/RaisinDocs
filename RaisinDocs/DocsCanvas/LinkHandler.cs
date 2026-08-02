using System.Windows;
using System.Windows.Controls;

namespace RaisinDocs;

/// <summary>
/// Manages link UI operations including hover tooltips, link clicking, and link insertion.
/// Delegates popup management to LinkPopupController while handling link detection and display.
/// </summary>
internal class LinkHandler
{
    private readonly IDocsCanvasServices _services;
    private string? _hoveredLinkUrl;
    private readonly ToolTip _linkToolTip = new()
    {
        Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
    };

    public LinkHandler(IDocsCanvasServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Attempts to open a link at the clicked position (Ctrl+Click).
    /// Opens http://, https://, and file:// URLs in the default application.
    /// </summary>
    public bool TryOpenLinkAtClick(Point pos)
    {
        if (((DocsCanvas)_services)._parsedBlocks == null) return false;

        ((DocsCanvas)_services).ComputeLayout();
        ((DocsCanvas)_services).HitTestToPosition(pos, out int block, out int offset);
        if (block >= ((DocsCanvas)_services)._parsedBlocks.Count) return false;

        var parsed = ((DocsCanvas)_services)._parsedBlocks[block];
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
        if (((DocsCanvas)_services)._parsedBlocks == null) return null;

        ((DocsCanvas)_services).HitTestToPosition(pos, out int block, out int offset);
        if (block >= ((DocsCanvas)_services)._parsedBlocks.Count) return null;

        var parsed = ((DocsCanvas)_services)._parsedBlocks[block];
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
                double effectiveScroll = ((DocsCanvas)_services)._scroll.EffectiveOffset;
                int vli = ((DocsCanvas)_services).HitTestVisualLine(pos.Y + effectiveScroll);
                double lineY = ((DocsCanvas)_services)._lineYPositions[vli] - effectiveScroll;
                double lineH = ((DocsCanvas)_services).GetEffectiveLineHeight(((DocsCanvas)_services)._visualLines[vli]);
                _linkToolTip.Content = url;
                _linkToolTip.PlacementTarget = (DocsCanvas)_services;
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
        if (((DocsCanvas)_services)._parsedBlocks == null || ((DocsCanvas)_services)._doc.CursorBlock >= ((DocsCanvas)_services)._parsedBlocks.Count)
            return null;

        var parsed = ((DocsCanvas)_services)._parsedBlocks[((DocsCanvas)_services)._doc.CursorBlock];
        if (parsed.Links == null) return null;

        int offset = ((DocsCanvas)_services)._doc.CursorOffset;
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
        if (((DocsCanvas)_services).IsVisual)
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
