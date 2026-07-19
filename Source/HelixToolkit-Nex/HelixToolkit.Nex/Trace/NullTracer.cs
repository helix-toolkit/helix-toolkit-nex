using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Trace;

/// <summary>
/// A null tracer that performs no operations. Used for zero-overhead when tracing is disabled.
/// All methods are inlined and do nothing, resulting in zero allocation and minimal CPU cost.
/// </summary>
public sealed class NullTracer : ITracer
{
    private static readonly TraceEntry[] EmptyEntries = Array.Empty<TraceEntry>();

    /// <summary>
    /// Singleton instance of the NullTracer.
    /// </summary>
    public static readonly NullTracer Instance = new();

    private NullTracer() { }

    /// <inheritdoc/>
    public bool IsEnabled => false;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope BeginScope(string name)
    {
        return default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope BeginScope(string name, string category)
    {
        return default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceEvent(string name, double value)
    {
        // No operation
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceEvent(string name, string category, double value)
    {
        // No operation
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceMarker(string name)
    {
        // No operation
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceMarker(string name, string category)
    {
        // No operation
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<TraceEntry> GetTraceEntries()
    {
        return EmptyEntries;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        // No operation
    }

    public void EndScope(string name, string? category, long startTicks, long endTicks, int threadId)
    {
    }
}
