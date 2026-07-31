# Code Review – 2026-07-30

**Scope**: 56 commits, 7,257 line diff across 8 files

**Level**: High severity

**Status**: All issues fixed ✓

---

## Critical Findings (FIXED)

### 1. ✓ Unhandled File.AppendAllText() in Mouse Wheel Event Handler
**File**: RaisinDocs/ScrollController.cs  
**Line**: 62  
**Status**: FIXED

**Removed** unhandled `File.AppendAllText()` debug logging calls from hot paths (lines 62 and 121).

---

### 2. ✓ Unhandled File.AppendAllText() in Animation Frame Loop
**File**: RaisinDocs/ScrollController.cs  
**Line**: 121  
**Status**: FIXED

**Removed** unhandled `File.AppendAllText()` debug logging in animation loop.

---

## High-Priority Findings (FIXED)

### 3. ✓ Incomplete Bounds Check Before _visualMaps Indexing
**File**: RaisinDocs/DocsCanvas.cs  
**Line**: 1678  
**Status**: FIXED

**Added** bounds check for `_visualMaps.Count` to prevent IndexOutOfRangeException:
```csharp
if (vl.BlockIndex >= _visualMaps.Count) return;
```

---

### 4. ✓ Null-Forgiving Operator Without Runtime Guard
**File**: RaisinDocs/DocsCanvas.cs  
**Line**: 2103-2104  
**Status**: FIXED

**Added** null check at start of `OnRender()`:
```csharp
if (_parsedBlocks == null)
    return;
```

**Removed** unsafe null-forgiving operator from line 2138.

---

### 5. ✓ Defensive Cursor Offset Clamping Masks Root Cause
**File**: RaisinDocs/DocsCanvas.VisualMode.cs  
**Line**: 208  
**Status**: FIXED

**Fixed root cause** in `ClampCursorBeforeTrailingHidden()` by clamping offset before processing:
```csharp
offset = Math.Min(offset, blockLen);
```

**Removed** defensive clamp from DocsCanvas.Input.cs (lines 326-330).

---

### 6. ✓ String Allocation in Hot Path Without Log Level Check
**File**: RaisinDocs/DocsCanvas.VisualMode.cs  
**Line**: 119+  
**Status**: FIXED

**Added log level checks** before string allocations in `SkipCursorOverHiddenRanges()`:
- Added `IsDebugEnabled` property to `IDocsLogger` interface
- Implemented `IsDebugEnabled` in `FileLogger`
- Wrapped all debug logging calls with `if (Logger?.IsDebugEnabled ?? false)` checks

---

## Medium-Priority Findings (FIXED)

### 7. ✓ Dead Parameters in BlockVisualMap.Compute()
**File**: RaisinDocs/BlockVisualMap.cs  
**Line**: 161  
**Status**: FIXED

**Removed** unused parameters:
- `double padding`
- `double listIndent`
- `Func<string, BlockKind, double>? measureReplacementPrefix`

**Updated** call site in DocsCanvas.cs (line 1011).

---

### 8. ✓ Duplicated nestingOffset Calculation
**File**: RaisinDocs/TextMeasurer.cs  
**Status**: FIXED

**Created helper method** `ComputeNestingOffset()` in TextMeasurer:
```csharp
internal double ComputeNestingOffset(string prefix, BlockKind blockKind) =>
    MeasureReplacementPrefix(prefix, blockKind) - ListIndent;
```

**Replaced** duplicated calculations in:
- DocsCanvas.cs (3 occurrences)
- DocsCanvas.Print.cs (3 occurrences)
- DocsCanvas.VisualMode.cs (2 occurrences)

---

## Build Verification

✅ **Build Status**: Success (after clean)
- RaisinDocs library: ✓ Compiled
- RaisinDocs.Editor: ✓ Compiled
- RaisinDocs.Tests: ✓ Passed
- All projects: ✓ Built successfully

---

## Summary

All 8 findings have been fixed:
- **2 Critical DOS vulnerabilities** — removed from hot paths
- **4 High-priority issues** — bounds checking, null safety, root cause fixes
- **2 Medium-priority issues** — DRY violation, dead code removal

**Total changes**:
- 8 files modified
- 2 critical security vulnerabilities eliminated
- 0 regressions (tests pass)
