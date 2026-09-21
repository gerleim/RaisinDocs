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

    public void LoadProjectDictionary(string? dictionaryPath)
    {
        _projectDictionary.Clear();
        _projectDictionaryPath = dictionaryPath;
        _cache.Clear();

        if (dictionaryPath is null || !File.Exists(dictionaryPath)) return;

        _projectDictionary.UnionWith(ReadWords(dictionaryPath, skipComments: true));
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

    /// <summary>Told when a dictionary could not be written, so a host can log it.</summary>
    /// <remarks>
    /// <para>
    /// <b>These saves used to throw, and the throw ended the application.</b> They run from the
    /// add-word action, RaisinDocs.Editor has no unhandled-exception handler, and RaisinTerminal2 —
    /// which embeds this editor for task documents and attachments — leaves unexpected exceptions
    /// unhandled on purpose. So adding a word while a dictionary was held by another program took down
    /// the editor, or the terminal with every session in it, along with whatever was unsaved.
    /// </para>
    /// <para>
    /// A failure is reported instead, and nothing is lost by it: the word joins the in-memory set
    /// before the save, so it stops being flagged at once, and every later save writes the whole set —
    /// so it reaches disk at the next successful one. It is only lost if the application closes first.
    /// </para>
    /// <para>
    /// Both are written through <c>SafeFile.WriteWithStream</c>, so a dictionary is replaced whole or
    /// not at all rather than truncated at the start of every save — the user dictionary is words
    /// gathered over months, and a kill inside an in-place rewrite could empty it. The output is
    /// byte-for-byte what <c>File.WriteAllLines</c> wrote, so no dictionary under version control
    /// changes by being saved this way.
    /// </para>
    /// <para>
    /// <b>A save merges with the file rather than overwriting it.</b> Every editor builds its own
    /// service and read the dictionaries once, so each saved its own copy over the file: two tabs, or
    /// RaisinDocs and RaisinTerminal2 sharing the user dictionary, dropped each other's words with
    /// nothing said. The file is now read again just before each save and the words on disk joined to
    /// the ones in memory, so a save only ever adds. If that read fails the save is skipped and
    /// reported — writing without it is the very overwrite this prevents — and the word is carried by
    /// the next save as above. Two saves in the same instant can still race between read and write;
    /// adding words is a hand action, and that window is a few milliseconds wide.
    /// </para>
    /// </remarks>
    internal Action<string>? SaveFailed { get; set; }

    private void SaveProjectDictionary()
    {
        if (_projectDictionaryPath is null) return;
        SaveMerged(_projectDictionaryPath, _projectDictionary, skipComments: true, "project dictionary");
    }

    private void LoadUserDictionary()
    {
        _userDictionaryPath = RaisinDocsPaths.GetUserDictionaryPath();
        if (_userDictionaryPath is null || !File.Exists(_userDictionaryPath)) return;

        _userDictionary.UnionWith(ReadWords(_userDictionaryPath, skipComments: false));
    }

    /// <inheritdoc cref="SaveFailed"/>
    private void SaveUserDictionary()
    {
        if (_userDictionaryPath is null) return;
        SaveMerged(_userDictionaryPath, _userDictionary, skipComments: false, "user dictionary");
    }

    /// <inheritdoc cref="SaveFailed"/>
    private void SaveMerged(string path, HashSet<string> words, bool skipComments, string name)
    {
        try
        {
            if (File.Exists(path))
                words.UnionWith(ReadWords(path, skipComments));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Raisin.Core.SafeFile.WriteWithStream(path, writer =>
            {
                foreach (var word in words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase))
                    writer.WriteLine(word);
            });
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke($"Could not save the {name} {path}: {ex.Message}");
        }
    }

    private static List<string> ReadWords(string path, bool skipComments)
    {
        var words = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !(skipComments && trimmed.StartsWith('#')))
                words.Add(trimmed);
        }
        return words;
    }

    public void Dispose()
    {
        _cache.Clear();
    }
}
