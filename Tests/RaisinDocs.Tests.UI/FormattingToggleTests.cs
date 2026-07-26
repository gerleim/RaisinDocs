using System.Windows;
using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests.UI;

public class FormattingToggleTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 600;

    private static DocsCanvas CreateCanvas(string text)
    {
        var canvas = new DocsCanvas();
        canvas.SetText(text);
        canvas.TestSetEditMode(DocsCanvas.EditMode.Visual);
        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.TestComputeLayout();
        return canvas;
    }

    // --- Gap #9: Toggling Bold off from ***bold-italic*** ---

    [StaFact]
    public void ToggleBold_OnBoldItalic_RemovesBoldKeepsItalic()
    {
        var canvas = CreateCanvas("***text***");
        canvas.TestSetSelection(0, 3, 0, 7);
        canvas.ToggleBold();
        canvas.TestGetBlockText(0).Should().Be("*text*");
    }

    [StaFact]
    public void ToggleItalic_OnBoldItalic_RemovesItalicKeepsBold()
    {
        var canvas = CreateCanvas("***text***");
        canvas.TestSetSelection(0, 3, 0, 7);
        canvas.ToggleItalic();
        canvas.TestGetBlockText(0).Should().Be("**text**");
    }

    [StaFact]
    public void ToggleBold_OnBoldItalic_SelectIncludingMarkers_AddsBoldWrapper()
    {
        // Selecting the full `***text***` includes marker chars that aren't styled,
        // so toggle detects "not all styled" and wraps with ** instead of removing
        var canvas = CreateCanvas("***text***");
        canvas.TestSetSelection(0, 0, 0, 10);
        canvas.ToggleBold();
        canvas.TestGetBlockText(0).Should().Be("**" + "***text***" + "**");
    }

    [StaFact]
    public void ToggleItalic_OnBoldItalic_SelectIncludingMarkers_AddsItalicWrapper()
    {
        var canvas = CreateCanvas("***text***");
        canvas.TestSetSelection(0, 0, 0, 10);
        canvas.ToggleItalic();
        canvas.TestGetBlockText(0).Should().Be("*" + "***text***" + "*");
    }

    [StaFact]
    public void ToggleBold_OnBoldItalicWithContext_RemovesBold()
    {
        var canvas = CreateCanvas("before ***text*** after");
        canvas.TestSetSelection(0, 10, 0, 14);
        canvas.ToggleBold();
        canvas.TestGetBlockText(0).Should().Be("before *text* after");
    }

    [StaFact]
    public void ToggleBold_OnPlainBold_RemovesBold()
    {
        var canvas = CreateCanvas("**text**");
        canvas.TestSetSelection(0, 0, 0, 8);
        canvas.ToggleBold();
        canvas.TestGetBlockText(0).Should().Be("text");
    }

    // --- Block prefix toggle: selection should include added prefixes ---

    [StaFact]
    public void ToggleBulletList_MultiBlock_SelectionIncludesFirstPrefix()
    {
        var canvas = CreateCanvas("one\ntwo");
        canvas.TestSetSelection(0, 0, 1, 3);

        canvas.ToggleBulletList();

        canvas.TestGetBlockText(0).Should().Be("- one");
        canvas.TestGetBlockText(1).Should().Be("- two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(5);
    }

    [StaFact]
    public void ToggleTaskList_MultiBlock_SelectionIncludesFirstPrefix()
    {
        var canvas = CreateCanvas("one\ntwo");
        canvas.TestSetSelection(0, 0, 1, 3);

        canvas.ToggleTaskList();

        canvas.TestGetBlockText(0).Should().Be("- [ ] one");
        canvas.TestGetBlockText(1).Should().Be("- [ ] two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(9);
    }

    [StaFact]
    public void ToggleOrderedList_MultiBlock_SelectionIncludesFirstPrefix()
    {
        var canvas = CreateCanvas("one\ntwo");
        canvas.TestSetSelection(0, 0, 1, 3);

        canvas.ToggleOrderedList();

        canvas.TestGetBlockText(0).Should().Be("1. one");
        canvas.TestGetBlockText(1).Should().Be("2. two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(6);
    }

    [StaFact]
    public void ToggleHeading_MultiBlock_SelectionIncludesFirstPrefix()
    {
        var canvas = CreateCanvas("one\ntwo");
        canvas.TestSetSelection(0, 0, 1, 3);

        canvas.ToggleHeading(2);

        canvas.TestGetBlockText(0).Should().Be("## one");
        canvas.TestGetBlockText(1).Should().Be("## two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(6);
    }

    [StaFact]
    public void ToggleBlockquote_MultiBlock_SelectionIncludesFirstPrefix()
    {
        // Use two headings (which cannot be merged as continuations)
        var canvas = CreateCanvas("# Heading 1\n# Heading 2");
        canvas.TestSetSelection(0, 0, 1, 9);

        canvas.ToggleBlockquote();

        canvas.TestGetBlockText(0).Should().Be("> # Heading 1");
        canvas.TestGetBlockText(1).Should().Be("> # Heading 2");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(13);
    }

    [StaFact]
    public void ToggleBulletList_RemovePrefix_SelectionCoversFullLines()
    {
        var canvas = CreateCanvas("- one\n- two");
        canvas.TestSetSelection(0, 0, 1, 5);

        canvas.ToggleBulletList();

        canvas.TestGetBlockText(0).Should().Be("one");
        canvas.TestGetBlockText(1).Should().Be("two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(3);
    }

    // --- Pending style-off toggle (no selection, cursor inside styled run) ---

    [StaFact]
    public void PendingBoldOff_InsideBold_TypingSplitsRun()
    {
        var canvas = CreateCanvas("**asd**");
        canvas.TestSetCursor(0, 3); // between 'a' and 's'
        canvas.ToggleBold();
        canvas.SelectionIsBold.Should().BeFalse("pending-off should report bold as off");
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("**a**X**sd**");
    }

    [StaFact]
    public void PendingBoldOff_Toggle_CancelsPending()
    {
        var canvas = CreateCanvas("**asd**");
        canvas.TestSetCursor(0, 3);
        canvas.ToggleBold();
        canvas.SelectionIsBold.Should().BeFalse();
        canvas.ToggleBold(); // cancel
        canvas.SelectionIsBold.Should().BeTrue("cancelling pending should restore bold");
    }

    [StaFact]
    public void PendingBoldOff_NavigationClearsPending()
    {
        var canvas = CreateCanvas("**asd**");
        canvas.TestSetCursor(0, 3);
        canvas.ToggleBold();
        canvas.SelectionIsBold.Should().BeFalse();
        canvas.TestNavigate(System.Windows.Input.Key.Right);
        canvas.SelectionIsBold.Should().BeTrue("navigation should clear pending");
    }

    [StaFact]
    public void PendingItalicOff_InsideItalic_TypingSplitsRun()
    {
        var canvas = CreateCanvas("*abc*");
        canvas.TestSetCursor(0, 2); // inside "abc"
        canvas.ToggleItalic();
        canvas.SelectionIsItalic.Should().BeFalse();
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("*a*X*bc*");
    }

    [StaFact]
    public void PendingStrikethroughOff_InsideStrikethrough_TypingSplitsRun()
    {
        var canvas = CreateCanvas("~~abc~~");
        canvas.TestSetCursor(0, 3);
        canvas.ToggleStrikethrough();
        canvas.SelectionIsStrikethrough.Should().BeFalse();
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("~~a~~X~~bc~~");
    }

    [StaFact]
    public void PendingCodeOff_InsideCode_TypingSplitsRun()
    {
        var canvas = CreateCanvas("`abc`");
        canvas.TestSetCursor(0, 2);
        canvas.ToggleCodeSpan();
        canvas.SelectionIsCode.Should().BeFalse();
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("`a`X`bc`");
    }

    [StaFact]
    public void PendingBoldOff_AtStartOfContent_ProducesEmptyOpenRun()
    {
        var canvas = CreateCanvas("**asd**");
        canvas.TestSetCursor(0, 2); // right at start of content
        canvas.ToggleBold();
        canvas.TestTypeText("X");
        canvas.TestGetBlockText(0).Should().Be("****X**asd**");
    }

    [StaFact]
    public void PendingBoldOff_OutsideBold_InsertsNewMarkers()
    {
        var canvas = CreateCanvas("hello **asd**");
        canvas.TestSetCursor(0, 3); // inside plain "hello"
        canvas.ToggleBold();
        // Not inside bold, so normal toggle: insert markers
        canvas.TestGetBlockText(0).Should().Be("hel****lo **asd**");
    }
}
