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

        // Parse and convert. The stylesheet is read from the whole payload, not the fragment:
        // Excel declares cell formatting as class rules in the document head, which sits
        // outside the fragment markers.
        settings ??= new();
        var blocks = ParseBlockStructure(contentToConvert, settings, HtmlStyleSheet.Parse(html));
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
    internal static List<BlockElement> ParseBlockStructure(
        string html, MarkdownOutputSettings? settings = null, HtmlStyleSheet? styles = null)
    {
        settings ??= new();
        styles ??= HtmlStyleSheet.Parse(html);
        var blocks = new List<BlockElement>();
        int pos = 0;

        while (pos < html.Length)
        {
            // Skip whitespace and non-tag content
            while (pos < html.Length && html[pos] != '<')
                pos++;

            if (pos >= html.Length)
                break;

            if (HtmlTableParser.TryParseTable(html, pos, styles, settings, out var tableBlock, out var afterTable))
            {
                blocks.Add(tableBlock);
                pos = afterTable;
                continue;
            }

            // Excel's CF_HTML fragment starts inside the <table>, so the rows arrive
            // without their enclosing element.
            if (HtmlTableParser.TryParseOrphanRows(html, pos, styles, settings, out var rowsBlock, out var afterRows))
            {
                blocks.Add(rowsBlock);
                pos = afterRows;
                continue;
            }

            // Try to match block-level tags
            if (TryParseHeader(html, pos, out var headerBlock, out var newPos, settings))
            {
                blocks.Add(headerBlock);
                pos = newPos;
                continue;
            }

            // Ahead of the paragraph check, which would otherwise take "<pre" for "<p".
            if (TryParsePreformatted(html, pos, out var preBlock, out newPos))
            {
                if (preBlock.PreformattedLines!.Count > 0)
                    blocks.Add(preBlock);
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
    /// Try to parse preformatted text: &lt;pre&gt;...&lt;/pre&gt;.
    /// </summary>
    /// <remarks>
    /// Terminals and RaisinDocs's own copy-out put coloured text here, one source line per line,
    /// so line breaks and whitespace are kept rather than collapsed. The block counts as a
    /// paragraph for separation purposes: its lines are ordinary markdown text.
    /// </remarks>
    private static bool TryParsePreformatted(string html, int startPos, out BlockElement block, out int endPos)
    {
        block = null!;
        endPos = startPos;

        if (!HtmlTagScanner.IsOpenTag(html, startPos, "pre"))
            return false;

        int contentStart = HtmlTagScanner.EndOfTag(html, startPos);
        int closeStart = HtmlTagScanner.FindMatchingClose(html, contentStart, "pre");
        int contentEnd = closeStart < 0 ? html.Length : closeStart;

        block = new BlockElement
        {
            Kind = BlockKind.Paragraph,
            PreformattedLines = ParsePreformattedLines(html[contentStart..contentEnd]),
        };

        endPos = closeStart < 0 ? html.Length : HtmlTagScanner.EndOfTag(html, closeStart);
        return true;
    }

    /// <summary>
    /// Splits a &lt;pre&gt; block's inner HTML into lines of styled segments. Newlines and
    /// &lt;br&gt; end a line; a style that spans a newline carries on into the next line.
    /// </summary>
    private static List<List<InlineContent>> ParsePreformattedLines(string html)
    {
        var lines = new List<List<InlineContent>> { new() };
        var textBuf = new StringBuilder();
        var styleStack = new Stack<InlineFormat>();

        void Flush()
        {
            if (textBuf.Length == 0) return;
            lines[^1].Add(new InlineContent
            {
                Text = textBuf.ToString(),
                Format = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new(),
            });
            textBuf.Clear();
        }

        void Push(Action<InlineFormat> apply)
        {
            var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
            apply(fmt);
            styleStack.Push(fmt);
        }

        // HTML ignores a newline straight after the opening tag.
        int pos = html.StartsWith("\r\n", StringComparison.Ordinal) ? 2
            : html.StartsWith('\n') ? 1
            : 0;

        while (pos < html.Length)
        {
            char c = html[pos];

            if (c == '<')
            {
                Flush();

                if (html.AsSpan(pos).StartsWith("<!--"))
                {
                    int commentEnd = html.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                    pos = commentEnd < 0 ? html.Length : commentEnd + 3;
                    continue;
                }

                int tagEnd = html.IndexOf('>', pos);
                if (tagEnd < 0)
                    break;

                if (HtmlTagScanner.IsOpenTag(html, pos, "br"))
                {
                    lines.Add(new());
                }
                else if (HtmlTagScanner.IsOpenTag(html, pos, "span"))
                {
                    // Same rule as ParseInlineContent: a span only ever adds emphasis.
                    var spanFormat = HtmlStyleSheet.Empty.ResolveFormat(html[pos..(tagEnd + 1)]);
                    Push(fmt =>
                    {
                        fmt.ForegroundColor = spanFormat.ForegroundColor ?? fmt.ForegroundColor;
                        fmt.BackgroundColor = spanFormat.BackgroundColor ?? fmt.BackgroundColor;
                        fmt.Bold |= spanFormat.Bold;
                        fmt.Italic |= spanFormat.Italic;
                    });
                }
                else if (HtmlTagScanner.IsOpenTag(html, pos, "b") || HtmlTagScanner.IsOpenTag(html, pos, "strong"))
                {
                    Push(fmt => fmt.Bold = true);
                }
                else if (HtmlTagScanner.IsOpenTag(html, pos, "i") || HtmlTagScanner.IsOpenTag(html, pos, "em"))
                {
                    Push(fmt => fmt.Italic = true);
                }
                else if (HtmlTagScanner.IsCloseTag(html, pos, "span")
                         || HtmlTagScanner.IsCloseTag(html, pos, "b") || HtmlTagScanner.IsCloseTag(html, pos, "strong")
                         || HtmlTagScanner.IsCloseTag(html, pos, "i") || HtmlTagScanner.IsCloseTag(html, pos, "em"))
                {
                    if (styleStack.Count > 0)
                        styleStack.Pop();
                }
                // Anything else, <code> included, carries no formatting worth keeping here.

                pos = tagEnd + 1;
            }
            else if (c == '\n')
            {
                Flush();
                lines.Add(new());
                pos++;
            }
            else if (c == '\r')
            {
                pos++;
            }
            else if (c == '&')
            {
                pos += HtmlParsingContext.DecodeEntity(html, pos, textBuf);
            }
            else
            {
                textBuf.Append(c);
                pos++;
            }
        }

        Flush();

        // A trailing newline before </pre> ends the last line; it does not start another.
        while (lines.Count > 0 && lines[^1].Count == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines;
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

        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        // Depth-aware: the first </ul> after this point may close a nested list, not this one.
        int closeStart = HtmlTagScanner.FindMatchingClose(html, tagEnd + 1, "ul");
        if (closeStart < 0)
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

        endPos = HtmlTagScanner.EndOfTag(html, closeStart);
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

        int tagEnd = html.IndexOf('>', startPos);
        if (tagEnd < 0)
            return false;

        // Depth-aware: the first </ol> after this point may close a nested list, not this one.
        int closeStart = HtmlTagScanner.FindMatchingClose(html, tagEnd + 1, "ol");
        if (closeStart < 0)
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

        endPos = HtmlTagScanner.EndOfTag(html, closeStart);
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
            int liStart = HtmlTagScanner.IndexOfOpenTag(listContent, pos, "li");
            if (liStart < 0)
                break;

            // Extract content between tags
            int tagEnd = listContent.IndexOf('>', liStart);
            if (tagEnd < 0)
                break;

            // Depth-aware: an item holding a nested list contains further </li> tags of its own.
            int liCloseStart = HtmlTagScanner.FindMatchingClose(listContent, tagEnd + 1, "li");
            int contentEnd = liCloseStart < 0 ? listContent.Length : liCloseStart;

            string itemContent = listContent[(tagEnd + 1)..contentEnd];

            // A list nested inside the item is a child block, not part of the item's text. Left
            // in the inline content its text would be flattened into this item's own line.
            var (inlineHtml, nestedLists) = SplitListItemContent(itemContent, settings);

            items.Add(new BlockElement
            {
                Kind = BlockKind.UnorderedListItem,
                Content = ParseInlineContent(inlineHtml, BlockKind.UnorderedListItem, settings),
                NestedBlocks = nestedLists.Count > 0 ? nestedLists : null,
            });

            pos = liCloseStart < 0 ? listContent.Length : HtmlTagScanner.EndOfTag(listContent, liCloseStart);
        }

        return items;
    }

    /// <summary>
    /// Splits a list item's inner HTML into the text belonging to the item itself and any lists
    /// nested inside it.
    /// </summary>
    private static (string Inline, List<BlockElement> Nested) SplitListItemContent(
        string itemContent, MarkdownOutputSettings settings)
    {
        int listStart = -1;
        for (int i = 0; i < itemContent.Length; i++)
        {
            if (itemContent[i] != '<') continue;
            if (HtmlTagScanner.IsOpenTag(itemContent, i, "ul") || HtmlTagScanner.IsOpenTag(itemContent, i, "ol"))
            {
                listStart = i;
                break;
            }
        }

        var nested = new List<BlockElement>();
        if (listStart < 0)
            return (itemContent, nested);

        int pos = listStart;
        while (pos < itemContent.Length)
        {
            if (TryParseUnorderedList(itemContent, pos, out var ul, out int ulEnd, settings))
            {
                nested.Add(ul);
                pos = ulEnd;
            }
            else if (TryParseOrderedList(itemContent, pos, out var ol, out int olEnd, settings))
            {
                nested.Add(ol);
                pos = olEnd;
            }
            else
            {
                pos++;
            }
        }

        return (itemContent[..listStart], nested);
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

        // Parsed as one run of inline content, the quote's <p> elements would run together
        // ("Line oneLine two"). A quote of several paragraphs keeps them as nested blocks.
        settings ??= new();
        var paragraphs = ParseQuoteParagraphs(quoteContent, settings);

        block = new BlockElement
        {
            Kind = BlockKind.Blockquote,
            Content = paragraphs.Count == 1 ? paragraphs[0] : new(),
            NestedBlocks = paragraphs.Count > 1
                ? paragraphs.Select(p => new BlockElement { Kind = BlockKind.Paragraph, Content = p }).ToList()
                : null,
        };

        endPos = closeStart + 13; // "</blockquote>" is 13 characters
        return true;
    }

    /// <summary>
    /// Splits a blockquote's inner HTML into paragraphs: one per &lt;p&gt; element, and one per
    /// stretch of loose text between them. Stretches with no text are dropped.
    /// </summary>
    private static List<List<InlineContent>> ParseQuoteParagraphs(string content, MarkdownOutputSettings settings)
    {
        var paragraphs = new List<List<InlineContent>>();

        void Add(int start, int end)
        {
            var inline = ParseInlineContent(content[start..end], BlockKind.Blockquote, settings);
            if (inline.Any(s => !string.IsNullOrWhiteSpace(s.Text)))
                paragraphs.Add(inline);
        }

        int pos = 0;
        while (pos < content.Length)
        {
            int pStart = HtmlTagScanner.IndexOfOpenTag(content, pos, "p");
            Add(pos, pStart < 0 ? content.Length : pStart);
            if (pStart < 0) break;

            int inner = HtmlTagScanner.EndOfTag(content, pStart);
            int pClose = HtmlTagScanner.FindMatchingClose(content, inner, "p");
            Add(inner, pClose < 0 ? content.Length : pClose);
            pos = pClose < 0 ? content.Length : HtmlTagScanner.EndOfTag(content, pClose);
        }

        return paragraphs;
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
                    // Resolve the span's own style attribute. Word and Excel express emphasis
                    // as font-weight/font-style here rather than with <b>/<i> tags.
                    var spanFormat = HtmlStyleSheet.Empty.ResolveFormat(tag);
                    var fmt = styleStack.Count > 0 ? CloneFormat(styleStack.Peek()) : new();
                    if (spanFormat.ForegroundColor != null)
                        fmt.ForegroundColor = spanFormat.ForegroundColor;
                    if (spanFormat.BackgroundColor != null)
                        fmt.BackgroundColor = spanFormat.BackgroundColor;
                    // Only ever add emphasis: a span that states a normal weight is not
                    // an instruction to un-bold its surroundings.
                    fmt.Bold |= spanFormat.Bold;
                    fmt.Italic |= spanFormat.Italic;
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
    /// <summary>
    /// Whether a blank line has to separate two adjacent blocks to stop the second being read as
    /// a continuation of the first.
    /// </summary>
    /// <remarks>
    /// Blocks are otherwise emitted adjacent on purpose - browsers do not show the gap, so adding
    /// one everywhere would pad pasted content with blank lines it never had. Only a paragraph is
    /// at risk: written straight after a list item or another paragraph it becomes a lazy
    /// continuation of it, which renders indented under the bullet instead of as its own block.
    /// Headings, thematic breaks, blockquotes, fenced code and further list items all interrupt a
    /// paragraph on their own and need no separator.
    /// </remarks>
    private static bool NeedsBlankLineBetween(BlockKind? previous, BlockKind current)
    {
        if (current != BlockKind.Paragraph) return false;

        return previous is BlockKind.Paragraph
            or BlockKind.UnorderedListItem
            or BlockKind.OrderedListItem;
    }

    internal static string ConvertToMarkdown(List<BlockElement> blocks, MarkdownOutputSettings? settings = null)
    {
        settings ??= new();
        var output = new List<string>();
        BlockKind? previousBlockKind = null;

        foreach (var block in blocks)
        {
            if (NeedsBlankLineBetween(previousBlockKind, block.Kind))
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
                        var paraLines = block.PreformattedLines != null
                            ? FormatPreformatted(block.PreformattedLines, settings)
                            : FormatParagraph(block.Content, settings);
                        output.AddRange(paraLines);
                        break;
                    }

                case BlockKind.UnorderedListItem:
                    RenderList(block, ordered: false, depth: 0, output, settings);
                    break;

                case BlockKind.OrderedListItem:
                    RenderList(block, ordered: true, depth: 0, output, settings);
                    break;

                case BlockKind.Blockquote:
                    {
                        var paragraphs = block.NestedBlocks?.Select(p => p.Content) ?? [block.Content];
                        bool first = true;
                        foreach (var paragraph in paragraphs)
                        {
                            // A bare '>' separates paragraphs inside the quote, as a blank line
                            // does outside it; without it they would merge into one.
                            if (!first)
                                output.Add(">");
                            first = false;

                            foreach (var line in FormatParagraph(paragraph, settings))
                                output.Add($"> {line}");
                        }
                        break;
                    }

                case BlockKind.ThematicBreak:
                    output.Add("---");
                    break;

                case BlockKind.TableHeaderRow:
                    if (block.TableData != null)
                        output.AddRange(FormatTable(block.TableData, settings));
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
    /// Renders a parsed table as GitHub-flavored markdown: header row, alignment separator,
    /// then the data rows.
    /// </summary>
    private static List<string> FormatTable(TableBlockData table, MarkdownOutputSettings settings)
    {
        var lines = new List<string>();
        int width = table.ColumnCount;
        if (width == 0 || table.Rows.Count == 0) return lines;

        // Markdown allows exactly one header row; extra <th> rows become ordinary rows.
        int headerIndex = table.Rows.FindIndex(r => r.IsHeader);
        if (headerIndex < 0) headerIndex = 0;

        lines.Add(FormatTableRow(table.Rows[headerIndex], width, settings));
        lines.Add(FormatSeparatorRow(table.Alignments, width));

        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (i == headerIndex) continue;
            lines.Add(FormatTableRow(table.Rows[i], width, settings));
        }

        return lines;
    }

    private static string FormatTableRow(TableRowContent row, int width, MarkdownOutputSettings settings)
    {
        var sb = new StringBuilder("|");
        for (int col = 0; col < width; col++)
        {
            string cell = col < row.Cells.Count
                ? EscapeTableCell(FormatCellSegments(row.Cells[col].Content, settings))
                : "";
            sb.Append(' ').Append(cell).Append(" |");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats one cell's segments. Unlike a paragraph, a cell cannot break across lines,
    /// so a hard break inside it becomes a space.
    /// </summary>
    private static string FormatCellSegments(List<InlineContent> content, MarkdownOutputSettings settings)
    {
        var sb = new StringBuilder();
        foreach (var segment in content)
        {
            sb.Append(FormatSegment(segment, settings));
            if (segment.FollowedByHardBreak) sb.Append(' ');
        }
        return sb.ToString();
    }

    private static string FormatSeparatorRow(List<ColumnAlignment> alignments, int width)
    {
        var sb = new StringBuilder("|");
        for (int col = 0; col < width; col++)
        {
            var align = col < alignments.Count ? alignments[col] : ColumnAlignment.Left;
            sb.Append(align switch
            {
                ColumnAlignment.Center => " :---: |",
                ColumnAlignment.Right => " ---: |",
                _ => " --- |",
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Makes cell text safe for a markdown table row: pipes would end the cell, and a table
    /// row cannot span lines.
    /// </summary>
    private static string EscapeTableCell(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '|') sb.Append("\\|");
            else if (c == '\n' || c == '\r') sb.Append(' ');
            else sb.Append(c);
        }

        // Collapse the runs that flattened breaks can leave behind.
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
                // Trim before the marker: segments carry edge whitespace so inter-element
                // gaps survive, but a trailing space here would corrupt the break syntax.
                string line = currentLine.ToString().Trim();

                // Hard break: apply HardBreak setting
                if (settings.HardBreak == DocsCanvas.HardBreakStyle.Backslash)
                    line += "\\";
                else if (settings.HardBreak == DocsCanvas.HardBreakStyle.TrailingSpaces)
                    line += "  ";

                lines.Add(line);
                currentLine.Clear();
            }
        }

        // Add final line if any content. Lines ended by a hard break were already trimmed
        // above, before their break marker was appended.
        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString().Trim());

        return lines;
    }

    /// <summary>
    /// Formats a &lt;pre&gt; block one markdown line per line. Two or more consecutive lines in
    /// one uniform colour become a colour div, so the colour is written once rather than on
    /// every line.
    /// </summary>
    private static List<string> FormatPreformatted(List<List<InlineContent>> lines, MarkdownOutputSettings settings)
    {
        var uniform = lines.Select(line => UniformColors(line, settings)).ToList();
        var output = new List<string>();
        int i = 0;

        while (i < lines.Count)
        {
            if (uniform[i] is { } colors)
            {
                int runEnd = i + 1;
                while (runEnd < lines.Count && uniform[runEnd] == colors)
                    runEnd++;

                if (runEnd - i >= 2)
                {
                    output.Add($"<!--@div {ColorProps(colors.Fg, colors.Bg)}-->");
                    for (int k = i; k < runEnd; k++)
                        output.Add(FormatEmphasisRuns(lines[k], 0, lines[k].Count));
                    output.Add("<!--/@div-->");
                    i = runEnd;
                    continue;
                }
            }

            output.Add(FormatColorRuns(lines[i], settings));
            i++;
        }

        return output;
    }

    /// <summary>
    /// The colours a line is entirely written in, or null when it is uncoloured, empty, or mixes
    /// colours.
    /// </summary>
    private static (RgbColor? Fg, RgbColor? Bg)? UniformColors(List<InlineContent> line, MarkdownOutputSettings settings)
    {
        if (line.Count == 0) return null;

        var first = SegmentColors(line[0], settings);
        if (first.Fg == null && first.Bg == null) return null;

        for (int i = 1; i < line.Count; i++)
        {
            if (SegmentColors(line[i], settings) != first) return null;
        }

        return first;
    }

    private static (RgbColor? Fg, RgbColor? Bg) SegmentColors(InlineContent segment, MarkdownOutputSettings settings)
        => settings.PreserveColors
            ? (segment.Format.ForegroundColor, segment.Format.BackgroundColor)
            : (null, null);

    /// <summary>
    /// Formats one line, wrapping each run of same-coloured segments in a single colour tag, so
    /// emphasis changing inside a coloured run does not split the tag.
    /// </summary>
    private static string FormatColorRuns(List<InlineContent> line, MarkdownOutputSettings settings)
    {
        var sb = new StringBuilder();
        int i = 0;

        while (i < line.Count)
        {
            var colors = SegmentColors(line[i], settings);
            int runEnd = i + 1;
            while (runEnd < line.Count && SegmentColors(line[runEnd], settings) == colors)
                runEnd++;

            string text = FormatEmphasisRuns(line, i, runEnd);
            if (colors.Fg != null && colors.Bg != null)
                sb.Append($"<!--@{ColorProps(colors.Fg, colors.Bg)}-->{text}<!--/@-->");
            else if (colors.Fg != null)
                sb.Append($"<!--@fg:{FormatColor(colors.Fg.Value)}-->{text}<!--/@fg-->");
            else if (colors.Bg != null)
                sb.Append($"<!--@bg:{FormatColor(colors.Bg.Value)}-->{text}<!--/@bg-->");
            else
                sb.Append(text);

            i = runEnd;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats segments [start, end), joining neighbours with the same emphasis so each run gets
    /// one pair of markers.
    /// </summary>
    private static string FormatEmphasisRuns(List<InlineContent> segments, int start, int end)
    {
        var sb = new StringBuilder();
        int i = start;

        while (i < end)
        {
            bool bold = segments[i].Format.Bold, italic = segments[i].Format.Italic;
            var text = new StringBuilder();
            int runEnd = i;
            while (runEnd < end && segments[runEnd].Format.Bold == bold && segments[runEnd].Format.Italic == italic)
                text.Append(segments[runEnd++].Text);

            string marker = bold && italic ? "***" : bold ? "**" : italic ? "*" : "";
            sb.Append(marker).Append(text).Append(marker);
            i = runEnd;
        }

        return sb.ToString();
    }

    /// <summary>"fg:X bg:Y", with either half left out when that colour is absent.</summary>
    private static string ColorProps(RgbColor? fg, RgbColor? bg)
    {
        var parts = new List<string>(2);
        if (fg != null) parts.Add($"fg:{FormatColor(fg.Value)}");
        if (bg != null) parts.Add($"bg:{FormatColor(bg.Value)}");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Emits one line per item, then recurses into any list nested inside an item, indenting each
    /// level by two spaces so the nesting survives as markdown.
    /// </summary>
    private static void RenderList(
        BlockElement list, bool ordered, int depth, List<string> output, MarkdownOutputSettings settings)
    {
        if (list.NestedBlocks == null) return;

        string indent = new string(' ', depth * 2);
        int itemNum = 1;

        foreach (var item in list.NestedBlocks)
        {
            string marker = ordered ? $"{itemNum++}. " : "- ";
            // Whitespace sits between an item's text and a list nested after it; without trimming
            // it would be emitted as a trailing space on the item's line.
            string itemText = FormatInlineSegments(item.Content, settings).Trim();
            output.Add($"{indent}{marker}{itemText}");

            if (item.NestedBlocks == null) continue;

            foreach (var child in item.NestedBlocks)
                RenderList(child, child.Kind == BlockKind.OrderedListItem, depth + 1, output, settings);
        }
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
        return result.ToString().Trim();
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
            // Default: collapse all whitespace to single space (matches browser rendering).
            // Edge whitespace is kept as a single space so that inter-element gaps
            // ("Some <b>bold</b>") are not lost; blocks trim their own assembled line.
            string lead = char.IsWhiteSpace(text[0]) ? " " : "";
            string trail = char.IsWhiteSpace(text[^1]) ? " " : "";
            return lead + System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ") + trail;
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
