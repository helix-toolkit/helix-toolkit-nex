using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Rendering.RenderNodes;
using Microsoft.Extensions.DependencyInjection;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Example / integration tests for the engine-hosted gizmo service lifecycle.
/// <para>
/// Feature: gizmo-factory-engine-integration. Drives the real <see cref="Engine"/> gizmo hosting
/// (<see cref="Engine.Gizmos"/>) end to end to verify the Requirement 4 lifecycle and wiring
/// contract that the property tests do not cover:
/// </para>
/// <list type="bullet">
/// <item>Repeated access returns the same non-null service instance (Requirements 4.1, 4.5).</item>
/// <item>Gizmos can be created, updated, and removed through the service (Requirement 4.5).</item>
/// <item>The first gizmo creation registers a <see cref="GizmoRenderNode"/> without manual wiring,
/// and doing so again does not add a second node (Requirement 4.6).</item>
/// <item>Disposing the engine disposes the service, clears every tracked gizmo, and causes the
/// <see cref="Engine.Gizmos"/> accessor and any retained service operation to throw
/// <see cref="ObjectDisposedException"/> (Requirements 4.2, 4.3).</item>
/// </list>
/// </summary>
[TestClass]
public class EngineGizmoServiceLifecycleTests
{
    /// <summary>
    /// Builds an <see cref="Engine"/> backed by an initialized <see cref="MockContext"/>, mirroring
    /// the construction pattern the other engine integration tests use.
    /// </summary>
    private static Engine CreateEngine(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        services.AddSingleton<IResourceManager, ResourceManager>();
        var provider = services.BuildServiceProvider();
        return new Engine(new EngineConfig(provider));
    }

    /// <summary>A well-formed translate gizmo definition used across the lifecycle tests.</summary>
    private static GizmoDefinition SampleDefinition() =>
        new(
            GizmoMode.Translate,
            GizmoSpace.World,
            TargetEntityId: 1,
            new GizmoHandleConfiguration(DesiredPixelSize: 100f, GizmoOcclusionMode.AlwaysOnTop)
        );

    /// <summary>
    /// Requirements 4.1, 4.5: the engine exposes a single non-null gizmo service and returns that
    /// same instance on every access for the engine's lifetime.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Gizmos_ReturnsSameNonNullInstance_AcrossRepeatedAccesses()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var engine = CreateEngine(mock);

        GizmoManager first = engine.Gizmos;
        GizmoManager second = engine.Gizmos;
        GizmoManager third = engine.Gizmos;

