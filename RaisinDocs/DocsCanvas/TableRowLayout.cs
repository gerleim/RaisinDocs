namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Where an offset sits in a table row: its X relative to the left padding, the cell, and the
    /// line of that cell.
    /// </summary>
    internal readonly record struct TableCaretPos(double X, int Column, int SubLine);

    /// <summary>
    /// One horizontal piece of a highlighted range on a visual line: its X extent relative to the
    /// left padding, which of the line's lines of text it is on, and how tall one such line is.
    /// </summary>
    /// <remarks>
    /// A plain line gives a range one span covering the whole line's height. A table row gives one
    /// per line of each cell the range crosses, because its cells wrap independently and a single
    /// rectangle from the range's first X to its last would paint across the other cells.
    /// </remarks>
    /// <param name="Continues">The range goes on past this span, into a later line or cell.</param>
    internal readonly record struct LineSpan(double X1, double X2, int SubLine, int RowLineCount,
        double LineHeight, bool Continues)
    {
        /// <summary>
        /// The top and height of this span's band inside a line visual whose snapped height is
        /// <paramref name="bgH"/>. A one-line row fills it, as every highlight did; a wrapped
        /// row's bands snap to whole pixels and the last one runs to the bottom, so a range
        /// that crosses lines tiles without gaps or overlaps.
        /// </summary>
        public (double Top, double Height) Band(double y, double bgH)
        {
            if (RowLineCount <= 1) return (y, bgH);
            double top = Math.Round(SubLine * LineHeight);
            double bottom = SubLine == RowLineCount - 1 ? bgH : Math.Round((SubLine + 1) * LineHeight);
            return (y + top, bottom - top);
        }

        /// <summary>The bottom of this span's line of text, from the top of its visual line.</summary>
        public double Bottom => RowLineCount <= 1 ? LineHeight : (SubLine + 1) * LineHeight;
    }

    /// <summary>
    /// Where each cell of one table row wraps, as raw offsets into the row's block text.
    /// </summary>
    /// <remarks>
    /// A row stays one VisualLine however many lines its cells wrap to, because cells wrap
    /// independently: a VisualLine is one offset range, and "cell 0's second line plus cell 2's
    /// second line" is not one. The line is made taller through OverrideHeight instead, and this
    /// says which part of each cell sits on which of its lines. See design/Table Cell Wrapping.md.
    ///
    /// A plain class rather than a record, so VisualLine's generated equality compares it by
    /// reference - the same reason ParagraphGroup is one.
    /// </remarks>
    internal sealed class TableRowLayout
    {
        /// <summary>
        /// Per drawn cell, the raw offset each of its lines starts at. The first is the cell's
        /// trimmed start, so an empty cell still has one line.
        /// </summary>
        public required int[][] LineStarts { get; init; }

        /// <summary>Per drawn cell, its trimmed end - where its last line stops.</summary>
        public required int[] CellEnds { get; init; }

        /// <summary>The most lines any cell wraps to: the row's height in lines, at least one.</summary>
        public required int LineCount { get; init; }

        public int CellCount => LineStarts.Length;

        public int CellLineCount(int cell) => LineStarts[cell].Length;

        /// <summary>The raw range of line <paramref name="k"/> of a cell, its trailing space included.</summary>
        public (int Start, int End) GetLine(int cell, int k)
        {
            var starts = LineStarts[cell];
            return (starts[k], k + 1 < starts.Length ? starts[k + 1] : CellEnds[cell]);
        }

        /// <summary>
        /// The line of a cell a raw offset is on: the last one starting at or before it, and the
        /// first for an offset in the padding before the cell's text.
        /// </summary>
        public int LineOf(int cell, int raw)
        {
            var starts = LineStarts[cell];
            int k = 0;
            while (k + 1 < starts.Length && starts[k + 1] <= raw) k++;
            return k;
        }
    }
}
