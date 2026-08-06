# HTML to Markdown Conversion Architecture

**Status**: Design Document  
**Last Updated**: 2026-08-06  
**Relates to**: HtmlToMarkdownConverter, PreprocessBlockElements, ParseHtmlFragment

## Executive Summary

The HTML-to-Markdown converter transforms clipboard HTML (from Word, browsers, Office) into RaisinDocs markdown with color preservation. The current implementation uses a **manual character-by-character parser** without external HTML parsing libraries.

### Key Achievement
- ✅ Fixes CommonMark compliance bug (headers and paragraphs no longer merge)
- ✅ Preserves inline colors and formatting
- ⚠️ **Remaining challenges**: Soft/hard break distinction, nested breaks, paragraph boundaries

### Test Results
- **82/84 tests passing**
- Main bug fixed: `Header_FollowedByParagraph_KeepsSeparate` ✓
- 2 remaining: nested lists, multiple colored paragraphs

---

## Part 1: The Big Picture

### Input → Output Flow

```
HTML (from clipboard)
    ↓
ExtractFragment()
    ↓ (strips CF_HTML headers)
PreprocessBlockElements()
    ↓ (converts block-level tags to MARKDOWN_BLOCK markers)
ParseHtmlFragment()
    ↓ (character-by-character parsing to colored segments)
ConvertToMarkdown()
    ↓ (assembles colored segments into final markdown string)
Markdown String (with colors as HTML comments)
```

### Key Design Decision: No External Parser

**Why no HTML parser library?**

✅ **Advantages (for clipboard HTML):**
- Clipboard HTML is well-formed, predictable (Word, browsers, Office)
- Specific tag set (p, strong, em, h1-h6, ul, ol, li, hr, blockquote, span with style)
- Lightweight, no external dependencies
- Full control over parsing logic
- Can optimize for specific patterns
- Respects color-preservation requirements

❌ **Disadvantages (real-world HTML):**
- No graceful handling of malformed HTML
- Tag attribute parsing is manual and fragile
- Edge cases with nested quotes, entities, CDATA
- Doesn't handle all HTML5 features
- Scale limitations

**Recommendation**: Appropriate for clipboard use case. Consider HtmlAgilityPack if expanding to general HTML input.

---

## Part 2: Current Architecture

### Component 1: PreprocessBlockElements()

**Purpose**: Convert block-level HTML elements to MARKDOWN_BLOCK markers

**Inputs**: 
- HTML fragment from clipboard

**Processing**:
- Scans for block-level tags: `<h1-h6>`, `<p>`, `<hr>`, `<blockquote>`, `<ul>`, `<ol>`
- For each block tag found:
  - Extracts content
  - Converts to Markdown representation
  - Wraps in `<!--@MARKDOWN_BLOCK-->...</<!--/@MARKDOWN_BLOCK-->`
  - Adds `\n` after closing marker to separate from next block

**Example**:
```
Input:  <h3>RPG.net</h3><p>Nothing substantial.</p>
Output: <!--@MARKDOWN_BLOCK-->### RPG.net<!--/@MARKDOWN_BLOCK-->
        <p>Nothing substantial.</p>
```

**Key Feature**: List conversion
- Delegates to `ListConverter.ConvertList()`
- Handles nested lists with indentation
- Preserves ordered vs unordered distinction

**Current Code Location**: `HtmlToMarkdownConverter.cs`, lines 60-199

### Component 2: ParseHtmlFragment()

**Purpose**: Parse HTML to colored segments

**Input**: 
- Preprocessed HTML (with MARKDOWN_BLOCK markers)

**Output**: 
- `List<List<ColoredSegment>>` - lines of colored text segments

**Process**: Character-by-character parsing with:
1. **MARKDOWN_BLOCK handling**
   - Splits markdown by newlines
   - Creates one `List<ColoredSegment>` per line
   - **Key fix (2026-08-06)**: Skip newline after MARKDOWN_BLOCK close, create fresh currentLine

2. **Comment skipping**
   - Ignores HTML comments (except MARKDOWN_BLOCK)

3. **Tag processing**
   - `<span>`: Extract style attributes (color, background, bold, italic)
   - `<strong>`, `<b>`: Push bold to style stack
   - `<em>`, `<i>`: Push italic to style stack
   - `<br>`: Create new line
   - `<pre>`: Enable preformatted mode
   - Plain tags: Pass through or skip

4. **Style stack**
   - Maintains nested style context
   - Respects inner style override of outer style

