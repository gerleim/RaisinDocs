using System.Text;
using System.Text.RegularExpressions;

namespace RaisinDocs;

/// <summary>
/// Converts HTML list elements (ul/ol with li) to RaisinDocs markdown list syntax.
/// Handles unordered lists (- prefix), ordered lists (1. prefix), nested lists with indentation,
/// and preserves inline formatting within list items.
/// </summary>
internal static class ListConverter
{
    /// <summary>
    /// Converts a list (ul or ol) element to markdown list syntax.
    /// </summary>
    internal static string ConvertList(string html, bool isOrdered = false, int nestingLevel = 0)
    {
        var result = new StringBuilder();
        string indent = new string(' ', nestingLevel * 2);
        int itemNumber = 1;

        int pos = 0;
        while (pos < html.Length)
        {
            // Look for <li> tags
            int liStart = html.IndexOf("<li", pos, StringComparison.OrdinalIgnoreCase);
            if (liStart < 0) break;

            // Find end of <li> opening tag
            int liTagEnd = html.IndexOf('>', liStart);
            if (liTagEnd < 0) break;

            // Find matching </li> (accounting for nested lists)
            int liCloseStart = FindMatchingLiCloseTag(html, liTagEnd + 1);
            if (liCloseStart < 0) break;

            // Extract content between <li> and </li>
            string itemContent = html[(liTagEnd + 1)..liCloseStart];
            string marker = isOrdered ? $"{itemNumber}. " : "- ";

            // Convert nested lists within this item
            string convertedContent = ConvertNestedLists(itemContent, nestingLevel + 1);

            // Convert inline HTML to markdown
            convertedContent = ConvertInlineHtml(convertedContent);

            // Add the list item
            result.Append(indent).Append(marker).Append(convertedContent.Trim());
            result.Append('\n');

            itemNumber++;
            pos = liCloseStart + 5; // length of "</li>"
        }

        return result.ToString();
    }

    /// <summary>
    /// Finds the position of the matching &lt;/li&gt; tag, accounting for nested lists.
    /// </summary>
    private static int FindMatchingLiCloseTag(string html, int startPos)
    {
        int liDepth = 1;
        int ulDepth = 0;
        int olDepth = 0;
        int pos = startPos;

        while (pos < html.Length)
        {
            // Find the next tag
            int nextTagStart = html.IndexOf('<', pos);
            if (nextTagStart < 0) return -1;

            int nextTagEnd = html.IndexOf('>', nextTagStart);
            if (nextTagEnd < 0) return -1;

            string tag = html[nextTagStart..(nextTagEnd + 1)];

            // Check for list-related tags
            if (tag.StartsWith("</li>", StringComparison.OrdinalIgnoreCase))
            {
                liDepth--;
                if (liDepth == 0)
                    return nextTagStart; // Found our matching closing tag
            }
            else if (tag.StartsWith("<li", StringComparison.OrdinalIgnoreCase) && !tag.StartsWith("</li>", StringComparison.OrdinalIgnoreCase))
            {
                liDepth++;
            }
            else if (tag.StartsWith("<ul", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("<ol", StringComparison.OrdinalIgnoreCase))
            {
                if (tag[1] == 'u' || tag[1] == 'U') ulDepth++;
                else olDepth++;
            }
            else if (tag.StartsWith("</ul>", StringComparison.OrdinalIgnoreCase))
            {
                if (ulDepth > 0) ulDepth--;
            }
            else if (tag.StartsWith("</ol>", StringComparison.OrdinalIgnoreCase))
            {
                if (olDepth > 0) olDepth--;
            }

            pos = nextTagEnd + 1;
        }

        return -1;
    }

    private static string ConvertNestedLists(string content, int nestingLevel)
    {
        var result = new StringBuilder();
        int pos = 0;

        while (pos < content.Length)
        {
            // Look for nested <ul> or <ol>
            int ulStart = content.IndexOf("<ul", pos, StringComparison.OrdinalIgnoreCase);
            int olStart = content.IndexOf("<ol", pos, StringComparison.OrdinalIgnoreCase);

            int listStart = -1;
            bool isOrdered = false;

            if (ulStart >= 0 && olStart >= 0)
            {
                if (ulStart < olStart)
                {
                    listStart = ulStart;
                    isOrdered = false;
                }
                else
                {
                    listStart = olStart;
                    isOrdered = true;
                }
            }
            else if (ulStart >= 0)
            {
                listStart = ulStart;
                isOrdered = false;
            }
            else if (olStart >= 0)
            {
                listStart = olStart;
                isOrdered = true;
            }

            if (listStart < 0)
            {
                // No nested list found, add remaining content
                string remaining = content[pos..].Trim();
                if (!string.IsNullOrEmpty(remaining))
                    result.Append(remaining);
                break;
            }

            // Add text before the list
            if (listStart > pos)
            {
                string before = content[pos..listStart].Trim();
                if (!string.IsNullOrEmpty(before))
                    result.Append(before).Append('\n');
            }

            // Find end of list opening tag
            int listTagEnd = content.IndexOf('>', listStart);
            if (listTagEnd < 0) break;

            // Find closing list tag
            string closeTag = isOrdered ? "</ol>" : "</ul>";
            int listCloseStart = content.IndexOf(closeTag, listTagEnd, StringComparison.OrdinalIgnoreCase);
            if (listCloseStart < 0) break;

            // Extract and convert the nested list
            string nestedListContent = content[(listTagEnd + 1)..listCloseStart];
            string converted = ConvertList(nestedListContent, isOrdered, nestingLevel);
            result.Append(converted);

            pos = listCloseStart + closeTag.Length;
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts inline HTML tags to markdown syntax (e.g., &lt;strong&gt; to **, &lt;em&gt; to *).
    /// Handles tags with attributes (e.g., &lt;strong data-start="0"&gt;).
    /// </summary>
    private static string ConvertInlineHtml(string content)
    {
        // Convert <strong> and <b> to ** (handle attributes with [^>]*)
        content = Regex.Replace(content, @"</?(?:strong|b)(?:\s[^>]*)?>", "**", RegexOptions.IgnoreCase);

        // Convert <em> and <i> to *
        content = Regex.Replace(content, @"</?(?:em|i)(?:\s[^>]*)?>", "*", RegexOptions.IgnoreCase);

        // Convert <code> to backticks
        content = Regex.Replace(content, @"</?code(?:\s[^>]*)?>", "`", RegexOptions.IgnoreCase);

        // Convert <del> or <s> to ~~
        content = Regex.Replace(content, @"</?(?:del|s)(?:\s[^>]*)?>", "~~", RegexOptions.IgnoreCase);

        // Remove any remaining tags (like <br>, <span>, etc.)
        content = Regex.Replace(content, @"<[^>]+>", "");

        return content;
    }
}
