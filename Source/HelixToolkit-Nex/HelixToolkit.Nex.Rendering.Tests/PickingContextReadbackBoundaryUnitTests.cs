using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Example (unit) tests for <see cref="PickingContext"/> readback boundaries:
/// <list type="bullet">
/// <item>Fail-fast readback: a <c>ReadbackLocation</c> whose Byte Offset is outside the staging
/// buffer causes <see cref="PickingContext.ReadResult"/> to throw rather than read from an
/// incorrect location (Requirement 3.4).</item>
/// <item>Overflow boundary: the 64th request is accepted at Request Slot 63 (Byte Offset 504) and
/// the 65th request is rejected with <see cref="PickingContext.InvalidRequestId"/>
/// (Requirements 5.1, 5.2, 2.1).</item>
/// </list>
/// </summary>
[TestClass]
public class PickingContextReadbackBoundaryUnitTests
{
    /// <summary>The size in bytes of a single request's staging cell (one RG_F32 pixel).</summary>
    private const int CellSizeBytes = sizeof(ulong);

    /// <summary>The per-frame picking capacity under test.</summary>
    private const int Capacity = (int)GraphicsSettings.MaxRequestsPerFrame;

    /// <summary>The exclusive upper bound of any valid Byte Offset within a frame's staging buffer.</summary>
    private const int BufferSizeBytes = CellSizeBytes * Capacity;

    /// <summary>
    /// Req 3.4: when a request's resolved Byte Offset lands beyond the end of its staging buffer,
    /// <see cref="PickingContext.ReadResult"/> fails fast with
    /// <see cref="ArgumentOutOfRangeException"/> instead of reading from an incorrect location.
    /// </summary>
    [TestMethod]
    public void ReadResult_OffsetBeyondBuffer_ThrowsArgumentOutOfRange()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var picking = new PickingContext(mock);

        const uint requestId = 1u;
        // Offset one full cell past the end of the buffer: [0, BufferSizeBytes) is valid, so
        // BufferSizeBytes (= 8 * MaxRequestsPerFrame = 512) is the first out-of-bounds offset.
        var outOfBoundsOffset = BufferSizeBytes;
        picking.SetReadbackLocationForTest(requestId, frameSlot: 0, byteOffset: outOfBoundsOffset);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => picking.ReadResult(requestId));
    }

    /// <summary>
    /// Req 3.4: an offset that is in range for the buffer start but whose 8-byte read would spill
    /// past the buffer end also fails fast.
    /// </summary>
    [TestMethod]
    public void ReadResult_LastCellOverhangingBuffer_ThrowsArgumentOutOfRange()
    {
        using var mock = new MockContext();
        mock.Initialize();
        using var picking = new PickingContext(mock);

        const uint requestId = 2u;
        // Start inside the buffer but only 1 byte before the end: reading 8 bytes overhangs.
        var overhangingOffset = BufferSizeBytes - 1;
        picking.SetReadbackLocationForTest(requestId, frameSlot: 0, byteOffset: overhangingOffset);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => picking.ReadResult(requestId));
    }

    /// <summary>
    /// Req 5.1, 5.2, 2.1: submitting exactly <see cref="GraphicsSettings.MaxRequestsPerFrame"/>
    /// requests fills the frame; the 64nd accepted request occupies Request Slot 63 (Byte Offset
    /// 504 = 63 * 8), and the 65rd request is rejected with
    /// <see cref="PickingContext.InvalidRequestId"/>.
    /// </summary>
    [TestMethod]
    public void Overflow_ThirtySecondAtSlot31_ThirtyThirdRejected()
    {
        using var mock = new MockContext();
        using var picking = new PickingContext(mock);

        uint lastAcceptedId = PickingContext.InvalidRequestId;
        for (var i = 0; i < Capacity; i++)
        {
            var id = picking.SetPendingSubmit(new Vector2(i, i));
            Assert.AreNotEqual(
                PickingContext.InvalidRequestId,
                id,
                $"Request {i + 1} of {Capacity} should be accepted (frame not yet full)."
            );
            lastAcceptedId = id;
        }

        // The 64nd (last) accepted request occupies the highest Request Slot: 63.
        var lastSlot = picking.GetRequestSlot(lastAcceptedId);
        Assert.AreEqual(Capacity - 1, lastSlot, "The 64nd request must occupy Request Slot 63.");

        // Byte Offset for the last slot = 63 * 8 = 504, fully inside the 512-byte buffer.
        var lastByteOffset = lastSlot * CellSizeBytes;
        Assert.AreEqual(504, lastByteOffset, "Request Slot 63 must map to Byte Offset 504.");
        Assert.IsTrue(
            lastByteOffset + CellSizeBytes <= BufferSizeBytes,
            "The 64nd request's 8-byte cell must fit within the staging buffer."
        );

        // The 65th request overflows the frame and is rejected with the sentinel.
        var overflowId = picking.SetPendingSubmit(new Vector2(100, 100));
        Assert.AreEqual(
            PickingContext.InvalidRequestId,
            overflowId,
            "The 65th request must be rejected with InvalidRequestId."
        );
    }
}
