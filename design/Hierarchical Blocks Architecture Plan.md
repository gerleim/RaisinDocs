# Architectural Refactor Plan: Hierarchical Blocks for List Items

## Executive Summary

Current architecture treats markdown as flat lines, each becoming one ParsedBlock. This doesn't support CommonMark's model where list items are **containers** holding nested blocks (code, paragraphs, nested lists, blockquotes, etc.).

**Goal:** Restructure parser and renderer to support hierarchical block nesting.

**Scope:** 4 phases, ~14-23 hours, starting with design then additive implementation.

---

## Current Architecture

### Flat Block Model

Each markdown line is classified and becomes a ParsedBlock:

```
Line 1: "- foo"          → ParsedBlock(Kind=UnorderedListItem, text="foo")
Line 2: "  bar"          → ParsedBlock(Kind=Paragraph, text="bar", OwnerBlock=0)
Line 3: "-     code"     → ParsedBlock(Kind=UnorderedListItem, text="code")
```

**Continuation Logic:**
- Lazy continuation: line with no indentation follows list item
- Indented continuation: line indented ≥ content column after blank line
- Tracked via `OwnerBlock` and `IsLazyContinuation` / `IsIndentedContinuation` flags

**Limitation:** Can't represent list item containing both a paragraph AND a code block, or nested lists within a list item.

---

## Target Architecture

### Hierarchical Block Model

List items and other containers hold child blocks:

```
ParsedBlock(Kind=UnorderedListItem, Children=[
  ParsedBlock(Kind=Paragraph, text="foo"),
  ParsedBlock(Kind=IndentedCodeLine, text="code")
])
ParsedBlock(Kind=UnorderedListItem, Children=[
  ParsedBlock(Kind=Paragraph, text="baz")
])
```

**Benefits:**
- Naturally represents CommonMark structure
- Simplifies rendering (recurse through hierarchy)
- Handles code blocks in list items (5+ spaces)
- No continuation tracking complexity

---

## Key Changes Required

### 1. ParsedBlock Structure

**Remove:**
```csharp
public int OwnerBlock { get; init; }
public bool IsLazyContinuation { get; init; }
public bool IsIndentedContinuation { get; init; }
```

**Add:**
```csharp
public IReadOnlyList<ParsedBlock>? Children { get; init; }
```

**Result:**
```csharp
public record class ParsedBlock {
    public required BlockKind Kind { get; init; }
    public IReadOnlyList<ParsedBlock>? Children { get; init; }  // NEW
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public int ContentColumn { get; init; }
    public int LeadingSpaces { get; init; }
    public int ListNestingLevel { get; init; }
    // ... other properties (Images, Links, etc.)
    // REMOVED: OwnerBlock, IsLazyContinuation, IsIndentedContinuation
}
```

**Impact:** Medium - breaking change, but clean up old properties

### 2. Parser Pipeline

**Current:**
1. `ClassifyBlockContent()` - assign Kind to each line
2. `DetectContinuations()` - link via OwnerBlock
3. `DetectIndentedCode()` - convert IndentedCodeLine chains
4. Flatten result for rendering

**Target:**
1. `ClassifyBlockContent()` - assign Kind to each line (unchanged)
2. `BuildHierarchy()` - NEW: convert flat list to tree
   - Group blocks by indentation and parent content column
   - Nest children under container blocks (list items, blockquotes)
   - Recognize 5+ space markers as code blocks
3. Recursive rendering traverses tree

**Algorithm Outline:**

```csharp
List<ParsedBlock> BuildHierarchy(List<ParsedBlock> flatBlocks, 
                                 Func<int, string> getBlockText)
{
    var result = new List<ParsedBlock>();
    int i = 0;
    
    while (i < flatBlocks.Count) {
        var block = flatBlocks[i];
        
        if (IsContainerBlock(block.Kind)) {
            // Collect children based on indentation
            var children = CollectChildren(flatBlocks, i, block.ContentColumn, 
                                          out int nextIndex);
            
            // Recursively build hierarchy for children
            var nestedChildren = BuildHierarchy(children, getBlockText);
            
            // Create container with children
            var containerBlock = block with { Children = nestedChildren };
            result.Add(containerBlock);
            i = nextIndex;
        } else {
            result.Add(block);
            i++;
        }
    }
    
    return result;
}

List<ParsedBlock> CollectChildren(List<ParsedBlock> blocks, int containerIdx,
                                  int containerContentCol, out int nextIdx)
{
    var children = new List<ParsedBlock>();
    int j = containerIdx + 1;
    
    while (j < blocks.Count) {
        var nextBlock = blocks[j];
        
        // Determine if block is a child (indented ≥ content column)
        if (ShouldNestBlock(nextBlock, containerContentCol)) {
            children.Add(nextBlock);
            j++;
        } else {
            break;
        }
    }
    
    nextIdx = j;
    return children;
}

bool ShouldNestBlock(ParsedBlock block, int parentContentColumn)
{
    // Skip blank blocks
    if (block.Kind == BlockKind.Paragraph && block.IsEmpty) return true;
    
    // List items and other containers nest if indented
    // Paragraphs nest if indented ≥ parent content column
    // Blockquotes nest if indented ≥ parent content column
    
    int blockIndent = block.LeadingSpaces + ... ;  // calculate
    return blockIndent >= parentContentColumn;
}
```

