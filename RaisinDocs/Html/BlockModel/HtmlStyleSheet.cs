using System.Text;

namespace RaisinDocs;

/// <summary>
/// Minimal CSS class-rule index for clipboard HTML.
///
/// Excel expresses all cell formatting through generated class names (.xl65, .xl66, …)
/// declared in a &lt;style&gt; block in the document head — which sits *outside* the
/// CF_HTML fragment markers. Resolving a cell's colors or boldness therefore means
/// reading the stylesheet from the whole payload before the fragment is extracted.
///
/// Only class selectors are indexed. Element rules (Excel emits a `td { color:black;
/// font-weight:400 }` baseline) are deliberately ignored: they describe defaults, and
/// honoring them would tag every pasted cell as explicitly black.
/// </summary>
internal sealed class HtmlStyleSheet
{
    private readonly Dictionary<string, string> _classRules;

    internal static readonly HtmlStyleSheet Empty = new(new Dictionary<string, string>());

    private HtmlStyleSheet(Dictionary<string, string> classRules) => _classRules = classRules;

    internal int Count => _classRules.Count;

    /// <summary>Declarations for a class name (without the leading dot), or null.</summary>
    internal string? GetDeclarations(string className)
        => _classRules.TryGetValue(className, out var decls) ? decls : null;

    /// <summary>
    /// Indexes every class rule found in the &lt;style&gt; blocks of <paramref name="html"/>.
    /// </summary>
    internal static HtmlStyleSheet Parse(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0)
            return Empty;

        var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int pos = 0;

        while (pos < html.Length)
        {
            int styleStart = html.IndexOf("<style", pos, StringComparison.OrdinalIgnoreCase);
            if (styleStart < 0) break;

            int openEnd = html.IndexOf('>', styleStart);
            if (openEnd < 0) break;

            int styleEnd = html.IndexOf("</style", openEnd, StringComparison.OrdinalIgnoreCase);
            if (styleEnd < 0) styleEnd = html.Length;

            ParseRuleBlock(html[(openEnd + 1)..styleEnd], rules);
            pos = styleEnd + 1;
        }

