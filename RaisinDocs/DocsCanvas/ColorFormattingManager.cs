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
        ((DocsCanvas)_services).ComputeLayout();
        ((DocsCanvas)_services).SealAndStopTimer();
        ((DocsCanvas)_services)._doc.BeginUndoGroup();
        BackgroundHelper.RemoveBackgroundAtCursor(((DocsCanvas)_services)._doc, ((DocsCanvas)_services)._parsedBlocks);
        ((DocsCanvas)_services)._doc.SealUndoGroup();
        ((DocsCanvas)_services).InvalidateLayout();
        ((DocsCanvas)_services).EnsureCursorVisible();
        ((DocsCanvas)_services).RaiseFormattingChanged();
    }

    /// <summary>
    /// Removes background color from the selected text.
    /// </summary>
    public void RemoveBackgroundFromSelection()
    {
        if (!((DocsCanvas)_services)._doc.HasSelection) return;
        ((DocsCanvas)_services).ComputeLayout();
        ((DocsCanvas)_services).SealAndStopTimer();
        ((DocsCanvas)_services)._doc.BeginUndoGroup();
        BackgroundHelper.RemoveBackgroundFromSelection(((DocsCanvas)_services)._doc, ((DocsCanvas)_services)._parsedBlocks);
        ((DocsCanvas)_services)._doc.SealUndoGroup();
        ((DocsCanvas)_services).InvalidateLayout();
        ((DocsCanvas)_services).EnsureCursorVisible();
        ((DocsCanvas)_services).RaiseFormattingChanged();
    }

    /// <summary>
    /// Internal helper that inserts color wrapper tags (opener and closer).
    /// Handles both inline tags (same block) and block div tags (multiple blocks).
    /// </summary>
    private void InsertColorWrapper(string opener, string closer, string divProperty)
    {
        ((DocsCanvas)_services).SealAndStopTimer();
        ((DocsCanvas)_services)._doc.BeginUndoGroup();

        if (((DocsCanvas)_services)._doc.HasSelection)
        {
            var (sb, so, eb, eo) = ((DocsCanvas)_services)._doc.GetOrderedSelection();
            if (sb == eb)
            {
                ((DocsCanvas)_services)._doc.InsertTextAt(sb, eo, closer);
                ((DocsCanvas)_services)._doc.InsertTextAt(sb, so, opener);
                ((DocsCanvas)_services)._doc.CursorBlock = sb;
                ((DocsCanvas)_services)._doc.CursorOffset = eo + opener.Length;
                ((DocsCanvas)_services)._doc.AnchorBlock = sb;
                ((DocsCanvas)_services)._doc.AnchorOffset = ((DocsCanvas)_services)._doc.CursorOffset;
            }
            else
            {
                string divOpen = $"<!--@div {divProperty}-->";
                ((DocsCanvas)_services)._doc.InsertBlockAt(eb + 1, "<!--/@div-->");
                ((DocsCanvas)_services)._doc.InsertBlockAt(sb, divOpen);
                ((DocsCanvas)_services)._doc.CursorBlock = eb + 1;
                ((DocsCanvas)_services)._doc.CursorOffset = eo;
                ((DocsCanvas)_services)._doc.AnchorBlock = ((DocsCanvas)_services)._doc.CursorBlock;
                ((DocsCanvas)_services)._doc.AnchorOffset = ((DocsCanvas)_services)._doc.CursorOffset;
            }
        }
        else
        {
            int block = ((DocsCanvas)_services)._doc.CursorBlock;
            int offset = ((DocsCanvas)_services)._doc.CursorOffset;
            ((DocsCanvas)_services)._doc.InsertTextAt(block, offset, opener + closer);
            ((DocsCanvas)_services)._doc.CursorOffset = offset + opener.Length;
            ((DocsCanvas)_services)._doc.AnchorBlock = block;
            ((DocsCanvas)_services)._doc.AnchorOffset = ((DocsCanvas)_services)._doc.CursorOffset;
        }

        ((DocsCanvas)_services)._doc.SealUndoGroup();
        ((DocsCanvas)_services).InvalidateLayout();
        ((DocsCanvas)_services).EnsureCursorVisible();
        ((DocsCanvas)_services).RaiseFormattingChanged();
    }
}
