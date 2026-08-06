# HTML to Markdown: Semantic Block Model Architecture

**Status**: Recommended Architecture  
**Date**: 2026-08-06  
**Replaces**: Character-by-character parsing approach  
**Scope**: Markdown-representable HTML only (clipboard HTML from Word, browsers, Office)

---

## Executive Summary

**Problem**: Current character-by-character parser loses semantic information about block structure, resulting in:
- Headers and paragraphs merging on same line
- Paragraph boundaries disappearing
- Break semantics unclear (hard vs soft vs block breaks)
- Settings (HardBreak/SoftBreak) ignored
- 82/84 tests passing, 2 related tests still failing

**Solution**: Parse HTML to explicit **block structure** before converting to Markdown.

**Result**: 
- ✅ Semantically correct output
- ✅ All break types handled automatically
- ✅ Settings naturally applied
- ✅ Cleaner, more maintainable code
- ✅ Easier to extend

---

## Part 1: Core Concept

### Markdown is a Block Language

Markdown fundamentally operates at two levels:

```
BLOCK LEVEL (top-level structure)
├─ Headers (h1-h6)
├─ Paragraphs
├─ Lists (ordered/unordered)
├─ Blockquotes
├─ Code blocks
├─ Horizontal rules
└─ (blank lines separate blocks)

INLINE LEVEL (within a block)
├─ Text
├─ **Bold** (strong)
├─ *Italic* (em)
├─ `Code` (inline)
├─ [Links](url)
├─ Line breaks (soft or hard)
└─ Colors (via HTML comments)
```

### Current Approach: Character-by-Character (Wrong Model)

```
Parse: H-T-M-L- -c-h-a-r-a-c-t-e-r-b-y-c-h-a-r-a-c-t-e-r
                   ↓
           Lose block structure
                   ↓
           Try to reconstruct semantics (fail)
```

### Proposed Approach: Block-First (Correct Model)

```
Parse: Extract block boundaries first
       ↓
       For each block, parse inline content
       ↓
       Convert block → Markdown line(s)
       ↓
       Block structure handles everything else
```

---

## Part 2: Data Structures

### Core Types

```csharp
/// <summary>
/// Represents a markdown block element (header, paragraph, list, etc.)
/// Markdown is fundamentally a block-oriented language.
/// </summary>
internal class BlockElement
{
    /// <summary>Type of block (determines conversion strategy)</summary>
    public BlockType Type { get; set; }
    
    /// <summary>For BlockType.Header only: 1-6</summary>
    public int? HeaderLevel { get; set; }
    
    /// <summary>Inline content (text, formatting, colors, breaks)</summary>
    public List<InlineContent> Content { get; set; } = new();
    
    /// <summary>Nested blocks (for lists, blockquotes, etc.)</summary>
    public List<BlockElement>? NestedBlocks { get; set; }
}

/// <summary>Block types that map 1:1 to Markdown</summary>
internal enum BlockType
{
    Header,         // h1-h6 → # text
    Paragraph,      // p → text (with breaks handled)
    UnorderedList,  // ul → - item
    OrderedList,    // ol → 1. item
    ListItem,       // li → content
    Blockquote,     // blockquote → > text
    HorizontalRule, // hr → ---
    CodeBlock,      // pre → ``` code ```
    // Skip: div, span, and non-markdown elements
}

/// <summary>
/// Inline content within a block.
/// Handles text, formatting, and breaks.
/// </summary>
internal class InlineContent
{
    /// <summary>The actual text</summary>
    public string Text { get; set; }
    
    /// <summary>Formatting applied to this segment</summary>
    public InlineFormat Format { get; set; } = new();
    
    /// <summary>Does this segment end with a hard break?</summary>
    /// Only meaningful within paragraphs, lists.
    public bool FollowedByHardBreak { get; set; }
}

/// <summary>Inline formatting (colors, bold, italic, code)</summary>
internal class InlineFormat
{
    public RgbColor? ForegroundColor { get; set; }
    public RgbColor? BackgroundColor { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Code { get; set; }
}

/// <summary>Settings that affect markdown output</summary>
internal class MarkdownOutputSettings
{
    /// <summary>How to represent hard line breaks</summary>
    public HardBreakMode HardBreak { get; set; } = HardBreakMode.Backslash;
    
    /// <summary>How to handle soft breaks (newlines in HTML)</summary>
    public SoftBreakMode SoftBreak { get; set; } = SoftBreakMode.Relaxed;
    
    /// <summary>Show color tags in output (always true for clipboard paste)</summary>
    public bool PreserveColors { get; set; } = true;
}

internal enum HardBreakMode
{
    Backslash,      // Line 1\ + newline + Line 2
    TrailingSpaces, // Line 1  + newline + Line 2 (two spaces)
}

internal enum SoftBreakMode
{
    Relaxed,        // Whitespace/newlines → single space
    Strict,         // Preserve newlines as-is
}
```

