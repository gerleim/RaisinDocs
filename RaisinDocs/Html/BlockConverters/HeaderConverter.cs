using System.Text;

namespace RaisinDocs;

/// <summary>
/// Converts HTML heading elements (h1-h6) to RaisinDocs markdown heading syntax (# ## ### etc).
/// Preserves inline formatting (bold, italic, colors) within headings.
/// </summary>
internal static class HeaderConverter
{
    /// <summary>
    /// Converts an HTML heading tag (h1-h6) to markdown heading syntax.
    /// </summary>
    /// <returns>Markdown heading string (e.g., "# Title") or null if not a heading tag.</returns>
    internal static string? ConvertHeader(ReadOnlySpan<char> tagName, string content)
    {
        if (!IsHeaderTag(tagName, out int level))
            return null;

        var prefix = new string('#', level) + " ";
        return prefix + content.Trim();
    }

    private static bool IsHeaderTag(ReadOnlySpan<char> tagName, out int level)
    {
        level = 0;

        if (tagName.Length == 2 && tagName[0] == 'h')
        {
            if (char.IsDigit(tagName[1]))
            {
                level = tagName[1] - '0';
                return level >= 1 && level <= 6;
            }
        }

        return false;
    }
}
