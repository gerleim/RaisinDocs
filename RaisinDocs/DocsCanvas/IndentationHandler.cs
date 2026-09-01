namespace RaisinDocs;

/// <summary>
/// Handles Tab/Shift+Tab indentation for text and list items.
/// Encapsulates indentation logic from DocsCanvas keyboard input handling.
///
/// Supported operations:
/// - Tab indentation of selections (indent all lines)
/// - Shift+Tab dedentation of selections (outdent all lines)
/// - Tab indentation of single line with context-appropriate indent size
/// - Shift+Tab dedentation of single line
/// - Context-aware indent calculation based on block kind (list items, blockquotes, code blocks)
///
/// Manages different indent sizes for different block types:
/// - List items (unordered, ordered, task lists): 2 spaces
/// - Blockquotes: 2 spaces
/// - Regular blocks: 4 spaces
/// </summary>
internal class IndentationHandler
{
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _parsed;

    public IndentationHandler(IDocumentServices doc, IParsedContentServices parsed)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _parsed = parsed ?? throw new ArgumentNullException(nameof(parsed));
    }

    /// <summary>
    /// Handles the Tab key for indentation.
    /// When selection exists, indents/outdents all selected lines.
    /// When no selection, indents single line with context-appropriate size or inserts spaces.
    /// </summary>
    public void HandleTabIndent(bool shift)
    {
        if (_doc.Document.HasSelection)
        {
            var (sb, _, eb, _) = _doc.Document.GetOrderedSelection();
            if (shift)
                _doc.Document.OutdentLines(sb, eb, 4);
            else
                _doc.Document.IndentLines(sb, eb, 4);
            _doc.Document.AnchorBlock = sb;
            _doc.Document.AnchorOffset = 0;
            _doc.Document.CursorBlock = eb;
            _doc.Document.CursorOffset = _doc.GetBlockLength(eb);
        }
        else
        {
            int indentStep = GetIndentStep(_doc.Document.CursorBlock);
            if (indentStep != 4)
            {
                if (shift)
                    _doc.Document.OutdentLines(_doc.Document.CursorBlock, _doc.Document.CursorBlock, indentStep);
                else
                    _doc.Document.IndentLines(_doc.Document.CursorBlock, _doc.Document.CursorBlock, indentStep);
            }
            else
            {
                if (shift)
                {
                    _doc.Document.OutdentLines(_doc.Document.CursorBlock, _doc.Document.CursorBlock, 4);
                }
                else
                {
                    _doc.Document.InsertTextAt(_doc.Document.CursorBlock, _doc.Document.CursorOffset, "    ");
                    _doc.Document.CursorOffset += 4;
                }
            }
            _doc.Document.CollapseSelection();
        }
    }

    /// <summary>
    /// Returns the context-appropriate indent size for a given block.
    /// Different block types have different indent sizes:
    /// - List items and blockquotes: 2 spaces
    /// - Regular blocks: 4 spaces
    /// </summary>
    private int GetIndentStep(int blockIndex)
    {
        if (_parsed.ParsedBlocks == null) return 4;
        var kind = _parsed.ParsedBlocks[blockIndex].Kind;
        var text = _doc.GetBlockText(blockIndex);
        if (kind == BlockKind.IndentedCodeLine)
            kind = MarkdownParser.ClassifyBlock(text.TrimStart());
        return kind switch
        {
            BlockKind.UnorderedListItem => 2,
            BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked => 2,
            BlockKind.OrderedListItem => MarkdownParser.GetOrderedListPrefixLength(text.TrimStart()),
            BlockKind.Blockquote => 2,
            _ => 4,
        };
    }
}
