using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for the uniqueness of Request Ids returned by
/// <see cref="PickingContext.SetPendingSubmit"/>.
/// </summary>
[TestClass]
public class PickingContextUniqueRequestIdPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>
    /// Generates a screen coordinate. Acceptance in <see cref="PickingContext.SetPendingSubmit"/>
    /// does not depend on the coordinate (only on remaining per-frame capacity), so coordinates
    /// range over both in-bounds and out-of-bounds values.
    /// </summary>
    private static Gen<Vector2> CoordGen() =>
        from x in Gen.Choose(-10, 4000)
        from y in Gen.Choose(-10, 4000)
        select new Vector2(x, y);

    /// <summary>
    /// Generates a sequence of 0..<see cref="GraphicsSettings.MaxRequestsPerFrame"/> coordinates,
    /// representing up to a full frame's worth of picking requests submitted before any recording.
    /// Every request in the sequence is within per-frame capacity, so all are accepted.
    /// </summary>
    private static Arbitrary<Vector2[]> AcceptedRequestSequences() =>
        Gen.Choose(0, (int)GraphicsSettings.MaxRequestsPerFrame)
            .SelectMany(n => Gen.ArrayOf(CoordGen(), n))
            .ToArbitrary();

    // Feature: multi-request-picking, Property 8: Unique request ids
    /// <summary>
    /// Property 8: Unique request ids.
    /// <para>
    /// For any sequence of accepted picking requests, the returned Request Ids are unique (strictly
    /// increasing) and never equal to <see cref="PickingContext.InvalidRequestId"/>.
    /// </para>
    /// **Validates: Requirements 1.4**
    /// </summary>
    [TestMethod]
    public void AcceptedRequests_ReturnStrictlyIncreasingUniqueIds_NeverTheSentinel()
    {
        Prop.ForAll(
                AcceptedRequestSequences(),
                coords =>
                {
                    using var picking = new PickingContext(new MockContext());

                    var acceptedIds = new List<uint>(coords.Length);
                    foreach (var coord in coords)
                    {
                        var requestId = picking.SetPendingSubmit(coord);

                        // Within capacity every request is accepted; the sentinel must never be returned.
                        if (requestId == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }

                        acceptedIds.Add(requestId);
                    }

                    // Req 1.4: accepted Request Ids are strictly increasing (which implies pairwise unique).
                    for (var i = 1; i < acceptedIds.Count; i++)
                    {
                        if (acceptedIds[i] <= acceptedIds[i - 1])
                        {
                            return false;
                        }
                    }

                    // Redundant with strict monotonicity, but asserts the uniqueness claim directly.
                    var allUnique = acceptedIds.Distinct().Count() == acceptedIds.Count;

                    // Req 1.4: no accepted id equals the overflow sentinel.
                    var noneSentinel = acceptedIds.TrueForAll(id => id != PickingContext.InvalidRequestId);

                    return allUnique && noneSentinel;
                }
            )
            .Check(DefaultConfig);
    }
}
