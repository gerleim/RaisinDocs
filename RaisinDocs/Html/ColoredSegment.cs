namespace RaisinDocs;

/// <summary>
/// Represents a segment of text with associated formatting (colors, bold, italic).
/// Used internally during HTML to markdown conversion to track styling information.
/// </summary>
internal readonly record struct ColoredSegment(
    string Text,
    RgbColor? Foreground,
    RgbColor? Background,
    bool Bold,
    bool Italic);
