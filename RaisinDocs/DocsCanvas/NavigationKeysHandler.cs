using System.Windows.Input;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles advanced keyboard shortcuts for cursor navigation (Ctrl+Home/End/Left/Right).
    /// Encapsulates advanced navigation key dispatch logic from DocsCanvas.Input keyboard handling.
    ///
    /// Supported shortcuts:
    /// - Ctrl+Home: Jump to document start
    /// - Ctrl+End: Jump to document end
    /// - Ctrl+Left: Move cursor to previous word
    /// - Ctrl+Right: Move cursor to next word
    /// </summary>
    internal class NavigationKeysHandler
    {
        private readonly CursorNavigationEngine _navigationEngine;
        private readonly IDocumentServices _doc;

        public NavigationKeysHandler(CursorNavigationEngine navigationEngine, IDocumentServices doc)
        {
            _navigationEngine = navigationEngine ?? throw new ArgumentNullException(nameof(navigationEngine));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Attempts to handle an advanced navigation key press. Returns true if the key was handled.
        /// Checks for Ctrl modifier and dispatches to appropriate handler.
        /// </summary>
        public bool TryHandleNavigationKey(Key key, bool shift, bool ctrl)
        {
            if (!ctrl)
                return false;

            return key switch
            {
                Key.Home => HandleCtrlHome(shift),
                Key.End => HandleCtrlEnd(shift),
                Key.Left => HandleCtrlLeft(shift),
                Key.Right => HandleCtrlRight(shift),
                _ => false,
            };
        }

        /// <summary>
        /// Handles Ctrl+Home to jump to document start.
        /// </summary>
        private bool HandleCtrlHome(bool shift)
        {
            _navigationEngine.HandleHome(shift, ctrl: true);
            return true;
        }

        /// <summary>
        /// Handles Ctrl+End to jump to document end.
        /// </summary>
        private bool HandleCtrlEnd(bool shift)
        {
            _navigationEngine.HandleEnd(shift, ctrl: true);
            return true;
        }

        /// <summary>
        /// Handles Ctrl+Left to move cursor to previous word.
        /// </summary>
        private bool HandleCtrlLeft(bool shift)
        {
            _navigationEngine.HandleLeft(shift, ctrl: true);
            return true;
        }

        /// <summary>
        /// Handles Ctrl+Right to move cursor to next word.
        /// </summary>
        private bool HandleCtrlRight(bool shift)
        {
            _navigationEngine.HandleRight(shift, ctrl: true);
            return true;
        }
    }
}
