using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.Extensions.DependencyInjection;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration — smoke tests for the simplified <c>GizmoTest</c> sample
/// flow (Requirement 8).
/// <para>
/// The sample (<c>Samples/Integration/GizmoTest/GizmoDemo.cs</c>) obtains its manager from
/// <see cref="Engine.Gizmos"/> and issues click picks with
/// <see cref="Engine.CreatePickingRequest(RenderContext, Vector2, Action{PickingResponse}, bool)"/>
/// using <c>routeGizmoPicks: true</c>. Its <c>OnClickPickResponse</c> callback classifies the delivered
/// <see cref="PickingResponse"/> with exactly three branches:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <see cref="GizmoManager.IsDragging"/> is <see langword="true"/> → the engine already routed a
///     decoded gizmo pick to the gizmo service and began the drag before the callback ran, so the
///     sample only reports "Dragging gizmo handle" and returns (Req 8.2). It never decodes the pixel or
///     resolves a scene entity for a gizmo pick.
///     </description>
///   </item>
///   <item>
///     <description>
///     otherwise <see cref="PickingResponse.TryGetPickingResult"/> succeeds → a scene pick resolves the
///     same scene entity manual classification resolved (Req 8.3) and no gizmo drag is begun (Req 8.4).
///     </description>
///   </item>
///   <item>
///     <description>
///     otherwise → a no-hit begins no drag and resolves no scene entity (Req 8.7).
///     </description>
///   </item>
/// </list>
/// <para>
/// The sample project is an executable and is not referenced by a test project, so these smoke tests
/// reproduce the sample's decision logic faithfully: they wire the engine's routing exactly as
/// <c>Engine.CreatePickingRequest(..., routeGizmoPicks: true)</c> does
/// (<c>new GizmoPickRouter(Gizmos).Wrap(callback, true)</c>) and drive a callback that mirrors
/// <c>OnClickPickResponse</c>'s three-branch logic. The core routing behaviour (Property 9) is covered
/// by <see cref="GizmoPickRoutingPropertyTests"/>; these tests assert the sample-level branch outcomes.
/// </para>
/// **Validates: Requirements 8.2, 8.3, 8.4, 8.7**
/// </summary>
[TestClass]
public sealed class GizmoSampleFlowSmokeTests
{
    /// <summary>Which branch of the sample's <c>OnClickPickResponse</c> the delivered result took.</summary>
    private enum SampleBranch
    {
        /// <summary>No callback observed yet.</summary>
        None,

        /// <summary>Branch 1: the gizmo service is dragging (gizmo pick was routed) — Req 8.2.</summary>
        Dragging,

        /// <summary>Branch 2: a scene entity was resolved — Req 8.3, 8.4.</summary>
        Scene,

        /// <summary>Branch 3: no hit — Req 8.7.</summary>
        NoHit,
    }

    private MockContext _mock = null!;
    private ServiceProvider _provider = null!;
    private RenderContext _validContext = null!;

    [TestInitialize]
    public void Setup()
    {
        _mock = new MockContext();
        _mock.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<IContext>(_mock);
        _provider = services.BuildServiceProvider();

        // A context with a real viewport + non-identity perspective camera so TryUnProject succeeds:
        // a resolved gizmo pick can derive its world ray (and begin the drag) and a scene pick can
        // unproject its coordinate — matching the sample's live render context.
        _validContext = new RenderContext(_provider) { WindowSize = new Size(800, 600) };
        _validContext.CameraParams = MakePerspectiveCamera(800f / 600f);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _validContext?.Dispose();
        _provider?.Dispose();
        _mock?.Dispose();
    }

    private static CameraParams MakePerspectiveCamera(float aspect)
    {
        Vector3 position = new(0f, 0f, 5f);
        Vector3 target = Vector3.Zero;
        Vector3 up = Vector3.UnitY;

        Matrix4x4 view = Matrix4x4.CreateLookAt(position, target, up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 100f);
        Matrix4x4.Invert(view, out Matrix4x4 invView);
        Matrix4x4.Invert(projection, out Matrix4x4 invProjection);

        return new CameraParams(view, projection, invView, invProjection, position, target, up, 0.1f, 100f);
    }

