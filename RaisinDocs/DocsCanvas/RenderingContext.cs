using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles all rendering operations for DocsCanvas, including text drawing, styling,
    /// backgrounds, selection highlights, and visual element rendering.
    /// Encapsulates layout-independent rendering logic that depends on parsed content,
    /// visual lines, and theme palettes.
    /// </summary>
    internal class RenderingContext
    {
        private readonly DocsCanvas _canvas;

        public RenderingContext(DocsCanvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        /// <summary>
        /// Main rendering orchestration. Renders backgrounds, text, selection, cursor, and images.
        /// </summary>
        public void OnRender(DrawingContext dc)
        {
            _canvas._measure.EnsureMeasured(_canvas);
            dc.DrawRectangle(_canvas._palette.Background, null,
                new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));

            if (_canvas._parsedBlocks == null)
                return;

            double effectiveScroll = Math.Round(_canvas._scroll.EffectiveOffset);
            double viewTop = effectiveScroll;
            double viewBottom = effectiveScroll + _canvas.ActualHeight;

            DrawCodeBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
            DrawColorBlockBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
            DrawInlineColorBackgrounds(dc, effectiveScroll, viewTop, viewBottom);
            if (_canvas.IsVisual)
                _canvas._tableRenderer.DrawTableBackgrounds(dc, effectiveScroll, viewTop, viewBottom);

            if (_canvas.FindAndReplace.TestSearchMatchCount > 0)
                DrawSearchHighlights(dc, effectiveScroll);

            if (_canvas._doc.HasSelection)
                DrawSelection(dc, effectiveScroll);

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                double lineH = _canvas.GetEffectiveLineHeight(vl);
                double lineY = _canvas._lineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                if (vl.Length > 0)
                {
                    if (vl.Group != null)
                    {
                        DrawJoinedLine(dc, vl, lineY, effectiveScroll);
                    }
                    else
                    {
                        var parsed = _canvas._parsedBlocks[vl.BlockIndex];
                        string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
                        double fontSize = _canvas._measure.GetBlockFontSize(parsed.Kind);
                        var baseTypeface = TextMeasurer.GetBlockBaseTypeface(parsed.Kind);
                        var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;

                        double textX = _canvas._layoutEngine.GetTextStartXForVisualLine(vl);

                        if (_canvas.IsVisual && parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
                        {
                            DrawBlockquoteBar(dc, lineY, effectiveScroll);
                        }

                        if (_canvas.IsVisual && parsed.Kind == BlockKind.ThematicBreak)
                        {
                            double ruleY = lineY - effectiveScroll + 10;
                            double ruleRight = _canvas.ActualWidth - DocsCanvas._padding;
                            dc.DrawLine(_canvas._palette.TableBorderPen, new Point(DocsCanvas._padding, ruleY), new Point(ruleRight, ruleY));
                        }
                        else if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null)
                        {
                            _canvas._tableRenderer.DrawTableRow(dc, vl, blockText, parsed, lineY, effectiveScroll, fontSize, baseTypeface);
                        }
                        else if (map != null)
                        {
                            if (HasImagesOnLine(vl, map))
                            {
                                DrawVisualLineWithImages(dc, vl, blockText, parsed, map,
                                    lineY, effectiveScroll, fontSize, baseTypeface);
                            }
                            else
                            {
                                // In source mode, only draw actual markdown syntax (bullets, numbers, etc)
                                // but NOT continuation indentation - show raw text at column 0
                                if (map.ReplacementPrefix != null && vl.StartOffset == 0 && !map.IsContinuationIndent)
                                {
                                    if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                                    {
                                        var spacing = _canvas.GetVisualLineSpacing(vl);
                                        if (spacing != null)
                                        {
                                            DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                                                new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(lineY - effectiveScroll),
                                                parsed.Kind);
                                        }
                                    }
                                    else if (parsed.Kind == BlockKind.UnorderedListItem)
                                    {
                                        var spacing = _canvas.GetVisualLineSpacing(vl);
                                        if (spacing != null)
                                        {
                                            DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX),
                                                new AbsoluteY(lineY - effectiveScroll),
                                                parsed.Kind, parsed.ListNestingLevel);
                                        }
                                    }
                                    else if (parsed.Kind == BlockKind.OrderedListItem)
                                    {
                                        var spacing = _canvas.GetVisualLineSpacing(vl);
                                        if (spacing != null)
                                        {
                                            DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX),
                                                new AbsoluteY(lineY - effectiveScroll),
                                                map.ReplacementPrefix!, fontSize, parsed.ListNestingLevel);
                                        }
                                    }
                                    else
                                    {
                                        var prefixFt = new FormattedText(map.ReplacementPrefix!,
                                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                            TextMeasurer.NormalTypeface, fontSize, _canvas._palette.Syntax, _canvas._measure.DpiScale);
                                        dc.DrawText(prefixFt, new Point(DocsCanvas._padding, lineY - effectiveScroll));
                                    }
                                }

                                string displayText = map.BuildDisplayString(blockText, vl.StartOffset, vl.Length);
                                if (displayText.Length > 0 || (parsed.Kind == BlockKind.HtmlBlock && parsed.CreateVisualSeparation))
                                {
                                    if (displayText.Length > 0)
                                    {
                                        var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                                            FlowDirection.LeftToRight, baseTypeface, fontSize,
                                            _canvas._palette.Foreground, _canvas._measure.DpiScale);
                                        ApplyInlineStylesVisual(ft, vl, parsed, map);
                                        if (parsed.Kind == BlockKind.TaskListItemChecked)
                                        {
                                            ft.SetForegroundBrush(_canvas._palette.Syntax, 0, displayText.Length);
                                            ft.SetTextDecorations(TextDecorations.Strikethrough, 0, displayText.Length);
                                        }
                                        dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));
                                    }
                                }
                            }
                        }
                        else
                        {
                            string text = blockText.Substring(vl.StartOffset, vl.Length);
                            var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, baseTypeface, fontSize,
                                _canvas._palette.Foreground, _canvas._measure.DpiScale);
                            ApplyInlineStyles(ft, vl, parsed, blockText);
                            dc.DrawText(ft, new Point(textX, lineY - effectiveScroll));

                            if (_canvas._showWhitespace)
                                DrawTrailingSpaceDots(dc, vl, blockText, parsed, textX, lineY - effectiveScroll);

                            if (_canvas._imagePreview == DocsCanvas.ImagePreviewMode.Inline && parsed.Images != null)
                                DrawSourceInlineImages(dc, vl, parsed.Images, lineY, effectiveScroll);
                        }
                    }
                }
            }

            if (_canvas.SpellCheckEnabled)
                DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

            if (_canvas.ShowPageBreaks)
                DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);

            if (_canvas._cursorVisible && _canvas.IsFocused && _canvas._visualLines.Count > 0)
            {
                int vli = _canvas.CursorToVisualLineIndex();
                double cx = DocsCanvas._padding + _canvas.CursorXInVisualLine(vli);
                double cy = _canvas._lineYPositions[vli] - effectiveScroll;
                double lineH = _canvas.GetEffectiveLineHeight(_canvas._visualLines[vli]);
                dc.DrawLine(_canvas._palette.CursorPen, new Point(cx, cy), new Point(cx, cy + lineH));
            }

            if (!_canvas.IsVisual && _canvas._imagePreview == DocsCanvas.ImagePreviewMode.OnHover && _canvas._hoveredImage != null)
                DrawHoverImagePreview(dc);

            _canvas.Dispatcher.BeginInvoke(() =>
            {
                _canvas.Minimap?.InvalidateVisual();
                _canvas.ScrollStateChanged?.Invoke();
            });
        }

        private void DrawJoinedLine(DrawingContext dc, VisualLine vl,
            double lineY, double effectiveScroll)
        {
            var group = vl.Group!;
            _canvas.Logger?.Log(DocsLogLevel.Debug, $"DrawJoinedLine: Rendering joined line with text '{group.JoinedText}'");

            if (HasImagesOnLine(vl, group.JoinedMap))
            {
                DrawVisualLineWithImages(dc, vl, group.JoinedText, group.JoinedParsed,
                    group.JoinedMap, lineY, effectiveScroll,
                    _canvas._measure.GetBlockFontSize(BlockKind.Paragraph), TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph));
                return;
            }

            // Build base display string (with "¶" only, no spaces yet)
            var baseDisplay = group.JoinedMap.BuildDisplayString(group.JoinedText, vl.StartOffset, vl.Length);

            // Add visual spaces after pilcrows
            var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
            var sb = new System.Text.StringBuilder();
            int visPos = 0;
            for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
            {
                if (group.JoinedMap.IsHidden(i)) continue;

                // Add the visible character from base display
                if (visPos < baseDisplay.Length)
                    sb.Append(baseDisplay[visPos]);

                // Add visual space after pilcrow
                if (softBreaks.Contains(i) && i < group.JoinedText.Length && group.JoinedText[i] == '¶')
                    sb.Append(' ');

                visPos++;
            }

            string displayText = sb.ToString();
            if (displayText.Length == 0) return;

            double fontSize = _canvas._measure.GetBlockFontSize(BlockKind.Paragraph);
            var baseTypeface = TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph);

            var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, baseTypeface, fontSize,
                _canvas._palette.Foreground, _canvas._measure.DpiScale);
            ApplyInlineStylesVisual(ft, vl, group.JoinedParsed, group.JoinedMap);

            // Color soft breaks (pilcrow + visual space)
            visPos = 0;
            int displayPos = 0;
            for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
            {
                if (group.JoinedMap.IsHidden(i)) continue;

                if (softBreaks.Contains(i) && displayPos < displayText.Length)
                    ft.SetForegroundBrush(_canvas._palette.Syntax, displayPos, 2);  // color pilcrow + visual space

                // Advance display position (by 2 if soft break with visual space, else by 1)
                displayPos += (softBreaks.Contains(i)) ? 2 : 1;
                visPos++;
            }

            dc.DrawText(ft, new Point(DocsCanvas._padding, lineY - effectiveScroll));
        }

        private void ApplyInlineStyles(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            if (parsed.SyntaxTokens != null)
            {
                ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens);
                return;
            }

            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                int vlEnd = vl.StartOffset + vl.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

                int localStart = Math.Max(0, run.Start - vl.StartOffset);
                int localEnd = Math.Min(vl.Length, runEnd - vl.StartOffset);
                int count = localEnd - localStart;
                if (count <= 0) continue;

                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

                switch (run.Style)
                {
                    case InlineStyle.Bold:
                        ft.SetFontWeight(FontWeights.Bold, localStart, count);
                        break;
                    case InlineStyle.Italic:
                        ft.SetFontStyle(FontStyles.Italic, localStart, count);
                        break;
                    case InlineStyle.BoldItalic:
                        ft.SetFontWeight(FontWeights.Bold, localStart, count);
                        ft.SetFontStyle(FontStyles.Italic, localStart, count);
                        break;
                    case InlineStyle.Code:
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, localStart, count);
                        break;
                    case InlineStyle.Strikethrough:
                        ft.SetTextDecorations(TextDecorations.Strikethrough, localStart, count);
                        break;
                    case InlineStyle.Link:
                        ft.SetForegroundBrush(DocsCanvas._checkboxCheckedBrush, localStart, count);
                        ft.SetTextDecorations(TextDecorations.Underline, localStart, count);
                        break;
                }
            }

            ApplyColorSpans(ft, vl, parsed, blockText);
            ApplySyntaxDimming(ft, vl, parsed, blockText);
        }

        private void ApplySyntaxTokens(FormattedText ft, VisualLine vl, IReadOnlyList<SyntaxToken> tokens, BlockVisualMap? map = null)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var token in tokens)
            {
                int tokenEnd = token.Start + token.Length;
                if (tokenEnd <= vl.StartOffset || token.Start >= vlEnd) continue;

                int localStart;
                int count;
                if (map != null)
                {
                    int rawStart = Math.Max(token.Start, vl.StartOffset);
                    int rawEnd = Math.Min(tokenEnd, vlEnd);
                    int vlVisualOffset = map.RawToVisual(vl.StartOffset);
                    int visStart = map.RawToVisual(rawStart) - vlVisualOffset;
                    int visEnd = map.RawToVisual(rawEnd) - vlVisualOffset;
                    localStart = visStart;
                    count = visEnd - visStart;
                }
                else
                {
                    localStart = Math.Max(0, token.Start - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, tokenEnd - vl.StartOffset);
                    count = localEnd - localStart;
                }
                if (count <= 0) continue;

                var brush = GetSyntaxBrush(token.ForegroundArgb);
                ft.SetForegroundBrush(brush, localStart, count);
            }
        }

        private Brush GetSyntaxBrush(int argb)
        {
            if (_canvas._syntaxBrushCache.TryGetValue(argb, out var cached))
                return cached;

            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            _canvas._syntaxBrushCache[argb] = brush;
            return brush;
        }

        private void ApplyColorSpans(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            if (parsed.ColorSpans == null && parsed.BlockColor == null) return;
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;

            int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
                ? MarkdownParser.GetContentEnd(blockText) - 1
                : int.MaxValue;

            if (parsed.BlockColor?.Foreground is { } blockFg)
            {
                int len = Math.Min(vl.Length, hardBreakClip - vl.StartOffset);
                if (len > 0)
                    ft.SetForegroundBrush(_canvas.GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, len);
            }

            if (parsed.ColorSpans != null)
            {
                foreach (var cs in parsed.ColorSpans)
                {
                    int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                    int vlEnd = vl.StartOffset + vl.Length;
                    if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                    int localStart = Math.Max(0, cs.Start - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, csEnd - vl.StartOffset);
                    int count = localEnd - localStart;
                    if (count <= 0) continue;

                    if (cs.Foreground is { } fg)
                    {
                        ft.SetForegroundBrush(_canvas.GetCachedBrush(fg.R, fg.G, fg.B), localStart, count);
                    }
                }
            }
        }

        private void ApplySyntaxDimming(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            int vlEnd = vl.StartOffset + vl.Length;

            int ls = parsed.LeadingSpaces;

            if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                if (stripped.Length > 0 && stripped[0] == '#')
                {
                    int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
                    int totalPrefix = ls + hashCount + 1;
                    int localStart = Math.Max(0, 0 - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, totalPrefix - vl.StartOffset);
                    if (localEnd > localStart)
                        ft.SetForegroundBrush(_canvas._palette.Syntax, localStart, localEnd - localStart);
                }
            }

            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked && vl.StartOffset == 0 && vl.Length >= ls + 6)
            {
                ft.SetForegroundBrush(_canvas._palette.Syntax, 0, ls + 6);
            }
            else if (parsed.Kind == BlockKind.UnorderedListItem && vl.StartOffset == 0 && vl.Length >= ls + 2)
            {
                ft.SetForegroundBrush(_canvas._palette.Syntax, 0, ls + 2);
            }
            else if (parsed.Kind == BlockKind.OrderedListItem && vl.StartOffset == 0)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
                if (prefixLen > 0 && vl.Length >= ls + prefixLen)
                    ft.SetForegroundBrush(_canvas._palette.Syntax, 0, ls + prefixLen);
            }

            if (parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                if (stripped.Length > 0 && stripped[0] == '>')
                {
                    int dimLength = ls + 1;
                    if (stripped.Length > 1 && stripped[1] == ' ')
                        dimLength += 1;
                    if (vl.Length >= dimLength)
                        ft.SetForegroundBrush(_canvas._palette.Syntax, 0, dimLength);
                }
            }

            if (parsed.Kind == BlockKind.LinkDefinition)
                ft.SetForegroundBrush(_canvas._palette.Syntax, 0, vl.Length);

            if (parsed.Kind is BlockKind.ThemeDefinition or BlockKind.ColorDivOpen or BlockKind.ColorDivClose)
                ft.SetForegroundBrush(_canvas._palette.Syntax, 0, vl.Length);

            if (parsed.Kind is BlockKind.TableSeparatorRow or BlockKind.ThematicBreak or BlockKind.SetextUnderline)
            {
                ft.SetForegroundBrush(_canvas._palette.Syntax, 0, vl.Length);
            }
            else if (parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                for (int ci = vl.StartOffset; ci < vlEnd; ci++)
                {
                    if (ci > 0 && blockText[ci - 1] == '\\') continue;
                    if (blockText[ci] == '|')
                        DimRange(ft, vl, ci, 1);
                }
            }

            if (parsed.Images != null)
            {
                foreach (var img in parsed.Images)
                {
                    int imgEnd = img.Start + img.Length;
                    if (imgEnd <= vl.StartOffset || img.Start >= vlEnd) continue;

                    DimRange(ft, vl, img.Start, 2);
                    int closeBracket = img.Start + 2 + img.AltText.Length;
                    DimRange(ft, vl, closeBracket, imgEnd - closeBracket);
                }
            }

            if (parsed.Links != null)
            {
                foreach (var link in parsed.Links)
                {
                    if (link.Text == link.Url) continue;
                    int linkEnd = link.Start + link.Length;
                    if (linkEnd <= vl.StartOffset || link.Start >= vlEnd) continue;

                    DimRange(ft, vl, link.Start, 1);
                    int closeBracket = link.Start + 1 + link.Text.Length;
                    DimRange(ft, vl, closeBracket, linkEnd - closeBracket);
                }
            }

            foreach (var run in parsed.Runs)
            {
                if (run.Style is InlineStyle.Normal or InlineStyle.Image or InlineStyle.Link) continue;
                int runEnd = run.Start + run.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

                if (run.Style is InlineStyle.Code or InlineStyle.Strikethrough)
                {
                    int markerLen = run.Style == InlineStyle.Code
                        ? CountBackticks(blockText, run.Start)
                        : MarkdownParser.GetMarkerLength(run.Style);
                    if (markerLen == 0) continue;

                    DimRange(ft, vl, run.Start, markerLen);
                    DimRange(ft, vl, runEnd - markerLen, markerLen);
                }
            }

            if (parsed.EmphasisMarkers != null)
            {
                foreach (var marker in parsed.EmphasisMarkers)
                    DimRange(ft, vl, marker.Start, marker.Length);
            }

            if (MarkdownParser.IsTrailingHardBreak(parsed, blockText))
                DimRange(ft, vl, MarkdownParser.GetContentEnd(blockText) - 1, 1);

            if (parsed.Kind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine)
            {
                var tagRanges = MarkdownParser.FindInlineColorTagRanges(blockText);
                if (tagRanges != null)
                {
                    foreach (var tag in tagRanges)
                        DimRange(ft, vl, tag.Start, tag.Length);
                }
            }

            if (parsed.Kind == BlockKind.HtmlBlock)
            {
                var htmlCommentRanges = MarkdownParser.FindHtmlCommentRanges(blockText);
                if (htmlCommentRanges != null)
                {
                    foreach (var commentRange in htmlCommentRanges)
                        DimRange(ft, vl, commentRange.Start, commentRange.Length);
                }
            }
        }

        private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
            ParsedBlock parsed, BlockVisualMap map)
        {
            if (parsed.SyntaxTokens != null)
            {
                ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens, map);
                return;
            }

            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var run in parsed.Runs)
            {
                if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
                int runEnd = run.Start + run.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

                int rawStart = Math.Max(run.Start, vl.StartOffset);
                int rawEnd = Math.Min(runEnd, vlEnd);
                int visStart = map.RawToVisual(rawStart) - map.RawToVisual(vl.StartOffset);
                int visEnd = map.RawToVisual(rawEnd) - map.RawToVisual(vl.StartOffset);
                int count = visEnd - visStart;
                if (count <= 0) continue;

                switch (run.Style)
                {
                    case InlineStyle.Bold:
                        ft.SetFontWeight(FontWeights.Bold, visStart, count);
                        break;
                    case InlineStyle.Italic:
                        ft.SetFontStyle(FontStyles.Italic, visStart, count);
                        break;
                    case InlineStyle.BoldItalic:
                        ft.SetFontWeight(FontWeights.Bold, visStart, count);
                        ft.SetFontStyle(FontStyles.Italic, visStart, count);
                        break;
                    case InlineStyle.Code:
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visStart, count);
                        break;
                    case InlineStyle.Strikethrough:
                        ft.SetTextDecorations(TextDecorations.Strikethrough, visStart, count);
                        break;
                    case InlineStyle.Link:
                        ft.SetForegroundBrush(DocsCanvas._checkboxCheckedBrush, visStart, count);
                        ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
                        break;
                }
            }

            ApplyColorSpansVisual(ft, vl, parsed, map);
        }

        private void ApplyColorSpansVisual(FormattedText ft, VisualLine vl,
            ParsedBlock parsed, BlockVisualMap map)
        {
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
            int ftLen = ft.Text.Length;

            if (parsed.BlockColor?.Foreground is { } blockFg)
            {
                int vlVisLen = Math.Min(ftLen, map.RawToVisual(vl.StartOffset + vl.Length) - map.RawToVisual(vl.StartOffset));
                if (vlVisLen > 0)
                    ft.SetForegroundBrush(_canvas.GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, vlVisLen);
            }

            var colorSpans = map.ColorSpans;
            if (colorSpans == null) return;

            int vlEnd = vl.StartOffset + vl.Length;
            int vlVisBase = map.RawToVisual(vl.StartOffset);

            foreach (var cs in colorSpans)
            {
                int csEnd = cs.Start + cs.Length;
                if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                int rawStart = Math.Max(cs.Start, vl.StartOffset);
                int rawEnd = Math.Min(csEnd, vlEnd);
                int visStart = map.RawToVisual(rawStart) - vlVisBase;
                int visEnd = map.RawToVisual(rawEnd) - vlVisBase;
                visEnd = Math.Min(visEnd, ftLen);
                int count = visEnd - visStart;
                if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

                if (cs.Foreground is { } fg)
                {
                    ft.SetForegroundBrush(_canvas.GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
                }
            }
        }

        private bool HasImagesOnLine(VisualLine vl, BlockVisualMap map)
        {
            if (map.Images == null) return false;
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in map.Images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd) return true;
                if (img.Start >= vlEnd) break;
            }
            return false;
        }

        private void DrawVisualLineWithImages(DrawingContext dc, VisualLine vl,
            string blockText, ParsedBlock parsed, BlockVisualMap map,
            double lineY, double effectiveScroll, double fontSize, Typeface baseTypeface)
        {
            if (map.Images == null) return;

            double x = DocsCanvas._padding;
            double screenY = lineY - effectiveScroll;
            double textLineH = _canvas._measure.GetLineHeight(vl.BlockKind);
            double totalLineH = vl.OverrideHeight > textLineH ? vl.OverrideHeight : textLineH;

            if (map.ReplacementPrefix != null && vl.StartOffset == 0)
            {
                if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    var spacing = _canvas.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                            new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY), parsed.Kind);
                    }
                }
                else if (parsed.Kind == BlockKind.UnorderedListItem)
                {
                    var spacing = _canvas.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                            parsed.Kind, parsed.ListNestingLevel);
                    }
                    x += _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else if (parsed.Kind == BlockKind.OrderedListItem)
                {
                    var spacing = _canvas.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                            map.ReplacementPrefix, fontSize, parsed.ListNestingLevel);
                    }
                    x += _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else if (map.IsContinuationIndent)
                {
                    x += _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else
                {
                    var prefixFt = new FormattedText(map.ReplacementPrefix,
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        TextMeasurer.NormalTypeface, fontSize, _canvas._palette.Syntax, _canvas._measure.DpiScale);
                    dc.DrawText(prefixFt, new Point(DocsCanvas._padding, screenY));
                    x += _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
            }

            int vlEnd = vl.StartOffset + vl.Length;
            int segStart = vl.StartOffset;

            foreach (var img in map.Images)
            {
                if (img.Start >= vlEnd) break;
                if (img.Start + img.Length <= vl.StartOffset) continue;

                if (segStart < img.Start)
                    x = DrawTextSegment(dc, blockText, segStart, img.Start, map, parsed, fontSize, baseTypeface, x, screenY);

                var (imgW, imgH) = _canvas.GetImageSize(img, _canvas._layoutMaxWidth);
                var cached = _canvas._imageCache.Get(img.Url, _canvas.DocumentBasePath, _canvas._layoutMaxWidth);
                double imgY = screenY + totalLineH - imgH;
                if (cached != null)
                {
                    dc.DrawImage(cached.Value.Image, new Rect(x, imgY, imgW, imgH));
                }
                else
                {
                    DrawImagePlaceholder(dc, x, imgY, imgW, imgH, img.AltText);
                }
                x += imgW;

                segStart = img.Start + img.Length;
            }

            if (segStart < vlEnd)
                DrawTextSegment(dc, blockText, segStart, vlEnd, map, parsed, fontSize, baseTypeface, x, screenY);
        }

        private double DrawTextSegment(DrawingContext dc, string blockText,
            int rawStart, int rawEnd, BlockVisualMap map, ParsedBlock parsed,
            double fontSize, Typeface baseTypeface, double x, double screenY)
        {
            string displayText = map.BuildDisplayString(blockText, rawStart, rawEnd - rawStart);
            if (displayText.Length == 0) return x;

            var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, baseTypeface, fontSize,
                _canvas._palette.Foreground, _canvas._measure.DpiScale);

            int visBase = 0;
            int runIdx = 0;
            for (int r = rawStart; r < rawEnd; r++)
            {
                if (map.IsHidden(r)) continue;
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, r, ref runIdx);
                if (style != InlineStyle.Normal && style != InlineStyle.Image && visBase < displayText.Length)
                {
                    switch (style)
                    {
                        case InlineStyle.Bold:
                            ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                            break;
                        case InlineStyle.Italic:
                            ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                            break;
                        case InlineStyle.BoldItalic:
                            ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                            ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                            break;
                        case InlineStyle.Code:
                            ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visBase, 1);
                            break;
                        case InlineStyle.Strikethrough:
                            ft.SetTextDecorations(TextDecorations.Strikethrough, visBase, 1);
                            break;
                    }
                }
                visBase++;
            }

            dc.DrawText(ft, new Point(x, screenY));
            return x + ft.WidthIncludingTrailingWhitespace;
        }

        private void DrawTaskListCheckbox(DrawingContext dc, bool isChecked, AbsoluteX markerCenterX, AbsoluteY screenY,
            BlockKind blockKind)
        {
            double lineH = _canvas._measure.GetLineHeight(blockKind);
            double baseline = _canvas._measure.GetBaseline(blockKind);
            double fontSize = _canvas._measure.GetBlockFontSize(blockKind);
            double capHeight = fontSize * _canvas._measure.CapsHeightRatio;
            double boxSize = Math.Round(lineH * 0.65);

            // Align checkbox with text baseline, same as bullets
            double checkboxCenterY = screenY.Value + baseline - capHeight / 2;
            double checkboxX = markerCenterX.Value - boxSize / 2;
            double checkboxY = Math.Round(checkboxCenterY - boxSize / 2);
            var rect = new Rect(checkboxX, checkboxY, boxSize, boxSize);
            double radius = 2.5;

            if (isChecked)
            {
                dc.DrawRoundedRectangle(DocsCanvas._checkboxCheckedBrush, null, rect, radius, radius);
                var pen = new Pen(_canvas._palette.Background, 1.6);
                pen.Freeze();
                double cx = checkboxX, cy = checkboxY, s = boxSize;
                dc.DrawLine(pen,
                    new Point(cx + s * 0.22, cy + s * 0.52),
                    new Point(cx + s * 0.42, cy + s * 0.72));
                dc.DrawLine(pen,
                    new Point(cx + s * 0.42, cy + s * 0.72),
                    new Point(cx + s * 0.78, cy + s * 0.28));
            }
            else
            {
                var pen = new Pen(_canvas._palette.Syntax, 1.2);
                pen.Freeze();
                dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            }
        }

        private void DrawListBullet(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
            BlockKind blockKind, int nestingLevel)
        {
            double lineH = _canvas._measure.GetLineHeight(blockKind);
            double baseline = _canvas._measure.GetBaseline(blockKind);
            double fontSize = _canvas._measure.GetBlockFontSize(blockKind);
            double capHeight = fontSize * _canvas._measure.CapsHeightRatio;
            double bulletSize = Math.Round(lineH * 0.32);

            // markerCenterX is the center of the marker area; adjust to draw position
            double bulletX = markerCenterX.Value - bulletSize / 2;
            double bulletCenterY = screenY.Value + baseline - capHeight / 2;
            double bulletY = Math.Round(bulletCenterY - bulletSize / 2);

            DrawBulletAtPosition(dc, bulletX, bulletY, bulletSize, nestingLevel);
        }

        private void DrawBulletAtPosition(DrawingContext dc, double bulletX, double bulletY, double bulletSize, int nestingLevel)
        {
            int shape = nestingLevel % 3;
            if (shape == 0)
            {
                dc.DrawEllipse(_canvas._palette.Syntax, null, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                    bulletSize / 2, bulletSize / 2);
            }
            else if (shape == 1)
            {
                var pen = new Pen(_canvas._palette.Syntax, 1.2);
                pen.Freeze();
                dc.DrawEllipse(null, pen, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                    bulletSize / 2, bulletSize / 2);
            }
            else
            {
                dc.DrawRectangle(_canvas._palette.Syntax, null, new Rect(bulletX, bulletY, bulletSize, bulletSize));
            }
        }

        private void DrawOrderedListNumber(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
            string replacementPrefix, double fontSize, int nestingLevel)
        {
            string trimmed = replacementPrefix.TrimStart();
            string numberText = trimmed.TrimEnd();

            int delimiterPos = numberText.IndexOfAny(new[] { '.', ')' });
            string numberOnly = delimiterPos > 0 ? numberText.Substring(0, delimiterPos) : numberText;

            var ftNumberOnly = new FormattedText(numberOnly, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
                _canvas._palette.Syntax, _canvas._measure.DpiScale);

            // Center number at marker center position (adjusted for width)
            double numberX = markerCenterX.Value - ftNumberOnly.WidthIncludingTrailingWhitespace / 2;
            dc.DrawText(ftNumberOnly, new Point(numberX, screenY.Value));

            // Draw delimiter after number
            double delimiterX = numberX + ftNumberOnly.WidthIncludingTrailingWhitespace;
            var ftDelimiter = new FormattedText(numberText.Substring(numberOnly.Length), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
                _canvas._palette.Syntax, _canvas._measure.DpiScale);
            dc.DrawText(ftDelimiter, new Point(delimiterX, screenY.Value));
        }

        private void DrawBlockquoteBar(DrawingContext dc, double lineY, double effectiveScroll)
        {
            var aligner = new ContentBlockAligner(DocsCanvas._padding, _canvas._measure.ListIndent);
            double barX = aligner.GetBlockquoteBarX();
            double barWidth = 3;
            double barY = lineY - effectiveScroll;
            double barHeight = _canvas._measure.GetLineHeight(BlockKind.Blockquote);
            var barBrush = new SolidColorBrush(Color.FromArgb(80, 150, 150, 150));
            barBrush.Freeze();
            dc.DrawRectangle(barBrush, null, new Rect(barX, barY, barWidth, barHeight));
        }

        private void DrawCodeBlockBackgrounds(DrawingContext dc, double effectiveScroll,
            double viewTop, double viewBottom)
        {
            double contentWidth = _canvas.ActualWidth;

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                if (vl.BlockKind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine) continue;

                double lineH = _canvas._measure.GetLineHeight(vl.BlockKind);
                double lineY = _canvas._lineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                dc.DrawRectangle(_canvas._palette.CodeBackground, null,
                    new Rect(0, lineY - effectiveScroll, contentWidth, lineH));
            }
        }

        private void DrawColorBlockBackgrounds(DrawingContext dc, double effectiveScroll,
            double viewTop, double viewBottom)
        {
            if (_canvas._parsedBlocks == null) return;
            double contentWidth = _canvas.ActualWidth;

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                if (vl.BlockIndex >= _canvas._parsedBlocks.Count) continue;
                var parsed = _canvas._parsedBlocks[vl.BlockIndex];
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;
                if (parsed.BlockColor?.Background is not { } bg) continue;

                double lineH = _canvas.GetEffectiveLineHeight(vl);
                double lineY = _canvas._lineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                dc.DrawRectangle(_canvas.GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                    new Rect(0, lineY - effectiveScroll, contentWidth, lineH));
            }
        }

        private void DrawInlineColorBackgrounds(DrawingContext dc, double effectiveScroll,
            double viewTop, double viewBottom)
        {
            if (_canvas._parsedBlocks == null) return;

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                double lineH = _canvas.GetEffectiveLineHeight(vl);
                double lineY = _canvas._lineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                string blockText;
                ParsedBlock parsed;
                BlockVisualMap? map;
                IReadOnlyList<ColorSpan>? colorSpans;

                if (vl.Group != null)
                {
                    var group = vl.Group;
                    blockText = group.JoinedText;
                    parsed = group.JoinedParsed;
                    map = group.JoinedMap;
                    colorSpans = map.ColorSpans;
                }
                else
                {
                    if (vl.BlockIndex >= _canvas._parsedBlocks.Count) continue;
                    parsed = _canvas._parsedBlocks[vl.BlockIndex];
                    if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;
                    blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
                    map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;
                    colorSpans = _canvas.IsVisual ? map?.ColorSpans : parsed.ColorSpans;
                }

                if (colorSpans == null) continue;
                if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null) continue;

                int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
                    ? MarkdownParser.GetContentEnd(blockText) - 1
                    : int.MaxValue;
                int vlEnd = vl.StartOffset + vl.Length;

                foreach (var cs in colorSpans)
                {
                    if (cs.Background == null) continue;
                    int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                    if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                    int rangeStart = Math.Max(cs.Start, vl.StartOffset);
                    int rangeEnd = Math.Min(csEnd, vlEnd);

                    double x1 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, rangeStart - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);
                    double x2 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, rangeEnd - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);

                    if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                    {
                        double prefixW = _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                        x1 += prefixW;
                        x2 += prefixW;
                    }

                    double w = x2 - x1;
                    if (w <= 0) continue;

                    var bg = cs.Background.Value;
                    dc.DrawRectangle(_canvas.GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                        new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, w, lineH));
                }
            }
        }

        private void DrawSelection(DrawingContext dc, double effectiveScroll)
        {
            var rectSel = _canvas.TryGetTableRectSelection();
            if (rectSel != null)
            {
                var r = rectSel.Value;
                DrawTableRectSelection(dc, effectiveScroll, r.StartCol, r.EndCol, r.StartBlock, r.EndBlock, r.Table);
                return;
            }

            var (sb, so, eb, eo) = _canvas._doc.GetOrderedSelection();
            double viewTop = effectiveScroll;
            double viewBottom = effectiveScroll + _canvas.ActualHeight;

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                double lineH = _canvas.GetEffectiveLineHeight(vl);
                double lineY = _canvas._lineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                if (vl.Group != null)
                {
                    DrawJoinedSelection(dc, vl, lineY, lineH, effectiveScroll, sb, so, eb, eo);
                    continue;
                }

                int vlEnd = vl.StartOffset + vl.Length;

                bool startsBeforeSelEnd = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
                bool endsAfterSelStart = Document.ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
                if (!startsBeforeSelEnd || !endsAfterSelStart) continue;

                int hlStart = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                    ? vl.StartOffset : so;
                int hlEnd = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                    ? vlEnd : eo;

                var parsed = _canvas._parsedBlocks![vl.BlockIndex];
                string blockText = _canvas._doc.GetBlockText(vl.BlockIndex);
                var map = _canvas.IsVisual ? _canvas._visualMaps?[vl.BlockIndex] : null;

                double x1, x2;
                if (_canvas.IsVisual && parsed.Table != null && parsed.TableRow != null)
                {
                    if (_canvas._tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                    {
                        x1 = _canvas._tableRenderer.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                        x2 = _canvas._tableRenderer.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                    }
                    else
                    {
                        x1 = 0; x2 = 0;
                    }
                }
                else
                {
                    x1 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);
                    x2 = _canvas.MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);

                    if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
                    {
                        double prefixW = _canvas._measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                        x1 += prefixW;
                        x2 += prefixW;
                    }
                }

                bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
                if (selectionContinues && x2 - x1 < 4)
                    x2 = x1 + 4;
                else if (selectionContinues)
                    x2 += 4;

                double selW = Math.Max(0, x2 - x1);
                if (selW > 0)
                    dc.DrawRectangle(_canvas._palette.Selection, null,
                        new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, selW, lineH));
            }
        }

        private void DrawTableRectSelection(DrawingContext dc, double effectiveScroll,
            int startCol, int endCol, int startBlock, int endBlock, TableInfo table)
        {
            if (!_canvas._tableColumnWidths.TryGetValue(table, out var colWidths)) return;

            double xStart = 0;
            for (int c = 0; c < startCol && c < colWidths.Length; c++)
                xStart += colWidths[c];
            double xEnd = xStart;
            for (int c = startCol; c <= endCol && c < colWidths.Length; c++)
                xEnd += colWidths[c];

            double viewTop = effectiveScroll;
            double viewBottom = effectiveScroll + _canvas.ActualHeight;

            for (int i = 0; i < _canvas._visualLines.Count; i++)
            {
                var vl = _canvas._visualLines[i];
                if (vl.BlockIndex < startBlock || vl.BlockIndex > endBlock) continue;
                var parsed = _canvas._parsedBlocks![vl.BlockIndex];
                if (parsed.IsTableSeparator) continue;

                double lineY = _canvas._lineYPositions[i];
                double lineH = _canvas.GetEffectiveLineHeight(vl);
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                dc.DrawRectangle(_canvas._palette.Selection, null,
                    new Rect(DocsCanvas._padding + xStart, lineY - effectiveScroll, xEnd - xStart, lineH));
            }
        }

        private void DrawJoinedSelection(DrawingContext dc, VisualLine vl,
            double lineY, double lineH, double effectiveScroll,
            int sb, int so, int eb, int eo)
        {
            var group = vl.Group!;
            int selStartJoined = group.SourceToJoined(sb, so);
            int selEndJoined = group.SourceToJoined(eb, eo);
            if (selStartJoined < 0)
                selStartJoined = sb < group.FirstBlock ? 0 : group.JoinedText.Length;
            if (selEndJoined < 0)
                selEndJoined = eb > group.LastBlock ? group.JoinedText.Length : 0;

            int vlStart = vl.StartOffset;
            int vlEnd = vl.StartOffset + vl.Length;

            if (vlEnd <= selStartJoined || vlStart >= selEndJoined) return;

            int hlStart = Math.Max(vlStart, selStartJoined);
            int hlEnd = Math.Min(vlEnd, selEndJoined);

            double x1 = _canvas.MeasureJoinedRange(group, vlStart, hlStart - vlStart);
            double x2 = _canvas.MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

            bool selectionContinues = vlEnd < selEndJoined;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            double selW = Math.Max(0, x2 - x1);
            if (selW > 0)
                dc.DrawRectangle(_canvas._palette.Selection, null,
                    new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, selW, lineH));
        }

        private void DrawTrailingSpaceDots(DrawingContext dc, VisualLine vl,
            string blockText, ParsedBlock parsed, double textX, double screenY)
        {
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
            if (vl.StartOffset + vl.Length < blockText.Length) return;

            int trailStart = blockText.Length;
            while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
            int trailCount = blockText.Length - trailStart;
            if (trailCount == 0) return;

            var measureKind = !_canvas.IsVisual && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow
                ? BlockKind.Paragraph : parsed.Kind;

            double x = textX;
            int runIdx = 0;
            for (int i = vl.StartOffset; i < trailStart; i++)
            {
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                x += _canvas._measure.MeasureCharWidth(blockText[i], measureKind, style);
            }

            double spaceW = _canvas._measure.MeasureCharWidth(' ', measureKind, InlineStyle.Normal);
            double dotSize = Math.Max(2, spaceW * 0.25);
            double lineH = _canvas._measure.GetLineHeight(parsed.Kind);
            double cy = screenY + lineH / 2;

            for (int i = 0; i < trailCount; i++)
            {
                double cx = x + spaceW * (i + 0.5);
                dc.DrawEllipse(_canvas._palette.Syntax, null, new Point(cx, cy), dotSize / 2, dotSize / 2);
            }
        }

        private void DimRange(FormattedText ft, VisualLine vl, int docStart, int length)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            int localStart = Math.Max(0, docStart - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, docStart + length - vl.StartOffset);
            if (localEnd > localStart)
                ft.SetForegroundBrush(_canvas._palette.Syntax, localStart, localEnd - localStart);
        }

        private void DrawImagePlaceholder(DrawingContext dc, double x, double y, double w, double h, string? altText)
        {
            dc.DrawRectangle(DocsCanvas._imagePlaceholderBrush, null, new Rect(x, y, w, h));
            if (!string.IsNullOrEmpty(altText))
            {
                var altFt = new FormattedText(altText,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    TextMeasurer.NormalTypeface, Math.Round(11 * _canvas._measure.ZoomFactor), _canvas._palette.Syntax, _canvas._measure.DpiScale);
                altFt.MaxTextWidth = Math.Max(1, w);
                altFt.MaxTextHeight = Math.Max(1, h);
                dc.DrawText(altFt, new Point(x + 2, y + 2));
            }
        }

        private void DrawSourceInlineImages(DrawingContext dc, VisualLine vl,
            IReadOnlyList<InlineImage> images, double lineY, double effectiveScroll)
        {
            double textLineH = _canvas._measure.GetLineHeight(vl.BlockKind);
            double imgY = lineY - effectiveScroll + textLineH;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var img in images)
            {
                if (img.Start < vl.StartOffset || img.Start >= vlEnd) continue;

                var (imgW, imgH) = _canvas.GetImageSize(img, _canvas._layoutMaxWidth);
                var cached = _canvas._imageCache.Get(img.Url, _canvas.DocumentBasePath, _canvas._layoutMaxWidth);
                if (cached != null)
                {
                    dc.DrawImage(cached.Value.Image, new Rect(DocsCanvas._padding, imgY, imgW, imgH));
                }
                else
                {
                    DrawImagePlaceholder(dc, DocsCanvas._padding, imgY, imgW, imgH, img.AltText);
                }
                imgY += imgH;
            }
        }

        private void DrawHoverImagePreview(DrawingContext dc)
        {
            var img = _canvas._hoveredImage!.Value;
            double maxPreviewW = Math.Min(_canvas._layoutMaxWidth, 300);
            var (imgW, imgH) = _canvas.GetImageSize(img, maxPreviewW);
            var cached = _canvas._imageCache.Get(img.Url, _canvas.DocumentBasePath, maxPreviewW);

            double popupX = Math.Min(_canvas._hoverPosition.X, Math.Max(0, _canvas.ActualWidth - imgW - 16));
            double popupY = _canvas._hoverPosition.Y + 20;
            if (popupY + imgH + 8 > _canvas.ActualHeight)
                popupY = Math.Max(0, _canvas._hoverPosition.Y - imgH - 12);

            var borderPen = new Pen(_canvas._palette.Syntax, 1);
            borderPen.Freeze();
            var bgBrush = _canvas._palette.Background.Clone();
            bgBrush.Freeze();

            dc.DrawRectangle(bgBrush, borderPen,
                new Rect(popupX - 4, popupY - 4, imgW + 8, imgH + 8));

            if (cached != null)
            {
                dc.DrawImage(cached.Value.Image, new Rect(popupX, popupY, imgW, imgH));
            }
            else
            {
                DrawImagePlaceholder(dc, popupX, popupY, imgW, imgH, img.AltText);
            }
        }

        private static int CountBackticks(string text, int start)
        {
            int count = 0;
            while (start + count < text.Length && text[start + count] == '`') count++;
            return count;
        }

        // Search-related rendering (delegated from parent)
        private void DrawSearchHighlights(DrawingContext dc, double effectiveScroll)
            => _canvas.DrawSearchHighlights(dc, effectiveScroll);

        private void DrawSpellingErrors(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
            => _canvas.DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

        private void DrawPageBreaks(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
            => _canvas.DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);
    }
}
