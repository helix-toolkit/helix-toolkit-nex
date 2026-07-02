using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HelixToolkit.Nex;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Installs a warning-capturing <see cref="ILoggerFactory"/> into <see cref="LogManager.Factory"/>
/// before any test runs, so warnings emitted by rendering code under test can be observed. The
/// capturing logger only records warning-or-higher messages and is otherwise inert.
/// </summary>
internal static class GizmoLoggerCapture
{
    /// <summary>The installed capturing factory, exposing the recorded warnings.</summary>
    public static CapturingLoggerFactory Factory { get; } = new();

    /// <summary>
    /// Installs <see cref="Factory"/> as the process-wide <see cref="LogManager.Factory"/> exactly
    /// once, before any test in this assembly executes.
    /// </summary>
    [ModuleInitializer]
    internal static void Install()
    {
        LogManager.Factory = Factory;
    }

    /// <summary>
    /// An <see cref="ILoggerFactory"/> whose loggers append every warning-or-higher message to a
    /// shared, thread-safe log that tests can count and snapshot. Warnings are only ever appended,
    /// so a monotonic <see cref="WarningCount"/> comparison is concurrency-safe.
    /// </summary>
    internal sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        /// <summary>The number of warning-or-higher messages recorded so far (monotonic).</summary>
        public int WarningCount => _warnings.Count;

        /// <summary>Returns a point-in-time copy of the recorded warning messages.</summary>
        public IReadOnlyList<string> Snapshot() => [.. _warnings];

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        /// <inheritdoc/>
        public void AddProvider(ILoggerProvider provider) { }

        /// <inheritdoc/>
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
        {
            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                private NullScope() { }

                public void Dispose() { }
            }

            private readonly ConcurrentQueue<string> _sink = sink;

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) =>
                logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);
                if (!string.IsNullOrEmpty(message))
                {
                    _sink.Enqueue(message);
                }
            }
        }
    }
}
