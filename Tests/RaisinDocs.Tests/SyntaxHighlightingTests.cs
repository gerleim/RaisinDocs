using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class SyntaxHighlightingTests
{
    [Fact]
    public void Parse_ExtractsCodeLanguage_FromFenceInfoString()
    {
        var blocks = MarkdownParser.Parse(i => new[] {
            "```csharp",
            "public class Foo { }",
            "```",
        }[i], 3);

        blocks[0].CodeLanguage.Should().Be("csharp");
        blocks[0].IsFenceDelimiter.Should().BeTrue();
        blocks[1].CodeLanguage.Should().Be("csharp");
        blocks[2].CodeLanguage.Should().Be("csharp");
        blocks[2].IsFenceDelimiter.Should().BeTrue();
    }

    [Fact]
    public void Parse_NullLanguage_WhenNoInfoString()
    {
        var blocks = MarkdownParser.Parse(i => new[] {
            "```",
            "plain code",
            "```",
        }[i], 3);

        blocks[0].CodeLanguage.Should().BeNull();
        blocks[1].CodeLanguage.Should().BeNull();
    }

    [Fact]
    public void GetFenceInfo_ExtractsLanguage()
    {
        var (count, lang, _) = MarkdownParser.GetFenceInfo("```csharp");
        count.Should().Be(3);
        lang.Should().Be("csharp");
    }

    [Fact]
    public void GetFenceInfo_TrimsWhitespace()
    {
        var (count, lang, _) = MarkdownParser.GetFenceInfo("```  python  ");
        count.Should().Be(3);
        lang.Should().Be("python");
    }

    [Fact]
    public void GetFenceInfo_NullLanguage_WhenEmpty()
    {
        var (count, lang, _) = MarkdownParser.GetFenceInfo("```");
        count.Should().Be(3);
        lang.Should().BeNull();
    }

    [Fact]
    public void GetFenceInfo_RejectsBacktickInInfoString()
    {
        var (count, _, _) = MarkdownParser.GetFenceInfo("```foo`bar");
        count.Should().Be(0);
    }

    [Fact]
    public void SyntaxHighlighter_TokenizesCSharp()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var lines = new[] { "public static void Main()" };
        var result = highlighter.Tokenize("csharp", lines);

        result.Should().NotBeNull();
        result!.Length.Should().Be(1);
        result[0].Should().NotBeEmpty();
    }

    [Fact]
    public void SyntaxHighlighter_ReturnsNull_ForUnknownLanguage()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var result = highlighter.Tokenize("nonexistent_language_xyz", new[] { "hello" });

        result.Should().BeNull();
    }

    [Fact]
    public void SyntaxHighlighter_TokensHaveValidOffsets()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var line = "int x = 42;";
        var result = highlighter.Tokenize("csharp", new[] { line });

        result.Should().NotBeNull();
        foreach (var token in result![0])
        {
            token.Start.Should().BeGreaterThanOrEqualTo(0);
            (token.Start + token.Length).Should().BeLessThanOrEqualTo(line.Length);
            token.ForegroundArgb.Should().NotBe(0);
        }
    }

    [Fact]
    public void SyntaxHighlighter_HandlesEmptyLines()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var result = highlighter.Tokenize("csharp", new[] { "", "int x;", "" });

        result.Should().NotBeNull();
        result!.Length.Should().Be(3);
        result[0].Should().BeEmpty();
        result[1].Should().NotBeEmpty();
        result[2].Should().BeEmpty();
    }

    [Fact]
    public void SyntaxHighlighter_MultilineState_TracksBlockComments()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var result = highlighter.Tokenize("csharp", new[] {
            "/* start",
            "middle",
            "end */",
            "int x;",
        });

        result.Should().NotBeNull();
        // "middle" line should have tokens (comment continuation)
        result![1].Should().NotBeEmpty();
        // "int x;" should have different coloring than comment lines
        var commentColor = result[1][0].ForegroundArgb;
        var codeTokens = result[3];
        codeTokens.Should().NotBeEmpty();
        codeTokens.Should().Contain(t => t.ForegroundArgb != commentColor);
    }

    [Fact]
    public void Parse_WithHighlighter_AttachesSyntaxTokens()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var lines = new[] {
            "```csharp",
            "public class Foo { }",
            "```",
        };
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Length, highlighter);

        blocks[0].SyntaxTokens.Should().BeNull();
        blocks[1].SyntaxTokens.Should().NotBeNull();
        blocks[1].SyntaxTokens!.Count.Should().BeGreaterThan(0);
        blocks[2].SyntaxTokens.Should().BeNull();
    }

    [Fact]
    public void Parse_WithHighlighter_AttachesSyntaxTokens_AllContentLines()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var lines = new[] {
            "```c#",
            "public void One()",
            "public void Two()",
            "```",
        };
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Length, highlighter);

        blocks[1].SyntaxTokens.Should().NotBeNull("line 1 should have syntax tokens");
        blocks[1].SyntaxTokens!.Count.Should().BeGreaterThan(0, "line 1 should have coloring");
        blocks[2].SyntaxTokens.Should().NotBeNull("line 2 should have syntax tokens");
        blocks[2].SyntaxTokens!.Count.Should().BeGreaterThan(0, "line 2 should have coloring");
    }

    [Fact]
    public void SyntaxHighlighter_Tokenize_MultipleLines_AllGetTokens()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var result = highlighter.Tokenize("c#", new[] {
            "public void One()",
            "public void Two()",
        });

        result.Should().NotBeNull();
        result!.Length.Should().Be(2);
        result[0].Should().NotBeEmpty("line 1 should have tokens");
        result[1].Should().NotBeEmpty("line 2 should have tokens");
    }

    [Fact]
    public void SyntaxHighlighter_SingleLine_Gets_Tokens()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);

        // Each line tokenized independently (fresh ruleStack)
        var r1 = highlighter.Tokenize("c#", new[] { "public void One()" });
        var r2 = highlighter.Tokenize("c#", new[] { "public void Two()" });

        r1.Should().NotBeNull();
        r2.Should().NotBeNull();
        r1![0].Should().NotBeEmpty("One() alone should have tokens");
        r2![0].Should().NotBeEmpty("Two() alone should have tokens");
    }

    [Fact]
    public void Parse_WithHighlighter_NoTokens_WhenNoLanguage()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var lines = new[] {
            "```",
            "plain code",
            "```",
        };
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Length, highlighter);

        blocks[1].SyntaxTokens.Should().BeNull();
    }

    [Fact]
    public void Parse_WithHighlighter_NoTokens_WhenUnknownLanguage()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);
        var lines = new[] {
            "```unknownlang",
            "some code",
            "```",
        };
        var blocks = MarkdownParser.Parse(i => lines[i], lines.Length, highlighter);

        blocks[1].SyntaxTokens.Should().BeNull();
    }

    [Fact]
    public void SyntaxHighlighter_LanguageAliases_Work()
    {
        var highlighter = new SyntaxHighlighter(TextMateSharp.Grammars.ThemeName.DarkPlus);

        highlighter.Tokenize("cs", new[] { "int x;" }).Should().NotBeNull();
        highlighter.Tokenize("js", new[] { "let x;" }).Should().NotBeNull();
        highlighter.Tokenize("py", new[] { "x = 1" }).Should().NotBeNull();
        highlighter.Tokenize("ts", new[] { "let x: number;" }).Should().NotBeNull();
        highlighter.Tokenize("rs", new[] { "let x = 1;" }).Should().NotBeNull();
    }
}
