using System.Text;

namespace RaisinDocs;

internal enum InlineConstructKind { Bold, Italic, Strikethrough, Code, Link, Color }

/// <summary>
/// A paired piece of inline markup: an opening and a closing marker around content, such as the
/// two <c>**</c> of bold text, the brackets and URL of a link, or a colour tag and its close.
/// <paramref name="Color"/> is set for <see cref="InlineConstructKind.Color"/>.
/// </summary>
internal readonly record struct InlineConstruct(
    HiddenRange Open, HiddenRange Close, InlineConstructKind Kind, ColorSpan? Color = null)
{
    public int ContentStart => Open.Start + Open.Length;
    public int ContentEnd => Close.Start;

    /// <summary>Whether this construct applies the same formatting as <paramref name="other"/>.</summary>
    public bool SameFormattingAs(InlineConstruct other) =>
        Kind == other.Kind
        && (Kind != InlineConstructKind.Color
            || (Color?.Foreground == other.Color?.Foreground && Color?.Background == other.Color?.Background));
}

/// <summary>
/// Reads a visual-mode selection as markdown.
/// </summary>
/// <remarks>
/// Visual mode hides markup, so the ends of a selection sit wherever the caret was allowed to
/// stop, on one side or the other of a hidden marker. The raw text between them can hold half a
/// pair ("b**", "a **") or lose markup the user saw applied: "who" selected from a bold "whole"
/// is bold on screen, and should paste as bold. Here the markers follow the selected content:
/// every construct with selected content brings both its markers, and no other construct brings
/// any. Source mode shows its markers, so there the raw range is exactly what was selected.
/// </remarks>
internal static class VisualSelection
{
    /// <summary>
    /// The paired constructs in a block, located the way <see cref="BlockVisualMap"/> hides them.
    /// </summary>
    internal static List<InlineConstruct> FindConstructs(ParsedBlock parsed, string text)
    {
        var constructs = new List<InlineConstruct>();
        if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine)
            return constructs;

        if (parsed.EmphasisMarkers is { } markers)
        {
            for (int i = 0; i + 1 < markers.Count; i += 2)
            {
                var open = markers[i];
                // The parser pairs one or two delimiters at a time: "~~" strikes, two of '*' or
                // '_' are bold, one is italic.
                var kind = text[open.Start] == '~' ? InlineConstructKind.Strikethrough
                    : open.Length == 2 ? InlineConstructKind.Bold
                    : InlineConstructKind.Italic;
                constructs.Add(new(new(open.Start, open.Length),
                    new(markers[i + 1].Start, markers[i + 1].Length), kind));
            }
        }

        foreach (var run in parsed.Runs)
        {
            if (run.Style != InlineStyle.Code) continue;
            int ticks = CountBackticks(text, run.Start);
            if (ticks == 0 || run.Length < 2 * ticks) continue;
            int runEnd = run.Start + run.Length;
            constructs.Add(new(new(run.Start, ticks), new(runEnd - ticks, ticks), InlineConstructKind.Code));
        }

        if (parsed.Links is { } links)
        {
            foreach (var link in links)
            {
                if (link.IsAngleBracket)
                {
                    constructs.Add(new(new(link.Start, 1), new(link.Start + link.Length - 1, 1), InlineConstructKind.Link));
                    continue;
                }
                if (link.Text == link.Url) continue;
                int closeBracket = link.Start + 1 + link.Text.Length;
                constructs.Add(new(new(link.Start, 1), new(closeBracket, link.Start + link.Length - closeBracket),
                    InlineConstructKind.Link));
            }
        }

        if (parsed.ColorSpans is { } spans && MarkdownParser.FindInlineColorTagRanges(text) is { } tags)
        {
            foreach (var span in spans)
            {
                int open = tags.FindIndex(t => t.Start + t.Length == span.Start);
                int close = tags.FindIndex(t => t.Start == span.Start + span.Length);
                if (open >= 0 && close >= 0)
                    constructs.Add(new(tags[open], tags[close], InlineConstructKind.Color, span));
            }
        }