        Assert.IsNotNull(first, "The engine must expose a non-null gizmo service (Requirement 4.1).");
        Assert.AreSame(
            first,
            second,
            "Repeated Gizmos access must return the same service instance (Requirements 4.1, 4.5)."
        );
        Assert.AreSame(
            first,
            third,
            "Repeated Gizmos access must return the same service instance (Requirements 4.1, 4.5)."
        );
    }

    /// <summary>
    /// Requirement 4.5: the application can create, update, and remove a gizmo through the
    /// engine-hosted service, observing the create/publish/update/remove outcomes on the carrier
    /// entity's <see cref="GizmoDrawInfo"/> component.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Gizmos_SupportsCreateUpdateRemove_ThroughService()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var engine = CreateEngine(mock);
        using World world = World.CreateWorld();

        GizmoManager service = engine.Gizmos;

        // Create: the factory returns a valid handle for a well-formed definition.
        Assert.IsTrue(
            service.TryCreateGizmo(SampleDefinition(), out GizmoInstanceHandle handle),
            "Creating a gizmo through the engine service should succeed (Requirement 4.5)."
        );
        Assert.IsTrue(handle.IsValid, "A created gizmo should yield a valid instance handle.");

        // Set: associating the handle with an entity publishes a GizmoDrawInfo component.
        Entity entity = world.CreateEntity();
        Assert.IsTrue(
            service.TrySetGizmo(entity, handle),
            "Setting a created gizmo on an entity should succeed (Requirement 4.5)."
        );
        Assert.IsTrue(
            entity.TryGet(out GizmoDrawInfo published),
            "Setting a gizmo should publish a GizmoDrawInfo component on the entity."
        );
        Assert.IsTrue(published.Valid, "The published gizmo component should carry a non-empty handle set.");

        // Update: driving the per-instance frame updates the published origin in place.
        var origin = new Vector3(3f, 4f, 5f);
        service.UpdateInstance(
            handle,
            CameraParams.Identity,
            Size.Empty,
            Matrix4x4.CreateTranslation(origin)
        );
        Assert.IsTrue(
            entity.TryGet(out GizmoDrawInfo afterUpdate),
            "The gizmo component should still be present after an update."
        );
        Assert.AreEqual(
            origin,
            afterUpdate.Origin,
            "Updating the instance should refresh the published origin (Requirement 4.5)."
        );

        // Remove: removing the gizmo drops the component from the entity.
        Assert.IsTrue(
            service.RemoveGizmo(handle),
            "Removing a tracked gizmo through the service should succeed (Requirement 4.5)."
        );
        Assert.IsFalse(
            entity.TryGet(out GizmoDrawInfo _),
            "Removing a gizmo should remove its GizmoDrawInfo component from the entity (Requirement 4.5)."
        );
    }

    /// <summary>
    /// Requirement 4.6: the first access of the gizmo service auto-registers a
    /// <see cref="GizmoRenderNode"/> without the application wiring it manually, and further access /
    /// gizmo creation is idempotent — no second node is added.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Gizmos_RegistersGizmoRenderNode_OnFirstAccess_AndIsIdempotent()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var engine = CreateEngine(mock);

        // Before touching the service, no gizmo render node exists.
        Assert.IsNull(
            engine.GetRenderNode<GizmoRenderNode>(),
            "No GizmoRenderNode should be registered before the gizmo service is used."
        );

        // First access lazily creates the service and registers the render node (Requirement 4.6).
        GizmoManager service = engine.Gizmos;
        GizmoRenderNode? node = engine.GetRenderNode<GizmoRenderNode>();
        Assert.IsNotNull(
            node,
            "The first gizmo service access should register a GizmoRenderNode automatically (Requirement 4.6)."
        );

        // Creating gizmos and re-accessing the service must not add a second node (idempotent).
        Assert.IsTrue(service.TryCreateGizmo(SampleDefinition(), out GizmoInstanceHandle handle));
        Assert.IsTrue(handle.IsValid);
        _ = engine.Gizmos;

        Assert.AreSame(
            node,
            engine.GetRenderNode<GizmoRenderNode>(),
            "Re-accessing the service or creating gizmos must not register a second GizmoRenderNode (Requirement 4.6)."
        );
    }

    /// <summary>
    /// Requirements 4.2, 4.3: disposing the engine disposes the gizmo service, stops tracking every
    /// gizmo it owned (the carrier entity's component is removed), and makes the
    /// <see cref="Engine.Gizmos"/> accessor and any retained service operation throw
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Dispose_ClearsTracking_AndGizmosAndServiceOpsThrowObjectDisposed()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using World world = World.CreateWorld();

        var engine = CreateEngine(mock);

        // Initialize so engine teardown (which disposes the gizmo service) runs on Dispose.
        Assert.AreEqual(
            ResultCode.Ok,
            engine.Initialize(),
            "The engine should initialize with the mock context so teardown runs on dispose."
        );

        // Retain a reference to the service and track a gizmo on an entity before disposal.
        GizmoManager service = engine.Gizmos;
        Assert.IsTrue(service.TryCreateGizmo(SampleDefinition(), out GizmoInstanceHandle handle));
        Entity entity = world.CreateEntity();
        Assert.IsTrue(service.TrySetGizmo(entity, handle));
        Assert.IsTrue(
            entity.TryGet(out GizmoDrawInfo _),
            "Precondition: the tracked gizmo publishes a component before disposal."
        );

        // Dispose the engine: it disposes the service and clears tracked gizmos (Requirement 4.2).
        engine.Dispose();

        Assert.IsTrue(engine.IsDisposed, "The engine should report disposed after Dispose.");
        Assert.IsTrue(
            service.IsDisposed,
            "Disposing the engine should dispose the gizmo service (Requirement 4.2)."
        );

        // Tracking is cleared: the carrier entity no longer carries the gizmo component.
        Assert.IsFalse(
            entity.TryGet(out GizmoDrawInfo _),
            "Disposing the engine should stop tracking every gizmo, removing its component (Requirement 4.2)."
        );

        // The accessor rejects use after disposal (Requirement 4.3).
        Assert.ThrowsException<ObjectDisposedException>(
            () => _ = engine.Gizmos,
            "Accessing Engine.Gizmos after disposal should throw ObjectDisposedException (Requirement 4.3)."
        );

        // Retained service operations reject use after disposal (Requirement 4.3).
        Assert.ThrowsException<ObjectDisposedException>(
            () => service.TryCreateGizmo(SampleDefinition(), out _),
            "Using the service after engine disposal should throw ObjectDisposedException (Requirement 4.3)."
        );
        Assert.ThrowsException<ObjectDisposedException>(
            () => service.RemoveGizmo(handle),
            "Using the service after engine disposal should throw ObjectDisposedException (Requirement 4.3)."
        );
    }
}
