using Winly.Core.Actions;
using Winly.Platform.Settings;

namespace Winly.Core.Tests.Actions;

/// <summary>
/// The record is Platform-layer because it touches the disk, but it has no interop in it, so it is
/// the one part of that layer CI can cover.
/// </summary>
public class ActionRecordTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"winly-actions-{Guid.NewGuid():N}.jsonl");

    public void Dispose() => File.Delete(_path);

    private JsonLinesActionRecord Record() => new(_path);

    private static ActionOutcome Completed(string target) =>
        ActionOutcome.Completed(new DesktopAction(DesktopActionKind.Open, target));

    private static void Append(IActionRecord record, ActionOutcome outcome) =>
        record.Append(ActionRecordEntry.From(outcome, DateTimeOffset.UtcNow));

    [Fact]
    public void EveryAttemptIsRecordedWithWhatItWasAndWhatBecameOfIt()
    {
        var record = Record();
        Append(record, Completed("Spotify"));
        Append(record, ActionOutcome.Failed(new DesktopAction(DesktopActionKind.Open, "Spotty"), "I couldn't find it."));

        var entries = record.Read();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry is { Verb: "Open", Target: "Spotify", Status: "Completed" });
        Assert.Contains(entries, entry => entry is { Target: "Spotty", Status: "Failed", Reason: "I couldn't find it." });
    }

    /// <summary>A declined action must appear as declined, not be absent (FR-023).</summary>
    [Fact]
    public void ADeclinedActionIsRecordedRatherThanOmitted()
    {
        var record = Record();
        Append(record, ActionOutcome.Declined(new DesktopAction(DesktopActionKind.Window, "Word", "close"), "you said no to closing Word"));

        var entry = Assert.Single(record.Read());

        Assert.Equal("Declined", entry.Status);
        Assert.Equal("Word", entry.Target);
    }

    [Fact]
    public void ABlockedActionIsRecordedToo()
    {
        var record = Record();
        Append(record, ActionOutcome.Blocked(new DesktopAction(DesktopActionKind.Media, "next")));

        Assert.Equal("Blocked", Assert.Single(record.Read()).Status);
    }

    /// <summary>
    /// The one thing that must never reach the disk. A file listing everything the user dictated
    /// would be a worse problem than the one the record solves (FR-026, SC-010).
    /// </summary>
    [Fact]
    public void TypedTextIsNeverWrittenDown()
    {
        var record = Record();
        Append(record, ActionOutcome.Completed(new DesktopAction(DesktopActionKind.Type, "my bank password is hunter2")));

        var entry = Assert.Single(record.Read());

        Assert.Equal(ActionRecordEntry.RedactedText, entry.Target);
        Assert.DoesNotContain("hunter2", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public void EntriesSurviveBeingReadBackByAFreshInstance()
    {
        Append(Record(), Completed("Spotify"));

        var entries = Record().Read();

        Assert.Equal("Spotify", Assert.Single(entries).Target);
    }

    [Fact]
    public void TheOldestEntriesAreDiscardedOnceTheCapIsReached()
    {
        var record = Record();
        for (var index = 0; index < JsonLinesActionRecord.MaxEntries + 120; index++)
        {
            Append(record, Completed($"app{index}"));
        }

        var entries = Record().Read();

        Assert.True(entries.Count <= JsonLinesActionRecord.MaxEntries, $"kept {entries.Count} entries");
        Assert.DoesNotContain(entries, entry => entry.Target == "app0");
        Assert.Contains(entries, entry => entry.Target == $"app{JsonLinesActionRecord.MaxEntries + 119}");
    }

    [Fact]
    public void ClearingLeavesNothingBehind()
    {
        var record = Record();
        Append(record, Completed("Spotify"));

        record.Clear();

        Assert.Empty(record.Read());
        Assert.Empty(Record().Read());
    }

    [Fact]
    public void ReadingARecordThatWasNeverWrittenIsEmptyRatherThanAFailure() =>
        Assert.Empty(Record().Read());

    /// <summary>A half-written final line from a power cut costs that entry, never the file.</summary>
    [Fact]
    public void ATruncatedLineIsSkippedAndTheRestStillReads()
    {
        var record = Record();
        Append(record, Completed("Spotify"));
        File.AppendAllText(_path, "{\"Verb\":\"Open\",\"Targ");

        Assert.Equal("Spotify", Assert.Single(Record().Read()).Target);
    }

    [Fact]
    public void TheNewestEntryIsListedFirst()
    {
        var record = Record();
        Append(record, Completed("first"));
        Append(record, Completed("second"));

        Assert.Equal("second", record.Read()[0].Target);
    }
}
