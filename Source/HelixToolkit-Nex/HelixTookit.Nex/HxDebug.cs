using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex;

/// <summary>
/// Provides debugging and assertion utilities for the Helix Toolkit.
/// </summary>
public sealed class HxDebug
{
    private static readonly ILogger logger = LogManager.Create<HxDebug>();

    /// <summary>
    /// Asserts that a condition is true. Only active in DEBUG builds.
    /// </summary>
    /// <param name="cond">The condition to check.</param>
    /// <param name="message">Optional message to log if the assertion fails. Defaults to empty string.</param>
    /// <param name="function">The name of the calling function (automatically populated by the compiler).</param>
    /// <remarks>
    /// This method only executes in DEBUG builds. If the condition is false, it logs an error
    /// and triggers a debug assertion.
    /// </remarks>
    [Conditional("DEBUG")]
    public static void Assert(bool cond, string message = "", [CallerMemberName] string function = "")
    {
        if (!cond)
        {
            logger.LogError($"Assertion failed in {function}: {message}");
            Debug.Assert(false);
        }
    }
}
