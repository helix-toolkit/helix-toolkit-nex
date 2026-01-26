using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Trace;

/// <summary>
/// A lightweight struct that represents a trace scope. Automatically ends the scope when disposed.
/// Zero allocation when tracing is disabled.
/// </summary>
public readonly struct TraceScope : IDisposable
{
    private readonly ITracer? _tracer;
    private readonly string? _name;
    private readonly string? _category;
    private readonly long _startTicks;
    private readonly int _threadId;

    /// <summary>
    /// Creates a new trace scope.
    /// </summary>
    /// <param name="tracer">The tracer instance.</param>
    /// <param name="name">The name of the scope.</param>
    /// <param name="category">The category of the scope.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceScope(ITracer? tracer, string name, string? category)
    {
        if (tracer?.IsEnabled == true)
        {
            _tracer = tracer;
            _name = name;
            _category = category;
            _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _threadId = Environment.CurrentManagedThreadId;
        }
        else
        {
            _tracer = null;
            _name = null;
            _category = null;
            _startTicks = 0;
            _threadId = 0;
        }
    }

    /// <summary>
    /// Ends the trace scope and records the trace entry.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_tracer != null && _name != null)
        {
            var endTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_tracer is PerformanceTracer perfTracer)
            {
                perfTracer.EndScope(_name, _category, _startTicks, endTicks, _threadId);
            }
        }
    }

    /// <summary>
    /// Gets whether this scope is active (tracing is enabled).
    /// </summary>
    public bool IsActive => _tracer != null;
}
