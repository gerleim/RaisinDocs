using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>
/// Adding a word to a dictionary that cannot be written reports, rather than ending the application.
/// </summary>
/// <remarks>
/// The dictionary saves threw out of the add-word action. RaisinDocs.Editor has no unhandled-exception
/// handler and RaisinTerminal2, which embeds this editor, leaves unexpected exceptions unhandled on
/// purpose — so a dictionary held by another program took down the editor, or the terminal with every
/// session in it. The user dictionary's path is global and cannot be pointed at a test folder; it
/// shares this code exactly, so the project dictionary stands for both.
/// </remarks>
public class DictionarySaveFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rd-dictionary-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public DictionarySaveFailureTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "custom-dictionary.txt");
        File.WriteAllText(_path, "Existing\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private SpellCheckService ServiceOver(List<string> failures)
    {
        var svc = new SpellCheckService { SaveFailed = failures.Add };
        svc.LoadEmbeddedDictionary();
        svc.LoadProjectDictionary(_path);
        return svc;
    }

    private FileStream HeldByAnotherProgram() =>
        new(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public void Adding_a_word_to_a_held_dictionary_reports_rather_than_throws()
    {
        var failures = new List<string>();
        using var svc = ServiceOver(failures);

        using (HeldByAnotherProgram())
        {
            var add = () => svc.AddToProjectDictionary("Zyxwvq");
            add.Should().NotThrow("a throw here ended the editor, and the terminal that embeds it");
        }

        failures.Should().ContainSingle().Which.Should().Contain("project dictionary");
        svc.Check("Zyxwvq").Should().BeTrue("the word joined the dictionary in memory before the save");
    }

    [Fact]
    public void A_word_that_failed_to_save_reaches_disk_at_the_next_save()
    {
        // Why a failed dictionary save is a Warning and not worse: every save writes the whole set,
        // so a word that missed its own save is carried by the next one that lands.
        var failures = new List<string>();
        using var svc = ServiceOver(failures);

        using (HeldByAnotherProgram())
            svc.AddToProjectDictionary("Zyxwvq");

        svc.AddToProjectDictionary("Qwvxyz");

        File.ReadAllLines(_path).Should().Contain(["Existing", "Zyxwvq", "Qwvxyz"]);
    }

    [Fact]
    public void A_dictionary_is_swapped_in_whole()
    {
        // What tells an atomic save from an in-place one: a reader holding the file across it. An
        // in-place rewrite empties the very file that reader has open; a swap leaves its handle on the
        // old version, complete — and a kill inside the save leaves that same old version on disk.
        using var svc = ServiceOver([]);
        var before = File.ReadAllText(_path);

        string seenByReader;
        using (var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            svc.AddToProjectDictionary("Zyxwvq");
            using var reader = new StreamReader(held);
            seenByReader = reader.ReadToEnd();
        }

        seenByReader.Should().Be(before);
        File.ReadAllLines(_path).Should().Contain("Zyxwvq");
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void A_dictionary_is_written_exactly_as_before()
    {
        // Project dictionaries live in project folders, often under version control. Saving must not
        // change a byte of their format, or every one of them shows as modified.
        using var svc = ServiceOver([]);

        svc.AddToProjectDictionary("naïve");

        File.ReadAllBytes(_path).Should().Equal(System.Text.Encoding.UTF8.GetBytes("Existing\r\nnaïve\r\n"),
            "UTF-8 without a byte-order mark, CRLF after every line, as File.WriteAllLines wrote it");
    }

    // ---- two editors on one dictionary ----
    //
    // Every editor builds its own service, and each read the dictionary once and saved its own copy
    // over the file — so the second of two tabs to add a word erased the first one's. The user
    // dictionary is shared the same way between RaisinDocs and RaisinTerminal2.

    [Fact]
    public void Two_editors_keep_each_others_words()
    {
        using var first = ServiceOver([]);
        using var second = ServiceOver([]);

        first.AddToProjectDictionary("Alphaword");
        second.AddToProjectDictionary("Betaword");

        File.ReadAllLines(_path).Should().Equal("Alphaword", "Betaword", "Existing");
    }

    [Fact]
    public void An_editor_learns_the_words_it_merged()
    {
        using var first = ServiceOver([]);
        using var second = ServiceOver([]);

        first.AddToProjectDictionary("Alphaword");
        second.AddToProjectDictionary("Betaword");

        second.Check("Alphaword").Should().BeTrue("the second editor read it from disk when it saved");
    }

    [Fact]
    public void A_dictionary_that_cannot_be_read_back_is_not_saved_over()
    {
        // Writing without the merge is the overwrite the merge exists to prevent. The holder lets the
        // file be replaced but not read, so only the refusal to save keeps its words.
        var failures = new List<string>();
        using var svc = ServiceOver(failures);
        File.WriteAllText(_path, "Existing\nAddedElsewhere\n");

        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Write | FileShare.Delete))
            svc.AddToProjectDictionary("Zyxwvq");

        failures.Should().ContainSingle();
        File.ReadAllLines(_path).Should().Contain("AddedElsewhere");

        svc.AddToProjectDictionary("Qwvxyz");
        File.ReadAllLines(_path).Should().Equal("AddedElsewhere", "Existing", "Qwvxyz", "Zyxwvq");
    }
}