---

## Part 3: Architecture

### Three-Stage Pipeline

```
┌────────────────────────────────────────────────────────────┐
│ STAGE 1: Block Structure Extraction                        │
│ ParseBlockStructure(html: string) → List<BlockElement>    │
├────────────────────────────────────────────────────────────┤
│ Input:  HTML string with mixed blocks and inline content   │
│                                                             │
│ Process:                                                   │
│  1. Scan for block-level tags: <h1-6>, <p>, <ul>, etc.   │
│  2. Extract boundaries of each block                       │
│  3. Identify nesting (lists, blockquotes)                 │
│  4. Call ParseInline() for each block's content           │
│  5. Build BlockElement tree                               │
│                                                             │
│ Output: List<BlockElement> with structure intact          │
│         (Semantics preserved!)                             │
└────────────────────────────────────────────────────────────┘
                        │
                        ↓
┌────────────────────────────────────────────────────────────┐
│ STAGE 2: Inline Content Parsing                            │
│ ParseInline(html: string, context: BlockType)             │
│   → List<InlineContent>                                    │
├────────────────────────────────────────────────────────────┤
│ Input:  HTML of a single block's content                   │
│         e.g., "<span style='color:red'>Text</span><br>OK"  │
│                                                             │
│ Process:                                                   │
│  1. Character-by-character parsing (focused scope)         │
│  2. Extract: text, colors, bold/italic, breaks            │
│  3. Handle entities, whitespace per SoftBreak setting      │
│  4. Mark hard breaks (<br> tags)                          │
│  5. Skip non-representable inline tags                     │
│                                                             │
│ Output: List<InlineContent>                               │
│         Clean, focused parsing (no block confusion!)       │
└────────────────────────────────────────────────────────────┘
                        │
                        ↓
┌────────────────────────────────────────────────────────────┐
│ STAGE 3: Markdown Assembly                                 │
│ ConvertToMarkdown(blocks: List<BlockElement>,              │
│                   settings: MarkdownOutputSettings)        │
│   → string                                                  │
├────────────────────────────────────────────────────────────┤
│ Input:  Structured blocks with formatted content           │
│         Settings (HardBreak, SoftBreak, PreserveColors)   │
│                                                             │
│ Process:                                                   │
│  1. For each block:                                        │
│     ├─ Header → "### text"                                │
│     ├─ Paragraph → format text + handle breaks            │
│     ├─ List → "- item" or "1. item"                       │
│     ├─ Blockquote → "> text"                              │
│     └─ HR → "---"                                         │
│  2. Apply inline formatting (bold, italic, colors)         │
│  3. Handle hard breaks per HardBreak setting               │
│  4. Separate blocks with blank lines                       │
│  5. Preserve colors as <!--@fg:color--> comments          │
│                                                             │
│ Output: Final Markdown string                              │
│         (Correct and complete!)                            │
└────────────────────────────────────────────────────────────┘
```

---

## Part 4: Implementation Guide

