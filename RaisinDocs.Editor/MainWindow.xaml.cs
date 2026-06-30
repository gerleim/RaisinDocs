using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Raisin.WPF.Base;

namespace RaisinDocs.Editor;

public partial class MainWindow : Window
{
    private string? _filePath;

    public MainWindow()
    {
        InitializeComponent();
        Editor.IsDirtyChanged += (_, _) => UpdateTitle();
        UpdateTitle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
    }

    private void UpdateTitle()
    {
        var name = _filePath != null ? Path.GetFileName(_filePath) : "Untitled";
        var dirty = Editor.IsDirty ? " *" : "";
        Title = $"{name}{dirty} — RaisinDocs Editor";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _filePath = null;
        Editor.SetText("");
        Editor.MarkClean();
        UpdateTitle();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = "Markdown files|*.md|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        OpenFile(dlg.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath != null)
            SaveToFile(_filePath);
        else
            SaveAs_Click(sender, e);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Markdown files|*.md|All files|*.*" };
        if (_filePath != null)
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_filePath)!;
            dlg.FileName = Path.GetFileName(_filePath);
        }
        if (dlg.ShowDialog(this) != true) return;
        SaveToFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        _filePath = path;
        Editor.DocumentBasePath = Path.GetDirectoryName(path)!;
        Editor.SetText(File.ReadAllText(path));
        Editor.MarkClean();
        UpdateTitle();
    }

    private void SaveToFile(string path)
    {
        _filePath = path;
        Editor.DocumentBasePath = Path.GetDirectoryName(path)!;
        File.WriteAllText(path, Editor.GetText());
        Editor.MarkClean();
        UpdateTitle();
    }

    private bool ConfirmDiscard()
    {
        if (!Editor.IsDirty) return true;
        var result = MessageBox.Show(this,
            "You have unsaved changes. Do you want to save before continuing?",
            "RaisinDocs Editor",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            Save_Click(this, new RoutedEventArgs());
            return !Editor.IsDirty;
        }
        return true;
    }
}
