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
}
