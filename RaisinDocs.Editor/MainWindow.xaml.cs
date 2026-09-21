using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private const int MaxRecentFiles = 10;

    private readonly SessionStore _sessionStore = new(SessionPath);
    private readonly List<DocumentTab> _tabs = [];
    private readonly List<string> _recentFiles = [];

    /// <summary>
    /// Started with a file to open, so the saved tab list is not this window's to overwrite.
    /// </summary>
    /// <remarks>
    /// Without it a launch with a path skipped the session entirely, then saved on close: the
    /// settings went back to defaults, and the tab list and recent files were replaced by the
    /// one file that had been opened.
    /// </remarks>
    private bool _openedFromPath;

    /// <summary>The edit mode the settings held before a --visual or --source override.</summary>
    private DocsCanvas.EditMode _savedEditMode;

    /// <summary>What the settings held before a --zoom, --toc or --minimap override.</summary>
    private double _savedZoom;
    private bool _savedToc, _savedMinimap;

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
            // Skip switches, so a file path can be given alongside one - App has already
            // read them.
            string? path = Environment.GetCommandLineArgs()
                .Skip(1)
                .FirstOrDefault(a => !a.StartsWith('-'));

            // Settings and recent files are preferences and come back however the editor is
            // started. The open tabs are a session: a file on the command line opens that file
            // instead of resuming it, and leaves the saved tab list for the next plain start.
            RestoreSettings();

            _savedEditMode = _editorState.EditMode;
            if (App.EditModeOverride is { } mode)
                _editorState.EditMode = mode;

            _savedZoom = _editorState.ZoomLevel;
            _savedToc = _editorState.ShowToc;
            _savedMinimap = _editorState.ShowMinimap;
            if (App.ZoomOverride is { } zoom) _editorState.ZoomLevel = zoom;
            if (App.TocOverride is { } toc) _editorState.ShowToc = toc;
            if (App.MinimapOverride is { } minimap) _editorState.ShowMinimap = minimap;

            if (path is not null)
            {
                _openedFromPath = true;
                TryOpenFileFromPath(path);
                return;
            }

            RestoreOpenFiles();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        var ctrlShift = (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;

        switch (e.Key)
        {
            case Key.N when ctrl:
                New_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.O when ctrl:
                Open_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S when ctrl && !ctrlShift:
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S when ctrlShift:
                SaveAs_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.P when ctrl:
                Print_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.W when ctrl:
                CloseTab_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Z when ctrl:
                Undo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Y when ctrl:
                Redo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F when ctrl:
                Find_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.H when ctrl:
                FindReplace_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.T when ctrl:
                Toc_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.M when ctrl:
                ActiveTab?.Editor.Canvas.ToggleEditMode();
                e.Handled = true;
                break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                ActiveTab?.Editor.Canvas.ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                ActiveTab?.Editor.Canvas.ZoomOut();
                e.Handled = true;
                break;
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                ActiveTab?.Editor.Canvas.ZoomReset();
                e.Handled = true;
                break;
            case Key.PageUp:
                if (ActiveTab?.Editor.Canvas.IsFocused != true)
                {
                    ActiveTab?.Editor.Canvas.PageUpScroll();
                    e.Handled = true;
                }
                break;
            case Key.PageDown:
                if (ActiveTab?.Editor.Canvas.IsFocused != true)
                {
                    ActiveTab?.Editor.Canvas.PageDownScroll();
                    e.Handled = true;
                }
                break;
        }

        if (!e.Handled)
            base.OnPreviewKeyDown(e);
    }

    private DocumentTab? ActiveTab =>
        TabControl.SelectedItem is TabItem item
            ? _tabs.Find(t => t.TabItem == item)
            : null;

    private DocumentTab AddTab(string? filePath = null, string text = "")
    {
        var editor = new DocsEditor();
        editor.Canvas.Logger = App.Logger;
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
        editor.Canvas.ContentChanged += (_, _) =>
        {
            if (tab == ActiveTab)
                UpdateTitle();
        };
        editor.IsDirtyChanged += (_, _) =>
        {
            UpdateTabHeader(tab);
            if (tab == ActiveTab)
                UpdateTitle();
        };

        _tabs.Add(tab);
        TabControl.Items.Add(tabItem);
        TabControl.SelectedItem = tabItem;

        tab.SetupFileWatcher(this);

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
        var blockCount = tab.Editor.Canvas.BlockCount;
        Title = $"{name}{dirty} — RaisinDocs Editor [Blocks: {blockCount}]";
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

    private void Print_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Print();

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
        PageBreaksMenuItem.IsChecked = editor?.Canvas.ShowPageBreaks ?? false;
        SpellCheckMenuItem.IsChecked = editor?.Canvas.SpellCheckEnabled ?? false;
    }

    private void Toc_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.ToggleToc();

    private void Minimap_Click(object sender, RoutedEventArgs e) =>
        ActiveTab?.Editor.Canvas.ToggleMinimap();

    private void PageBreaks_Click(object sender, RoutedEventArgs e)
    {
        var canvas = ActiveTab?.Editor.Canvas;
        if (canvas != null)
            canvas.SetShowPageBreaks(!canvas.ShowPageBreaks);
    }

    private void SpellCheck_Click(object sender, RoutedEventArgs e)
    {
        var canvas = ActiveTab?.Editor.Canvas;
        if (canvas != null)
            canvas.SetSpellCheckEnabled(!canvas.SpellCheckEnabled);
    }

    private void Tools_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var folder = ActiveTab?.Editor.Canvas.ProjectFolder;
        ProjectFolderMenuItem.InputGestureText = folder ?? "";
        ProjectDictionaryMenuItem.IsEnabled = folder is not null;
    }

    private void ProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var canvas = ActiveTab?.Editor.Canvas;
        if (canvas is null) return;

        var dlg = new OpenFolderDialog
        {
            Title = "Set Project Folder",
        };
        if (canvas.ProjectFolder is not null)
            dlg.InitialDirectory = canvas.ProjectFolder;
        else if (canvas.DocumentBasePath is not null)
            dlg.InitialDirectory = canvas.DocumentBasePath;

        if (dlg.ShowDialog(this) != true) return;
        canvas.SetProjectFolder(dlg.FolderName);
    }

    private void UserDictionary_Click(object sender, RoutedEventArgs e)
    {
        var path = DocsCanvas.UserDictionaryPath;
        if (path is null) return;

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (!File.Exists(path))
            File.WriteAllText(path, "");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ProjectDictionary_Click(object sender, RoutedEventArgs e)
    {
        var path = ActiveTab?.Editor.Canvas.ProjectDictionaryPath;
        if (path is null) return;

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (!File.Exists(path))
            File.WriteAllText(path, "");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void CloseTab(DocumentTab tab)
    {
        if (tab.Editor.IsDirty)
        {
            TabControl.SelectedItem = tab.TabItem;
            if (!ConfirmDiscard(tab)) return;
        }

        if (tab.FilePath != null)
            AddRecentFile(tab.FilePath);

        _tabs.Remove(tab);
        TabControl.Items.Remove(tab.TabItem);
        tab.Dispose();

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
        foreach (var tab in _tabs)
        {
            if (tab.FilePath != null)
                AddRecentFile(tab.FilePath);
        }

        SaveSession();

        foreach (var tab in _tabs)
            tab.Dispose();

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

    private void AddRecentFile(string path)
    {
        _recentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, path);
        if (_recentFiles.Count > MaxRecentFiles)
            _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);
    }

    private void RecentFiles_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentFilesMenuItem.Items.Clear();
        if (_recentFiles.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var path in _recentFiles)
        {
            var item = new MenuItem { Header = path.Replace("_", "__") };
            var captured = path;
            item.Click += (_, _) => TryOpenFileFromPath(captured);
            RecentFilesMenuItem.Items.Add(item);
        }
    }

    private void SaveSession()
    {
        var saved = _sessionStore.State;
        var state = new SessionState
        {
            OpenFiles = _openedFromPath
                ? new List<string>(saved.OpenFiles)
                : _tabs
                    .Where(t => t.FilePath != null)
                    .Select(t => t.FilePath!)
                    .ToList(),
            ActiveTabIndex = _openedFromPath
                ? saved.ActiveTabIndex
                : ActiveTab != null ? _tabs.IndexOf(ActiveTab) : 0,
            RecentFiles = new List<string>(_recentFiles),
        };

        if (ActiveTab != null)
            state.EditorState = ActiveTab.Editor.GetState();

        // A command-line mode is for that run. Saved only if it was changed away from during it,
        // since that change is the user's own choice.
        if (App.EditModeOverride is { } mode && state.EditorState is { } es && es.EditMode == mode)
            es.EditMode = _savedEditMode;
        if (state.EditorState is { } editor)
        {
            if (App.ZoomOverride is { } zoom && Math.Abs(editor.ZoomLevel - zoom) < 0.001) editor.ZoomLevel = _savedZoom;
            if (App.TocOverride is { } toc && editor.ShowToc == toc) editor.ShowToc = _savedToc;
            if (App.MinimapOverride is { } minimap && editor.ShowMinimap == minimap) editor.ShowMinimap = _savedMinimap;
        }

        _sessionStore.Save(state);
    }

    private void RestoreSettings()
    {
        var session = _sessionStore.State;

        if (session.EditorState != null)
            _editorState = session.EditorState;

        _recentFiles.AddRange(session.RecentFiles);
    }

    private void RestoreOpenFiles()
    {
        var session = _sessionStore.State;

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
        var existing = _tabs.Find(t =>
            string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            TabControl.SelectedItem = existing.TabItem;
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

    /// <remarks>
    /// <para>
    /// <b>A save that fails must not take the document with it.</b> The write used to throw straight
    /// out of <c>Save_Click</c>, and this app has no unhandled-exception handler, so a file another
    /// program had locked, a full disk or a read-only folder terminated the editor — and the unsaved
    /// buffer, which is the one thing the save was trying to protect, went with it.
    /// </para>
    /// <para>
    /// Now nothing about the tab changes unless the write lands: it keeps the path and base it had,
    /// stays dirty, and watches its file again. That is also what makes closing safe, because
    /// <c>ConfirmDiscard</c> saves and then asks whether the tab is still dirty — so a failed save now
    /// cancels the close instead of crashing through it. The path and base are still set before the
    /// write, as they were, since a Save As may need the new base to resolve the document's images.
    /// </para>
    /// <para>
    /// The write goes through <c>SafeFile.WriteAllText</c>, so the document is replaced whole or not
    /// at all: an in-place write truncated it first, and a kill or power loss in that window left it
    /// empty. The swap needs delete access, so a program holding the document open without sharing
    /// delete now blocks the save where it did not before — which fails here loudly, with the document
    /// still open and dirty, where truncation failed silently and for good. The file watcher already
    /// treats a replace onto its file as a modification, not a rename, and is suppressed across the
    /// save besides.
    /// </para>
    /// </remarks>
    private void SaveToFile(DocumentTab tab, string path)
    {
        var previousPath = tab.FilePath;
        var previousBasePath = tab.Editor.DocumentBasePath;

        tab.SuppressFileWatcher();
        tab.FilePath = path;
        tab.Editor.DocumentBasePath = Path.GetDirectoryName(path)!;

        try
        {
            Raisin.Core.SafeFile.WriteAllText(path, tab.Editor.GetText());
        }
        catch (Exception ex)
        {
            tab.FilePath = previousPath;
            tab.Editor.DocumentBasePath = previousBasePath;
            tab.SetupFileWatcher(this);

            MessageBox.Show(this,
                $"'{Path.GetFileName(path)}' could not be saved. The document is still open, with your changes.\n\n{ex.Message}",
                "RaisinDocs Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        tab.Editor.MarkClean();
        AddRecentFile(path);
        UpdateTabHeader(tab);
        UpdateTitle();
        tab.SetupFileWatcher(this);
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

    private class DocumentTab(TabItem tabItem, DocsEditor editor, TextBlock headerText) : IDisposable
    {
        private FileChangeWatcher? _fileWatcher;
        private bool _isReloadingFromDisk;

        public TabItem TabItem { get; } = tabItem;
        public DocsEditor Editor { get; } = editor;
        public TextBlock HeaderText { get; } = headerText;
        public string? FilePath { get; set; }

        public void SuppressFileWatcher() => _fileWatcher?.Suppress();

        public void SetupFileWatcher(MainWindow owner)
        {
            CleanupFileWatcher();
            if (FilePath == null)
                return;

            _fileWatcher = new FileChangeWatcher(change =>
            {
                if (change.ChangeType == FileChangeType.Renamed)
                {
                    owner.Dispatcher.Invoke(() =>
                    {
                        FilePath = change.FilePath;
                        UpdateTabHeader(this);
                        owner.UpdateTitle();
                    });
                    return;
                }

                owner.Dispatcher.Invoke(() =>
                {
                    if (_isReloadingFromDisk)
                        return;

                    if (!Editor.IsDirty)
                    {
                        ReloadFromDisk(owner);
                        return;
                    }

                    var editorState = Editor.GetState();
                    if (!editorState.PromptOnExternalChanges)
                    {
                        ReloadFromDisk(owner);
                        return;
                    }

                    var name = Path.GetFileName(FilePath);
                    var result = MessageBox.Show(owner,
                        $"'{name}' has been modified by another application.\n\nReload from disk and discard your unsaved changes?",
                        "File Changed", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                        ReloadFromDisk(owner);
                });
            });

            _fileWatcher.WatchFile(FilePath);
        }

        private void ReloadFromDisk(MainWindow? owner = null)
        {
            if (FilePath == null || !File.Exists(FilePath))
                return;

            try
            {
                _isReloadingFromDisk = true;
                var content = File.ReadAllText(FilePath);
                Editor.SetText(content);
                Editor.MarkClean();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Could not reload file: {ex.Message}");
            }
            finally
            {
                _isReloadingFromDisk = false;
            }
        }

        private void CleanupFileWatcher()
        {
            _fileWatcher?.Dispose();
            _fileWatcher = null;
        }

        public void Dispose()
        {
            CleanupFileWatcher();
            GC.SuppressFinalize(this);
        }
    }
}
