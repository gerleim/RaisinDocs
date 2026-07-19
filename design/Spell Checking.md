# Spell Checking — Design

## Overview

Spell checking for RaisinDocs using **WeCantSpell.Hunspell** (pure managed .NET Hunspell port). Red squiggly underlines on misspelled words, right-click suggestions, custom user dictionary. Markdown-aware — skips code, URLs, image paths, HTML tags.

## Package

- **WeCantSpell.Hunspell** (NuGet) — actively maintained, .NET 8 compatible, no native dependencies
- Bundle `en_US.dic` + `en_US.aff` from LibreOffice dictionaries (~2 MB) as embedded resources or content files in `RaisinDocs/Dictionaries/`

## Architecture

```
Document (text changes)
    ↓ block index
MarkdownParser (already parsed — reuse ParsedBlock)
    ↓ styled runs, code spans, links, images
SpellChecker.ExtractWords() — yields checkable words with raw offsets
    ↓
WordList.Check(word) — Hunspell lookup
    ↓
SpellingResult[] stored per block on ParsedBlock
    ↓
DocsCanvas.OnRender — draw squiggly underlines
```

Fits the existing data flow: `Document → MarkdownParser → DocsCanvas.OnRender`. Spell results attach to `ParsedBlock` alongside existing `StyledRuns`, `ColorSpans`, etc.

## Key Types

### SpellCheckService

New file: `SpellCheckService.cs` in the RaisinDocs project.

```csharp
internal sealed class SpellCheckService : IDisposable
{
    private WordList _wordList;                          // WeCantSpell.Hunspell
    private readonly HashSet<string> _userDictionary;   // persisted custom words
    private readonly HashSet<string> _sessionIgnores;   // "Ignore All" this session

    public bool IsEnabled { get; set; }

    public bool Check(string word);                     // true = correctly spelled
    public IReadOnlyList<string> Suggest(string word);  // ranked suggestions
    public void AddToUserDictionary(string word);       // persists to file
    public void IgnoreAll(string word);                 // session-only
    public void LoadDictionary(string language);        // e.g. "en_US"
}
```

Owned by `DocsCanvas`. Single instance, created lazily on first use.

### SpellingError

```csharp
internal readonly record struct SpellingError(int StartOffset, int Length, string Word);
```

