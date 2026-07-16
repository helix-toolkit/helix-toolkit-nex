using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext"/> per-frame Request Slot / Byte Offset
/// allocation.
/// </summary>
[TestClass]
public class PickingContextSlotOffsetPropertyTests
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

    // Feature: multi-request-picking, Property 1: Distinct offsets for same-frame accepted requests
    /// <summary>
    /// Property 1: Distinct offsets for same-frame accepted requests.
    /// <para>
    /// For any sequence of up to <see cref="GraphicsSettings.MaxRequestsPerFrame"/> accepted picking
    /// requests in a single frame, every accepted request is assigned a distinct Request Slot, so the
    /// Byte Offsets computed as <c>RequestSlot * sizeof(ulong)</c> are pairwise distinct and
    /// non-overlapping (each occupies its own 8-byte cell).
    /// </para>
    /// **Validates: Requirements 1.2, 1.3, 2.2, 2.4**
    /// </summary>
    [TestMethod]
    public void SameFrameAcceptedRequests_HaveDistinctNonOverlappingByteOffsets()
    {
        Prop.ForAll(
                AcceptedRequestSequences(),
                coords =>
                {
                    using var picking = new PickingContext(new MockContext());

                    var offsets = new List<int>(coords.Length);
                    foreach (var coord in coords)
                    {
                        // Req 1.2 / 1.4: each request within capacity is accepted (never the sentinel).
                        var requestId = picking.SetPendingSubmit(coord);
                        if (requestId == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }

                        // Req 1.3 / 2.4: each accepted request owns a distinct Request Slot.
                        var slot = picking.GetRequestSlot(requestId);
                        if (slot < 0)
                        {
                            return false;
                        }

                        // Req 2.2: Byte Offset = RequestSlot * sizeof(ulong).
                        var byteOffset = slot * CellSizeBytes;

                        // Each cell is 8-byte aligned and fully inside the staging buffer.
                        if (byteOffset % CellSizeBytes != 0)
                        {
                            return false;
                        }
                        if (byteOffset < 0 || byteOffset + CellSizeBytes > BufferSizeBytes)
                        {
                            return false;
                        }

                        offsets.Add(byteOffset);
                    }

                    // Req 2.4: offsets are pairwise distinct; since every cell is exactly 8 bytes and
                    // 8-byte aligned, distinct offsets are also non-overlapping.
                    return offsets.Distinct().Count() == offsets.Count;
                }
            )
            .Check(DefaultConfig);
    }
}
