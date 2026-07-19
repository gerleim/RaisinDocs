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
}
