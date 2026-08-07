namespace RaisinDocs;

/// <summary>
/// Tracks the type of the last editing action for undo grouping purposes.
/// </summary>
internal enum LastActionKind
{
    None,
    Typing,
    Deleting
}
