using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RaisinDocs;

/// <summary>
/// Semantic Block Model parser for HTML-to-Markdown conversion.
///
/// Three-stage pipeline:
/// 1. ParseBlockStructure(): Extract block boundaries (h1-6, p, ul, ol, etc.)
/// 2. ParseInlineContent(): Character parsing within each block's content
/// 3. ConvertToMarkdown(): Apply settings and format to final markdown
///
/// Public entry point: ConvertHtmlToMarkdown() handles both CF_HTML and raw HTML.
/// </summary>
internal static class HtmlBlockModelParser
{
    /// <summary>
    /// Converts HTML from clipboard format (CF_HTML) or raw HTML to markdown.
    /// Returns null if no content is found or parsing fails.
    /// </summary>
    internal static string? ConvertHtmlToMarkdown(string html, MarkdownOutputSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        // Extract fragment from CF_HTML format if present
        string? fragment = ExtractCfHtmlFragment(html);
        string contentToConvert = fragment ?? html;

        if (string.IsNullOrWhiteSpace(contentToConvert))
            return null;

        // Parse and convert
        settings ??= new();
        var blocks = ParseBlockStructure(contentToConvert, settings);
        if (blocks.Count == 0)
            return null;

        return ConvertToMarkdown(blocks, settings);
    }

    /// <summary>
    /// Extracts the HTML fragment from CF_HTML clipboard format.
    /// Returns null if markers are not found.
    /// </summary>
    private static string? ExtractCfHtmlFragment(string cfHtml)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";

        int start = cfHtml.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += startMarker.Length;

