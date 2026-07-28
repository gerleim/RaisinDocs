namespace RaisinDocs;

internal class ContentBlockAligner
{
    private readonly double _padding;
    private readonly double _blockIndent;
    private readonly double _markerSpacing;

    internal ContentBlockAligner(double padding, double blockIndent, double markerSpacing = 8)
    {
        _padding = padding;
        _blockIndent = blockIndent;
        _markerSpacing = markerSpacing;
    }

    internal double CalculateMarkerCenterX(double nestingOffset = 0)
    {
        return _padding + nestingOffset + _blockIndent - _blockIndent / 2;
    }

    internal double CalculateMarkerXForSize(double markerSize, double nestingOffset = 0)
    {
        double centerX = CalculateMarkerCenterX(nestingOffset);
        return centerX - markerSize / 2;
    }

    internal double CalculateContentStartX(double nestingOffset = 0)
    {
        return _padding + nestingOffset + _blockIndent + _markerSpacing;
    }

    internal double CalculateContentStartXForWidth(double elementWidth, double nestingOffset = 0)
    {
        double centerX = CalculateMarkerCenterX(nestingOffset);
        double minTextStart = CalculateContentStartX(nestingOffset);
        double textStartForWidth = centerX + elementWidth / 2 + _markerSpacing;
        return Math.Max(minTextStart, textStartForWidth);
    }

    internal double GetBlockquoteBarX(double nestingOffset = 0)
    {
        return _padding + nestingOffset;
    }

    internal double GetBlockquoteContentIndentX(double nestingOffset = 0)
    {
        return CalculateContentStartX(nestingOffset);
    }

    internal double GetBlockIndentWidth() => _blockIndent;

    internal double GetSpacingAfterMarker() => _markerSpacing;
}
