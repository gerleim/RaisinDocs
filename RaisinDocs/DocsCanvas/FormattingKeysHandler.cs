using System.Windows.Input;

namespace RaisinDocs;

/// <summary>
/// Handles keyboard shortcuts for formatting operations (Bold, Italic, Link insertion).
/// Encapsulates formatting key dispatch logic from DocsCanvas.Input keyboard handling.
///
/// Supported shortcuts:
/// - Ctrl+B: Toggle bold
/// - Ctrl+I: Toggle italic
/// - Ctrl+K: Insert/edit link
/// - Ctrl+`: Toggle code span (future)
/// - Ctrl+~: Toggle strikethrough (future)
/// </summary>
internal class FormattingKeysHandler
{
    private readonly DocsCanvas _canvas;
    private readonly ICanvasOperations _ops;
    private readonly IDocumentServices _doc;

    public FormattingKeysHandler(DocsCanvas canvas, ICanvasOperations ops, IDocumentServices doc)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    /// <summary>
    /// Attempts to handle a formatting key press. Returns true if the key was handled.
    /// Checks for Ctrl modifier and verifies not in fenced code context.
    /// </summary>
    public bool TryHandleFormattingKey(Key key, bool ctrl, bool isReadOnly)
    {
        if (!ctrl || isReadOnly)
            return false;

        return key switch
        {
            Key.B => HandleBold(),
            Key.I => HandleItalic(),
            Key.K => HandleInsertLink(),
            // Future: Key.OemTilde or OemPlus => HandleStrikethrough() (Ctrl+~)
            // Future: Key.D => HandleCodeSpan() (Ctrl+`)
            _ => false,
        };
    }

    /// <summary>
    /// Handles Ctrl+B to toggle bold formatting on selection or insert bold markers at cursor.
    /// </summary>
    private bool HandleBold()
    {
        if (_canvas.IsInFencedCode)
            return false;

        _canvas.ToggleBold();
        return true;
    }

    /// <summary>
    /// Handles Ctrl+I to toggle italic formatting on selection or insert italic markers at cursor.
    /// </summary>
    private bool HandleItalic()
    {
        if (_canvas.IsInFencedCode)
            return false;

        _canvas.ToggleItalic();
        return true;
    }

    /// <summary>
    /// Handles Ctrl+K to show link insertion/editing popup.
    /// Shows existing link or prompts for new link URL if no link at cursor.
    /// </summary>
    private bool HandleInsertLink()
    {
        if (_canvas.IsInFencedCode)
            return false;

        _canvas.InsertLink();
        return true;
    }

    // Future handlers for additional formatting shortcuts:
    //
    // private bool HandleCodeSpan()
    // {
    //     if (_canvas.IsInFencedCode) return false;
    //     _canvas.ToggleCodeSpan();
    //     return true;
    // }
    //
    // private bool HandleStrikethrough()
    // {
    //     if (_canvas.IsInFencedCode) return false;
    //     _canvas.ToggleStrikethrough();
    //     return true;
    // }
}
