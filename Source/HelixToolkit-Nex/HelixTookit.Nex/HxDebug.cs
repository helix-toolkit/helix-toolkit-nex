using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex;

public sealed class HxDebug
{
    static readonly ILogger logger = LogManager.Create<HxDebug>();

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