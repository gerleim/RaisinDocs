namespace RaisinDocs;

/// <summary>
/// Inline content within a block.
/// Handles text, formatting (bold, italic, colors), and break markers.
/// </summary>
internal class InlineContent
{
    /// <summary>The actual text content</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Formatting applied to this segment</summary>
    public InlineFormat Format { get; set; } = new();

    /// <summary>Does this segment end with a hard break? (&lt;br&gt; tag)</summary>
    /// <remarks>Only meaningful within paragraphs and lists. Signals need for line break after this segment.</remarks>
    public bool FollowedByHardBreak { get; set; }

    /// <summary>If true, content was marked as trailing whitespace that can be normalized</summary>
    public bool IsTrailingWhitespace { get; set; }
}

/// <summary>Inline formatting (colors, bold, italic, code)</summary>
internal class InlineFormat
{
    /// <summary>Foreground color (text color)</summary>
    public RgbColor? ForegroundColor { get; set; }

    /// <summary>Background color</summary>
    public RgbColor? BackgroundColor { get; set; }

    /// <summary>Bold (strong) emphasis</summary>
    public bool Bold { get; set; }

    /// <summary>Italic (em) emphasis</summary>
    public bool Italic { get; set; }

    /// <summary>Inline code format</summary>
    public bool Code { get; set; }

    /// <summary>Check if any formatting is applied</summary>
    public bool IsEmpty => ForegroundColor == null && BackgroundColor == null && !Bold && !Italic && !Code;
}

/// <summary>Settings that affect markdown output</summary>
/// <remarks>Uses enums from DocsCanvas for consistency</remarks>
internal class MarkdownOutputSettings
{
    /// <summary>How to represent hard line breaks (&lt;br&gt; tags)</summary>
    /// <remarks>Uses DocsCanvas.HardBreakStyle enum</remarks>
    public DocsCanvas.HardBreakStyle HardBreak { get; set; } = DocsCanvas.HardBreakStyle.Backslash;

    /// <summary>How to handle soft breaks (newlines in HTML)</summary>
    /// <remarks>Uses DocsCanvas.SoftBreakMode enum</remarks>
    public DocsCanvas.SoftBreakMode SoftBreak { get; set; } = DocsCanvas.SoftBreakMode.Relaxed;

    /// <summary>Preserve inline color tags in output (always true for clipboard paste)</summary>
    public bool PreserveColors { get; set; } = true;
}
