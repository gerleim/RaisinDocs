using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Raisin.WPF.Base;

namespace RaisinDocs.Viewer;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".txt"
    };

    private const string FileFilter =
        "Markdown files|*.md;*.markdown;*.mdown;*.mkd;*.mkdn|Text files|*.txt|All files|*.*";

    private FileChangeWatcher? _fileWatcher;
    private string? _currentFilePath;
    private bool _isReloadingFromDisk;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Viewer.Canvas.IsReadOnly = true;
            Viewer.Canvas.SetEditMode(DocsCanvas.EditMode.Visual);

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
                TryOpenFileFromPath(args[1]);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        if (dlg.ShowDialog(this) != true) return;
        OpenFile(dlg.FileName);
    }

    private void TryOpenFileFromPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            MessageBox.Show(this, $"File not found:\n{fullPath}", "RaisinDocs Viewer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            OpenFile(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open file:\n{ex.Message}", "RaisinDocs Viewer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        var file = files.FirstOrDefault(f => AcceptedExtensions.Contains(Path.GetExtension(f)));
        if (file != null)
            TryOpenFileFromPath(file);
    }

    private void OpenFile(string path)
    {
        _currentFilePath = path;
        Viewer.DocumentBasePath = Path.GetDirectoryName(path)!;
        Viewer.SetText(File.ReadAllText(path));
        Viewer.Canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        Title = $"{Path.GetFileName(path)} — RaisinDocs Viewer";

        SetupFileWatcher(path);
    }

    private void SetupFileWatcher(string filePath)
    {
        _fileWatcher?.Dispose();

        _fileWatcher = new FileChangeWatcher(change =>
        {
            if (change.ChangeType == FileChangeType.Renamed)
            {
                _currentFilePath = change.FilePath;
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (_isReloadingFromDisk)
                    return;

                try
                {
                    _isReloadingFromDisk = true;
                    var content = File.ReadAllText(filePath);
                    Viewer.SetText(content);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not reload file:\n{ex.Message}", "RaisinDocs Viewer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _isReloadingFromDisk = false;
                }
            });
        });

        _fileWatcher.WatchFile(filePath);
    }

    private void Find_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.PerformFind();

    private void View_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var theme = Viewer.Canvas.Theme;
        ThemeLight.IsChecked = theme == DocsCanvas.EditorTheme.Light;
        ThemeDark.IsChecked = theme == DocsCanvas.EditorTheme.Dark;
        ThemeDarkBlue.IsChecked = theme == DocsCanvas.EditorTheme.DarkBlue;

        var mode = Viewer.Canvas.CurrentEditMode;
        ModeSource.IsChecked = mode == DocsCanvas.EditMode.Source;
        ModeVisual.IsChecked = mode == DocsCanvas.EditMode.Visual;

        var preview = Viewer.Canvas.CurrentImagePreview;
        ImageOff.IsChecked = preview == DocsCanvas.ImagePreviewMode.Off;
        ImageInline.IsChecked = preview == DocsCanvas.ImagePreviewMode.Inline;
        ImageOnHover.IsChecked = preview == DocsCanvas.ImagePreviewMode.OnHover;

        TocMenuItem.IsChecked = Viewer.ShowToc;
        MinimapMenuItem.IsChecked = Viewer.ShowMinimap;
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetCurrentValue(DocsCanvas.ThemeProperty, DocsCanvas.EditorTheme.Light);

    private void ThemeDark_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetCurrentValue(DocsCanvas.ThemeProperty, DocsCanvas.EditorTheme.Dark);

    private void ThemeDarkBlue_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetCurrentValue(DocsCanvas.ThemeProperty, DocsCanvas.EditorTheme.DarkBlue);

    private void ModeSource_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetEditMode(DocsCanvas.EditMode.Source);

    private void ModeVisual_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetEditMode(DocsCanvas.EditMode.Visual);

    private void ImageOff_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetImagePreview(DocsCanvas.ImagePreviewMode.Off);

    private void ImageInline_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetImagePreview(DocsCanvas.ImagePreviewMode.Inline);

    private void ImageOnHover_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.SetImagePreview(DocsCanvas.ImagePreviewMode.OnHover);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.ZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.ZoomOut();

    private void ZoomReset_Click(object sender, RoutedEventArgs e) =>
        Viewer.Canvas.ZoomReset();

    private void Toc_Click(object sender, RoutedEventArgs e)
    {
        Viewer.Canvas.ToggleToc();
        TocMenuItem.IsChecked = Viewer.ShowToc;
    }

    private void Minimap_Click(object sender, RoutedEventArgs e)
    {
        Viewer.Canvas.ToggleMinimap();
        MinimapMenuItem.IsChecked = Viewer.ShowMinimap;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _fileWatcher?.Dispose();
        base.OnClosing(e);
    }
}