        int end = cfHtml.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) return null;

        return cfHtml[start..end];
    }
    /// <summary>
    /// Stage 1: Extract block-level HTML elements and return structured blocks.
    /// </summary>
    internal static List<BlockElement> ParseBlockStructure(string html, MarkdownOutputSettings? settings = null)
    {
        settings ??= new();
        var blocks = new List<BlockElement>();
        int pos = 0;

        while (pos < html.Length)
        {
            // Skip whitespace and non-tag content
            while (pos < html.Length && html[pos] != '<')
                pos++;

            if (pos >= html.Length)
                break;

            // Try to match block-level tags
            if (TryParseHeader(html, pos, out var headerBlock, out var newPos, settings))
            {
                blocks.Add(headerBlock);
                pos = newPos;
                continue;
            }

            if (TryParseParagraph(html, pos, out var paraBlock, out newPos, settings))
            {
                blocks.Add(paraBlock);
                pos = newPos;
                continue;
            }

            if (TryParseUnorderedList(html, pos, out var ulBlock, out newPos, settings))
            {
                blocks.Add(ulBlock);
                pos = newPos;
                continue;
            }

            if (TryParseOrderedList(html, pos, out var olBlock, out newPos, settings))
            {
                blocks.Add(olBlock);
                pos = newPos;
                continue;
            }

            if (TryParseBlockquote(html, pos, out var bqBlock, out newPos, settings))
            {
                blocks.Add(bqBlock);
                pos = newPos;
                continue;
            }

            if (TryParseThematicBreak(html, pos, out var hrBlock, out newPos))
            {
                blocks.Add(hrBlock);
                pos = newPos;
                continue;
            }

            // Skip unrecognized tags
            int closePos = html.IndexOf('>', pos);
            pos = closePos >= 0 ? closePos + 1 : pos + 1;
        }

        return blocks;
    }

    /// <summary>
    /// Try to parse a header block: &lt;h1&gt;...&lt;/h1&gt;
    /// </summary>
    private static bool TryParseHeader(string html, int startPos, out BlockElement block, out int endPos, MarkdownOutputSettings? settings = null)
    {
        block = null!;
        endPos = startPos;

        // Check for <h1-6 tag
        if (!html.AsSpan(startPos).StartsWith("<h", StringComparison.OrdinalIgnoreCase))
            return false;

        if (startPos + 2 >= html.Length || !char.IsDigit(html[startPos + 2]))
            return false;

        int level = html[startPos + 2] - '0';
        if (level < 1 || level > 6)
            return false;

        // Find closing tag
        string closeTag = $"</h{level}>";
        int closeStart = html.IndexOf(closeTag, startPos, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
            return false;

        // Extract content between tags
        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        string headerContent = html[(tagEnd + 1)..closeStart];

        // Parse inline content
        settings ??= new();
        var inline = ParseInlineContent(headerContent, BlockKind.Heading1, settings);

        // Create block element
        var headerKind = level switch
        {
            1 => BlockKind.Heading1,
            2 => BlockKind.Heading2,
            3 => BlockKind.Heading3,
            4 => BlockKind.Heading4,
            5 => BlockKind.Heading5,
            6 => BlockKind.Heading6,
            _ => BlockKind.Heading1, // Shouldn't happen
        };

        block = new BlockElement
        {
            Kind = headerKind,
            Content = inline,
        };

        endPos = closeStart + closeTag.Length;
        return true;
    }

    /// <summary>
    /// Try to parse a paragraph block: &lt;p&gt;...&lt;/p&gt;
    /// </summary>
    private static bool TryParseParagraph(string html, int startPos, out BlockElement block, out int endPos, MarkdownOutputSettings? settings = null)
    {
        block = null!;
        endPos = startPos;

        if (!html.AsSpan(startPos).StartsWith("<p", StringComparison.OrdinalIgnoreCase))
            return false;

        // Find closing tag
        int closeStart = html.IndexOf("</p>", startPos, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
            return false;

        // Extract content between tags
        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        string paraContent = html[(tagEnd + 1)..closeStart];

        // Parse inline content (includes <br> handling)
        settings ??= new();
        var inline = ParseInlineContent(paraContent, BlockKind.Paragraph, settings);

        block = new BlockElement
        {
            Kind = BlockKind.Paragraph,
            Content = inline,
        };

        endPos = closeStart + 4; // "</p>" is 4 characters
        return true;
    }

    /// <summary>
    /// Try to parse an unordered list: &lt;ul&gt;...&lt;li&gt;...&lt;/li&gt;...&lt;/ul&gt;
    /// </summary>
    private static bool TryParseUnorderedList(string html, int startPos, out BlockElement block, out int endPos, MarkdownOutputSettings? settings = null)
    {
        block = null!;
        endPos = startPos;

        if (!html.AsSpan(startPos).StartsWith("<ul", StringComparison.OrdinalIgnoreCase))
            return false;

        // Find closing tag
        int closeStart = html.IndexOf("</ul>", startPos, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
            return false;

        // Extract content between tags
        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        string listContent = html[(tagEnd + 1)..closeStart];

        // Parse list items
        settings ??= new();
        var items = ParseListItems(listContent, settings);

        block = new BlockElement
        {
            Kind = BlockKind.UnorderedListItem,
            NestedBlocks = items,
        };

        endPos = closeStart + 5; // "</ul>" is 5 characters
        return true;
    }

    /// <summary>
    /// Try to parse an ordered list: &lt;ol&gt;...&lt;li&gt;...&lt;/li&gt;...&lt;/ol&gt;
    /// </summary>
    private static bool TryParseOrderedList(string html, int startPos, out BlockElement block, out int endPos, MarkdownOutputSettings? settings = null)
    {
        block = null!;
        endPos = startPos;

        if (!html.AsSpan(startPos).StartsWith("<ol", StringComparison.OrdinalIgnoreCase))
            return false;

        // Find closing tag
        int closeStart = html.IndexOf("</ol>", startPos, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
            return false;

        // Extract content between tags
        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        string listContent = html[(tagEnd + 1)..closeStart];

        // Parse list items
        settings ??= new();
        var items = ParseListItems(listContent, settings);

        block = new BlockElement
        {
            Kind = BlockKind.OrderedListItem,
            NestedBlocks = items,
        };

        endPos = closeStart + 5; // "</ol>" is 5 characters
        return true;
    }

    /// <summary>
    /// Parse individual list items from list content.
    /// </summary>
    private static List<BlockElement> ParseListItems(string listContent, MarkdownOutputSettings? settings = null)
    {
        settings ??= new();
        var items = new List<BlockElement>();
        int pos = 0;

        while (pos < listContent.Length)
        {
            // Find next <li> tag
            int liStart = listContent.IndexOf("<li", pos, StringComparison.OrdinalIgnoreCase);
            if (liStart < 0)
                break;

            // Find closing </li>
            int liCloseStart = listContent.IndexOf("</li>", liStart, StringComparison.OrdinalIgnoreCase);
            if (liCloseStart < 0)
                break;

            // Extract content between tags
            int tagEnd = listContent.IndexOf('>', liStart);
            if (tagEnd < 0)
                break;

            string itemContent = listContent[(tagEnd + 1)..liCloseStart];

            // Parse inline content of list item
            var inline = ParseInlineContent(itemContent, BlockKind.UnorderedListItem, settings);

            items.Add(new BlockElement
            {
                Kind = BlockKind.UnorderedListItem,
                Content = inline,
            });

            pos = liCloseStart + 5; // "</li>" is 5 characters
        }

        return items;
    }

    /// <summary>
    /// Try to parse a blockquote: &lt;blockquote&gt;...&lt;/blockquote&gt;
    /// </summary>
    private static bool TryParseBlockquote(string html, int startPos, out BlockElement block, out int endPos, MarkdownOutputSettings? settings = null)
    {
        block = null!;
        endPos = startPos;

        if (!html.AsSpan(startPos).StartsWith("<blockquote", StringComparison.OrdinalIgnoreCase))
            return false;

        // Find closing tag
        int closeStart = html.IndexOf("</blockquote>", startPos, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
            return false;

        // Extract content between tags
        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        string quoteContent = html[(tagEnd + 1)..closeStart];

        // Parse blockquote as inline content
        settings ??= new();
        var inline = ParseInlineContent(quoteContent, BlockKind.Blockquote, settings);

        block = new BlockElement
        {
            Kind = BlockKind.Blockquote,
            Content = inline,
        };

        endPos = closeStart + 13; // "</blockquote>" is 13 characters
        return true;
    }

    /// <summary>
    /// Try to parse a thematic break (horizontal rule): &lt;hr&gt; or &lt;hr/&gt;
    /// </summary>
    private static bool TryParseThematicBreak(string html, int startPos, out BlockElement block, out int endPos)
    {
        block = null!;
        endPos = startPos;

        // Check for <hr tag
        if (!html.AsSpan(startPos).StartsWith("<hr", StringComparison.OrdinalIgnoreCase))
            return false;

        // Find the closing > (self-closing or with />)
        int closePos = html.IndexOf('>', startPos);
        if (closePos < 0)
            return false;

        // Create thematic break block (no content)
        block = new BlockElement
        {
            Kind = BlockKind.ThematicBreak,
            Content = new(),
        };

        endPos = closePos + 1;
        return true;
    }

    /// <summary>
    /// Stage 2: Parse inline content within a block.
    /// Handles text, formatting tags (span, strong, em), and hard breaks (br).
    /// </summary>
    internal static List<InlineContent> ParseInlineContent(
        string html,
        BlockKind context,
        MarkdownOutputSettings? settings = null)
    {
        settings ??= new();
        var segments = new List<InlineContent>();
        var textBuf = new StringBuilder();
        var styleStack = new Stack<InlineFormat>();
        int pos = 0;

        while (pos < html.Length)
        {
            char c = html[pos];

            if (c == '<')
            {
                // Flush accumulated text
                if (textBuf.Length > 0)
                {
                    var text = NormalizeWhitespace(textBuf.ToString(), settings.SoftBreak);
                    if (!string.IsNullOrEmpty(text))
                    {
                        segments.Add(new InlineContent
                        {
                            Text = text,
                            Format = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new(),
                        });
                    }
                    textBuf.Clear();
                }

                // Parse tag
                int tagEnd = html.IndexOf('>', pos);
                if (tagEnd < 0)
                    break;

                string tag = html[pos..(tagEnd + 1)];

                // Handle specific tags
                if (tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("<br />", StringComparison.OrdinalIgnoreCase))
                {
                    // Hard break: mark on last segment
                    if (segments.Count > 0)
                    {
                        segments[^1].FollowedByHardBreak = true;
                    }
                    // Else: leading break (not representable), skip silently
                }
                else if (tag.Equals("<strong>", StringComparison.OrdinalIgnoreCase) ||
                         tag.Equals("<b>", StringComparison.OrdinalIgnoreCase))
                {
                    var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
                    fmt.Bold = true;
                    styleStack.Push(fmt);
                }
                else if (tag.Equals("</strong>", StringComparison.OrdinalIgnoreCase) ||
                         tag.Equals("</b>", StringComparison.OrdinalIgnoreCase))
                {
                    if (styleStack.Count > 0)
                        styleStack.Pop();
                }
                else if (tag.Equals("<em>", StringComparison.OrdinalIgnoreCase) ||
                         tag.Equals("<i>", StringComparison.OrdinalIgnoreCase))
                {
                    var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
                    fmt.Italic = true;
                    styleStack.Push(fmt);
                }
                else if (tag.Equals("</em>", StringComparison.OrdinalIgnoreCase) ||
                         tag.Equals("</i>", StringComparison.OrdinalIgnoreCase))
                {
                    if (styleStack.Count > 0)
                        styleStack.Pop();
                }
                else if (tag.StartsWith("<span", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract color from style attribute
                    var (fg, bg) = ExtractColorsFromTag(tag);
                    var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
                    if (fg != null)
                        fmt.ForegroundColor = fg;
                    if (bg != null)
                        fmt.BackgroundColor = bg;
                    styleStack.Push(fmt);
                }
                else if (tag.Equals("</span>", StringComparison.OrdinalIgnoreCase))
                {
                    if (styleStack.Count > 0)
                        styleStack.Pop();
                }
                else if (tag.Equals("<code>", StringComparison.OrdinalIgnoreCase))
                {
                    var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
                    fmt.Code = true;
                    styleStack.Push(fmt);
                }
                else if (tag.Equals("</code>", StringComparison.OrdinalIgnoreCase))
                {
                    if (styleStack.Count > 0)
                        styleStack.Pop();
                }
                // Skip comments, unrecognized tags

                pos = tagEnd + 1;
            }
            else if (c == '&')
            {
                // Decode HTML entity
                int entityLength = HtmlParsingContext.DecodeEntity(html, pos, textBuf);
                pos += entityLength;
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
            var text = NormalizeWhitespace(textBuf.ToString(), settings.SoftBreak);
            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(new InlineContent
                {
                    Text = text,
                    Format = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new(),
                });
            }
        }

        return segments;
    }

    /// <summary>
    /// Stage 3: Convert structured blocks to markdown string.
    /// Applies settings and handles all formatting.
    /// </summary>
    internal static string ConvertToMarkdown(List<BlockElement> blocks, MarkdownOutputSettings? settings = null)
    {
        settings ??= new();
        var output = new List<string>();
        BlockKind? previousBlockKind = null;

        foreach (var block in blocks)
        {
            // Add blank line between consecutive paragraphs for proper separation
            if (previousBlockKind == BlockKind.Paragraph && block.Kind == BlockKind.Paragraph)
            {
                output.Add("");
            }

            switch (block.Kind)
            {
                case BlockKind.Heading1:
                case BlockKind.Heading2:
                case BlockKind.Heading3:
                case BlockKind.Heading4:
                case BlockKind.Heading5:
                case BlockKind.Heading6:
                    {
                        int level = block.GetHeadingLevel() ?? 1;
                        string hashes = new('#', level);
                        string headerText = FormatInlineSegments(block.Content, settings);
                        output.Add($"{hashes} {headerText}");
                        break;
                    }

                case BlockKind.Paragraph:
                    {
                        var paraLines = FormatParagraph(block.Content, settings);
                        output.AddRange(paraLines);
                        break;
                    }

                case BlockKind.UnorderedListItem:
                    {
                        if (block.NestedBlocks != null)
                        {
                            foreach (var item in block.NestedBlocks)
                            {
                                string itemText = FormatInlineSegments(item.Content, settings);
                                output.Add($"- {itemText}");
                            }
                        }
                        break;
                    }

                case BlockKind.OrderedListItem:
                    {
                        if (block.NestedBlocks != null)
                        {
                            int itemNum = 1;
                            foreach (var item in block.NestedBlocks)
                            {
                                string itemText = FormatInlineSegments(item.Content, settings);
                                output.Add($"{itemNum}. {itemText}");
                                itemNum++;
                            }
                        }
                        break;
                    }

                case BlockKind.Blockquote:
                    {
                        var quoteLines = FormatParagraph(block.Content, settings);
                        foreach (var line in quoteLines)
                            output.Add($"> {line}");
                        break;
                    }

                case BlockKind.ThematicBreak:
                    output.Add("---");
                    break;

                default:
                    // Placeholder for other block types
                    if (block.Content.Count > 0)
                        output.Add(FormatInlineSegments(block.Content, settings));
                    break;
            }

            // Track block kind for next iteration (for paragraph separation)
            previousBlockKind = block.Kind;
        }

        // Join blocks without extra blank lines (browsers don't display them anyway)
        // CommonMark spec requires blank lines only between certain block types
        // (e.g., between consecutive paragraphs for proper paragraph separation).
        return string.Join("\n", output);
    }

    /// <summary>
    /// Format inline segments for a paragraph, respecting hard breaks.
    /// Returns list of lines (each line is a string).
    /// </summary>
    private static List<string> FormatParagraph(List<InlineContent> content, MarkdownOutputSettings settings)
    {
        var lines = new List<string>();
        var currentLine = new StringBuilder();

        foreach (var segment in content)
        {
            string formatted = FormatSegment(segment, settings);
            currentLine.Append(formatted);

            if (segment.FollowedByHardBreak)
            {
                // Hard break: apply HardBreak setting
                if (settings.HardBreak == DocsCanvas.HardBreakStyle.Backslash)
                    currentLine.Append("\\");
                else if (settings.HardBreak == DocsCanvas.HardBreakStyle.TrailingSpaces)
                    currentLine.Append("  ");

                lines.Add(currentLine.ToString());
                currentLine.Clear();
            }
        }

        // Add final line if any content
        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines;
    }

    /// <summary>
    /// Format all inline segments into a single line (no breaks).
    /// Used for headers and other single-line content.
    /// </summary>
    private static string FormatInlineSegments(List<InlineContent> content, MarkdownOutputSettings settings)
    {
        var result = new StringBuilder();
        foreach (var segment in content)
        {
            result.Append(FormatSegment(segment, settings));
        }
        return result.ToString();
    }

    /// <summary>
    /// Format a single inline segment with all its styling.
    /// </summary>
    private static string FormatSegment(InlineContent segment, MarkdownOutputSettings settings)
    {
        string text = segment.Text;

        // Apply formatting
        if (segment.Format.Bold)
            text = $"**{text}**";

        if (segment.Format.Italic)
            text = $"*{text}*";

        if (segment.Format.Code)
            text = $"`{text}`";

        // Apply colors as HTML comments (if enabled)
        if (settings.PreserveColors && segment.Format.ForegroundColor != null)
        {
            string colorStr = FormatColor(segment.Format.ForegroundColor.Value);
            text = $"<!--@fg:{colorStr}-->{text}<!--/@fg-->";
        }

        return text;
    }

    /// <summary>
    /// Normalize whitespace according to soft break mode.
    /// Relaxed: collapse all whitespace to single space (matches browser behavior)
    /// Strict: preserve line structure
    /// </summary>
    private static string NormalizeWhitespace(string text, DocsCanvas.SoftBreakMode softBreakMode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        if (softBreakMode == DocsCanvas.SoftBreakMode.Relaxed)
        {
            // Default: collapse all whitespace to single space (matches browser rendering)
            return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        }
        else // Strict mode
        {
            // Preserve line breaks but normalize internal spaces on each line
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return "";

            var normalized = lines
                .Select(line => System.Text.RegularExpressions.Regex.Replace(line.Trim(), @"\s+", " "))
                .Where(line => !string.IsNullOrEmpty(line));

            return string.Join("\n", normalized);
        }
    }

    /// <summary>Extract foreground and background colors from a tag's style attribute.</summary>
    private static (RgbColor? fg, RgbColor? bg) ExtractColorsFromTag(string tag)
    {
        int styleIdx = tag.IndexOf("style=", StringComparison.OrdinalIgnoreCase);
        if (styleIdx < 0)
            return (null, null);

        int quotePos = styleIdx + 6;
        if (quotePos >= tag.Length)
            return (null, null);

        char quote = tag[quotePos];
        if (quote != '"' && quote != '\'')
            return (null, null);

        int styleStart = quotePos + 1;
        int styleEnd = tag.IndexOf(quote, styleStart);
        if (styleEnd < 0)
            return (null, null);

        string style = tag[styleStart..styleEnd];

        // Look for color: value
        int colorIdx = style.IndexOf("color:", StringComparison.OrdinalIgnoreCase);
        RgbColor? fg = null;
        if (colorIdx >= 0)
        {
            // Check it's not background-color
            bool isBg = colorIdx > 0 && style[colorIdx - 1] == '-';
            if (!isBg)
            {
                int valueStart = colorIdx + 6;
                int valueEnd = style.IndexOfAny(new[] { ';', '}' }, valueStart);
                if (valueEnd < 0)
                    valueEnd = style.Length;

                string colorValue = style[valueStart..valueEnd].Trim();
                fg = HtmlParsingContext.ParseCssColor(colorValue.AsSpan());
            }
        }

        // For now, we don't extract background colors in Phase 1
        RgbColor? bg = null;

        return (fg, bg);
    }

    /// <summary>Format an RgbColor as a color name or hex code.</summary>
    private static string FormatColor(RgbColor color)
    {
        return MarkdownParser.TryGetColorName(color) ?? color.ToHex();
    }

    /// <summary>Create a deep copy of an InlineFormat to avoid sharing references in style stack.</summary>
    private static InlineFormat CloneFormat(InlineFormat format)
    {
        return new InlineFormat
        {
            ForegroundColor = format.ForegroundColor,
            BackgroundColor = format.BackgroundColor,
            Bold = format.Bold,
            Italic = format.Italic,
            Code = format.Code,
        };
    }
}
