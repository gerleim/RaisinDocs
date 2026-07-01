using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

public class DocumentTests
{
    private static Document CreateDoc(params string[] blocks)
    {
        var doc = new Document();
        if (blocks.Length == 0) return doc;

        // Type first block
        foreach (char c in blocks[0])
            doc.Insert(c);

        for (int i = 1; i < blocks.Length; i++)
        {
            doc.InsertParagraphBreak();
            foreach (char c in blocks[i])
                doc.Insert(c);
        }

        doc.CollapseSelection();
        return doc;
    }

    // --- Empty document ---

    [Fact]
    public void NewDocument_HasOneEmptyBlock()
    {
        var doc = new Document();
        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().BeEmpty();
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(0);
    }

    // --- Insert ---

    [Fact]
    public void Insert_AppendsCharAndAdvancesCursor()
    {
        var doc = new Document();
        doc.Insert('a');
        doc.Insert('b');
        doc.Insert('c');

        doc.GetBlockText(0).Should().Be("abc");
        doc.CursorOffset.Should().Be(3);
    }

    [Fact]
    public void Insert_AtMiddle_InsertsAtCursorPosition()
    {
        var doc = CreateDoc("ac");
        doc.CursorOffset = 1;
        doc.Insert('b');

        doc.GetBlockText(0).Should().Be("abc");
        doc.CursorOffset.Should().Be(2);
    }

    // --- Paragraph break (Enter) ---

    [Fact]
    public void InsertParagraphBreak_SplitsBlock()
    {
        var doc = CreateDoc("helloworld");
        doc.CursorOffset = 5;
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().Be("world");
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(0);
    }

    [Fact]
    public void InsertParagraphBreak_AtStart_CreatesEmptyBlockBefore()
    {
        var doc = CreateDoc("hello");
        doc.CursorOffset = 0;
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().BeEmpty();
        doc.GetBlockText(1).Should().Be("hello");
    }

    [Fact]
    public void InsertParagraphBreak_AtEnd_CreatesEmptyBlockAfter()
    {
        var doc = CreateDoc("hello");
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().BeEmpty();
    }

    // --- Hard break (Shift+Enter) ---

    [Fact]
    public void HardBreak_Backslash_AppendsMarkerAndSplits()
    {
        var doc = CreateDoc("hello world");
        doc.CursorOffset = 5;
        doc.Paste("\\");
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello\\");
        doc.GetBlockText(1).Should().Be(" world");
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(0);
    }

    [Fact]
    public void HardBreak_TrailingSpaces_AppendsMarkerAndSplits()
    {
        var doc = CreateDoc("hello world");
        doc.CursorOffset = 5;
        doc.Paste("  ");
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello  ");
        doc.GetBlockText(1).Should().Be(" world");
    }

    [Fact]
    public void NewParagraph_TwoBreaksCreatesBlankLine()
    {
        var doc = CreateDoc("hello");
        doc.InsertParagraphBreak();
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().BeEmpty();
        doc.GetBlockText(2).Should().BeEmpty();
        doc.CursorBlock.Should().Be(2);
    }

    // --- Backspace ---

    [Fact]
    public void Backspace_DeletesCharBeforeCursor()
    {
        var doc = CreateDoc("abc");
        doc.Backspace();

        doc.GetBlockText(0).Should().Be("ab");
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void Backspace_AtBlockStart_MergesWithPrevious()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset = 0;
        doc.Backspace();

        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("helloworld");
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void Backspace_AtDocumentStart_DoesNothing()
    {
        var doc = new Document();
        doc.Backspace();

        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().BeEmpty();
    }

    // --- Delete ---

    [Fact]
    public void Delete_RemovesCharAfterCursor()
    {
        var doc = CreateDoc("abc");
        doc.CursorOffset = 1;
        doc.Delete();

        doc.GetBlockText(0).Should().Be("ac");
        doc.CursorOffset.Should().Be(1);
    }

    [Fact]
    public void Delete_AtBlockEnd_MergesWithNext()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.Delete();

        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("helloworld");
    }

