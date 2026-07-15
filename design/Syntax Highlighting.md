# Syntax Highlighting for Fenced Code Blocks

## Goal

Add syntax highlighting to fenced code blocks in both source and visual mode. Use TextMate grammars (the VS Code / Sublime standard) via the TextMateSharp NuGet package so one engine covers all languages with community-maintained, MIT-licensed grammar files.

## Current state

- `MarkdownParser` classifies fenced code block lines as `BlockKind.FencedCodeLine` with a single `StyledRun(0, text.Length, InlineStyle.Normal)` — no per-token information.
- The opening fence's info string (language identifier) is parsed by `GetFenceBacktickCount` but **discarded** — never stored on `ParsedBlock`.
- `ApplyInlineStyles` explicitly skips `FencedCodeLine` blocks (line 1492).
- Code blocks render as plain monospace text (Cascadia Mono, 14px) on a tinted background. No syntax coloring.

## Approach: TextMateSharp

### Why TextMateSharp

- MIT-licensed .NET port of VS Code's `vscode-textmate` engine.
- NuGet: `TextMateSharp` (core engine) + `TextMateSharp.Grammars` (bundled grammars + themes).
- `TextMateSharp.Grammars` bundles 50+ language grammars (C#, JS, TS, Python, Rust, Go, Java, SQL, HTML, CSS, YAML, JSON, PowerShell, Bash, etc.) and 20+ themes (Dark+, Light+, Monokai, Solarized, etc.) as embedded resources.
- Line-by-line tokenizer API returns token spans with scope names; a theme engine maps scopes to colors via prefix matching.
- Handles the Oniguruma regex dialect that TextMate grammars require.

### API usage

```csharp
// Setup (once, at app startup or first code block encounter)
var options = new RegistryOptions(ThemeName.DarkPlus);
var registry = new Registry(options);

// Load grammar for a language
IGrammar grammar = registry.LoadGrammar(
    options.GetScopeByExtension(".cs"));

// Tokenize line by line, carrying state across lines
IStateStack? ruleStack = null;
foreach (string line in codeBlockLines)
{
    var result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);
    ruleStack = result.RuleStack;

    foreach (var token in result.Tokens)
    {
        // token.StartIndex, token.EndIndex — char offsets in the line
        // token.Scopes — e.g. ["source.cs", "keyword.control.cs"]
    }
}
```

### Theme mapping

TextMateSharp includes a `Theme` class that resolves scope stacks to colors via prefix matching:

```csharp
Theme theme = registry.GetTheme();
foreach (var themeRule in theme.Match(token.Scopes))
{
    string hexFg = theme.GetColor(themeRule.foreground); // "#C586C0"
}
```

VS Code themes define rules like `{ scope: "keyword.control", foreground: "#C586C0" }`. The selector `"keyword"` matches `keyword.control.if.cs`, `keyword.operator.assignment.cs`, etc. More-specific prefixes win.

## Architecture

### Language identifier extraction

Modify `MarkdownParser.GetFenceBacktickCount` to return the info string (language identifier). Add a `string? CodeLanguage` property to `ParsedBlock`. During `Parse`, when an opening fence is encountered, extract the language from the info string and propagate it to all `FencedCodeLine` blocks within that fence.

Common info string values and their mappings:
- `csharp`, `cs`, `c#` → `.cs`
- `javascript`, `js` → `.js`
- `typescript`, `ts` → `.ts`
- `python`, `py` → `.py`
- `rust`, `rs` → `.rs`
- `json`, `yaml`, `xml`, `html`, `css`, `sql`, `bash`, `sh`, `powershell`, `ps1`, `go`, `java`, `cpp`, `c`

`TextMateSharp.Grammars.RegistryOptions.GetScopeByExtension()` handles extension-to-scope mapping. We need a small lookup table from info-string aliases to file extensions.

### Syntax token storage

Add to `ParsedBlock`:

```csharp
public IReadOnlyList<SyntaxToken>? SyntaxTokens { get; init; }
```

Where:

```csharp
public readonly record struct SyntaxToken(int Start, int Length, int ForegroundColor);
```

`ForegroundColor` is a packed ARGB int (from the resolved theme color). This avoids storing scope strings per token — the theme resolution happens during tokenization, not during rendering.

### Tokenization pipeline

Create a new class `SyntaxHighlighter` that encapsulates the TextMateSharp registry and grammar loading:

```csharp
internal class SyntaxHighlighter
{
    private readonly Registry _registry;
    private readonly RegistryOptions _options;
    private readonly Dictionary<string, IGrammar?> _grammarCache = new();
    private Theme _theme;

    public SyntaxHighlighter(ThemeName themeName) { ... }

    public void SetTheme(ThemeName themeName) { ... }

    public List<SyntaxToken>[]? Tokenize(string language, List<string> lines)
    {
        // Returns one List<SyntaxToken> per line, or null if language unknown.
        // Tokenizes all lines in sequence, carrying ruleStack across lines.
    }
}
```

### Integration point: MarkdownParser.Parse

After the existing parse pass classifies blocks, a post-pass (similar to `DetectTables`) groups consecutive `FencedCodeLine` blocks by their fence (using `IsFenceDelimiter` to find boundaries), tokenizes each group as a unit, and attaches `SyntaxTokens` to each `ParsedBlock` via `with` expressions.

This keeps tokenization in the parser layer, not in the rendering layer — consistent with the existing `Document → MarkdownParser → BlockVisualMap → DocsCanvas.OnRender` pipeline.

### Integration point: DocsCanvas rendering

In `ApplyInlineStyles` (source mode), after the existing skip for code blocks, add a new path:

```csharp
if (parsed.Kind is BlockKind.FencedCodeLine && parsed.SyntaxTokens != null)
{
    foreach (var token in parsed.SyntaxTokens)
    {
        int localStart = Math.Max(0, token.Start - vl.StartOffset);
        int localEnd = Math.Min(vl.Length, token.Start + token.Length - vl.StartOffset);
        int count = localEnd - localStart;
        if (count > 0)
        {
            var brush = GetOrCreateBrush(token.ForegroundColor);
            ft.SetForegroundBrush(brush, localStart, count);
        }
    }
}
```

Same pattern applies to `ApplyInlineStylesVisual` in visual mode. In visual mode, fence delimiters are hidden (`IsSkippedInVisual`), so only the content lines render — the tokens align with raw text offsets, and `BlockVisualMap` is not involved for code blocks (they have no hidden ranges within the line).

### Brush caching

Create a small `Dictionary<int, Brush>` cache for ARGB-to-frozen-Brush lookup. Syntax themes typically use 15–30 distinct colors, so this cache stays tiny.

### Theme synchronization

When the editor theme changes (`OnThemePropertyChanged`), map the `EditorTheme` to a TextMateSharp `ThemeName`:
- `EditorTheme.Dark` → `ThemeName.DarkPlus`
- `EditorTheme.DarkBlue` → `ThemeName.DarkPlus` (or a closer match)
- `EditorTheme.Light` → `ThemeName.LightPlus`

Call `SyntaxHighlighter.SetTheme()`, clear cached `SyntaxTokens`, and trigger re-parse (`InvalidateLayout`).

### Fence delimiter dimming

In source mode, the opening/closing fence lines (`IsFenceDelimiter = true`) should be dimmed with `_palette.Syntax` as they are structural markdown syntax. The language identifier on the opening fence could remain un-dimmed or receive a distinct color.

## Performance

### Tokenization cost

TextMateSharp tokenizes ~10,000 lines of C# in ~100–200ms on modern hardware. For a markdown editor where code blocks are typically 5–50 lines, this is negligible.

### When to tokenize

- **On parse**: tokenize during `MarkdownParser.Parse` post-pass, same as table detection.
- **Incremental**: when a single line within a code block changes, ideally only re-tokenize from that line forward (since earlier lines' state is unchanged). For initial implementation, re-tokenize the entire code block on any change — this is fast enough for typical block sizes.
- **Lazy**: defer tokenization until the code block is visible in the viewport. For initial implementation, tokenize all code blocks eagerly during parse — optimize later if needed.

### Memory

Each `SyntaxToken` is 12 bytes (two ints + one int). A 50-line code block averaging 20 tokens per line = 1000 tokens = 12 KB. Negligible.

## Files to modify/create

### New files

1. **`RaisinDocs/SyntaxHighlighter.cs`** (~150 lines) — wraps TextMateSharp registry, grammar loading, theme management, tokenization API.

2. **`RaisinDocs/SyntaxToken.cs`** — `readonly record struct SyntaxToken(int Start, int Length, int ForegroundColor)` and the info-string-to-extension alias map.

### Modified files

3. **`RaisinDocs/MarkdownParser.cs`**
   - `GetFenceBacktickCount`: return info string alongside backtick count.
   - `Parse`: extract language from opening fence, store on all `FencedCodeLine` blocks as `CodeLanguage`.
   - Post-pass: call `SyntaxHighlighter.Tokenize()` and attach `SyntaxTokens` to each code block.
   - `ParsedBlock`: add `string? CodeLanguage` and `IReadOnlyList<SyntaxToken>? SyntaxTokens` properties.

4. **`RaisinDocs/DocsCanvas.cs`**
   - Instantiate `SyntaxHighlighter` (lazily, on first code block encounter).
   - On theme change: update highlighter theme, invalidate layout.
   - `ApplyInlineStyles`: add syntax token rendering path for `FencedCodeLine` blocks.
   - Brush cache for token colors.

5. **`RaisinDocs/DocsCanvas.VisualMode.cs`**
   - `ApplyInlineStylesVisual`: same syntax token rendering path.

6. **`RaisinDocs.csproj`**
   - Add NuGet references: `TextMateSharp`, `TextMateSharp.Grammars`.

### Tests

7. **`Tests/RaisinDocs.Tests/SyntaxHighlightingTests.cs`**
   - Verify `ParsedBlock.CodeLanguage` extraction from fence info strings.
   - Verify `SyntaxToken` lists are populated for known languages.
   - Verify empty/null tokens for unknown languages.
   - Verify theme switching clears and regenerates tokens.

## Language alias table

```
csharp, cs, c#              → .cs
javascript, js              → .js
typescript, ts              → .ts
python, py                  → .py
json, jsonc                 → .json
xml                         → .xml
html, htm                   → .html
css                         → .css
yaml, yml                   → .yaml
sql                         → .sql
rust, rs                    → .rs
go, golang                  → .go
java                        → .java
cpp, c++                    → .cpp
c                           → .c
bash, sh, shell, zsh        → .sh
powershell, ps1, pwsh       → .ps1
ruby, rb                    → .rb
php                         → .php
swift                       → .swift
kotlin, kt                  → .kt
markdown, md                → .md
dockerfile                  → Dockerfile
toml                        → .toml
ini, conf                   → .ini
lua                         → .lua
r                           → .r
scala                       → .scala
fsharp, fs, f#              → .fs
```

Unmapped info strings: render as plain monospace (current behavior). No error, no warning.

## Scope of initial implementation

1. Extract and store `CodeLanguage` from fence info strings.
2. Add `TextMateSharp` + `TextMateSharp.Grammars` NuGet packages.
3. Implement `SyntaxHighlighter` with grammar caching and theme mapping.
4. Tokenize code blocks during parse and store `SyntaxTokens` on `ParsedBlock`.
5. Render syntax colors in both source and visual mode via `SetForegroundBrush`.
6. Map editor themes to TextMateSharp themes.
7. Dim fence delimiter lines in source mode.

### Deferred

- Custom theme support (user-provided `.tmTheme` files).
- Incremental re-tokenization (only lines after the edit).
- Grammar injection (e.g., CSS inside HTML).
- User-configurable language alias overrides.