        return rules.Count == 0 ? Empty : new HtmlStyleSheet(rules);
    }

    private static void ParseRuleBlock(string css, Dictionary<string, string> rules)
    {
        int pos = 0;
        int selectorStart = 0;

        while (pos < css.Length)
        {
            char c = css[pos];

            if (c == '"' || c == '\'')
            {
                pos = SkipQuoted(css, pos);
                continue;
            }

            if (c != '{')
            {
                pos++;
                continue;
            }

            string selector = css[selectorStart..pos];
            int bodyStart = pos + 1;
            int bodyEnd = FindDeclarationsEnd(css, bodyStart);
            string declarations = css[bodyStart..bodyEnd];

            AddSelectorRules(selector, declarations, rules);

            pos = bodyEnd < css.Length ? bodyEnd + 1 : bodyEnd;
            selectorStart = pos;
        }
    }

    private static void AddSelectorRules(string selector, string declarations, Dictionary<string, string> rules)
    {
        foreach (var part in selector.Split(','))
        {
            // Strip the comment opener Excel puts immediately before the first selector,
            // the comment closer before </style>, and any surrounding whitespace.
            var name = part.Replace("<!--", " ").Replace("-->", " ").Trim();
            if (name.Length < 2 || name[0] != '.') continue;

            name = name[1..];
            if (!IsSimpleClassName(name)) continue;

            // A later rule for the same class wins, matching CSS cascade for equal specificity.
            rules[name] = declarations;
        }
    }

    private static bool IsSimpleClassName(string name)
    {
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }
        return true;
    }

    private static int FindDeclarationsEnd(string css, int start)
    {
        int pos = start;
        while (pos < css.Length)
        {
            char c = css[pos];
            if (c == '"' || c == '\'')
            {
                pos = SkipQuoted(css, pos);
                continue;
            }
            if (c == '}') return pos;
            pos++;
        }
        return css.Length;
    }

    private static int SkipQuoted(string css, int quotePos)
    {
        char quote = css[quotePos];
        int pos = quotePos + 1;
        while (pos < css.Length)
        {
            if (css[pos] == '\\') { pos += 2; continue; }
            if (css[pos] == quote) return pos + 1;
            pos++;
        }
        return css.Length;
    }

    /// <summary>
    /// Resolves the effective formatting for an element, combining the declarations of every
    /// class it carries with its own inline style attribute (inline wins).
    /// </summary>
    internal InlineFormat ResolveFormat(string tag)
    {
        var combined = new StringBuilder();

        foreach (var className in GetAttributeValue(tag, "class").Split(
                     ' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var decls = GetDeclarations(className);
            if (decls != null) combined.Append(decls).Append(';');
        }

        combined.Append(GetAttributeValue(tag, "style"));

        return ParseFormat(combined.ToString());
    }

    private static InlineFormat ParseFormat(string declarations)
    {
        var format = new InlineFormat();
        if (declarations.Length == 0) return format;

        foreach (var declaration in SplitDeclarations(declarations))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            string property = declaration[..colon].Trim();
            string value = declaration[(colon + 1)..].Trim();
            if (value.Length == 0) continue;

            // mso-* properties are Office-specific and carry no visual meaning here.
            if (property.StartsWith("mso-", StringComparison.OrdinalIgnoreCase)) continue;

            if (property.Equals("color", StringComparison.OrdinalIgnoreCase))
                format.ForegroundColor = HtmlParsingContext.ParseCssColor(value.AsSpan()) ?? format.ForegroundColor;
            else if (property.Equals("background-color", StringComparison.OrdinalIgnoreCase)
                     || property.Equals("background", StringComparison.OrdinalIgnoreCase))
                format.BackgroundColor = HtmlParsingContext.ParseCssColor(FirstToken(value).AsSpan()) ?? format.BackgroundColor;
            else if (property.Equals("font-weight", StringComparison.OrdinalIgnoreCase))
                format.Bold = IsBoldWeight(value);
            else if (property.Equals("font-style", StringComparison.OrdinalIgnoreCase))
                format.Italic = value.StartsWith("italic", StringComparison.OrdinalIgnoreCase)
                                || value.StartsWith("oblique", StringComparison.OrdinalIgnoreCase);
        }

        return format;
    }

    /// <summary>Splits on semicolons that are not inside a quoted value.</summary>
    private static List<string> SplitDeclarations(string declarations)
    {
        var parts = new List<string>();
        int start = 0, pos = 0;

        while (pos < declarations.Length)
        {
            char c = declarations[pos];
            if (c == '"' || c == '\'')
            {
                pos = SkipQuoted(declarations, pos);
                continue;
            }
            if (c == ';')
            {
                parts.Add(declarations[start..pos]);
                start = pos + 1;
            }
            pos++;
        }

        if (start < declarations.Length)
            parts.Add(declarations[start..]);

        return parts;
    }

    private static string FirstToken(string value)
    {
        int space = value.IndexOf(' ');
        return space < 0 ? value : value[..space];
    }

    private static bool IsBoldWeight(string value)
    {
        if (value.StartsWith("bold", StringComparison.OrdinalIgnoreCase)) return true;
        // Excel writes numeric weights: 400 for normal, 700 for bold.
        return int.TryParse(value, out int weight) && weight >= 600;
    }

    /// <summary>
    /// Reads an attribute value from a tag. Handles double-quoted, single-quoted and
    /// unquoted values — Excel emits all three (class=xl65, style='…', width=64).
    /// </summary>
    internal static string GetAttributeValue(string tag, string attribute)
    {
        int pos = 0;
        while (pos < tag.Length)
        {
            int idx = tag.IndexOf(attribute, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";

            // Must be preceded by whitespace so `class` doesn't match inside another name.
            bool boundedLeft = idx > 0 && (char.IsWhiteSpace(tag[idx - 1]) || tag[idx - 1] == '<');
            int after = idx + attribute.Length;
            while (after < tag.Length && char.IsWhiteSpace(tag[after])) after++;
            bool boundedRight = after < tag.Length && tag[after] == '=';

            if (!boundedLeft || !boundedRight)
            {
                pos = idx + attribute.Length;
                continue;
            }

            int valueStart = after + 1;
            while (valueStart < tag.Length && char.IsWhiteSpace(tag[valueStart])) valueStart++;
            if (valueStart >= tag.Length) return "";

            char quote = tag[valueStart];
            if (quote == '"' || quote == '\'')
            {
                int end = tag.IndexOf(quote, valueStart + 1);
                return end < 0 ? "" : tag[(valueStart + 1)..end];
            }

            int unquotedEnd = valueStart;
            while (unquotedEnd < tag.Length
                   && !char.IsWhiteSpace(tag[unquotedEnd])
                   && tag[unquotedEnd] != '>'
                   && tag[unquotedEnd] != '/')
                unquotedEnd++;

            return tag[valueStart..unquotedEnd];
        }

        return "";
    }
}
