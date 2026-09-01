namespace RaisinDocs;

/// <summary>
/// Handles list formatting operations for the Enter key, including smart list continuation.
/// Encapsulates list-specific formatting logic from DocsCanvas keyboard input handling.
///
/// Supported operations:
/// - Unordered list item continuation (-, *, +)
/// - Ordered list item continuation with auto-numbering
/// - Task list item continuation ([ ], [x])
/// - Blockquote continuation (>)
/// - Smart hard break removal before continuing lists
/// - Automatic renumbering of ordered lists
///
/// Manages complex logic for list prefix detection, stripping, and continuation,
/// maintaining consistent list formatting across multiple list types.
/// </summary>
internal class ListFormattingHandler
{
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _parsed;
    private readonly HardBreakStyleProvider _hardBreakProvider;

    public ListFormattingHandler(IDocumentServices doc, IParsedContentServices parsed, HardBreakStyleProvider hardBreakProvider)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _parsed = parsed ?? throw new ArgumentNullException(nameof(parsed));
        _hardBreakProvider = hardBreakProvider ?? throw new ArgumentNullException(nameof(hardBreakProvider));
    }

    /// <summary>
    /// Handles the Enter key for list formatting.
    /// Supports Shift+Enter (soft break) and Ctrl+Enter (paragraph break).
    /// For normal Enter, provides smart list continuation, auto-numbering, and content wrapping.
    /// </summary>
    public void HandleEnter(bool shift, bool ctrl)
    {
        _doc.BeginUndoGroup();
        if (_doc.Document.HasSelection) _doc.Document.DeleteSelection();
        if (shift)
        {
            var blockKind = MarkdownParser.ClassifyBlock(_doc.GetBlockText(_doc.Document.CursorBlock));
            bool isHeading = blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6;
            if (!isHeading)
            {
                string marker = _hardBreakProvider.CurrentHardBreak == DocsCanvas.HardBreakStyle.Backslash ? "\\" : "  ";
                string beforeCursor = _doc.GetBlockText(_doc.Document.CursorBlock)[.._doc.Document.CursorOffset];
                if (!beforeCursor.EndsWith(marker))
                    _doc.Document.Paste(marker);
            }
            _doc.Document.InsertParagraphBreak();
        }
        else if (ctrl)
        {
            _doc.Document.InsertParagraphBreak();
        }
        else
        {
            string blockText = _doc.GetBlockText(_doc.Document.CursorBlock);
            var blockKind = MarkdownParser.ClassifyBlock(blockText, out int leadingSpaces);
            if (blockKind == BlockKind.IndentedCodeLine)
            {
                var (chars, _) = MarkdownParser.MeasureLeadingWhitespace(blockText);
                string stripped = chars < blockText.Length ? blockText[chars..] : "";
                var innerKind = MarkdownParser.ClassifyBlock(stripped);
                if (innerKind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                    or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    blockKind = innerKind;
                    leadingSpaces = chars;
                }
            }
            bool isStandalone = (blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6)
                             || MarkdownParser.IsFenceLine(blockText)
                             || blockKind == BlockKind.ThematicBreak;
            StripTrailingHardBreak();

            if (blockKind == BlockKind.OrderedListItem)
            {
                string stripped = leadingSpaces > 0 ? blockText[leadingSpaces..] : blockText;
                int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
                string content = stripped.Substring(prefixLen);
                if (content.Length == 0)
                {
                    _doc.Document.RemoveTextAt(_doc.Document.CursorBlock, 0, blockText.Length);
                    _doc.Document.CursorOffset = 0;
                }
                else
                {
                    string indent = blockText[..leadingSpaces];
                    string number = stripped.Substring(0, prefixLen - 2);
                    char delim = stripped[prefixLen - 2];
                    _doc.Document.InsertParagraphBreak();
                    StripExistingListPrefix();
                    if (int.TryParse(number, out int n))
                    {
                        _doc.Document.Paste(indent + (n + 1).ToString() + delim + " ");
                        RenumberOrderedList(_doc.Document.CursorBlock + 1, n + 2, delim);
                    }
                }
            }
            else if (blockKind is BlockKind.UnorderedListItem
                or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked
                or BlockKind.Blockquote)
            {
                string stripped = leadingSpaces > 0 ? blockText[leadingSpaces..] : blockText;
                string newPrefix;
                int prefixLen;
                if (blockKind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    newPrefix = stripped[..2] + "[ ] ";
                    prefixLen = 6;
                }
                else if (blockKind == BlockKind.Blockquote)
                {
                    newPrefix = "> ";
                    prefixLen = stripped.StartsWith("> ") ? 2 : 1;
                }
                else
                {
                    prefixLen = Math.Min(2, stripped.Length);
                    newPrefix = stripped[..prefixLen];
                }

                if (stripped.Length <= prefixLen || stripped[prefixLen..].Length == 0)
                {
                    _doc.Document.RemoveTextAt(_doc.Document.CursorBlock, 0, blockText.Length);
                    _doc.Document.CursorOffset = 0;
                }
                else
                {
                    string indent = blockText[..leadingSpaces];
                    _doc.Document.InsertParagraphBreak();
                    StripExistingListPrefix();
                    _doc.Document.Paste(indent + newPrefix);
                }
            }
            else
            {
                if (_parsed.IsVisual && blockKind >= BlockKind.Heading1 && blockKind <= BlockKind.Heading6)
                {
                    var headingPrefix = Document.GetBlockPrefix(blockText);
                    if (headingPrefix != null && _doc.Document.CursorOffset <= headingPrefix.Length)
                        _doc.Document.CursorOffset = 0;
                }
                _doc.Document.InsertParagraphBreak();
                if (!isStandalone)
                    _doc.Document.InsertParagraphBreak();
            }
        }
        _doc.Document.CollapseSelection();
        _doc.SealUndoGroup();
    }

    /// <summary>
    /// Removes trailing hard break markers (backslash or trailing spaces) from the current block.
    /// Called before inserting paragraph breaks to prevent duplicate break markers.
    /// </summary>
    private void StripTrailingHardBreak()
    {
        string text = _doc.GetBlockText(_doc.Document.CursorBlock);
        int end = MarkdownParser.GetContentEnd(text);
        if (end > 0 && text[end - 1] == '\\')
        {
            _doc.Document.RemoveTextAt(_doc.Document.CursorBlock, end - 1, 1);
        }
        else if (end >= 2 && text[end - 1] == ' ' && text[end - 2] == ' ')
        {
            int trailStart = end;
            while (trailStart > 0 && text[trailStart - 1] == ' ') trailStart--;
            _doc.Document.RemoveTextAt(_doc.Document.CursorBlock, trailStart, end - trailStart);
        }
    }

    /// <summary>
    /// Removes the list/blockquote prefix from the current block.
    /// Called after inserting a new line to strip the auto-continued prefix, allowing content to be entered.
    /// Handles unordered lists, ordered lists, task lists, and blockquotes.
    /// </summary>
    private void StripExistingListPrefix()
    {
        string text = _doc.GetBlockText(_doc.Document.CursorBlock);
        var kind = MarkdownParser.ClassifyBlock(text, out int leading);
        if (kind == BlockKind.IndentedCodeLine)
        {
            var (chars, _) = MarkdownParser.MeasureLeadingWhitespace(text);
            string inner = chars < text.Length ? text[chars..] : "";
            var innerKind = MarkdownParser.ClassifyBlock(inner);
            if (innerKind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem
                or BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
            {
                kind = innerKind;
                leading = chars;
            }
        }
        string stripped = leading > 0 ? text[leading..] : text;
        int stripLen = kind switch
        {
            BlockKind.UnorderedListItem => leading + 2,
            BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked => leading + 6,
            BlockKind.OrderedListItem => leading + MarkdownParser.GetOrderedListPrefixLength(stripped),
            BlockKind.Blockquote => leading + (stripped.StartsWith("> ") ? 2 : 1),
            _ => 0,
        };
        stripLen = Math.Min(stripLen, text.Length);
        if (stripLen > 0)
            _doc.Document.RemoveTextAt(_doc.Document.CursorBlock, 0, stripLen);
    }

    /// <summary>
    /// Renumbers ordered list items starting from a given block and number.
    /// Walks forward through consecutive ordered list items and updates their numbering.
    /// Stops when a non-list block is encountered.
    /// </summary>
    private void RenumberOrderedList(int startBlock, int nextNumber, char delim)
    {
        for (int i = startBlock; i < _doc.BlockCount; i++)
        {
            string text = _doc.GetBlockText(i);
            var kind = MarkdownParser.ClassifyBlock(text, out int ls);
            if (kind != BlockKind.OrderedListItem) break;
            string stripped = ls > 0 ? text[ls..] : text;
            int oldPl = MarkdownParser.GetOrderedListPrefixLength(stripped);
            if (oldPl == 0) break;
            string newPrefix = nextNumber.ToString() + delim + " ";
            _doc.Document.RemoveTextAt(i, ls, oldPl);
            _doc.Document.InsertTextAt(i, ls, newPrefix);
            nextNumber++;
        }
    }
}

/// <summary>
/// Provider for hard break style setting. Injected into ListFormattingHandler to access the current hard break style.
/// </summary>
internal class HardBreakStyleProvider
{
    private readonly DocsCanvas _canvas;

    public HardBreakStyleProvider(DocsCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public DocsCanvas.HardBreakStyle CurrentHardBreak => _canvas.CurrentHardBreak;
}
