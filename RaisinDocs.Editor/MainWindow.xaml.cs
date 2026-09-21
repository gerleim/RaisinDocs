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
            // Once the window is up, whatever the tabs below turn out to be.
            Dispatcher.BeginInvoke(OfferRecoveredDocuments, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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

        // See App.CrashTestSwitch.
        if (App.CrashTestKeys && modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F12)
                throw new InvalidOperationException("Crash test: an exception on the UI thread (--crash-test, Ctrl+Alt+Shift+F12).");
            if (key == Key.F11)
            {
                new Thread(() => throw new InvalidOperationException(
                    "Crash test: an exception on a background thread (--crash-test, Ctrl+Alt+Shift+F11).")).Start();
                e.Handled = true;
                return;
            }
        }

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

        tab.RecordDiskBaseline();
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
            tab.RecordDiskBaseline();
            // The reused tab never watched its file, so another program's changes went unseen here.
            tab.SetupFileWatcher(this);
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
        if (!ConfirmOverwriteExternalChange(tab, path))
            return;

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
        tab.RecordDiskBaseline();
        AddRecentFile(path);
        UpdateTabHeader(tab);
        UpdateTitle();
        tab.SetupFileWatcher(this);
    }

    /// <summary>
    /// Asks before a save replaces a version of the file this tab's text was not based on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keeping your edits used to mean overwriting theirs, unasked.</b> When another program
    /// changed the file while the tab had unsaved edits, answering No to the reload kept the edits —
    /// and nothing remembered that the file had moved on, so the next Ctrl+S replaced the other
    /// program's work without a word. The same happened when a reload failed, or when a change went
    /// unseen.
    /// </para>
    /// <para>
    /// The tab now keeps the stamp of the version it was loaded from or last saved, and a save to that
    /// same file checks it first. Saving to a different file is left to the Save As dialog's own
    /// overwrite question. No here cancels the save and keeps the tab dirty, so closing is cancelled
    /// too, through <see cref="ConfirmDiscard"/>.
    /// </para>
    /// <para>
    /// A change the user already answered is not asked about again: keeping their version then
    /// records that one as seen, as Notepad++ does, so this asks only about a change nobody was asked
    /// about — one the watcher missed, a reload that never settled, or one that landed after the answer.
    /// </para>
    /// </remarks>
    private bool ConfirmOverwriteExternalChange(DocumentTab tab, string path)
    {
        if (tab.FilePath is null || tab.DiskBaseline is not { } baseline
            || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(tab.FilePath), StringComparison.OrdinalIgnoreCase))
            return true;

        if (DiskStamp.Of(path) is not { } onDisk || onDisk == baseline)
            return true;

        var result = MessageBox.Show(this,
            $"'{Path.GetFileName(path)}' was changed by another program after it was opened or last saved here.\n\n"
          + "Save anyway? Their version will be replaced by yours.",
            "File Changed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>The tabs with unsaved changes, for rescue as the editor goes down.</summary>
    /// <remarks>
    /// Nothing is read here: the text is read inside the rescue, one tab at a time, so a tab whose
    /// state the crash damaged costs only itself.
    /// </remarks>
    internal List<RescuedDocument> UnsavedDocuments() =>
        _tabs.Where(tab =>
            {
                try { return tab.Editor.IsDirty; }
                catch { return true; }
            })
            .Select(tab => new RescuedDocument(tab.FilePath, () => tab.Editor.GetText(), tab.DiskBaseline))
            .ToList();

    /// <summary>
    /// Offers back the documents an earlier session rescued when it crashed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yes opens each with its unsaved changes — dirty, so closing it asks, as it would have before
    /// the crash — and removes it from the recovery folder. No removes them. Cancel leaves them to be
    /// offered at the next start.
    /// </para>
    /// <para>
    /// A document goes back to its file when that file is still there: into the tab the session
    /// already opened for it if there is one, or a new one. It keeps the version on disk it was based
    /// on, so if another program changed the file since the crash, saving asks first. A document
    /// whose file is gone, or that never had one, comes back untitled.
    /// </para>
    /// </remarks>
    private void OfferRecoveredDocuments()
    {
        var recovery = new DocumentRecovery(App.RecoveryDirectory);
        IReadOnlyList<RecoveredDocument> found;
        try
        {
            found = recovery.Find();
        }
        catch (Exception ex)
        {
            App.Logger.Log(DocsLogLevel.Error, $"Could not look for rescued documents in {App.RecoveryDirectory}: {ex.Message}");
            return;
        }
        if (found.Count == 0)
            return;

        var names = string.Join("\n", found.Select(d => "  " + (d.OriginalPath ?? "Untitled")));
        var answer = MessageBox.Show(this,
            $"RaisinDocs closed unexpectedly, and {found.Count} unsaved document{(found.Count == 1 ? " was" : "s were")} kept:\n\n{names}\n\n"
          + "Yes: open them now, with their unsaved changes.\n"
          + "No: discard them.\n"
          + "Cancel: decide the next time RaisinDocs starts.",
            "Recover Documents", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Cancel)
            return;

        var placeholder = _tabs.Count == 1 && _tabs[0].FilePath is null && !_tabs[0].Editor.IsDirty
            && _tabs[0].Editor.GetText().Length == 0 ? _tabs[0] : null;

        foreach (var document in found)
        {
            try
            {
                if (answer == MessageBoxResult.Yes)
                    Reopen(document);
                recovery.Discard(document);
            }
            catch (Exception ex)
            {
                App.Logger.Log(DocsLogLevel.Error, $"Could not recover {document.RecoveryFile}: {ex.Message}. It is left in the recovery folder.");
            }
        }

        if (placeholder is not null && _tabs.Count > 1)
            CloseTab(placeholder);
    }

    private void Reopen(RecoveredDocument document)
    {
        var path = document.OriginalPath is { } original && File.Exists(original) ? original : null;

        var tab = path is null
            ? null
            : _tabs.Find(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase) && !t.Editor.IsDirty);
        tab ??= AddTab(path, path is null ? "" : File.ReadAllText(path));

        tab.Editor.RestoreUnsavedText(document.Text);
        if (path is not null && document.Baseline is { } baseline)
            tab.RestoreDiskBaseline(baseline);
        TabControl.SelectedItem = tab.TabItem;
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
        private bool _isAsking;

        public TabItem TabItem { get; } = tabItem;
        public DocsEditor Editor { get; } = editor;
        public TextBlock HeaderText { get; } = headerText;
        public string? FilePath { get; set; }

        /// <summary>The version on disk this tab's text was loaded from or last saved to.</summary>
        public DiskStamp? DiskBaseline { get; private set; }

        public void RecordDiskBaseline() => DiskBaseline = FilePath is null ? null : DiskStamp.Of(FilePath);

        /// <summary>Takes the baseline a rescued document had, so a save still asks if the file moved on since.</summary>
        public void RestoreDiskBaseline(DiskStamp baseline) => DiskBaseline = baseline;

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

                // Off the UI thread: wait for the other program to finish before anything is loaded
                // or asked. A tab left stale when this fails is still safe — its baseline is unchanged,
                // so a save asks before replacing the file.
                var settled = SettledFile.Read(change.FilePath);
                owner.Dispatcher.Invoke(() => OnExternalChange(owner, settled));
            });

            _fileWatcher.WatchFile(FilePath);
        }

        /// <remarks>
        /// <para>
        /// A clean tab takes the new version silently. A tab with unsaved edits always asks — including
        /// when <c>PromptOnExternalChanges</c> is off, which used to reload over the edits and discard
        /// them without a word. Unsaved typing is never thrown away unasked.
        /// </para>
        /// <para>
        /// One question per burst: a change that arrives while the question is open is not asked about
        /// again, and a Yes loads whatever is on disk by then.
        /// </para>
        /// <para>
        /// A No is the decision, and is not asked again at save: it records the version it was about
        /// as seen, so saving replaces that version without a second question. It first moved nothing,
        /// and the save asked the same thing twice. Only the version asked about is recorded — a change
        /// that lands while the question is open still differs from it, and the save asks about that one.
        /// </para>
        /// </remarks>
        private void OnExternalChange(MainWindow owner, SettledText? settled)
        {
            if (_isReloadingFromDisk || _isAsking || FilePath is null || settled is null)
                return;

            if (settled.Stamp == DiskBaseline)
                return;

            if (!Editor.IsDirty)
            {
                Apply(settled);
                return;
            }

            MessageBoxResult result;
            _isAsking = true;
            try
            {
                result = MessageBox.Show(owner,
                    $"'{Path.GetFileName(FilePath)}' was changed by another program, and you have unsaved changes here.\n\n"
                  + "Yes: load their version. Your unsaved changes are lost.\n"
                  + "No: keep your version. Saving it will replace theirs.",
                    "File Changed", MessageBoxButton.YesNo, MessageBoxImage.Question);
            }
            finally
            {
                _isAsking = false;
            }

            if (result != MessageBoxResult.Yes)
            {
                DiskBaseline = settled.Stamp;
                return;
            }

            // The file may have moved on while the question was open.
            var latest = DiskStamp.Of(FilePath) == settled.Stamp ? settled : SettledFile.Read(FilePath);
            if (latest is null)
            {
                MessageBox.Show(owner,
                    $"'{Path.GetFileName(FilePath)}' could not be read, so your version is still open.",
                    "File Changed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Apply(latest);
        }

        private void Apply(SettledText settled)
        {
            try
            {
                _isReloadingFromDisk = true;
                Editor.SetText(settled.Content);
                Editor.MarkClean();
                DiskBaseline = settled.Stamp;
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
