namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Event arguments for trace events.
/// </summary>
public class TraceEventArgs : EventArgs
{
    /// <summary>
    /// Gets the trace entry that was recorded.
    /// </summary>
    public TraceEntry Entry { get; }

    /// <summary>
    /// Gets the timestamp when the event was raised.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the TraceEventArgs class.
    /// </summary>
    /// <param name="entry">The trace entry.</param>
    public TraceEventArgs(TraceEntry entry)
    {
        Entry = entry;
        Timestamp = DateTime.UtcNow;
    }
}
