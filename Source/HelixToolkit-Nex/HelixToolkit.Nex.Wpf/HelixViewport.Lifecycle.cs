using System.Runtime.ExceptionServices;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// Identifies the current phase of a viewport lifecycle transition.
/// </summary>
internal enum ViewportLifecycleState
{
    Unloaded,
    Loading,
    Loaded,
    Replacing,
    Unloading,
    Disposing,
    Disposed,
    Faulted,
}

/// <summary>
/// Owns the active viewport session and coordinates its lifecycle.
/// </summary>
/// <typeparam name="TSession">The loaded-surface resource owner.</typeparam>
internal sealed class ViewportLifecycle<TSession> : IDisposable
    where TSession : class, IDisposable
{
    private readonly Action<TSession?> _activate;
    private readonly Action _deactivate;
    private readonly Action _disposeTerminalResources;
    private TSession? _session;

    /// <summary>
    /// Initializes a viewport lifecycle.
    /// </summary>
    /// <param name="activate">Connects a prepared session to the viewport.</param>
    /// <param name="deactivate">Disconnects the current session from the viewport.</param>
    /// <param name="disposeTerminalResources">Releases resources retained across unloading.</param>
    public ViewportLifecycle(
        Action<TSession?> activate,
        Action deactivate,
        Action disposeTerminalResources
    )
    {
        _activate = activate;
        _deactivate = deactivate;
        _disposeTerminalResources = disposeTerminalResources;
    }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public ViewportLifecycleState State { get; private set; } = ViewportLifecycleState.Unloaded;

    /// <summary>
    /// Gets the active loaded-surface session if one exists.
    /// </summary>
    public TSession? Session => _session;

    /// <summary>
    /// Creates and activates a session for a loaded period.
    /// </summary>
    /// <param name="createSession">Creates the session or returns null when no surface is needed.</param>
    public void Load(Func<TSession?> createSession)
    {
        if (State != ViewportLifecycleState.Unloaded)
        {
            return;
        }

        State = ViewportLifecycleState.Loading;
        CreateAndActivate(createSession);
    }

    /// <summary>
    /// Destructively rebuilds the active session.
    /// </summary>
    /// <param name="createSession">Creates the replacement session.</param>
    public void Replace(Func<TSession?> createSession)
    {
        if (State != ViewportLifecycleState.Loaded)
        {
            return;
        }

        State = ViewportLifecycleState.Replacing;
        var failures = new List<Exception>();
        DeactivateAndDisposeSession(failures);
        if (failures.Count > 0)
        {
            State = ViewportLifecycleState.Faulted;
            ThrowFailures(failures);
        }

        CreateAndActivate(createSession);
    }

    /// <summary>
    /// Suspends the viewport and releases its loaded-surface session.
    /// </summary>
    public void Unload()
    {
        if (State != ViewportLifecycleState.Loaded)
        {
            return;
        }

        State = ViewportLifecycleState.Unloading;
        var failures = new List<Exception>();
        DeactivateAndDisposeSession(failures);
        State = failures.Count == 0
            ? ViewportLifecycleState.Unloaded
            : ViewportLifecycleState.Faulted;
        ThrowIfAny(failures);
    }

    /// <summary>
    /// Releases the active session and all resources retained across unloading.
    /// </summary>
    public void Dispose()
    {
        if (State is ViewportLifecycleState.Disposing or ViewportLifecycleState.Disposed)
        {
            return;
        }

        State = ViewportLifecycleState.Disposing;
        var failures = new List<Exception>();
        DeactivateAndDisposeSession(failures);
        Try(_disposeTerminalResources, failures);
        State = ViewportLifecycleState.Disposed;
        ThrowIfAny(failures);
    }

    /// <summary>
    /// Creates and activates one session, faulting after one-pass cleanup on failure.
    /// </summary>
    private void CreateAndActivate(Func<TSession?> createSession)
    {
        try
        {
            _session = createSession();
            _activate(_session);
            State = ViewportLifecycleState.Loaded;
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            DeactivateAndDisposeSession(failures);
            State = ViewportLifecycleState.Faulted;
            ThrowFailures(failures);
        }
    }

    /// <summary>
    /// Deactivates the viewport and attempts to dispose of its current session.
    /// </summary>
    private void DeactivateAndDisposeSession(List<Exception> failures)
    {
        Try(_deactivate, failures);
        var session = _session;
        _session = null;
        if (session is not null)
        {
            Try(session.Dispose, failures);
        }
    }

    /// <summary>
    /// Attempts a cleanup action and records its failure.
    /// </summary>
    private static void Try(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    /// <summary>
    /// Throws when one or more cleanup operations failed.
    /// </summary>
    private static void ThrowIfAny(List<Exception> failures)
    {
        if (failures.Count > 0)
        {
            ThrowFailures(failures);
        }
    }

    /// <summary>
    /// Preserves a single exception or aggregates multiple failures.
    /// </summary>
    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(failures);
    }
}
