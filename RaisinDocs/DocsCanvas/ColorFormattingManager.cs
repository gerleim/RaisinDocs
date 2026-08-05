namespace RaisinDocs;

/// <summary>
/// Manages color formatting operations for inline and block color tags.
/// Extracts color-related formatting logic from DocsCanvas to reduce its size.
/// Handles insertion of foreground/background color tags and color removal.
/// </summary>
internal class ColorFormattingManager
{
    private readonly IDocumentServices _doc;
    private readonly IParsedContentServices _content;
    private readonly ILayoutDataServices _layout;
    private readonly ICanvasOperations _canvas;
    private readonly IScrollServices _scroll;

    public ColorFormattingManager(
        IDocumentServices doc,
        IParsedContentServices content,
        ILayoutDataServices layout,
        ICanvasOperations canvas,
        IScrollServices scroll)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
    }

    /// <summary>
    /// Inserts an inline foreground color tag around the selection or at cursor.
    /// </summary>
    public void InsertFgColor(string colorName)
    {
        InsertColorWrapper($"<!--@fg:{colorName}-->", "<!--/@fg-->", $"fg:{colorName}");
    }

    /// <summary>
    /// Inserts an inline background color tag around the selection or at cursor.
    /// </summary>
    public void InsertBgColor(string colorName)
    {
        InsertColorWrapper($"<!--@bg:{colorName}-->", "<!--/@bg-->", $"bg:{colorName}");
    }

    /// <summary>
    /// Checks if the selection contains any background color.
    /// </summary>
    public bool SelectionHasBackground()
    {
        _layout.ComputeLayout();
        if (_content.ParsedBlocks == null) return false;
        return BackgroundHelper.SelectionHasBackground(_doc.Document, _content.ParsedBlocks);
    }

    /// <summary>
    /// Checks if the cursor position has background color.
    /// </summary>
    public bool CursorHasBackground()
    {
        _layout.ComputeLayout();
        if (_content.ParsedBlocks == null) return false;
        return BackgroundHelper.CursorHasBackground(_doc.Document, _content.ParsedBlocks);
    }

    /// <summary>
    /// Removes background color at the cursor position.
    /// </summary>
    public void RemoveBackgroundAtCursor()
    {
        _layout.ComputeLayout();
        _canvas.SealAndStopTimer();
        _doc.Document.BeginUndoGroup();
        BackgroundHelper.RemoveBackgroundAtCursor(_doc.Document, _content.ParsedBlocks);
        _doc.Document.SealUndoGroup();
        _layout.InvalidateLayout();
        _scroll.EnsureCursorVisible();
        _canvas.RaiseFormattingChanged();
    }

    /// <summary>
    /// Removes background color from the selected text.
    /// </summary>
    public void RemoveBackgroundFromSelection()
    {
        if (!_doc.Document.HasSelection) return;
        _layout.ComputeLayout();
        _canvas.SealAndStopTimer();
        _doc.Document.BeginUndoGroup();
        BackgroundHelper.RemoveBackgroundFromSelection(_doc.Document, _content.ParsedBlocks);
        _doc.Document.SealUndoGroup();
        _layout.InvalidateLayout();
        _scroll.EnsureCursorVisible();
        _canvas.RaiseFormattingChanged();
    }

    /// <summary>
    /// Internal helper that inserts color wrapper tags (opener and closer).
    /// Handles both inline tags (same block) and block div tags (multiple blocks).
    /// </summary>
    private void InsertColorWrapper(string opener, string closer, string divProperty)
    {
        _canvas.SealAndStopTimer();
        _doc.Document.BeginUndoGroup();

        if (_doc.Document.HasSelection)
        {
            var (sb, so, eb, eo) = _doc.Document.GetOrderedSelection();
            if (sb == eb)
            {
                _doc.Document.InsertTextAt(sb, eo, closer);
                _doc.Document.InsertTextAt(sb, so, opener);
                _doc.Document.CursorBlock = sb;
                _doc.Document.CursorOffset = eo + opener.Length;
                _doc.Document.AnchorBlock = sb;
                _doc.Document.AnchorOffset = _doc.Document.CursorOffset;
            }
            else
            {
                string divOpen = $"<!--@div {divProperty}-->";
                _doc.Document.InsertBlockAt(eb + 1, "<!--/@div-->");
                _doc.Document.InsertBlockAt(sb, divOpen);
                _doc.Document.CursorBlock = eb + 1;
                _doc.Document.CursorOffset = eo;
                _doc.Document.AnchorBlock = _doc.Document.CursorBlock;
                _doc.Document.AnchorOffset = _doc.Document.CursorOffset;
            }
        }
        else
        {
            int block = _doc.Document.CursorBlock;
            int offset = _doc.Document.CursorOffset;
            _doc.Document.InsertTextAt(block, offset, opener + closer);
            _doc.Document.CursorOffset = offset + opener.Length;
            _doc.Document.AnchorBlock = block;
            _doc.Document.AnchorOffset = _doc.Document.CursorOffset;
        }

        _doc.Document.SealUndoGroup();
        _layout.InvalidateLayout();
        _scroll.EnsureCursorVisible();
        _canvas.RaiseFormattingChanged();
    }
}