5. **Text accumulation**
   - Builds `StringBuilder` for pending text
   - Flushes when encountering style change, tag, or end of input
   - Creates `ColoredSegment` with accumulated text + current style

6. **Entity decoding**
   - `&nbsp;` → space
   - `&lt;` → `<`
   - `&#123;` → `{`
   - `&#xAB;` → `«` (hex entities)

**Current Code Location**: `HtmlToMarkdownConverter.cs`, lines 385-594

### Component 3: ConvertToMarkdown()

**Purpose**: Assemble colored segments into final markdown string

**Input**: 
- `List<List<ColoredSegment>>` (segments organized by line)

**Output**: 
- Final markdown string with inline color tags

**Process**:
1. Analyzes each line to determine uniform color
2. Groups consecutive lines with same color
3. Wraps multi-line uniform colors in `<!--@div fg:red-->...<!--/@div-->`
4. For individual lines/colors: `<!--@fg:red-->text<!--/@fg-->`
5. Joins lines with `\n`

**Color Format Examples**:
```markdown
<!-- Inline color span -->
<!--@fg:red-->error<!--/@fg-->

<!-- Inline with hex -->
<!--@fg:#FF0000-->error<!--/@fg-->

<!-- Block color (multiple lines) -->
<!--@div fg:red-->
Line 1
Line 2
<!--/@div-->

<!-- With background -->
<!--@fg:red bg:#FFFF00-->Yellow background, red text<!--/@fg-->
```

**Current Code Location**: `HtmlToMarkdownConverter.cs`, lines 586-724

---

## Part 3: The Break Problem

### HTML vs Markdown Break Semantics

This is the **fundamental impedance mismatch** causing issues.

#### HTML Break Types

| Break Type | HTML Syntax | Semantic Meaning | Example |
|-----------|------------|-----------------|---------|
| **Soft break** | Newline in source | Whitespace (collapsed) | `<p>Line 1\nLine 2</p>` → "Line 1 Line 2" |
| **Hard break** | `<br>` or `<br/>` | Explicit line break within block | `<p>Line 1<br>Line 2</p>` → two lines |
| **Block break** | `</p><p>` or similar | Separate block elements | `<p>Para 1</p><p>Para 2</p>` → two paragraphs |
| **Whitespace collapse** | Multiple spaces/tabs | Single space | `<p>Text    with spaces</p>` → single space |

#### Markdown Break Types (per CLAUDE.md Settings)

| Break Type | Setting | Syntax | Meaning |
|-----------|---------|--------|---------|
| **Hard break** | `HardBreak: Backslash` | `Line 1\` + newline | Explicit line break |
| **Hard break** | `HardBreak: TrailingSpaces` | `Line 1  ` + newline | Explicit line break (2 spaces) |
| **Soft break** | `SoftBreak: Relaxed` | newline → space | Newline becomes space |
| **Soft break** | `SoftBreak: Strict` | newline preserved | Keep as-is |
| **Paragraph break** | N/A | empty line (`\n\n`) | Separate paragraphs |

### The Mapping Problem

When converting `<br>`, we need to know:
- Is this a **line break within a paragraph** → needs HardBreak setting
- Is this a **break between blocks** → needs paragraph spacing
- Are there **multiple `<br>` tags** → multiple line breaks or double-paragraph?

**Current Code**:
```csharp
else if (!closing && tagName.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase))
{
    HtmlParsingContext.FlushText(textBuf, currentLine, ...);
    currentLine = new List<ColoredSegment>();
    lines.Add(currentLine);  // ← Creates new line
}
```

**Problem**: No metadata about *why* this line was created. Later code can't distinguish:
- Was this from `<br>`? (hard break)
- Was this from `<p>` boundary? (block break)
- Was this from whitespace handling? (soft break)

### The Missing Information

What we lose during parsing:

```
HTML:  <p>Line 1<br>Line 2</p><p>More</p>

Current output:
lines = [
    [Segment("Line 1")],      // ← Lost: came from <br>
    [Segment("Line 2")],      // ← Lost: came from </p>
    [Segment("More")]         // ← Lost: is this end-of-doc?
]

