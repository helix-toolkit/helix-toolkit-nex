using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: component-driven-gizmos, Property 1: Gizmo encoding round-trip.
/// <para>
/// For any owning entity id representable in the gizmo owning-entity field
/// (<c>0 .. 2^24 - 1</c>, i.e. <see cref="GizmoEncodingConstants.OwningEntityMask"/>) and any
/// <see cref="GizmoHandleId"/> (mode + axis), packing a gizmo pick with
/// <see cref="Utils.PackGizmoInfo"/> and then decoding the resulting <c>(R, G)</c> with
/// <see cref="Utils.UnpackEntityId(uint, uint)"/> produces <see cref="EntityIdPickKind.Gizmo"/>,
/// the same owning entity id, and the same <see cref="GizmoHandleId"/>, with the handle id carried
/// in the G channel.
/// </para>
/// **Validates: Requirements 1.5, 4.1, 4.2, 4.3, 4.5**
/// </summary>
[TestClass]
public class GizmoEncodingRoundTripPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(500);

    /// <summary>The maximum owning entity id representable in the 24-bit owning-entity field.</summary>
    private const uint MaxOwningEntityId = GizmoEncodingConstants.OwningEntityMask; // 2^24 - 1

    /// <summary>All defined <see cref="GizmoMode"/> values.</summary>
    private static readonly GizmoMode[] AllModes = (GizmoMode[])Enum.GetValues(typeof(GizmoMode));

    /// <summary>All defined <see cref="GizmoAxis"/> values.</summary>
    private static readonly GizmoAxis[] AllAxes = (GizmoAxis[])Enum.GetValues(typeof(GizmoAxis));

    /// <summary>
    /// Generates owning entity ids across the full representable range <c>[0, 2^24 - 1]</c>,
    /// weighting the boundary values (<c>0</c> and the max) so they are always exercised alongside
    /// uniformly-random interior values.
    /// </summary>
    private static Arbitrary<uint> OwningEntityIds() =>
        Gen.Frequency(
                (1, Gen.Constant(0u)),
                (1, Gen.Constant(MaxOwningEntityId)),
                (6, Gen.Choose(0, (int)MaxOwningEntityId).Select(i => (uint)i))
            )
            .ToArbitrary();

    /// <summary>Generates every <see cref="GizmoHandleId"/> across all mode × axis combinations.</summary>
    private static Arbitrary<GizmoHandleId> HandleIds() =>
        Gen.Elements(AllModes)
            .SelectMany(mode => Gen.Elements(AllAxes).Select(axis => new GizmoHandleId(mode, axis)))
            .ToArbitrary();

    /// <summary>
    /// Property 1: pack then unpack round-trips the owning entity id and the handle id, and always
    /// classifies as a gizmo pick.
    /// **Validates: Requirements 1.5, 4.1, 4.2, 4.3, 4.5**
    /// </summary>
    [TestMethod]
    public void PackThenUnpack_RoundTripsOwningEntityAndHandle_AsGizmo()
    {
        Prop.ForAll(
                OwningEntityIds(),
                HandleIds(),
                (uint owningEntityId, GizmoHandleId handle) =>
                {
                    Utils.PackGizmoInfo(owningEntityId, handle, out uint r, out uint g);

                    // Req 4.2: the handle id is carried in the G channel (the R channel carries the
                    // world-id-zero discriminator, encoding type, and owning entity id).
                    Utils.UnpackGizmoInfo(r, g, out _, out var handleFromG);

                    var decoded = Utils.UnpackEntityId(r, g);

                    // Req 4.1 / 4.5: routed to the gizmo decode (never scene / no-hit).
                    bool isGizmo = decoded.Kind == EntityIdPickKind.Gizmo;

                    // Req 1.5 / 4.3: same owning entity id and same handle id round-trip.
                    bool sameOwner = decoded.OwningEntityId == owningEntityId;
                    bool sameHandle = decoded.Handle == handle;

                    // Req 4.2: the handle recovered directly from G matches the packed handle.
                    bool handleInG = handleFromG == handle;

                    return isGizmo && sameOwner && sameHandle && handleInG;
                }
            )
            .Check(DefaultConfig);
    }

    /// <summary>
    /// Boundary example: owning entity id <c>0</c> round-trips for a representative handle.
    /// **Validates: Requirements 4.3**
    /// </summary>
    [TestMethod]
    public void RoundTrip_MinOwningEntityId_Boundary()
    {
        AssertRoundTrip(0u, new GizmoHandleId(GizmoMode.Translate, GizmoAxis.X));
    }

    /// <summary>
    /// Boundary example: the maximum representable owning entity id (<c>2^24 - 1</c>) round-trips.
    /// **Validates: Requirements 4.3**
    /// </summary>
    [TestMethod]
    public void RoundTrip_MaxOwningEntityId_Boundary()
    {
        AssertRoundTrip(MaxOwningEntityId, new GizmoHandleId(GizmoMode.Scale, GizmoAxis.Uniform));
    }

    /// <summary>
    /// Exhaustive example over every mode × axis handle id for a fixed owning entity id, confirming
    /// each distinct handle round-trips as a gizmo pick.
    /// **Validates: Requirements 4.2, 4.3, 4.5**
    /// </summary>
    [TestMethod]
    public void RoundTrip_AllModeAxisCombinations()
    {
        const uint owningEntityId = 0x00ABCDEF & GizmoEncodingConstants.OwningEntityMask;
        foreach (var mode in AllModes)
        {
            foreach (var axis in AllAxes)
            {
                AssertRoundTrip(owningEntityId, new GizmoHandleId(mode, axis));
            }
        }
    }

    private static void AssertRoundTrip(uint owningEntityId, GizmoHandleId handle)
    {
        Utils.PackGizmoInfo(owningEntityId, handle, out uint r, out uint g);
        var decoded = Utils.UnpackEntityId(r, g);

        Assert.AreEqual(
            EntityIdPickKind.Gizmo,
            decoded.Kind,
            $"Expected a gizmo pick for owner {owningEntityId} handle {handle}."
        );
        Assert.AreEqual(owningEntityId, decoded.OwningEntityId, "Owning entity id must round-trip.");
        Assert.AreEqual(handle, decoded.Handle, "Handle id must round-trip.");
    }
}
