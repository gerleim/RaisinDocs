using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class ExtractCheckableWordsTests
{
    private static List<ParsedBlock> ParseBlocks(params string[] blocks)
    {
        return MarkdownParser.Parse(i => blocks[i], blocks.Length);
    }

    private static List<(int Offset, string Word)> ExtractWords(string text)
    {
        var parsed = ParseBlocks(text)[0];
        return MarkdownParser.ExtractCheckableWords(text, parsed);
    }

    [Fact]
    public void PlainText_ExtractsAllWords()
    {
        var words = ExtractWords("hello world");
        words.Should().SatisfyRespectively(
            w => { w.Word.Should().Be("hello"); w.Offset.Should().Be(0); },
            w => { w.Word.Should().Be("world"); w.Offset.Should().Be(6); });
    }

    [Fact]
    public void SingleLetterWords_AreSkipped()
    {
        var words = ExtractWords("I am a test");
        words.Select(w => w.Word).Should().NotContain("I");
        words.Select(w => w.Word).Should().NotContain("a");
        words.Select(w => w.Word).Should().Contain("am");
        words.Select(w => w.Word).Should().Contain("test");
    }

    [Fact]
    public void WordsWithDigits_AreSkipped()
    {
        var words = ExtractWords("hello test123 world h1");
        words.Select(w => w.Word).Should().BeEquivalentTo(["hello", "world"]);
    }

    [Fact]
    public void AllCapsShortWords_AreSkipped()
    {
        var words = ExtractWords("the API and URL are WPF things");
        words.Select(w => w.Word).Should().NotContain("API");
        words.Select(w => w.Word).Should().NotContain("URL");
        words.Select(w => w.Word).Should().NotContain("WPF");
    }

    [Fact]
    public void AllCapsLongWords_AreKept()
    {
        var words = ExtractWords("the README and INFRASTRUCTURE");
        words.Select(w => w.Word).Should().Contain("README");
        words.Select(w => w.Word).Should().Contain("INFRASTRUCTURE");
    }

    [Fact]
    public void CodeSpan_IsSkipped()
    {
        var words = ExtractWords("use `myVar` here");
        words.Select(w => w.Word).Should().BeEquivalentTo(["use", "here"]);
    }

    [Fact]
    public void InlineLink_IsSkipped()
    {
        var words = ExtractWords("click [here](https://example.com) now");
        words.Select(w => w.Word).Should().BeEquivalentTo(["click", "now"]);
    }

    [Fact]
    public void InlineImage_IsSkipped()
    {
        var words = ExtractWords("see ![alt text](image.png) below");
        words.Select(w => w.Word).Should().BeEquivalentTo(["see", "below"]);
    }

    [Fact]
    public void HeadingPrefix_IsSkipped()
    {
        var text = "## Hello world";
        var parsed = ParseBlocks(text)[0];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        words.Select(w => w.Word).Should().BeEquivalentTo(["Hello", "world"]);
    }

    [Fact]
    public void BlockquotePrefix_IsSkipped()
    {
        var text = "> quoted text";
        var parsed = ParseBlocks(text)[0];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        words.Select(w => w.Word).Should().BeEquivalentTo(["quoted", "text"]);
    }

    [Fact]
    public void UnorderedListItem_SkipsPrefix()
    {
        var text = "- list item";
        var parsed = ParseBlocks(text)[0];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        words.Select(w => w.Word).Should().BeEquivalentTo(["list", "item"]);
    }

    [Fact]
    public void OrderedListItem_SkipsPrefix()
    {
        var text = "1. ordered item";
        var parsed = ParseBlocks(text)[0];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        words.Select(w => w.Word).Should().BeEquivalentTo(["ordered", "item"]);
    }

    [Fact]
    public void FencedCodeBlock_ReturnsNoWords()
    {
        var blocks = ParseBlocks("```", "some code here", "```");
        var words = MarkdownParser.ExtractCheckableWords("some code here", blocks[1]);
        words.Should().BeEmpty();
    }

    [Fact]
    public void Apostrophe_KeptInWord()
    {
        var words = ExtractWords("don't can't it's");
        words.Select(w => w.Word).Should().BeEquivalentTo(["don't", "can't", "it's"]);
    }

    [Fact]
    public void Offsets_AreCorrect()
    {
        var words = ExtractWords("hello, world!");
        words.Should().SatisfyRespectively(
            w => { w.Word.Should().Be("hello"); w.Offset.Should().Be(0); },
            w => { w.Word.Should().Be("world"); w.Offset.Should().Be(7); });
    }

    [Fact]
    public void BoldAndItalic_TextIsChecked()
    {
        var words = ExtractWords("this is **bold** and *italic* text");
        words.Select(w => w.Word).Should().Contain("bold");
        words.Select(w => w.Word).Should().Contain("italic");
    }

    [Fact]
    public void Strikethrough_TextIsChecked()
    {
        var words = ExtractWords("this is ~~struck~~ text");
        words.Select(w => w.Word).Should().Contain("struck");
    }

    [Fact]
    public void EmptyText_ReturnsNoWords()
    {
        var words = ExtractWords("");
        words.Should().BeEmpty();
    }

    [Fact]
    public void TableDataRow_ExtractsWords()
    {
        var blocks = ParseBlocks("| col1 | col2 |", "| --- | --- |", "| hello | world |");
        var words = MarkdownParser.ExtractCheckableWords("| hello | world |", blocks[2]);
        words.Select(w => w.Word).Should().BeEquivalentTo(["hello", "world"]);
    }

    [Fact]
    public void TableSeparatorRow_ReturnsNoWords()
    {
        var blocks = ParseBlocks("| col1 | col2 |", "| --- | --- |", "| hello | world |");
        var words = MarkdownParser.ExtractCheckableWords("| --- | --- |", blocks[1]);
        words.Should().BeEmpty();
    }

    [Fact]
    public void ThematicBreak_ReturnsNoWords()
    {
        var blocks = ParseBlocks("---");
        var words = MarkdownParser.ExtractCheckableWords("---", blocks[0]);
        words.Should().BeEmpty();
    }

    [Fact]
    public void TaskListItem_SkipsPrefix()
    {
        var text = "- [ ] todo item";
        var parsed = ParseBlocks(text)[0];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        words.Select(w => w.Word).Should().BeEquivalentTo(["todo", "item"]);
    }

    [Fact]
    public void ColorTag_SkipsTagSyntax_ChecksContent()
    {
        var words = ExtractWords("<!--@bg:orange-->nott corect<!--/@bg-->");
        words.Select(w => w.Word).Should().BeEquivalentTo(["nott", "corect"]);
    }

    [Fact]
    public void InlineFgColorTag_SkipsTagSyntax_ChecksContent()
    {
        var words = ExtractWords("<!--@fg:red-->misspeled<!--/@fg--> word");
        words.Select(w => w.Word).Should().BeEquivalentTo(["misspeled", "word"]);
    }

    [Fact]
    public void HtmlComment_IsSkipped()
    {
        var words = ExtractWords("hello <!-- some comment --> world");
        words.Select(w => w.Word).Should().BeEquivalentTo(["hello", "world"]);
    }
}

