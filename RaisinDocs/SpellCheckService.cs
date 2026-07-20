using System.IO;
using System.Reflection;
using WeCantSpell.Hunspell;

namespace RaisinDocs;

public readonly record struct SpellingError(int StartOffset, int Length, string Word);

internal sealed class SpellCheckService : IDisposable
{
    private WordList? _wordList;
    private readonly HashSet<string> _userDictionary = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _projectDictionary = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionIgnores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _userDictionaryPath;
    private string? _projectDictionaryPath;

    public const string ProjectDictionaryFileName = "custom-dictionary.txt";

    public bool IsLoaded => _wordList is not null;

    public void LoadEmbeddedDictionary()
    {
        if (_wordList is not null) return;

        var assembly = Assembly.GetExecutingAssembly();
        using var dicStream = assembly.GetManifestResourceStream("RaisinDocs.Dictionaries.en_US.dic");
        using var affStream = assembly.GetManifestResourceStream("RaisinDocs.Dictionaries.en_US.aff");
        if (dicStream is null || affStream is null)
            throw new InvalidOperationException("Embedded dictionary resources not found.");

        _wordList = WordList.CreateFromStreams(dicStream, affStream);
        LoadUserDictionary();
    }

    public bool Check(string word)
    {
        if (_wordList is null) return true;
        if (_sessionIgnores.Contains(word)) return true;
        if (_userDictionary.Contains(word)) return true;
        if (_projectDictionary.Contains(word)) return true;

        if (_cache.TryGetValue(word, out var cached))
            return cached;

        bool valid = _wordList.Check(word);
        _cache[word] = valid;
        return valid;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        if (_wordList is null) return [];
        return _wordList.Suggest(word).Take(5).ToList();
    }

    public void AddToUserDictionary(string word)
    {
        if (_userDictionary.Add(word))
        {
            _cache.Remove(word);
            SaveUserDictionary();
        }
    }

    public void IgnoreAll(string word)
    {
        if (_sessionIgnores.Add(word))
            _cache.Remove(word);
    }

    public void LoadProjectDictionary(string? projectFolder)
    {
        _projectDictionary.Clear();
        _projectDictionaryPath = null;
        _cache.Clear();

        if (string.IsNullOrEmpty(projectFolder)) return;

        _projectDictionaryPath = Path.Combine(projectFolder, ProjectRootFinder.MarkerDirectoryName, ProjectDictionaryFileName);

        if (!File.Exists(_projectDictionaryPath)) return;

        foreach (var line in File.ReadLines(_projectDictionaryPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                _projectDictionary.Add(trimmed);
        }
    }

    public void AddToProjectDictionary(string word)
    {
        if (_projectDictionaryPath is null) return;
        if (_projectDictionary.Add(word))
        {
            _cache.Remove(word);
            SaveProjectDictionary();
        }
    }

    public void ClearCache() => _cache.Clear();

    private void SaveProjectDictionary()
    {
        if (_projectDictionaryPath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_projectDictionaryPath)!);
        File.WriteAllLines(_projectDictionaryPath,
            _projectDictionary.OrderBy(w => w, StringComparer.OrdinalIgnoreCase));
    }

    private void LoadUserDictionary()
    {
        _userDictionaryPath = GetUserDictionaryPath();
        if (_userDictionaryPath is null || !File.Exists(_userDictionaryPath)) return;

        foreach (var line in File.ReadLines(_userDictionaryPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                _userDictionary.Add(trimmed);
        }
    }

    private void SaveUserDictionary()
    {
        if (_userDictionaryPath is null) return;
        var dir = Path.GetDirectoryName(_userDictionaryPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllLines(_userDictionaryPath, _userDictionary.OrderBy(w => w, StringComparer.OrdinalIgnoreCase));
    }

    private static string? GetUserDictionaryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) return null;
        return Path.Combine(appData, "Raisin", "RaisinDocs", "user-dictionary.txt");
    }

    public void Dispose()
    {
        _cache.Clear();
    }
}
