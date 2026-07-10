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

    [Fact]
    public void InsertParagraphBreak_CursorBeyondBlockLength_ClampsAndSplits()
    {
        var doc = CreateDoc("hello\\");
        // Simulate visual-mode desync: RemoveTextAt shortens the block without adjusting CursorOffset
        doc.CursorOffset = 6; // at end of "hello\"
        doc.RemoveTextAt(0, 5, 1); // remove backslash → block is now "hello" (len 5), cursor still 6
        doc.InsertParagraphBreak();

        doc.BlockCount.Should().Be(2);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().BeEmpty();
    }

    [Fact]
    public void Paste_CursorBeyondBlockLength_ClampsAndInserts()
    {
        var doc = CreateDoc("hello\\");
        doc.CursorOffset = 6;
        doc.RemoveTextAt(0, 5, 1); // block is "hello" (len 5), cursor still 6
        doc.Paste("!");

        doc.GetBlockText(0).Should().Be("hello!");
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

    [Fact]
    public void DeleteSelection_CrossBlock_OffsetBeyondBlockLength_DoesNotThrow()
    {
        var doc = CreateDoc("hello", "world");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 3;
        doc.CursorBlock = 1;
        doc.CursorOffset = 99;

        doc.DeleteSelection();

        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("hel");
        doc.CursorBlock.Should().Be(0);
        doc.CursorOffset.Should().Be(3);
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
    public void Reflow_WithSelection_CollapsesMultipleBlankLinesToOne()
    {
        var doc = new Document();
        doc.SetText("qwe\n\n\nasd");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 0;
        doc.CursorBlock = doc.BlockCount - 1;
        doc.CursorOffset = 3;
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("qwe");
        doc.GetBlockText(1).Should().Be("");
        doc.GetBlockText(2).Should().Be("asd");
    }

    [Fact]
    public void Reflow_WithSelection_PreservesSingleBlankLine()
    {
        var doc = new Document();
        doc.SetText("qwe\n\nasd");
        doc.AnchorBlock = 0;
        doc.AnchorOffset = 0;
        doc.CursorBlock = doc.BlockCount - 1;
        doc.CursorOffset = 3;
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("qwe");
        doc.GetBlockText(1).Should().Be("");
        doc.GetBlockText(2).Should().Be("asd");
    }

    [Fact]
    public void Reflow_WithoutSelection_PreservesBlankLines()
    {
        var doc = new Document();
        doc.SetText("qwe\n\n\nasd");
        doc.CollapseSelection();
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph);
        doc.BlockCount.Should().Be(4);
        doc.GetBlockText(0).Should().Be("qwe");
        doc.GetBlockText(3).Should().Be("asd");
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

    private static int IsFence(string text) => text.TrimStart().StartsWith("```") ? 3 : 0;

    [Fact]
    public void Reflow_PreservesFencedCodeBlock()
    {
        var doc = new Document();
        doc.SetText("before\n```\nline1\nline2\n```\nafter");
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph, IsFence);
        doc.BlockCount.Should().Be(6);
        doc.GetBlockText(0).Should().Be("before");
        doc.GetBlockText(1).Should().Be("```");
        doc.GetBlockText(2).Should().Be("line1");
        doc.GetBlockText(3).Should().Be("line2");
        doc.GetBlockText(4).Should().Be("```");
        doc.GetBlockText(5).Should().Be("after");
    }

    [Fact]
    public void Reflow_MergesOutsideFenceButNotInside()
    {
        var doc = new Document();
        doc.SetText("a\nb\n```\nx\ny\n```\nc\nd");
        doc.Reflow(0, doc.BlockCount - 1, IsParagraph, IsFence);
        doc.GetBlockText(0).Should().Be("a b");
        doc.GetBlockText(1).Should().Be("```");
        doc.GetBlockText(2).Should().Be("x");
        doc.GetBlockText(3).Should().Be("y");
        doc.GetBlockText(4).Should().Be("```");
        doc.GetBlockText(5).Should().Be("c d");
        doc.BlockCount.Should().Be(6);
    }

    // --- SplitInlineColorDivs ---

    private static int FindOpenEnd(string t) => MarkdownParser.FindInlineColorOpenEnd(t);
    private static int FindCloseStart(string t) => MarkdownParser.FindInlineColorCloseStart(t);
    private static string OpenToDiv(string t) => MarkdownParser.InlineOpenToDivOpen(t);

    [Fact]
    public void SplitInlineColorDivs_ConvertsToBlockDiv()
    {
        var doc = new Document();
        doc.SetText("<!--@fg:blue-->asdsd\n\nffsdf<!--/@fg-->");
        doc.SplitInlineColorDivs(0, doc.BlockCount - 1, FindOpenEnd, FindCloseStart, OpenToDiv);
        doc.GetBlockText(0).Should().Be("<!--@div fg:blue-->");
        doc.GetBlockText(1).Should().Be("asdsd");
        doc.GetBlockText(2).Should().BeEmpty();
        doc.GetBlockText(3).Should().Be("ffsdf");
        doc.GetBlockText(4).Should().Be("<!--/@div-->");
        doc.BlockCount.Should().Be(5);
    }

    [Fact]
    public void SplitInlineColorDivs_LeavesProperDivsAlone()
    {
        var doc = new Document();
        doc.SetText("<!--@div fg:blue-->\nhello\n<!--/@div-->");
        doc.SplitInlineColorDivs(0, doc.BlockCount - 1, FindOpenEnd, FindCloseStart, OpenToDiv);
        doc.BlockCount.Should().Be(3);
        doc.GetBlockText(0).Should().Be("<!--@div fg:blue-->");
        doc.GetBlockText(1).Should().Be("hello");
        doc.GetBlockText(2).Should().Be("<!--/@div-->");
    }

    [Fact]
    public void SplitInlineColorDivs_LeavesSameLineInlineAlone()
    {
        var doc = new Document();
        doc.SetText("hello <!--@fg:blue-->world<!--/@fg--> end");
        doc.SplitInlineColorDivs(0, doc.BlockCount - 1, FindOpenEnd, FindCloseStart, OpenToDiv);
        doc.BlockCount.Should().Be(1);
        doc.GetBlockText(0).Should().Be("hello <!--@fg:blue-->world<!--/@fg--> end");
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
    public void ReflowBoxTable_MergesMultiLineRows()
    {
        var doc = new Document();
        doc.SetText(
            "┌─────┬──────────────┐\n" +
            "│  #  │     Area     │\n" +
            "├─────┼──────────────┤\n" +
            "│ 1   │ Grammar      │\n" +
            "│     │   a          │\n" +
            "├─────┼──────────────┤\n" +
            "│ 2   │ Wording      │\n" +
            "│     │  a           │\n" +
            "├─────┼──────────────┤\n" +
            "│ 3   │ Completeness │\n" +
            "└─────┴──────────────┘");
        doc.ReflowBoxTable(0, doc.BlockCount - 1);
        doc.BlockCount.Should().Be(5);
        doc.GetBlockText(0).Should().Be("| # | Area |");
        doc.GetBlockText(1).Should().Be("| --- | --- |");
        doc.GetBlockText(2).Should().Be("| 1 | Grammar a |");
        doc.GetBlockText(3).Should().Be("| 2 | Wording a |");
        doc.GetBlockText(4).Should().Be("| 3 | Completeness |");
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

    // --- TrimWhitespace ---

    [Fact]
    public void TrimWhitespace_TrimsLeadingSpaces()
    {
        var doc = new Document();
        doc.SetText("  hello\n    world");
        doc.TrimWhitespace(0, doc.BlockCount - 1);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().Be("world");
    }

    [Fact]
    public void TrimWhitespace_TrimsOneTrailingSpace()
    {
        var doc = new Document();
        doc.SetText("hello \nworld ");
        doc.TrimWhitespace(0, doc.BlockCount - 1);
        doc.GetBlockText(0).Should().Be("hello");
        doc.GetBlockText(1).Should().Be("world");
    }

    [Fact]
    public void TrimWhitespace_PreservesMarkdownLineBreak()
    {
        var doc = new Document();
        doc.SetText("hello  \nworld");
        doc.TrimWhitespace(0, doc.BlockCount - 1);
        doc.GetBlockText(0).Should().Be("hello  ");
        doc.GetBlockText(1).Should().Be("world");
    }

    [Fact]
    public void TrimWhitespace_AdjustsCursorForLeadingTrim()
    {
        var doc = new Document();
        doc.SetText("   hello");
        doc.CursorBlock = 0;
        doc.CursorOffset = 5;
        doc.TrimWhitespace(0, doc.BlockCount - 1);
        doc.GetBlockText(0).Should().Be("hello");
        doc.CursorOffset.Should().Be(2);
    }

    [Fact]
    public void TrimWhitespace_ReturnsFalseWhenNothingToTrim()
    {
        var doc = new Document();
        doc.SetText("hello\nworld");
        doc.TrimWhitespace(0, doc.BlockCount - 1).Should().BeFalse();
    }

    [Fact]
    public void TrimWhitespace_ReturnsTrueWhenTrimmed()
    {
        var doc = new Document();
        doc.SetText("  hello");
        doc.TrimWhitespace(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void TrimWhitespace_NormalizesExcessiveTrailingSpacesToTwo()
    {
        var doc = new Document();
        doc.SetText("hello     \nworld");
        doc.TrimWhitespace(0, doc.BlockCount - 1);
        doc.GetBlockText(0).Should().Be("hello  ");
        doc.GetBlockText(1).Should().Be("world");
    }

    // --- HasBoxDrawingTable ---

    [Fact]
    public void HasBoxDrawingTable_DetectsBoxDrawing()
    {
        var doc = new Document();
        doc.SetText("┌───┬───┐\n│ A │ B │\n└───┴───┘");
        doc.HasBoxDrawingTable(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasBoxDrawingTable_ReturnsFalseForPlainText()
    {
        var doc = new Document();
        doc.SetText("hello\nworld");
        doc.HasBoxDrawingTable(0, doc.BlockCount - 1).Should().BeFalse();
    }

    // --- HasMergeableParagraphs ---

    [Fact]
    public void HasMergeableParagraphs_DetectsConsecutiveParagraphs()
    {
        var doc = new Document();
        doc.SetText("hello\nworld");
        doc.HasMergeableParagraphs(0, doc.BlockCount - 1, IsParagraph).Should().BeTrue();
    }

    [Fact]
    public void HasMergeableParagraphs_SkipsInsideFence()
    {
        var doc = new Document();
        doc.SetText("```\nline1\nline2\n```");
        doc.HasMergeableParagraphs(0, doc.BlockCount - 1, IsParagraph, IsFence).Should().BeFalse();
    }

    // --- HasConsecutiveBlankLines ---

    [Fact]
    public void HasConsecutiveBlankLines_DetectsMultipleBlanks()
    {
        var doc = new Document();
        doc.SetText("hello\n\n\nworld");
        doc.SelectAll();
        doc.HasConsecutiveBlankLines(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasConsecutiveBlankLines_IgnoresSingleBlank()
    {
        var doc = new Document();
        doc.SetText("hello\n\nworld");
        doc.SelectAll();
        doc.HasConsecutiveBlankLines(0, doc.BlockCount - 1).Should().BeFalse();
    }

    [Fact]
    public void HasConsecutiveBlankLines_RequiresSelection()
    {
        var doc = new Document();
        doc.SetText("hello\n\n\nworld");
        doc.CollapseSelection();
        doc.HasConsecutiveBlankLines(0, doc.BlockCount - 1).Should().BeFalse();
    }

    // --- HasTrimmableWhitespace ---

    [Fact]
    public void HasTrimmableWhitespace_DetectsLeadingSpaces()
    {
        var doc = new Document();
        doc.SetText("  indented");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasTrimmableWhitespace_DetectsLeadingTab()
    {
        var doc = new Document();
        doc.SetText("\tindented");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasTrimmableWhitespace_DetectsTrailingSpacesNonHardBreak()
    {
        var doc = new Document();
        doc.SetText("hello ");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasTrimmableWhitespace_IgnoresTrailingDoubleSpaceHardBreak()
    {
        var doc = new Document();
        doc.SetText("hello  ");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeFalse();
    }

    [Fact]
    public void HasTrimmableWhitespace_DetectsExcessiveTrailingSpaces()
    {
        var doc = new Document();
        doc.SetText("hello   ");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeTrue();
    }

    [Fact]
    public void HasTrimmableWhitespace_ReturnsFalseForCleanContent()
    {
        var doc = new Document();
        doc.SetText("hello");
        doc.HasTrimmableWhitespace(0, doc.BlockCount - 1).Should().BeFalse();
    }

    // --- HasMisnumberedOrderedList ---

    [Fact]
    public void HasMisnumberedOrderedList_DetectsMisnumbered()
    {
        var doc = new Document();
        doc.SetText("1. first\n3. second");
        doc.HasMisnumberedOrderedList(0, doc.BlockCount - 1, GetOrderedPrefix).Should().BeTrue();
    }

    [Fact]
    public void HasMisnumberedOrderedList_IgnoresCorrectlyNumbered()
    {
        var doc = new Document();
        doc.SetText("1. first\n2. second");
        doc.HasMisnumberedOrderedList(0, doc.BlockCount - 1, GetOrderedPrefix).Should().BeFalse();
    }

    // --- RenumberOrderedLists ---

    private static int GetOrderedPrefix(string text)
    {
        int i = 0;
        while (i < text.Length && i < 9 && text[i] >= '0' && text[i] <= '9') i++;
        if (i == 0 || i > 9) return 0;
        if (i < text.Length && text[i] is '.' or ')')
        {
            if (i + 1 < text.Length && text[i + 1] == ' ')
                return i + 2;
        }
        return 0;
    }

    [Fact]
    public void RenumberOrderedLists_FixesGap()
    {
        var doc = new Document();
        doc.SetText("1. first\n3. second\n4. third");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(0).Should().Be("1. first");
        doc.GetBlockText(1).Should().Be("2. second");
        doc.GetBlockText(2).Should().Be("3. third");
    }

    [Fact]
    public void RenumberOrderedLists_PreservesDelimiter()
    {
        var doc = new Document();
        doc.SetText("1) first\n3) second\n5) third");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(0).Should().Be("1) first");
        doc.GetBlockText(1).Should().Be("2) second");
        doc.GetBlockText(2).Should().Be("3) third");
    }

    [Fact]
    public void RenumberOrderedLists_PreservesStartNumber()
    {
        var doc = new Document();
        doc.SetText("3. alpha\n3. beta\n3. gamma");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(0).Should().Be("3. alpha");
        doc.GetBlockText(1).Should().Be("4. beta");
        doc.GetBlockText(2).Should().Be("5. gamma");
    }

    [Fact]
    public void RenumberOrderedLists_ReturnsFalseWhenAlreadyCorrect()
    {
        var doc = new Document();
        doc.SetText("1. first\n2. second\n3. third");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix).Should().BeFalse();
    }

    [Fact]
    public void RenumberOrderedLists_ReturnsTrueWhenChanged()
    {
        var doc = new Document();
        doc.SetText("1. first\n5. second");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix).Should().BeTrue();
    }

    [Fact]
    public void RenumberOrderedLists_HandlesMultipleSeparateRuns()
    {
        var doc = new Document();
        doc.SetText("1. a\n3. b\nparagraph\n1. c\n5. d");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(0).Should().Be("1. a");
        doc.GetBlockText(1).Should().Be("2. b");
        doc.GetBlockText(2).Should().Be("paragraph");
        doc.GetBlockText(3).Should().Be("1. c");
        doc.GetBlockText(4).Should().Be("2. d");
    }

    [Fact]
    public void RenumberOrderedLists_SkipsFencedCodeBlocks()
    {
        var doc = new Document();
        doc.SetText("1. first\n```\n5. not a list\n```\n3. second");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix, IsFence);
        doc.GetBlockText(0).Should().Be("1. first");
        doc.GetBlockText(2).Should().Be("5. not a list");
        doc.GetBlockText(4).Should().Be("3. second");
    }

    [Fact]
    public void RenumberOrderedLists_AdjustsCursorOffset()
    {
        var doc = new Document();
        doc.SetText("1. first\n10. second");
        doc.CursorBlock = 1;
        doc.CursorOffset = 6;
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(1).Should().Be("2. second");
        doc.CursorOffset.Should().Be(5);
    }

    [Fact]
    public void RenumberOrderedLists_NumberWidthChange()
    {
        var doc = new Document();
        doc.SetText("1. a\n2. b\n3. c\n4. d\n5. e\n6. f\n7. g\n8. h\n9. i\n11. j");
        doc.RenumberOrderedLists(0, doc.BlockCount - 1, GetOrderedPrefix);
        doc.GetBlockText(9).Should().Be("10. j");
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
