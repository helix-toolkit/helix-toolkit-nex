namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Represents a single trace entry with minimal allocation overhead.
/// </summary>
public readonly struct TraceEntry
{
    /// <summary>
    /// The type of trace entry.
    /// </summary>
    public TraceEntryType EntryType { get; init; }

    /// <summary>
    /// The name of the trace entry.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The category of the trace entry (optional).
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// The start timestamp in ticks (high-resolution performance counter).
    /// </summary>
    public long StartTicks { get; init; }

    /// <summary>
    /// The end timestamp in ticks (for scopes only).
    /// </summary>
    public long EndTicks { get; init; }

    /// <summary>
    /// The numeric value associated with the entry (for events).
    /// </summary>
    public double Value { get; init; }

    /// <summary>
    /// The thread ID where the trace was recorded.
    /// </summary>
    public int ThreadId { get; init; }

    /// <summary>
    /// Gets the duration in milliseconds (for scopes only).
    /// </summary>
    public double DurationMs => (EndTicks - StartTicks) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Gets the duration in ticks (for scopes only).
    /// </summary>
    public long DurationTicks => EndTicks - StartTicks;

    /// <summary>
    /// Creates a new trace entry for a scope start.
    /// </summary>
    public static TraceEntry CreateScopeStart(
        string name,
        string? category,
        long startTicks,
        int threadId
    )
    {
        return new TraceEntry
        {
            EntryType = TraceEntryType.ScopeBegin,
            Name = name,
            Category = category,
            StartTicks = startTicks,
            EndTicks = 0,
            Value = 0,
            ThreadId = threadId,
        };
    }

    /// <summary>
    /// Creates a new trace entry for a completed scope.
    /// </summary>
    public static TraceEntry CreateScope(
        string name,
        string? category,
        long startTicks,
        long endTicks,
        int threadId
    )
    {
        return new TraceEntry
        {
            EntryType = TraceEntryType.Scope,
            Name = name,
            Category = category,
            StartTicks = startTicks,
            EndTicks = endTicks,
            Value = 0,
            ThreadId = threadId,
        };
    }

    /// <summary>
    /// Creates a new trace entry for an event.
    /// </summary>
    public static TraceEntry CreateEvent(
        string name,
        string? category,
        double value,
        long timestamp,
        int threadId
    )
    {
        return new TraceEntry
        {
            EntryType = TraceEntryType.Event,
            Name = name,
            Category = category,
            StartTicks = timestamp,
            EndTicks = timestamp,
            Value = value,
            ThreadId = threadId,
        };
    }

    /// <summary>
    /// Creates a new trace entry for a marker.
    /// </summary>
    public static TraceEntry CreateMarker(
        string name,
        string? category,
        long timestamp,
        int threadId
    )
    {
        return new TraceEntry
        {
            EntryType = TraceEntryType.Marker,
            Name = name,
            Category = category,
            StartTicks = timestamp,
            EndTicks = timestamp,
            Value = 0,
            ThreadId = threadId,
        };
    }

    public override string ToString()
    {
        var categoryStr = Category != null ? $"[{Category}] " : "";
        return EntryType switch
        {
            TraceEntryType.Scope => $"{categoryStr}{Name}: {DurationMs:F3}ms",
            TraceEntryType.Event => $"{categoryStr}{Name}: {Value}",
            TraceEntryType.Marker => $"{categoryStr}{Name}",
            TraceEntryType.ScopeBegin => $"{categoryStr}{Name}: Begin",
            _ => $"{categoryStr}{Name}",
        };
    }
}

/// <summary>
/// The type of trace entry.
/// </summary>
public enum TraceEntryType : byte
{
    /// <summary>
    /// A completed scope with start and end times.
    /// </summary>
    Scope,

    /// <summary>
    /// An event with a value.
    /// </summary>
    Event,

    /// <summary>
    /// An instant marker.
    /// </summary>
    Marker,

    /// <summary>
    /// A scope begin marker (not yet completed).
    /// </summary>
    ScopeBegin,
}