Correct output (with metadata):
lines = [
    [Segment("Line 1", TrailingBreak: Hard)],      // <br> was here
    [Segment("Line 2", TrailingBreak: Block)],     // </p> was here
    [Segment("More", TrailingBreak: None)]         // End of input
]
```

### Current Gaps

1. **HardBreak/SoftBreak settings not used**
   ```csharp
   // These exist in DocsCanvas settings (CLAUDE.md):
   // HardBreak: Backslash or TrailingSpaces
   // SoftBreak: Relaxed or Strict
   
   // But HtmlToMarkdownConverter ignores them!
   // Always outputs one \n per line, regardless of setting
   ```

2. **Multiple `<br>` tags not handled**
   ```html
   <!-- What should this produce? -->
   <p>Line1<br><br>Line2</p>
   
   Current: Two lines (Line1, Line2)
   Correct: Depends on HardBreak setting:
     - Backslash mode: "Line1\\\nLine2"
     - TrailingSpaces mode: "Line1  \n  \nLine2"
     - Or paragraph break mode: "Line1\n\nLine2"
   ```

3. **Whitespace in HTML not normalized**
   ```html
   <!-- These should all be equivalent in Markdown -->
   <p>Text on
   multiple lines</p>
   
   <p>Text on  
   multiple lines</p>
   
   <p>Text on<br>multiple lines</p>
   
   Currently: Different outputs
   Correct: Same output (soft vs hard break based on setting)
   ```

4. **Block vs inline breaks not distinguished**
   ```html
   <!-- Block break (between paragraphs) -->
   <p>Para 1</p>
   <p>Para 2</p>
   
   <!-- Inline break (within paragraph) -->
   <p>Line 1<br>Line 2</p>
   
   Both currently create new lines, but with different semantics
   ```

---

## Part 4: The Paragraph Mixing Problem

### What Should Happen

```
HTML Input:  <p><span style="color:red">Text 1</span></p>
             <p><span style="color:blue">Text 2</span></p>

Expected Markdown:
<!--@fg:red-->Text 1<!--/@fg-->

<!--@fg:blue-->Text 2<!--/@fg-->

Current Output: (missing blank line)
<!--@fg:red-->Text 1<!--/@fg-->
<!--@fg:blue-->Text 2<!--/@fg-->
```

### Why It Happens

**The Issue**:
- After first `</p>`, we should create a new line
- But paragraph tags contain inline content (spans, text)
- The parser doesn't know if a new line should be a paragraph break or line break

**Current approach**:
1. PreprocessBlockElements doesn't touch `<p>` tags (leaves them as-is)
2. ParseHtmlFragment processes `<p>...</p>` inline
3. No distinction between `</p><p>` (block boundary) and `<br>` (inline break)

**Result**: Consecutive paragraphs get concatenated instead of separated.

### Why It's Hard to Fix

The architectural issue:
- Block elements are preprocessed (`<h1>`, `<hr>`, etc.)
- Inline elements are parsed character-by-character
- Paragraph tags are... both? They contain inline content but are block elements

```
Current design:
┌─────────────────────────────┐
│ PreprocessBlockElements     │
│ - Handles: h1-h6, hr, etc.  │
│ - Converts to MARKDOWN_BLOCK│
│ - Ignores: p tags           │  ← Problem: p not handled here
└──────────────┬──────────────┘
               │
               ↓
┌─────────────────────────────┐
│ ParseHtmlFragment           │
│ - Char-by-char parsing      │
│ - Handles: spans, p text    │
│ - No block semantics        │  ← Problem: loses block context
└─────────────────────────────┘
```

---

## Part 5: Solutions

### Solution 1: Add Break Metadata (Recommended)

**Approach**: Track break type through the parsing pipeline

**Implementation**:

```csharp
// 1. Extend ColoredSegment
internal class ColoredSegment
{
    public string Text { get; set; }
    public RgbColor? Foreground { get; set; }
    public RgbColor? Background { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    
    // NEW: Track trailing break
    public BreakType TrailingBreak { get; set; } = BreakType.None;
    public int BreakCount { get; set; } = 0;  // Multiple <br> handling
}

internal enum BreakType
{
    None,           // No break after this segment
    Soft,           // Whitespace/newline → becomes space
    Hard,           // <br> tag → backslash or trailing spaces
    Block,          // </p>, </div> → paragraph break (blank line)
}

// 2. Track breaks during parsing
else if (!closing && tagName.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase))
{
    HtmlParsingContext.FlushText(textBuf, currentLine, ...);
    
    // Mark as hard break (to be converted based on HardBreak setting)
    if (currentLine.Count > 0)
        currentLine[^1].TrailingBreak = BreakType.Hard;
    
    currentLine = new List<ColoredSegment>();
    lines.Add(currentLine);
}