        return constructs;
    }

    /// <summary>
    /// The markdown for a visual-mode selection, joined with CRLF as
    /// <see cref="Document.GetSelectedText"/> joins it. Table rows are taken raw: their hidden
    /// ranges are cell padding and pipes, not paired markup.
    /// </summary>
    internal static string BuildCopyText(Func<int, string> getBlockText, IReadOnlyList<ParsedBlock> parsed,
        IReadOnlyList<BlockVisualMap> maps, int startBlock, int startOffset, int endBlock, int endOffset)
    {
        var result = new StringBuilder();
        for (int i = startBlock; i <= endBlock; i++)
        {
            if (i > startBlock) result.Append("\r\n");

            string text = getBlockText(i);
            int from = i == startBlock ? Math.Min(startOffset, text.Length) : 0;
            int to = i == endBlock ? Math.Min(endOffset, text.Length) : text.Length;

            string part = i < parsed.Count && i < maps.Count && !IsTableRow(parsed[i])
                ? BlockSelectionText(parsed[i], maps[i], text, from, to)
                : text[from..to];
            result.Append(part.Replace("\n", "\r\n"));
        }
        return result.ToString();
    }

    /// <summary>The markdown for the raw range [<paramref name="from"/>, <paramref name="to"/>) of one block.</summary>
    internal static string BlockSelectionText(ParsedBlock parsed, BlockVisualMap map, string text, int from, int to)
    {
        if (from >= to) return "";

        // An image is hidden markup that draws as content, so it counts as something selected.
        bool IsContent(int i) => !map.IsHidden(i) || InImage(parsed, i);

        bool AnyContent(int start, int end)
        {
            for (int i = start; i < end; i++)
                if (IsContent(i)) return true;
            return false;
        }

        // A selection reaching the block's first or last visible character takes the hidden
        // markup at that edge too: a heading's "# ", a list marker, a closing "**".
        if (!AnyContent(0, from)) from = 0;
        if (!AnyContent(to, text.Length)) to = text.Length;
        if (from == 0 && to == text.Length) return text;

        var include = new bool[text.Length];
        for (int i = from; i < to; i++)
            include[i] = true;

        foreach (var construct in FindConstructs(parsed, text))
        {
            bool selected = AnyContent(Math.Max(construct.ContentStart, from), Math.Min(construct.ContentEnd, to));
            SetRange(include, construct.Open, selected);
            SetRange(include, construct.Close, selected);
        }

        var sb = new StringBuilder(to - from);
        for (int i = 0; i < text.Length; i++)
            if (include[i]) sb.Append(text[i]);
        return sb.ToString();
    }

    /// <summary>
    /// What deleting a visual-mode selection must do besides removing its raw range, which on its
    /// own would leave half-pairs behind.
    /// </summary>
    /// <param name="KeptMarkers">Markers inside the range that stay, in order: their construct
    /// still has content outside the selection. They go back where the range was.</param>
    /// <param name="CaretInKept">How many characters of <paramref name="KeptMarkers"/> belong
    /// before the caret: the caret goes where the first deleted character was.</param>
    /// <param name="RemoveFromHead">Markers before the range, in the start block, whose content
    /// was all deleted.</param>
    /// <param name="RemoveFromTail">Markers after the range, in the end block, whose content was
    /// all deleted.</param>
    internal readonly record struct DeletionPlan(
        string KeptMarkers, int CaretInKept,
        IReadOnlyList<HiddenRange> RemoveFromHead, IReadOnlyList<HiddenRange> RemoveFromTail);

    /// <summary>
    /// Plans the deletion of a visual-mode selection. A construct whose visible content is all
    /// selected goes with its markers, wherever they are; any other construct keeps its markers,
    /// even those inside the selection. With <paramref name="keepFormattingAtStart"/> - typing
    /// over a selection - the constructs around the first selected character count as kept, so
    /// what is typed takes that character's formatting.
    /// </summary>
    internal static DeletionPlan PlanDeletion(Func<int, string> getBlockText, IReadOnlyList<ParsedBlock> parsed,
        IReadOnlyList<BlockVisualMap> maps, int startBlock, int startOffset, int endBlock, int endOffset,
        bool keepFormattingAtStart = false)
    {
        var kept = new List<(int Block, int Start, string Text)>();
        var removeHead = new List<HiddenRange>();
        var removeTail = new List<HiddenRange>();
        (int Block, int Offset)? firstContent = null;

        void Process(int block, int from, int to)
        {
            if (block >= parsed.Count || block >= maps.Count || IsTableRow(parsed[block])) return;
            var p = parsed[block];
            var map = maps[block];
            string text = getBlockText(block);
            from = Math.Min(from, text.Length);
            to = Math.Min(to, text.Length);

            bool IsContent(int i) => !map.IsHidden(i) || InImage(p, i);

            if (firstContent == null)
            {
                for (int i = from; i < to; i++)
                    if (IsContent(i)) { firstContent = (block, i); break; }
            }

            foreach (var construct in FindConstructs(p, text))
            {
                int contentChars = 0, deletedChars = 0;
                for (int i = construct.ContentStart; i < construct.ContentEnd; i++)
                {
                    if (!IsContent(i)) continue;
                    contentChars++;
                    if (i >= from && i < to) deletedChars++;
                }

                bool holdsFirst = keepFormattingAtStart && firstContent is { } f && f.Block == block
                    && f.Offset >= construct.ContentStart && f.Offset < construct.ContentEnd;
                bool goes = contentChars > 0 && deletedChars == contentChars && !holdsFirst;

                foreach (var marker in new[] { construct.Open, construct.Close })
                {
                    int mStart = marker.Start, mEnd = marker.Start + marker.Length;
                    if (goes)
                    {
                        // The part inside the range goes with the range already.
                        if (block == startBlock && mStart < from)
                            removeHead.Add(new(mStart, Math.Min(mEnd, from) - mStart));
                        if (block == endBlock && mEnd > to)
                            removeTail.Add(new(Math.Max(mStart, to), mEnd - Math.Max(mStart, to)));
                    }
                    else
                    {
                        int keepStart = Math.Max(mStart, from), keepEnd = Math.Min(mEnd, to);
                        if (keepStart < keepEnd)
                            kept.Add((block, keepStart, text[keepStart..keepEnd]));
                    }
                }
            }
        }

        Process(startBlock, startOffset, startBlock == endBlock ? endOffset : int.MaxValue);
        if (endBlock != startBlock)
            Process(endBlock, 0, endOffset);

        kept.Sort((a, b) => a.Block != b.Block ? a.Block.CompareTo(b.Block) : a.Start.CompareTo(b.Start));

        var keptText = new StringBuilder();
        int caretInKept = 0;
        foreach (var (block, start, text) in kept)
        {
            bool beforeCaret = firstContent is not { } f
                || block < f.Block || (block == f.Block && start < f.Offset);
            if (beforeCaret) caretInKept += text.Length;
            keptText.Append(text);
        }

        return new DeletionPlan(keptText.ToString(), caretInKept, removeHead, removeTail);
    }

    /// <summary>
    /// Adapts single-line markdown pasted in visual mode to where it lands: markers for formatting
    /// already in force at the caret are dropped, because the text there has that formatting
    /// already. Pasted inside a bold run, "**who**" would otherwise close the bold it lands in and
    /// open another after it - "**whol**who**e**" shows "who" plain - where "**wholwhoe**" keeps
    /// it bold. Code spans and links are left alone: nothing pasted into them is formatting.
    /// </summary>
    internal static string AdaptPasteToCaret(string pasted, ParsedBlock context, string contextText, int caret)
    {
        if (pasted.Length == 0 || pasted.IndexOfAny(['\r', '\n']) >= 0) return pasted;

        var inForce = FindConstructs(context, contextText)
            .Where(c => c.Kind is not (InlineConstructKind.Code or InlineConstructKind.Link)
                        && caret >= c.ContentStart && caret <= c.ContentEnd)
            .ToList();
        if (inForce.Count == 0) return pasted;

        var parsedPaste = MarkdownParser.ParseInlineContent(pasted) with
        {
            ColorSpans = MarkdownParser.ParseInlineColorTags(pasted, null),
        };

        var drop = new bool[pasted.Length];
        foreach (var construct in FindConstructs(parsedPaste, pasted))
        {
            if (!inForce.Any(f => f.SameFormattingAs(construct))) continue;
            SetRange(drop, construct.Open, true);
            SetRange(drop, construct.Close, true);
        }

        var sb = new StringBuilder(pasted.Length);
        for (int i = 0; i < pasted.Length; i++)
            if (!drop[i]) sb.Append(pasted[i]);
        return sb.ToString();
    }

    private static bool InImage(ParsedBlock parsed, int offset)
    {
        if (parsed.Images == null) return false;
        foreach (var img in parsed.Images)
            if (offset >= img.Start && offset < img.Start + img.Length) return true;
        return false;
    }

    private static void SetRange(bool[] include, HiddenRange range, bool value)
    {
        int end = Math.Min(range.Start + range.Length, include.Length);
        for (int i = Math.Max(range.Start, 0); i < end; i++)
            include[i] = value;
    }

    private static bool IsTableRow(ParsedBlock parsed) =>
        parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow or BlockKind.TableSeparatorRow;

    private static int CountBackticks(string text, int start)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == '`') count++;
        return count;
    }
}
