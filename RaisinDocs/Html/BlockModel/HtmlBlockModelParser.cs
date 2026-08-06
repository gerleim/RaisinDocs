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
/// </summary>
internal static class HtmlBlockModelParser
{
    /// <summary>
    /// Stage 1: Extract block-level HTML elements and return structured blocks.
    /// </summary>
    internal static List<BlockElement> ParseBlockStructure(string html)
    {
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
            if (TryParseHeader(html, pos, out var headerBlock, out var newPos))
            {
                blocks.Add(headerBlock);
                pos = newPos;
                continue;
            }

            if (TryParseParagraph(html, pos, out var paraBlock, out newPos))
            {
                blocks.Add(paraBlock);
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
    private static bool TryParseHeader(string html, int startPos, out BlockElement block, out int endPos)
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
        var inline = ParseInlineContent(headerContent, BlockKind.Heading1);

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
    private static bool TryParseParagraph(string html, int startPos, out BlockElement block, out int endPos)
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
        var inline = ParseInlineContent(paraContent, BlockKind.Paragraph);

        block = new BlockElement
        {
            Kind = BlockKind.Paragraph,
            Content = inline,
        };

        endPos = closeStart + 4; // "</p>" is 4 characters
        return true;
    }

    /// <summary>
    /// Stage 2: Parse inline content within a block.
    /// Handles text, formatting tags (span, strong, em), and hard breaks (br).
    /// </summary>
    internal static List<InlineContent> ParseInlineContent(string html, BlockKind context)
    {
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
                    var text = NormalizeWhitespace(textBuf.ToString());
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
            var text = NormalizeWhitespace(textBuf.ToString());
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

        foreach (var block in blocks)
        {
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

                case BlockKind.ThematicBreak:
                    output.Add("---");
                    break;

                default:
                    // Placeholder for other block types
                    if (block.Content.Count > 0)
                        output.Add(FormatInlineSegments(block.Content, settings));
                    break;
            }

            // Separate blocks with blank line
            output.Add("");
        }

        return string.Join("\n", output).TrimEnd();
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

    /// <summary>Normalize whitespace: collapse multiple spaces/newlines to single space.</summary>
    private static string NormalizeWhitespace(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
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
