namespace HelixToolkit.Nex.Graphics;

public static class Extensions
{
    static readonly ILogger logger = LogManager.Create("HelixToolkit.Nex.Graphics.Extensions");
    public static bool Ok(this ResultCode result)
    {
        return result == ResultCode.Ok;
    }

    public static bool HasError(this ResultCode result)
    {
        return result != ResultCode.Ok;
    }

    public static bool IsNull(this nint ptr)
    {
        return ptr == IntPtr.Zero;
    }

    public static bool Valid(this nint ptr)
    {
        return ptr != IntPtr.Zero;
    }

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
