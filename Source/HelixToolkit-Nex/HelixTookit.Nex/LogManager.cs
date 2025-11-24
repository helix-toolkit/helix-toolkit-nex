using System.Diagnostics.CodeAnalysis;

namespace HelixToolkit.Nex;

/// <summary>
/// Provides centralized logging management for the Helix Toolkit.
/// </summary>
/// <remarks>
/// This class manages logger creation and allows customization of the logging infrastructure
/// by replacing the default <see cref="Factory"/> at application startup.
/// </remarks>
public static class LogManager
{
    /// <summary>
    /// Gets or sets the logger factory used to create logger instances.
    /// </summary>
    /// <value>
    /// The <see cref="ILoggerFactory"/> instance. Defaults to <see cref="DebugLoggerFactory"/>.
    /// Replace this at app startup to use a custom logger implementation.
    /// </value>
    public static ILoggerFactory Factory { set; get; } = new DebugLoggerFactory();

    /// <summary>
    /// Creates a logger for the specified type.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for. The type name is used as the category name.</typeparam>
    /// <returns>An <see cref="ILogger"/> instance for the specified type.</returns>
    public static ILogger Create<T>()
    {
        return Factory.CreateLogger<T>();
    }

    /// <summary>
    /// Creates a logger with the specified category name.
    /// </summary>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <returns>An <see cref="ILogger"/> instance with the specified category name.</returns>
    public static ILogger Create(string categoryName)
    {
        return Factory.CreateLogger(categoryName);
    }
}
