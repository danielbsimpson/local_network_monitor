using LocalNetworkMonitor.Core;
using Xunit;

namespace LocalNetworkMonitor.Tests;

public class MeasurementHistoryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void RepeatedCountersDoNotCreateSamplesOrShortenTheMeasuredInterval()
    {
        var history = new MeasurementHistory();
        history.Observe(Start, [new("entry", 100, 200)]);
        history.Observe(Start.AddSeconds(5), [new("entry", 100, 200)]);
        Assert.Empty(history.Entries["entry"].Intervals);
        Assert.Null(history.Entries["entry"].LastChange);
        history.Observe(Start.AddSeconds(25), [new("entry", 2600, 5200)]);
        var interval = Assert.Single(history.Entries["entry"].Intervals);
        Assert.Equal(Start, interval.Start);
        Assert.Equal(100, interval.DownloadBytesPerSecond);
        Assert.Equal(200, interval.UploadBytesPerSecond);
        Assert.Equal(Start.AddSeconds(25), history.Entries["entry"].LastChange);
    }

    [Fact]
    public void IdentityCasingDoesNotTurnRepeatedCountersIntoMeasurements()
    {
        var history = new MeasurementHistory();
        history.Observe(Start, [new("entry", 100, 200)]);
        history.Observe(Start.AddSeconds(5), [new("ENTRY", 100, 200)]);
        Assert.Empty(history.Entries["entry"].Intervals);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("disappearance")]
    [InlineData("reset")]
    public void CollectionGapsAndCounterResetsRequireANewBaseline(string interruption)
    {
        var history = new MeasurementHistory();
        history.Observe(Start, [new("entry", 100, 200)]);
        if (interruption == "timeout")
            history.BreakContinuity();
        else if (interruption == "disappearance")
            history.Observe(Start.AddSeconds(5), []);
        else
            history.Observe(Start.AddSeconds(5), [new("entry", 1, 2)]);
        history.Observe(Start.AddSeconds(25), [new("entry", 100, 200)]);
        if (interruption != "reset")
            Assert.Empty(history.Entries["entry"].Intervals);
        else
            Assert.Equal(Start.AddSeconds(5), Assert.Single(history.Entries["entry"].Intervals).Start);
    }

    [Fact]
    public void LongUnchangedIntervalsRemainCoarseAndHistoryExpiresAfterTenMinutes()
    {
        var history = new MeasurementHistory();
        history.Observe(Start, [new("entry", 100, 200)]);
        history.Observe(Start.AddSeconds(80), [new("entry", 8100, 16200)]);
        var interval = Assert.Single(history.Entries["entry"].Intervals);
        Assert.Equal(80, (interval.End - interval.Start).TotalSeconds);
        Assert.Equal(100, interval.DownloadBytesPerSecond);
        history.Prune(Start.AddSeconds(680));
        Assert.Empty(history.Entries["entry"].Intervals);
    }

    [Fact]
    public void FallingRollingCountersAreObservedChangesButNotNegativeRates()
    {
        var history = new MeasurementHistory();
        history.Observe(Start, [new("entry", 100, 200)]);
        history.Observe(Start.AddSeconds(25), [new("entry", 1, 2)]);
        Assert.Equal(Start.AddSeconds(25), history.Entries["entry"].LastChange);
        Assert.Empty(history.Entries["entry"].Intervals);
    }
}
