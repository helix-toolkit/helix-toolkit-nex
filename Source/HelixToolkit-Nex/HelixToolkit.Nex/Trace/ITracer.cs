namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Core interface for high-performance tracing with minimal allocation.
/// </summary>
public interface ITracer
{
    /// <summary>
    /// Gets whether tracing is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Begins a new trace scope with the specified name.
    /// </summary>
    /// <param name="name">The name of the trace scope.</param>
    /// <returns>A TraceScope that automatically ends when disposed.</returns>
    TraceScope BeginScope(string name);

    /// <summary>
    /// Begins a new trace scope with the specified name and category.
    /// </summary>
    /// <param name="name">The name of the trace scope.</param>
    /// <param name="category">The category of the trace.</param>
    /// <returns>A TraceScope that automatically ends when disposed.</returns>
    TraceScope BeginScope(string name, string category);

    /// <summary>
    /// Ends a previously started logical scope and records its completion details for diagnostic or logging purposes.
    /// </summary>
    /// <param name="name">The name of the scope to end. This identifies the logical operation or region being tracked.</param>
    /// <param name="category">The category associated with the scope. May be used to group or filter scopes. Can be null if no category is
    /// specified.</param>
    /// <param name="startTicks">The timestamp, in ticks, when the scope started. Used to calculate the duration of the scope.</param>
    /// <param name="endTicks">The timestamp, in ticks, when the scope ended. Must be greater than or equal to startTicks.</param>
    /// <param name="threadId">The identifier of the thread on which the scope was active.</param>
    void EndScope(string name, string? category, long startTicks, long endTicks, int threadId);

    /// <summary>
    /// Records a single trace event with a value.
    /// </summary>
    /// <param name="name">The name of the trace event.</param>
    /// <param name="value">The numeric value to record.</param>
    void TraceEvent(string name, double value);

    /// <summary>
    /// Records a single trace event with a value and category.
    /// </summary>
    /// <param name="name">The name of the trace event.</param>
    /// <param name="category">The category of the trace.</param>
    /// <param name="value">The numeric value to record.</param>
    void TraceEvent(string name, string category, double value);

    /// <summary>
    /// Records an instant marker in the trace timeline.
    /// </summary>
    /// <param name="name">The name of the marker.</param>
    void TraceMarker(string name);

    /// <summary>
    /// Records an instant marker with a category.
    /// </summary>
    /// <param name="name">The name of the marker.</param>
    /// <param name="category">The category of the marker.</param>
    void TraceMarker(string name, string category);

    /// <summary>
    /// Gets all recorded trace entries.
    /// </summary>
    /// <returns>A read-only collection of trace entries.</returns>
    IReadOnlyList<TraceEntry> GetTraceEntries();

    /// <summary>
    /// Clears all recorded trace entries.
    /// </summary>
    void Clear();
}
