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
    /// <remarks>
    /// This method only executes in DEBUG builds with EnableDebugAssertions == true. If the condition is false, it logs an error
    /// and triggers a debug assertion.
    /// </remarks>
    [Conditional("DEBUG")]
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
}
