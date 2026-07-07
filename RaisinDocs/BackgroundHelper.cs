namespace RaisinDocs;

internal static class BackgroundHelper
{
    internal static bool SelectionHasBackground(Document doc, List<ParsedBlock> parsedBlocks)
    {
        if (!doc.HasSelection) return false;
        var (sb, so, eb, eo) = doc.GetOrderedSelection();

        for (int b = sb; b <= eb; b++)
        {
            if (b >= parsedBlocks.Count) break;
            var parsed = parsedBlocks[b];

            if (parsed.BlockColor?.Background != null) return true;

            if (parsed.ColorSpans != null)
            {
                int bStart = (b == sb) ? so : 0;
                int bEnd = (b == eb) ? eo : doc.GetBlockLength(b);
                foreach (var cs in parsed.ColorSpans)
                {
                    if (cs.Background == null) continue;
                    int csEnd = cs.Start + cs.Length;
                    if (csEnd > bStart && cs.Start < bEnd) return true;
                }
            }
        }

        return false;
    }

    internal static bool CursorHasBackground(Document doc, List<ParsedBlock> parsedBlocks)
    {
        int block = doc.CursorBlock;
        if (block >= parsedBlocks.Count) return false;
        var parsed = parsedBlocks[block];

        if (parsed.BlockColor?.Background != null) return true;

        if (parsed.ColorSpans != null)
        {
            int offset = doc.CursorOffset;
            foreach (var cs in parsed.ColorSpans)
            {
                if (cs.Background == null) continue;
                if (offset >= cs.Start && offset <= cs.Start + cs.Length) return true;
            }
        }
        return false;
    }

