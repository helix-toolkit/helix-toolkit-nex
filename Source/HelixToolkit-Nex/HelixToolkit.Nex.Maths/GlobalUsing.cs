global using System.Globalization;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Numerics;
global using Matrix = System.Numerics.Matrix4x4;
#if NET6_0_OR_GREATER
global using System.Runtime.Intrinsics.X86;
global using System.Runtime.Intrinsics;
global using size_t = uint;
#endif
namespace HelixToolkit.Nex.Maths;
public static class MathSettings
{
    public static bool EnableSIMD = true;
}