### Stage 1: Block Structure Extraction

```csharp
internal List<BlockElement> ParseBlockStructure(string html)
{
    var blocks = new List<BlockElement>();
    int pos = 0;
    
    while (pos < html.Length)
    {
        // Skip whitespace/non-tag content
        while (pos < html.Length && html[pos] != '<')
            pos++;
        
        if (pos >= html.Length)
            break;
        
        // Try to match block-level tags
        if (html.AsSpan(pos).StartsWith("<h", StringComparison.OrdinalIgnoreCase))
        {
            var (block, newPos) = ParseHeader(html, pos);
            if (block != null)
            {
                blocks.Add(block);
                pos = newPos;
                continue;
            }
        }
        
        if (html.AsSpan(pos).StartsWith("<p", StringComparison.OrdinalIgnoreCase))
        {
            var (block, newPos) = ParseParagraph(html, pos);
            if (block != null)
            {
                blocks.Add(block);
                pos = newPos;
                continue;
            }
        }
        
        if (html.AsSpan(pos).StartsWith("<ul", StringComparison.OrdinalIgnoreCase))
        {
            var (block, newPos) = ParseUnorderedList(html, pos);
            if (block != null)
            {
                blocks.Add(block);
                pos = newPos;
                continue;
            }
        }
        
        if (html.AsSpan(pos).StartsWith("<ol", StringComparison.OrdinalIgnoreCase))
        {
            var (block, newPos) = ParseOrderedList(html, pos);
            if (block != null)
            {
                blocks.Add(block);
                pos = newPos;
                continue;
            }
        }
        
        // ... handle blockquote, hr, pre, etc.
        
        // Skip unrecognized tags
        int closePos = html.IndexOf('>', pos);
        pos = closePos >= 0 ? closePos + 1 : pos + 1;
    }
    
    return blocks;
}

/// <summary>Parse a header block: <h1>Text</h1> → BlockElement</summary>
private (BlockElement?, int) ParseHeader(string html, int startPos)
{
    // Extract level: <h3> → 3
    int level = ExtractHeaderLevel(html, startPos);
    if (level < 1 || level > 6) return (null, startPos);
    
    // Find closing tag
    string closeTag = $"</h{level}>";
    int closeStart = html.IndexOf(closeTag, startPos, StringComparison.OrdinalIgnoreCase);
    if (closeStart < 0) return (null, startPos);
    
    // Extract content between tags
    int contentStart = html.IndexOf('>', startPos) + 1;
    string headerContent = html[contentStart..closeStart];
    
    // Parse inline content
    var inline = ParseInlineContent(headerContent, BlockType.Header);
    
    return (new BlockElement
    {
        Type = BlockType.Header,
        HeaderLevel = level,
        Content = inline
    }, closeStart + closeTag.Length);
}

/// <summary>Parse a paragraph block: <p>Text</p> → BlockElement</summary>
private (BlockElement?, int) ParseParagraph(string html, int startPos)
{
    int closeStart = html.IndexOf("</p>", startPos, StringComparison.OrdinalIgnoreCase);
    if (closeStart < 0) return (null, startPos);
    
    int contentStart = html.IndexOf('>', startPos) + 1;
    string paraContent = html[contentStart..closeStart];
    
    // Parse inline content (includes <br> handling)
    var inline = ParseInlineContent(paraContent, BlockType.Paragraph);
    
    return (new BlockElement
    {
        Type = BlockType.Paragraph,
        Content = inline
    }, closeStart + 4);  // "</p>" is 4 chars
}

// ... similar for lists, blockquotes, etc.
```

### Stage 2: Inline Content Parsing

