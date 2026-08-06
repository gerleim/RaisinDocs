# HTML Break Semantics and Paragraph Handling

**Status**: Architecture Documentation  
**Date**: 2026-08-06  
**Applies to**: HtmlBlockModelParser HTML-to-Markdown conversion

---

## Executive Summary

The markdown standard distinguishes three types of **breaks**:

1. **Soft breaks** — Whitespace/newlines within inline content (rendered as space)
2. **Hard breaks** — Explicit line breaks within a block (`<br>` in HTML, `\` in Markdown)
3. **Block breaks** — Paragraph separation (blank line in Markdown, adjacent `<p>` tags in HTML)

This document specifies how HtmlBlockModelParser handles each, and how settings control output.

---

## Part 1: Soft Breaks (Whitespace Within Blocks)

### What They Are

In HTML, whitespace (newlines, tabs, multiple spaces) inside a block element is often just formatting:

```html
<p>This is a paragraph
   spanning multiple lines
   for readability.</p>
```

**In a browser**, this renders as:

```
This is a paragraph spanning multiple lines for readability.
```

Newlines and extra spaces collapse to single spaces.

### Markdown Representation

Markdown follows the same rule. In **Relaxed mode** (default):

```markdown
This is a paragraph
   spanning multiple lines
   for readability.
```

Renders identically to:

```markdown
This is a paragraph spanning multiple lines for readability.
```

### Two Modes

**SoftBreakMode.Relaxed** (default):
```
Input:  "text\n   more\n  text"
Output: "text more text"
Rule:   Collapse multiple spaces/newlines to single space
```

**SoftBreakMode.Strict**:
```
Input:  "text\n   more\n  text"
Output: "text\n more\n text"  (or with normalized spacing)
Rule:   Preserve line structure, but normalize internal whitespace
```

### Implementation

When parsing inline content in `ParseInlineContent()`:
- Accumulate text character-by-character
- When flushing segments, call `NormalizeWhitespace(text, softBreakMode)`
- Pass the `SoftBreakMode` setting through the pipeline

---

## Part 2: Hard Breaks (Explicit Line Breaks)

### What They Are

In HTML, `<br>` is an explicit line break:

```html
<p>Line 1<br>
Line 2<br>
Line 3</p>
```

**In a browser**, this renders as three separate lines (but still within one paragraph).

### Markdown Representation

Markdown represents hard breaks in two ways:

**HardBreakStyle.Backslash**:
```markdown
Line 1\
Line 2\
Line 3
```

**HardBreakStyle.TrailingSpaces** (two spaces):
```markdown
Line 1  
Line 2  
Line 3
```

Both render identically in a markdown viewer.

### Implementation

When parsing inline content:
1. Detect `<br>` tags in `ParseInlineContent()`
2. Mark the segment with `FollowedByHardBreak = true`
3. In `FormatParagraph()`, when encountered:
   - Append `\` (Backslash mode) or `  ` (TrailingSpaces mode)
   - Output current line and start new line

**Current state**: ✅ Already implemented

---

## Part 3: Paragraph Separation (Block Breaks)

### The CommonMark Rule

In Markdown, a **blank line** (two consecutive newlines) separates paragraphs:

```markdown
Paragraph 1

Paragraph 2
```

This is **two** paragraphs.

Without a blank line:
```markdown
Paragraph 1
Paragraph 2
```

This is **also** two paragraphs (blank line optional in CommonMark).

### HTML Representation

In HTML, each paragraph is a separate `<p>` tag:

```html
<p>Paragraph 1</p>
<p>Paragraph 2</p>
```

### Markdown Representation

To properly separate paragraphs in markdown output, we need:

```markdown
Paragraph 1

Paragraph 2
```

### Implementation

When converting blocks to markdown:
1. Each `<p>` tag becomes `BlockKind.Paragraph`
2. When outputting consecutive paragraph blocks, insert blank line between them
3. **Rule**: Add blank line ONLY between paragraph blocks (not after every block)

**Current state**: ⚠️ **Not implemented** — we removed all inter-block blank lines, which breaks paragraph separation

---

## Part 4: Specification

### Data Structures

```csharp
/// <summary>Settings for markdown output format</summary>
internal class MarkdownOutputSettings
{
    /// <summary>How to represent hard line breaks from <br> tags</summary>
    public HardBreakStyle HardBreak { get; set; } = HardBreakStyle.Backslash;

    /// <summary>How to handle soft breaks (newlines in HTML content)</summary>
    public SoftBreakMode SoftBreak { get; set; } = SoftBreakMode.Relaxed;

    /// <summary>Preserve color tags in output</summary>
    public bool PreserveColors { get; set; } = true;
}

/// <summary>How to represent hard line breaks (from <br>)</summary>
internal enum HardBreakStyle
{
    /// <summary>Line 1\ + newline</summary>
    Backslash,

    /// <summary>Line 1  + newline (two spaces)</summary>
    TrailingSpaces,
}

/// <summary>How to handle soft breaks (newlines in HTML)</summary>
internal enum SoftBreakMode
{
    /// <summary>Collapse whitespace to single space (default, matches browser behavior)</summary>
    Relaxed,

    /// <summary>Preserve newlines and line structure</summary>
    Strict,
}
```

### Pipeline Changes

**Stage 2 (ParseInlineContent)** needs to respect `SoftBreakMode`:

```csharp
internal static List<InlineContent> ParseInlineContent(
    string html, 
    BlockKind context,
    SoftBreakMode softBreakMode = SoftBreakMode.Relaxed)  // ← ADD THIS
{
    // ... existing code ...
    
    // When flushing text:
    var text = NormalizeWhitespace(textBuf.ToString(), softBreakMode);
    
    // ... rest of code ...
}

private static string NormalizeWhitespace(
    string text, 
    SoftBreakMode softBreakMode = SoftBreakMode.Relaxed)  // ← ADD THIS
{
    if (string.IsNullOrWhiteSpace(text))
        return "";

    if (softBreakMode == SoftBreakMode.Relaxed)
    {
        // Current behavior: collapse all whitespace to single space
        return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
    }
    else // Strict mode
    {
        // Preserve line structure but normalize internal spaces
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", lines.Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"\s+", " ")));
    }
}
```

**Stage 3 (ConvertToMarkdown)** needs to handle paragraph separation:

```csharp
internal static string ConvertToMarkdown(List<BlockElement> blocks, MarkdownOutputSettings? settings = null)
{
    settings ??= new();
    var output = new List<string>();
    BlockKind? previousBlockKind = null;

    foreach (var block in blocks)
    {
        // Insert blank line between consecutive paragraphs
        if (previousBlockKind == BlockKind.Paragraph && block.Kind == BlockKind.Paragraph)
        {
            output.Add("");  // Blank line for paragraph separation
        }

        // ... process block as before ...
        
        previousBlockKind = block.Kind;
    }

    return string.Join("\n", output);
}
```

---

## Part 5: Test Cases

### Soft Breaks

**Relaxed Mode** (default):
```
Input HTML:  <p>Line 1
             Line 2</p>
Expected:    Line 1 Line 2
```

**Strict Mode**:
```
Input HTML:  <p>Line 1
             Line 2</p>
Expected:    Line 1
             Line 2
```

### Hard Breaks

**Backslash Style**:
```
Input HTML:  <p>Line 1<br>Line 2</p>
Expected:    Line 1\
             Line 2
```

**TrailingSpaces Style**:
```
Input HTML:  <p>Line 1<br>Line 2</p>
Expected:    Line 1  
             Line 2
```

### Paragraph Separation

```
Input HTML:  <p>Para 1</p><p>Para 2</p>
Expected:    Para 1

             Para 2
```

---

## Part 6: Migration Path

### Current State
- ✅ Hard breaks: implemented
- ⚠️ Soft breaks: partially implemented (only Relaxed mode, not configurable)
- ❌ Paragraph separation: broken (removed all inter-block blanks)

### Action Items
1. Add `SoftBreakMode` parameter to `ParseInlineContent()`
2. Update `NormalizeWhitespace()` to accept and respect mode
3. Pass settings through entire pipeline
4. Implement paragraph separation logic in `ConvertToMarkdown()`
5. Add test coverage for all three break types
6. Update clipboard integration to use correct settings
