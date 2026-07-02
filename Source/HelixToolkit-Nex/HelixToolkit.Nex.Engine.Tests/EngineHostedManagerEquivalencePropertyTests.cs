using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.Extensions.DependencyInjection;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 8: Engine-hosted service matches a directly
/// constructed manager.
/// <para>
/// For any sequence of equivalent create / update / remove / resolve / drag inputs, the engine-hosted
/// gizmo service (<see cref="Engine.Gizmos"/>) produces the same observable gizmo creation, tracking,
/// pick-resolution, and drag results as a directly constructed <see cref="GizmoManager"/> given the
/// same inputs.
/// </para>
/// <para>
/// <b>Approach.</b> A real <see cref="Engine"/> is constructed headlessly over a
/// <see cref="MockContext"/> (the same pattern the multi-request picking integration test uses), so
/// the property drives the genuine engine-hosted <see cref="Engine.Gizmos"/> service — not a stand-in.
/// The engine-hosted service and a directly constructed <see cref="GizmoManager"/> each own their own
/// ECS <see cref="World"/> whose entities are created in lockstep, so a gizmo's carrier entity id
/// matches across the two managers and the two are fed byte-for-byte equivalent inputs. After each
/// operation the two managers' observable state is compared: creation/set success, the
/// <see cref="GizmoManager.HandleSetBuildCount"/>, every published <see cref="GizmoDrawInfo"/>
/// (mode/space/origin/occlusion/pixel-size, the <see cref="GizmoDrawInfo.HighlightedHandle"/> overlay,
/// and the cached <see cref="GizmoDrawInfo.Handles"/> reference-sharing pattern <em>within</em> each
/// manager), pick-resolution outcomes, and drag begin/continue results (including the produced
/// transform delta). The property also asserts the lightweight lifecycle guarantee that repeated
/// <see cref="Engine.Gizmos"/> access returns the same instance (Requirement 4.1).
/// </para>
/// **Validates: Requirements 4.4**
/// </summary>
[TestClass]
public sealed class EngineHostedManagerEquivalencePropertyTests
{
    /// <summary>The three gizmo modes; each distinct mode is one distinct handle-set shape.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; space does not affect the handle geometry.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect the handle geometry.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>Generates a well-formed (always valid) <see cref="GizmoDefinition"/>.</summary>
    private static Gen<GizmoDefinition> DefinitionGen() =>
        from mode in ModeGen()
        from space in SpaceGen()
        from target in Gen.Choose(0, 1000)
        from pixel in Gen.Choose(1, 200)
        from occlusion in OcclusionGen()
        select new GizmoDefinition(
            mode,
            space,
            (uint)target,
            new GizmoHandleConfiguration(pixel, occlusion));

    private static Gen<Vector3> PositionGen() =>
        from x in Gen.Choose(-500, 500)
        from y in Gen.Choose(-500, 500)
        from z in Gen.Choose(-500, 500)
        select new Vector3(x, y, z);

    /// <summary>Generates one operation to apply identically to both managers.</summary>
    private static Gen<GizmoOp> OpGen() =>
        from kind in Gen.Elements(
            GizmoOpKind.Update,
            GizmoOpKind.Highlight,
            GizmoOpKind.ResolvePick,
            GizmoOpKind.Drag,
            GizmoOpKind.UpdateDefinition,
            GizmoOpKind.Remove)
        from gizmoIndex in Gen.Choose(0, 100)
        from handleIndex in Gen.Choose(0, 100)
        from origin in PositionGen()
        from newDef in DefinitionGen()
        from beginPos in PositionGen()
        from updatePos in PositionGen()
        select new GizmoOp(kind, gizmoIndex, handleIndex, origin, newDef, beginPos, updatePos);

    /// <summary>
    /// A scenario: an initial set of 1..4 valid definitions to create, and a sequence of 0..10
    /// operations to apply identically to both managers.
    /// </summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, GizmoOp[] Ops)> ScenarioArb() =>
        (from count in Gen.Choose(1, 4)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         from opCount in Gen.Choose(0, 10)
         from ops in Gen.ArrayOf(OpGen(), opCount)
         select (defs, ops)).ToArbitrary();

