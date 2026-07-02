using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.DependencyInjection;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Unit test for callback exception isolation during picking readback delivery.
/// <para>
/// Feature: multi-request-picking. Verifies Requirement 4.7: when a picking request's callback
/// throws during <see cref="Engine.BeginFrame"/> delivery, the engine catches and logs the
/// exception and still delivers the remaining pending picking results in the same frame.
/// </para>
/// </summary>
[TestClass]
public class PickingCallbackExceptionIsolationTests
{
    /// <summary>
    /// Builds an <see cref="Engine"/> backed by a <see cref="MockContext"/>. The mock context
    /// satisfies the GPU-facing calls the picking pipeline needs: <c>Submit</c> returns a handle,
    /// <c>IsReady</c> reports completion immediately, and <c>GetBufferSubData</c> returns
    /// <see cref="ResultCode.Ok"/>, so a recorded + submitted picking copy is deliverable on the
    /// next <see cref="Engine.BeginFrame"/>.
    /// </summary>
    private static Engine CreateEngine(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        services.AddSingleton<IResourceManager, ResourceManager>();
        var provider = services.BuildServiceProvider();
        return new Engine(new EngineConfig(provider));
    }

    /// <summary>
    /// Requirement 4.7: a throwing callback for one picking request is caught and logged, and does
    /// NOT prevent delivery of the other pending request accepted in the same frame.
    /// <para>
    /// The test drives two picking requests through the real Engine pipeline
    /// (<c>CreatePickingRequest</c> → record copies via <c>RenderOffscreen</c> →
    /// <c>Submit</c> pairs the submit handle → next <c>BeginFrame</c> delivers). The first
    /// request's callback throws; the second records that it ran. After delivery the non-throwing
    /// callback must have been invoked, proving the exception was isolated.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ThrowingCallback_DoesNotPreventDeliveryOfOtherPendingRequests()
    {
        using var mock = new MockContext();
        mock.Initialize();

        var engine = CreateEngine(mock);
        using var rc = engine.CreateRenderContext();

        // Warm up the render-graph resources once so the real frame's EnsureResources does not
        // clear the entity-id texture we install below (CreateAllResources runs only on the first
        // EnsureResources call for this resource set). The render graph is empty and the default
        // 1x1 window makes Renderer.Render a no-op, so this only creates (empty) graph resources.
        engine.RenderOffscreen(rc, null!, TextureHandle.Null);

        // Install an entity-id texture large enough to contain both pick coordinates so
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

        // Begin the frame in which the two picking copies are recorded.
        engine.BeginFrame();

        var throwingCallbackInvoked = false;
        var survivingCallbackInvoked = false;

        // First request: its callback throws during delivery.
        var throwingId = engine.CreatePickingRequest(
            rc,
            new Vector2(10, 10),
            _ =>
            {
                throwingCallbackInvoked = true;
                throw new InvalidOperationException("Simulated callback failure.");
            }
        );

        // Second request: its callback must still run despite the first one throwing.
        var survivingId = engine.CreatePickingRequest(
            rc,
            new Vector2(20, 20),
            _ => survivingCallbackInvoked = true
        );

        // Both requests must have been accepted (not the overflow sentinel) and be distinct.
        Assert.AreNotEqual(
            RenderContext.InvalidPickingRequestId,
            throwingId,
            "First picking request should be accepted."
        );
        Assert.AreNotEqual(
            RenderContext.InvalidPickingRequestId,
            survivingId,
            "Second picking request should be accepted."
        );
        Assert.AreNotEqual(throwingId, survivingId, "Accepted requests must have distinct ids.");

        // Record the picking copies for both requests and submit so their submit handle is paired,
        // making both readbacks ready for delivery on the next BeginFrame.
        var cmd = engine.RenderOffscreen(rc, null!, TextureHandle.Null);
        engine.Submit(cmd, TextureHandle.Null);

        // Deliver the ready readbacks. The first callback throws (caught + logged); the second must
        // still be delivered in the same BeginFrame.
        engine.BeginFrame();

        Assert.IsTrue(
            throwingCallbackInvoked,
            "The throwing callback should have been invoked (and its exception caught)."
        );
        Assert.IsTrue(
            survivingCallbackInvoked,
            "The non-throwing callback must still be delivered even though the other callback threw."
        );

        // Both entries were delivered and removed: a subsequent BeginFrame must not re-invoke
        // either callback (delivery happens exactly once).
        survivingCallbackInvoked = false;
        throwingCallbackInvoked = false;
        engine.BeginFrame();

        Assert.IsFalse(
            survivingCallbackInvoked,
            "Delivered picking requests must not be re-delivered on a later frame."
        );
        Assert.IsFalse(
            throwingCallbackInvoked,
            "Delivered picking requests must not be re-delivered on a later frame."
        );
    }
}
