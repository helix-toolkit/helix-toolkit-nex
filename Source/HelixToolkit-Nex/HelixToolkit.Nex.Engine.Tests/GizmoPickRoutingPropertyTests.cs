using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.Extensions.DependencyInjection;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 9: Automatic pick routing dispatches only
/// resolved gizmo picks.
/// <para>
/// For any delivered picking result and routing-enabled flag, the <see cref="GizmoPickRouter"/>
/// dispatches a begin/continue drag (deriving the world ray from the pick's screen coordinate via
/// <see cref="RenderContext.TryUnProject(float, float, out Ray)"/>) <em>if and only if</em> routing is
/// enabled AND the decode is a gizmo pick that resolves to a gizmo the service tracks AND the ray can
/// be derived; in every other case (routing disabled, scene pick, no-hit, unresolved gizmo pick, or a
/// failing/absent render context) it performs no dispatch, raises no exception to the application
/// callback, leaves the result available unchanged, and — when it does dispatch — does so before the
/// application callback observes the result.
/// </para>
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7**
/// </summary>
[TestClass]
public sealed class GizmoPickRoutingPropertyTests
{
    /// <summary>How the decoded pick should classify.</summary>
    private enum PickKind
    {
        /// <summary>A scene pick (world id &gt; 0) — never routed (Req 6.2).</summary>
        Scene,

        /// <summary>A gizmo pick whose owning entity + handle resolve to a tracked gizmo (Req 6.1).</summary>
        GizmoResolved,

        /// <summary>A gizmo pick whose owning entity is not tracked — resolves to nothing (Req 6.3).</summary>
        GizmoUnresolved,

        /// <summary>A no-hit pixel — never routed (Req 6.4).</summary>
        NoHit,
    }

    /// <summary>The render context supplied on the delivered picking result.</summary>
    private enum ContextMode
    {
        /// <summary>A context with a valid viewport + camera, so <c>TryUnProject</c> succeeds.</summary>
        Valid,

        /// <summary>A context whose camera is identity, so <c>TryUnProject</c> fails (no ray).</summary>
        IdentityCamera,

        /// <summary>A <see langword="null"/> context, so deriving the ray throws (must be swallowed).</summary>
        Null,
    }

    private sealed record Scenario(bool RoutingEnabled, PickKind Kind, ContextMode Context);

    private MockContext _mock = null!;
    private ServiceProvider _provider = null!;
    private RenderContext _validContext = null!;
    private RenderContext _identityContext = null!;

    [TestInitialize]
    public void Setup()
    {
        _mock = new MockContext();
        _mock.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<IContext>(_mock);
        _provider = services.BuildServiceProvider();

        // A context with a real viewport and a non-identity perspective camera: TryUnProject succeeds
        // and yields a usable world ray for the pick coordinate.
        _validContext = new RenderContext(_provider) { WindowSize = new Size(800, 600) };
        _validContext.CameraParams = MakePerspectiveCamera(800f / 600f);

        // A context left with the default identity camera: TryUnProject returns false (no ray) so a
        // resolved gizmo pick still cannot be dispatched.
        _identityContext = new RenderContext(_provider) { WindowSize = new Size(800, 600) };
    }