    internal static void RemoveBackgroundAtCursor(Document doc, List<ParsedBlock>? parsedBlocks)
    {
        int block = doc.CursorBlock;
        int offset = doc.CursorOffset;
        string text = doc.GetBlockText(block);

        var bgOpeners = new Stack<(int tagStart, int tagEnd, string body)>();
        (int opStart, int opEnd, string opBody, int clStart, int clEnd)? target = null;

        int pos = 0;
        while (MarkdownParser.FindNextColorTag(text, ref pos,
                   out int tagStart, out int tagEnd, out bool isOpener,
                   out int bodyStart, out int bodyEnd))
        {
            var body = text[bodyStart..bodyEnd].Trim();

            if (isOpener)
            {
                bool hasBg = body.StartsWith("bg:", StringComparison.OrdinalIgnoreCase)
                    || (body.IndexOf(" bg:", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasBg)
                    bgOpeners.Push((tagStart, tagEnd, body));
            }
            else
            {
                bool closesBg = body.Equals("bg", StringComparison.OrdinalIgnoreCase)
                    || body.Length == 0;
                if (closesBg && bgOpeners.Count > 0)
                {
                    var opener = bgOpeners.Pop();
                    if (offset >= opener.tagEnd && offset <= tagStart)
                    {
                        target = (opener.tagStart, opener.tagEnd, opener.body, tagStart, tagEnd);
                        break;
                    }
                }
            }
        }

        if (target == null)
        {
            if (bgOpeners.Count > 0)
            {
                var opener = bgOpeners.Pop();
                if (offset >= opener.tagEnd)
                    target = (opener.tagStart, opener.tagEnd, opener.body, -1, -1);
            }
        }

        if (target != null)
        {
            var t = target.Value;

            if (t.clStart >= 0)
            {
                string closerBody = text[t.clStart..t.clEnd];
                bool bgOnlyCloser = closerBody.Equals("<!--/@bg-->", StringComparison.OrdinalIgnoreCase)
                    || (t.opBody.StartsWith("bg:", StringComparison.OrdinalIgnoreCase)
                        && t.opBody.IndexOf(' ') < 0);
                if (bgOnlyCloser)
                    doc.RemoveTextAt(block, t.clStart, t.clEnd - t.clStart);
            }

            RemoveBgFromOpenerTag(doc, block, t.opStart, t.opEnd, t.opBody);
        }
        else if (parsedBlocks != null && block < parsedBlocks.Count
                 && parsedBlocks[block].BlockColor?.Background != null)
        {
            RemoveEnclosingDivBackground(doc, parsedBlocks, block);
        }
    }

    internal static void RemoveBackgroundFromSelection(Document doc, List<ParsedBlock>? parsedBlocks)
    {
        if (!doc.HasSelection) return;
        var (sb, so, eb, eo) = doc.GetOrderedSelection();

        var removedBlocks = new List<int>();
        var wrappedBlocks = new Dictionary<int, int>();

        if (parsedBlocks != null)
        {
            var seenDivOpens = new HashSet<int>();
            var divs = new List<(int divOpen, int divClose, string bgColor, string? strippedDiv)>();

            for (int b = sb; b <= eb; b++)
            {
                if (b >= parsedBlocks.Count) break;
                if (parsedBlocks[b].BlockColor?.Background == null) continue;

                int divOpen = FindEnclosingDivOpenWithBg(parsedBlocks, b);
                if (divOpen < 0 || !seenDivOpens.Add(divOpen)) continue;

                int divClose = FindMatchingDivClose(doc, parsedBlocks, divOpen);
                if (divClose < 0) continue;

                string divOpenText = doc.GetBlockText(divOpen);
                string? bgColor = ExtractBgColorFromDivOpen(divOpenText);
                if (bgColor == null) continue;

                string? strippedDiv = StripBgFromDivOpen(divOpenText);
                divs.Add((divOpen, divClose, bgColor, strippedDiv));
            }

            divs.Sort((a, b) => b.divOpen.CompareTo(a.divOpen));

            foreach (var (divOpen, divClose, bgColor, strippedDiv) in divs)
            {
                string bgOpener = $"<!--@bg:{bgColor}-->";
                int firstContent = divOpen + 1;
                int lastContent = divClose - 1;

                if (strippedDiv != null)
                {
                    int cur = AdjustIndex(divOpen, removedBlocks);
                    doc.RemoveTextAt(cur, 0, doc.GetBlockLength(cur));
                    doc.InsertTextAt(cur, 0, strippedDiv);
                }
                else
                {
                    doc.RemoveBlockAt(AdjustIndex(divClose, removedBlocks));
                    removedBlocks.Add(divClose);
                    doc.RemoveBlockAt(AdjustIndex(divOpen, removedBlocks));
                    removedBlocks.Add(divOpen);
                }

                for (int origB = lastContent; origB >= firstContent; origB--)
                {
                    int curB = AdjustIndex(origB, removedBlocks);
                    int len = doc.GetBlockLength(curB);
                    doc.InsertTextAt(curB, len, "<!--/@bg-->");
                    doc.InsertTextAt(curB, 0, bgOpener);
                    wrappedBlocks[origB] = bgOpener.Length;
                }
            }
        }

        for (int origB = eb; origB >= sb; origB--)
        {
            if (removedBlocks.Contains(origB)) continue;

            int curB = AdjustIndex(origB, removedBlocks);
            int bSelStart = (origB == sb) ? so : 0;
            int bSelEnd = (origB == eb) ? eo : doc.GetBlockLength(curB);

            if (wrappedBlocks.TryGetValue(origB, out int openerLen))
            {
                if (origB == sb) bSelStart += openerLen;
                if (origB == eb) bSelEnd += openerLen;
            }

            RemoveBgTagsFromBlock(doc, curB, bSelStart, bSelEnd);
        }

        doc.CollapseSelection();
    }

    private static int AdjustIndex(int originalIndex, List<int> removedBlocks)
    {
        int shift = 0;
        foreach (int r in removedBlocks)
        {
            if (r < originalIndex) shift++;
        }
        return originalIndex - shift;
    }

    private static string? ExtractBgColorFromDivOpen(string text)
    {
        const string divPrefix = "<!--@div ";
        const string commentClose = "-->";
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith(divPrefix.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(commentClose.AsSpan(), StringComparison.Ordinal))
            return null;

        var props = trimmed[divPrefix.Length..^commentClose.Length].Trim().ToString();
        return ExtractBgColor(props);
    }

    private static void RemoveEnclosingDivBackground(Document doc, List<ParsedBlock> parsedBlocks, int blockIndex)
    {
        int divOpen = FindEnclosingDivOpenWithBg(parsedBlocks, blockIndex);
        if (divOpen < 0) return;

        int divClose = FindMatchingDivClose(doc, parsedBlocks, divOpen);
        string openText = doc.GetBlockText(divOpen);
        string? stripped = StripBgFromDivOpen(openText);

        if (stripped != null)
        {
            doc.RemoveTextAt(divOpen, 0, doc.GetBlockLength(divOpen));
            doc.InsertTextAt(divOpen, 0, stripped);
        }
        else
        {
            if (divClose >= 0)
                doc.RemoveBlockAt(divClose);
            doc.RemoveBlockAt(divOpen);
        }
    }

    private static int FindEnclosingDivOpenWithBg(List<ParsedBlock> parsedBlocks, int blockIndex)
    {
        int depth = 0;
        for (int i = blockIndex - 1; i >= 0; i--)
        {
            if (i >= parsedBlocks.Count) continue;
            var p = parsedBlocks[i];
            if (p.Kind == BlockKind.ColorDivClose) depth++;
            else if (p.Kind == BlockKind.ColorDivOpen)
            {
                if (depth == 0)
                {
                    var divColor = p.DivOpenColor ?? p.BlockColor;
                    if (divColor?.Background != null) return i;
                    return -1;
                }
                depth--;
            }
        }
        return -1;
    }

    private static int FindMatchingDivClose(Document doc, List<ParsedBlock> parsedBlocks, int divOpenBlock)
    {
        int depth = 0;
        for (int i = divOpenBlock + 1; i < doc.BlockCount && i < parsedBlocks.Count; i++)
        {
            var p = parsedBlocks[i];
            if (p.Kind == BlockKind.ColorDivOpen) depth++;
            else if (p.Kind == BlockKind.ColorDivClose)
            {
                if (depth == 0) return i;
                depth--;
            }
        }
        return -1;
    }

    private static string? StripBgFromDivOpen(string text)
    {
        const string divOpen = "<!--@div ";
        const string commentClose = "-->";
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith(divOpen.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(commentClose.AsSpan(), StringComparison.Ordinal))
            return null;

        var props = trimmed[divOpen.Length..^commentClose.Length].Trim();
        var remaining = new List<string>();

        while (!props.IsEmpty)
        {
            while (!props.IsEmpty && props[0] == ' ') props = props[1..];
            if (props.IsEmpty) break;
            int space = props.IndexOf(' ');
            var token = space >= 0 ? props[..space] : props;
            props = space >= 0 ? props[(space + 1)..] : ReadOnlySpan<char>.Empty;

            if (!token.StartsWith("bg:".AsSpan(), StringComparison.OrdinalIgnoreCase))
                remaining.Add(token.ToString());
        }

        if (remaining.Count == 0) return null;
        return $"<!--@div {string.Join(' ', remaining)}-->";
    }

    private static void RemoveBgFromOpenerTag(Document doc, int block, int opStart, int opEnd, string opBody)
    {
        if (opBody.StartsWith("bg:", StringComparison.OrdinalIgnoreCase))
        {
            int space = opBody.IndexOf(' ');
            if (space < 0)
                doc.RemoveTextAt(block, opStart, opEnd - opStart);
            else
            {
                var rest = opBody[(space + 1)..].Trim();
                doc.RemoveTextAt(block, opStart, opEnd - opStart);
                doc.InsertTextAt(block, opStart, $"<!--@{rest}-->");
            }
        }
        else if (opBody.StartsWith("fg:", StringComparison.OrdinalIgnoreCase))
        {
            int space = opBody.IndexOf(' ');
            if (space >= 0)
            {
                var rest = opBody[(space + 1)..].Trim();
                if (rest.StartsWith("bg:", StringComparison.OrdinalIgnoreCase))
                {
                    var fgPart = opBody[..space];
                    doc.RemoveTextAt(block, opStart, opEnd - opStart);
                    doc.InsertTextAt(block, opStart, $"<!--@{fgPart}-->");
                }
            }
        }
    }

    private static void RemoveBgTagsFromBlock(Document doc, int blockIndex, int selStart, int selEnd)
    {
        string text = doc.GetBlockText(blockIndex);

        var bgSpans = new List<(int opStart, int opEnd, string opBody, int clStart, int clEnd)>();
        var openerStack = new Stack<(int start, int end, string body)>();

        int pos = 0;
        while (MarkdownParser.FindNextColorTag(text, ref pos,
                   out int tagStart, out int tagEnd, out bool isOpener,
                   out int bodyStart, out int bodyEnd))
        {
            var body = text[bodyStart..bodyEnd].Trim();

            if (isOpener)
            {
                bool hasBg = body.StartsWith("bg:", StringComparison.OrdinalIgnoreCase)
                    || body.IndexOf(" bg:", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasBg)
                    openerStack.Push((tagStart, tagEnd, body));
            }
            else
            {
                bool closesBg = body.Equals("bg", StringComparison.OrdinalIgnoreCase)
                    || body.Length == 0;
                if (closesBg && openerStack.Count > 0)
                {
                    var opener = openerStack.Pop();
                    bgSpans.Add((opener.start, opener.end, opener.body, tagStart, tagEnd));
                }
            }
        }
        while (openerStack.Count > 0)
        {
            var opener = openerStack.Pop();
            bgSpans.Add((opener.start, opener.end, opener.body, -1, -1));
        }

        var edits = new List<(int Start, int Length, string? Replacement)>();

        foreach (var span in bgSpans)
        {
            int contentStart = span.opEnd;
            int contentEnd = span.clStart >= 0 ? span.clStart : text.Length;

            int overlapStart = Math.Max(selStart, contentStart);
            int overlapEnd = Math.Min(selEnd, contentEnd);

            if (overlapStart >= overlapEnd) continue;

            bool fullCoverage = overlapStart <= contentStart && overlapEnd >= contentEnd;
            bool isBgOnly = span.opBody.StartsWith("bg:", StringComparison.OrdinalIgnoreCase)
                && span.opBody.IndexOf(' ') < 0;

            if (fullCoverage || span.clStart < 0)
            {
                AddOpenerStripEdits(edits, span);

                if (span.clStart >= 0)
                {
                    var closerText = text.AsSpan()[span.clStart..span.clEnd];
                    if (closerText.Equals("<!--/@bg-->".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        edits.Add((span.clStart, span.clEnd - span.clStart, null));
                }
            }
            else if (isBgOnly)
            {
                string openerTag = text[span.opStart..span.opEnd];
                const string bgCloser = "<!--/@bg-->";

                if (overlapStart > contentStart && overlapEnd < contentEnd)
                {
                    edits.Add((overlapEnd, 0, openerTag));
                    edits.Add((overlapStart, 0, bgCloser));
                }
                else if (overlapStart <= contentStart)
                {
                    edits.Add((overlapEnd, 0, openerTag));
                    edits.Add((span.opStart, span.opEnd - span.opStart, null));
                }
                else
                {
                    edits.Add((span.clStart, span.clEnd - span.clStart, null));
                    edits.Add((overlapStart, 0, bgCloser));
                }
            }
            else
            {
                string? bgColor = ExtractBgColor(span.opBody);
                if (bgColor == null) continue;

                string bgOpener = $"<!--@bg:{bgColor}-->";
                const string bgCloser = "<!--/@bg-->";

                if (overlapStart > contentStart && overlapEnd < contentEnd)
                {
                    edits.Add((overlapEnd, 0, bgOpener));
                    edits.Add((overlapStart, 0, bgCloser));
                }
                else if (overlapStart <= contentStart)
                {
                    AddOpenerStripEdits(edits, span);
                    edits.Add((overlapEnd, 0, bgOpener));
                }
                else
                {
                    edits.Add((overlapStart, 0, bgCloser));
                }
            }
        }

        edits.Sort((a, b) =>
        {
            int cmp = b.Start.CompareTo(a.Start);
            if (cmp != 0) return cmp;
            return b.Length.CompareTo(a.Length);
        });

        foreach (var (start, length, replacement) in edits)
        {
            if (length > 0)
                doc.RemoveTextAt(blockIndex, start, length);
            if (replacement != null)
                doc.InsertTextAt(blockIndex, start, replacement);
        }
    }

    private static void AddOpenerStripEdits(
        List<(int Start, int Length, string? Replacement)> edits,
        (int opStart, int opEnd, string opBody, int clStart, int clEnd) span)
    {
        if (span.opBody.StartsWith("bg:", StringComparison.OrdinalIgnoreCase))
        {
            int space = span.opBody.IndexOf(' ');
            if (space < 0)
                edits.Add((span.opStart, span.opEnd - span.opStart, null));
            else
            {
                var rest = span.opBody[(space + 1)..].Trim();
                edits.Add((span.opStart, span.opEnd - span.opStart, $"<!--@{rest}-->"));
            }
        }
        else if (span.opBody.StartsWith("fg:", StringComparison.OrdinalIgnoreCase))
        {
            int space = span.opBody.IndexOf(' ');
            if (space >= 0)
            {
                var rest = span.opBody[(space + 1)..].Trim();
                if (rest.StartsWith("bg:", StringComparison.OrdinalIgnoreCase))
                {
                    var fgPart = span.opBody[..space];
                    edits.Add((span.opStart, span.opEnd - span.opStart, $"<!--@{fgPart}-->"));
                }
            }
        }
    }

    private static string? ExtractBgColor(string opBody)
    {
        int bgIdx = opBody.IndexOf("bg:", StringComparison.OrdinalIgnoreCase);
        if (bgIdx < 0) return null;
        var afterBg = opBody.AsSpan()[(bgIdx + 3)..];
        int space = afterBg.IndexOf(' ');
        return space >= 0 ? afterBg[..space].ToString() : afterBg.ToString();
    }
}
