using System.Collections.Generic;

namespace RaisinDocs;

/// <summary>
/// Represents a markdown block element (header, paragraph, list, etc.)
/// Markdown is fundamentally a block-oriented language.
/// Each block element represents a top-level structural unit.
/// </summary>
internal class BlockElement
{
    /// <summary>Type of block (determines conversion strategy). Uses MarkdownParser.BlockKind.</summary>
    public BlockKind Kind { get; set; }

    /// <summary>Inline content (text, formatting, colors, breaks)</summary>
    public List<InlineContent> Content { get; set; } = new();

    /// <summary>Nested blocks (for lists, blockquotes, etc.)</summary>
    public List<BlockElement>? NestedBlocks { get; set; }

    /// <summary>Table rows and columns, when this block is a table.</summary>
    public TableBlockData? TableData { get; set; }

    /// <summary>
    /// Helper to extract heading level from BlockKind.
    /// Returns 1-6 for Heading1-Heading6, null for non-headings.
    /// </summary>
    public int? GetHeadingLevel() => Kind switch
    {
        BlockKind.Heading1 => 1,
        BlockKind.Heading2 => 2,
        BlockKind.Heading3 => 3,
        BlockKind.Heading4 => 4,
        BlockKind.Heading5 => 5,
        BlockKind.Heading6 => 6,
        _ => null,
    };
}