```csharp
/// <summary>Parse inline content within a block</summary>
internal List<InlineContent> ParseInlineContent(string html, BlockType context)
{
    var segments = new List<InlineContent>();
    var textBuf = new StringBuilder();
    var styleStack = new Stack<InlineFormat>();
    int pos = 0;
    
    while (pos < html.Length)
    {
        char c = html[pos];
        
        // Handle tags
        if (c == '<')
        {
            // Flush accumulated text
            if (textBuf.Length > 0)
            {
                segments.Add(new InlineContent
                {
                    Text = NormalizeWhitespace(textBuf.ToString()),
                    Format = styleStack.Count > 0 ? styleStack.Peek() : new()
                });
                textBuf.Clear();
            }
            
            // Parse tag
            int tagEnd = html.IndexOf('>', pos);
            if (tagEnd < 0) break;
            
            string tag = html[pos..(tagEnd + 1)];
            
            // Handle specific tags
            if (tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase))
            {
                // Hard break: mark on last segment
                if (segments.Count > 0)
                    segments[^1].FollowedByHardBreak = true;
                else
                    // Leading break (not representable), skip
                    ;
            }
            else if (tag.Equals("<strong>", StringComparison.OrdinalIgnoreCase) ||
                     tag.Equals("<b>", StringComparison.OrdinalIgnoreCase))
            {
                var fmt = styleStack.Count > 0 ? styleStack.Peek() : new();
                fmt.Bold = true;
                styleStack.Push(fmt);
            }
            else if (tag.Equals("</strong>", StringComparison.OrdinalIgnoreCase) ||
                     tag.Equals("</b>", StringComparison.OrdinalIgnoreCase))
            {
                if (styleStack.Count > 0) styleStack.Pop();
            }
            else if (tag.StartsWith("<span", StringComparison.OrdinalIgnoreCase))
            {
                // Extract style attributes
                var (fg, bg) = ExtractColors(tag);
                var fmt = styleStack.Count > 0 ? styleStack.Peek() : new();
                if (fg != null) fmt.ForegroundColor = fg;
                if (bg != null) fmt.BackgroundColor = bg;
                styleStack.Push(fmt);
            }
            else if (tag.Equals("</span>", StringComparison.OrdinalIgnoreCase))
            {
                if (styleStack.Count > 0) styleStack.Pop();
            }
            // ... handle other inline tags
            
            pos = tagEnd + 1;
        }
        else
        {
            // Accumulate text
            textBuf.Append(c);
            pos++;
        }
    }
    
    // Flush remaining text
    if (textBuf.Length > 0)
    {
        segments.Add(new InlineContent
        {
            Text = NormalizeWhitespace(textBuf.ToString()),
            Format = styleStack.Count > 0 ? styleStack.Peek() : new()
        });
    }
    
    return segments;
}

private string NormalizeWhitespace(string text)
{
    // Collapse multiple spaces to single space
    return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
```

### Stage 3: Markdown Assembly

