using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RaisinDocs;

/// <summary>
/// Handles right-click context menu display and operations.
/// Encapsulates context menu building, spell check integration, and menu item styling.
/// </summary>
internal class ContextMenuHandler
{
    private readonly DocsCanvas _canvas;
    private readonly IDocumentServices _doc;
    private readonly ISpellCheckAccess _spellCheck;

    private Style? _contextMenuStyle;
    private Style? _menuItemStyle;

    public ContextMenuHandler(DocsCanvas canvas, IDocumentServices doc, ISpellCheckAccess spellCheck)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _spellCheck = spellCheck ?? throw new ArgumentNullException(nameof(spellCheck));
    }

    /// <summary>
    /// Shows a context menu at the specified position with appropriate menu items
    /// based on cursor state, selection, and formatting context.
    /// </summary>
    public void ShowContextMenu(Point position)
    {
        var menu = new ContextMenu();
        ApplyContextMenuStyle(menu);

        bool selectionIsMultiWord = _doc.Document.HasSelection
            && _doc.Document.GetSelectedText().AsSpan().IndexOfAny(' ', '\t') >= 0;

        if (_spellCheck.SpellCheckEnabled && !selectionIsMultiWord)
            _spellCheck.AddSpellCheckMenuItems(menu, position);

        bool hasSelection = _doc.Document.HasSelection;
        bool inCode = _canvas.IsInFencedCode;

        // Clipboard operations
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        if (!_canvas.IsReadOnly)
        {
            var cut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X", IsEnabled = hasSelection };
            ApplyMenuItemStyle(cut);
            cut.Click += (_, _) => { _canvas.PerformCut(); _canvas.Focus(); };
            menu.Items.Add(cut);
        }

        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = hasSelection };
        ApplyMenuItemStyle(copy);
        copy.Click += (_, _) => { _canvas.PerformCopy(); _canvas.Focus(); };
        menu.Items.Add(copy);

        if (!_canvas.IsReadOnly)
        {
            var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = Clipboard.ContainsText() };
            ApplyMenuItemStyle(paste);
            paste.Click += (_, _) => { _canvas.PerformPaste(); _canvas.Focus(); };
            menu.Items.Add(paste);
        }

        var selectAll = new MenuItem { Header = "Select all", InputGestureText = "Ctrl+A" };
        ApplyMenuItemStyle(selectAll);
        selectAll.Click += (_, _) => { _canvas.PerformSelectAll(); _canvas.Focus(); };
        menu.Items.Add(selectAll);

        // Inline formatting (only when selection exists and not in code block)
        if (hasSelection && !_canvas.IsReadOnly && !inCode)
        {
            menu.Items.Add(new Separator());

            var bold = new MenuItem { Header = "Bold", InputGestureText = "Ctrl+B", IsChecked = _canvas.SelectionIsBold };
            ApplyMenuItemStyle(bold);
            bold.Click += (_, _) => { _canvas.ToggleBold(); _canvas.Focus(); };
            menu.Items.Add(bold);

            var italic = new MenuItem { Header = "Italic", InputGestureText = "Ctrl+I", IsChecked = _canvas.SelectionIsItalic };
            ApplyMenuItemStyle(italic);
            italic.Click += (_, _) => { _canvas.ToggleItalic(); _canvas.Focus(); };
            menu.Items.Add(italic);

            var strikethrough = new MenuItem { Header = "Strikethrough", IsChecked = _canvas.SelectionIsStrikethrough };
            ApplyMenuItemStyle(strikethrough);
            strikethrough.Click += (_, _) => { _canvas.ToggleStrikethrough(); _canvas.Focus(); };
            menu.Items.Add(strikethrough);

            var code = new MenuItem { Header = "Code", IsChecked = _canvas.SelectionIsCode };
            ApplyMenuItemStyle(code);
            code.Click += (_, _) => { _canvas.ToggleCodeSpan(); _canvas.Focus(); };
            menu.Items.Add(code);
        }

        // Reformat
        if (!_canvas.IsReadOnly)
        {
            bool canReformat = hasSelection ? _canvas.CanReformat : _canvas.CanReformatAll;
            menu.Items.Add(new Separator());
            var reformat = new MenuItem
            {
                Header = hasSelection ? "Reformat" : "Reformat all",
                IsEnabled = canReformat
            };
            ApplyMenuItemStyle(reformat);
            reformat.Click += (_, _) =>
            {
                if (_doc.Document.HasSelection)
                    _canvas.Reflow();
                else
                    _canvas.ReflowAll();
                _canvas.Focus();
            };
            menu.Items.Add(reformat);
        }

        // Clear background
        bool hasBg = hasSelection ? _canvas.SelectionHasBackground() : _canvas.CursorHasBackground();
        if (hasBg)
        {
            menu.Items.Add(new Separator());
            var clearBackground = new MenuItem { Header = "Clear background" };
            ApplyMenuItemStyle(clearBackground);
            clearBackground.Click += (_, _) =>
            {
                if (_doc.Document.HasSelection)
                    _canvas.RemoveBackgroundFromSelection();
                else
                    _canvas.RemoveBackgroundAtCursor();
                _canvas.Focus();
            };
            menu.Items.Add(clearBackground);
        }

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
        menu.PlacementTarget = _canvas;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Applies theme-aware styling to the context menu.
    /// </summary>
    private void ApplyContextMenuStyle(ContextMenu menu)
    {
        _contextMenuStyle ??= _canvas.TryFindResource("DarkContextMenu") as Style;
        if (_contextMenuStyle != null)
            menu.Style = _contextMenuStyle;
    }

    /// <summary>
    /// Applies theme-aware styling to a menu item.
    /// </summary>
    internal void ApplyMenuItemStyle(MenuItem item)
    {
        _menuItemStyle ??= _canvas.TryFindResource("DarkContextMenuItem") as Style;
        if (_menuItemStyle != null)
            item.Style = _menuItemStyle;
    }
}
