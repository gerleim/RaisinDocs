# BlockVisualMap & BlockVisualSpacing Usage Map

## BlockVisualMap
**Purpose:** Per-block visual mapping that describes how to transform raw markdown text to visual display

**Created:** 
- `BlockVisualMap.Compute()` (static factory method)
- Called during layout computation for each block

**Stored in:**
- `DocsCanvas._visualMaps` — List<BlockVisualMap>, one per block

### Fields & Their Uses:
| Field | Used By | Purpose |
|-------|---------|---------|
| `HiddenRanges` | Rendering, Cursor, Search | Which raw text chars to hide (e.g., `**`, `#`, etc.) |
| `ReplacementPrefix` | Layout, Rendering, Measurement | What to display instead of hidden prefix (e.g., "● ", "  1. ") |
| `IsContinuationIndent` | Layout, Rendering | Is this a continuation line (affects indentation) |
| `PrefixMeasureKind` | Measurement | Block kind for measuring prefix width (affects character widths) |
| `Images` | Rendering, Layout | Inline images to render |
| `Links` | Rendering | Inline links (for tooltips/popups) |
| `ColorSpans` | Rendering | Color tags to apply |

### Major Usage Points:

1. **Layout Computation** (`DocsCanvas.ComputeLayout`)
   - Creates `_visualMaps` for all blocks
   - Uses to build visual lines with correct widths

2. **Rendering** (`DocsCanvas.OnRender`)
   - Visual mode: uses `ReplacementPrefix`, `HiddenRanges` for display
   - Accesses `Images`, `Links`, `ColorSpans` for styling
   - Uses with `DrawVisualLineWithImages`, `DrawTableRow`, etc.

3. **Cursor Navigation** (`DocsCanvas.VisualMode.cs`)
   - Uses `HiddenRanges` to skip over hidden chars
   - Uses `ReplacementPrefix` for positioning

4. **Searching** (`DocsCanvas.Find.cs`)
   - Uses `ReplacementPrefix` width for position calculations
   - Filters by visual vs raw offsets using `HiddenRanges`

5. **Printing** (`DocsCanvas.Print.cs`)
   - Creates new `_visualMaps` for printed output
   - Uses for layout, styling, image rendering

---

## BlockVisualSpacing
**Purpose:** Precomputed horizontal spacing for a visual line (where markers and content start)

**Created:**
- `ComputeVisualLineSpacing()` method
- Computed during layout, one per visual line

**Stored in:**
- `DocsCanvas._visualLineSpacings` — List<BlockVisualSpacing?>, one per visual line

### Fields:
| Field | Meaning |
|-------|---------|
| `MarkerStartX` | X position where marker (bullet, number, checkbox) starts |
| `MarkerWidth` | Width of the marker area |
| `SpacingAfterMarker` | Gap between marker and content |
| `ContentStartX` | X position where content text starts |

### Calculation Logic (ComputeVisualLineSpacing):
```
For list items with ReplacementPrefix:
  ContentStartX = textX + MeasureReplacementPrefix()
  
Where:
  textX = base position (padding + nesting offset)
  MeasureReplacementPrefix() = actual visual width of prefix
```

### Major Usage Points:

1. **Text Rendering** (`DocsCanvas.OnRender`, line 2214)
   - Uses `ContentStartX` to position actual content text
   - `dc.DrawText(ft, new Point(textX, lineY))`

2. **Cursor Navigation** (`GetTextStartXForVisualLine`)
   - Returns `ContentStartX` for cursor positioning
   - Falls back to `_padding` if spacing not computed

3. **Layout Calculations**
   - Used internally in `ComputeVisualLineSpacing()` to compute positions
   - Cached for reuse during rendering

---

## Data Flow for Ordered List Numbers

**Precomputation (Layout Time):**
```
1. BlockVisualMap.Compute() for "5. item"
   → Creates ReplacementPrefix = "  5. "
   
2. ComputeVisualLineSpacing()
   → Measures prefix: MeasureReplacementPrefix("  5. ")
   → Calculates: ContentStartX = _padding + prefixWidth
   → Stores in _visualLineSpacings
```

**Rendering Time:**
```
1. Get cached spacing: textX = GetTextStartXForVisualLine(vl)
   → Returns ContentStartX from _visualLineSpacings
   
2. Draw marker: DrawOrderedListNumber(dc, _padding, ...)
   → Uses _padding, not precomputed ContentStartX
   → Positions number based on ListIndent centering
   
3. Draw content: dc.DrawText(ft, new Point(textX, ...))
   → Uses precomputed ContentStartX
```

**MISMATCH:** Marker (step 2) uses `_padding` independently, but content (step 3) uses precomputed `ContentStartX` from prefix measurement. Different prefix widths → different `ContentStartX` → misaligned text.

---

## Problem & Solution Space

**Current Issue:**
- Precomputation: `ContentStartX = _padding + MeasureReplacementPrefix()`
- Rendering: Marker positioned via independent `ContentBlockAligner` calculation
- Result: Single-digit "1. " vs double-digit "10. " have different `ContentStartX` values

**Possible Solutions:**

1. **Fix Precomputation** (make all prefixes measure same width)
   - Need max number length in list
   - Can't use fixed max (lists can be infinite)
   - Could calculate max from actual list at Compute time

2. **Fix Rendering** (use precomputed spacing for markers)
   - Pass `textX` to marker drawing
   - But `textX` can fall back to `_padding` in edge cases

3. **Refactor BlockVisualMap** (separate data from computation)
   - Extract prefix metadata into separate struct
   - Extract inline content into separate struct
   - Clearer responsibilities