// Handle multiple <br>
else if (successive <br> tags)
{
    if (currentLine.Count > 0)
        currentLine[^1].BreakCount++;  // Accumulate
}

// 3. Respect settings in ConvertToMarkdown
private static string ConvertToMarkdown(List<List<ColoredSegment>> lines, MarkdownSettings settings)
{
    var output = new List<string>();
    
    for (int i = 0; i < lines.Count; i++)
    {
        var line = lines[i];
        var lineText = FormatLine(line);
        output.Add(lineText);
        
        // Apply break handling based on metadata
        if (i < lines.Count - 1 && line.Count > 0)
        {
            var lastSegment = line[^1];
            
            switch (lastSegment.TrailingBreak)
            {
                case BreakType.Hard:
                    // Respect HardBreak setting
                    if (settings.HardBreak == HardBreakMode.Backslash)
                    {
                        output[^1] += "\\";  // Will add \n between lines
                    }
                    else if (settings.HardBreak == HardBreakMode.TrailingSpaces)
                    {
                        output[^1] += "  ";  // Two spaces before newline
                    }
                    
                    // Handle multiple breaks
                    for (int j = 1; j < lastSegment.BreakCount; j++)
                    {
                        output.Add("");  // Empty line for extra breaks
                    }
                    break;
                    
                case BreakType.Block:
                    // Add blank line for paragraph/block separation
                    output.Add("");
                    break;
                    
                case BreakType.Soft:
                    // Respect SoftBreak setting
                    if (settings.SoftBreak == SoftBreakMode.Strict)
                    {
                        output[^1] += "\";  // Keep line break
                    }
                    // Relaxed mode: already becomes space (default)
                    break;
            }
        }
    }
    
    return string.Join("\n", output);
}
```

**Advantages**:
- ✅ Preserves semantic information through pipeline
- ✅ Respects user settings (HardBreak, SoftBreak)
- ✅ Handles multiple `<br>` correctly
- ✅ Can distinguish soft/hard/block breaks

**Disadvantages**:
- ❌ Requires ColoredSegment modification
- ❌ Changes pipeline (need settings passed through)
- ❌ More complex ConvertToMarkdown logic

**Effort**: Medium (2-3 hours for thorough implementation)

### Solution 2: Preprocess Paragraphs (Simpler)

**Approach**: Handle `<p>` tags like block elements in PreprocessBlockElements

**Implementation**:

```csharp
// In PreprocessBlockElements, add paragraph handling:
if (IsOpenTag(tag, "p"))
{
    int closeTagStart = html.IndexOf("</p>", tagEnd, StringComparison.OrdinalIgnoreCase);
    if (closeTagStart > 0)
    {
        // Treat <p> content as block, not inline
        string pContent = html[(tagEnd + 1)..closeTagStart];
        
        // Parse content for spans, formatting, etc.
        string converted = ParseParagraphContent(pContent);
        
        // If this is not the first paragraph, add separator
        if (result.Length > 0 && !result.EndsWith("\n\n"))
            result.Append('\n');
        
        result.Append(converted);
        
        pos = closeTagStart + "</p>".Length;
        continue;
    }
}
```

**Advantages**:
- ✅ Simpler (doesn't change ColoredSegment)
- ✅ Easier to implement
- ✅ Follows existing pattern

**Disadvantages**:
- ❌ Doesn't respect HardBreak/SoftBreak settings
- ❌ Doesn't handle `<br>` within paragraphs properly
- ❌ Still doesn't distinguish soft/hard breaks

**Effort**: Low (1 hour)

### Solution 3: Separate Block and Inline Parsing (Best Long-term)

**Approach**: Restructure to have distinct block and inline parsers

```
New architecture:
┌────────────────────────────────────────┐
│ ParseBlocks()                          │
│ - Identifies block boundaries          │
│ - Tracks block type (p, div, pre, etc) │
│ - Calls ParseInline() for content      │
└────────────────────────────────────────┘
         │
         ↓
┌────────────────────────────────────────┐
│ ParseInline()                          │
│ - Character parsing within block       │
│ - Handles spans, br, formatting        │
│ - Returns: (content, breaks inside)    │
└────────────────────────────────────────┘
         │
         ↓