public class SpellCheckServiceTests
{
    [Fact]
    public void LoadEmbeddedDictionary_Succeeds()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void Check_CorrectWord_ReturnsTrue()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.Check("hello").Should().BeTrue();
        svc.Check("world").Should().BeTrue();
        svc.Check("the").Should().BeTrue();
    }

    [Fact]
    public void Check_MisspelledWord_ReturnsFalse()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.Check("helo").Should().BeFalse();
        svc.Check("wrold").Should().BeFalse();
        svc.Check("teh").Should().BeFalse();
    }

    [Fact]
    public void Check_WhenNotLoaded_ReturnsTrue()
    {
        using var svc = new SpellCheckService();
        svc.Check("anything").Should().BeTrue();
    }

    [Fact]
    public void Suggest_ReturnsRelevantSuggestions()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        var suggestions = svc.Suggest("helo");
        suggestions.Should().NotBeEmpty();
        suggestions.Should().HaveCountLessOrEqualTo(5);
    }

    [Fact]
    public void IgnoreAll_WordPassesCheck()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.Check("xyzzy").Should().BeFalse();
        svc.IgnoreAll("xyzzy");
        svc.Check("xyzzy").Should().BeTrue();
    }

    [Fact]
    public void Check_IsCaseInsensitive_ForCache()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.Check("Hello").Should().BeTrue();
        svc.Check("hello").Should().BeTrue();
    }

    [Fact]
    public void ProjectDictionary_WordPassesCheck()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaisinSpellTest_" + Guid.NewGuid().ToString("N"));
        var markerDir = Path.Combine(dir, ".raisindocs");
        Directory.CreateDirectory(markerDir);
        var dictPath = Path.Combine(markerDir, RaisinDocsPaths.ProjectDictionaryFileName);
        try
        {
            File.WriteAllText(dictPath, "DocsCanvas\nRaisinDocs\n");

            using var svc = new SpellCheckService();
            svc.LoadEmbeddedDictionary();
            svc.Check("DocsCanvas").Should().BeFalse();

            svc.LoadProjectDictionary(dictPath);
            svc.Check("DocsCanvas").Should().BeTrue();
            svc.Check("RaisinDocs").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ProjectDictionary_CommentsAreIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaisinSpellTest_" + Guid.NewGuid().ToString("N"));
        var markerDir = Path.Combine(dir, ".raisindocs");
        Directory.CreateDirectory(markerDir);
        var dictPath = Path.Combine(markerDir, RaisinDocsPaths.ProjectDictionaryFileName);
        try
        {
            File.WriteAllText(dictPath, "# Project terms\nDocsCanvas\n");

            using var svc = new SpellCheckService();
            svc.LoadEmbeddedDictionary();
            svc.LoadProjectDictionary(dictPath);
            svc.Check("DocsCanvas").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ProjectDictionary_NullPath_NoError()
    {
        using var svc = new SpellCheckService();
        svc.LoadEmbeddedDictionary();
        svc.LoadProjectDictionary(null);
        svc.Check("anything").Should().BeTrue();
    }

    [Fact]
    public void ProjectDictionary_NoFile_CreatesOnAdd()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaisinSpellTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dictPath = RaisinDocsPaths.GetProjectDictionaryPath(dir);
        try
        {
            using var svc = new SpellCheckService();
            svc.LoadEmbeddedDictionary();
            svc.LoadProjectDictionary(dictPath);

            svc.Check("DocsCanvas").Should().BeFalse();
            svc.AddToProjectDictionary("DocsCanvas");
            svc.Check("DocsCanvas").Should().BeTrue();
            File.Exists(dictPath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class RaisinDocsPathsTests
{
    [Fact]
    public void FindsRaisindocsMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "docs", "notes");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(root, ".raisindocs"));
        try
        {
            RaisinDocsPaths.FindProjectRoot(sub).Should().Be(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindsGitDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "docs");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            RaisinDocsPaths.FindProjectRoot(sub).Should().Be(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RaisindocsMarkerTakesPriority()
    {
        var gitRoot = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        var markerRoot = Path.Combine(gitRoot, "project");
        var sub = Path.Combine(markerRoot, "docs");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(gitRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(markerRoot, ".raisindocs"));
        try
        {
            RaisinDocsPaths.FindProjectRoot(sub).Should().Be(markerRoot);
        }
        finally
        {
            Directory.Delete(gitRoot, true);
        }
    }

    [Fact]
    public void NoMarker_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            RaisinDocsPaths.FindProjectRoot(dir).Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetProjectFolder_CreatesMarkerDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            RaisinDocsPaths.SetProjectFolder(dir);
            Directory.Exists(Path.Combine(dir, ".raisindocs")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetProjectFolder_MergesNestedMarkerContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        var nestedMarker = Path.Combine(root, "sub", ".raisindocs");
        Directory.CreateDirectory(nestedMarker);
        File.WriteAllText(Path.Combine(nestedMarker, "custom-dictionary.txt"), "DocsCanvas\nRaisinDocs\n");
        try
        {
            RaisinDocsPaths.SetProjectFolder(root);

            var targetDict = Path.Combine(root, ".raisindocs", "custom-dictionary.txt");
            File.Exists(targetDict).Should().BeTrue();
            var words = File.ReadAllLines(targetDict);
            words.Should().Contain("DocsCanvas");
            words.Should().Contain("RaisinDocs");

            Directory.Exists(nestedMarker).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SetProjectFolder_MergesWithExistingDictionary()
    {
        var root = Path.Combine(Path.GetTempPath(), "RaisinRootTest_" + Guid.NewGuid().ToString("N"));
        var rootMarker = Path.Combine(root, ".raisindocs");
        var nestedMarker = Path.Combine(root, "sub", ".raisindocs");
        Directory.CreateDirectory(rootMarker);
        Directory.CreateDirectory(nestedMarker);
        File.WriteAllText(Path.Combine(rootMarker, "custom-dictionary.txt"), "ExistingWord\nDocsCanvas\n");
        File.WriteAllText(Path.Combine(nestedMarker, "custom-dictionary.txt"), "DocsCanvas\nNewWord\n");
        try
        {
            RaisinDocsPaths.SetProjectFolder(root);

            var words = File.ReadAllLines(Path.Combine(rootMarker, "custom-dictionary.txt"));
            words.Should().Contain("ExistingWord");
            words.Should().Contain("DocsCanvas");
            words.Should().Contain("NewWord");

            Directory.Exists(nestedMarker).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
