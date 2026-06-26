using System.IO;
using System.Text.Json;
using System.Windows;
using AvalonDock.Themes;
using Raisin.WPF.Base;

namespace RaisinDocs.TestApp;

public partial class MainWindow : Window
{
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaisinDocs");

    private static readonly string ContentPath = Path.Combine(SaveDir, "scratch.md");
    private static readonly string StatePath = Path.Combine(SaveDir, "editor-state.json");

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
        Editor.DocumentBasePath = Path.GetDirectoryName(ContentPath)!;
        if (File.Exists(ContentPath))
            Editor.SetText(File.ReadAllText(ContentPath));
        if (File.Exists(StatePath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<DocsEditorState>(File.ReadAllText(StatePath));
                if (state != null) Editor.ApplyState(state);
            }
            catch (JsonException) { }
        }
    }

    private void SaveContent()
    {
        Directory.CreateDirectory(SaveDir);
        File.WriteAllText(ContentPath, Editor.GetText());
        File.WriteAllText(StatePath, JsonSerializer.Serialize(Editor.GetState()));
    }
}
