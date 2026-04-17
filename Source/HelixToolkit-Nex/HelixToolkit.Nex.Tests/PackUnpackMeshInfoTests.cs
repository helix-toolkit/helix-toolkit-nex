using HelixToolkit.Nex.Engine;

namespace HelixToolkit.Nex.Tests;

[TestClass]
public sealed class PackUnpackMeshInfoTests
{
    // ── Round-trip with current limits ─────────────────────────────────────

    [TestMethod]
    public void RoundTrip_ZeroValues_ShouldReturnZeros()
    {
        Utils.PackMeshInfo(0, 0, 0, 0, out uint r, out uint g);
        Utils.UnpackMeshInfo(
            r,
            g,
            out uint worldId,
            out uint entityId,
            out uint instanceId,
            out uint primitiveId
        );

        Assert.AreEqual(0u, worldId);
        Assert.AreEqual(0u, entityId);
        Assert.AreEqual(0u, instanceId);
        Assert.AreEqual(0u, primitiveId);
    }

    [TestMethod]
    public void RoundTrip_MaxValues_ShouldReturnOriginals()
    {
        uint wId = Limits.MaxWorldId;
        uint eId = Limits.MaxEntityId;
        uint inst = Limits.MaxInstanceCount;
        uint prim = Limits.MaxPrimitiveCount;

        Utils.PackMeshInfo(wId, eId, inst, prim, out uint r, out uint g);
        Utils.UnpackMeshInfo(
            r,
            g,
            out uint worldId,
            out uint entityId,
            out uint instanceId,
            out uint primitiveId
        );

        Assert.AreEqual(wId, worldId);
        Assert.AreEqual(eId, entityId);
        Assert.AreEqual(inst, instanceId);
        Assert.AreEqual(prim, primitiveId);
    }

    [TestMethod]
    public void RoundTrip_TypicalValues_ShouldReturnOriginals()
    {
        uint wId = 5;
        uint eId = 1234;
        uint inst = 42;
        uint prim = 99;

        Utils.PackMeshInfo(wId, eId, inst, prim, out uint r, out uint g);
        Utils.UnpackMeshInfo(
            r,
            g,
            out uint worldId,
            out uint entityId,
            out uint instanceId,
            out uint primitiveId
        );

        Assert.AreEqual(wId, worldId);
        Assert.AreEqual(eId, entityId);
        Assert.AreEqual(inst, instanceId);
        Assert.AreEqual(prim, primitiveId);
    }

    [TestMethod]
    public void RoundTrip_WorldIdOnly_ShouldReturnOriginal()
    {
        for (uint w = 0; w <= Limits.MaxWorldId; w++)
        {
            Utils.PackMeshInfo(w, 0, 0, 0, out uint r, out uint g);
            Utils.UnpackMeshInfo(r, g, out uint worldId, out _, out _, out _);
            Assert.AreEqual(w, worldId, $"Failed for worldId={w}");
        }
    }

    [TestMethod]
    public void RoundTrip_EntityIdBoundary_ShouldReturnOriginals()
    {
        uint[] entityIds = [0, 1, 255, 256, 1000, 32767, 65534, Limits.MaxEntityId];

        foreach (uint eId in entityIds)
        {
            Utils.PackMeshInfo(1, eId, 0, 0, out uint r, out uint g);
            Utils.UnpackMeshInfo(r, g, out _, out uint entityId, out _, out _);
            Assert.AreEqual(eId, entityId, $"Failed for entityId={eId}");
        }
    }

    [TestMethod]
    public void RoundTrip_InstanceIndexSpansXAndY_ShouldReturnOriginal()
    {
        // Instance index is split across X (low bits) and Y (high bits).
        // The split point is derived from limits, so use LimitsShaderConstants.
        uint instanceLowMax = LimitsShaderConstants.InstanceLowMask;
        uint[] instanceIndices =
        [
            0,
            1,
            instanceLowMax, // max low bits only
            instanceLowMax + 1, // first bit in high portion
            instanceLowMax + 2, // bits in both low and high
            Limits.MaxInstanceCount, // max instance count
            Limits.MaxInstanceCount / 2,
        ];

        foreach (uint inst in instanceIndices)
        {
            Utils.PackMeshInfo(3, 100, inst, 0, out uint r, out uint g);
            Utils.UnpackMeshInfo(r, g, out _, out _, out uint instanceId, out _);
            Assert.AreEqual(inst, instanceId, $"Failed for instanceIndex={inst} (0x{inst:X})");
        }
    }

    [TestMethod]
    public void RoundTrip_PrimitiveIdBoundary_ShouldReturnOriginals()
    {
        uint[] primitiveIds = [0, 1, 1000, Limits.MaxPrimitiveCount / 2, Limits.MaxPrimitiveCount];

        foreach (uint prim in primitiveIds)
        {
            Utils.PackMeshInfo(1, 1, 0, prim, out uint r, out uint g);
            Utils.UnpackMeshInfo(r, g, out _, out _, out _, out uint primitiveId);
            Assert.AreEqual(prim, primitiveId, $"Failed for primitiveId={prim}");
        }
    }

