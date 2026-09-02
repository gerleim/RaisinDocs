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

    /// <summary>Document to open instead of the scratch pad, if one was given.</summary>
    private readonly string? _file;

    public MainWindow(string? file = null)
    {
        _file = file;
        InitializeComponent();
        Loaded += (_, _) => LoadContent();
        // A document opened by path is not ours to write back over. Only the scratch pad is.
        Closing += (_, _) => { if (_file == null) SaveContent(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkWindowHelper.Apply(this);
        DockingManager.Theme = new Vs2013DarkTheme();
    }

    private void LoadContent()
    {
        string path = _file ?? ContentPath;
        Editor.DocumentBasePath = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (File.Exists(path))
        {
            Editor.SetText(File.ReadAllText(path));
            if (_file != null) Title = $"{Path.GetFileName(_file)} - RaisinDocs sandbox (read-only)";
        }
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
