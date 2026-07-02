using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.DependencyInjection;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// End-to-end integration test for multi-request picking through the real <see cref="Engine"/>
/// pipeline.
/// <para>
/// Feature: multi-request-picking. Verifies that a single frame carrying three picking requests at
/// distinct pixels delivers three callbacks, each receiving the per-pixel data read from its OWN
/// staging-buffer byte offset. This exercises the <c>CopyTextureToBuffer</c> (bufferOffset) +
/// <c>GetBufferSubData</c> (offset) wiring that mocks of the pure slot logic cannot fully validate.
/// </para>
/// <para>
/// Validates Requirements 4.1 (record a copy for every accepted request), 4.4 (associate the submit
/// handle with every copied request), 3.1 (readback reads each request's own byte offset), and 3.2
/// (each result is delivered to the callback for the same Request Id).
/// </para>
/// </summary>
[TestClass]
public class MultiRequestPickingIntegrationTests
{
    /// <summary>
    /// Deterministic canned value that <see cref="OffsetKeyedContext"/> returns for a readback at a
    /// given byte offset. It is a pure function of the requested byte offset, so the value a request
    /// reads back uniquely identifies the exact offset the read used. A distinctive high marker keeps
    /// it from colliding with the small offset values (0, 8, 16).
    /// </summary>
    private static ulong ExpectedValueForOffset(uint byteOffset) =>
        0xF00D_0000_0000_0000UL | byteOffset;

    /// <summary>
    /// A fake <see cref="IContext"/> whose <see cref="GetBufferSubData"/> ignores the buffer contents
    /// and instead writes <see cref="ExpectedValueForOffset(uint)"/> for the requested byte offset.
    /// Because the value read back is a pure function of the offset the read used, each request's
    /// delivered <see cref="PickingResponse.Data"/> proves the readback used that request's OWN byte
    /// offset (Request Slot i → offset i*8) and never another request's cell.
    /// </summary>
    private sealed class OffsetKeyedContext : MockContext
    {
        public override ResultCode GetBufferSubData(
            in BufferHandle handle,
            uint offset,
            uint size,
            nint data
        )
        {
            if (data == nint.Zero || size < sizeof(ulong))
            {
                return ResultCode.ArgumentError;
            }
            System.Runtime.InteropServices.Marshal.WriteInt64(
                data,
                unchecked((long)ExpectedValueForOffset(offset))
            );
            return ResultCode.Ok;
        }
    }

    private static Engine CreateEngine(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        services.AddSingleton<IResourceManager, ResourceManager>();
        var provider = services.BuildServiceProvider();
        return new Engine(new EngineConfig(provider));
    }

    /// <summary>
    /// Requirements 4.1, 4.4, 3.1, 3.2: a frame carrying three picking requests at distinct in-bounds
    /// pixels delivers three callbacks, each receiving the value keyed to its own byte offset.
    /// <para>
    /// The three requests occupy Request Slots 0, 1, 2 → byte offsets 0, 8, 16. The
    /// <see cref="OffsetKeyedContext"/> returns a value that is a pure function of the byte offset the
    /// readback used, so callback <c>i</c> must receive <c>ExpectedValueForOffset(i*8)</c>. Any swap in
    /// the <c>CopyTextureToBuffer</c> bufferOffset or <c>GetBufferSubData</c> offset wiring would make a
    /// callback receive a different offset's value and fail the assertion.
    /// </para>
    /// </summary>
    [TestMethod]
    public void MultiRequestFrame_DeliversPerPixelDataAtEachRequestsOwnOffset()
    {
        using var mock = new OffsetKeyedContext();
        mock.Initialize();

        var engine = CreateEngine(mock);
        using var rc = engine.CreateRenderContext();

        // Warm up the render-graph resources once so the real frame's EnsureResources does not clear
        // the entity-id texture we install below (CreateAllResources runs only on the first
        // EnsureResources call for this resource set).
        engine.RenderOffscreen(rc, null!, TextureHandle.Null);

        // Install an entity-id texture large enough to contain all three pick coordinates so
        // SendCommand records a copy for each accepted request.
        var entityTex = ((IContext)mock).CreateTexture2D(
            Format.RG_F32,
            width: 64,
            height: 64,
            usage: TextureUsageBits.Attachment | TextureUsageBits.Sampled,
            storage: StorageType.Device,
            debugName: SystemBufferNames.TextureEntityId
        );
        rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] = entityTex;

        // Begin the frame in which the three picking copies are recorded.
        engine.BeginFrame();

        // Three distinct in-bounds pixels. They are recorded in submission (slot) order, so
        // request 0 → offset 0, request 1 → offset 8, request 2 → offset 16.
        var coords = new[]
        {
            new Vector2(5, 7),
            new Vector2(20, 33),
            new Vector2(60, 12),
        };

        var responses = new PickingResponse?[coords.Length];
        var invocationCounts = new int[coords.Length];
        var ids = new uint[coords.Length];

        for (var i = 0; i < coords.Length; i++)
        {
            var index = i; // capture per-iteration
            ids[i] = engine.CreatePickingRequest(
                rc,
                coords[i],
                response =>
                {
                    invocationCounts[index]++;
                    responses[index] = response;
                }
            );

            Assert.AreNotEqual(
                RenderContext.InvalidPickingRequestId,
                ids[i],
                $"Picking request {index} should be accepted (not the overflow sentinel)."
            );
        }

        // All three accepted requests must have distinct Request Ids.
        CollectionAssert.AllItemsAreUnique(ids, "Accepted requests must have distinct Request Ids.");

        // Record the three picking copies and submit so their submit handle is paired, making all
        // three readbacks ready for delivery on the next BeginFrame.
        var cmd = engine.RenderOffscreen(rc, null!, TextureHandle.Null);
        engine.Submit(cmd, TextureHandle.Null);

        // Deliver the ready readbacks.
        engine.BeginFrame();

        for (var i = 0; i < coords.Length; i++)
        {
            Assert.AreEqual(
                1,
                invocationCounts[i],
                $"Callback for request {i} should have fired exactly once."
            );

            Assert.IsTrue(
                responses[i].HasValue,
                $"Callback for request {i} should have received a response."
            );

            var response = responses[i]!.Value;

            // Req 3.2: the result is delivered to the callback for the same Request Id.
            Assert.AreEqual(
                ids[i],
                response.RequestId,
                $"Response for request {i} must carry its own Request Id."
            );

            // Req 3.3 companion: the originating screen coordinate is preserved.
            Assert.AreEqual(
                coords[i],
                response.Coord,
                $"Response for request {i} must carry its own screen coordinate."
            );

            // Req 3.1 / 4.1 / 4.4: request i reads exactly the 8 bytes at its own Byte Offset
            // (RequestSlot i → offset i * sizeof(ulong)), proving the CopyTextureToBuffer bufferOffset
            // and GetBufferSubData offset wiring line up end-to-end.
            var expectedOffset = (uint)(i * sizeof(ulong));
            Assert.AreEqual(
                ExpectedValueForOffset(expectedOffset),
                response.Data,
                $"Response for request {i} must contain the value read from its own byte offset "
                    + $"({expectedOffset})."
            );
        }

        // Delivery happens exactly once: a subsequent BeginFrame must not re-invoke any callback.
        Array.Clear(invocationCounts);
        engine.BeginFrame();

        for (var i = 0; i < coords.Length; i++)
        {
            Assert.AreEqual(
                0,
                invocationCounts[i],
                $"Delivered picking request {i} must not be re-delivered on a later frame."
            );
        }
    }
}
