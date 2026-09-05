using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles all layout computation for DocsCanvas including word wrapping, visual line building,
    /// paragraph grouping (soft breaks), and spacing calculations. Encapsulates the complex layout
    /// pipeline that transforms markdown blocks into visual lines for rendering.
    /// </summary>
    internal class LayoutEngine
    {
        private readonly ILayoutDataServices _layout;
        private readonly IDocumentServices _doc;
        private readonly IRenderingServices _rendering;
        private readonly IParsedContentServices _content;
        private readonly IVisualModeServices _visual;
        private readonly ILoggingServices _logging;
        private readonly ITableServices _table;
        private readonly IImageServices _image;
        private readonly DocsCanvas _canvas;

        public LayoutEngine(
            ILayoutDataServices layout,
            IDocumentServices doc,
            IRenderingServices rendering,
            IParsedContentServices content,
            IVisualModeServices visual,
            ILoggingServices logging,
            ITableServices table,
            IImageServices image,
            DocsCanvas canvas)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _visual = visual ?? throw new ArgumentNullException(nameof(visual));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        /// <summary>
        /// Main entry point for layout computation. Parses markdown, builds visual maps,
        /// and computes layout in the given width. Called whenever content or viewport changes.
        /// </summary>
        public void ComputeLayout()
        {
            if (!_layout.LayoutDirty) return;
            _layout.LayoutDirty = false;
            _rendering.Measure.EnsureMeasured(_canvas);

            // Stage timings for LayoutDiag. Every stage below runs over the whole document on
            // every keystroke, so which of them dominates is worth knowing on a real document
            // rather than a synthetic one. Mark is a timestamp and an early return when
            // diagnostics are off; there is no closure per stage.
            long _t = System.Diagnostics.Stopwatch.GetTimestamp();

            _content.ParsedBlocks ??= MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _rendering.SyntaxHighlighter);
            _t = LayoutDiag.Mark("parse", _t);

            // Merge paragraph lazy continuations in the Document (logical structure per CommonMark spec)
            // Only re-parse if that actually moved something. The rebuild was unconditional,
            // which made every keystroke parse the whole document twice: measured at 52 ms a
            // character on a 2895-block report, against 26 for one parse. A merge needs the
            // rebuild because it changes the block structure underneath the parse; no merge
            // leaves both the blocks and the parse above still current.
            if (_doc.Document.MergeParagraphContinuations(_content.ParsedBlocks))
            {
                _content.ParsedBlocks = MarkdownParser.Parse(i => _doc.GetBlockText(i), _doc.BlockCount, _rendering.SyntaxHighlighter);
                LayoutDiag.NoteReparse();
            }
            _t = LayoutDiag.Mark("merge", _t);
            _content.VisualMaps = null;

            // Build visual block structure for visual mode rendering
            if (_visual.IsVisual)
            {
                _content.VisualBlockStructure = VisualBlockStructure.Build(_content.ParsedBlocks, i => _doc.GetBlockText(i));
            }
            _t = LayoutDiag.Mark("structure", _t);

            if (_visual.IsVisual && _content.VisualMaps == null)
            {
                _content.VisualMaps = new List<BlockVisualMap>(_doc.BlockCount);
                Func<int, string> getText = _doc.GetBlockText;

                // Build parent map for O(1) parent lookup during visual map computation
                var parentMap = BlockVisualMap.BuildParentMap(_content.ParsedBlocks);

                for (int i = 0; i < _doc.BlockCount; i++)
                    _content.VisualMaps.Add(BlockVisualMap.Compute(_content.ParsedBlocks[i], getText(i), _content.ParsedBlocks, getText, parentMap));
            }
            _t = LayoutDiag.Mark("maps", _t);

            ComputeLayoutCore(_rendering.ActualWidth - DocsCanvas._padding * 2);
            _t = LayoutDiag.Mark("wrap", _t);

            if (_visual.IsVisual)
                _visual.ClampCursorAwayFromHidden();
            LayoutDiag.Mark("clamp", _t);

            LayoutDiag.EndPass(_doc.BlockCount, _layout.VisualLines.Count);
        }

        private void BuildParagraphGroups()
        {
            _layout.BlockToGroup = new Dictionary<int, DocsCanvas.ParagraphGroup>();

            // If we have VisualBlockStructure, try to use it to identify merged paragraphs
            if (_content.VisualBlockStructure != null)
            {
                bool createdAnyGroups = false;
                for (int vi = 0; vi < _content.VisualBlockStructure.Blocks.Count; vi++)
                {
                    var vblock = _content.VisualBlockStructure.Blocks[vi];
                    if (vblock.SourceBlockIndices.Count > 1)
                    {
                        EmitParagraphGroupFromVisualBlock(vblock);
                        createdAnyGroups = true;
                    }
                }
                // If we created groups from VisualBlockStructure, we're done
                if (createdAnyGroups)
                    return;
                // Otherwise fall through to original logic
            }

            // Original logic: detect paragraph continuations by analyzing content
            var groupBlocks = new List<int>();

            for (int bi = 0; bi <= _doc.BlockCount; bi++)
            {
                bool canContinue = false;
                if (bi < _doc.BlockCount && _content.ParsedBlocks![bi].Kind == BlockKind.Paragraph
                    && _doc.GetBlockLength(bi) > 0 && groupBlocks.Count > 0)
                {
                    int prev = groupBlocks[^1];
                    string prevText = _doc.GetBlockText(prev);
                    var prevParsed = _content.ParsedBlocks![prev];
                    int prevContentEnd = MarkdownParser.GetContentEnd(prevText);
                    bool prevHardBreak = MarkdownParser.IsTrailingHardBreak(prevParsed, prevText)
                        || (prevContentEnd >= 2 && prevText[prevContentEnd - 1] == ' ' && prevText[prevContentEnd - 2] == ' ');
                    if (!prevHardBreak)
                    {
                        bool hasEmptyBetween = false;
                        for (int mid = prev + 1; mid < bi; mid++)
                        {
                            if (_doc.GetBlockLength(mid) == 0) { hasEmptyBetween = true; break; }
                        }
                        canContinue = !hasEmptyBetween;
                    }
                }

                if (canContinue)
                {
                    groupBlocks.Add(bi);
                }
                else
                {
                    if (groupBlocks.Count >= 2)
                        EmitParagraphGroup(groupBlocks);
                    groupBlocks.Clear();
                    if (bi < _doc.BlockCount && _content.ParsedBlocks![bi].Kind == BlockKind.Paragraph
                        && _doc.GetBlockLength(bi) > 0)
                        groupBlocks.Add(bi);
                }
            }

            // After grouping consecutive blocks, check for single blocks that contain
            // internal newlines (merged continuations from MergeParagraphContinuations)
            for (int bi = 0; bi < _doc.BlockCount; bi++)
            {
                if (_layout.BlockToGroup != null && _layout.BlockToGroup.ContainsKey(bi))
                    continue;  // Already in a group

                if (_content.ParsedBlocks![bi].Kind == BlockKind.Paragraph)
                {
                    string blockText = _doc.GetBlockText(bi);
                    // Skip empty blocks and blocks with consecutive newlines (merged empty blocks)
                    // Only process actual text continuations like "sad\ns"
                    if (blockText.Length > 0 && blockText.Contains('\n') && !blockText.Contains("\n\n"))
                    {
                        // This block has internal newlines - it's a merged continuation
                        // Create a group for it
                        var singleBlockGroup = new List<int> { bi };
                        EmitParagraphGroup(singleBlockGroup);
                    }
                }
            }
        }

        private void EmitParagraphGroupFromVisualBlock(VisualBlock vblock)
        {
            var blockIndices = vblock.SourceBlockIndices;
            // Convert internal \n to "¶" (pilcrow only; visual space is rendered, not in text)
            var joinedText = vblock.MergedText.ToString().Replace("\n", "¶");

            // Build segments from source indices
            var segments = new DocsCanvas.JoinSegment[blockIndices.Count];
            var softBreakOffsets = new List<int>();
            int currentOffset = 0;

            for (int i = 0; i < blockIndices.Count; i++)
            {
                if (i > 0)
                {
                    // Soft break marker (¶) is at the position
                    softBreakOffsets.Add(currentOffset);
                    currentOffset += 1; // for "¶" (1 character; replaces \n 1-to-1)
                }

                int bi = blockIndices[i];
                string text = _doc.GetBlockText(bi);
                segments[i] = new DocsCanvas.JoinSegment(bi, currentOffset, text.Length);
                currentOffset += text.Length;
            }

            // Create BlockVisualMap for the merged text
            var mergedHiddenRanges = new List<HiddenRange>();
            if (_visual.IsVisual && _content.VisualMaps != null)
            {
                for (int i = 0; i < blockIndices.Count; i++)
                {
                    var map = _content.VisualMaps[blockIndices[i]];
                    foreach (var hr in map.HiddenRanges)
                        mergedHiddenRanges.Add(new HiddenRange(hr.Start + segments[i].OffsetInJoined, hr.Length));
                }
            }
            mergedHiddenRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

            var joinedParsed = new ParsedBlock
            {
                Kind = BlockKind.Paragraph,
                Runs = vblock.Runs,
                Images = vblock.Images,
                ColorSpans = vblock.ColorSpans,
            };
            var joinedMap = new BlockVisualMap(mergedHiddenRanges,
                images: vblock.Images,
                colorSpans: vblock.ColorSpans);

            var group = new DocsCanvas.ParagraphGroup
            {
                Segments = segments,
                JoinedText = joinedText,
                JoinedMap = joinedMap,
                JoinedParsed = joinedParsed,
                SoftBreakOffsets = softBreakOffsets.ToArray(),
            };

            foreach (var seg in segments)
                _layout.BlockToGroup![seg.BlockIndex] = group;
        }

        private void EmitParagraphGroup(List<int> blockIndices)
        {
            var sb = new System.Text.StringBuilder();
            var segments = new DocsCanvas.JoinSegment[blockIndices.Count];
            var softBreakOffsets = new List<int>();
            // Map from original position to display position for offset adjustments
            var positionMaps = new List<Dictionary<int, int>>();

            for (int i = 0; i < blockIndices.Count; i++)
            {
                if (i > 0)
                {
                    softBreakOffsets.Add(sb.Length);
                    sb.Append("¶");  // pilcrow only (visual space is rendered, not in text)
                }
                int bi = blockIndices[i];
                string text = _doc.GetBlockText(bi);
                int startPos = sb.Length;
                var posMap = new Dictionary<int, int>();

                // Handle internal newlines in merged blocks
                if (text.Contains('\n'))
                {
                    int displayPos = startPos;
                    int sourcePos = 0;
                    var parts = text.Split('\n');

                    for (int j = 0; j < parts.Length; j++)
                    {
                        if (j > 0)
                        {
                            softBreakOffsets.Add(sb.Length);
                            sb.Append("¶");  // pilcrow only (visual space is rendered, not in text)
                            sourcePos++;  // skip the \n
                            displayPos++;  // ¶ replaces \n 1-to-1
                        }

                        string part = parts[j];
                        sb.Append(part);
                        for (int k = 0; k < part.Length; k++)
                        {
                            posMap[sourcePos] = displayPos;
                            sourcePos++;
                            displayPos++;
                        }
                    }

                    segments[i] = new DocsCanvas.JoinSegment(bi, startPos, text.Length);
                }
                else
                {
                    sb.Append(text);
                    // For non-merged blocks, positions don't change
                    for (int k = 0; k < text.Length; k++)
                        posMap[k] = startPos + k;
                    segments[i] = new DocsCanvas.JoinSegment(bi, startPos, text.Length);
                }

                positionMaps.Add(posMap);
            }

            string joinedText = sb.ToString();

            var mergedRuns = new List<StyledRun>();
            var mergedImages = new List<InlineImage>();
            var mergedHiddenRanges = new List<HiddenRange>();
            var mergedColorSpans = new List<ColorSpan>();

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                var parsed = _content.ParsedBlocks![seg.BlockIndex];
                var map = _content.VisualMaps![seg.BlockIndex];
                var posMap = positionMaps[i];

                foreach (var run in parsed.Runs)
                {
                    var (displayStart, displayLength) = MapOffset(run.Start, run.Length, posMap, seg.OffsetInJoined);
                    mergedRuns.Add(new StyledRun(displayStart, displayLength, run.Style));
                }

                if (parsed.Images != null)
                {
                    foreach (var img in parsed.Images)
                    {
                        var (displayStart, displayLength) = MapOffset(img.Start, img.Length, posMap, seg.OffsetInJoined);
                        mergedImages.Add(new InlineImage(
                            displayStart, displayLength, img.AltText, img.Url, img.Title));
                    }
                }

                if (parsed.ColorSpans != null)
                {
                    foreach (var cs in parsed.ColorSpans)
                    {
                        var (displayStart, displayLength) = MapOffset(cs.Start, cs.Length, posMap, seg.OffsetInJoined);
                        mergedColorSpans.Add(new ColorSpan(
                            displayStart, displayLength, cs.Foreground, cs.Background));
                    }
                }

                foreach (var hr in map.HiddenRanges)
                {
                    var (displayStart, displayLength) = MapOffset(hr.Start, hr.Length, posMap, seg.OffsetInJoined);
                    mergedHiddenRanges.Add(new HiddenRange(displayStart, displayLength));
                }
            }

            mergedRuns.Sort((a, b) => a.Start.CompareTo(b.Start));
            mergedHiddenRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

            var joinedParsed = new ParsedBlock
            {
                Kind = BlockKind.Paragraph,
                Runs = mergedRuns,
                Images = mergedImages.Count > 0 ? mergedImages : null,
                ColorSpans = mergedColorSpans.Count > 0 ? mergedColorSpans : null,
            };
            var joinedMap = new BlockVisualMap(mergedHiddenRanges,
                images: mergedImages.Count > 0 ? mergedImages : null,
                colorSpans: mergedColorSpans.Count > 0 ? mergedColorSpans : null);

            var group = new DocsCanvas.ParagraphGroup
            {
                Segments = segments,
                JoinedText = joinedText,
                JoinedMap = joinedMap,
                JoinedParsed = joinedParsed,
                SoftBreakOffsets = softBreakOffsets.ToArray(),
            };

            foreach (var seg in segments)
                _layout.BlockToGroup![seg.BlockIndex] = group;
        }

        private (int displayStart, int displayLength) MapOffset(int sourceStart, int sourceLength, Dictionary<int, int> posMap, int segOffset)
        {
            if (posMap.Count == 0)
                return (segOffset + sourceStart, sourceLength);

            // Get display start position
            int displayStart = posMap.ContainsKey(sourceStart) ? posMap[sourceStart] : segOffset + sourceStart;

            // Calculate display end position
            int sourceEnd = sourceStart + sourceLength - 1;  // Last source position in the range
            int displayEnd;

            if (posMap.ContainsKey(sourceEnd))
            {
                displayEnd = posMap[sourceEnd];
            }
            else if (posMap.Count > 0)
            {
                // Extrapolate from the last mapped position
                int lastMappedSource = posMap.Keys.Max();
                int lastMappedDisplay = posMap[lastMappedSource];
                int unmappedDistance = sourceEnd - lastMappedSource;
                displayEnd = lastMappedDisplay + unmappedDistance;
            }
            else
            {
                displayEnd = segOffset + sourceEnd;
            }

            int displayLength = displayEnd - displayStart + 1;
            return (displayStart, displayLength);
        }

        internal void ComputeLayoutCore(double maxWidth)
        {
            _layout.VisualLines.Clear();
            _layout.LineYPositions.Clear();
            _table.TableColumnWidths.Clear();
            maxWidth = Math.Max(0, maxWidth);
            _layout.LayoutMaxWidth = maxWidth;

            if (_visual.IsVisual)
            {
                _layout.VisualLineSpacings = [];
                _table.TableRenderer.ComputeAllTableColumnWidths(maxWidth);
                BuildParagraphGroups();
            }

            // Identify which blocks are children of containers (used to skip during iteration)
            var childBlockIndices = new HashSet<int>();
            for (int bi = 0; bi < _doc.BlockCount; bi++)
            {
                var parsed = _content.ParsedBlocks![bi];
                if (parsed.Children != null)
                {
                    foreach (var child in parsed.Children)
                    {
                        // Find the flat index of this child
                        for (int ci = 0; ci < _doc.BlockCount; ci++)
                        {
                            if (_content.ParsedBlocks![ci] == child)
                            {
                                childBlockIndices.Add(ci);
                                break;
                            }
                        }
                    }
                }
            }

            // Process blocks, using hierarchy when available
            for (int bi = 0; bi < _doc.BlockCount; bi++)
            {
                var parsed = _content.ParsedBlocks![bi];

                if (_visual.IsVisual && parsed.IsSkippedInVisual)
                    continue;

                // Skip blocks that are children - they'll be processed via their parent's Children
                if (childBlockIndices.Contains(bi))
                    continue;

                // Process this block and its children recursively
                ProcessBlockAndChildren(bi, parsed, maxWidth, nestingDepth: 0, parentContentCol: 0);
            }

            double y = DocsCanvas._padding;
            for (int i = 0; i < _layout.VisualLines.Count; i++)
            {
                int bi = _layout.VisualLines[i].BlockIndex;
                if (i > 0 && bi != _layout.VisualLines[i - 1].BlockIndex)
                {
                    var curGroup = _layout.VisualLines[i].Group;
                    var prevGroup = _layout.VisualLines[i - 1].Group;
                    bool sameGroup = curGroup != null && prevGroup == curGroup;

                    if (!sameGroup)
                    {
                        bool paragraphBreak = false;
                        for (int prev = _layout.VisualLines[i - 1].BlockIndex; prev < bi && !paragraphBreak; prev++)
                        {
                            if (_doc.GetBlockLength(prev) == 0)
                                paragraphBreak = true;
                        }
                        if (paragraphBreak && _doc.GetBlockLength(_layout.VisualLines[i - 1].BlockIndex) > 0)
                            y += DocsCanvas._paragraphGap;
                    }
                }
                _layout.LineYPositions.Add(y);
                var lineVl = _layout.VisualLines[i];
                double lineH = _rendering.Measure.GetLineHeight(lineVl.BlockKind);
                if (lineVl.OverrideHeight > lineH) lineH = lineVl.OverrideHeight;
                y += lineH;
            }
            _layout.TotalContentHeight = y + DocsCanvas._padding;

            // Compute and cache spacing for each visual line (visual mode only)
            if (_visual.IsVisual && _layout.VisualLineSpacings != null)
            {
                foreach (var vl in _layout.VisualLines)
                {
                    _layout.VisualLineSpacings.Add(ComputeVisualLineSpacing(vl));
                }
            }

            _layout.LayoutVersion++;
        }

        private void ProcessBlockAndChildren(int blockIndex, ParsedBlock parsed, double maxWidth, int nestingDepth, int parentContentCol)
        {
            if (_visual.IsVisual && _layout.BlockToGroup != null && _layout.BlockToGroup.TryGetValue(blockIndex, out var group))
            {
                if (blockIndex == group.FirstBlock)
                {
                    WrapSegmentJoined(group, maxWidth);
                }
                return;
            }

            string text = _doc.GetBlockText(blockIndex);

            if (text.Length == 0)
            {
                _layout.VisualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, 0, parsed.Kind)
                {
                    OverrideHeight = DocsCanvas._paragraphGap,
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });

                // Process children of empty blocks
                if (parsed.Children != null)
                {
                    int childParentCol = nestingDepth > 0 ? parentContentCol : parsed.ContentColumn;
                    foreach (var child in parsed.Children)
                    {
                        int childIndex = FindBlockIndex(child);
                        if (childIndex >= 0)
                            ProcessBlockAndChildren(childIndex, child, maxWidth, nestingDepth + 1, childParentCol);
                    }
                }
                return;
            }

            var map = _visual.IsVisual ? _content.VisualMaps?[blockIndex] : null;

            if (_visual.IsVisual && parsed.Kind == BlockKind.ThematicBreak)
            {
                _layout.VisualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, text.Length, parsed.Kind)
                {
                    OverrideHeight = 20,
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });
                return;
            }

            if (_visual.IsVisual && parsed.Table != null && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                _layout.VisualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, text.Length, parsed.Kind)
                {
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });
                return;
            }

            var segments = text.Split('\n');
            int offset = 0;
            for (int s = 0; s < segments.Length; s++)
            {
                WrapSegment(blockIndex, offset, segments[s], maxWidth, parsed, map, nestingDepth, parentContentCol);
                offset += segments[s].Length + 1;
            }

            // Process children (skip paragraph continuations - they're rendered with parent)
            if (parsed.Children != null)
            {
                int childParentCol = nestingDepth > 0 ? parentContentCol : parsed.ContentColumn;
                foreach (var child in parsed.Children)
                {
                    // Skip rendering paragraph lazy continuations separately
                    if (parsed.Kind == BlockKind.Paragraph && child.Kind == BlockKind.Paragraph)
                        continue;

                    int childIndex = FindBlockIndex(child);
                    if (childIndex >= 0)
                        ProcessBlockAndChildren(childIndex, child, maxWidth, nestingDepth + 1, childParentCol);
                }
            }
        }

        private int FindBlockIndex(ParsedBlock block)
        {
            for (int i = 0; i < _doc.BlockCount; i++)
            {
                if (_content.ParsedBlocks![i] == block)
                    return i;
            }
            return -1;
        }

        private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth,
            ParsedBlock parsed, BlockVisualMap? map = null, int nestingDepth = 0, int parentContentCol = 0)
        {
            if (segment.Length == 0)
            {
                _layout.VisualLines.Add(new DocsCanvas.VisualLine(blockIndex, startOffset, 0, parsed.Kind)
                {
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });
                return;
            }

            // Every line of the block starts at its text column, the first one included, so they
            // all reach the right margin that much sooner than the width alone suggests. Measuring
            // the first line against the prefix width instead used to over-grant it space, and a
            // marker wider than the right padding had its last glyph clipped away.
            double contentIndent = 0;
            if (_visual.IsVisual && map != null)
            {
                double blockTextX = ComputeBlockTextX(parsed, map, nestingDepth, parentContentCol);
                contentIndent = ComputeBlockSpacing(parsed, map, blockTextX).ContentStartX - DocsCanvas._padding;
            }
            double lineMax = maxWidth - contentIndent;

            int pos = 0;
            while (pos < segment.Length)
            {
                int lineLen = FitLine(segment, pos, lineMax, parsed, map, startOffset);
                var vl = new DocsCanvas.VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind)
                {
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                };
                if (_visual.IsVisual && map?.Images != null)
                {
                    double imgH = GetImageMaxLineHeight(vl, map);
                    if (imgH > 0) vl = vl with { OverrideHeight = imgH };
                }
                else if (!_visual.IsVisual && _image.ImagePreview == DocsCanvas.ImagePreviewMode.Inline && parsed.Images != null)
                {
                    double imgH = GetSourceInlineImageHeight(vl, parsed.Images);
                    if (imgH > 0)
                        vl = vl with { OverrideHeight = _rendering.Measure.GetLineHeight(parsed.Kind) + imgH };
                }
                _layout.VisualLines.Add(vl);
                pos += lineLen;
            }
        }

        private void WrapSegmentJoined(DocsCanvas.ParagraphGroup group, double maxWidth)
        {
            string text = group.JoinedText;
            if (text.Length == 0)
            {
                _layout.VisualLines.Add(new DocsCanvas.VisualLine(group.FirstBlock, 0, 0, BlockKind.Paragraph)
                { Group = group });
                return;
            }

            var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
            int pos = 0;
            while (pos < text.Length)
            {
                int lineLen = FitLine(text, pos, maxWidth, group.JoinedParsed, group.JoinedMap,
                    softBreaks: softBreaks);
                var (bi, _) = group.JoinedToSource(pos);
                var vl = new DocsCanvas.VisualLine(bi, pos, lineLen, BlockKind.Paragraph) { Group = group };
                if (group.JoinedMap.Images != null)
                {
                    double imgH = GetImageMaxLineHeight(vl, group.JoinedMap);
                    if (imgH > 0) vl = vl with { OverrideHeight = imgH };
                }
                _layout.VisualLines.Add(vl);
                pos += lineLen;
            }
        }

        private int FitLine(string text, int start, double maxWidth, ParsedBlock parsed,
            BlockVisualMap? map = null, int blockOffset = 0, HashSet<int>? softBreaks = null)
        {
            int lastSpace = -1;
            double width = 0;
            int runIdx = 0;
            bool anyVisible = false;
            for (int i = start; i < text.Length; i++)
            {
                int rawOffset = blockOffset + i;
                if (map != null && map.IsHidden(rawOffset))
                {
                    var img = FindImageAtRawOffset(map.Images, rawOffset);
                    if (img != null)
                    {
                        var (imgW, _) = GetImageSize(img.Value, _layout.LayoutMaxWidth);
                        if (width + imgW > maxWidth && anyVisible && i > start)
                        {
                            if (lastSpace >= start)
                                return lastSpace - start + 1;
                            return i - start;
                        }
                        width += imgW;
                        anyVisible = true;
                        i += img.Value.Length - 1;
                    }
                    continue;
                }
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawOffset, ref runIdx);
                double charW = _rendering.Measure.MeasureCharWidth(text[i], parsed.Kind, style);

                // A soft-break pilcrow is rendered followed by a visual space (see
                // RenderingContext.DrawJoinedLine). The pilcrow itself is visible ink and
                // must fit, so check it before it can become this line's break point.
                bool isSoftBreak = text[i] == '¶' && softBreaks != null && softBreaks.Contains(rawOffset);
                if (isSoftBreak && width + charW > maxWidth && anyVisible && i > start)
                {
                    if (lastSpace >= start)
                        return lastSpace - start + 1;
                    return i - start;
                }

                if (text[i] is ' ' or '¶') lastSpace = i;
                width += charW;
                // The visual space after the pilcrow must be measured too, or the rendered
                // line ends up wider than maxWidth and its tail gets clipped. Like any
                // trailing space it is allowed to hang past the edge when it ends the line.
                if (isSoftBreak)
                    width += _rendering.Measure.MeasureCharWidth(' ', parsed.Kind, style);
                anyVisible = true;
                if (width > maxWidth && anyVisible && i > start)
                {
                    if (lastSpace >= start)
                        return lastSpace - start + 1;
                    return i - start;
                }
            }
            return text.Length - start;
        }

        internal BlockVisualSpacing ComputeVisualLineSpacing(DocsCanvas.VisualLine vl)
        {
            if (!_visual.IsVisual || _content.ParsedBlocks == null || _content.VisualMaps == null || vl.BlockIndex >= _content.ParsedBlocks.Count || vl.BlockIndex >= _content.VisualMaps.Count)
                return new BlockVisualSpacing { ContentStartX = DocsCanvas._padding };

            var parsed = _content.ParsedBlocks[vl.BlockIndex];
            var map = _content.VisualMaps[vl.BlockIndex];

            double textX = ComputeBlockTextX(parsed, map, vl.NestingDepth, vl.ParentContentColumn);
            var spacing = ComputeBlockSpacing(parsed, map, textX);

            if (vl.StartOffset == 0)
                return spacing;

            // A wrapped line draws no marker of its own; it only sits on the text column, which
            // is a property of the block and the same for every line the block produces.
            return new BlockVisualSpacing
            {
                MarkerStartX = textX,
                MarkerWidth = 0,
                SpacingAfterMarker = 0,
                ContentStartX = spacing.ContentStartX,
            };
        }

        /// <summary>
        /// Where the block's indentation starts, before any marker: the nesting indentation of the
        /// block hierarchy. Continuation blocks skip it, because they take their whole indentation
        /// from the owner they align to.
        /// </summary>
        private double ComputeBlockTextX(ParsedBlock parsed, BlockVisualMap map, int nestingDepth, int parentContentColumn)
        {
            if (nestingDepth <= 0 || map.IsContinuationIndent)
                return DocsCanvas._padding;

            double charWidth = _rendering.Measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);
            return DocsCanvas._padding + parentContentColumn * charWidth;
        }

        /// <summary>
        /// The block's marker position and text column. Every visual line of the block reads this:
        /// the first line draws its marker here and starts its text at <see
        /// cref="BlockVisualSpacing.ContentStartX"/>, and the wrapped lines hang on that same X.
        /// </summary>
        private BlockVisualSpacing ComputeBlockSpacing(ParsedBlock parsed, BlockVisualMap map, double textX)
        {
            if (parsed.Kind == BlockKind.Blockquote && map.ReplacementPrefix == null)
            {
                // Blockquote bar positioning
                var quoteAligner = new ContentBlockAligner(textX, _rendering.Measure.ListIndent);
                return new BlockVisualSpacing
                {
                    MarkerStartX = quoteAligner.GetBlockquoteBarX(),
                    MarkerWidth = 3,
                    SpacingAfterMarker = quoteAligner.GetSpacingAfterMarker(),
                    ContentStartX = quoteAligner.GetBlockquoteContentIndentX(),
                };
            }

            if (map.ReplacementPrefix == null)
            {
                return new BlockVisualSpacing
                {
                    MarkerStartX = textX,
                    MarkerWidth = 0,
                    SpacingAfterMarker = 0,
                    ContentStartX = textX,
                };
            }

            double prefixWidth = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

            // A continuation block carries its owner's prefix, so running the owner's geometry over
            // it puts the continuation on the owner's text column. It draws no marker itself.
            if (map.IsContinuationIndent)
            {
                if (!IsListItem(map.PrefixMeasureKind))
                    return new BlockVisualSpacing
                    {
                        MarkerStartX = textX,
                        MarkerWidth = 0,
                        SpacingAfterMarker = 0,
                        ContentStartX = textX + prefixWidth,
                    };

                var owner = ComputeListItemSpacing(map.PrefixMeasureKind, map.ReplacementPrefix,
                    TextMeasurer.GetNestingLevelFromPrefix(map.ReplacementPrefix));
                return new BlockVisualSpacing
                {
                    MarkerStartX = textX,
                    MarkerWidth = 0,
                    SpacingAfterMarker = 0,
                    ContentStartX = owner.ContentStartX,
                };
            }

            if (IsListItem(parsed.Kind))
                return ComputeListItemSpacing(parsed.Kind, map.ReplacementPrefix, parsed.ListNestingLevel);

            // Non-list markers (blockquotes, etc.)
            var aligner = new ContentBlockAligner(textX, _rendering.Measure.ListIndent);
            if (parsed.Kind == BlockKind.Blockquote)
                return new BlockVisualSpacing
                {
                    MarkerStartX = aligner.GetBlockquoteBarX(),
                    MarkerWidth = 3,
                    SpacingAfterMarker = aligner.GetSpacingAfterMarker(),
                    ContentStartX = aligner.GetBlockquoteContentIndentX(),
                };

            return new BlockVisualSpacing
            {
                MarkerStartX = textX,
                MarkerWidth = prefixWidth,
                SpacingAfterMarker = 0,
                ContentStartX = textX + prefixWidth,
            };
        }

        private static bool IsListItem(BlockKind kind) =>
            kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem or
                BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked;

        /// <summary>
        /// The list marker column. Bullets, checkboxes and ordered numbers all share one
        /// column, so every list kind starts its text at the same X:
        /// <list type="number">
        /// <item>Nesting indentation (from the nesting level)</item>
        /// <item>Fixed space before the marker (2 spaces)</item>
        /// <item>The column: bullets and checkboxes centred in it, numbers right-aligned to
        /// its right edge so a list's delimiters line up</item>
        /// <item>Fixed space after the marker</item>
        /// <item>Text content, at <see cref="BlockVisualSpacing.ContentStartX"/></item>
        /// </list>
        /// A number too wide for the column would have to start left of the margin, so instead
        /// it clamps there and pushes the column's right edge - and the text with it - to the
        /// right, keeping the gap after the marker constant. Each item does this on its own
        /// widest number; no block needs to know its siblings.
        /// </summary>
        private BlockVisualSpacing ComputeListItemSpacing(BlockKind kind, string prefix, int nestingLevel)
        {
            double spaceCharWidth = _rendering.Measure.MeasureCharWidth(' ', kind, InlineStyle.Normal);

            double nestingIndentWidth = nestingLevel > 0
                ? nestingLevel * BlockVisualMap.SpacesPerNestingLevel * spaceCharWidth
                : 0;

            const double spacesBeforeMarker = 2;
            double spaceBeforeMarkerWidth = spacesBeforeMarker * spaceCharWidth;

            // Wide enough for the checked checkbox and for a two-digit number, whichever asks
            // for more. The checkbox wins at present, which is why sharing the column costs
            // bullets and checkboxes nothing.
            double columnWidth = Math.Max(
                _rendering.Measure.MeasureReplacementPrefix("☑", kind),
                _rendering.Measure.MeasureReplacementPrefix("99.", kind));

            const double spacingAfterMarker = 4.0;

            double leftLimit = DocsCanvas._padding + nestingIndentWidth;
            double nominalRightX = leftLimit + spaceBeforeMarkerWidth + columnWidth;

            // A number may reach back over the space before the marker; past that it would
            // cross the margin, so the column grows to the right instead.
            double markerRightX = nominalRightX;
            if (kind == BlockKind.OrderedListItem)
            {
                double numberWidth = _rendering.Measure.MeasureReplacementPrefix(
                    OrderedMarkerText(prefix), kind);
                markerRightX = Math.Max(nominalRightX, leftLimit + numberWidth);
            }

            return new BlockVisualSpacing
            {
                MarkerStartX = markerRightX - (columnWidth / 2),
                MarkerWidth = columnWidth,
                MarkerRightX = markerRightX,
                SpacingAfterMarker = spacingAfterMarker,
                ContentStartX = markerRightX + spacingAfterMarker,
            };
        }

        /// <summary>
        /// The ink of an ordered marker - digits and delimiter, without the spaces the prefix
        /// carries for layout. That is what has to fit the column and what gets right-aligned.
        /// </summary>
        internal static string OrderedMarkerText(string prefix) => prefix.Trim();

        internal double GetTextStartXForVisualLine(DocsCanvas.VisualLine vl)
        {
            // Recovers the index by scanning. VisualLine is a record struct with eight
            // members, so every comparison is a full value equality - fine for the cursor
            // paths that call this a handful of times, but never call it from OnRender: use
            // the overload below, which takes the index the render loop already has.
            if (!_visual.IsVisual || _layout.VisualLineSpacings == null || vl.BlockIndex < 0)
                return DocsCanvas._padding;

            int vlIndex = -1;
            for (int i = 0; i < _layout.VisualLines.Count; i++)
            {
                if (_layout.VisualLines[i] == vl)
                {
                    vlIndex = i;
                    break;
                }
            }

            return GetTextStartXForVisualLine(vl, vlIndex);
        }

        /// <summary>
        /// The X the line's text starts at, for a line whose index is already known.
        /// </summary>
        internal double GetTextStartXForVisualLine(DocsCanvas.VisualLine vl, int vlIndex)
        {
            if (!_visual.IsVisual || _layout.VisualLineSpacings == null || vl.BlockIndex < 0)
                return DocsCanvas._padding;

            if (vlIndex < 0 || vlIndex >= _layout.VisualLineSpacings.Count)
                return DocsCanvas._padding;

            return _layout.VisualLineSpacings[vlIndex]?.ContentStartX ?? DocsCanvas._padding;
        }

        private (double Width, double Height) GetImageSize(InlineImage img, double maxWidth)
        {
            var cached = _image.ImageCache.Get(img.Url, _image.DocumentBasePath, maxWidth);
            if (cached != null)
                return (cached.Value.Width, cached.Value.Height);

            // See DocsCanvas.GetImageSize: a size read from the header means the decode only
            // has to repaint, rather than invalidate layout from under a running scroll.
            var known = _image.ImageCache.GetPixelSize(img.Url, _image.DocumentBasePath, maxWidth);
            _image.ImageCache.RequestLoad(img.Url, _image.DocumentBasePath,
                known != null ? () => _canvas.RedrawLinesWithImage(img.Url) : _layout.InvalidateLayout);
            return known ?? (20, 20);
        }

        private static InlineImage? FindImageAtRawOffset(IReadOnlyList<InlineImage>? images, int rawOffset)
        {
            if (images == null) return null;
            foreach (var img in images)
            {
                if (img.Start == rawOffset) return img;
                if (img.Start > rawOffset) break;
            }
            return null;
        }

        private double GetImageMaxLineHeight(DocsCanvas.VisualLine vl, BlockVisualMap? map)
        {
            if (map?.Images == null) return 0;
            double maxH = 0;
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in map.Images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                {
                    var (_, h) = GetImageSize(img, _layout.LayoutMaxWidth);
                    if (h > maxH) maxH = h;
                }
            }
            return maxH;
        }

        private double GetSourceInlineImageHeight(DocsCanvas.VisualLine vl, IReadOnlyList<InlineImage> images)
        {
            double totalH = 0;
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd)
                {
                    var (_, h) = GetImageSize(img, _layout.LayoutMaxWidth);
                    totalH += h;
                }
            }
            return totalH;
        }
    }
}
