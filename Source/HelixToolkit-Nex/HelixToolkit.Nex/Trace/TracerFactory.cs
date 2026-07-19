using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Factory for creating and managing tracer instances.
/// Provides centralized control over tracing configuration.
/// </summary>
public static class TracerFactory
{
    private static readonly ConcurrentDictionary<string, ITracer> _tracers = new();
    private static volatile ITracer _defaultTracer = NullTracer.Instance;
    private static volatile bool _globalEnabled = false;
    private static TracerConfiguration _configuration = new();

    /// <summary>
    /// Gets or sets the global tracing enabled state.
    /// When false, all tracers return NullTracer instances.
    /// </summary>
    public static bool GlobalEnabled
    {
        get => _globalEnabled;
        set
        {
            _globalEnabled = value;
            if (!value)
            {
                // Disable all existing tracers
                foreach (var tracer in _tracers.Values)
                {
                    if (tracer is PerformanceTracer perfTracer)
                    {
                        perfTracer.IsEnabled = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the default tracer configuration.
    /// </summary>
    public static TracerConfiguration Configuration
    {
        get => _configuration;
        set => _configuration = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the default tracer instance.
    /// </summary>
    public static ITracer Default
    {
        get => _defaultTracer;
        set => _defaultTracer = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Creates or gets a named tracer instance.
    /// </summary>
    /// <param name="name">The name of the tracer.</param>
    /// <returns>A tracer instance.</returns>
    public static ITracer GetTracer(string name)
    {
        if (!_globalEnabled)
        {
            return NullTracer.Instance;
        }

        return _tracers.GetOrAdd(
            name,
            n =>
            {
                return new PerformanceTracer(
                    _configuration.InitialCapacity,
                    _configuration.MaxEntries
                )
                {
                    IsEnabled = _configuration.Enabled,
                };
            }
        );
    }

    /// <summary>
    /// Creates a new performance tracer with the specified configuration.
    /// </summary>
    /// <param name="initialCapacity">Initial capacity for trace entries.</param>
    /// <param name="maxEntries">Maximum number of entries (0 for unlimited).</param>
    /// <returns>A new PerformanceTracer instance.</returns>
    public static ITracer CreateTracer(int initialCapacity = 1024, int maxEntries = 0)
    {
        if (!_globalEnabled)
        {
            return NullTracer.Instance;
        }

        return new PerformanceTracer(initialCapacity, maxEntries)
        {
            IsEnabled = _configuration.Enabled,
        };
    }

    /// <summary>
    /// Gets all registered named tracers.
    /// </summary>
    public static IReadOnlyDictionary<string, ITracer> GetAllTracers()
    {
        return _tracers;
    }

    /// <summary>
    /// Clears all trace data from all registered tracers.
    /// </summary>
    public static void ClearAll()
    {
        foreach (var tracer in _tracers.Values)
        {
            tracer.Clear();
        }
        _defaultTracer.Clear();
    }

    /// <summary>
    /// Removes a named tracer.
    /// </summary>
    public static bool RemoveTracer(string name)
    {
        return _tracers.TryRemove(name, out _);
    }

    /// <summary>
    /// Enables tracing globally and for all existing tracers.
    /// </summary>
    public static void Enable()
    {
        _globalEnabled = true;
        _configuration.Enabled = true;

        foreach (var tracer in _tracers.Values)
        {
            if (tracer is PerformanceTracer perfTracer)
            {
                perfTracer.IsEnabled = true;
            }
        }

        if (_defaultTracer is PerformanceTracer defaultPerfTracer)
        {
            defaultPerfTracer.IsEnabled = true;
        }
    }

    /// <summary>
    /// Disables tracing globally and for all existing tracers.
    /// </summary>
    public static void Disable()
    {
        _globalEnabled = false;
        _configuration.Enabled = false;

        foreach (var tracer in _tracers.Values)
        {
            if (tracer is PerformanceTracer perfTracer)
            {
                perfTracer.IsEnabled = false;
            }
        }

        if (_defaultTracer is PerformanceTracer defaultPerfTracer)
        {
            defaultPerfTracer.IsEnabled = false;
        }
    }

    /// <summary>
    /// Resets the factory to its initial state, clearing all tracers.
    /// </summary>
    public static void Reset()
    {
        _tracers.Clear();
        _defaultTracer = NullTracer.Instance;
        _globalEnabled = false;
        _configuration = new TracerConfiguration();
    }
}