    /// <summary>
    /// Sets up a single tracked translate gizmo (World space) on a fresh entity in <paramref name="world"/>
    /// and returns the owning entity id and an X-axis (draggable) handle id, mirroring how the sample
    /// creates its gizmo through the factory and sets it on a carrier entity.
    /// </summary>
    private static bool TrySetupGizmo(
        World world,
        GizmoManager manager,
        out uint owningEntityId,
        out GizmoHandleId axisHandle)
    {
        owningEntityId = 0;
        axisHandle = default;

        var definition = new GizmoDefinition(
            GizmoMode.Translate,
            GizmoSpace.World,
            TargetEntityId: 0u,
            new GizmoHandleConfiguration(100f, GizmoOcclusionMode.AlwaysOnTop));

        if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
        {
            return false;
        }

        Entity entity = world.CreateEntity();
        if (!manager.TrySetGizmo(entity, handle) || !entity.TryGet(out GizmoDrawInfo published))
        {
            return false;
        }

        foreach (GizmoHandle candidate in published!.Handles)
        {
            if (candidate.Id.Axis == GizmoAxis.X)
            {
                axisHandle = candidate.Id;
                owningEntityId = (uint)entity.Id;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a minimal single-triangle mesh entity that <see cref="PickingResponse.TryGetPickingResult"/>
    /// can resolve (it carries a <see cref="MeshDrawInfo"/> with a real geometry and a primitive 0), so a
    /// packed scene pick for this entity resolves the same scene entity the sample would.
    /// </summary>
    private static Entity CreateSceneEntity(World world)
    {
        var vertices = new Vector4[]
        {
            new(0f, 0f, 0f, 1f),
            new(1f, 0f, 0f, 1f),
            new(0f, 1f, 0f, 1f),
        };
        var indices = new uint[] { 0u, 1u, 2u };
        var geometry = new Geometry(vertices, null, indices, null, Topology.Triangle);

        Entity entity = world.CreateEntity();
        entity.Set(new MeshDrawInfo(geometry));
        return entity;
    }

    /// <summary>Packs a resolved gizmo pick (owning entity + handle) into R/G picking data.</summary>
    private static ulong PackGizmo(uint owningEntityId, GizmoHandleId handle)
    {
        Utils.PackGizmoInfo(owningEntityId, handle, out uint r, out uint g);
        return r | ((ulong)g << 32);
    }

    /// <summary>Packs a scene pick (world id &gt; 0) into R/G picking data.</summary>
    private static ulong PackScene(uint worldId, uint entityId)
    {
        Utils.PackMeshInfo(worldId, entityId, instanceIndex: 0u, primitiveId: 0u, out uint r, out uint g);
        return r | ((ulong)g << 32);
    }

    /// <summary>
    /// Builds the sample-mirroring callback + its recorded state, wrapped through the engine's routing
    /// exactly as <c>Engine.CreatePickingRequest(..., routeGizmoPicks: true)</c> wires it.
    /// </summary>
    private static (Action<PickingResponse> Wrapped, Func<SampleBranch> Branch, Func<Entity> Resolved) BuildSampleFlow(
        GizmoManager manager)
    {
        var router = new GizmoPickRouter(manager);

        var branch = SampleBranch.None;
        Entity resolvedEntity = Entity.Null;

        // Faithful reproduction of GizmoDemo.OnClickPickResponse's three-branch logic.
        void OnClickPickResponse(PickingResponse response)
        {
            // Branch 1 (Req 8.2): the engine's routing already began the drag before this callback ran.
            if (manager.IsDragging)
            {
                branch = SampleBranch.Dragging;
                return;
            }

            // Branch 2 (Req 8.3, 8.4): a scene pick resolves a scene entity; a gizmo pixel or no-hit
            // returns false here, so no gizmo drag is ever begun from this branch.
            if (response.TryGetPickingResult(out PickingResult result))
            {
                branch = SampleBranch.Scene;
                resolvedEntity = result.Entity;
                return;
            }

            // Branch 3 (Req 8.7): no hit — begins no drag and resolves no scene entity.
            branch = SampleBranch.NoHit;
        }

        Action<PickingResponse> wrapped = router.Wrap(OnClickPickResponse, routeGizmoPicks: true);
        return (wrapped, () => branch, () => resolvedEntity);
    }

    /// <summary>
    /// Req 8.2 / 8.4: a click pick that decodes as a gizmo pick is routed to the gizmo service by the
    /// engine before the callback runs, so the service is dragging and the sample takes the "dragging"
    /// branch — it never resolves a scene entity and the drag was begun by routing, not by the sample.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void GizmoPick_IsRoutedAndReportedAsDragging_WithoutSceneResolution()
    {
        using World world = World.CreateWorld();
        using var manager = new GizmoManager();
        Assert.IsTrue(TrySetupGizmo(world, manager, out uint owningEntityId, out GizmoHandleId axisHandle));

        var (wrapped, branch, resolved) = BuildSampleFlow(manager);

        var response = new PickingResponse
        {
            Context = _validContext,
            Coord = new Vector2(400f, 300f),
            Data = PackGizmo(owningEntityId, axisHandle),
            RequestId = 1u,
        };

        wrapped(response);

        Assert.IsTrue(manager.IsDragging, "Routing must begin the drag on the gizmo service (Req 8.2).");
        Assert.AreEqual(SampleBranch.Dragging, branch(), "The sample must take the dragging branch for a gizmo pick (Req 8.2).");
        Assert.IsFalse(resolved().Valid, "A gizmo pick must not resolve a scene entity (Req 8.4).");
    }

    /// <summary>
    /// Req 8.3 / 8.4: a click pick that decodes as a scene pick resolves the scene entity (the same one
    /// pre-feature manual classification resolved) and never begins a gizmo drag.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void ScenePick_ResolvesSceneEntity_AndBeginsNoDrag()
    {
        using World world = World.CreateWorld();
        using var manager = new GizmoManager();
        // A tracked gizmo also exists in the world; a scene pick must still not touch it.
        Assert.IsTrue(TrySetupGizmo(world, manager, out _, out _));
        Entity sceneEntity = CreateSceneEntity(world);

        var (wrapped, branch, resolved) = BuildSampleFlow(manager);

        var response = new PickingResponse
        {
            Context = _validContext,
            Coord = new Vector2(400f, 300f),
            Data = PackScene((uint)world.Id, (uint)sceneEntity.Id),
            RequestId = 2u,
        };

        wrapped(response);

        Assert.IsFalse(manager.IsDragging, "A scene pick must never begin a gizmo drag (Req 8.4).");
        Assert.AreEqual(SampleBranch.Scene, branch(), "The sample must take the scene-resolution branch (Req 8.3).");
        Assert.AreEqual(sceneEntity.Id, resolved().Id, "The scene pick must resolve the same scene entity (Req 8.3).");
    }

    /// <summary>
    /// Req 8.7: a click pick that decodes as a no-hit begins no gizmo drag and resolves no scene entity.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void NoHitPick_BeginsNoDrag_AndResolvesNoSceneEntity()
    {
        using World world = World.CreateWorld();
        using var manager = new GizmoManager();
        Assert.IsTrue(TrySetupGizmo(world, manager, out _, out _));

        var (wrapped, branch, resolved) = BuildSampleFlow(manager);

        var response = new PickingResponse
        {
            Context = _validContext,
            Coord = new Vector2(400f, 300f),
            Data = 0UL, // cleared pixel => no-hit decode
            RequestId = 3u,
        };

        wrapped(response);

        Assert.IsFalse(manager.IsDragging, "A no-hit must never begin a gizmo drag (Req 8.7).");
        Assert.AreEqual(SampleBranch.NoHit, branch(), "The sample must take the no-hit branch (Req 8.7).");
        Assert.IsFalse(resolved().Valid, "A no-hit must resolve no scene entity (Req 8.7).");
    }
}
