using System.IO;
using System.Windows;
using AvalonDock.Themes;
using Raisin.WPF.Base;

namespace RaisinDocs.TestApp;

public partial class MainWindow : Window
{
    private static readonly string SavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "scratch.md");

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadContent();
        Closing += (_, _) => SaveContent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
        DockingManager.Theme = new Vs2013DarkTheme();
    }

    private void LoadContent()
    {
        Editor.DocumentBasePath = Path.GetDirectoryName(SavePath)!;
        if (File.Exists(SavePath))
            Editor.SetText(File.ReadAllText(SavePath));
    }

    private void SaveContent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        File.WriteAllText(SavePath, Editor.GetText());
    }
}
