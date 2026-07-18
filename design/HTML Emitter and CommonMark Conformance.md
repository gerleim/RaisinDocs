# Design: HTML Emitter and CommonMark Conformance

## Context

The `MarkdownParser` produces `ParsedBlock` structures (block classification, inline `StyledRun` lists, table info, color spans) — but has no HTML output path. An HTML emitter is needed for two purposes:

1. **CommonMark conformance testing** — run the official 652-example spec test suite (`spec.json`) against `MarkdownParser` → `HtmlEmitter` and measure pass/fail rate
2. **Chrome extension renderer** — the `renderer.js` in the browser extension (see `Chrome Extension - RaisinDocs Viewer.md`) is a JS port of the same logic; the C# emitter serves as the reference implementation and can also be used server-side

## Project: RaisinDocs.Html

New class library, no WPF dependencies. References only `RaisinDocs` (for `MarkdownParser`, `ParsedBlock`, `StyledRun`, etc.).

### Public API

```csharp
public static class HtmlEmitter
{
    public static string Render(string markdown, HtmlEmitterOptions? options = null);
    public static string Render(IReadOnlyList<ParsedBlock> blocks, HtmlEmitterOptions? options = null);
}

public class HtmlEmitterOptions
{
    public bool IncludeColorExtensions { get; set; } = true;
    public bool Softbreak { get; set; } = true; // \n between blocks → <br> or just \n
}
```

### Output mapping

| ParsedBlock.Kind | HTML |
|---|---|
| Paragraph | `<p>...</p>` |
| H1–H6 | `<h1>`–`<h6>` |
| UnorderedListItem | `<ul><li>...</li></ul>` (group consecutive) |
| OrderedListItem | `<ol><li>...</li></ol>` (group consecutive, `start` attr) |
| TaskListItem | `<ul><li><input type="checkbox" disabled> ...</li></ul>` |
| FencedCode | `<pre><code class="language-X">...</code></pre>` |
| IndentedCode | `<pre><code>...</code></pre>` |
| BlockQuote | `<blockquote><p>...</p></blockquote>` |
| ThematicBreak | `<hr />` |
| Table | `<table><thead>...</thead><tbody>...</tbody></table>` |
| LinkDefinition | (consumed by reference resolution, no output) |

| StyledRun.Style | HTML |
|---|---|
| Bold | `<strong>` |
| Italic | `<em>` |
| BoldItalic | `<em><strong>` |
| Code | `<code>` |
| Strikethrough | `<del>` |
| Link | `<a href="..." title="...">` |
| Image | `<img src="..." alt="..." title="..." />` |

| Color extension | HTML |
|---|---|
| Inline `<!--@fg:red-->` | `<span style="color: red">` |
| Block `<!--@div fg:red-->` | `<div style="color: red">` |

### Grouping logic

The emitter must handle list grouping (consecutive list items → single `<ul>`/`<ol>`), which `MarkdownParser` doesn't do (it classifies per-block). The emitter walks the block list and groups:
- Adjacent `UnorderedListItem` blocks → one `<ul>`
- Adjacent `OrderedListItem` blocks → one `<ol>` (with `start` attribute from first item's number)
- Adjacent `TaskListItem` blocks → one `<ul class="task-list">`

Table grouping is already handled by `MarkdownParser.DetectTables` → `TableInfo`.

## Project: RaisinDocs.Tests.Conformance

xUnit test project that validates against the official CommonMark spec.

### Setup

1. Include `spec.json` from CommonMark 0.31.2 as an embedded resource or content file
2. Deserialize into test case objects: `{ example, section, markdown, html }`
3. Run as `[Theory]` with `[MemberData]`

### Test structure

```csharp
[Theory]
[MemberData(nameof(SpecExamples))]
public void CommonMark_Example(int example, string section, string markdown, string expectedHtml)
{
    var actual = HtmlEmitter.Render(markdown);
    Assert.Equal(expectedHtml, actual);
}
```

### Expected failures

Some sections will fail by design (features we don't implement). These should be tracked explicitly:

| Section | Reason | Action |
|---|---|---|
| 4.6 HTML blocks | Intentionally unsupported | `[Skip]` with reason |
| 6.6 Raw HTML | Intentionally unsupported | `[Skip]` with reason |
| 2.5 Entity references | Intentionally unsupported | `[Skip]` with reason |
| Nested containers | Not yet implemented | Track as known failures |
| Underscore emphasis | Not yet implemented | Track as known failures |

### Reporting

The test run produces:
- Total examples: 652
- Passing: N
- Failing (known/deferred): M
- Failing (unexpected): K

Goal: drive unexpected failures to zero. Known/deferred failures are tracked in a skip list with issue references.

## Relationship to Chrome extension

The C# `HtmlEmitter` is the **reference implementation**. The JS `renderer.js` in the Chrome extension should produce identical output for the same input. Validation:

1. Run `spec.json` through both C# and JS emitters
2. Diff outputs — they must match (excluding color extension tests which are project-specific)

This ensures the Chrome extension renders the same as the conformance-tested C# path.

## Implementation order

1. Create `RaisinDocs.Html` project with `HtmlEmitter` — inline rendering first (emphasis, code, links)
2. Add block-level rendering (paragraphs, headings, lists, code blocks, tables)
3. Add list grouping logic
4. Create `RaisinDocs.Tests.Conformance`, download `spec.json`, wire up the theory
5. Run suite, categorize failures, iterate on parser + emitter
6. Add color extension output (behind `HtmlEmitterOptions` flag — disabled for conformance tests)

## CommonMark gaps to close

Gaps that cause conformance failures and should be fixed in `MarkdownParser`:

| Gap | Priority | Effort |
|---|---|---|
| Underscore emphasis (`_`, `__`) | High | Medium — extend delimiter scanner |
| Tilde fenced code (`~~~`) | Medium | Low — add to `GetFenceInfo` |
| General backslash escapes (`\*`, `\[`, etc.) | High | Medium — new inline pass |
| Two-trailing-spaces hard breaks | Medium | Low — extend `IsTrailingHardBreak` |
| Closing `#` on ATX headings | Low | Low — strip in `ClassifyBlock` |
| Shortcut reference links (`[label]`) | Medium | Low — extend ref link resolution |
| Nested containers | High | High — requires recursive block parsing |
