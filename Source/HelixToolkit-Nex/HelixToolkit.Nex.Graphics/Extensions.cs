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
}
