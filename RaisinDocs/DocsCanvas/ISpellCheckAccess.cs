using System.Windows;
using System.Windows.Controls;

namespace RaisinDocs;

/// <summary>
/// Minimal interface providing spell check access to ContextMenuHandler.
/// Abstracts spell check functionality so ContextMenuHandler doesn't depend directly on SpellCheckController.
/// </summary>
internal interface ISpellCheckAccess
{
    /// <summary>Gets whether spell checking is enabled.</summary>
    bool SpellCheckEnabled { get; }

    /// <summary>
    /// Adds spell check menu items (suggestions, add to dictionary) to a context menu.
    /// Returns true if any items were added.
    /// </summary>
    bool AddSpellCheckMenuItems(ContextMenu menu, Point position);
}