    [TestCleanup]
    public void Cleanup()
    {
        _validContext?.Dispose();
        _identityContext?.Dispose();
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

    private static Arbitrary<Scenario> ScenarioArb() =>
        (from routing in Gen.Elements(true, false)
         from kind in Gen.Elements(PickKind.Scene, PickKind.GizmoResolved, PickKind.GizmoUnresolved, PickKind.NoHit)
         from context in Gen.Elements(ContextMode.Valid, ContextMode.IdentityCamera, ContextMode.Null)
         select new Scenario(routing, kind, context)).ToArbitrary();

    /// <summary>
    /// Sets up a single tracked translate gizmo (World space) on a fresh entity and returns the pieces
    /// needed to build a delivered picking result for the scenario.
    /// </summary>
    private static bool TrySetupGizmo(
        World world,
        GizmoManager manager,
        out uint owningEntityId,
        out GizmoHandleId axisHandle)
    {
        owningEntityId = 0;
        axisHandle = default;

        // Translate gizmo in World space so a resolved pick can begin an axis drag deterministically
        // (its X handle's constrained world axis is the canonical UnitX).
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

        // Pick a translate axis handle (X) — a draggable handle so the routed dispatch begins a drag.
        bool foundHandle = false;
        foreach (GizmoHandle candidate in published.Handles)
        {
            if (candidate.Id.Axis == GizmoAxis.X)
            {
                axisHandle = candidate.Id;
                foundHandle = true;
                break;
            }
        }

        if (!foundHandle)
        {
            return false;
        }

        owningEntityId = (uint)entity.Id;
        return true;
    }

    private static ulong PackData(PickKind kind, uint owningEntityId, GizmoHandleId axisHandle)
    {
        uint r;
        uint g;
        switch (kind)
        {
            case PickKind.Scene:
                // World id > 0 => a scene pick decode.
                Utils.PackMeshInfo(worldId: 1u, entityId: 7u, instanceIndex: 0u, primitiveId: 0u, out r, out g);
                break;

            case PickKind.GizmoResolved:
                Utils.PackGizmoInfo(owningEntityId, axisHandle, out r, out g);
                break;

            case PickKind.GizmoUnresolved:
                // A gizmo pick whose owning entity id is not tracked by the service.
                Utils.PackGizmoInfo(owningEntityId + 1000u, axisHandle, out r, out g);
                break;

            case PickKind.NoHit:
            default:
                // World id 0 with a cleared (None) encoding => no-hit decode.
                return 0UL;
        }

        return r | ((ulong)g << 32);
    }

    private RenderContext? ContextFor(ContextMode mode) => mode switch
    {
        ContextMode.Valid => _validContext,
        ContextMode.IdentityCamera => _identityContext,
        _ => null,
    };

    /// <summary>
    /// Property 9 (via <see cref="GizmoPickRouter.Wrap"/>): the wrapper always invokes the application
    /// callback exactly once with the unmodified result and never throws, for every routing flag, pick
    /// kind, and render-context state. The routed drag dispatch is visible to the service if and only
    /// if routing is enabled AND the pick resolves to a tracked gizmo AND a world ray can be derived,
    /// and any such dispatch is already visible by the time the application callback runs (ordering).
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Property9_Wrap_RoutesOnlyResolvedGizmoPicks_AlwaysInvokesCallbackOnce_NeverThrows()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            using World world = World.CreateWorld();
            using var manager = new GizmoManager();

            if (!TrySetupGizmo(world, manager, out uint owningEntityId, out GizmoHandleId axisHandle))
            {
                return false;
            }

            var router = new GizmoPickRouter(manager);

            ulong data = PackData(scenario.Kind, owningEntityId, axisHandle);
            var coord = new Vector2(400f, 300f); // viewport centre
            var response = new PickingResponse
            {
                Context = ContextFor(scenario.Context)!,
                Coord = coord,
                Data = data,
                RequestId = 42u,
            };

            int invocations = 0;
            bool draggingObservedAtCallback = false;
            PickingResponse captured = default;

            Action<PickingResponse> appCallback = r =>
            {
                invocations++;
                captured = r;
                draggingObservedAtCallback = manager.IsDragging;
            };

            Action<PickingResponse> wrapped = router.Wrap(appCallback, scenario.RoutingEnabled);

            // Req 6.3 / 6.4: no exception ever propagates out of the wrapper for any input.
            wrapped(response);

            // Req 6.6 / 6.7: the application callback is always invoked exactly once.
            if (invocations != 1)
            {
                return false;
            }

            // Req 6.2 / 6.4 / 6.7: the result reaches the application unchanged.
            if (captured.Data != data || captured.Coord != coord || captured.RequestId != 42u)
            {
                return false;
            }

            // The router dispatches (begins the drag) iff routing is enabled, the pick resolves to the
            // tracked gizmo, and the ray can be derived (valid context). The translate X handle makes a
            // successful begin-drag deterministic, so IsDragging is the observable dispatch effect.
            bool expectDispatch =
                scenario.RoutingEnabled
                && scenario.Kind == PickKind.GizmoResolved
                && scenario.Context == ContextMode.Valid;

            bool draggingAfter = manager.IsDragging;
            if (draggingAfter != expectDispatch)
            {
                return false;
            }

            // Req 6.6 (ordering): whatever routing did is already visible when the callback runs, so the
            // dispatch state observed inside the callback equals the state after the wrapper returns.
            if (draggingObservedAtCallback != draggingAfter)
            {
                return false;
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 9 (via <see cref="GizmoPickRouter.TryRoute"/>): the router reports a dispatch — and the
    /// service observably begins the drag — if and only if the decode is a gizmo pick that resolves to
    /// a tracked gizmo AND the world ray can be derived. Scene picks, no-hits, unresolved gizmo picks,
    /// and contexts that cannot produce a ray never dispatch, and the call never throws.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Property9_TryRoute_DispatchesOnlyResolvedGizmoPicks_WithDerivableRay_NeverThrows()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            using World world = World.CreateWorld();
            using var manager = new GizmoManager();

            if (!TrySetupGizmo(world, manager, out uint owningEntityId, out GizmoHandleId axisHandle))
            {
                return false;
            }

            var router = new GizmoPickRouter(manager);

            ulong data = PackData(scenario.Kind, owningEntityId, axisHandle);
            var response = new PickingResponse
            {
                Context = ContextFor(scenario.Context)!,
                Coord = new Vector2(400f, 300f),
                Data = data,
                RequestId = 7u,
            };

            // TryRoute never consults the routing-enabled flag (that is Wrap's job); it decodes,
            // resolves, and dispatches, and must never throw for any input (Req 6.3, 6.4).
            bool dispatched = router.TryRoute(in response);

            bool expectDispatch =
                scenario.Kind == PickKind.GizmoResolved
                && scenario.Context == ContextMode.Valid;

            // The return value reflects exactly the routing decision (Req 6.1–6.5).
            if (dispatched != expectDispatch)
            {
                return false;
            }

            // A dispatch that reached the service began the drag on the resolved handle; a non-dispatch
            // left the service untouched (Req 6.2, 6.3, 6.4).
            if (manager.IsDragging != expectDispatch)
            {
                return false;
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    // ---- Example / edge-case unit tests -------------------------------------------------------

    private (GizmoPickRouter Router, GizmoManager Manager, uint OwningId, GizmoHandleId Handle, World World) NewFixture()
    {
        World world = World.CreateWorld();
        var manager = new GizmoManager();
        Assert.IsTrue(TrySetupGizmo(world, manager, out uint owningId, out GizmoHandleId handle));
        return (new GizmoPickRouter(manager), manager, owningId, handle, world);
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void ResolvedGizmoPick_RoutingEnabled_BeginsDrag_AndInvokesCallback()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            ulong data = PackData(PickKind.GizmoResolved, owningId, handle);
            var response = new PickingResponse { Context = _validContext, Coord = new Vector2(400, 300), Data = data, RequestId = 1u };

            bool callbackRan = false;
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: true)(response);

            Assert.IsTrue(manager.IsDragging, "A resolved gizmo pick with a derivable ray must begin a drag.");
            Assert.IsTrue(callbackRan, "The application callback must still be invoked.");
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void ResolvedGizmoPick_RoutingDisabled_DoesNotDispatch_ButInvokesCallback()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            ulong data = PackData(PickKind.GizmoResolved, owningId, handle);
            var response = new PickingResponse { Context = _validContext, Coord = new Vector2(400, 300), Data = data, RequestId = 1u };

            bool callbackRan = false;
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: false)(response);

            Assert.IsFalse(manager.IsDragging, "Routing disabled must never dispatch a gizmo pick (Req 6.7).");
            Assert.IsTrue(callbackRan, "The application callback must still be invoked unchanged.");
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void ScenePick_RoutingEnabled_DoesNotDispatch_AndPassesThrough()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            ulong data = PackData(PickKind.Scene, owningId, handle);
            var response = new PickingResponse { Context = _validContext, Coord = new Vector2(400, 300), Data = data, RequestId = 1u };

            bool callbackRan = false;
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: true)(response);

            Assert.IsFalse(manager.IsDragging, "A scene pick must never dispatch to the gizmo service (Req 6.2).");
            Assert.IsTrue(callbackRan);
            Assert.IsFalse(router.TryRoute(in response), "A scene pick resolves to no dispatch.");
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void NoHit_RoutingEnabled_DoesNotDispatch_AndPassesThrough()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            var response = new PickingResponse { Context = _validContext, Coord = new Vector2(400, 300), Data = 0UL, RequestId = 1u };

            bool callbackRan = false;
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: true)(response);

