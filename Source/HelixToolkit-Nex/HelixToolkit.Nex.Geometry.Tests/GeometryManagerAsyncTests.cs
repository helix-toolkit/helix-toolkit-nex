using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
[TestCategory("GPURequired")]
public sealed class GeometryManagerAsyncTests
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

    // -----------------------------------------------------------------------
    // Fire-and-forget overload: bool AddAsync(Geometry, out uint)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AddAsync_OutParam_SetsAttachedAndAssignsId()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 2, 3, 1), 64)],
        };

        var ok = _manager!.AddAsync(geometry, out uint id);

        Assert.IsTrue(ok, "AddAsync should return true for a new geometry.");
        Assert.IsTrue(geometry.Attached, "Geometry should be attached after AddAsync.");
        Assert.AreEqual(id, geometry.Id, "Id returned via out param should match geometry.Id.");
    }

    [TestMethod]
    public void AddAsync_OutParam_AlreadyManaged_ReturnsFalse()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(0, 1, 0, 1), 8)],
        };

        _manager!.AddAsync(geometry, out _);
        var ok = _manager.AddAsync(geometry, out _);

        Assert.IsFalse(ok, "AddAsync should return false when geometry already belongs to a manager.");
    }

    // -----------------------------------------------------------------------
    // Awaitable overload: Task<(bool Success, uint Id)> AddAsync(Geometry)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task AddAsync_Task_SuccessAndValidId()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 32)],
        };

        var (success, id) = await _manager!.AddAsync(geometry);

        Assert.IsTrue(success, "Task AddAsync should succeed for a new geometry.");
        Assert.AreEqual(id, geometry.Id, "Returned id should match geometry.Id.");
        Assert.IsTrue(geometry.Attached, "Geometry should be attached after awaiting AddAsync.");
    }

    [TestMethod]
    public async Task AddAsync_Task_NoPendingUpdateAfterAwait()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(0, 0, 1, 1), 64)],
        };

        await _manager!.AddAsync(geometry);

        Assert.IsFalse(
            geometry.HasPendingBufferUpdate,
            "HasPendingBufferUpdate should be false once the awaitable AddAsync completes."
        );
    }

    [TestMethod]
    public async Task AddAsync_Task_VertexBufferValid()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 1, 1, 1), 128)],
        };

        await _manager!.AddAsync(geometry);

        Assert.IsTrue(
            geometry.VertexBuffer.Valid,
            "VertexBuffer should be valid after awaiting AddAsync."
        );
    }

    [TestMethod]
    public async Task AddAsync_Task_FullGeometry_AllBuffersValid()
    {
        using var geometry = new Geometry(isDynamic: true)
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 0, 1, 1), 64)],
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 64)];

        await _manager!.AddAsync(geometry);

        Assert.IsTrue(geometry.VertexBuffer.Valid, "VertexBuffer should be valid.");
        Assert.IsTrue(geometry.IndexBuffer.Valid, "IndexBuffer should be valid for dynamic geometry with indices.");
        Assert.IsTrue(geometry.VertexColorBuffer.Valid, "VertexColorBuffer should be valid.");
    }

    [TestMethod]
    public async Task AddAsync_Task_AlreadyManaged_ReturnsFalse()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(0, 1, 1, 1), 16)],
        };

        await _manager!.AddAsync(geometry);
        var (success, id) = await _manager.AddAsync(geometry);

        Assert.IsFalse(success, "Second AddAsync of already-managed geometry should return Success=false.");
        Assert.AreEqual(0u, id, "Id should be 0 when AddAsync fails.");
    }

    [TestMethod]
    public async Task AddAsync_Task_CountIncrementsPerAdd()
    {
        var before = _manager!.Count;

        using var g1 = new Geometry { Vertices = [new Vector4(1, 0, 0, 1)] };
        using var g2 = new Geometry { Vertices = [new Vector4(0, 1, 0, 1)] };
        using var g3 = new Geometry { Vertices = [new Vector4(0, 0, 1, 1)] };

        await _manager.AddAsync(g1);
        await _manager.AddAsync(g2);
        await _manager.AddAsync(g3);

        Assert.AreEqual(before + 3, _manager.Count, "Count should increment by one per AddAsync.");
    }

    [TestMethod]
    public async Task AddAsync_Task_StaticGeometry_TotalIndexCountAccumulates()
    {
        const int indicesPerGeometry = 96;
        var before = _manager!.TotalStaticIndexCount;

        using var g1 = new Geometry(isDynamic: false)
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), indicesPerGeometry)],
            Indices = [.. Enumerable.Range(0, indicesPerGeometry).Select(i => (uint)i)],
        };
        using var g2 = new Geometry(isDynamic: false)
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(0, 1, 0, 1), indicesPerGeometry)],
            Indices = [.. Enumerable.Range(0, indicesPerGeometry).Select(i => (uint)i)],
        };

        await _manager.AddAsync(g1);
        await _manager.AddAsync(g2);

        Assert.AreEqual(
            before + indicesPerGeometry * 2,
            _manager.TotalStaticIndexCount,
            "TotalStaticIndexCount should accumulate index counts for static geometries."
        );
    }

    [TestMethod]
    public async Task AddAsync_Task_MultipleConcurrent_AllSucceed_UniqueIds()
    {
        const int count = 8;
        var geometries = Enumerable
            .Range(0, count)
            .Select(_ => new Geometry { Vertices = [.. Enumerable.Repeat(new Vector4(1, 1, 0, 1), 32)] })
            .ToArray();

        try
        {
            var tasks = geometries.Select(g => _manager!.AddAsync(g)).ToArray();
            var results = await Task.WhenAll(tasks);

            Assert.AreEqual(count, results.Length);
            Assert.IsTrue(results.All(r => r.Success), "All concurrent AddAsync calls should succeed.");

            var ids = results.Select(r => r.Id).ToHashSet();
            Assert.AreEqual(count, ids.Count, "All concurrent AddAsync results should have unique IDs.");
        }
        finally
        {
            foreach (var g in geometries)
                g.Dispose();
        }
    }

    [TestMethod]
    public async Task AddAsync_Task_EmptyVertices_Succeeds()
    {
        using var geometry = new Geometry { Vertices = [] };

        var (success, id) = await _manager!.AddAsync(geometry);

        Assert.IsTrue(success, "AddAsync should succeed even with empty vertex data.");
        Assert.IsTrue(geometry.Attached);
    }

    [TestMethod]
    public async Task Remove_AfterAsyncAdd_DecreasesCount()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vector4(1, 0, 1, 1), 32)],
        };

        await _manager!.AddAsync(geometry);
        var countAfterAdd = _manager.Count;

        var removed = _manager.Remove(geometry);

        Assert.IsTrue(removed, "Remove should return true for a managed geometry.");
        Assert.AreEqual(countAfterAdd - 1, _manager.Count, "Count should decrement after Remove.");
        Assert.IsFalse(geometry.Attached, "Geometry should no longer be attached after Remove.");
    }
}