### 3. Rendering Changes

**Current:**
```csharp
OnRender(DrawingContext dc) {
    for each VisualLine vl in _visualLines {
        get displayText via BuildDisplayString()
        draw text
    }
}
```

**Target:**
```csharp
OnRender(DrawingContext dc) {
    RenderBlocks(_parsedBlocks, nestingLevel: 0, dc: dc);
}

void RenderBlocks(IList<ParsedBlock> blocks, int nestingLevel, DrawingContext dc) {
    for each block in blocks {
        RenderBlock(block, nestingLevel, dc);
    }
}

void RenderBlock(ParsedBlock block, int nestingLevel, DrawingContext dc) {
    // Compute indentation from nesting level
    double leftIndent = ComputeIndentation(nestingLevel, block.ContentColumn);
    
    // Draw block's own content (marker, text)
    DrawBlockContent(block, leftIndent, dc);
    
    // Recursively render children
    if (block.Children != null && block.Children.Count > 0) {
        RenderBlocks(block.Children, nestingLevel + 1, dc);
    }
}
```

### 4. Visual Line Generation

**Current:**
- One visual line per text line
- Indentation via `ReplacementPrefix`

**Target:**
- Still generate flat visual line list (for rendering efficiency)
- But generated from hierarchical block tree
- Visual indentation = nesting depth × indent size + block's own indentation

```csharp
List<VisualLine> ComputeVisualLinesFromHierarchy(List<ParsedBlock> blocks, 
                                                 int nestingLevel = 0)
{
    var visualLines = new List<VisualLine>();
    
    for each block in blocks {
        double blockIndent = nestingLevel * NESTING_INDENT + block.ContentColumn;
        
        // Generate visual lines for this block's content
        AddVisualLinesForBlock(block, blockIndent, visualLines);
        
        // Recursively handle children
        if (block.Children != null) {
            var childLines = ComputeVisualLinesFromHierarchy(block.Children, 
                                                            nestingLevel + 1);
            visualLines.AddRange(childLines);
        }
    }
    
    return visualLines;
}
```

---

## Container Block Types

### Phase 2 (Must Have)
- `BlockKind.UnorderedListItem` - can contain paragraphs, code blocks, nested lists
- `BlockKind.OrderedListItem` - same as unordered
- `BlockKind.TaskListItemUnchecked` - same as unordered
- `BlockKind.TaskListItemChecked` - same as unordered

### Phase 2+ (Nice to Have)
- `BlockKind.Blockquote` - can contain any blocks
- `BlockKind.FencedCode` - special case, raw content block

### Not Containers
- `BlockKind.Heading*` - leaf nodes
- `BlockKind.Paragraph` - leaf nodes
- `BlockKind.IndentedCodeLine` - leaf nodes
- `BlockKind.TableHeaderRow` / `TableDataRow` - leaf nodes

---

## Implementation Phases

### Phase 1: Design & Validation ⏳ (2-4 hours)

**Goals:**
- Finalize ParsedBlock hierarchy shape
- Write detailed pseudocode for BuildHierarchy()
- Identify edge cases and corner cases
- Define `ShouldNestBlock()` rules precisely

**Deliverables:**
- Refined algorithm pseudocode
- Decision on when to remove OwnerBlock (Phase 4 vs later)
- List of edge cases to test

**Key Decisions:**
1. Can a Paragraph contain children? (No - only containers can)
2. How do we detect block boundaries in hierarchical model?
3. When do we stop collecting children?

### Phase 2: Add Hierarchy Support ⏳ (4-6 hours)

**Goals:**
- Implement `BuildHierarchy()` method
- Add `Children` property to ParsedBlock
- Ensure backward compatibility (old rendering still works)

