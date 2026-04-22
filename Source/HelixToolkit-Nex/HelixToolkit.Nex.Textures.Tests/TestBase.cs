using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Provides the shared FsCheck ArbMap with all custom generators registered.
/// Property-based tests should use <see cref="DefaultArbMap"/> when calling
/// <c>Prop.ForAll(...).QuickCheckThrowOnFailure()</c>.
/// </summary>
[TestClass]
public class TestBase
{
    /// <summary>
    /// Shared ArbMap with all custom generators registered.
    /// Use this in property tests: <c>Prop.ForAll(DefaultArbMap.ArbFor&lt;Format&gt;(), ...)</c>
    /// </summary>
    public static readonly IArbMap DefaultArbMap = BuildArbMap();

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext context)
    {
        // ArbMap is immutable in FsCheck 3.x; DefaultArbMap is built once at startup.
        // No global registration needed — pass DefaultArbMap to Prop.ForAll calls.
    }

    private static IArbMap BuildArbMap()
    {
        return ArbMap
            .Default.MergeArbFactory(() => Generators.NexFormatArb())
            .MergeArbFactory(() => Generators.ValidImageDescription());
    }
}
