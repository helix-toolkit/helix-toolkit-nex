using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Example (unit) tests for <see cref="PickingContext.SendCommand"/> when the entity-id texture is
/// missing (key absent from the resource set) or empty (key present but mapped to a null handle).
/// In both cases no copies are recorded and an empty list is returned.
/// </summary>
[TestClass]
public class PickingContextMissingTextureUnitTests
{
    private static RenderContext CreateRenderContext(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    /// <summary>
    /// Req 4.3: when the entity-id texture key is absent from the resource set, submitting a few
    /// requests and then recording records zero copies and returns an empty list.
    /// </summary>
    [TestMethod]
    public void SendCommand_MissingEntityIdTexture_RecordsNoCopiesAndReturnsEmpty()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var rc = CreateRenderContext(mock);

        // Submit a few in-bounds-looking picking requests; all accepted (well under capacity).
        var id1 = rc.SendPicking(new Vector2(1, 1));
        var id2 = rc.SendPicking(new Vector2(2, 2));
        var id3 = rc.SendPicking(new Vector2(3, 3));

        Assert.AreNotEqual(PickingContext.InvalidRequestId, id1);
        Assert.AreNotEqual(PickingContext.InvalidRequestId, id2);
        Assert.AreNotEqual(PickingContext.InvalidRequestId, id3);

        // Entity-id texture key is intentionally left absent from the resource set.
        var copied = rc.PickingContext.SendCommand(mock.AcquireCommandBuffer(), rc, frameSlot: 0);

        Assert.AreEqual(0, copied.Count, "Missing entity-id texture must record zero copies.");
    }

    /// <summary>
    /// Req 4.3: when the entity-id texture key is present but maps to an empty (null) texture
    /// handle, submitting a few requests and then recording records zero copies and returns an
    /// empty list.
    /// </summary>
    [TestMethod]
    public void SendCommand_EmptyEntityIdTexture_RecordsNoCopiesAndReturnsEmpty()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var rc = CreateRenderContext(mock);

        var id1 = rc.SendPicking(new Vector2(1, 1));
        var id2 = rc.SendPicking(new Vector2(2, 2));

        Assert.AreNotEqual(PickingContext.InvalidRequestId, id1);
        Assert.AreNotEqual(PickingContext.InvalidRequestId, id2);

        // Entity-id key present but mapped to a null texture handle => empty.
        rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] = TextureHandle.Null;

        var copied = rc.PickingContext.SendCommand(mock.AcquireCommandBuffer(), rc, frameSlot: 0);

        Assert.AreEqual(0, copied.Count, "Empty entity-id texture must record zero copies.");
    }
}