    [Fact]
    public void Delete_AtDocumentEnd_DoesNothing()
    {
        var doc = CreateDoc("abc");
        doc.Delete();

        doc.GetBlockText(0).Should().Be("abc");
    }

    // --- MoveLeft / MoveRight ---

    [Fact]
    public void MoveLeft_DecrementsOffset()
    {
        var doc = CreateDoc("abc");
        doc.MoveLeft();
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void MoveLeft_AtBlockStart_MovesToPreviousBlockEnd()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorOffset = 0;
        doc.MoveLeft();

        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void MoveLeft_AtDocumentStart_StaysAtStart()
    {
        var doc = new Document();
        doc.MoveLeft();

        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(0);
    }

    [Fact]
    public void MoveRight_IncrementsOffset()
    {
        var doc = CreateDoc("abc");
        doc.CursorOffset = 0;
        doc.MoveRight();
        doc.CursorOffset.Should().Be(1);
    }

    [Fact]
    public void MoveRight_AtBlockEnd_MovesToNextBlockStart()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.MoveRight();

        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(0);
    }

    [Fact]
    public void MoveRight_AtDocumentEnd_StaysAtEnd()
    {
        var doc = CreateDoc("abc");
        doc.MoveRight();

        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(3);
    }

    // --- Selection ---