┌────────────────────────────────────────┐
│ ConvertToMarkdown()                    │
│ - Applies block break rules            │
│ - Applies HardBreak/SoftBreak settings │
│ - Outputs final markdown               │
└────────────────────────────────────────┘
```

**Advantages**:
- ✅ Clean separation of concerns
- ✅ Handles all break types correctly
- ✅ Extensible for future block types
- ✅ Testable independently

**Disadvantages**:
- ❌ Major refactor (requires rewriting most of ParseHtmlFragment)
- ❌ Longer timeline
- ❌ More risk of breaking existing functionality

**Effort**: High (1-2 days)

---

## Part 6: Test Cases Missing

### Break Handling Test Suite

```csharp
[Fact]
public void SingleBrTag_ProducesHardBreak()
{
    var html = "<p>Line 1<br>Line 2</p>";
    var result = Convert(html);
    
    // Should respect HardBreak setting:
    if (HardBreak == Backslash)
        result.Should().Contain("Line 1\\\nLine 2");
    else
        result.Should().Contain("Line 1  \nLine 2");
}

[Fact]
public void MultipleBrTags_ProducesMultipleBreaks()
{
    var html = "<p>Line 1<br><br>Line 2</p>";
    var result = Convert(html);
    
    // Two breaks should create blank line
    result.Should().Contain("Line 1\n\nLine 2");
}

[Fact]
public void WhitespaceInHtml_RespectsSettings()
{
    var html = "<p>Line 1\nLine 2</p>";  // Newline in source
    
    if (SoftBreak == Relaxed)
        result.Should().Contain("Line 1 Line 2");  // Collapsed
    else
        result.Should().Contain("Line 1\nLine 2");  // Preserved
}

[Fact]
public void ConsecutiveParagraphs_SeparatedByBlankLine()
{
    var html = "<p>Para 1</p><p>Para 2</p>";
    var result = Convert(html);
    
    result.Should().Contain("Para 1\n\nPara 2");
}

[Fact]
public void BrBetweenBlocks_NotConsecutive()
{
    var html = "<p>Para 1</p><br><p>Para 2</p>";
    var result = Convert(html);
    
    // <br> between blocks: should it create triple break or single?
    // Define expected behavior
}

[Fact]
public void NestedBlocksWithBreaks_MaintainStructure()
{
    var html = "<div><p>P1<br>P1b</p><p>P2</p></div>";
    var result = Convert(html);
    
    // Breaks within div should be preserved
}
```

---

## Part 7: Recommendations

### Immediate (Current Session)

1. **Document the current limitation**
   - Note: HardBreak/SoftBreak settings not used in HTML converter
   - Paragraph breaks handled by MARKDOWN_BLOCK newlines only

2. **Add "Known Limitations" section to code**
   ```csharp
   // KNOWN LIMITATIONS:
   // - Multiple <br> tags not handled specially
   // - HardBreak/SoftBreak settings not applied (always outputs \n)
   // - <p> tags not treated as block elements (mixed with inline)
   // - Soft breaks in HTML not distinguished from hard breaks
   ```

3. **Document the Break Mapping** (this document!)
   - Include in design folder for future reference

### Short-term (Next Sprint)

1. **Implement Solution 2** (Paragraph preprocessing)
   - Handle `<p>` tags in PreprocessBlockElements
   - Add paragraph separator logic
   - Fixes `WordStyle_ParagraphsWithColorSpans` test

2. **Create Break Test Suite**
   - Ensure current behavior is well-tested
   - Define expected behavior for edge cases
   - Baseline for future refactoring

### Medium-term (Future Refactor)

1. **Implement Solution 1** (Break metadata)
   - If settings need to be respected
   - If multiple `<br>` handling needed
   - More test coverage first

2. **Consider HTML Parser Library**
   - If expanding beyond clipboard HTML
   - If nested structure becomes important
   - Timeline: Only if scope expands significantly

### Long-term (Major Refactor)

1. **Solution 3** (Separate block/inline parsing)
   - When HTML handling becomes complex
   - When supporting more HTML features
   - When performance optimizations needed

---

## Part 8: Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│ Clipboard HTML (from Word, browsers, Office)                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ ExtractFragment()                                            │
│ - Removes CF_HTML headers                                   │
│ - Extracts body content between <!--StartFragment-->        │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ PreprocessBlockElements()                                    │
│ ┌──────────────────────────────────────────────────────────┐│
│ │ For each block tag (h1-h6, p, hr, ul, ol, blockquote):  ││
│ │  1. Extract content                                      ││
│ │  2. Convert to Markdown (e.g., ### for h3)              ││
│ │  3. Wrap in <!--@MARKDOWN_BLOCK-->...<!--/@MARKDOWN_BLOCK-->││
│ │  4. Add \n after closing tag                            ││
│ └──────────────────────────────────────────────────────────┘│
│ Delegates: ListConverter.ConvertList() for ul/ol            │
│ Ignores: p tags (⚠️ Known issue)                            │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ ParseHtmlFragment()  [LINE-BY-LINE CHARACTER PARSING]        │
│ ┌──────────────────────────────────────────────────────────┐│
│ │ While pos < html.Length:                                 ││
│ │  ├─ MARKDOWN_BLOCK: Split by \n, create lines           ││
│ │  │  └─ [2026-08-06 FIX] Skip \n, create fresh currentLine││
│ │  ├─ Comments: Skip                                       ││
│ │  ├─ Tags: <span>, <strong>, <em>, <br>                 ││
│ │  │  └─ Manage style stack                               ││
│ │  ├─ Text: Accumulate in StringBuilder                    ││
│ │  ├─ Entities: Decode &nbsp;, &#123;, etc.              ││
│ │  └─ Whitespace: Collapse/normalize                       ││
│ │                                                          ││
│ │ Output: List<List<ColoredSegment>>                       ││
│ │  - Each outer list = one line                            ││
│ │  - Each ColoredSegment = text + (fg, bg, bold, italic)  ││
│ └──────────────────────────────────────────────────────────┘│
│ ⚠️ Missing: Break metadata (soft/hard/block)                │
│ ⚠️ Problem: <p> tags not handled as block elements         │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ ConvertToMarkdown()                                          │
│ ┌──────────────────────────────────────────────────────────┐│
│ │ For each line:                                            ││
│ │  1. Analyze uniform color                                ││
│ │  2. Group consecutive lines with same color              ││
│ │  3. Wrap in <!--@div fg:x-->...<!--/@div-->             ││
│ │  4. Join lines with \n                                   ││
│ │                                                          ││
│ │ Returns: Final markdown string with color tags           ││
│ └──────────────────────────────────────────────────────────┘│
│ ⚠️ Ignores: HardBreak/SoftBreak settings                    │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Markdown Output (with inline color comments)                │
│                                                              │
│ Example:                                                    │
│ ### Header                                                  │
│ <!--@fg:red-->Error message<!--/@fg-->                    │
│                                                              │
│ <!--@div fg:#4EBA65-->                                     │
│ Success line 1                                              │
│ Success line 2                                              │
│ <!--/@div-->                                                │
└─────────────────────────────────────────────────────────────┘
```

