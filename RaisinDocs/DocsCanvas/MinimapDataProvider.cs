using System.Collections.Generic;

namespace RaisinDocs;

/// <summary>
/// Provides data access for minimap rendering and navigation.
/// </summary>
internal interface IMinimapDataProvider
{
    /// <summary>
    /// Gets the list of visual lines computed during layout.
    /// </summary>
    List<DocsCanvas.VisualLine> GetVisualLines();

    /// <summary>
    /// Gets the list of Y-positions for each visual line.
    /// </summary>
    List<double> GetLineYPositions();

    /// <summary>
    /// Gets the total height of all content.
    /// </summary>
    double GetTotalContentHeight();

    /// <summary>
    /// Gets the height of the viewport (ActualHeight).
    /// </summary>
    double GetViewportHeight();

    /// <summary>
    /// Gets the list of visual maps for blocks (visual mode only).
    /// </summary>
    List<BlockVisualMap>? GetVisualMaps();
}

/// <summary>
/// Provides minimap data by delegating to a DocsCanvas instance.
/// </summary>
internal class MinimapDataProvider : IMinimapDataProvider
{
    private readonly DocsCanvas _canvas;

    public MinimapDataProvider(DocsCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public List<DocsCanvas.VisualLine> GetVisualLines() => _canvas._visualLines;

    public List<double> GetLineYPositions() => _canvas._lineYPositions;

    public double GetTotalContentHeight() => _canvas.TotalContentHeight;

    public double GetViewportHeight() => _canvas.ActualHeight;

    public List<BlockVisualMap>? GetVisualMaps() => _canvas._visualMaps;
}
