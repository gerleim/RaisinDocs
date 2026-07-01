using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Raisin.WPF.Base;

namespace RaisinDocs.Viewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
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
        var dlg = new OpenFileDialog { Filter = "Markdown files|*.md|All files|*.*" };
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

    private void OpenFile(string path)
    {
        Viewer.DocumentBasePath = Path.GetDirectoryName(path)!;
        Viewer.SetText(File.ReadAllText(path));
        Viewer.Canvas.SetEditMode(DocsCanvas.EditMode.Visual);
        Title = $"{Path.GetFileName(path)} — RaisinDocs Viewer";
    }
}
