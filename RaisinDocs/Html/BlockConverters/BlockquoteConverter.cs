namespace RaisinDocs;

/// <summary>
/// Converts HTML blockquote elements to RaisinDocs markdown blockquote syntax (> ).
/// Handles multi-line quotes and preserves inline formatting within them.
/// </summary>
internal static class BlockquoteConverter
{
    /// <summary>
    /// Converts blockquote content to markdown blockquote syntax.
    /// Each line is prefixed with "> ".
    /// </summary>
    internal static string ConvertBlockquote(string content)
    {
        // Handle multi-line quotes by prefixing each line with "> "
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var quotedLines = new System.Collections.Generic.List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                quotedLines.Add("> " + trimmed);
            }
        }

        return quotedLines.Count > 0
            ? string.Join("\n", quotedLines)
            : "> ";
    }
}