            Assert.IsFalse(manager.IsDragging, "A no-hit must never dispatch (Req 6.4).");
            Assert.IsTrue(callbackRan);
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void UnresolvedGizmoPick_RoutingEnabled_DoesNotDispatch_AndPassesThrough()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            ulong data = PackData(PickKind.GizmoUnresolved, owningId, handle);
            var response = new PickingResponse { Context = _validContext, Coord = new Vector2(400, 300), Data = data, RequestId = 1u };

            bool callbackRan = false;
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: true)(response);

            Assert.IsFalse(manager.IsDragging, "An unresolved gizmo pick must never dispatch (Req 6.3).");
            Assert.IsTrue(callbackRan);
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void ResolvedGizmoPick_NullContext_SwallowsException_AndInvokesCallback()
    {
        var (router, manager, owningId, handle, world) = NewFixture();
        using (world)
        {
            ulong data = PackData(PickKind.GizmoResolved, owningId, handle);
            var response = new PickingResponse { Context = null!, Coord = new Vector2(400, 300), Data = data, RequestId = 1u };

            bool callbackRan = false;

            // Deriving the ray from a null context throws internally; the router must swallow it so the
            // application callback still receives the unmodified result (Req 6.3, 6.4).
            router.Wrap(_ => callbackRan = true, routeGizmoPicks: true)(response);

            Assert.IsFalse(manager.IsDragging, "A failed ray derivation must not dispatch.");
            Assert.IsTrue(callbackRan, "The application callback must still be invoked despite the internal failure.");
        }
    }
}
