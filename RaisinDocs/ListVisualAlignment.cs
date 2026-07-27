namespace RaisinDocs;

internal class ListVisualAlignment
{
    private readonly double _padding;
    private readonly double _listIndent;
    private readonly double _markerSpacing;

    internal ListVisualAlignment(double padding, double listIndent, double markerSpacing = 8)
    {
        _padding = padding;
        _listIndent = listIndent;
        _markerSpacing = markerSpacing;
    }

    internal double CalculateMarkerCenterX(double nestingOffset = 0)
    {
        return _padding + nestingOffset + _listIndent - _listIndent / 2;
    }

    internal double CalculateMarkerXForSize(double markerSize, double nestingOffset = 0)
    {
        double centerX = CalculateMarkerCenterX(nestingOffset);
        return centerX - markerSize / 2;
    }

    internal double CalculateTextStartX(double nestingOffset = 0)
    {
        return _padding + nestingOffset + _listIndent + _markerSpacing;
    }

    internal double CalculateTextStartXForWidth(double elementWidth, double nestingOffset = 0)
    {
        double centerX = CalculateMarkerCenterX(nestingOffset);
        double minTextStart = CalculateTextStartX(nestingOffset);
        double textStartForWidth = centerX + elementWidth / 2 + _markerSpacing;
        return Math.Max(minTextStart, textStartForWidth);
    }

    internal double GetMarkerReservedWidth() => _listIndent;

    internal double GetSpacingAfterMarker() => _markerSpacing;
}
