using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Raisin.WPF.Base;

namespace RaisinDocs.Editor;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".txt"
    };

    private const string FileFilter =
        "Markdown files|*.md;*.markdown;*.mdown;*.mkd;*.mkdn|Text files|*.txt|All files|*.*";

    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "editor-session.json");

    private readonly SessionStore _sessionStore = new(SessionPath);
    private readonly List<DocumentTab> _tabs = [];

    private DocsEditorState _editorState = new()
    {
        Theme = DocsCanvas.EditorTheme.DarkBlue,
        ShowMinimap = true,
    };

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                TryOpenFileFromPath(args[1]);
                return;
            }

            RestoreSession();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
    }

    private DocumentTab? ActiveTab =>
        TabControl.SelectedItem is TabItem item
            ? _tabs.Find(t => t.TabItem == item)
            : null;

    private DocumentTab AddTab(string? filePath = null, string text = "")
    {
        var editor = new DocsEditor();
        editor.ApplyState(ActiveTab?.Editor.GetState() ?? _editorState);

        editor.SetText(text);
        if (filePath != null)
            editor.DocumentBasePath = Path.GetDirectoryName(filePath)!;
        editor.MarkClean();

        var headerText = new TextBlock
        {
            Text = filePath != null ? Path.GetFileName(filePath) : "Untitled",
            VerticalAlignment = VerticalAlignment.Center,
        };

        var closeButton = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(headerText);
        header.Children.Add(closeButton);

        var tabItem = new TabItem
        {
            Header = header,
            Content = editor,
        };

        var tab = new DocumentTab(tabItem, editor, headerText) { FilePath = filePath };

        closeButton.Click += (_, _) => CloseTab(tab);
        editor.IsDirtyChanged += (_, _) =>
        {
            UpdateTabHeader(tab);
            if (tab == ActiveTab)
                UpdateTitle();
        };

        _tabs.Add(tab);
        TabControl.Items.Add(tabItem);
        TabControl.SelectedItem = tabItem;

        return tab;
    }

    private void UpdateTitle()
    {
        var tab = ActiveTab;
        if (tab == null)
        {
            Title = "RaisinDocs Editor";
            return;
        }
        var name = tab.FilePath != null ? Path.GetFileName(tab.FilePath) : "Untitled";
        var dirty = tab.Editor.IsDirty ? " *" : "";
        Title = $"{name}{dirty} — RaisinDocs Editor";
    }

    private static void UpdateTabHeader(DocumentTab tab)
    {
        var name = tab.FilePath != null ? Path.GetFileName(tab.FilePath) : "Untitled";
        var dirty = tab.Editor.IsDirty ? " *" : "";
        tab.HeaderText.Text = $"{name}{dirty}";
    }

    private void New_Click(object sender, RoutedEventArgs e) => AddTab();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        if (dlg.ShowDialog(this) != true) return;

        var existing = _tabs.Find(t =>
            string.Equals(t.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            TabControl.SelectedItem = existing.TabItem;
            return;
        }

        OpenFileInTab(dlg.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveTab;
        if (tab == null) return;

        if (tab.FilePath != null)
            SaveToFile(tab, tab.FilePath);
        else
            SaveAs_Click(sender, e);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var tab = ActiveTab;
        if (tab == null) return;

        var dlg = new SaveFileDialog { Filter = FileFilter };
        if (tab.FilePath != null)
        {
            dlg.InitialDirectory = Path.GetDirectoryName(tab.FilePath)!;
            dlg.FileName = Path.GetFileName(tab.FilePath);
        }
        if (dlg.ShowDialog(this) != true) return;
        SaveToFile(tab, dlg.FileName);
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { } tab)
            CloseTab(tab);
    }

    private void Undo_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformUndo();

    private void Redo_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformRedo();

    private void Cut_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformCut();

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformCopy();

    private void Paste_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformPaste();

    private void SelectAll_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformSelectAll();

    private void Find_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformFind();

    private void FindReplace_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.PerformFindReplace();

    private void View_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var editor = ActiveTab?.Editor;
        TocMenuItem.IsChecked = editor?.ShowToc ?? false;
        MinimapMenuItem.IsChecked = editor?.ShowMinimap ?? false;
    }

    private void Toc_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.ToggleToc();

    private void Minimap_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.ToggleMinimap();

    private void CloseTab(DocumentTab tab)
    {
        if (tab.Editor.IsDirty)
        {
            TabControl.SelectedItem = tab.TabItem;
            if (!ConfirmDiscard(tab)) return;
        }

        _tabs.Remove(tab);
        TabControl.Items.Remove(tab.TabItem);

        if (_tabs.Count == 0)
            AddTab();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateTitle();

    protected override void OnClosing(CancelEventArgs e)
    {
        foreach (var tab in _tabs)
        {
            if (!tab.Editor.IsDirty) continue;
            TabControl.SelectedItem = tab.TabItem;
            if (!ConfirmDiscard(tab))
            {
                e.Cancel = true;
                return;
            }
        }
        SaveSession();
        base.OnClosing(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] files
            && files.Any(f => AcceptedExtensions.Contains(Path.GetExtension(f))))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var file in files)
        {
            if (AcceptedExtensions.Contains(Path.GetExtension(file)))
                TryOpenFileFromPath(file);
        }
    }

    private void SaveSession()
    {
        var state = new SessionState
        {
            OpenFiles = _tabs
                .Where(t => t.FilePath != null)
                .Select(t => t.FilePath!)
                .ToList(),
            ActiveTabIndex = ActiveTab != null ? _tabs.IndexOf(ActiveTab) : 0,
        };

        if (ActiveTab != null)
            state.EditorState = ActiveTab.Editor.GetState();

        _sessionStore.Save(state);
    }

    private void RestoreSession()
    {
        var session = _sessionStore.State;

        if (session.EditorState != null)
            _editorState = session.EditorState;

        foreach (var path in session.OpenFiles)
        {
            if (File.Exists(path))
                AddTab(path, File.ReadAllText(path));
        }

        if (_tabs.Count > 0 && session.ActiveTabIndex >= 0 && session.ActiveTabIndex < _tabs.Count)
            TabControl.SelectedItem = _tabs[session.ActiveTabIndex].TabItem;

        if (_tabs.Count == 0)
            AddTab();
    }

    private void TryOpenFileFromPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            MessageBox.Show(this, $"File not found:\n{fullPath}", "RaisinDocs Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            AddTab();
            return;
        }
        try
        {
            OpenFileInTab(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open file:\n{ex.Message}", "RaisinDocs Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            AddTab();
        }
    }

    private void OpenFileInTab(string path)
    {
        if (_tabs.Count == 1 && _tabs[0].FilePath == null && !_tabs[0].Editor.IsDirty)
        {
            var tab = _tabs[0];
            tab.FilePath = path;
            tab.Editor.DocumentBasePath = Path.GetDirectoryName(path)!;
            tab.Editor.SetText(File.ReadAllText(path));
            tab.Editor.MarkClean();
            UpdateTabHeader(tab);
            UpdateTitle();
            return;
        }

        AddTab(path, File.ReadAllText(path));
    }

    private void SaveToFile(DocumentTab tab, string path)
    {
        tab.FilePath = path;
        tab.Editor.DocumentBasePath = Path.GetDirectoryName(path)!;
        File.WriteAllText(path, tab.Editor.GetText());
        tab.Editor.MarkClean();
        UpdateTabHeader(tab);
        UpdateTitle();
    }

    private bool ConfirmDiscard(DocumentTab tab)
    {
        var name = tab.FilePath != null ? Path.GetFileName(tab.FilePath) : "Untitled";
        var result = MessageBox.Show(this,
            $"'{name}' has unsaved changes. Save before closing?",
            "RaisinDocs Editor",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            Save_Click(this, new RoutedEventArgs());
            return !tab.Editor.IsDirty;
        }
        return true;
    }

    private class DocumentTab(TabItem tabItem, DocsEditor editor, TextBlock headerText)
    {
        public TabItem TabItem { get; } = tabItem;
        public DocsEditor Editor { get; } = editor;
        public TextBlock HeaderText { get; } = headerText;
        public string? FilePath { get; set; }
    }
}
