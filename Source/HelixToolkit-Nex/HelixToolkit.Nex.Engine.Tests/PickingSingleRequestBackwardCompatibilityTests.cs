using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.DependencyInjection;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Backward-compatibility integration test for the single-request picking path.
/// <para>
/// Feature: multi-request-picking. Verifies Requirements 7.2 and 7.3: a single picking request
/// issued in a frame delivers its Picking Result to its callback with the same observable behavior
/// as before this feature, and issuing one request per frame across successive frames delivers each
/// request's result to its own callback.
/// </para>
/// <para>
/// The test drives the real <see cref="Engine"/> pipeline end-to-end
/// (<c>CreatePickingRequest</c> → record the copy via <c>RenderOffscreen</c> → <c>Submit</c> pairs
/// the submit handle → next <c>BeginFrame</c> delivers). A faked <see cref="IContext"/> returns a
/// known entity-id value from the staging buffer so the delivered <see cref="PickingResponse"/> can
/// be asserted exactly, matching the pre-feature single-request result.
/// </para>
/// </summary>
[TestClass]
public class PickingSingleRequestBackwardCompatibilityTests
{
    /// <summary>
    /// The canned entity-id value the faked context reports for every picking readback. A single
    /// request reads Byte Offset 0 of its staging buffer, so this is the value its callback must
    /// receive in <see cref="PickingResponse.Data"/>.
    /// </summary>
    private const ulong CannedEntityId = 0xABCD_1234_0000_0001UL;

    /// <summary>
    /// A faked <see cref="IContext"/> whose <see cref="GetBufferSubData"/> returns a fixed, known
    /// entity-id value regardless of buffer contents. This lets the test assert the exact value
    /// delivered to a single request's callback without a live GPU, since <c>MockContext</c>'s
    /// default <c>GetBufferSubData</c> leaves the destination untouched.
    /// </summary>
    private sealed class CannedEntityIdContext : MockContext
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
                unchecked((long)CannedEntityId)
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
    /// Installs a 64×64 entity-id texture into the render context's resource set. Called after a
    /// warm-up <c>RenderOffscreen</c> so the render-graph's first-run resource creation (which
    /// clears the texture table) does not wipe it.
    /// </summary>
    private static void InstallEntityIdTexture(MockContext mock, RenderContext rc)
    {
        var entityTex = ((IContext)mock).CreateTexture2D(
            Format.RG_F32,
            width: 64,
            height: 64,
            usage: TextureUsageBits.Attachment | TextureUsageBits.Sampled,
            storage: StorageType.Device,
            debugName: SystemBufferNames.TextureEntityId
        );
        rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] = entityTex;
    }

    /// <summary>
    /// Requirement 7.2: a single picking request issued in a frame is accepted, recorded, submitted,
    /// and its Picking Result is delivered to its callback exactly once — reproducing the pre-feature
    /// single-request behavior. The delivered <see cref="PickingResponse"/> carries the request's own
    /// Request Id, the requested coordinate, and the entity-id value read from its staging cell.
    /// </summary>
    [TestMethod]
    public void SingleRequest_DeliversResultToCallbackOnce()
    {
        using var mock = new CannedEntityIdContext();
        mock.Initialize();

        var engine = CreateEngine(mock);
        using var rc = engine.CreateRenderContext();

        // Warm up render-graph resources once before installing the entity-id texture, so the real
        // frame's EnsureResources does not clear the texture we install (CreateAllResources runs
        // only on the first EnsureResources for this resource set).
        engine.RenderOffscreen(rc, null!, TextureHandle.Null);
        InstallEntityIdTexture(mock, rc);

        engine.BeginFrame();

        var invocations = 0;
        PickingResponse received = default;
        var pickCoord = new Vector2(12, 34);

        var requestId = engine.CreatePickingRequest(
            rc,
            pickCoord,
            response =>
            {
                invocations++;
                received = response;
            }
        );

        Assert.AreNotEqual(
            RenderContext.InvalidPickingRequestId,
            requestId,
            "A single picking request must be accepted."
        );

        // Record the copy and submit so the readback becomes deliverable on the next BeginFrame.
        var cmd = engine.RenderOffscreen(rc, null!, TextureHandle.Null);
        engine.Submit(cmd, TextureHandle.Null);

        // Delivery happens on the next BeginFrame.
        engine.BeginFrame();

        Assert.AreEqual(1, invocations, "The single request's callback must be invoked exactly once.");
        Assert.AreEqual(requestId, received.RequestId, "The delivered result must carry its own Request Id.");
        Assert.AreEqual(pickCoord, received.Coord, "The delivered result must carry the requested coordinate.");
        Assert.AreEqual(CannedEntityId, received.Data, "The delivered result must carry the entity-id value read from the request's staging cell.");

        // Delivered exactly once: a later BeginFrame must not re-invoke the callback.
        engine.BeginFrame();
        Assert.AreEqual(1, invocations, "A delivered single request must not be re-delivered on a later frame.");
    }

    /// <summary>
    /// Requirement 7.3: issuing one picking request per frame across successive frames delivers each
    /// request's Picking Result to its own callback. Each frame issues a single request at a distinct
    /// coordinate, records + submits it, and the following frame delivers it; every request's callback
    /// fires exactly once with its own Request Id and coordinate.
    /// </summary>
    [TestMethod]
    public void SingleRequestPerFrame_AcrossSuccessiveFrames_DeliversEach()
    {
        using var mock = new CannedEntityIdContext();
        mock.Initialize();

        var engine = CreateEngine(mock);
        using var rc = engine.CreateRenderContext();

        // Warm up before installing the entity-id texture (see SingleRequest_DeliversResultToCallbackOnce).
        engine.RenderOffscreen(rc, null!, TextureHandle.Null);
        InstallEntityIdTexture(mock, rc);

        var coords = new[]
        {
            new Vector2(1, 2),
            new Vector2(10, 20),
            new Vector2(40, 50),
        };

        var invocationsById = new Dictionary<uint, int>();
        var coordById = new Dictionary<uint, Vector2>();

        // Pipeline each frame delivers the previous frame's request: issue+record+submit in frame N,
        // then BeginFrame N+1 delivers it. Drive one extra BeginFrame at the end to flush the last.
        for (var frame = 0; frame < coords.Length; frame++)
        {
            engine.BeginFrame();

            var coord = coords[frame];
            var requestId = engine.CreatePickingRequest(
                rc,
                coord,
                response =>
                {
                    invocationsById.TryGetValue(response.RequestId, out var count);
                    invocationsById[response.RequestId] = count + 1;
                    coordById[response.RequestId] = response.Coord;
                    Assert.AreEqual(
                        CannedEntityId,
                        response.Data,
                        "Each single request must read the entity-id value from its own staging cell."
                    );
                }
            );

            Assert.AreNotEqual(
                RenderContext.InvalidPickingRequestId,
                requestId,
                "Each per-frame single picking request must be accepted."
            );

            var cmd = engine.RenderOffscreen(rc, null!, TextureHandle.Null);
            engine.Submit(cmd, TextureHandle.Null);
        }

        // Flush delivery of the final frame's request.
        engine.BeginFrame();

        Assert.AreEqual(
            coords.Length,
            invocationsById.Count,
            "Every per-frame request should have delivered its result to its own callback."
        );
        foreach (var (requestId, count) in invocationsById)
        {
            Assert.AreEqual(1, count, $"Request {requestId} must be delivered exactly once.");
        }
        // Each distinct request id maps back to a distinct issued coordinate.
        CollectionAssert.AreEquivalent(coords, coordById.Values.ToList());
    }
}
