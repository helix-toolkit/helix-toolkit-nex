using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext"/> per-frame overflow handling: once a frame
/// has accepted <see cref="GraphicsSettings.MaxRequestsPerFrame"/> requests, further submissions are
/// rejected without disturbing the already-accepted requests.
/// </summary>
[TestClass]
public class PickingContextOverflowPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>The number of requests that fills a single frame to capacity.</summary>
    private const int Capacity = (int)GraphicsSettings.MaxRequestsPerFrame;

    /// <summary>
    /// Generates a screen coordinate. Acceptance in <see cref="PickingContext.SetPendingSubmit"/>
    /// depends only on remaining per-frame capacity, not on the coordinate, so coordinates range
    /// over both in-bounds and out-of-bounds values.
    /// </summary>
    private static Gen<Vector2> CoordGen() =>
        from x in Gen.Choose(-10, 4000)
        from y in Gen.Choose(-10, 4000)
        select new Vector2(x, y);

    /// <summary>
    /// Generates the inputs for one overflow scenario: exactly <see cref="Capacity"/> coordinates
    /// that fill the frame, plus 1..16 additional coordinates that must all be rejected.
    /// </summary>
    private static Arbitrary<(Vector2[] Accepted, Vector2[] Overflow)> OverflowScenarios() =>
        (
            from accepted in Gen.ArrayOf(CoordGen(), Capacity)
            from extra in Gen.Choose(1, 16)
            from overflow in Gen.ArrayOf(CoordGen(), extra)
            select (accepted, overflow)
        ).ToArbitrary();

    // Feature: multi-request-picking, Property 3: Overflow returns the sentinel and preserves prior state
    /// <summary>
    /// Property 3: Overflow returns the sentinel and preserves prior state.
    /// <para>
    /// For any frame in which <see cref="GraphicsSettings.MaxRequestsPerFrame"/> requests have
    /// already been accepted, any further <see cref="PickingContext.SetPendingSubmit"/> returns
    /// <see cref="PickingContext.InvalidRequestId"/>, does not change the set of previously accepted
    /// requests (their Request Slots and Request Ids), and registers no pending readback for the
    /// rejected request.
    /// </para>
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    /// </summary>
    [TestMethod]
    public void OverflowRequests_ReturnSentinel_AndLeavePriorRequestsUnchanged()
    {
        Prop.ForAll(
                OverflowScenarios(),
                scenario =>
                {
                    var (acceptedCoords, overflowCoords) = scenario;
                    using var picking = new PickingContext(new MockContext());

                    // Fill the frame to capacity, recording each accepted request's id and slot.
                    var acceptedIds = new uint[Capacity];
                    var acceptedSlots = new int[Capacity];
                    for (var i = 0; i < Capacity; i++)
                    {
                        var id = picking.SetPendingSubmit(acceptedCoords[i]);
                        // Req 5.x precondition: everything up to capacity must be accepted.
                        if (id == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }
                        var slot = picking.GetRequestSlot(id);
                        if (slot < 0)
                        {
                            return false;
                        }
                        acceptedIds[i] = id;
                        acceptedSlots[i] = slot;
                    }

                    // Every further request is rejected with the sentinel (Req 5.1, 5.2).
                    foreach (var overflowCoord in overflowCoords)
                    {
                        var rejected = picking.SetPendingSubmit(overflowCoord);
                        if (rejected != PickingContext.InvalidRequestId)
                        {
                            return false;
                        }
                    }

                    // Previously accepted requests are unchanged: each id still resolves to the same
                    // Request Slot it was assigned (Req 5.3).
                    for (var i = 0; i < Capacity; i++)
                    {
                        if (picking.GetRequestSlot(acceptedIds[i]) != acceptedSlots[i])
                        {
                            return false;
                        }
                    }

                    // The rejected requests registered no accepted slot / pending readback: the
                    // sentinel id owns no Request Slot in the frame (Req 5.4).
                    if (picking.GetRequestSlot(PickingContext.InvalidRequestId) != -1)
                    {
                        return false;
                    }

                    return true;
                }
            )
            .Check(DefaultConfig);
    }
}
