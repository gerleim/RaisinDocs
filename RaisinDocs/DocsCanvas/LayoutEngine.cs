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
        private readonly IDocsCanvasServices _services;

        public LayoutEngine(IDocsCanvasServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>
        /// Main entry point for layout computation. Parses markdown, builds visual maps,
        /// and computes layout in the given width. Called whenever content or viewport changes.
        /// </summary>
        public void ComputeLayout()
        {
            if (!((DocsCanvas)_services)._layoutDirty) return;
            ((DocsCanvas)_services)._layoutDirty = false;
            ((DocsCanvas)_services)._measure.EnsureMeasured((DocsCanvas)_services);

            ((DocsCanvas)_services)._parsedBlocks ??= MarkdownParser.Parse(i => ((DocsCanvas)_services)._doc.GetBlockText(i), ((DocsCanvas)_services)._doc.BlockCount, ((DocsCanvas)_services)._syntaxHighlighter);

            // Merge paragraph lazy continuations in the Document (logical structure per CommonMark spec)
            ((DocsCanvas)_services)._doc.MergeParagraphContinuations(((DocsCanvas)_services)._parsedBlocks);

            // After merging, always rebuild parsedBlocks to reflect current block structure and content
            ((DocsCanvas)_services)._parsedBlocks = MarkdownParser.Parse(i => ((DocsCanvas)_services)._doc.GetBlockText(i), ((DocsCanvas)_services)._doc.BlockCount, ((DocsCanvas)_services)._syntaxHighlighter);
            ((DocsCanvas)_services)._visualMaps = null;

            // Build visual block structure for visual mode rendering
            if (_services.IsVisual)
            {
                ((DocsCanvas)_services)._visualBlockStructure = VisualBlockStructure.Build(((DocsCanvas)_services)._parsedBlocks, i => ((DocsCanvas)_services)._doc.GetBlockText(i));
            }

            if (_services.IsVisual && ((DocsCanvas)_services)._visualMaps == null)
            {
                ((DocsCanvas)_services)._visualMaps = new List<BlockVisualMap>(((DocsCanvas)_services)._doc.BlockCount);
                Func<int, string> getText = ((DocsCanvas)_services)._doc.GetBlockText;

                // Build parent map for O(1) parent lookup during visual map computation
                var parentMap = BlockVisualMap.BuildParentMap(((DocsCanvas)_services)._parsedBlocks);

                for (int i = 0; i < ((DocsCanvas)_services)._doc.BlockCount; i++)
                    ((DocsCanvas)_services)._visualMaps.Add(BlockVisualMap.Compute(((DocsCanvas)_services)._parsedBlocks[i], getText(i), ((DocsCanvas)_services)._parsedBlocks, getText, parentMap));
            }

            ComputeLayoutCore(_services.ActualWidth - DocsCanvas._padding * 2);

            if (_services.IsVisual)
                _services.ClampCursorAwayFromHidden();
        }

        private void BuildParagraphGroups()
        {
            ((DocsCanvas)_services)._blockToGroup = new Dictionary<int, DocsCanvas.ParagraphGroup>();

            // If we have VisualBlockStructure, try to use it to identify merged paragraphs
            if (((DocsCanvas)_services)._visualBlockStructure != null)
            {
                bool createdAnyGroups = false;
                for (int vi = 0; vi < ((DocsCanvas)_services)._visualBlockStructure.Blocks.Count; vi++)
                {
                    var vblock = ((DocsCanvas)_services)._visualBlockStructure.Blocks[vi];
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

            for (int bi = 0; bi <= ((DocsCanvas)_services)._doc.BlockCount; bi++)
            {
                bool canContinue = false;
                if (bi < ((DocsCanvas)_services)._doc.BlockCount && ((DocsCanvas)_services)._parsedBlocks![bi].Kind == BlockKind.Paragraph
                    && ((DocsCanvas)_services)._doc.GetBlockLength(bi) > 0 && groupBlocks.Count > 0)
                {
                    int prev = groupBlocks[^1];
                    string prevText = ((DocsCanvas)_services)._doc.GetBlockText(prev);
                    var prevParsed = ((DocsCanvas)_services)._parsedBlocks![prev];
                    int prevContentEnd = MarkdownParser.GetContentEnd(prevText);
                    bool prevHardBreak = MarkdownParser.IsTrailingHardBreak(prevParsed, prevText)
                        || (prevContentEnd >= 2 && prevText[prevContentEnd - 1] == ' ' && prevText[prevContentEnd - 2] == ' ');
                    if (!prevHardBreak)
                    {
                        bool hasEmptyBetween = false;
                        for (int mid = prev + 1; mid < bi; mid++)
                        {
                            if (((DocsCanvas)_services)._doc.GetBlockLength(mid) == 0) { hasEmptyBetween = true; break; }
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
                    if (bi < ((DocsCanvas)_services)._doc.BlockCount && ((DocsCanvas)_services)._parsedBlocks![bi].Kind == BlockKind.Paragraph
                        && ((DocsCanvas)_services)._doc.GetBlockLength(bi) > 0)
                        groupBlocks.Add(bi);
                }
            }

            // After grouping consecutive blocks, check for single blocks that contain
            // internal newlines (merged continuations from MergeParagraphContinuations)
            for (int bi = 0; bi < ((DocsCanvas)_services)._doc.BlockCount; bi++)
            {
                if (((DocsCanvas)_services)._blockToGroup != null && ((DocsCanvas)_services)._blockToGroup.ContainsKey(bi))
                    continue;  // Already in a group

                if (((DocsCanvas)_services)._parsedBlocks![bi].Kind == BlockKind.Paragraph)
                {
                    string blockText = ((DocsCanvas)_services)._doc.GetBlockText(bi);
                    // Skip empty blocks and blocks with consecutive newlines (merged empty blocks)
                    // Only process actual text continuations like "sad\ns"
                    if (blockText.Length > 0 && blockText.Contains('\n') && !blockText.Contains("\n\n"))
                    {
                        _services.Logger?.Log(DocsLogLevel.Debug, $"Continuation: Block {bi} has internal newline");
                        // This block has internal newlines - it's a merged continuation
                        // Create a group for it
                        var singleBlockGroup = new List<int> { bi };
                        EmitParagraphGroup(singleBlockGroup);
                        _services.Logger?.Log(DocsLogLevel.Debug, $"Continuation: Created ParagraphGroup for block {bi}");
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
                string text = ((DocsCanvas)_services)._doc.GetBlockText(bi);
                segments[i] = new DocsCanvas.JoinSegment(bi, currentOffset, text.Length);
                currentOffset += text.Length;
            }

            // Create BlockVisualMap for the merged text
            var mergedHiddenRanges = new List<HiddenRange>();
            if (_services.IsVisual && ((DocsCanvas)_services)._visualMaps != null)
            {
                for (int i = 0; i < blockIndices.Count; i++)
                {
                    var map = ((DocsCanvas)_services)._visualMaps[blockIndices[i]];
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
                ((DocsCanvas)_services)._blockToGroup![seg.BlockIndex] = group;
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
                string text = ((DocsCanvas)_services)._doc.GetBlockText(bi);
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
                var parsed = ((DocsCanvas)_services)._parsedBlocks![seg.BlockIndex];
                var map = ((DocsCanvas)_services)._visualMaps![seg.BlockIndex];
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
                ((DocsCanvas)_services)._blockToGroup![seg.BlockIndex] = group;
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
            ((DocsCanvas)_services)._visualLines.Clear();
            ((DocsCanvas)_services)._lineYPositions.Clear();
            ((DocsCanvas)_services)._tableColumnWidths.Clear();
            maxWidth = Math.Max(0, maxWidth);
            ((DocsCanvas)_services)._layoutMaxWidth = maxWidth;

            if (_services.IsVisual)
            {
                ((DocsCanvas)_services)._visualLineSpacings = [];
                ((DocsCanvas)_services)._tableRenderer.ComputeAllTableColumnWidths(maxWidth);
                BuildParagraphGroups();
            }

            // Identify which blocks are children of containers (used to skip during iteration)
            var childBlockIndices = new HashSet<int>();
            for (int bi = 0; bi < ((DocsCanvas)_services)._doc.BlockCount; bi++)
            {
                var parsed = ((DocsCanvas)_services)._parsedBlocks![bi];
                if (parsed.Children != null)
                {
                    foreach (var child in parsed.Children)
                    {
                        // Find the flat index of this child
                        for (int ci = 0; ci < ((DocsCanvas)_services)._doc.BlockCount; ci++)
                        {
                            if (((DocsCanvas)_services)._parsedBlocks![ci] == child)
                            {
                                childBlockIndices.Add(ci);
                                break;
                            }
                        }
                    }
                }
            }

            // Process blocks, using hierarchy when available
            for (int bi = 0; bi < ((DocsCanvas)_services)._doc.BlockCount; bi++)
            {
                var parsed = ((DocsCanvas)_services)._parsedBlocks![bi];

                if (_services.IsVisual && parsed.IsSkippedInVisual)
                    continue;

                // Skip blocks that are children - they'll be processed via their parent's Children
                if (childBlockIndices.Contains(bi))
                    continue;

                // Process this block and its children recursively
                ProcessBlockAndChildren(bi, parsed, maxWidth, nestingDepth: 0, parentContentCol: 0);
            }

            double y = DocsCanvas._padding;
            for (int i = 0; i < ((DocsCanvas)_services)._visualLines.Count; i++)
            {
                int bi = ((DocsCanvas)_services)._visualLines[i].BlockIndex;
                if (i > 0 && bi != ((DocsCanvas)_services)._visualLines[i - 1].BlockIndex)
                {
                    var curGroup = ((DocsCanvas)_services)._visualLines[i].Group;
                    var prevGroup = ((DocsCanvas)_services)._visualLines[i - 1].Group;
                    bool sameGroup = curGroup != null && prevGroup == curGroup;

                    if (!sameGroup)
                    {
                        bool paragraphBreak = false;
                        for (int prev = ((DocsCanvas)_services)._visualLines[i - 1].BlockIndex; prev < bi && !paragraphBreak; prev++)
                        {
                            if (((DocsCanvas)_services)._doc.GetBlockLength(prev) == 0)
                                paragraphBreak = true;
                        }
                        if (paragraphBreak && ((DocsCanvas)_services)._doc.GetBlockLength(((DocsCanvas)_services)._visualLines[i - 1].BlockIndex) > 0)
                            y += DocsCanvas._paragraphGap;
                    }
                }
                ((DocsCanvas)_services)._lineYPositions.Add(y);
                var lineVl = ((DocsCanvas)_services)._visualLines[i];
                double lineH = ((DocsCanvas)_services)._measure.GetLineHeight(lineVl.BlockKind);
                if (lineVl.OverrideHeight > lineH) lineH = lineVl.OverrideHeight;
                y += lineH;
            }
            ((DocsCanvas)_services)._totalContentHeight = y + DocsCanvas._padding;

            // Compute and cache spacing for each visual line (visual mode only)
            if (_services.IsVisual && ((DocsCanvas)_services)._visualLineSpacings != null)
            {
                foreach (var vl in ((DocsCanvas)_services)._visualLines)
                {
                    ((DocsCanvas)_services)._visualLineSpacings.Add(ComputeVisualLineSpacing(vl));
                }
            }

            ((DocsCanvas)_services)._layoutVersion++;
        }

        private void ProcessBlockAndChildren(int blockIndex, ParsedBlock parsed, double maxWidth, int nestingDepth, int parentContentCol)
        {
            if (_services.IsVisual && ((DocsCanvas)_services)._blockToGroup != null && ((DocsCanvas)_services)._blockToGroup.TryGetValue(blockIndex, out var group))
            {
                _services.Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is in a ParagraphGroup");
                if (blockIndex == group.FirstBlock)
                {
                    _services.Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is FirstBlock, wrapping as joined");
                    WrapSegmentJoined(group, maxWidth);
                }
                return;
            }

            _services.Logger?.Log(DocsLogLevel.Debug, $"ProcessBlockAndChildren: Block {blockIndex} is NOT in a ParagraphGroup");

            string text = ((DocsCanvas)_services)._doc.GetBlockText(blockIndex);

            if (text.Length == 0)
            {
                ((DocsCanvas)_services)._visualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, 0, parsed.Kind)
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

            var map = _services.IsVisual ? ((DocsCanvas)_services)._visualMaps?[blockIndex] : null;

            if (_services.IsVisual && parsed.Kind == BlockKind.ThematicBreak)
            {
                ((DocsCanvas)_services)._visualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, text.Length, parsed.Kind)
                {
                    OverrideHeight = 20,
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });
                return;
            }

            if (_services.IsVisual && parsed.Table != null && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                ((DocsCanvas)_services)._visualLines.Add(new DocsCanvas.VisualLine(blockIndex, 0, text.Length, parsed.Kind)
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
            for (int i = 0; i < ((DocsCanvas)_services)._doc.BlockCount; i++)
            {
                if (((DocsCanvas)_services)._parsedBlocks![i] == block)
                    return i;
            }
            return -1;
        }

        private void WrapSegment(int blockIndex, int startOffset, string segment, double maxWidth,
            ParsedBlock parsed, BlockVisualMap? map = null, int nestingDepth = 0, int parentContentCol = 0)
        {
            if (segment.Length == 0)
            {
                ((DocsCanvas)_services)._visualLines.Add(new DocsCanvas.VisualLine(blockIndex, startOffset, 0, parsed.Kind)
                {
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                });
                return;
            }

            double prefixWidth = 0;
            if (map?.ReplacementPrefix != null)
                prefixWidth = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

            int pos = 0;
            while (pos < segment.Length)
            {
                double lineMax = pos == 0 ? maxWidth - prefixWidth : maxWidth;
                int lineLen = FitLine(segment, pos, lineMax, parsed, map, startOffset);
                var vl = new DocsCanvas.VisualLine(blockIndex, startOffset + pos, lineLen, parsed.Kind)
                {
                    NestingDepth = nestingDepth,
                    ParentContentColumn = parentContentCol
                };
                if (_services.IsVisual && map?.Images != null)
                {
                    double imgH = GetImageMaxLineHeight(vl, map);
                    if (imgH > 0) vl = vl with { OverrideHeight = imgH };
                }
                else if (!_services.IsVisual && ((DocsCanvas)_services)._imagePreview == DocsCanvas.ImagePreviewMode.Inline && parsed.Images != null)
                {
                    double imgH = GetSourceInlineImageHeight(vl, parsed.Images);
                    if (imgH > 0)
                        vl = vl with { OverrideHeight = ((DocsCanvas)_services)._measure.GetLineHeight(parsed.Kind) + imgH };
                }
                ((DocsCanvas)_services)._visualLines.Add(vl);
                pos += lineLen;
            }
        }

        private void WrapSegmentJoined(DocsCanvas.ParagraphGroup group, double maxWidth)
        {
            string text = group.JoinedText;
            if (text.Length == 0)
            {
                ((DocsCanvas)_services)._visualLines.Add(new DocsCanvas.VisualLine(group.FirstBlock, 0, 0, BlockKind.Paragraph)
                    { Group = group });
                return;
            }

            int pos = 0;
            while (pos < text.Length)
            {
                int lineLen = FitLine(text, pos, maxWidth, group.JoinedParsed, group.JoinedMap);
                var (bi, _) = group.JoinedToSource(pos);
                var vl = new DocsCanvas.VisualLine(bi, pos, lineLen, BlockKind.Paragraph) { Group = group };
                if (group.JoinedMap.Images != null)
                {
                    double imgH = GetImageMaxLineHeight(vl, group.JoinedMap);
                    if (imgH > 0) vl = vl with { OverrideHeight = imgH };
                }
                ((DocsCanvas)_services)._visualLines.Add(vl);
                pos += lineLen;
            }
        }

        private int FitLine(string text, int start, double maxWidth, ParsedBlock parsed,
            BlockVisualMap? map = null, int blockOffset = 0)
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
                        var (imgW, _) = GetImageSize(img.Value, ((DocsCanvas)_services)._layoutMaxWidth);
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
                if (text[i] is ' ' or '¶') lastSpace = i;
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, rawOffset, ref runIdx);
                width += ((DocsCanvas)_services)._measure.MeasureCharWidth(text[i], parsed.Kind, style);
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
            if (!_services.IsVisual || ((DocsCanvas)_services)._parsedBlocks == null || ((DocsCanvas)_services)._visualMaps == null || vl.BlockIndex >= ((DocsCanvas)_services)._parsedBlocks.Count || vl.BlockIndex >= ((DocsCanvas)_services)._visualMaps.Count)
                return new BlockVisualSpacing { ContentStartX = DocsCanvas._padding };

            var parsed = ((DocsCanvas)_services)._parsedBlocks[vl.BlockIndex];
            var map = ((DocsCanvas)_services)._visualMaps[vl.BlockIndex];

            var spacing = new BlockVisualSpacing();
            double textX = DocsCanvas._padding;

            // Add nesting indentation (block hierarchy)
            // For continuation blocks, skip this because the prefix width will serve as the indentation
            if (vl.NestingDepth > 0 && !map.IsContinuationIndent)
            {
                double charWidth = ((DocsCanvas)_services)._measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);
                textX += vl.ParentContentColumn * charWidth;
            }


            // Handle markers and content positioning
            if (vl.StartOffset == 0)
            {
                if (parsed.Kind == BlockKind.Blockquote)
                {
                    // Blockquote bar positioning
                    var aligner = new ContentBlockAligner(textX, ((DocsCanvas)_services)._measure.ListIndent);
                    spacing.MarkerStartX = aligner.GetBlockquoteBarX();
                    spacing.MarkerWidth = 3;
                    spacing.SpacingAfterMarker = aligner.GetSpacingAfterMarker();
                    spacing.ContentStartX = aligner.GetBlockquoteContentIndentX();
                }
                else if (map.ReplacementPrefix != null)
                {
                    double prefixWidth = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);

                    if (!map.IsContinuationIndent)
                    {
                        // List marker spacing structure:
                        // 1. Nesting indentation (from ListNestingLevel)
                        // 2. Fixed space before marker (2 spaces)
                        // 3. Marker (centered at MarkerStartX)
                        // 4. Fixed space after marker (SpacingAfterMarker)
                        // 5. Text content (at ContentStartX)

                        bool isListItem = parsed.Kind is BlockKind.UnorderedListItem or BlockKind.OrderedListItem or
                            BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked;

                        if (isListItem)
                        {
                            double spaceCharWidth = ((DocsCanvas)_services)._measure.MeasureCharWidth(' ', parsed.Kind, InlineStyle.Normal);

                            // 1. Nesting indentation (from ListNestingLevel)
                            double nestingIndentWidth = parsed.ListNestingLevel > 0
                                ? parsed.ListNestingLevel * BlockVisualMap.SpacesPerNestingLevel * spaceCharWidth
                                : 0;

                            // 2. Fixed space before marker (2 spaces)
                            const double spacesBeforeMarker = 2;
                            double spaceBeforeMarkerWidth = spacesBeforeMarker * spaceCharWidth;

                            // Use standard marker width (checked checkbox) for all types to align centers
                            double standardMarkerWidth = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix("☑", parsed.Kind);

                            // 3. Marker center position
                            double markerCenterX = DocsCanvas._padding + nestingIndentWidth + spaceBeforeMarkerWidth + (standardMarkerWidth / 2);
                            spacing.MarkerStartX = markerCenterX;

                            // 4. Fixed space after marker
                            const double spacingAfterMarker = 4.0;
                            spacing.SpacingAfterMarker = spacingAfterMarker;

                            // For ordered items, use actual marker width for proper content positioning
                            double actualMarkerWidth = prefixWidth;
                            if (parsed.Kind != BlockKind.OrderedListItem)
                            {
                                // For bullets and checkboxes, use standard width
                                actualMarkerWidth = standardMarkerWidth;
                            }

                            // 5. Text content start position
                            spacing.ContentStartX = DocsCanvas._padding + nestingIndentWidth + spaceBeforeMarkerWidth + actualMarkerWidth + spacingAfterMarker;

                            spacing.MarkerWidth = standardMarkerWidth;
                        }
                        else
                        {
                            // Non-list markers (blockquotes, etc.)
                            double baseX = textX;
                            var aligner = new ContentBlockAligner(baseX, ((DocsCanvas)_services)._measure.ListIndent);

                            if (parsed.Kind == BlockKind.Blockquote)
                            {
                                spacing.MarkerStartX = aligner.GetBlockquoteBarX();
                                spacing.MarkerWidth = 3;
                                spacing.SpacingAfterMarker = aligner.GetSpacingAfterMarker();
                                spacing.ContentStartX = aligner.GetBlockquoteContentIndentX();
                            }
                            else
                            {
                                spacing.MarkerStartX = textX;
                                spacing.MarkerWidth = prefixWidth;
                                spacing.SpacingAfterMarker = 0;
                                spacing.ContentStartX = textX + prefixWidth;
                            }
                        }
                    }
                    else
                    {
                        // Continuation block: indent to match parent's content by using prefix width
                        // (nesting indentation is skipped for continuation blocks)
                        spacing.ContentStartX = textX + prefixWidth;
                        spacing.MarkerStartX = textX;
                        spacing.MarkerWidth = 0;
                        spacing.SpacingAfterMarker = 0;
                    }

                }
                else
                {
                    spacing.MarkerStartX = textX;
                    spacing.MarkerWidth = 0;
                    spacing.SpacingAfterMarker = 0;
                    spacing.ContentStartX = textX;
                }
            }
            else
            {
                // Continuation lines - align with first line's content position
                spacing.MarkerStartX = textX;
                spacing.MarkerWidth = 0;
                spacing.SpacingAfterMarker = 0;

                if (map.ReplacementPrefix != null)
                {
                    // Continuation line of a list/blockquote - indent to match first line content
                    double prefixWidth = ((DocsCanvas)_services)._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                    spacing.ContentStartX = textX + prefixWidth;
                }
                else
                {
                    spacing.ContentStartX = textX;
                }
            }

            return spacing;
        }

        internal double GetTextStartXForVisualLine(DocsCanvas.VisualLine vl)
        {
            if (!_services.IsVisual || ((DocsCanvas)_services)._visualLineSpacings == null || vl.BlockIndex < 0)
                return DocsCanvas._padding;

            // Find the index of this VisualLine
            int vlIndex = -1;
            for (int i = 0; i < ((DocsCanvas)_services)._visualLines.Count; i++)
            {
                if (((DocsCanvas)_services)._visualLines[i] == vl)
                {
                    vlIndex = i;
                    break;
                }
            }

            if (vlIndex < 0 || vlIndex >= ((DocsCanvas)_services)._visualLineSpacings.Count)
                return DocsCanvas._padding;

            return ((DocsCanvas)_services)._visualLineSpacings[vlIndex]?.ContentStartX ?? DocsCanvas._padding;
        }

        private (double Width, double Height) GetImageSize(InlineImage img, double maxWidth)
        {
            var cached = ((DocsCanvas)_services)._imageCache.Get(img.Url, _services.DocumentBasePath, maxWidth);
            if (cached != null)
                return (cached.Value.Width, cached.Value.Height);
            ((DocsCanvas)_services)._imageCache.RequestLoad(img.Url, _services.DocumentBasePath, () => _services.InvalidateLayout());
            return (20, 20);
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
                    var (_, h) = GetImageSize(img, ((DocsCanvas)_services)._layoutMaxWidth);
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
                    var (_, h) = GetImageSize(img, ((DocsCanvas)_services)._layoutMaxWidth);
                    totalH += h;
                }
            }
            return totalH;
        }
    }
}
