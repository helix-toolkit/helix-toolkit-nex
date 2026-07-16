using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Geometries.Tests;

/// <summary>
/// Unit tests for <see cref="GeometryManager"/>'s deferred-removal API
/// (<see cref="GeometryManager.RemoveDeferred"/> / <see cref="GeometryManager.ProcessPendingRemovals"/>)
/// and the deferred <see cref="Geometry.Dispose"/> path.
///
/// Deferred removal keeps a geometry live in the pool until <c>ProcessPendingRemovals</c> runs
/// (the render loop calls it once per frame before the shared static-index buffer is rebuilt), so
/// the pool/<see cref="GeometryManager.TotalStaticIndexCount"/> bookkeeping changes at a single
/// controlled point instead of mid-frame.
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
public sealed class GeometryManagerDeferredRemovalTests
{
    private static IContext? _vkContext;
    private static GeometryManager? _manager;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig { TerminateOnValidationError = true };
        _vkContext = VulkanBuilder.CreateHeadless(config);
        _manager = new GeometryManager(_vkContext);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _manager?.Clear();
        _manager = null;
        _vkContext?.Dispose();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _manager!.Clear();
    }

    private static Geometry NewStaticGeometry(int count = 32)
    {
        return new Geometry(isDynamic: false)
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), count)],
            Indices = [.. Enumerable.Range(0, count).Select(i => (uint)i)],
        };
    }

    // -----------------------------------------------------------------------
    // RemoveDeferred does not remove immediately
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RemoveDeferred_DoesNotRemoveImmediately()
    {
        using var geometry = NewStaticGeometry();
        await _manager!.AddAsync(geometry);
        var countAfterAdd = _manager.Count;

        _manager.RemoveDeferred(geometry);

        Assert.AreEqual(
            countAfterAdd,
            _manager.Count,
            "RemoveDeferred must not change the pool count until ProcessPendingRemovals runs."
        );
        Assert.IsTrue(
            geometry.Valid,
            "Geometry must stay live in the pool until the deferred removal is processed."
        );
    }

    [TestMethod]
    public async Task ProcessPendingRemovals_RemovesDeferredGeometry()
    {
        using var geometry = NewStaticGeometry();
        await _manager!.AddAsync(geometry);
        var countAfterAdd = _manager.Count;

        _manager.RemoveDeferred(geometry);
        _manager.ProcessPendingRemovals();

        Assert.AreEqual(
            countAfterAdd - 1,
            _manager.Count,
            "ProcessPendingRemovals must remove the deferred geometry from the pool."
        );
        Assert.IsFalse(
            geometry.Valid,
            "Geometry must be detached after the deferred removal is processed."
        );
    }

    [TestMethod]
    public void ProcessPendingRemovals_NoPending_IsNoOp()
    {
        var before = _manager!.Count;

        // No removals queued: must not throw and must not change state.
        _manager.ProcessPendingRemovals();

        Assert.AreEqual(before, _manager.Count);
    }

    // -----------------------------------------------------------------------
    // TotalStaticIndexCount bookkeeping is deferred too
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RemoveDeferred_StaticGeometry_TotalIndexCountUnchangedUntilProcessed()
    {
        const int indicesPerGeometry = 48;
        using var geometry = NewStaticGeometry(indicesPerGeometry);
        await _manager!.AddAsync(geometry);

        var indexCountAfterAdd = _manager.TotalStaticIndexCount;
        Assert.AreEqual(
            indicesPerGeometry,
            indexCountAfterAdd,
            "Adding a static geometry should accumulate its index count."
        );

        _manager.RemoveDeferred(geometry);
        Assert.AreEqual(
            indexCountAfterAdd,
            _manager.TotalStaticIndexCount,
            "TotalStaticIndexCount must not change on RemoveDeferred (offsets are not disturbed yet)."
        );

        _manager.ProcessPendingRemovals();
        Assert.AreEqual(
            0,
            _manager.TotalStaticIndexCount,
            "TotalStaticIndexCount must drop once the deferred removal is processed."
        );
    }

    // -----------------------------------------------------------------------
    // Deferred removal preserves other geometries (the offset-stability concern)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessPendingRemovals_PreservesOtherGeometries()
    {
        using var g1 = NewStaticGeometry();
        using var g2 = NewStaticGeometry();
        using var g3 = NewStaticGeometry();
        await _manager!.AddAsync(g1);
        await _manager.AddAsync(g2);
        await _manager.AddAsync(g3);
        var countAfterAdds = _manager.Count;

        _manager.RemoveDeferred(g2);
        _manager.ProcessPendingRemovals();

        Assert.AreEqual(countAfterAdds - 1, _manager.Count, "Only the deferred geometry is removed.");
        Assert.IsTrue(g1.Valid, "Unrelated geometry g1 must remain valid.");
        Assert.IsFalse(g2.Valid, "The deferred-removed geometry g2 must be detached.");
        Assert.IsTrue(g3.Valid, "Unrelated geometry g3 must remain valid.");
    }

    // -----------------------------------------------------------------------
    // Idempotency / dedup
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task RemoveDeferred_Twice_ProcessRemovesOnce()
    {
        using var geometry = NewStaticGeometry();
        await _manager!.AddAsync(geometry);
        var countAfterAdd = _manager.Count;

        _manager.RemoveDeferred(geometry);
        _manager.RemoveDeferred(geometry); // duplicate enqueue must be coalesced

        _manager.ProcessPendingRemovals();

        Assert.AreEqual(
            countAfterAdd - 1,
            _manager.Count,
            "A geometry queued twice must be removed exactly once."
        );

        // A second process pass with nothing queued is a harmless no-op.
        _manager.ProcessPendingRemovals();
        Assert.AreEqual(countAfterAdd - 1, _manager.Count);
    }

    // -----------------------------------------------------------------------
    // Geometry.Dispose routes through deferred removal
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GeometryDispose_DefersRemoval_UntilProcessed()
    {
        var geometry = NewStaticGeometry();
        await _manager!.AddAsync(geometry);
        var countAfterAdd = _manager.Count;

        // Dispose should enqueue a deferred removal rather than removing synchronously.
        geometry.Dispose();

        Assert.AreEqual(
            countAfterAdd,
            _manager.Count,
            "Geometry.Dispose must defer the pool removal (no immediate count change)."
        );

        _manager.ProcessPendingRemovals();

        Assert.AreEqual(
            countAfterAdd - 1,
            _manager.Count,
            "The disposed geometry must be removed once the deferred removal is processed."
        );
    }

    // -----------------------------------------------------------------------
    // Foreign / unmanaged geometry is rejected
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RemoveDeferred_ForeignGeometry_IsNoOp()
    {
        using var foreign = NewStaticGeometry();
        var before = _manager!.Count;

        // 'foreign' was never added to this manager: RemoveDeferred must reject it and enqueue nothing.
        _manager.RemoveDeferred(foreign);
        _manager.ProcessPendingRemovals();

        Assert.AreEqual(before, _manager.Count, "A geometry not owned by the manager must not be removed.");
    }

    // -----------------------------------------------------------------------
    // Clear discards pending removals (and removes everything anyway)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Clear_DiscardsPendingRemovals()
    {
        using var g1 = NewStaticGeometry();
        using var g2 = NewStaticGeometry();
        await _manager!.AddAsync(g1);
        await _manager.AddAsync(g2);

        _manager.RemoveDeferred(g1);

        _manager.Clear();
        Assert.AreEqual(0, _manager.Count, "Clear must remove all pooled geometries.");
        Assert.AreEqual(0, _manager.TotalStaticIndexCount, "Clear must reset the static index count.");

        // The pending queue was discarded by Clear, so a subsequent process pass is a no-op and
        // must not attempt to re-remove the already-removed geometry.
        _manager.ProcessPendingRemovals();
        Assert.AreEqual(0, _manager.Count);
    }
}