    /// <summary>
    /// Property 8: For any equivalent input sequence, the engine-hosted service and a directly
    /// constructed manager produce identical observable results.
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void EngineHostedService_MatchesDirectlyConstructedManager()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            (GizmoDefinition[] definitions, GizmoOp[] ops) = scenario;

            using var mock = new MockContext();
            mock.Initialize();
            Engine engine = CreateEngine(mock);

            using World engineWorld = World.CreateWorld();
            using World directWorld = World.CreateWorld();
            var directMgr = new GizmoManager();

            try
            {
                // The genuine engine-hosted gizmo service.
                GizmoManager engineMgr = engine.Gizmos;

                // Lifecycle guarantee (Req 4.1): repeated access returns the same instance.
                if (!ReferenceEquals(engineMgr, engine.Gizmos))
                {
                    return false;
                }

                var engineHarness = new Harness(engineWorld, engineMgr);
                var directHarness = new Harness(directWorld, directMgr);

                // --- Setup: create + set each definition on its own lockstep-created entity. ---
                foreach (GizmoDefinition definition in definitions)
                {
                    SetupResult e = engineHarness.Setup(definition);
                    SetupResult d = directHarness.Setup(definition);

                    // Creation / set success and carrier entity id must match across the two managers.
                    if (!e.Equals(d))
                    {
                        return false;
                    }
                }

                if (!StatesEqual(engineHarness, directHarness))
                {
                    return false;
                }

                // --- Apply each operation identically to both managers and compare after each. ---
                foreach (GizmoOp op in ops)
                {
                    OpResult e = engineHarness.Apply(op);
                    OpResult d = directHarness.Apply(op);

                    if (!e.Equals(d) || !StatesEqual(engineHarness, directHarness))
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                directMgr.Dispose();
                // Dispose the engine (which disposes the engine-hosted service). Swallow teardown
                // hiccups from a never-rendered headless engine so cleanup never masks the result.
                try
                {
                    engine.Dispose();
                }
                catch
                {
                    // Intentionally ignored: headless test-engine teardown only.
                }
            }
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Builds a headless <see cref="Engine"/> over the supplied mock context, mirroring the engine
    /// construction used by the multi-request picking integration test.
    /// </summary>
    private static Engine CreateEngine(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        services.AddSingleton<IResourceManager, ResourceManager>();
        ServiceProvider provider = services.BuildServiceProvider();
        return new Engine(new EngineConfig(provider));
    }

    /// <summary>
    /// Compares the full observable gizmo state (build count + per-gizmo published component + the
    /// within-manager cached-handle-set sharing pattern) of two harnesses.
    /// </summary>
    private static bool StatesEqual(Harness a, Harness b)
    {
        if (a.Manager.HandleSetBuildCount != b.Manager.HandleSetBuildCount)
        {
            return false;
        }

        GizmoSnapshot[] sa = a.SnapshotAll();
        GizmoSnapshot[] sb = b.SnapshotAll();
        if (sa.Length != sb.Length)
        {
            return false;
        }

        for (int i = 0; i < sa.Length; i++)
        {
            if (!sa[i].Equals(sb[i]))
            {
                return false;
            }
        }

        // Within-manager reference-sharing pattern of the cached handle-set instances must match:
        // gizmos that share (or don't share) a cached list within one manager must do the same in the
        // other. groupOf[i] is the index of the first gizmo whose published Handles list is
        // reference-equal to gizmo i's (or -1 when gizmo i has no published component).
        int[] ga = a.HandleSetGroups();
        int[] gb = b.HandleSetGroups();
        for (int i = 0; i < ga.Length; i++)
        {
            if (ga[i] != gb[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The kinds of operation the equivalence scenario can apply.</summary>
    private enum GizmoOpKind
    {
        Update,
        Highlight,
        ResolvePick,
        Drag,
        UpdateDefinition,
        Remove,
    }

    /// <summary>One generated operation targeting a gizmo (by index) with the parameters it needs.</summary>
    private readonly record struct GizmoOp(
        GizmoOpKind Kind,
        int GizmoIndex,
        int HandleIndex,
        Vector3 Origin,
        GizmoDefinition NewDefinition,
        Vector3 BeginRayPos,
        Vector3 UpdateRayPos);

    /// <summary>The observable result of setup for one definition.</summary>
    private readonly record struct SetupResult(bool Created, bool Set, uint OwningEntityId);

    /// <summary>The observable result of applying one operation (op-specific fields only).</summary>
    private readonly record struct OpResult(
        bool ResolveResolved,
        GizmoHandleId ResolveHandle,
        uint ResolveOwning,
        bool DragBegan,
        bool DragIsDragging,
        uint DragOwning,
        bool DragUpdated,
        Matrix4x4 DragDelta)
    {
        public static readonly OpResult None = new(false, default, 0u, false, false, 0u, false, Matrix4x4.Identity);
    }

    /// <summary>A by-value snapshot of one gizmo's published <see cref="GizmoDrawInfo"/>.</summary>
    private readonly record struct GizmoSnapshot(
        bool HasComponent,
        GizmoMode Mode,
        GizmoSpace Space,
        Vector3 Origin,
        GizmoOcclusionMode Occlusion,
        float DesiredPixelSize,
        GizmoHandleId? Highlighted,
        int HandleCount);

    /// <summary>
    /// Drives one <see cref="GizmoManager"/> over its own world with lockstep entity creation, so two
    /// harnesses fed the same operation sequence receive equivalent inputs.
    /// </summary>
    private sealed class Harness(World world, GizmoManager manager)
    {
        private static readonly Size Viewport = new(1920, 1080);

        private readonly World _world = world;
        private readonly List<GizmoInstanceHandle> _handles = [];
        private readonly List<Entity> _entities = [];
        private readonly List<GizmoHandleId[]> _handleIds = [];
        private readonly List<bool> _present = [];

        public GizmoManager Manager { get; } = manager;

        public int Count => _entities.Count;

        /// <summary>Creates + sets one gizmo on a freshly created entity and captures its handle ids.</summary>
        public SetupResult Setup(GizmoDefinition definition)
        {
            bool created = Manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle);
            Entity entity = _world.CreateEntity();
            bool set = created && Manager.TrySetGizmo(entity, handle);

            _handles.Add(handle);
            _entities.Add(entity);
            _present.Add(set);
            _handleIds.Add(CaptureHandleIds(entity));

            return new SetupResult(created, set, (uint)entity.Id);
        }

        /// <summary>Applies one operation to this harness's manager and returns its observable result.</summary>
        public OpResult Apply(in GizmoOp op)
        {
            if (Count == 0)
            {
                return OpResult.None;
            }

            int gi = Math.Abs(op.GizmoIndex) % Count;
            Entity entity = _entities[gi];
            GizmoInstanceHandle handle = _handles[gi];
            uint owning = (uint)entity.Id;

            switch (op.Kind)
            {
                case GizmoOpKind.Update:
                    Manager.UpdateInstance(handle, CameraParams.Identity, Viewport, Matrix4x4.CreateTranslation(op.Origin));
                    return OpResult.None;

                case GizmoOpKind.Highlight:
                {
                    GizmoHandleId hid = PickHandleId(gi, op.HandleIndex);
                    Manager.SetHighlight(handle, hid);
                    return OpResult.None;
                }

                case GizmoOpKind.ResolvePick:
                {
                    GizmoHandleId hid = PickHandleId(gi, op.HandleIndex);
                    bool resolved = Manager.TryResolvePick(true, owning, hid, out GizmoPickResolution resolution);
                    return OpResult.None with
                    {
                        ResolveResolved = resolved,
                        ResolveHandle = resolution.Handle,
                        ResolveOwning = resolution.OwningEntityId,
                    };
                }

                case GizmoOpKind.Drag:
                {
                    GizmoHandleId hid = PickHandleId(gi, op.HandleIndex);
                    var resolution = new GizmoPickResolution(owning, hid);
                    Vector3 axis = UnitAxis(hid.Axis);
                    var beginRay = new Ray(op.BeginRayPos, DragRayDirection(axis));
                    var updateRay = new Ray(op.UpdateRayPos, DragRayDirection(axis));

                    bool began = Manager.BeginDrag(resolution, beginRay);
                    bool isDragging = Manager.IsDragging;
                    uint dragOwning = Manager.DragOwningEntityId;
                    bool updated = false;
                    Matrix4x4 delta = Matrix4x4.Identity;
                    if (began)
                    {
                        updated = Manager.UpdateDrag(updateRay, out delta);
                        Manager.EndDrag();
                    }

                    return OpResult.None with
                    {
                        DragBegan = began,
                        DragIsDragging = isDragging,
                        DragOwning = dragOwning,
                        DragUpdated = updated,
                        DragDelta = delta,
                    };
                }

                case GizmoOpKind.UpdateDefinition:
                {
                    bool ok = Manager.TryUpdateDefinition(handle, op.NewDefinition);
                    if (ok && _present[gi])
                    {
                        // The shape may have changed; recapture this gizmo's published handle ids so
                        // subsequent resolve/drag ops use the current handle set.
                        _handleIds[gi] = CaptureHandleIds(entity);
                    }
                    return OpResult.None;
                }

                case GizmoOpKind.Remove:
                {
                    Manager.RemoveGizmo(handle);
                    _present[gi] = false;
                    return OpResult.None;
                }

                default:
                    return OpResult.None;
            }
        }

        /// <summary>Snapshots every gizmo's published <see cref="GizmoDrawInfo"/> by value.</summary>
        public GizmoSnapshot[] SnapshotAll()
        {
            var snapshot = new GizmoSnapshot[Count];
            for (int i = 0; i < Count; i++)
            {
                snapshot[i] = _entities[i].TryGet(out GizmoDrawInfo info)
                    ? new GizmoSnapshot(true, info.Mode, info.Space, info.Origin, info.OcclusionMode, info.DesiredPixelSize, info.HighlightedHandle, info.Handles?.Count ?? 0)
                    : new GizmoSnapshot(false, default, default, default, default, 0f, null, 0);
            }

            return snapshot;
        }

        /// <summary>
        /// The within-manager cached-handle-set sharing pattern: <c>groupOf[i]</c> is the index of the
        /// first gizmo whose published <see cref="GizmoDrawInfo.Handles"/> list is reference-equal to
        /// gizmo <c>i</c>'s, or <c>-1</c> when gizmo <c>i</c> has no published component.
        /// </summary>
        public int[] HandleSetGroups()
        {
            var lists = new IReadOnlyList<GizmoHandle>?[Count];
            for (int i = 0; i < Count; i++)
            {
                lists[i] = _entities[i].TryGet(out GizmoDrawInfo info) ? info.Handles : null;
            }

            var groupOf = new int[Count];
            for (int i = 0; i < Count; i++)
            {
                groupOf[i] = -1;
                if (lists[i] is null)
                {
                    continue;
                }

                for (int j = 0; j <= i; j++)
                {
                    if (lists[j] is not null && ReferenceEquals(lists[j], lists[i]))
                    {
                        groupOf[i] = j;
                        break;
                    }
                }
            }

            return groupOf;
        }

        private GizmoHandleId PickHandleId(int gi, int handleIndex)
        {
            GizmoHandleId[] ids = _handleIds[gi];
            if (ids.Length == 0)
            {
                return default;
            }

            return ids[Math.Abs(handleIndex) % ids.Length];
        }

        private static GizmoHandleId[] CaptureHandleIds(Entity entity)
        {
            if (!entity.TryGet(out GizmoDrawInfo info) || info.Handles is null)
            {
                return [];
            }

            var ids = new GizmoHandleId[info.Handles.Count];
            for (int i = 0; i < info.Handles.Count; i++)
            {
                ids[i] = info.Handles[i].Id;
            }

            return ids;
        }

        private static Vector3 UnitAxis(GizmoAxis axis) => axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            _ => Vector3.UnitZ,
        };

        /// <summary>
        /// A pointer-ray direction whose alignment with <paramref name="axis"/> is moderate, so it is
        /// neither parallel nor perpendicular to the axis and a drag can begin for handles of any mode.
        /// </summary>
        private static Vector3 DragRayDirection(Vector3 axis)
        {
            Vector3 perp = axis == Vector3.UnitX ? Vector3.UnitY : Vector3.UnitX;
            return Vector3.Normalize((axis * 0.5f) + perp);
        }
    }
}