```csharp
internal string ConvertToMarkdown(List<BlockElement> blocks, MarkdownOutputSettings settings)
{
    var output = new List<string>();
    
    foreach (var block in blocks)
    {
        switch (block.Type)
        {
            case BlockType.Header:
                var hashes = new string('#', block.HeaderLevel.Value);
                var headerText = FormatInline(block.Content, settings);
                output.Add($"{hashes} {headerText}");
                break;
            
            case BlockType.Paragraph:
                var paraLines = FormatParagraph(block.Content, settings);
                output.AddRange(paraLines);
                break;
            
            case BlockType.UnorderedList:
                foreach (var item in block.NestedBlocks)
                    output.Add($"- {FormatInline(item.Content, settings)}");
                break;
            
            case BlockType.OrderedList:
                int itemNum = 1;
                foreach (var item in block.NestedBlocks)
                    output.Add($"{itemNum++}. {FormatInline(item.Content, settings)}");
                break;
            
            case BlockType.Blockquote:
                var quoteLines = FormatInline(block.Content, settings).Split('\n');
                foreach (var line in quoteLines)
                    output.Add($"> {line}");
                break;
            
            case BlockType.HorizontalRule:
                output.Add("---");
                break;
        }
        
        // Separate blocks with blank line
        output.Add("");
    }
    
    return string.Join("\n", output).TrimEnd();
}

/// <summary>Format inline content, respecting breaks and settings</summary>
private List<string> FormatParagraph(List<InlineContent> content, MarkdownOutputSettings settings)
{
    var lines = new List<string>();
    var currentLine = new StringBuilder();
    
    foreach (var segment in content)
    {
        var formatted = FormatSegment(segment, settings);
        currentLine.Append(formatted);
        
        if (segment.FollowedByHardBreak)
        {
            // Hard break: apply HardBreak setting
            if (settings.HardBreak == HardBreakMode.Backslash)
                currentLine.Append("\\");
            else if (settings.HardBreak == HardBreakMode.TrailingSpaces)
                currentLine.Append("  ");
            
            lines.Add(currentLine.ToString());
            currentLine.Clear();
        }
    }
    
    if (currentLine.Length > 0)
        lines.Add(currentLine.ToString());
    
    return lines;
}

/// <summary>Format a single segment with colors and emphasis</summary>
private string FormatSegment(InlineContent segment, MarkdownOutputSettings settings)
{
    string text = segment.Text;
    
    // Apply formatting
    if (segment.Format.Bold)
        text = $"**{text}**";
    
    if (segment.Format.Italic)
        text = $"*{text}*";
    
    if (segment.Format.Code)
        text = $"`{text}`";
    
    // Apply colors (as HTML comments)
    if (settings.PreserveColors && segment.Format.ForegroundColor != null)
    {
        var colorStr = FormatColor(segment.Format.ForegroundColor.Value);
        text = $"<!--@fg:{colorStr}-->{text}<!--/@fg-->";
    }
    
    return text;
}

private string FormatColor(RgbColor color)
{
    return MarkdownParser.TryGetColorName(color) ?? color.ToHex();
}
```

---

## Part 5: How This Fixes Everything

### Problem 1: Header/Paragraph Merging

```html
Input:  <h3>RPG.net</h3>
        <p>Nothing substantial.</p>

Block structure:
[
  BlockElement { Type: Header, HeaderLevel: 3, Content: ["RPG.net"] },
  BlockElement { Type: Paragraph, Content: ["Nothing substantial."] }
]

Output (automatic separation by block model):
### RPG.net

Nothing substantial.
```

✅ **Solved**: Blocks are separate, no merge possible.

### Problem 2: Paragraph Boundaries

```html
Input:  <p><span style="color:red">Text 1</span></p>
        <p><span style="color:blue">Text 2</span></p>

Block structure:
[
  BlockElement { Type: Paragraph, Content: [Segment("Text 1", red)] },
  BlockElement { Type: Paragraph, Content: [Segment("Text 2", blue)] }
]

Output (automatic spacing):
<!--@fg:red-->Text 1<!--/@fg-->

<!--@fg:blue-->Text 2<!--/@fg-->
```

✅ **Solved**: Each block produces separate line(s), automatic blank line between blocks.

### Problem 3: Break Semantics

```html
Input:  <p>Line 1<br>Line 2</p>
        <p>Line 3</p>

Block structure:
[
  BlockElement { 
    Type: Paragraph, 
    Content: [
      Segment("Line 1", followedByBreak: true),
      Segment("Line 2")
    ]
  },
  BlockElement { Type: Paragraph, Content: [Segment("Line 3")] }
]

ConvertToMarkdown with HardBreak: Backslash:
Line 1\
Line 2

Line 3

ConvertToMarkdown with HardBreak: TrailingSpaces:
Line 1  
Line 2

Line 3
```

✅ **Solved**: Break type known (inline vs block), settings applied correctly.

### Problem 4: Edge Cases

```html
<!-- Multiple breaks -->
<p>A<br><br>B</p>
→ A\
  
  B

<!-- Break between blocks (non-representable, ignored) -->
<p>A</p><br><p>B</p>
→ A

  B

<!-- Break at start (non-representable, ignored) -->
<p><br>Text</p>
→ Text

<!-- Nested blocks (lists, quotes) -->
<ul>
  <li>Item 1</li>
  <li>Item 2<br>continued</li>
</ul>
→ - Item 1
  - Item 2\
    continued
```

