namespace LocalNetworkMonitor.Core;

public sealed record DeviceCounters(string Id, ulong DownloadBytes, ulong UploadBytes);

public sealed record RateInterval(
    DateTimeOffset Start, DateTimeOffset End,
    double DownloadBytesPerSecond, double UploadBytesPerSecond);

public sealed class DeviceHistory
{
    private readonly List<RateInterval> intervals = [];
    internal DeviceCounters? Baseline { get; set; }
    internal DateTimeOffset BaselineAt { get; set; }
    public bool Present { get; internal set; }
    public DateTimeOffset? LastChange { get; internal set; }
    public DateTimeOffset FirstObserved { get; internal set; }
    public IReadOnlyList<RateInterval> Intervals => intervals.AsReadOnly();
    internal List<RateInterval> MutableIntervals => intervals;
}

public sealed class MeasurementHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, DeviceHistory> entries = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? lastObservation;
    public IReadOnlyDictionary<string, DeviceHistory> Entries => entries;

    public void Observe(DateTimeOffset at, IReadOnlyList<DeviceCounters> counters)
    {
        if (lastObservation is { } previous && at <= previous)
            throw new ArgumentOutOfRangeException(nameof(at), "Observation times must increase.");
        if (counters.Any(c => string.IsNullOrWhiteSpace(c.Id)) ||
            counters.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != counters.Count)
            throw new ArgumentException("Device entries must have unique, nonempty identities.", nameof(counters));
        lastObservation = at;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var counter in counters)
        {
            seen.Add(counter.Id);
            if (!entries.TryGetValue(counter.Id, out var entry))
                entries.Add(counter.Id, entry = new DeviceHistory { FirstObserved = at });
            entry.Present = true;
            if (entry.Baseline is { } baseline)
            {
                if (counter.DownloadBytes < baseline.DownloadBytes || counter.UploadBytes < baseline.UploadBytes)
                {
                    entry.LastChange = at;
                    entry.Baseline = null;
                }
                else if (counter.DownloadBytes != baseline.DownloadBytes || counter.UploadBytes != baseline.UploadBytes)
                {
                    var seconds = (at - entry.BaselineAt).TotalSeconds;
                    entry.MutableIntervals.Add(new(entry.BaselineAt, at,
                        (counter.DownloadBytes - baseline.DownloadBytes) / seconds,
                        (counter.UploadBytes - baseline.UploadBytes) / seconds));
                    entry.LastChange = at;
                    entry.Baseline = null;
                }
            }
            if (entry.Baseline is null)
            {
                entry.Baseline = counter;
                entry.BaselineAt = at;
            }
        }
        foreach (var (id, entry) in entries)
        {
            if (!seen.Contains(id))
            {
                entry.Present = false;
                entry.Baseline = null;
            }
        }
        Prune(at);
    }

    public void BreakContinuity()
    {
        foreach (var entry in entries.Values)
        {
            entry.Baseline = null;
            entry.Present = false;
        }
    }

    public void Prune(DateTimeOffset now)
    {
        foreach (var entry in entries.Values)
            entry.MutableIntervals.RemoveAll(i => i.End <= now - Retention);
    }
}
