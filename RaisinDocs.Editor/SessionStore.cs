using System.Text.Json;
using System.Text.Json.Serialization;
using Raisin.Core;

namespace RaisinDocs.Editor;

internal class SessionState
{
    public List<string> OpenFiles { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public DocsEditorState? EditorState { get; set; }
}

internal class SessionStore : DurableJsonStore<SessionState>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SessionStore(string filePath) : base(filePath, Options)
    {
        LoadFromDisk();
    }

    public SessionState State => Data;

    public void Save(SessionState state)
    {
        lock (Sync) { Data = state; WriteFile(); }
    }
}
