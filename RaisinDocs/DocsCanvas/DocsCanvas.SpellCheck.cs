using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    private SpellCheckController? _spellCheckController;

    internal SpellCheckController SpellCheck =>
        _spellCheckController ??= new SpellCheckController((IDocsCanvasServices)this);

    public bool SpellCheckEnabled => SpellCheck.SpellCheckEnabled;
    public string? ProjectFolder => SpellCheck.ProjectFolder;

    public void SetSpellCheckEnabled(bool enabled)
    {
        SpellCheck.SetSpellCheckEnabled(enabled);
    }

    private void CleanupSpellCheck()
    {
        _spellCheckController?.Cleanup();
    }

    internal void OnDocumentBasePathChanged()
    {
        _spellCheckController?.OnDocumentBasePathChanged();
    }

    public void SetProjectFolder(string folder)
    {
        SpellCheck.SetProjectFolder(folder);
    }

    private void OnContentChangedForSpellCheck()
    {
        _spellCheckController?.OnContentChanged();
    }

    private void DrawSpellingErrors(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        _spellCheckController?.DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);
    }

    private bool AddSpellCheckMenuItems(ContextMenu menu, Point position)
    {
        return _spellCheckController?.AddSpellCheckMenuItems(menu, position) ?? false;
    }

    public static string? UserDictionaryPath => SpellCheckController.UserDictionaryPath;
    public string? ProjectDictionaryPath => SpellCheck.ProjectDictionaryPath;

    internal SpellCheckService? TestSpellCheckService => _spellCheckController?.TestSpellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _spellCheckController?.TestGetSpellingErrors(blockIndex);
}