---

## Part 9: Open Questions

1. **Multiple consecutive breaks**: Should `<br><br>` be double hard break or paragraph break?
2. **Whitespace normalization**: When should HTML whitespace become space vs preserved?
3. **Nested block-inline mixing**: How should `<div><p>text<br>more</p></div>` be handled?
4. **Settings integration**: Should HtmlToMarkdownConverter receive MarkdownSettings?
5. **Performance**: Is character-by-character parsing sufficient for large files?
6. **Scope expansion**: If pasting HTML from sources other than clipboard, what changes?

---

## Part 10: References

**Related Files:**
- `RaisinDocs/Html/HtmlToMarkdownConverter.cs` - Main implementation
- `RaisinDocs/Html/HtmlParsingContext.cs` - Shared utilities
- `RaisinDocs/Html/BlockConverters/ListConverter.cs` - List handling
- `Tests/RaisinDocs.Tests/HtmlColorParserTests.cs` - Test suite
- `CLAUDE.md` - Project guidelines and settings

**Related Design Docs:**
- `HTML Emitter and CommonMark Conformance.md` - Reverse direction (Markdown → HTML)
- `RaisinDocs design v01.md` - Overall architecture

**External Resources:**
- [CommonMark Spec](https://spec.commonmark.org/) - Reference for Markdown semantics
- [HTML5 Spec](https://html.spec.whatwg.org/) - Reference for HTML semantics
- [HtmlAgilityPack](https://html-agility-pack.net/) - Alternative if switching to library
- [AngleSharp](https://github.com/AngleSharp/AngleSharp) - Standards-compliant HTML5 parser

---

## Changelog

| Date | Change | Status |
|------|--------|--------|
| 2026-08-06 | Fixed header/paragraph merging (MARKDOWN_BLOCK newline handling) | ✅ Complete |
| 2026-08-06 | Documented break handling architecture | 📄 This doc |
| TBD | Implement paragraph preprocessing (Solution 2) | ⏳ Planned |
| TBD | Add break metadata (Solution 1) | ⏳ Planned |
| TBD | Refactor to separate block/inline parsing (Solution 3) | ⏳ Future |

