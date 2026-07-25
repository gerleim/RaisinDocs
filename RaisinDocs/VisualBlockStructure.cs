using System.Text;

namespace RaisinDocs;

public record class VisualBlock
{
    public required BlockKind Kind { get; init; }
    public required StringBuilder MergedText { get; init; }
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public bool IsFenceDelimiter { get; init; }
    public bool IsTableSeparator { get; init; }
    public bool IsSkippedInVisual { get; init; }
    public IReadOnlyList<InlineImage>? Images { get; init; }
    public IReadOnlyList<InlineLink>? Links { get; init; }
    public IReadOnlyList<EmphasisMarker>? EmphasisMarkers { get; init; }
    public IReadOnlyList<ColorSpan>? ColorSpans { get; init; }
    public BlockColor? BlockColor { get; init; }
    public BlockColor? DivOpenColor { get; init; }
    public bool HasDivClose { get; init; }
    public TableRowInfo? TableRow { get; init; }
    public TableInfo? Table { get; init; }
    public int LeadingSpaces { get; init; }
    public int ContentColumn { get; init; }
    public int ListNestingLevel { get; init; }
    public string? CodeLanguage { get; init; }
    public IReadOnlyList<SyntaxToken>? SyntaxTokens { get; init; }
    public IReadOnlyList<SpellingError>? SpellingErrors { get; init; }
    public bool CreateVisualSeparation { get; init; }

    public required IReadOnlyList<int> SourceBlockIndices { get; init; }
}

public class VisualBlockStructure
{
    public required IReadOnlyList<VisualBlock> Blocks { get; init; }

    public static VisualBlockStructure Build(List<ParsedBlock> parsedBlocks, Func<int, string> getBlockText)
    {
        var visualBlocks = new List<VisualBlock>();
        var processed = new HashSet<int>();

        for (int i = 0; i < parsedBlocks.Count; i++)
        {
            if (processed.Contains(i))
                continue;

            var parsed = parsedBlocks[i];

            if (parsed.Kind == BlockKind.Paragraph && parsed.Children?.Count > 0)
            {
                var (merged, childIndices) = MergeParagraph(parsed, parsedBlocks, getBlockText, i);
                visualBlocks.Add(merged);

                foreach (var childIdx in childIndices)
                    processed.Add(childIdx);
            }
            else
            {
                var visual = new VisualBlock
                {
                    Kind = parsed.Kind,
                    MergedText = new StringBuilder(getBlockText(i)),
                    Runs = parsed.Runs,
                    IsFenceDelimiter = parsed.IsFenceDelimiter,
                    IsTableSeparator = parsed.IsTableSeparator,
                    IsSkippedInVisual = parsed.IsSkippedInVisual,
                    Images = parsed.Images,
                    Links = parsed.Links,
                    EmphasisMarkers = parsed.EmphasisMarkers,
                    ColorSpans = parsed.ColorSpans,
                    BlockColor = parsed.BlockColor,
                    DivOpenColor = parsed.DivOpenColor,
                    HasDivClose = parsed.HasDivClose,
                    TableRow = parsed.TableRow,
                    Table = parsed.Table,
                    LeadingSpaces = parsed.LeadingSpaces,
                    ContentColumn = parsed.ContentColumn,
                    ListNestingLevel = parsed.ListNestingLevel,
                    CodeLanguage = parsed.CodeLanguage,
                    SyntaxTokens = parsed.SyntaxTokens,
                    SpellingErrors = parsed.SpellingErrors,
                    CreateVisualSeparation = parsed.CreateVisualSeparation,
                    SourceBlockIndices = new[] { i }
                };
                visualBlocks.Add(visual);
            }
        }

        return new VisualBlockStructure { Blocks = visualBlocks };
    }

    private static (VisualBlock block, List<int> childIndices) MergeParagraph(
        ParsedBlock paragraph, List<ParsedBlock> allParsedBlocks, Func<int, string> getBlockText, int parentIdx)
    {
        var childIndices = new List<int> { parentIdx };
        var allBlockIndices = new List<int> { parentIdx };
        var allChildren = new List<ParsedBlock> { paragraph };

        for (int i = parentIdx + 1; i < allParsedBlocks.Count && childIndices.Count < paragraph.Children!.Count + 1; i++)
        {
            if (paragraph.Children!.Contains(allParsedBlocks[i]))
            {
                childIndices.Add(i);
                allBlockIndices.Add(i);
                allChildren.Add(allParsedBlocks[i]);
            }
        }

        var merged = new StringBuilder();
        var allRuns = new List<StyledRun>();
        var allImages = new List<InlineImage>();
        var allLinks = new List<InlineLink>();
        var allColorSpans = new List<ColorSpan>();
        var allEmphasisMarkers = new List<EmphasisMarker>();
        var allSyntaxTokens = new List<SyntaxToken>();
        var allSpellingErrors = new List<SpellingError>();

        int offset = 0;

        for (int i = 0; i < allChildren.Count; i++)
        {
            var child = allChildren[i];

            if (i > 0)
            {
                merged.Append('\n');
                offset++;
            }

            string blockText = getBlockText(allBlockIndices[i]);
            merged.Append(blockText);

            if (child.Runs != null)
            {
                foreach (var run in child.Runs)
                {
                    allRuns.Add(run with { Start = run.Start + offset });
                }
            }
            if (child.Images != null)
                allImages.AddRange(child.Images);
            if (child.Links != null)
                allLinks.AddRange(child.Links);
            if (child.ColorSpans != null)
            {
                foreach (var span in child.ColorSpans)
                {
                    allColorSpans.Add(span with { Start = span.Start + offset });
                }
            }
            if (child.EmphasisMarkers != null)
            {
                foreach (var marker in child.EmphasisMarkers)
                {
                    allEmphasisMarkers.Add(marker with { Start = marker.Start + offset });
                }
            }
            if (child.SyntaxTokens != null)
            {
                foreach (var token in child.SyntaxTokens)
                {
                    allSyntaxTokens.Add(token with { Start = token.Start + offset });
                }
            }
            if (child.SpellingErrors != null)
            {
                foreach (var error in child.SpellingErrors)
                {
                    allSpellingErrors.Add(error with { StartOffset = error.StartOffset + offset });
                }
            }

            offset += blockText.Length;
        }

        var block = new VisualBlock
        {
            Kind = BlockKind.Paragraph,
            MergedText = merged,
            Runs = allRuns,
            IsFenceDelimiter = false,
            IsTableSeparator = false,
            IsSkippedInVisual = false,
            Images = allImages.Count > 0 ? allImages : null,
            Links = allLinks.Count > 0 ? allLinks : null,
            ColorSpans = allColorSpans.Count > 0 ? allColorSpans : null,
            EmphasisMarkers = allEmphasisMarkers.Count > 0 ? allEmphasisMarkers : null,
            SyntaxTokens = allSyntaxTokens.Count > 0 ? allSyntaxTokens : null,
            SpellingErrors = allSpellingErrors.Count > 0 ? allSpellingErrors : null,
            BlockColor = paragraph.BlockColor,
            DivOpenColor = paragraph.DivOpenColor,
            HasDivClose = paragraph.HasDivClose,
            TableRow = paragraph.TableRow,
            Table = paragraph.Table,
            LeadingSpaces = paragraph.LeadingSpaces,
            ContentColumn = paragraph.ContentColumn,
            ListNestingLevel = paragraph.ListNestingLevel,
            CodeLanguage = paragraph.CodeLanguage,
            CreateVisualSeparation = paragraph.CreateVisualSeparation,
            SourceBlockIndices = allBlockIndices
        };

        return (block, childIndices);
    }
}
