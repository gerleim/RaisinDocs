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
        var canvas = CreateCanvas("one\ntwo");
        canvas.TestSetSelection(0, 0, 1, 3);

        canvas.ToggleBlockquote();

        canvas.TestGetBlockText(0).Should().Be("> one");
        canvas.TestGetBlockText(1).Should().Be("> two");
        canvas.TestAnchorBlock.Should().Be(0);
        canvas.TestAnchorOffset.Should().Be(0);
        canvas.TestCursorBlock.Should().Be(1);
        canvas.TestCursorOffset.Should().Be(5);
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
}
