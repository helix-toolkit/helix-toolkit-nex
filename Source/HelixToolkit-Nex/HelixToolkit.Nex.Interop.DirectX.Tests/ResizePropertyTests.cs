using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Feature: wpf-winui-integration, Property 5: Resize updates WindowSize and FinalOutputTexture
/// Validates: Requirements 4.8, 5.8, 8.2
/// </summary>
[TestClass]
[TestCategory("GPU")]
public class ResizePropertyTests
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
    /// Property 5: For any valid resize dimensions (width > 0, height > 0),
    /// setting RenderContext.WindowSize to the new dimensions and setting
    /// FinalOutputTexture to a valid handle results in WindowSize equaling
    /// the new dimensions and FinalOutputTexture being non-null.
    /// **Validates: Requirements 4.8, 5.8, 8.2**
    /// </summary>
    [TestMethod]
    public void Resize_UpdatesWindowSizeAndFinalOutputTexture()
    {
        // Generate valid resize dimensions (width > 0, height > 0, capped at 16384)
        var dimensionGen =
            from width in Gen.Choose(1, 16384)
            from height in Gen.Choose(1, 16384)
            select (width, height);

        // Generate a valid (non-null) TextureHandle to simulate the imported texture
        var handleGen =
            from index in Gen.Choose(0, 100_000).Select(i => (uint)i)
            from generation in Gen.Choose(1, 100_000).Select(g => (uint)g)
            select new TextureHandle(index, generation);

        using var rc = CreateRenderContext();

        Prop.ForAll(
                Arb.From(dimensionGen),
                Arb.From(handleGen),
                ((int width, int height) dims, TextureHandle handle) =>
                {
                    // Simulate what the control does on resize:
                    // 1. Set WindowSize to new dimensions
                    rc.WindowSize = new Size(dims.width, dims.height);
                    // 2. Set FinalOutputTexture to the newly imported texture handle
                    rc.FinalOutputTexture = handle;

                    // Verify WindowSize matches the new dimensions
                    var sizeMatches =
                        rc.WindowSize.Width == dims.width && rc.WindowSize.Height == dims.height;

                    // Verify FinalOutputTexture is valid (non-null)
                    var textureIsValid = rc.FinalOutputTexture != TextureHandle.Null;

                    return sizeMatches && textureIsValid;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 5 (consecutive resizes): The last resize dimensions are always
    /// reflected in WindowSize, simulating multiple resize events.
    /// **Validates: Requirements 4.8, 5.8, 8.2**
    /// </summary>
    [TestMethod]
    public void Resize_LastResizeWins()
    {
        var dimensionGen =
            from width in Gen.Choose(1, 16384)
            from height in Gen.Choose(1, 16384)
            select (width, height);

        var handleGen =
            from index in Gen.Choose(0, 100_000).Select(i => (uint)i)
            from generation in Gen.Choose(1, 100_000).Select(g => (uint)g)
            select new TextureHandle(index, generation);

        using var rc = CreateRenderContext();

        Prop.ForAll(
                Arb.From(dimensionGen),
                Arb.From(dimensionGen),
                Arb.From(handleGen),
                (
                    (int width, int height) first,
                    (int width, int height) second,
                    TextureHandle handle
                ) =>
                {
                    // First resize
                    rc.WindowSize = new Size(first.width, first.height);
                    rc.FinalOutputTexture = TextureHandle.Null;

                    // Second resize overwrites
                    rc.WindowSize = new Size(second.width, second.height);
                    rc.FinalOutputTexture = handle;

                    var sizeMatches =
                        rc.WindowSize.Width == second.width
                        && rc.WindowSize.Height == second.height;

                    var textureIsValid = rc.FinalOutputTexture != TextureHandle.Null;

                    return sizeMatches && textureIsValid;
                }
            )
            .Check(FsCheckConfig);
    }
}