    [Fact]
    public void HasSelection_FalseWhenAnchorEqualsCursor()
    {
        var doc = CreateDoc("abc");
        doc.CollapseSelection();
        doc.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void HasSelection_TrueWhenDifferent()
    {
        var doc = CreateDoc("abc");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 0;
        doc.HasSelection.Should().BeTrue();
    }

    [Fact]
    public void GetOrderedSelection_ReturnsSmallestFirst()
    {
        var doc = CreateDoc("abc");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 2;
        doc.CursorBlock = 0;
        doc.CursorOffset = 0;

        var (sb, so, eb, eo) = doc.GetOrderedSelection();
        sb.Should().Be(0);
        so.Should().Be(0);
        eb.Should().Be(0);
        eo.Should().Be(2);
    }

    [Fact]
    public void GetSelectedText_SameBlock()
    {
        var doc = CreateDoc("hello world");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 0;
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;

        doc.GetSelectedText().Should().Be("hello");
    }

    [Fact]
    public void GetSelectedText_CrossBlock()
    {
        var doc = CreateDoc("hello", "world");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 3;
        doc.CursorBlock = 1;
        doc.CursorOffset = 2;

        doc.GetSelectedText().Should().Be("lo\r\nwo");
    }

    [Fact]
    public void DeleteSelection_SameBlock()
    {
        var doc = CreateDoc("hello world");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 5;
        doc.CursorBlock = 0;
        doc.CursorOffset = 11;

        doc.DeleteSelection();

        doc.GetBlockText(0).Should().Be("hello");
        doc.CursorOffset.Should().Be(5);
        doc.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void DeleteSelection_CrossBlock()
    {
        var doc = CreateDoc("hello", "beautiful", "world");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 2;
        doc.CursorBlock = 2;
        doc.CursorOffset = 3;

        doc.DeleteSelection();

        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("held");
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void DeleteSelection_WhenNoSelection_DoesNothing()
    {
        var doc = CreateDoc("abc");
        doc.CollapseSelection();
        doc.DeleteSelection();

        doc.GetBlockText(0).Should().Be("abc");
    }

    // --- SelectAll ---

    [Fact]
    public void SelectAll_SelectsEntireDocument()
    {
        var doc = CreateDoc("hello", "world");
        doc.SelectAll();

        doc.AnchorBlock.Should().Be(0);
        doc.AnchorOffset.Should().Be(0);
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(5);
    }

    // --- Paste ---

    [Fact]
    public void Paste_SingleLine()
    {
        var doc = CreateDoc("ac");
        doc.CursorOffset = 1;
        doc.Paste("b");

        doc.GetBlockText(0).Should().Be("abc");
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void Paste_MultiLine_CreatesParagraphs()
    {
        var doc = CreateDoc("ac");
        doc.CursorOffset = 1;
        doc.Paste("1\r\n2\r\n3");

        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("a1");
        doc.GetBlockText(1).Should().Be("2");
        doc.GetBlockText(2).Should().Be("3c");
        doc.CursorBlock.Should().Be(2);
        doc.CursorOffset.Should().Be(1);
    }

    [Fact]
    public void Paste_IntoEmpty()
    {
        var doc = new Document();
        doc.Paste("hello\r\nworld");

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().Be("world");
    }

    // --- Reflow ---

    private static bool IsParagraph(string text) => true;

    [Fact]
    public void Reflow_MergesConsecutiveParagraphs()
    {
        var doc = new Document();
        doc.SetText("a\nb\nc");
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("a b c");
    }

    [Fact]
    public void Reflow_PreservesBlankLines()
    {
        var doc = new Document();
        doc.SetText("a\nb\n\nc\nd");
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("a b");
        doc.GetBlockText(1).Should().BeEmpty();
        doc.GetBlockText(2).Should().Be("c d");
    }

    [Fact]
    public void Reflow_SkipsNonMergeableBlocks()
    {
        var doc = new Document();
        doc.SetText("a\nb\n# heading\nc\nd");
        doc.Reflow(0, doc.BlockCount - 1, text => !text.StartsWith("# "));
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("a b");
        doc.GetBlockText(1).Should().Be("# heading");
        doc.GetBlockText(2).Should().Be("c d");
    }

    [Fact]
    public void Reflow_SelectedRange()
    {
        var doc = new Document();
        doc.SetText("a\nb\nc\nd\ne");
        doc.Reflow(1, 3, IsParagraph);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("a");
        doc.GetBlockText(1).Should().Be("b c d");
        doc.GetBlockText(2).Should().Be("e");
    }

    [Fact]
    public void Reflow_Undoable()
    {
        var doc = new Document();
        doc.SetText("a\nb\nc");
        doc.BeginUndoGroup();
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.SealUndoGroup();
        doc.GetBlockText(0).Should().Be("a b c");

        doc.Undo().Should().BeTrue();
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("a");
        doc.GetBlockText(1).Should().Be("b");
        doc.GetBlockText(2).Should().Be("c");
    }

    // --- ReflowBoxTable ---

    [Fact]
    public void ReflowBoxTable_ConvertsSimpleTable()
    {
        var doc = new Document();
        doc.SetText(
            "┌───┬───┐\n" +
            "│ A │ B │\n" +
            "├───┼───┤\n" +
            "│ 1 │ 2 │\n" +
            "└───┴───┘");
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("| A | B |");
        doc.GetBlockText(1).Should().Be("| --- | --- |");
        doc.GetBlockText(2).Should().Be("| 1 | 2 |");
    }

    [Fact]
    public void ReflowBoxTable_ConvertsMultiRowTable()
    {
        var doc = new Document();
        doc.SetText(
            "┌─────┬──────┬───────┐\n" +
            "│  #  │ File │ Lines │\n" +
            "├─────┼──────┼───────┤\n" +
            "│ 1   │ a.cs │ 10    │\n" +
            "├─────┼──────┼───────┤\n" +
            "│ 2   │ b.cs │ 20    │\n" +
            "└─────┴──────┴───────┘");
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("| # | File | Lines |");
        doc.GetBlockText(1).Should().Be("| --- | --- | --- |");
        doc.GetBlockText(2).Should().Be("| 1 | a.cs | 10 |");
        doc.GetBlockText(3).Should().Be("| 2 | b.cs | 20 |");
    }

    [Fact]
    public void ReflowBoxTable_PreservesSurroundingBlocks()
    {
        var doc = new Document();
        doc.SetText(
            "before\n" +
            "┌───┬───┐\n" +
            "│ A │ B │\n" +
            "└───┴───┘\n" +
            "after");
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("before");
        doc.GetBlockText(1).Should().Be("| A | B |");
        doc.GetBlockText(2).Should().Be("| --- | --- |");
        doc.GetBlockText(3).Should().Be("after");
    }

    [Fact]
    public void ReflowBoxTable_ReturnsUpdatedEndBlock()
    {
        var doc = new Document();
        doc.SetText(
            "┌───┬───┐\n" +
            "│ A │ B │\n" +
            "├───┼───┤\n" +
            "│ 1 │ 2 │\n" +
            "└───┴───┘");
        int newEnd = doc.ReflowBoxTable(0, doc.BlockCount - 1);
        newEnd.Should().Be(2);
    }

    [Fact]
    public void ReflowBoxTable_LeavesNonBoxLinesAlone()
    {
        var doc = new Document();
        doc.SetText("hello\nworld");
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().Be("world");
    }

    [Fact]
    public void ReflowBoxTable_AdjustsCursorAfterTable()
    {
        var doc = new Document();
        doc.SetText(
            "┌───┬───┐\n" +
            "│ A │ B │\n" +
            "├───┼───┤\n" +
            "│ 1 │ 2 │\n" +
            "└───┴───┘\n" +
            "after");
        doc.CursorBlock = 5;
        doc.CursorOffset = 2;
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.CursorBlock.Should().Be(3);
        doc.CursorOffset.Should().Be(2);
    }

    // --- ComparePositions ---

    [Fact]
    public void ComparePositions_SameBlockDifferentOffset()
    {
        Document.ComparePositions(0, 3, 0, 5).Should().BeNegative();
        Document.ComparePositions(0, 5, 0, 3).Should().BePositive();
        Document.ComparePositions(0, 3, 0, 3).Should().Be(0);
    }

    [Fact]
    public void ComparePositions_DifferentBlock()
    {
        Document.ComparePositions(0, 10, 1, 0).Should().BeNegative();
        Document.ComparePositions(2, 0, 1, 100).Should().BePositive();
    }

    // --- Undo/Redo helpers ---

    private static void TypeAndSeal(Document doc, string text)
    {
        doc.BeginUndoGroup();
        foreach (char c in text) doc.Insert(c);
        doc.CollapseSelection();
        doc.SealUndoGroup();
    }

    // --- Undo: basic round-trip ---

    [Fact]
    public void Undo_RevertsInsert()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");

        doc.Undo().Should().BeTrue();
        doc.GetBlockText(0).Should().BeEmpty();
    }

    [Fact]
    public void Redo_ReappliesInsert()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");
        doc.Undo();

        doc.Redo().Should().BeTrue();
        doc.GetBlockText(0).Should().Be("abc");
    }

    [Fact]
    public void Undo_RevertsParagraphBreak()
    {
        var doc = new Document();
        TypeAndSeal(doc, "hello");

        doc.BeginUndoGroup();
        doc.InsertParagraphBreak();
        doc.CollapseSelection();
        doc.SealUndoGroup();

        doc.BlockCount.Should().Be(2);
        doc.Undo().Should().BeTrue();
        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("hello");
    }

    [Fact]
    public void Undo_RevertsBackspace()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");

        doc.BeginUndoGroup();
        doc.Backspace();
        doc.SealUndoGroup();

        doc.GetBlockText(0).Should().Be("ab");
        doc.Undo().Should().BeTrue();
        doc.GetBlockText(0).Should().Be("abc");
    }

    [Fact]
    public void Undo_RevertsDelete()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");

        doc.CursorOffset = 1;
        doc.CollapseSelection();
        doc.BeginUndoGroup();
        doc.Delete();
        doc.SealUndoGroup();

        doc.GetBlockText(0).Should().Be("ac");
        doc.Undo().Should().BeTrue();
        doc.GetBlockText(0).Should().Be("abc");
    }

