namespace RaisinDocs;

/// <summary>Structured table content parsed from clipboard HTML.</summary>
internal sealed class TableBlockData
{
    public List<TableRowContent> Rows { get; } = new();

    /// <summary>Per-column alignment, inferred from the cells' align attributes/styles.</summary>
    public List<ColumnAlignment> Alignments { get; set; } = new();

    /// <summary>Widest row, after colspan expansion.</summary>
    public int ColumnCount
    {
        get
        {
            int max = 0;
            foreach (var row in Rows)
                max = Math.Max(max, row.Cells.Count);
            return max;
        }
    }
}

internal sealed class TableRowContent
{
    public List<TableCellContent> Cells { get; } = new();

    /// <summary>True for a &lt;th&gt; row, or the first row when the source has no &lt;th&gt;.</summary>
    public bool IsHeader { get; set; }
}

internal sealed class TableCellContent
{
    public List<InlineContent> Content { get; set; } = new();

    /// <summary>Alignment declared on this cell, if any. Null means "not stated".</summary>
    public ColumnAlignment? Align { get; set; }

    /// <summary>True for a cell synthesized to fill a colspan or a short row.</summary>
    public bool IsFiller { get; set; }
}
