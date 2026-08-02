using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    private FindAndReplaceController? _findAndReplaceController;

    internal FindAndReplaceController FindAndReplace =>
        _findAndReplaceController ??= new FindAndReplaceController(this);

    internal void OpenFind(bool showReplace)
    {
        string? initialText = null;
        if (_doc.HasSelection)
        {
            var (sb, so, eb, eo) = _doc.GetOrderedSelection();
            if (sb == eb)
                initialText = _doc.GetBlockText(sb).Substring(so, eo - so);
        }
        FindBar?.Open(showReplace, initialText);
        FindBar?.ApplyTheme(_palette.Background, _palette.Foreground, _palette.Syntax, _palette.CodeBackground);
    }

    internal void CloseFind()
    {
        FindAndReplace.ClearMatches();
        FindBar?.Close();
        InvalidateVisual();
    }

    internal void ExecuteSearch(string query, bool caseSensitive) =>
        FindAndReplace.ExecuteSearch(query, caseSensitive);

    internal void NavigateMatch(int direction) =>
        FindAndReplace.NavigateMatch(direction);

    internal void ReplaceCurrent(string replacement) =>
        FindAndReplace.ReplaceCurrent(replacement);

    internal void ReplaceAll(string replacement) =>
        FindAndReplace.ReplaceAll(replacement);

    private void InvalidateSearchOnContentChange() =>
        FindAndReplace.InvalidateSearchOnContentChange();

    private void DrawSearchHighlights(DrawingContext dc, double effectiveScroll) =>
        FindAndReplace.DrawSearchHighlights(dc, effectiveScroll);

    // Test hooks
    internal int TestSearchMatchCount => FindAndReplace.TestSearchMatchCount;
    internal int TestCurrentMatchIndex => FindAndReplace.TestCurrentMatchIndex;
    internal void TestExecuteSearch(string query, bool caseSensitive) => FindAndReplace.TestExecuteSearch(query, caseSensitive);
}
