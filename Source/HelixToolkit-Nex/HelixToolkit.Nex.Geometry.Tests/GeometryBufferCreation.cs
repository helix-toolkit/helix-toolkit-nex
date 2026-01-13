using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
[TestCategory("GPURequired")]
public sealed class GeometryBufferCreation
{
    private static IContext? _vkContext;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig { TerminateOnValidationError = true };
        _vkContext = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _vkContext?.Dispose();
    }

    [TestMethod]
    public void TestVertexBufferUpload()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 1024)],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Vertex buffer creation failed with error: " + result.ToString()
        );
    }

    [TestMethod]
    public void TestIndexBufferUpload()
    {
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, 1024).Select(i => (uint)i)],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Index buffer creation failed with error: " + result.ToString()
        );
    }

    [TestMethod]
    public void TestBiNormalBufferUpload()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 1024)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 1, 0), 1024)];
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.VertexColor);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "VertexColor buffer creation failed with error: " + result.ToString()
        );
    }

    [TestMethod]
    public void TestEmptyVertexBuffer()
    {
        using var geometry = new Geometry { Vertices = [] };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Empty vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsFalse(geometry.VertexBuffer.Valid, "Empty vertex buffer should not be valid");
    }

    [TestMethod]
    public void TestEmptyIndexBuffer()
    {
        using var geometry = new Geometry { Indices = [] };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Empty index buffer creation failed with error: " + result.ToString()
        );
        Assert.IsFalse(geometry.IndexBuffer.Valid, "Empty index buffer should not be valid");
    }

    [TestMethod]
    public void TestCombinedBuffersUpload()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 512)],
            Indices = [.. Enumerable.Range(0, 512).Select(i => (uint)i)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 1, 0), 512)];

        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Combined buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Vertex buffer should be valid");
        Assert.IsTrue(geometry.IndexBuffer.Valid, "Index buffer should be valid");
        Assert.IsTrue(geometry.BiNormalBuffer.Valid, "VertexColor buffer should be valid");
    }

    [TestMethod]
    public void TestSingleVertexBuffer()
    {
        using var geometry = new Geometry
        {
            Vertices =
            [
                new Vertex(new Vector3(1, 2, 3), new Vector3(0, 1, 0), new Vector2(0.5f, 0.5f)),
            ],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Single vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Single vertex buffer should be valid");
    }

    [TestMethod]
    public void TestLargeVertexBuffer()
    {
        const int vertexCount = 100000;
        using var geometry = new Geometry
        {
            Vertices =
            [
                .. Enumerable
                    .Range(0, vertexCount)
                    .Select(i => new Vertex(
                        new Vector3(i, i + 1, i + 2),
                        new Vector3(0, 1, 0),
                        new Vector2(i * 0.01f, i * 0.01f)
                    )),
            ],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Large vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Large vertex buffer should be valid");
    }

    [TestMethod]
    public void TestLargeIndexBuffer()
    {
        const int indexCount = 100000;
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, indexCount).Select(i => (uint)i)],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Large index buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.IndexBuffer.Valid, "Large index buffer should be valid");
    }

    [TestMethod]
    public void TestVertexBufferWithVariedData()
    {
        using var geometry = new Geometry
        {
            Vertices =
            [
                new Vertex(
                    new Vector3(0, 0, 0),
                    new Vector3(0, 0, 1),
                    new Vector2(0, 0),
                    new Vector4(1, 0, 0, 1)
                ),
                new Vertex(
                    new Vector3(1, 0, 0),
                    new Vector3(0, 0, 1),
                    new Vector2(1, 0),
                    new Vector4(0, 1, 0, 1)
                ),
                new Vertex(
                    new Vector3(0, 1, 0),
                    new Vector3(0, 0, 1),
                    new Vector2(0, 1),
                    new Vector4(0, 0, 1, 1)
                ),
                new Vertex(
                    new Vector3(1, 1, 0),
                    new Vector3(0, 0, 1),
                    new Vector2(1, 1),
                    new Vector4(1, 1, 0, 1)
                ),
            ],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Varied vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Varied vertex buffer should be valid");
    }

    [TestMethod]
    public void TestDynamicVertexBuffer()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 256)],
            IsDynamic = true,
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Dynamic vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Dynamic vertex buffer should be valid");
    }

    [TestMethod]
    public void TestDynamicIndexBuffer()
    {
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, 256).Select(i => (uint)i)],
            IsDynamic = true,
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Dynamic index buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.IndexBuffer.Valid, "Dynamic index buffer should be valid");
    }

    [TestMethod]
    public void TestBufferUpdateMultipleTimes()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 128)],
        };

        // First update
        var result1 = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(ResultCode.Ok, result1, "First buffer update failed");
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Buffer should be valid after first update");

        // Second update (should replace the buffer)
        geometry.Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(4, 5, 6)), 256)];
        var result2 = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(ResultCode.Ok, result2, "Second buffer update failed");
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Buffer should be valid after second update");
    }

    [TestMethod]
    public void TestBiNormalBufferWithMismatchedVertexCount()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 512)],
        };
        // Mismatched count - only 256 binormals for 512 vertices
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 1, 0), 256)];

        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.VertexColor);
        Assert.AreEqual(ResultCode.Ok, result, "VertexColor buffer update should complete");
        // VertexColor buffer should not be created due to count mismatch
        Assert.IsFalse(
            geometry.BiNormalBuffer.Valid,
            "VertexColor buffer should not be valid with mismatched count"
        );
    }

    [TestMethod]
    public void TestVertexBufferWithAllEmptyComponents()
    {
        using var geometry = new Geometry { Vertices = [.. Enumerable.Repeat(Vertex.Empty, 64)] };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Empty component vertex buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Empty component vertex buffer should be valid");
    }

    [TestMethod]
    public void TestBiNormalBufferWithEmptyComponents()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(Vector4.Zero, 64)];

        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.VertexColor);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Empty component VertexColor buffer creation failed with error: " + result.ToString()
        );
        Assert.IsTrue(
            geometry.BiNormalBuffer.Valid,
            "Empty component VertexColor buffer should be valid"
        );
    }

    [TestMethod]
    public void TestGeometryBufferDisposal()
    {
        var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 128)],
            Indices = [.. Enumerable.Range(0, 128).Select(i => (uint)i)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 1, 0, 0), 128)];

        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.AreEqual(ResultCode.Ok, result, "Buffer creation failed");

        // Dispose geometry
        geometry.Dispose();

        // Buffers should be disposed
        Assert.IsFalse(
            geometry.VertexBuffer.Valid,
            "Vertex buffer should be invalid after disposal"
        );
        Assert.IsFalse(geometry.IndexBuffer.Valid, "Index buffer should be invalid after disposal");
        Assert.IsFalse(
            geometry.BiNormalBuffer.Valid,
            "VertexColor buffer should be invalid after disposal"
        );
    }

    [TestMethod]
    public void TestPartialBufferUpdate()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 128)],
            Indices = [.. Enumerable.Range(0, 128).Select(i => (uint)i)],
        };

        // Update only vertex buffer
        var result1 = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(ResultCode.Ok, result1, "Vertex buffer update failed");
        Assert.IsTrue(geometry.VertexBuffer.Valid, "Vertex buffer should be valid");
        Assert.IsFalse(geometry.IndexBuffer.Valid, "Index buffer should not be valid yet");

        // Update only index buffer
        var result2 = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(ResultCode.Ok, result2, "Index buffer update failed");
        Assert.IsTrue(geometry.IndexBuffer.Valid, "Index buffer should be valid");
    }

    #region BufferDirty Flag Tests

    [TestMethod]
    public void TestBufferDirtyInitialState()
    {
        using var geometry = new Geometry();

        // BufferDirty should initially be set to All
        Assert.AreEqual(
            GeometryBufferType.All,
            geometry.BufferDirty,
            "BufferDirty should initially be set to All"
        );
    }

    [TestMethod]
    public void TestBufferDirtyClearedAfterVertexUpdate()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };

        // Initially all buffers are dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer should be marked as dirty initially"
        );

        // Update vertex buffer
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);

        // Vertex dirty flag should be cleared
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer dirty flag should be cleared after update"
        );

        // Other buffers should still be dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer should still be marked as dirty"
        );
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer should still be marked as dirty"
        );
    }

    [TestMethod]
    public void TestBufferDirtyClearedAfterIndexUpdate()
    {
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };

        // Initially all buffers are dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer should be marked as dirty initially"
        );

        // Update index buffer
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);

        // Index dirty flag should be cleared
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer dirty flag should be cleared after update"
        );

        // Other buffers should still be dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer should still be marked as dirty"
        );
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer should still be marked as dirty"
        );
    }

    [TestMethod]
    public void TestBufferDirtyClearedAfterBiNormalUpdate()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 1, 0, 0), 64)];

        // Initially all buffers are dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer should be marked as dirty initially"
        );

        // Update binormal buffer
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.VertexColor);

        // VertexColor dirty flag should be cleared
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer dirty flag should be cleared after update"
        );

        // Other buffers should still be dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer should still be marked as dirty"
        );
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer should still be marked as dirty"
        );
    }

    [TestMethod]
    public void TestBufferDirtyClearedAfterAllUpdate()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 64)];

        // Update all buffers
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);

        // All dirty flags should be cleared
        Assert.AreEqual(
            (GeometryBufferType)0,
            geometry.BufferDirty,
            "BufferDirty should be cleared (0) after updating all buffers"
        );
    }

    [TestMethod]
    public void TestBufferDirtySetWhenVerticesModified()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };

        // Update buffers to clear dirty flag
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer dirty flag should be cleared after update"
        );

        // Modify vertices
        geometry.Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(4, 5, 6)), 128)];

        // Vertex dirty flag should be set again
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer dirty flag should be set after vertices are modified"
        );
    }

    [TestMethod]
    public void TestBufferDirtySetWhenIndicesModified()
    {
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };

        // Update buffers to clear dirty flag
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer dirty flag should be cleared after update"
        );

        // Modify indices
        geometry.Indices = [.. Enumerable.Range(0, 128).Select(i => (uint)i)];

        // Index dirty flag should be set again
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer dirty flag should be set after indices are modified"
        );
    }

    [TestMethod]
    public void TestBufferDirtySetWhenBiNormalsModified()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 64)];

        // Update buffers to clear dirty flag
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer dirty flag should be cleared after update"
        );

        // Modify binormals
        geometry.VertexColors = [.. Enumerable.Repeat(new Vector4(1, 0, 0, 1), 128)];

        // VertexColor dirty flag should be set again
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.VertexColor),
            "VertexColor buffer dirty flag should be set after binormals are modified"
        );
    }

    [TestMethod]
    public void TestBufferDirtyParameterlessUpdateUsesFlag()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };

        // Manually clear only the vertex dirty flag
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);

        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex should not be dirty"
        );
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index should be dirty"
        );

        // Call parameterless UpdateBuffers - should only update dirty buffers (Index in this case)
        var result = geometry.UpdateBuffers(_vkContext!);

        Assert.AreEqual(ResultCode.Ok, result, "Parameterless update should succeed");
        Assert.IsTrue(geometry.IndexBuffer.Valid, "Index buffer should now be valid");
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index dirty flag should be cleared after parameterless update"
        );
    }

    [TestMethod]
    public void TestBufferDirtyManualSet()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
        };

        // Update and clear dirty flags
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.AreEqual((GeometryBufferType)0, geometry.BufferDirty, "All buffers should be clean");

        // Manually set vertex buffer as dirty
        geometry.BufferDirty = GeometryBufferType.Vertex;

        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer should be manually marked as dirty"
        );
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index buffer should not be dirty"
        );
    }

    [TestMethod]
    public void TestBufferDirtyMultipleModifications()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 64)],
            Indices = [.. Enumerable.Range(0, 64).Select(i => (uint)i)],
        };

        // Clear all dirty flags
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.All);
        Assert.AreEqual((GeometryBufferType)0, geometry.BufferDirty, "All buffers should be clean");

        // Modify vertices
        geometry.Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(4, 5, 6)), 32)];
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex should be dirty"
        );

        // Modify indices
        geometry.Indices = [.. Enumerable.Range(0, 32).Select(i => (uint)i)];
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Index),
            "Index should be dirty"
        );

        // Both should be marked as dirty
        Assert.IsTrue(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex | GeometryBufferType.Index),
            "Both Vertex and Index buffers should be dirty"
        );
    }

    [TestMethod]
    public void TestBufferDirtyWithEmptyBufferUpdate()
    {
        using var geometry = new Geometry { Vertices = [] };

        // Update empty vertex buffer
        geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);

        // Dirty flag should still be cleared even though no buffer was created
        Assert.IsFalse(
            geometry.BufferDirty.HasFlag(GeometryBufferType.Vertex),
            "Vertex buffer dirty flag should be cleared even for empty buffer"
        );
    }

    #endregion
}