Stored as `IReadOnlyList<SpellingError>` on `ParsedBlock`. Offsets are raw (into the block's text), same coordinate space as `StyledRun`.

### ParsedBlock addition

```csharp
// Add to existing ParsedBlock record class:
public IReadOnlyList<SpellingError> SpellingErrors { get; init; }
    = Array.Empty<SpellingError>();
```

Using `init` + `with` expressions, consistent with existing `ParsedBlock` pattern.

## Word Extraction

New static method on `MarkdownParser` (or a small helper class):

```csharp
internal static IEnumerable<(int offset, string word)>
    ExtractCheckableWords(string text, ParsedBlock parsed)
```

Walks the block text character by character, splits on whitespace/punctuation to find word boundaries. **Skips** ranges that overlap with:

- Code spans (`StyledRun` with `Style == Code`)
- Fenced code blocks (`BlockKind.FencedCode*`)
- Link/image URLs (from `InlineLinks` / `InlineImages` — URL portion only, check the display text)
- HTML tags (from `HtmlRanges` if available, or raw `<...>` spans)
- Color tag syntax (`<!--@fg:...-->` etc.)
- Heading prefix characters (`#`, spaces)
- Block quote prefixes (`>`)
- List markers (`-`, `*`, `1.`)

Most of these ranges are already identified by `MarkdownParser` on the `ParsedBlock`. Build a sorted "skip ranges" list, then iterate words skipping any that fall inside.

Also skip:
- Words that are ALL CAPS and ≤ 5 chars (likely acronyms: API, URL, HTML, WPF)
- Words containing digits (variable123, H1, etc.)
- Words starting with `@` or `#` (mentions, tags)
- File paths (contains `/` or `\` or `.ext` patterns)

## Incremental Checking

Full document re-check on every keystroke is wasteful. Strategy:

1. **Dirty tracking**: when `Document` reports a change, mark affected block indices as dirty.
2. **Debounced recheck**: after a short delay (300ms idle after last keystroke), recheck only dirty blocks.
3. **Cache**: `SpellCheckService` maintains a `Dictionary<string, bool>` word→valid cache. Hunspell lookups are ~1μs each but caching avoids repeated work for common words.
4. **Trigger**: use a `DispatcherTimer` at low priority (`DispatcherPriority.ApplicationIdle`) so checking never blocks typing.

```csharp
// In DocsCanvas:
private readonly HashSet<int> _dirtySpellBlocks = new();
private DispatcherTimer? _spellCheckTimer;

// On text change:
_dirtySpellBlocks.Add(blockIndex);
_spellCheckTimer?.Stop();
_spellCheckTimer?.Start();  // restart 300ms debounce

// On timer tick:
private void SpellCheckTick(object? sender, EventArgs e)
{
    _spellCheckTimer!.Stop();
    foreach (var idx in _dirtySpellBlocks)
        RecheckBlock(idx);
    _dirtySpellBlocks.Clear();
    InvalidateVisual();  // redraw squigglies
}
```

## Rendering — Squiggly Underlines

In `DocsCanvas.OnRender`, after drawing text and selection, draw squiggly underlines for each `SpellingError` on visible blocks.

```csharp
private void DrawSpellingErrors(DrawingContext dc, /* block info */)
{
    foreach (var error in parsed.SpellingErrors)
    {
        // Map error.StartOffset / Length to pixel X range
        // (source mode: direct; visual mode: through BlockVisualMap)
        double x1 = /* start X */;
        double x2 = /* end X */;
        double y = /* baseline Y + descent + 1px */;

        DrawSquigglyLine(dc, x1, x2, y, _spellErrorPen);
    }
}
```

**Squiggly line geometry**: a series of small arcs or a polyline zigzagging ±1.5px over 3px wavelength. Pre-build a reusable `PathGeometry` or use `StreamGeometry` for performance.

```csharp
private static void DrawSquigglyLine(DrawingContext dc, double x1, double x2,
    double y, Pen pen)
{
    var geometry = new StreamGeometry();
    using (var ctx = geometry.Open())
    {
        ctx.BeginFigure(new Point(x1, y), false, false);
        double x = x1;
        bool up = true;
        while (x < x2)
        {
            x = Math.Min(x + 3, x2);
            ctx.LineTo(new Point(x, y + (up ? -1.5 : 1.5)), true, false);
            up = !up;
        }
    }
    geometry.Freeze();
    dc.DrawGeometry(null, pen, geometry);
}
```

Pen: red, 0.75px, frozen. Cached as a field — one allocation.

## Right-Click Context Menu

When the user right-clicks on a misspelled word:

1. Hit-test to find the block and character offset.
2. Check if offset falls within any `SpellingError` range on that block.
3. If yes, build a `ContextMenu` with:
   - Top N suggestions (max 5) as menu items — clicking replaces the word
   - Separator
   - "Ignore All" — adds to session ignores, rechecks
   - "Add to Dictionary" — adds to user dictionary file, rechecks

```
┌──────────────────┐
│  should           │  ← suggestion (replaces "sould")
│  could            │
│  sold             │
│───────────────────│
│  Ignore All       │
│  Add to Dictionary│
└──────────────────┘
```

If right-click is NOT on a misspelled word, show the normal context menu (if any).

## User Dictionary Persistence

Store at `%APPDATA%/Raisin/RaisinDocs/user-dictionary.txt` — one word per line, UTF-8, sorted.

- Loaded into `HashSet<string>` on startup (case-insensitive)
- `AddToUserDictionary` appends to file and adds to the set
- Checked before Hunspell — if the word is in the user dictionary, it's valid

## DocsCanvas Integration

New dependency property on `DocsCanvas`:

```csharp
public bool SpellCheckEnabled
{
    get => (bool)GetValue(SpellCheckEnabledProperty);
    set => SetValue(SpellCheckEnabledProperty, value);
}
```

Exposed through `DocsEditor` and persisted in `DocsEditorState`.

## What NOT to Check

| Block kind | Check? |
|---|---|
| Paragraph, heading, list item, task list, blockquote | Yes — check the text content |
| Fenced code block | No |
| Indented code block | No |
| HTML block | No |
| Link definition line | No |
| Table cell | Yes — check cell text |

Within checkable blocks, skip the ranges listed in Word Extraction above.

## Phases

### Phase 1: Core service + word extraction ✅
- Add WeCantSpell.Hunspell NuGet package
- Bundle en_US dictionary files
- Implement `SpellCheckService` (Check, Suggest, Dispose)
- Implement `ExtractCheckableWords` on `MarkdownParser`
- Add `SpellingErrors` to `ParsedBlock`
- Unit tests in `RaisinDocs.Tests`: word extraction skips code/URLs/syntax, Check/Suggest work

### Phase 2: Rendering + incremental recheck ✅
- Dirty block tracking + debounced recheck timer (`DispatcherPriority.ApplicationIdle`, 300ms debounce)
- Squiggly underline drawing in `OnRender` via `StreamGeometry` zigzag
- Visual mode offset mapping through `BlockVisualMap` (via `MeasureRangeWidth`)
- Paragraph group (joined line) support via `SourceToJoined` + `MeasureJoinedRange`
- Table cell support via `CursorXInTableRow`
- `SpellCheckEnabled` property on `DocsCanvas` (field+setter pattern)
- `DocsEditorState` serialization, `DocsEditor` GetState/ApplyState integration
- New partial class: `DocsCanvas.SpellCheck.cs`

### Phase 3: Context menu + user dictionary
- Right-click hit-testing against `SpellingError` ranges
- Context menu with suggestions, Ignore All, Add to Dictionary
- User dictionary file persistence
- `DocsEditorState` serialization

### Phase 4: Polish
- Performance tuning: word cache size, recheck batch limits
- Multi-language support (detect or configure language, load matching dictionary)
- Formatting bar toggle button for spell check on/off
