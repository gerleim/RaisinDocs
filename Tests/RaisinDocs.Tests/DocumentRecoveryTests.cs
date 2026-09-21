using FluentAssertions;
using Xunit;

namespace RaisinDocs.Tests;

/// <summary>
/// Unsaved documents are kept through a crash and found again at the next start.
/// </summary>
/// <remarks>
/// The editor had no handler for an unexpected exception, so a crash ended it with every unsaved
/// document. It now writes them aside as it goes down and offers them back when it next starts.
/// </remarks>
public class DocumentRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rd-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly string _recoveryDir;
    private readonly string _original;

    public DocumentRecoveryTests()
    {
        Directory.CreateDirectory(_root);
        _recoveryDir = Path.Combine(_root, "recovery");
        _original = Path.Combine(_root, "notes.md");
        File.WriteAllText(_original, "what was saved");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private DocumentRecovery Recovery => new(_recoveryDir);

    [Fact]
    public void A_rescued_document_comes_back_with_its_text_file_and_version()
    {
        var baseline = DiskStamp.Of(_original);

        Recovery.Rescue([new RescuedDocument(_original, () => "what was typed", baseline)]).Should().Be(1);

        var found = Recovery.Find().Should().ContainSingle().Subject;
        found.Text.Should().Be("what was typed");
        found.OriginalPath.Should().Be(_original);
        found.Baseline.Should().Be(baseline, "a save after recovery asks if the file moved on since the crash");
    }

    [Fact]
    public void The_original_file_is_never_touched()
    {
        Recovery.Rescue([new RescuedDocument(_original, () => "what was typed", DiskStamp.Of(_original))]);

        File.ReadAllText(_original).Should().Be("what was saved");
        Directory.GetFiles(_root).Should().Equal(_original);
    }

    [Fact]
    public void An_untitled_document_comes_back_untitled()
    {
        Recovery.Rescue([new RescuedDocument(null, () => "never saved anywhere", null)]);

        var found = Recovery.Find().Should().ContainSingle().Subject;
        found.OriginalPath.Should().BeNull();
        found.Baseline.Should().BeNull();
        found.Text.Should().Be("never saved anywhere");
    }

    [Fact]
    public void A_document_whose_text_cannot_be_read_costs_only_itself()
    {
        // The damage that caused the crash may be in one of the documents.
        var rescued = Recovery.Rescue(
        [
            new RescuedDocument(_original, () => "the first", null),
            new RescuedDocument(null, () => throw new InvalidOperationException("damaged"), null),
            new RescuedDocument(null, () => "the third", null),
        ]);

        rescued.Should().Be(2);
        Recovery.Find().Select(d => d.Text).Should().Equal("the first", "the third");
    }

    [Fact]
    public void Two_documents_with_one_name_are_both_kept()
    {
        var other = Path.Combine(_root, "elsewhere", "notes.md");

        Recovery.Rescue(
        [
            new RescuedDocument(_original, () => "one", null),
            new RescuedDocument(other, () => "two", null),
        ]);

        Recovery.Find().Select(d => (d.OriginalPath, d.Text)).Should().Equal((_original, "one"), (other, "two"));
    }

    [Fact]
    public void Documents_come_back_in_the_order_they_were_rescued()
    {
        // Numbered with padding, so the tenth does not sort before the second.
        var documents = Enumerable.Range(1, 12).Select(i => new RescuedDocument(null, () => $"doc {i}", null)).ToList();

        Recovery.Rescue(documents);

        Recovery.Find().Select(d => d.Text).Should().Equal(Enumerable.Range(1, 12).Select(i => $"doc {i}"));
    }

    [Fact]
    public void A_text_whose_origin_is_lost_is_still_offered_untitled()
    {
        Recovery.Rescue([new RescuedDocument(_original, () => "what was typed", null)]);
        foreach (var origin in Directory.GetFiles(_recoveryDir, "*.origin"))
            File.Delete(origin);

        var found = Recovery.Find().Should().ContainSingle().Subject;
        found.Text.Should().Be("what was typed");
        found.OriginalPath.Should().BeNull();
    }

    [Fact]
    public void A_discarded_document_is_not_offered_again()
    {
        Recovery.Rescue([new RescuedDocument(_original, () => "what was typed", null)]);

        Recovery.Discard(Recovery.Find().Single());

        Recovery.Find().Should().BeEmpty();
        Directory.GetFiles(_recoveryDir).Should().BeEmpty();
    }

    [Fact]
    public void Nothing_rescued_means_nothing_offered()
    {
        Recovery.Find().Should().BeEmpty();
    }
}