**Changes:**
1. Add `Children: IReadOnlyList<ParsedBlock>?` to ParsedBlock record
2. Implement `BuildHierarchy()` after current block classification
3. Keep flat rendering path intact (don't break OnRender yet)
4. Update test hooks to handle hierarchical blocks

**Testing:**
- Unit tests for `BuildHierarchy()` with known inputs
- Spot-check: does `-   foo\n  bar` create hierarchy correctly?
- Spot-check: does `-     code` (5 spaces) nest correctly?

**Commit:** "Add hierarchical block support (Phase 2)"

**Not yet done:**
- Rendering still uses old flat paths
- `OwnerBlock` still exists (backward compat)
- No visual changes yet

### Phase 3: Update Rendering 🎨 (6-10 hours)

**Goals:**
- Implement recursive `RenderBlock()`
- Update visual line computation
- Handle code block styling (5+ spaces)
- Test indentation with nested blocks

**Changes:**
1. Rewrite `OnRender()` to call `RenderBlocks()`
2. Implement `RenderBlock()` with recursion
3. Update `ComputeLayout()` to generate visual lines from hierarchy
4. Apply code formatting to IndentedCodeLine blocks within list items
5. Verify indentation calculations per nesting level

**Testing:**
- Render list with nested list (should see indentation)
- Render list with code block (should see code styling)
- Render task list with paragraphs (should render correctly)
- Regression: ensure existing tests still pass

**Commit:** "Implement hierarchical rendering (Phase 3)"

### Phase 4: Cleanup & Remove Old Logic 🧹 (2-3 hours)

**Goals:**
- Remove `OwnerBlock`, `IsLazyContinuation`, `IsIndentedContinuation`
- Simplify `DetectContinuations()` (may become obsolete)
- Clean up test infrastructure

**Changes:**
1. Remove continuation-related properties from ParsedBlock
2. Simplify or remove `DetectContinuations()` method
3. Update BlockVisualMap (may no longer need continuation logic)
4. Update test hooks

**Testing:**
- Full test suite - may need many test updates
- Verify no regressions in existing functionality

**Commit:** "Remove flat continuation logic (Phase 4)"

---

## Algorithm Details

### BuildHierarchy() Pseudocode (Detailed)

```csharp
/// Converts flat block list into hierarchical tree
List<ParsedBlock> BuildHierarchy(List<ParsedBlock> flatBlocks,
                                 Func<int, string> getBlockText)
{
    var result = new List<ParsedBlock>();
    int i = 0;
    
    while (i < flatBlocks.Count) {
        var block = flatBlocks[i];
        
        // Only containers can have children
        if (!IsContainerBlock(block.Kind)) {
            result.Add(block);
            i++;
            continue;
        }
        
        // For containers, collect all following blocks that belong to it
        var (children, nextIndex) = CollectChildBlocks(flatBlocks, i);
        
        // Recursively build hierarchy within children
        var nestedChildren = BuildHierarchy(children, getBlockText);
        
        // Create new container with children
        var containerBlock = block with { 
            Children = nestedChildren.Count > 0 ? nestedChildren : null
        };
        result.Add(containerBlock);
        
        i = nextIndex;
    }
    
    return result;
}

(List<ParsedBlock> children, int nextIndex) CollectChildBlocks(
    List<ParsedBlock> blocks, int containerIdx)
{
    var container = blocks[containerIdx];
    var children = new List<ParsedBlock>();
    int j = containerIdx + 1;
    
    // Collect blocks following container until indentation decreases
    while (j < blocks.Count) {
        var nextBlock = blocks[j];
        
        // Empty blocks (blank lines) are part of container
        if (IsEmptyBlock(nextBlock)) {
            children.Add(nextBlock);
            j++;
            continue;
        }
        
        // Check indentation
        int nextIndent = GetAbsoluteIndentation(nextBlock);
        int containerContentCol = container.ContentColumn;
        
        // Block belongs to container if it's indented at least to content column
        if (nextIndent >= containerContentCol) {
            // Special case: list item at same level as container starts new item
            if (IsListItem(nextBlock) && 
                GetAbsoluteIndentation(nextBlock) == container.LeadingSpaces) {
                break;  // End this container's children
            }
            
            children.Add(nextBlock);
            j++;
        } else {
            break;  // Indentation decreased, end children
        }
    }
    
    return (children, j);
}

bool IsContainerBlock(BlockKind kind)
{
    return kind is BlockKind.UnorderedListItem
        or BlockKind.OrderedListItem
        or BlockKind.TaskListItemUnchecked
        or BlockKind.TaskListItemChecked
        or BlockKind.Blockquote;
}

bool IsEmptyBlock(ParsedBlock block)
{
    // Empty paragraphs (from blank lines)
    if (block.Kind == BlockKind.Paragraph && 
        GetBlockText(block).Trim().Length == 0) {
        return true;
    }
    return false;
}

int GetAbsoluteIndentation(ParsedBlock block)
{
    return block.LeadingSpaces;
}
```

---

## Edge Cases & Decisions

### 1. Blank Lines Within List Items

**Markdown:**
```
- foo

- bar
```

**Current:** Two separate list items

**Target:** Still two separate list items (blank line doesn't separate children, it ends the container)

**Decision:** Empty paragraph blocks end containers, they don't become children

### 2. Nested Lists

**Markdown:**
```
- foo
  - bar
  - baz
- qux
```

**Current:** List item "foo" has lazy continuation of sub-list

**Target:** List item "foo" has children: ["bar", "baz" items]

**Decision:** Child list items are recognized as children based on indentation ≥ content column

### 3. Code Blocks in List Items (5+ spaces)

**Markdown:**
```
-     code
    more code
```

**Current:** List item with weird spacing

**Target:** List item with IndentedCodeLine children

**Decision:** Detect 5+ spaces in `GetMarkerSpacing()`, set ContentColumn to markerEnd + 4 (done in Phase 1 fix), classify as code in BuildHierarchy()

### 4. Multiple Paragraphs in List Item

**Markdown:**
```
- foo

  bar
```

**Current:** "foo" is list item, "bar" is indented continuation

**Target:** List item "foo" has children: [Paragraph("foo"), Paragraph("bar")]

**Decision:** Blank line doesn't end container; following indented block is still a child

### 5. Mixed Content Types

**Markdown:**
```
- foo
  > quote
  - nested list item
    code

- new item
```

**Current:** Complex continuation tracking

**Target:** 
```
ListItem("foo", Children=[
  Blockquote("> quote"),
  ListItem("nested", Children=[...]),
  IndentedCodeLine("code")
])
ListItem("new item")
```

**Decision:** All indented blocks are potential children; BuildHierarchy() sorts them

---

## Risks & Mitigation

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Parser becomes complex | HIGH | Detailed Phase 1 design, write pseudocode carefully |
| Rendering breaks | HIGH | Phase 2 doesn't change rendering; Phase 3 does with tests |
| Tests fail en masse | MEDIUM | Expect many test updates in Phase 4; plan for it |
| Indentation calculation bugs | MEDIUM | Unit test ComputeIndentation() separately |
| Backward compat issues | LOW | Phase 2 is additive; old properties still exist |
| Performance regression | LOW | Recursive rendering should be similar to iterative |

---

## Testing Strategy

### Phase 1
- No code changes, no tests needed
- Peer review of pseudocode

### Phase 2
- **Unit tests for BuildHierarchy():**
  - Simple list with paragraph: `[ListItem("foo"), Paragraph("bar")]`
  - Nested lists: `[ListItem("foo"), nested list item]`
  - Code block in list (5+ spaces)
  - Empty blocks
  
- **Integration tests:**
  - Load CommonMark examples, verify hierarchy structure
  - Spot-check 2-3 complex examples manually

### Phase 3
- **Rendering regression tests:**
  - Capture before/after screenshots
  - Compare visual output for known examples
  
- **Indentation tests:**
  - Verify nested list has extra indentation
  - Verify code block in list is indented
  
- **Unit tests:**
  - `ComputeIndentation()` with various nesting levels
  - `RenderBlock()` with children

### Phase 4
- **Full test suite run:**
  - Expect failures in tests that check old properties
  - Update assertions to work with hierarchy
  - Run CommonMark test suite, verify improvements

---

## Success Criteria

- [x] Phase 1: Design documented and agreed upon
- [ ] Phase 2: `BuildHierarchy()` implemented and tested
- [ ] Phase 3: Rendering works recursively, indentation correct
- [ ] Phase 4: Old logic removed, tests updated
- [ ] List items with 5+ space markers show code blocks (visual)
- [ ] Nested lists render with correct per-level indentation
- [ ] No regression in existing passing tests
- [ ] CommonMark test suite improvements (more passing tests)

---

## Estimated Timeline

- **Phase 1 (Design):** 2-4 hours
- **Phase 2 (Hierarchy):** 4-6 hours
- **Phase 3 (Rendering):** 6-10 hours
- **Phase 4 (Cleanup):** 2-3 hours

**Total:** ~14-23 focused hours

**Realistic calendar time:** 2-4 weeks (depending on complexity during implementation)

---

## Open Questions

1. **Blockquotes as containers?** Should blockquotes have children, or stay flat?
2. **Task lists as real list items?** Or keep separate?
3. **Performance concern:** Is recursive rendering a concern for very deep nesting?
4. **Testing:** How extensively should we test hierarchy before Phase 3 rendering?

---

## Approval & Sign-Off

**Date:** [TBD]
**Reviewed by:** [User name]
**Status:** ⏳ Awaiting approval to proceed with Phase 1