✅ **Solved**: Structure clarifies intent.

---

## Part 6: Benefits

| Aspect | Current Approach | Block Model |
|--------|-----------------|------------|
| **Semantics** | Lost during parsing | Preserved in structure |
| **Break handling** | Confusing mix | Clear: inline vs block |
| **Settings** | Ignored | Applied naturally |
| **Edge cases** | Handled ad-hoc | Systematic |
| **Extensibility** | Hard (character parsing) | Easy (add block type) |
| **Testability** | Stages mixed | 3 independent stages |
| **Performance** | Single pass (fragile) | Structured (robust) |
| **Maintainability** | High complexity | Low complexity |

---

## Part 7: Implementation Roadmap

### Phase 1: Core Structure (Foundation)

1. Define `BlockElement`, `InlineContent`, `InlineFormat` classes
2. Implement `ParseBlockStructure()` for headers and paragraphs
3. Implement `ParseInlineContent()` for simple text and spans
4. Implement `ConvertToMarkdown()` for basic blocks
5. Test: Basic paragraphs, headers, colors

### Phase 2: Features (List & Quote Support)

1. Extend `ParseBlockStructure()` for lists (ul/ol/li)
2. Extend `ParseBlockStructure()` for blockquotes
3. Handle nesting in lists
4. Test: Nested lists, blockquotes with formatting

### Phase 3: Advanced (All Features)

1. Hard break handling (`<br>` tags)
2. Soft break handling (whitespace normalization)
3. Settings application (HardBreak, SoftBreak)
4. Multiple breaks handling
5. Test: All edge cases from test suite

### Phase 4: Migration (Replace Current)

1. Run all existing tests with new approach
2. Verify output matches expectations
3. Replace old implementation
4. Archive old code

---

## Part 8: Example: Full Conversion

### Input HTML (from clipboard)

```html
<h3>RPG Analysis</h3>
<p>First paragraph with <span style="color:red">colored text</span>.</p>
<p>Second paragraph with <br>hard break.</p>
<ul>
  <li>Item 1</li>
  <li>Item 2 with <strong>bold</strong></li>
</ul>
<blockquote><p>A famous quote</p></blockquote>
<hr>
<p>Final paragraph</p>
```

### Stage 1: Block Structure

```csharp
blocks = [
  BlockElement {
    Type: Header,
    HeaderLevel: 3,
    Content: [Segment("RPG Analysis")]
  },
  BlockElement {
    Type: Paragraph,
    Content: [
      Segment("First paragraph with "),
      Segment("colored text", Foreground: red),
      Segment(".")
    ]
  },
  BlockElement {
    Type: Paragraph,
    Content: [
      Segment("Second paragraph with "),
      Segment("hard break", FollowedByBreak: true)
    ]
  },
  BlockElement {
    Type: UnorderedList,
    NestedBlocks: [
      BlockElement { Type: ListItem, Content: [Segment("Item 1")] },
      BlockElement { Type: ListItem, Content: [
        Segment("Item 2 with "),
        Segment("bold", Bold: true)
      ]}
    ]
  },
  BlockElement {
    Type: Blockquote,
    Content: [Segment("A famous quote")]
  },
  BlockElement {
    Type: HorizontalRule
  },
  BlockElement {
    Type: Paragraph,
    Content: [Segment("Final paragraph")]
  }
]
```

### Stage 3: Markdown Output

```markdown
### RPG Analysis

First paragraph with <!--@fg:red-->colored text<!--/@fg-->.

Second paragraph with \
hard break.

- Item 1
- Item 2 with **bold**

> A famous quote

---

Final paragraph
```

Perfect! ✅

---

## Part 9: Comparison: Current vs Proposed

