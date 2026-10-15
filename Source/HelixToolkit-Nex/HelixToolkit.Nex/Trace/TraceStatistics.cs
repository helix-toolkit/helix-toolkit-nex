namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Statistics for a specific trace entry.
/// </summary>
public class TraceStatistics
{
    private readonly List<double> _samples = new();

    /// <summary>
    /// Gets the name of the trace.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the number of samples.
    /// </summary>
    public int Count => _samples.Count;

    /// <summary>
    /// Gets the minimum duration in milliseconds.
    /// </summary>
    public double Min => _samples.Count > 0 ? _samples.Min() : 0;

    /// <summary>
    /// Gets the maximum duration in milliseconds.
    /// </summary>
    public double Max => _samples.Count > 0 ? _samples.Max() : 0;

    /// <summary>
    /// Gets the average duration in milliseconds.
    /// </summary>
    public double Average => _samples.Count > 0 ? _samples.Average() : 0;

    /// <summary>
    /// Gets the total duration in milliseconds.
    /// </summary>
    public double Total => _samples.Sum();

    /// <summary>
    /// Gets the standard deviation.
    /// </summary>
    public double StandardDeviation
    {
        get
        {
            if (_samples.Count < 2)
                return 0;
            var avg = Average;
            var sumOfSquares = _samples.Sum(d => (d - avg) * (d - avg));
            return Math.Sqrt(sumOfSquares / _samples.Count);
        }
    }

    /// <summary>
    /// Gets the median duration in milliseconds.
    /// </summary>
    public double Median
    {
        get
        {
            if (_samples.Count == 0)
                return 0;
            var sorted = _samples.OrderBy(x => x).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }
    }

    /// <summary>
    /// Gets the 95th percentile duration in milliseconds.
    /// </summary>
    public double Percentile95
    {
        get
        {
            if (_samples.Count == 0)
                return 0;
            var sorted = _samples.OrderBy(x => x).ToList();
            int index = (int)Math.Ceiling(sorted.Count * 0.95) - 1;
            return sorted[Math.Max(0, index)];
        }
    }

    /// <summary>
    /// Gets the 99th percentile duration in milliseconds.
    /// </summary>
    public double Percentile99
    {
        get
        {
            if (_samples.Count == 0)
                return 0;
            var sorted = _samples.OrderBy(x => x).ToList();
            int index = (int)Math.Ceiling(sorted.Count * 0.99) - 1;
            return sorted[Math.Max(0, index)];
        }
    }

    /// <summary>
    /// Initializes a new instance of the TraceStatistics class.
    /// </summary>
    /// <param name="name">The name of the trace.</param>
    public TraceStatistics(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Adds a sample to the statistics.
    /// </summary>
    public void AddSample(double durationMs)
    {
        _samples.Add(durationMs);
    }

    /// <summary>
    /// Clears all samples.
    /// </summary>
    public void Clear()
    {
        _samples.Clear();
    }

    public override string ToString()
    {
        if (Count == 0)
            return $"{Name}: No samples";

        return $"{Name}: Count={Count}, Avg={Average:F3}ms, Min={Min:F3}ms, Max={Max:F3}ms, "
            + $"Median={Median:F3}ms, StdDev={StandardDeviation:F3}ms, "
            + $"P95={Percentile95:F3}ms, P99={Percentile99:F3}ms";
    }
}

/// <summary>
/// Utility class for aggregating and reporting trace data.
/// </summary>
public static class TraceAggregator
{
    /// <summary>
    /// Aggregates trace entries by name and returns statistics.
    /// </summary>
    public static Dictionary<string, TraceStatistics> AggregateByName(
        IReadOnlyList<TraceEntry> entries
    )
    {
        var stats = new Dictionary<string, TraceStatistics>();

        foreach (var entry in entries)
        {
            if (entry.EntryType != TraceEntryType.Scope)
                continue;

            if (!stats.TryGetValue(entry.Name, out var stat))
            {
                stat = new TraceStatistics(entry.Name);
                stats[entry.Name] = stat;
            }

            stat.AddSample(entry.DurationMs);
        }

        return stats;
    }

    /// <summary>
    /// Aggregates trace entries by category and returns statistics.
    /// </summary>
    public static Dictionary<string, TraceStatistics> AggregateByCategory(
        IReadOnlyList<TraceEntry> entries
    )
    {
        var stats = new Dictionary<string, TraceStatistics>();

        foreach (var entry in entries)
        {
            if (entry.EntryType != TraceEntryType.Scope || entry.Category == null)
                continue;

            if (!stats.TryGetValue(entry.Category, out var stat))
            {
                stat = new TraceStatistics(entry.Category);
                stats[entry.Category] = stat;
            }

            stat.AddSample(entry.DurationMs);
        }

        return stats;
    }

    /// <summary>
    /// Gets the top N slowest trace entries.
    /// </summary>
    public static IReadOnlyList<TraceEntry> GetSlowestEntries(
        IReadOnlyList<TraceEntry> entries,
        int count = 10
    )
    {
        return entries
            .Where(e => e.EntryType == TraceEntryType.Scope)
            .OrderByDescending(e => e.DurationMs)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets the top N fastest trace entries.
    /// </summary>
    public static IReadOnlyList<TraceEntry> GetFastestEntries(
        IReadOnlyList<TraceEntry> entries,
        int count = 10
    )
    {
        return entries
            .Where(e => e.EntryType == TraceEntryType.Scope)
            .OrderBy(e => e.DurationMs)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Groups trace entries by thread ID.
    /// </summary>
    public static Dictionary<int, List<TraceEntry>> GroupByThread(IReadOnlyList<TraceEntry> entries)
    {
        var groups = new Dictionary<int, List<TraceEntry>>();

        foreach (var entry in entries)
        {
            if (!groups.TryGetValue(entry.ThreadId, out var list))
            {
                list = new List<TraceEntry>();
                groups[entry.ThreadId] = list;
            }

            list.Add(entry);
        }

        return groups;
    }

    /// <summary>
    /// Generates a summary report of all trace data.
    /// </summary>
    public static string GenerateReport(IReadOnlyList<TraceEntry> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Trace Report ===");
        sb.AppendLine($"Total Entries: {entries.Count}");
        sb.AppendLine($"Scopes: {entries.Count(e => e.EntryType == TraceEntryType.Scope)}");
        sb.AppendLine($"Events: {entries.Count(e => e.EntryType == TraceEntryType.Event)}");
        sb.AppendLine($"Markers: {entries.Count(e => e.EntryType == TraceEntryType.Marker)}");
        sb.AppendLine();

        var stats = AggregateByName(entries);
        if (stats.Count > 0)
        {
            sb.AppendLine("=== Statistics by Name ===");
            foreach (var stat in stats.Values.OrderByDescending(s => s.Total))
            {
                sb.AppendLine(stat.ToString());
            }
            sb.AppendLine();
        }

        var slowest = GetSlowestEntries(entries, 5);
        if (slowest.Count > 0)
        {
            sb.AppendLine("=== Top 5 Slowest Operations ===");
            for (int i = 0; i < slowest.Count; i++)
            {
                var entry = slowest[i];
                sb.AppendLine(
                    $"{i + 1}. {entry.Name}: {entry.DurationMs:F3}ms (Thread {entry.ThreadId})"
                );
            }
        }

        return sb.ToString();
    }
}