    [TestMethod]
    public void RoundTrip_AllFieldsNonZero_ShouldReturnOriginals()
    {
        // Test a variety of combinations where all fields are non-zero
        (uint w, uint e, uint i, uint p)[] cases =
        [
            (1, 1, 1, 1),
            (Limits.MaxWorldId, Limits.MaxEntityId, Limits.MaxInstanceCount, Limits.MaxPrimitiveCount),
            (7, Limits.MaxEntityId / 2, Limits.MaxInstanceCount / 2, Limits.MaxPrimitiveCount / 2),
            (1, 100, 5000, 300000),
            (14, 60000, 100000, Limits.MaxPrimitiveCount - 1),
        ];

        foreach (var (w, e, i, p) in cases)
        {
            Utils.PackMeshInfo(w, e, i, p, out uint r, out uint g);
            Utils.UnpackMeshInfo(
                r,
                g,
                out uint worldId,
                out uint entityId,
                out uint instanceId,
                out uint primitiveId
            );

            Assert.AreEqual(w, worldId, $"worldId mismatch for ({w},{e},{i},{p})");
            Assert.AreEqual(e, entityId, $"entityId mismatch for ({w},{e},{i},{p})");
            Assert.AreEqual(i, instanceId, $"instanceId mismatch for ({w},{e},{i},{p})");
            Assert.AreEqual(p, primitiveId, $"primitiveId mismatch for ({w},{e},{i},{p})");
        }
    }

    // ── Pack output matches expected bit layout ───────────────────────────

    [TestMethod]
    public void Pack_ShouldMatchExpectedBitLayout()
    {
        // Use values that fit within the new limits
        uint wId = 0x5;
        uint eId = 0xABCD; // fits in 18 bits
        uint inst = 0x12345; // fits in 21 bits
        uint prim = 0x6789A; // fits in 21 bits (0x6789A = 423066 < 2097151)

        // Verify values fit within limits
        Assert.IsTrue(eId <= Limits.MaxEntityId);
        Assert.IsTrue(inst <= Limits.MaxInstanceCount);
        Assert.IsTrue(prim <= Limits.MaxPrimitiveCount);

        Utils.PackMeshInfo(wId, eId, inst, prim, out uint r, out uint g);

        // X channel: worldId | entityId << EntityIdShift | instanceLow << InstanceLowShift
        uint instanceLow = inst & LimitsShaderConstants.InstanceLowMask;
        uint expectedR =
            wId
            | (eId << LimitsShaderConstants.EntityIdShift)
            | (instanceLow << LimitsShaderConstants.InstanceLowShift);
        Assert.AreEqual(
            expectedR,
            r,
            $"X channel mismatch: expected 0x{expectedR:X8}, got 0x{r:X8}"
        );

        // Y channel: instanceHigh | primitiveId << PrimitiveIdShift
        uint instanceHigh =
            (inst >> LimitsShaderConstants.InstanceLowBits)
            & LimitsShaderConstants.InstanceHighMask;
        uint expectedG = instanceHigh | (prim << LimitsShaderConstants.PrimitiveIdShift);
        Assert.AreEqual(
            expectedG,
            g,
            $"Y channel mismatch: expected 0x{expectedG:X8}, got 0x{g:X8}"
        );
    }

    // ── Overflow / masking behavior ───────────────────────────────────────

    [TestMethod]
    public void Pack_ValuesExceedingLimits_ShouldBeMasked()
    {
        // Values larger than limits should be masked to fit
        uint wId = 0xFF; // exceeds MaxWorldId (0xF)
        uint eId = 0xFFFFF; // exceeds MaxEntityId (0xFFFF)
        uint inst = 0xFFFFFF; // exceeds MaxInstanceCount (0x3FFFFF)
        uint prim = 0xFFFFFF; // exceeds MaxIndexCount (0x3FFFFF)

        Utils.PackMeshInfo(wId, eId, inst, prim, out uint r, out uint g);
        Utils.UnpackMeshInfo(
            r,
            g,
            out uint worldId,
            out uint entityId,
            out uint instanceId,
            out uint primitiveId
        );

        // Should be masked to max values
        Assert.AreEqual(Limits.MaxWorldId, worldId);
        Assert.AreEqual(Limits.MaxEntityId, entityId);
        Assert.AreEqual(Limits.MaxInstanceCount, instanceId);
        Assert.AreEqual(Limits.MaxPrimitiveCount, primitiveId);
    }

    // ── Derived constants consistency ─────────────────────────────────────

    [TestMethod]
    public void DerivedConstants_XChannelBitsSumTo32()
    {
        Assert.AreEqual(
            32,
            LimitsShaderConstants.WorldIdBits
                + LimitsShaderConstants.EntityIdBits
                + LimitsShaderConstants.InstanceLowBits
        );
    }

    [TestMethod]
    public void DerivedConstants_YChannelBitsSumTo32()
    {
        Assert.AreEqual(
            32,
            LimitsShaderConstants.InstanceHighBits + LimitsShaderConstants.IndexCountBits
        );
    }

    [TestMethod]
    public void DerivedConstants_InstanceBitsSplitCorrectly()
    {
        Assert.AreEqual(
            LimitsShaderConstants.InstanceCountBits,
            LimitsShaderConstants.InstanceLowBits + LimitsShaderConstants.InstanceHighBits
        );
    }

    [TestMethod]
    public void DerivedConstants_MasksMatchExpectedValues()
    {
        Assert.AreEqual(Limits.MaxWorldId, LimitsShaderConstants.WorldIdMask);
        Assert.AreEqual(Limits.MaxEntityId, LimitsShaderConstants.EntityIdMask);
        Assert.AreEqual(Limits.MaxInstanceCount, LimitsShaderConstants.InstanceCountMask);
        Assert.AreEqual(Limits.MaxPrimitiveCount, LimitsShaderConstants.IndexCountMask);
    }
}