### Current Code Flow (Character-by-Character)

```
Input HTML
  ↓
PreprocessBlockElements (converts h1-6, hr, etc.)
  ↓
ParseHtmlFragment (char-by-char scanning)
  ├─ MARKDOWN_BLOCK handling (splits by \n)
  ├─ Tag processing (manages style stack)
  ├─ Text accumulation (StringBuilder)
  └─ Style stack operations
  ↓
Output: List<List<ColoredSegment>>
  ↓
ConvertToMarkdown (joins with colors)
  ↓
Markdown string (with lost structure)
```

**Issues**:
- ❌ Block boundaries blur (p tags not preprocessed)
- ❌ Semantic information lost (why was this line created?)
- ❌ Settings ignored throughout
- ❌ Edge cases handled ad-hoc

### Proposed Code Flow (Block-First)

```
Input HTML
  ↓
ParseBlockStructure (extracts block boundaries)
  ├─ Identifies: <h1-6>, <p>, <ul>, <ol>, <blockquote>, <hr>
  ├─ Handles nesting (lists)
  └─ Calls ParseInlineContent() for each
  ↓
ParseInlineContent (focused char parsing)
  ├─ Only processes inline tags within block
  ├─ Marks hard breaks
  ├─ Extracts colors, bold, italic
  └─ Returns InlineContent list
  ↓
Output: List<BlockElement> with structure
  ↓
ConvertToMarkdown (applies settings, formats by block type)
  ├─ Apply HardBreak setting
  ├─ Apply SoftBreak setting
  ├─ Format block-by-block
  └─ Automatic blank line separation
  ↓
Markdown string (correct and complete)
```

**Advantages**:
- ✅ Structure preserved through entire pipeline
- ✅ Each stage has clear input/output
- ✅ Settings applied systematically
- ✅ Edge cases handled by structure, not code

---

## Part 10: Migration Path

### Option A: New Implementation (Parallel)

1. Build `BlockModelConverter` alongside current code
2. Run both implementations
3. Compare outputs
4. Migrate when verified
5. Remove old code

### Option B: Incremental Refactor

1. Keep `ConvertToMarkdown()` interface
2. Implement block parser internally
3. Convert internal representation to blocks
4. Test at each stage
5. Replace `ParseHtmlFragment()` when ready

### Option C: Clean Break

1. Replace entire implementation at once
2. Higher risk, faster timeline
3. Easier to implement completely

**Recommendation**: Option A (parallel implementation)
- Lowest risk
- Can compare outputs
- Keeps working code until new code proven

---

## Part 11: Testing Strategy

### Unit Tests by Stage

```csharp
[TestClass]
public class BlockStructureTests
{
    [TestMethod]
    public void Header_ExtractedWithLevel()
    {
        var html = "<h3>Title</h3>";
        var blocks = ParseBlockStructure(html);
        
        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockType.Header);
        blocks[0].HeaderLevel.Should().Be(3);
    }
    
    [TestMethod]
    public void MultipleParagraphs_CreatedAsSeparateBlocks()
    {
        var html = "<p>Para 1</p><p>Para 2</p>";
        var blocks = ParseBlockStructure(html);
        
        blocks.Should().HaveCount(2);
        blocks.All(b => b.Type == BlockType.Paragraph).Should().BeTrue();
    }
    
    // ... more tests for lists, nesting, etc.
}

[TestClass]
public class InlineParsingTests
{
    [TestMethod]
    public void SpanWithColor_ExtractedCorrectly()
    {
        var html = "<span style='color:red'>Text</span>";
        var inline = ParseInlineContent(html, BlockType.Paragraph);
        
        inline.Should().HaveCount(1);
        inline[0].Text.Should().Be("Text");
        inline[0].Format.ForegroundColor.Should().Be(new RgbColor(255, 0, 0));
    }
    
    [TestMethod]
    public void HardBreak_MarkedOnSegment()
    {
        var html = "Text<br>More";
        var inline = ParseInlineContent(html, BlockType.Paragraph);
        
        inline.Should().HaveCount(2);
        inline[0].FollowedByHardBreak.Should().BeTrue();
    }
}

[TestClass]
public class MarkdownAssemblyTests
{
    [TestMethod]
    public void Header_FormattedWithHashtags()
    {
        var blocks = new[] {
            new BlockElement {
                Type = BlockType.Header,
                HeaderLevel = 3,
                Content = new[] { new InlineContent { Text = "Title" } }
            }
        };
        
        var markdown = ConvertToMarkdown(blocks, new());
        markdown.Should().Contain("### Title");
    }
    
    [TestMethod]
    public void ParagraphsWithHardBreak_UsesBackslashSetting()
    {
        var settings = new MarkdownOutputSettings { HardBreak = HardBreakMode.Backslash };
        
        var markdown = ConvertToMarkdown(/* ... */, settings);
        markdown.Should().Contain("Line 1\\");
    }
}
```