    [Fact]
    public void Undo_RevertsPaste()
    {
        var doc = new Document();
        doc.BeginUndoGroup();
        doc.Paste("hello\r\nworld");
        doc.SealUndoGroup();

        doc.BlockCount.Should().Be(2);
        doc.Undo().Should().BeTrue();
        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().BeEmpty();
    }

    [Fact]
    public void Undo_RevertsDeleteSelection()
    {
        var doc = new Document();
        TypeAndSeal(doc, "hello world");

        doc.AnchorBlock = 0;
        doc.AnchorOffset = 5;
        doc.CursorBlock = 0;
        doc.CursorOffset = 11;

        doc.BeginUndoGroup();
        doc.DeleteSelection();
        doc.SealUndoGroup();

        doc.GetBlockText(0).Should().Be("hello");
        doc.Undo().Should().BeTrue();
        doc.GetBlockText(0).Should().Be("hello world");
    }

    // --- Cursor restoration ---

    [Fact]
    public void Undo_RestoresCursorPosition()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");
        doc.CursorOffset.Should().Be(3);

        TypeAndSeal(doc, "def");
        doc.CursorOffset.Should().Be(6);

        doc.Undo();
        doc.CursorOffset.Should().Be(3);
    }

    [Fact]
    public void Redo_RestoresCursorPosition()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");
        TypeAndSeal(doc, "def");

        doc.Undo();
        doc.CursorOffset.Should().Be(3);

        doc.Redo();
        doc.CursorOffset.Should().Be(6);
    }

    // --- Group management ---

    [Fact]
    public void BeginUndoGroup_IsIdempotent()
    {
        var doc = new Document();
        doc.BeginUndoGroup();
        doc.Insert('a');
        doc.BeginUndoGroup();
        doc.Insert('b');
        doc.BeginUndoGroup();
        doc.Insert('c');
        doc.CollapseSelection();
        doc.SealUndoGroup();

        doc.Undo().Should().BeTrue();
        doc.GetBlockText(0).Should().BeEmpty();
        doc.Undo().Should().BeFalse();
    }

    [Fact]
    public void SealUndoGroup_WhenNoGroupOpen_IsNoOp()
    {
        var doc = new Document();
        doc.SealUndoGroup();
        doc.Undo().Should().BeFalse();
    }

    [Fact]
    public void SealUndoGroup_SkipsNoOpGroup()
    {
        var doc = new Document();
        doc.BeginUndoGroup();
        doc.SealUndoGroup();
        doc.Undo().Should().BeFalse();
    }

    [Fact]
    public void MultipleGroupsUndoInOrder()
    {
        var doc = new Document();
        TypeAndSeal(doc, "a");
        TypeAndSeal(doc, "b");
        TypeAndSeal(doc, "c");

        doc.GetBlockText(0).Should().Be("abc");
        doc.Undo();
        doc.GetBlockText(0).Should().Be("ab");
        doc.Undo();
        doc.GetBlockText(0).Should().Be("a");
        doc.Undo();
        doc.GetBlockText(0).Should().BeEmpty();
    }

    // --- Redo invalidation ---

    [Fact]
    public void NewMutation_ClearsRedoStack()
    {
        var doc = new Document();
        TypeAndSeal(doc, "abc");
        doc.Undo();
        doc.CanRedo.Should().BeTrue();

        TypeAndSeal(doc, "xyz");
        doc.CanRedo.Should().BeFalse();
        doc.Redo().Should().BeFalse();
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsFalse()
    {
        var doc = new Document();
        doc.Redo().Should().BeFalse();
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        var doc = new Document();
        doc.Undo().Should().BeFalse();
    }

    // --- Stack depth limit ---

    [Fact]
    public void UndoStack_CappedAtMaxDepth()
    {
        var doc = new Document();
        for (int i = 0; i < 250; i++)
            TypeAndSeal(doc, "x");

        int undoCount = 0;
        while (doc.Undo()) undoCount++;
        undoCount.Should().Be(200);
    }

    // --- Compound operation ---

    [Fact]
    public void Undo_CompoundDeleteSelectionAndType()
    {
        var doc = new Document();
        TypeAndSeal(doc, "hello world");

        doc.AnchorBlock = 0;
        doc.AnchorOffset = 5;
        doc.CursorBlock = 0;
        doc.CursorOffset = 11;

        doc.BeginUndoGroup();
        doc.DeleteSelection();
        foreach (char c in " earth") doc.Insert(c);
        doc.CollapseSelection();
        doc.SealUndoGroup();

        doc.GetBlockText(0).Should().Be("hello earth");
        doc.Undo();
        doc.GetBlockText(0).Should().Be("hello world");
    }

    // --- SelectWord ---

    [Fact]
    public void SelectWord_SelectsWordUnderCursor()
    {
        var doc = CreateDoc("hello world");
        doc.SelectWord(0, 2);
        doc.GetSelectedText().Should().Be("hello");
        doc.AnchorOffset.Should().Be(0);
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void SelectWord_SelectsSecondWord()
    {
        var doc = CreateDoc("hello world");
        doc.SelectWord(0, 8);
        doc.GetSelectedText().Should().Be("world");
        doc.AnchorOffset.Should().Be(6);
        doc.CursorOffset.Should().Be(11);
    }

    [Fact]
    public void SelectWord_SelectsPunctuation()
    {
        var doc = CreateDoc("foo---bar");
        doc.SelectWord(0, 4);
        doc.GetSelectedText().Should().Be("---");
        doc.AnchorOffset.Should().Be(3);
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void SelectWord_IncludesUnderscores()
    {
        var doc = CreateDoc("my_var = 1");
        doc.SelectWord(0, 1);
        doc.GetSelectedText().Should().Be("my_var");
        doc.AnchorOffset.Should().Be(0);
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void SelectWord_EmptyBlockDoesNothing()
    {
        var doc = CreateDoc("");
        doc.SelectWord(0, 0);
        doc.HasSelection.Should().BeFalse();
    }

    // --- ToggleBlockPrefix ---

    [Fact]
    public void ToggleBlockPrefix_AddsHeadingPrefix()
    {
        var doc = CreateDoc("hello");
        doc.CursorBlock = 0;
        doc.CursorOffset = 3;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "## ");

        doc.GetBlockText(0).Should().Be("## hello");
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void ToggleBlockPrefix_RemovesExistingPrefix()
    {
        var doc = CreateDoc("## hello");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "## ");

        doc.GetBlockText(0).Should().Be("hello");
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void ToggleBlockPrefix_ReplacesPrefix()
    {
        var doc = CreateDoc("## hello");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "### ");

        doc.GetBlockText(0).Should().Be("### hello");
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void ToggleBlockPrefix_AddsBulletPrefix()
    {
        var doc = CreateDoc("item");
        doc.CursorBlock = 0;
        doc.CursorOffset = 2;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "- ");

        doc.GetBlockText(0).Should().Be("- item");
        doc.CursorOffset.Should().Be(4);
    }

    [Fact]
    public void ToggleBlockPrefix_RemovesBulletPrefix()
    {
        var doc = CreateDoc("- item");
        doc.CursorBlock = 0;
        doc.CursorOffset = 4;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "- ");

        doc.GetBlockText(0).Should().Be("item");
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void ToggleBlockPrefix_AddsBlockquote()
    {
        var doc = CreateDoc("quoted");
        doc.CursorBlock = 0;
        doc.CursorOffset = 3;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "> ");

        doc.GetBlockText(0).Should().Be("> quoted");
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void ToggleBlockPrefix_ReplacesHeadingWithBullet()
    {
        var doc = CreateDoc("# heading");
        doc.CursorBlock = 0;
        doc.CursorOffset = 4;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "- ");

        doc.GetBlockText(0).Should().Be("- heading");
        doc.CursorOffset.Should().Be(4);
    }

    [Fact]
    public void ToggleBlockPrefix_CursorAtZero_DoesNotGoNegative()
    {
        var doc = CreateDoc("## hello");
        doc.CursorBlock = 0;
        doc.CursorOffset = 0;
        doc.CollapseSelection();

        doc.ToggleBlockPrefix(0, "## ");

        doc.GetBlockText(0).Should().Be("hello");
        doc.CursorOffset.Should().Be(0);
    }

    // --- InsertBlockAt ---

    [Fact]
    public void InsertBlockAt_Beginning_ShiftsCursorDown()
    {
        var doc = CreateDoc("first", "second");
        doc.CursorBlock = 1;
        doc.CursorOffset = 3;
        doc.AnchorBlock = 1;
        doc.AnchorOffset = 3;

        doc.InsertBlockAt(0, "new");

        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("new");
        doc.GetBlockText(1).Should().Be("first");
        doc.GetBlockText(2).Should().Be("second");
        doc.CursorBlock.Should().Be(2);
        doc.AnchorBlock.Should().Be(2);
    }

    [Fact]
    public void InsertBlockAt_End_DoesNotShiftCursor()
    {
        var doc = CreateDoc("first", "second");
        doc.CursorBlock = 0;
        doc.CursorOffset = 2;
        doc.CollapseSelection();

        doc.InsertBlockAt(2, "new");

        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(2).Should().Be("new");
        doc.CursorBlock.Should().Be(0);
    }

    [Fact]
    public void InsertBlockAt_AtCursor_ShiftsCursorDown()
    {
        var doc = CreateDoc("first", "second");
        doc.CursorBlock = 1;
        doc.CursorOffset = 0;
        doc.CollapseSelection();

        doc.InsertBlockAt(1, "inserted");

        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(1).Should().Be("inserted");
        doc.GetBlockText(2).Should().Be("second");
        doc.CursorBlock.Should().Be(2);
    }

    // --- RemoveBlockAt ---

    [Fact]
    public void RemoveBlockAt_BeforeCursor_ShiftsCursorUp()
    {
        var doc = CreateDoc("first", "second", "third");
        doc.CursorBlock = 2;
        doc.CursorOffset = 1;
        doc.CollapseSelection();

        doc.RemoveBlockAt(0);

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("second");
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(1);
    }

    [Fact]
    public void RemoveBlockAt_AfterCursor_DoesNotShift()
    {
        var doc = CreateDoc("first", "second", "third");
        doc.CursorBlock = 0;
        doc.CursorOffset = 2;
        doc.CollapseSelection();

        doc.RemoveBlockAt(2);

        doc.BlockCount.Should().Be(2);
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void RemoveBlockAt_AtCursor_MovesToPreviousBlockEnd()
    {
        var doc = CreateDoc("first", "second", "third");
        doc.CursorBlock = 1;
        doc.CursorOffset = 3;
        doc.CollapseSelection();

        doc.RemoveBlockAt(1);

        doc.BlockCount.Should().Be(2);
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void MoveWordRight_SkipsWordThenWhitespace()
    {
        var doc = CreateDoc("hello world test");
        doc.CursorOffset = 0;
        doc.MoveWordRight();
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void MoveWordRight_FromMiddleOfWord()
    {
        var doc = CreateDoc("hello world");
        doc.CursorOffset = 2;
        doc.MoveWordRight();
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void MoveWordRight_AtEndOfBlock_CrossesToNextBlock()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.MoveWordRight();
        doc.CursorBlock.Should().Be(1);
        doc.CursorOffset.Should().Be(0);
    }

    [Fact]
    public void MoveWordLeft_SkipsWhitespaceThenWord()
    {
        var doc = CreateDoc("hello world test");
        doc.CursorOffset = 12;
        doc.MoveWordLeft();
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void MoveWordLeft_FromMiddleOfWord()
    {
        var doc = CreateDoc("hello world");
        doc.CursorOffset = 8;
        doc.MoveWordLeft();
        doc.CursorOffset.Should().Be(6);
    }

    [Fact]
    public void MoveWordLeft_AtStartOfBlock_CrossesToPreviousBlock()
    {
        var doc = CreateDoc("hello", "world");
        doc.CursorBlock = 1;
        doc.CursorOffset = 0;
        doc.MoveWordLeft();
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void MoveWordRight_SkipsPunctuation()
    {
        var doc = CreateDoc("foo(bar, baz)");
        doc.CursorOffset = 0;
        doc.MoveWordRight();
        doc.CursorOffset.Should().Be(4);
    }

    [Fact]
    public void MoveWordLeft_SkipsPunctuation()
    {
        var doc = CreateDoc("foo(bar, baz)");
        doc.CursorOffset = 13;
        doc.MoveWordLeft();
        doc.CursorOffset.Should().Be(9);
    }
}
