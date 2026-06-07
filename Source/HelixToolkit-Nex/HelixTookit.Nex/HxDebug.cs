using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex;

/// <summary>
/// Provides debugging and assertion utilities for the Helix Toolkit.
/// </summary>
public sealed class HxDebug
{
    private static readonly ILogger logger = LogManager.Create<HxDebug>();

    /// <summary>
    /// Gets or sets a value indicating whether debug assertions are enabled for the application.
    /// </summary>
    /// <remarks>When enabled, additional runtime checks may be performed to help identify programming errors
    /// during development. This setting is typically used in debug builds and may impact performance if enabled in
    /// production.</remarks>
    public static bool EnableDebugAssertions { get; set; } = false;

    /// <summary>
    /// Asserts that a condition is true. Only active in DEBUG builds.
    /// </summary>
    /// <param name="cond">The condition to check.</param>
    /// <param name="message">Optional message to log if the assertion fails. Defaults to empty string.</param>
    /// <param name="function">The name of the calling function (automatically populated by the compiler).</param>
    public static void Assert(
        bool cond,
        string message = "",
        [CallerMemberName] string function = ""
    )
    {
        if (!cond)
        {
            logger.LogError($"Assertion failed in {function}: {message}");
            if (EnableDebugAssertions)
            {
                Debug.Assert(false, message);
            }
        }
    }

    public static DisableAssertScope TemporarilyDisableAsserts() => new DisableAssertScope();

    static HxDebug()
    {
#if DEBUG
        EnableDebugAssertions = true;
#endif
        logger.LogInformation("HxDebug initialized. EnableDebugAssertions = {EnableDebugAssertions}", EnableDebugAssertions);
    }


    public struct DisableAssertScope : IDisposable
    {
        private bool _previousState;

        public DisableAssertScope()
        {
            _previousState = EnableDebugAssertions;
            if (_previousState)
            {
                EnableDebugAssertions = false;
                logger.LogInformation("Debug assertions temporarily disabled.");
            }
        }
        public void Dispose()
        {
            EnableDebugAssertions = _previousState;
        }
    }
}
