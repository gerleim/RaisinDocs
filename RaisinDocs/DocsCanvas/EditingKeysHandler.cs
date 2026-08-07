using System.Windows.Input;

namespace RaisinDocs;

/// <summary>
/// Handles core editing key operations: Backspace, Delete, Undo, Redo.
/// Encapsulates editing key dispatch logic and undo grouping from DocsCanvas input handling.
///
/// Supported shortcuts:
/// - Backspace: Delete character before cursor or selection
/// - Delete: Delete character after cursor or selection
/// - Ctrl+Z: Undo last action
/// - Ctrl+Y / Ctrl+Shift+Z: Redo last action
///
/// Manages undo grouping to combine consecutive deletes into a single undo step.
/// Depends on IDocumentServices for undo/redo and IEditingServices for mode-specific handling.
/// </summary>
internal class EditingKeysHandler
{
    private readonly IDocumentServices _doc;
    private readonly IEditingServices _editing;

    public EditingKeysHandler(IDocumentServices doc, IEditingServices editing)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _editing = editing ?? throw new ArgumentNullException(nameof(editing));
    }

    /// <summary>
    /// Attempts to handle an editing key press. Returns true if the key was handled.
    /// Checks read-only state and dispatches to appropriate handler.
    /// </summary>
    public bool TryHandleEditingKey(Key key, bool ctrl, bool shift, bool isReadOnly, out bool textChanged)
    {
        textChanged = false;

        if (isReadOnly)
            return false;

        return key switch
        {
            Key.Back => HandleBackspace(shift, out textChanged),
            Key.Delete => HandleDelete(shift, out textChanged),
            Key.Z when ctrl => HandleUndo(out textChanged),
            Key.Y when ctrl => HandleRedo(out textChanged),
            _ => false,
        };
    }

    /// <summary>
    /// Handles Backspace key to delete character before cursor or selection.
    /// Groups consecutive deletes for single undo step.
    /// </summary>
    private bool HandleBackspace(bool shift, out bool textChanged)
    {
        textChanged = false;

        // Group consecutive deletes into single undo step
        if (_editing.LastAction != LastActionKind.Deleting)
        {
            _doc.SealUndoGroup();
            _editing.LastAction = LastActionKind.Deleting;
        }

        _doc.BeginUndoGroup();

        if (_doc.Document.HasSelection)
        {
            // Delete entire selection (including table rect selection if applicable)
            var rect = _editing.TryGetTableRectSelection();
            if (rect.HasValue)
                _editing.ClearTableRectCells(rect.Value);
            else
                _doc.Document.DeleteSelection();
            textChanged = true;
        }
        else if (_editing.IsVisual)
        {
            textChanged = _editing.HandleBackVisual();
        }
        else
        {
            textChanged = _editing.HandleBackSource();
        }

        _editing.ResetUndoSealTimer();
        return true;
    }

    /// <summary>
    /// Handles Delete key to delete character after cursor or selection.
    /// Groups consecutive deletes for single undo step.
    /// </summary>
    private bool HandleDelete(bool shift, out bool textChanged)
    {
        textChanged = false;

        // Group consecutive deletes into single undo step
        if (_editing.LastAction != LastActionKind.Deleting)
        {
            _doc.SealUndoGroup();
            _editing.LastAction = LastActionKind.Deleting;
        }

        _doc.BeginUndoGroup();

        if (_doc.Document.HasSelection)
        {
            // Delete entire selection (including table rect selection if applicable)
            var rect = _editing.TryGetTableRectSelection();
            if (rect.HasValue)
                _editing.ClearTableRectCells(rect.Value);
            else
                _doc.Document.DeleteSelection();
            textChanged = true;
        }
        else if (_editing.IsVisual)
        {
            textChanged = _editing.HandleDeleteVisual();
        }
        else
        {
            textChanged = _editing.HandleDeleteSource();
        }

        _editing.ResetUndoSealTimer();
        return true;
    }

    /// <summary>
    /// Handles Ctrl+Z to undo the last action.
    /// Stops undo grouping timer to finalize the current undo group.
    /// </summary>
    private bool HandleUndo(out bool textChanged)
    {
        textChanged = true;
        _editing.StopUndoSealTimer();
        _doc.Undo();
        _editing.LastAction = LastActionKind.None;
        return true;
    }

    /// <summary>
    /// Handles Ctrl+Y / Ctrl+Shift+Z to redo the last undone action.
    /// Stops undo grouping timer to finalize the current undo group.
    /// </summary>
    private bool HandleRedo(out bool textChanged)
    {
        textChanged = true;
        _editing.StopUndoSealTimer();
        _doc.Redo();
        _editing.LastAction = LastActionKind.None;
        return true;
    }
}