### Integration Tests (Full Conversion)

```csharp
[TestMethod]
public void Header_FollowedByParagraph_KeepsSeparate()
{
    var html = "<h3>RPG.net</h3><p>Nothing substantial.</p>";
    var markdown = ConvertHtmlToMarkdown(html);
    
    var lines = markdown.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    lines.Should().HaveCountGreaterThanOrEqualTo(2);
    
    var headerLine = lines.FirstOrDefault(l => l.StartsWith("###"));
    var paraLine = lines.FirstOrDefault(l => l.Contains("Nothing substantial"));
    
    headerLine.Should().NotBeNull();
    paraLine.Should().NotBeNull();
    lines.IndexOf(headerLine).Should().BeLessThan(lines.IndexOf(paraLine));
}

[TestMethod]
public void MultipleBreaks_CreatesBlankLine()
{
    var html = "<p>Line1<br><br>Line2</p>";
    var markdown = ConvertHtmlToMarkdown(html);
    
    markdown.Should().Contain("Line1\n\nLine2");
}
```

---

## Part 12: Scope Definition

### What We Handle (Markdown-Representable)

✅ **Block Elements**
- Headers (h1-h6)
- Paragraphs (p)
- Lists (ul/ol/li)
- Blockquotes (blockquote)
- Horizontal rules (hr)
- Code blocks (pre/code)

✅ **Inline Formatting**
- Bold (strong, b)
- Italic (em, i)
- Inline code (code)
- Hard breaks (br)
- Colors (span style="color:...")
- Links (a href)
- Images (img)

### What We Skip (Gracefully)

❌ **Non-Representable Elements**
- div, span (no semantic meaning)
- table (no markdown equivalent)
- form, input
- script, style
- class, id, data-* attributes (pass through)
- Inline CSS (only color extracted)

❌ **Note**: Skipped elements are **silently ignored** - we only extract text/structure if possible.

---

## Part 13: Success Criteria

After implementation, verify:

- ✅ All 84 existing tests pass
- ✅ Header/paragraph separation works
- ✅ Paragraph boundaries preserved
- ✅ Multiple breaks handled correctly
- ✅ Settings (HardBreak/SoftBreak) applied
- ✅ Colors preserved
- ✅ Lists with nesting work
- ✅ Blockquotes formatted correctly
- ✅ No data loss for markdown-representable HTML
- ✅ Non-representable HTML gracefully skipped

---

## Conclusion

The **Semantic Block Model** is superior because:

1. **Correctness**: Markdown is block-oriented; model reflects this
2. **Clarity**: Each stage has clear responsibility
3. **Maintainability**: Structure encodes semantics, less code logic needed
4. **Extensibility**: Adding features = adding cases, not rewriting parser
5. **Settings Integration**: No hacks; settings applied naturally
6. **Testing**: Three independent stages, each testable
7. **Robustness**: Edge cases handled by structure, not special cases

This is the **right model** for HTML-to-Markdown conversion.
