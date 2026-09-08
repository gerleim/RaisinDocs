namespace RaisinDocs;

/// <summary>
/// Tag scanning over raw clipboard HTML, tolerant of unquoted attributes and multi-line tags.
/// </summary>
/// <remarks>
/// The point of <see cref="FindMatchingClose"/> is that clipboard HTML nests: the first
/// <c>&lt;/ul&gt;</c> after a <c>&lt;ul&gt;</c> may close an inner list, not the one you opened.
/// Scanning with IndexOf silently truncates the outer element at the inner element's close, so
/// both the table parser and the list parser count depth instead.
/// </remarks>
internal static class HtmlTagScanner
{
    internal static bool IsOpenTag(string html, int pos, string name)
    {
        if (pos >= html.Length || html[pos] != '<') return false;
        int after = pos + 1;
        if (after + name.Length > html.Length) return false;
        if (!html.AsSpan(after, name.Length).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        int next = after + name.Length;
        return next >= html.Length || html[next] == '>' || html[next] == '/' || char.IsWhiteSpace(html[next]);
    }

    internal static bool IsCloseTag(string html, int pos, string name)
    {
        if (pos + 1 >= html.Length || html[pos] != '<' || html[pos + 1] != '/') return false;
        int after = pos + 2;
        if (after + name.Length > html.Length) return false;
        if (!html.AsSpan(after, name.Length).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        int next = after + name.Length;
        return next >= html.Length || html[next] == '>' || char.IsWhiteSpace(html[next]);
    }

    internal static int IndexOfOpenTag(string html, int start, string name)
    {
        for (int i = start; i < html.Length; i++)
        {
            if (html[i] == '<' && IsOpenTag(html, i, name)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the closing tag for an element already opened, honoring nesting.
    /// Returns the index of the '&lt;' of the closing tag, or -1 when unclosed.
    /// </summary>
    internal static int FindMatchingClose(string html, int start, string name)
    {
        int depth = 1;
        for (int i = start; i < html.Length; i++)
        {
            if (html[i] != '<') continue;

            if (IsCloseTag(html, i, name))
            {
                if (--depth == 0) return i;
            }
            else if (IsOpenTag(html, i, name))
            {
                // A self-closed or void occurrence never nests.
                int close = html.IndexOf('>', i);
                if (close > i && html[close - 1] != '/') depth++;
            }
        }
        return -1;
    }

    /// <summary>The index just past the '&gt;' of the tag starting at <paramref name="tagStart"/>.</summary>
    internal static int EndOfTag(string html, int tagStart)
    {
        int close = html.IndexOf('>', tagStart);
        return close < 0 ? html.Length : close + 1;
    }
}
