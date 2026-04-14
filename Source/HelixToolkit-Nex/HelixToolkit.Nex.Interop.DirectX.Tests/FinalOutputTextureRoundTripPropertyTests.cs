using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Rendering;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Feature: wpf-winui-integration, Property 4: FinalOutputTexture round-trip
/// Validates: Requirements 7.2
/// </summary>
[TestClass]
public class FinalOutputTextureRoundTripPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Creates a minimal RenderContext backed by a MockContext for pure-logic testing.
    /// </summary>
    private static RenderContext CreateRenderContext()
    {
        var mockContext = new MockContext();
        mockContext.Initialize();

        var services = new ServiceCollection
        {
            new ServiceDescriptor(typeof(IContext), mockContext),
        };
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    /// <summary>
    /// Property 4: For any valid TextureHandle, setting RenderContext.FinalOutputTexture
    /// and reading it back returns the same handle value.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [TestMethod]
    public void FinalOutputTexture_RoundTrip_PreservesHandle()
    {
        var handleGen =
            from index in Gen.Choose(0, 100_000).Select(i => (uint)i)
            from generation in Gen.Choose(1, 100_000).Select(g => (uint)g)
            select new TextureHandle(index, generation);

        using var rc = CreateRenderContext();

        Prop.ForAll(
                Arb.From(handleGen),
                (TextureHandle handle) =>
                {
                    rc.FinalOutputTexture = handle;
                    var readBack = rc.FinalOutputTexture;
                    return readBack == handle;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 4 (null handle): Setting FinalOutputTexture to Null and reading back returns Null.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [TestMethod]
    public void FinalOutputTexture_RoundTrip_NullHandle()
    {
        using var rc = CreateRenderContext();

        rc.FinalOutputTexture = TextureHandle.Null;
        var readBack = rc.FinalOutputTexture;

        Assert.AreEqual(TextureHandle.Null, readBack);
    }

    /// <summary>
    /// Property 4 (consecutive writes): The last written handle is always the one read back.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [TestMethod]
    public void FinalOutputTexture_RoundTrip_LastWriteWins()
    {
        var handleGen =
            from index in Gen.Choose(0, 100_000).Select(i => (uint)i)
            from generation in Gen.Choose(1, 100_000).Select(g => (uint)g)
            select new TextureHandle(index, generation);

        using var rc = CreateRenderContext();

        Prop.ForAll(
                Arb.From(handleGen),
                Arb.From(handleGen),
                (TextureHandle first, TextureHandle second) =>
                {
                    rc.FinalOutputTexture = first;
                    rc.FinalOutputTexture = second;
                    return rc.FinalOutputTexture == second;
                }
            )
            .Check(FsCheckConfig);
    }
}
