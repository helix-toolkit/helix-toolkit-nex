namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Provides extension methods for common graphics operations and type conversions.
/// </summary>
public static class Extensions
{
    static readonly ILogger logger = LogManager.Create("HelixToolkit.Nex.Graphics.Extensions");

    /// <summary>
    /// Checks if the result code indicates success.
    /// </summary>
    /// <param name="result">The result code to check.</param>
    /// <returns>True if the result is <see cref="ResultCode.Ok"/>; otherwise, false.</returns>
    public static bool Ok(this ResultCode result)
    {
        return result == ResultCode.Ok;
    }

    /// <summary>
    /// Checks if the result code indicates an error.
    /// </summary>
    /// <param name="result">The result code to check.</param>
    /// <returns>True if the result is not <see cref="ResultCode.Ok"/>; otherwise, false.</returns>
    public static bool HasError(this ResultCode result)
    {
        return result != ResultCode.Ok;
    }

    /// <summary>
    /// Checks if a native pointer is null (IntPtr.Zero).
    /// </summary>
    /// <param name="ptr">The pointer to check.</param>
    /// <returns>True if the pointer is IntPtr.Zero; otherwise, false.</returns>
    public static bool IsNull(this nint ptr)
    {
        return ptr == IntPtr.Zero;
    }

    /// <summary>
    /// Checks if a native pointer is valid (not IntPtr.Zero).
    /// </summary>
    /// <param name="ptr">The pointer to check.</param>
    /// <returns>True if the pointer is not IntPtr.Zero; otherwise, false.</returns>
    public static bool Valid(this nint ptr)
    {
        return ptr != IntPtr.Zero;
    }

    /// <summary>
    /// Validates a result code and throws an exception or logs an error if it indicates failure.
    /// </summary>
    /// <param name="result">The result code to check.</param>
    /// <param name="message">Optional error message to include. Defaults to "Operation failed".</param>
    /// <returns>The original result code for chaining.</returns>
    /// <remarks>
    /// In DEBUG builds, this method throws an <see cref="InvalidOperationException"/> on error.
    /// In RELEASE builds, it logs the error instead.
    /// </remarks>
    public static ResultCode CheckResult(this ResultCode result, string message = "Operation failed")
    {

        if (result.HasError())
        {
#if DEBUG
            throw new InvalidOperationException($"{message}: {result}");
#else
            logger.LogError("{MESSAGE}: {RESULT}", message, result);
#endif
        }
        return result;
    }

    /// <summary>
    /// Converts a semicolon-separated string of shader defines into an array of <see cref="ShaderDefine"/> structures.
    /// </summary>
    /// <param name="defines">A string containing shader defines in the format "NAME=VALUE;NAME2=VALUE2" or "NAME;NAME2".</param>
    /// <returns>An array of <see cref="ShaderDefine"/> structures, or an empty array if the input is null or empty.</returns>
    /// <exception cref="ArgumentException">Thrown if a define has an invalid format.</exception>
    /// <remarks>
    /// Defines can be in the format "NAME" (no value) or "NAME=VALUE" (with value).
    /// Multiple defines are separated by semicolons.
    /// </remarks>
    public static ShaderDefine[] ToShaderDefines(this string defines)
    {
        if (string.IsNullOrEmpty(defines))
        {
            return [];
        }
        var parts = defines.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var shaderDefines = new ShaderDefine[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var defineParts = parts[i].Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (defineParts.Length == 1)
            {
                shaderDefines[i] = new ShaderDefine(defineParts[0]);
            }
            else if (defineParts.Length == 2)
            {
                shaderDefines[i] = new ShaderDefine(defineParts[0], defineParts[1]);
            }
            else
            {
                throw new ArgumentException($"Invalid shader define format: {parts[i]}");
            }
        }
        return shaderDefines;
    }
}
