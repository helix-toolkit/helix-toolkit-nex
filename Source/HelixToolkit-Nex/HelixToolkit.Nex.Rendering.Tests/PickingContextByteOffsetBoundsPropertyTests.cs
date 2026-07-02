using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext"/> Byte Offset bounds and the fail-fast
/// readback path.
/// </summary>
[TestClass]
public class PickingContextByteOffsetBoundsPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>The number of bytes each accepted request occupies in its staging buffer (one RG_F32 pixel).</summary>
    private const int CellSizeBytes = sizeof(ulong);

    /// <summary>The exclusive upper bound of any valid Byte Offset within a frame's staging buffer.</summary>
    private const int BufferSizeBytes = CellSizeBytes * (int)GraphicsSettings.MaxRequestsPerFrame;

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
    /// </summary>
    private static Arbitrary<Vector2[]> AcceptedRequestSequences() =>
        Gen.Choose(0, (int)GraphicsSettings.MaxRequestsPerFrame)
            .SelectMany(n => Gen.ArrayOf(CoordGen(), n))
            .ToArbitrary();

    /// <summary>
    /// Generates a Request Id that was never accepted/recorded, so no <c>ReadbackLocation</c> is
    /// registered for it. Values are drawn from the high end of the <see cref="uint"/> range that
    /// the monotonic accepted-id counter cannot reach for a single-frame sequence, and never equal
    /// an id that was actually accepted below.
    /// </summary>
    private static Arbitrary<uint> UnregisteredRequestIds() =>
        Gen.Choose(1, int.MaxValue)
            .Select(i => (uint)i + (uint)GraphicsSettings.MaxRequestsPerFrame + 1u)
            .ToArbitrary();

    // Feature: multi-request-picking, Property 5: Byte offsets stay within buffer bounds
    /// <summary>
    /// Property 5: Byte offsets stay within buffer bounds.
    /// <para>
    /// For any accepted request assigned Request Slot <c>s</c> in
    /// <c>[0, MaxRequestsPerFrame)</c>, the computed Byte Offset satisfies <c>0 &lt;= offset</c> and
    /// <c>offset + sizeof(ulong) &lt;= sizeof(ulong) * MaxRequestsPerFrame</c>; and any readback
    /// whose resolved offset would fall outside these bounds fails fast rather than returning data
    /// from an incorrect location.
    /// </para>
    /// <para>
    /// Part 1 (Req 2.1): every accepted request's Byte Offset lies fully inside a staging buffer of
    /// <c>sizeof(ulong) * MaxRequestsPerFrame</c> bytes. Part 2 (Req 3.4): <see cref="PickingContext.ReadResult"/>
    /// fails fast (throwing rather than returning data from an incorrect location) for a request id
    /// that has no registered readback location.
    /// </para>
    /// **Validates: Requirements 2.1, 3.4**
    /// </summary>
    [TestMethod]
    public void AcceptedRequestByteOffsets_StayWithinBufferBounds_AndOutOfBoundsReadbackFailsFast()
    {
        Prop.ForAll(
                AcceptedRequestSequences(),
                UnregisteredRequestIds(),
                (Vector2[] coords, uint unregisteredId) =>
                {
                    using var picking = new PickingContext(new MockContext());

                    // Part 1 (Req 2.1): every accepted request's Byte Offset is fully in bounds of a
                    // buffer sized sizeof(ulong) * MaxRequestsPerFrame.
                    foreach (var coord in coords)
                    {
                        var requestId = picking.SetPendingSubmit(coord);
                        if (requestId == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }

                        var slot = picking.GetRequestSlot(requestId);
                        if (slot < 0)
                        {
                            return false;
                        }

                        var byteOffset = slot * CellSizeBytes;

                        // 0 <= offset and offset + sizeof(ulong) <= sizeof(ulong) * MaxRequestsPerFrame.
                        if (byteOffset < 0 || byteOffset + CellSizeBytes > BufferSizeBytes)
                        {
                            return false;
                        }
                    }

                    // Part 2 (Req 3.4): a readback that cannot resolve to an in-bounds location fails
                    // fast rather than returning data from an incorrect location. No copy was
                    // recorded (SendCommand was never called), so no request id has a registered
                    // ReadbackLocation; ReadResult must throw for the unregistered id.
                    var failedFast = false;
                    try
                    {
                        picking.ReadResult(unregisteredId);
                    }
                    catch (InvalidOperationException)
                    {
                        failedFast = true;
                    }

                    return failedFast;
                }
            )
            .Check(DefaultConfig);
    }
}
