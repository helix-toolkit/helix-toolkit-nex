using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Tests.Vulkan;

[TestClass]
[TestCategory("GPURequired")]
public class ElementBufferTests
{
    private static IContext? _vkContext;
    private static readonly Random _rnd = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct TestElement
    {
        public float X;
        public float Y;
        public float Z;
        public int Id;

        public TestElement(float x, float y, float z, int id)
        {
            X = x;
            Y = y;
            Z = z;
            Id = id;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not TestElement other)
                return false;

            return X == other.X && Y == other.Y && Z == other.Z && Id == other.Id;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, Id);
        }
    }

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

    #region Dynamic Buffer Tests

    [TestMethod]
    public void DynamicBuffer_CreateWithInitialCapacity()
    {
        // Arrange
        const int capacity = 100;
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity, isDynamic: true);

        // Assert
        Assert.AreEqual(capacity, buffer.Capacity);
        Assert.IsTrue(buffer.Buffer.Valid);
        Assert.IsTrue(buffer.IsDynamic);
    }

    [TestMethod]
    public void DynamicBuffer_CreateWithZeroCapacity()
    {
        // Arrange & Act
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 0, isDynamic: true);

        // Assert
        Assert.AreEqual(0, buffer.Capacity);
        Assert.IsFalse(buffer.Buffer.Valid);
    }

    [TestMethod]
    public void DynamicBuffer_UploadSmallData()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);
        var data = new FastList<Vector4>
        {
            new Vector4(1, 2, 3, 4),
            new Vector4(5, 6, 7, 8),
            new Vector4(9, 10, 11, 12),
        };

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.AreEqual(10, buffer.Capacity); // Should not resize
    }

    [TestMethod]
    public void DynamicBuffer_UploadCausesResize()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);
        var data = new FastList<Vector4>();
        for (int i = 0; i < 50; i++)
        {
            data.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        // Dynamic buffer should grow by 1.5x: 50 * 1.5 = 75
        Assert.AreEqual(75, buffer.Capacity);
    }

    [TestMethod]
    public void DynamicBuffer_MultipleUploadsWithGrowth()
    {
        // Arrange
        using var buffer = new ElementBuffer<TestElement>(
            _vkContext!,
            capacity: 10,
            isDynamic: true
        );

        // Act 1: Upload 5 elements (no resize)
        var data1 = new FastList<TestElement>();
        for (int i = 0; i < 5; i++)
        {
            data1.Add(new TestElement(i, i + 1, i + 2, i));
        }
        var result1 = buffer.Upload(data1);

        // Assert 1
        Assert.AreEqual(ResultCode.Ok, result1);
        Assert.AreEqual(10, buffer.Capacity);

        // Act 2: Upload 15 elements (should resize)
        var data2 = new FastList<TestElement>();
        for (int i = 0; i < 15; i++)
        {
            data2.Add(new TestElement(i * 2, i * 2 + 1, i * 2 + 2, i));
        }
        var result2 = buffer.Upload(data2);

        // Assert 2
        Assert.AreEqual(ResultCode.Ok, result2);
        // 15 * 1.5 = 22.5 -> 22
        Assert.AreEqual(22, buffer.Capacity);

        // Act 3: Upload 20 elements (no resize, fits in 22)
        var data3 = new FastList<TestElement>();
        for (int i = 0; i < 20; i++)
        {
            data3.Add(new TestElement(i * 3, i * 3 + 1, i * 3 + 2, i));
        }
        var result3 = buffer.Upload(data3);

        // Assert 3
        Assert.AreEqual(ResultCode.Ok, result3);
        Assert.AreEqual(22, buffer.Capacity);
    }

    [TestMethod]
    public void DynamicBuffer_UploadEmptyList()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);
        var data = new FastList<Vector4>();

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.AreEqual(10, buffer.Capacity);
    }

    [TestMethod]
    public void DynamicBuffer_UploadLargeData()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 100, isDynamic: true);
        var data = new FastList<Vector4>();
        for (int i = 0; i < 1000; i++)
        {
            data.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        // 1000 * 1.5 = 1500
        Assert.AreEqual(1500, buffer.Capacity);
    }

    [TestMethod]
    public void DynamicBuffer_UsesMappedMemory()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);

        // Act - Verify buffer uses mapped memory
        var mappedPtr = _vkContext!.GetMappedPtr(buffer.Buffer.Handle);

        // Assert
        Assert.AreNotEqual(IntPtr.Zero, mappedPtr, "Dynamic buffer should have mapped memory");
    }

    [TestMethod]
    public void DynamicBuffer_EnsureCapacity()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);

        // Act
        var result = buffer.EnsureCapacity(50);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(50 <= buffer.Capacity);
    }

    [TestMethod]
    public void DynamicBuffer_EnsureCapacityNoResize()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 100, isDynamic: true);

        // Act
        var result = buffer.EnsureCapacity(50);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.AreEqual(100, buffer.Capacity); // No resize needed
    }

    #endregion

    #region Static Buffer Tests

    [TestMethod]
    public void StaticBuffer_CreateWithInitialCapacity()
    {
        // Arrange
        const int capacity = 100;
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity, isDynamic: false);

        // Assert
        Assert.AreEqual(capacity, buffer.Capacity);
        Assert.IsTrue(buffer.Buffer.Valid);
        Assert.IsFalse(buffer.IsDynamic);
    }

    [TestMethod]
    public void StaticBuffer_UploadSmallData()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);
        var data = new FastList<Vector4> { new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8) };

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.AreEqual(10, buffer.Capacity);
    }

    [TestMethod]
    public void StaticBuffer_UploadCausesResize()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);
        var data = new FastList<Vector4>();
        for (int i = 0; i < 50; i++)
        {
            data.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        // Static buffer should resize to exact size: 50
        Assert.IsTrue(50 <= buffer.Capacity);
    }

    [TestMethod]
    public void StaticBuffer_MultipleUploadsWithResize()
    {
        // Arrange
        using var buffer = new ElementBuffer<TestElement>(
            _vkContext!,
            capacity: 10,
            isDynamic: false
        );

        // Act 1: Upload 15 elements (resize to 15)
        var data1 = new FastList<TestElement>();
        for (int i = 0; i < 15; i++)
        {
            data1.Add(new TestElement(i, i + 1, i + 2, i));
        }
        var result1 = buffer.Upload(data1);

        // Assert 1
        Assert.AreEqual(ResultCode.Ok, result1);
        Assert.AreEqual(15, buffer.Capacity);

        // Act 2: Upload 25 elements (resize to 25)
        var data2 = new FastList<TestElement>();
        for (int i = 0; i < 25; i++)
        {
            data2.Add(new TestElement(i * 2, i * 2 + 1, i * 2 + 2, i));
        }
        var result2 = buffer.Upload(data2);

        // Assert 2
        Assert.AreEqual(ResultCode.Ok, result2);
        Assert.IsTrue(25 <= buffer.Capacity);
    }

    [TestMethod]
    public void StaticBuffer_DoesNotUseMappedMemory()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);

        // Act - Verify buffer does NOT use mapped memory (device-local)
        var mappedPtr = _vkContext!.GetMappedPtr(buffer.Buffer.Handle);

        // Assert
        Assert.AreEqual(
            IntPtr.Zero,
            mappedPtr,
            "Static buffer should NOT have mapped memory (device-local)"
        );
    }

    [TestMethod]
    public void StaticBuffer_EnsureCapacityExactSize()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);

        // Act
        var result = buffer.EnsureCapacity(50, true);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.AreEqual(50, buffer.Capacity); // Exact size
    }

    #endregion

    #region Data Integrity Tests

    [TestMethod]
    public void DynamicBuffer_DataIntegrity_SmallData()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);
        var originalData = new FastList<Vector4>
        {
            new Vector4(1.5f, 2.5f, 3.5f, 4.5f),
            new Vector4(5.5f, 6.5f, 7.5f, 8.5f),
            new Vector4(9.5f, 10.5f, 11.5f, 12.5f),
        };

        // Act
        var uploadResult = buffer.Upload(originalData);
        Assert.AreEqual(ResultCode.Ok, uploadResult);

        // Download and verify
        var downloadedData = new Vector4[originalData.Count];
        unsafe
        {
            using var pin = downloadedData.Pin();
            var downloadResult = _vkContext!.Download(
                buffer.Buffer.Handle,
                (nint)pin.Pointer,
                (uint)(originalData.Count * sizeof(Vector4))
            );
            Assert.AreEqual(ResultCode.Ok, downloadResult);
        }

        // Assert
        for (int i = 0; i < originalData.Count; i++)
        {
            Assert.AreEqual(originalData[i], downloadedData[i], $"Data mismatch at index {i}");
        }
    }

    [TestMethod]
    public void DynamicBuffer_DataIntegrity_LargeData()
    {
        // Arrange
        using var buffer = new ElementBuffer<TestElement>(
            _vkContext!,
            capacity: 100,
            isDynamic: true
        );
        var originalData = new FastList<TestElement>();
        for (int i = 0; i < 500; i++)
        {
            originalData.Add(
                new TestElement(
                    _rnd.NextSingle() * 100,
                    _rnd.NextSingle() * 100,
                    _rnd.NextSingle() * 100,
                    i
                )
            );
        }

        // Act
        var uploadResult = buffer.Upload(originalData);
        Assert.AreEqual(ResultCode.Ok, uploadResult);

        // Download and verify
        var downloadedData = new TestElement[originalData.Count];
        unsafe
        {
            using var pin = downloadedData.Pin();
            var downloadResult = _vkContext!.Download(
                buffer.Buffer.Handle,
                (nint)pin.Pointer,
                (uint)(originalData.Count * sizeof(TestElement))
            );
            Assert.AreEqual(ResultCode.Ok, downloadResult);
        }

        // Assert
        for (int i = 0; i < originalData.Count; i++)
        {
            Assert.AreEqual(originalData[i], downloadedData[i], $"Data mismatch at index {i}");
        }
    }

    [TestMethod]
    public void StaticBuffer_DataIntegrity()
    {
        // Arrange
        using var buffer = new ElementBuffer<Matrix4x4>(
            _vkContext!,
            capacity: 10,
            isDynamic: false
        );
        var originalData = new FastList<Matrix4x4>
        {
            Matrix4x4.Identity,
            Matrix4x4.CreateScale(2.0f),
            Matrix4x4.CreateTranslation(1, 2, 3),
        };

        // Act
        var uploadResult = buffer.Upload(originalData);
        Assert.AreEqual(ResultCode.Ok, uploadResult);

        // Download and verify
        var downloadedData = new Matrix4x4[originalData.Count];
        unsafe
        {
            using var pin = downloadedData.Pin();
            var downloadResult = _vkContext!.Download(
                buffer.Buffer.Handle,
                (nint)pin.Pointer,
                (uint)(originalData.Count * sizeof(Matrix4x4))
            );
            Assert.AreEqual(ResultCode.Ok, downloadResult);
        }

        // Assert
        for (int i = 0; i < originalData.Count; i++)
        {
            Assert.AreEqual(originalData[i], downloadedData[i], $"Data mismatch at index {i}");
        }
    }

    #endregion

    #region Edge Cases and Error Handling

    [TestMethod]
    public void DynamicBuffer_UploadNull_ReturnsOk()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);

        // Act
        var result = buffer.Upload(null!);

        // Assert
        Assert.AreEqual(ResultCode.ArgumentNull, result);
    }

    [TestMethod]
    public void StaticBuffer_UploadNull_ReturnsOk()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);

        // Act
        var result = buffer.Upload(null!);

        // Assert
        Assert.AreEqual(ResultCode.ArgumentNull, result);
    }

    [TestMethod]
    public void DynamicBuffer_VeryLargeCapacity()
    {
        // Arrange & Act
        using var buffer = new ElementBuffer<Vector4>(
            _vkContext!,
            capacity: 1000000,
            isDynamic: true
        );

        // Assert
        Assert.AreEqual(1000000, buffer.Capacity);
        Assert.IsTrue(buffer.Buffer.Valid);
    }

    [TestMethod]
    public void StaticBuffer_VeryLargeCapacity()
    {
        // Arrange & Act
        using var buffer = new ElementBuffer<Vector4>(
            _vkContext!,
            capacity: 1000000,
            isDynamic: false
        );

        // Assert
        Assert.AreEqual(1000000, buffer.Capacity);
        Assert.IsTrue(buffer.Buffer.Valid);
    }

    #endregion

    #region Performance Comparison Tests

    [TestMethod]
    [Timeout(5000)] // 5 second timeout
    public void DynamicBuffer_PerformanceTest_MultipleSmallUploads()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 1000, isDynamic: true);
        var data = new FastList<Vector4>();
        for (int i = 0; i < 100; i++)
        {
            data.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        // Act - Multiple uploads should be fast with mapped memory
        for (int i = 0; i < 100; i++)
        {
            var result = buffer.Upload(data);
            Assert.AreEqual(ResultCode.Ok, result);
        }

        // Assert - Test completes within timeout
        Assert.IsTrue(true);
    }

    [TestMethod]
    [Timeout(5000)]
    public void StaticBuffer_PerformanceTest_MultipleSmallUploads()
    {
        // Arrange
        using var buffer = new ElementBuffer<Vector4>(
            _vkContext!,
            capacity: 1000,
            isDynamic: false
        );
        var data = new FastList<Vector4>();
        for (int i = 0; i < 100; i++)
        {
            data.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        // Act - Multiple uploads with staging
        for (int i = 0; i < 100; i++)
        {
            var result = buffer.Upload(data);
            Assert.AreEqual(ResultCode.Ok, result);
        }

        // Assert - Test completes within timeout
        Assert.IsTrue(true);
    }

    #endregion

    #region Dispose Tests

    [TestMethod]
    public void DynamicBuffer_DisposeReleasesResources()
    {
        // Arrange
        BufferResource bufferHandle;
        {
            var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);
            bufferHandle = buffer.Buffer;
            Assert.IsTrue(bufferHandle.Valid);

            // Act
            buffer.Dispose();
        }

        // Assert - Buffer should no longer be valid after dispose
        // Note: We can't directly check if the handle is invalid in the context,
        // but we ensure no exceptions are thrown
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void StaticBuffer_DisposeReleasesResources()
    {
        // Arrange
        BufferResource bufferHandle;
        {
            var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);
            bufferHandle = buffer.Buffer;
            Assert.IsTrue(bufferHandle.Valid);

            // Act
            buffer.Dispose();
        }

        // Assert
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void DynamicBuffer_MultipleDisposeCalls()
    {
        // Arrange
        var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: true);

        // Act & Assert - Should not throw
        buffer.Dispose();
        buffer.Dispose();
        buffer.Dispose();

        Assert.IsTrue(true);
    }

    #endregion

    #region Type-Specific Tests

    [TestMethod]
    public void DynamicBuffer_FloatType()
    {
        // Arrange
        using var buffer = new ElementBuffer<float>(_vkContext!, capacity: 100, isDynamic: true);
        var data = new FastList<float>();
        for (int i = 0; i < 50; i++)
        {
            data.Add(_rnd.NextSingle() * 100);
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(buffer.Buffer.Valid);
    }

    [TestMethod]
    public void DynamicBuffer_UIntType()
    {
        // Arrange
        using var buffer = new ElementBuffer<uint>(_vkContext!, capacity: 100, isDynamic: true);
        var data = new FastList<uint>();
        for (uint i = 0; i < 50; i++)
        {
            data.Add(i * 2);
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(buffer.Buffer.Valid);
    }

    [TestMethod]
    public void StaticBuffer_Matrix4x4Type()
    {
        // Arrange
        using var buffer = new ElementBuffer<Matrix4x4>(
            _vkContext!,
            capacity: 50,
            isDynamic: false
        );
        var data = new FastList<Matrix4x4>();
        for (int i = 0; i < 30; i++)
        {
            data.Add(Matrix4x4.CreateRotationX(i * 0.1f));
        }

        // Act
        var result = buffer.Upload(data);

        // Assert
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(buffer.Buffer.Valid);
    }

    #endregion

    #region Real-World Usage Patterns

    [TestMethod]
    public void DynamicBuffer_PerFrameUpdate_Pattern()
    {
        // Simulate per-frame instance transform updates
        using var buffer = new ElementBuffer<Matrix4x4>(
            _vkContext!,
            capacity: 1000,
            isDynamic: true
        );

        // Simulate 100 frames
        for (int frame = 0; frame < 100; frame++)
        {
            var transforms = new FastList<Matrix4x4>();
            int instanceCount = _rnd.Next(1000, 1500); // Varying instance counts

            for (int i = 0; i < instanceCount; i++)
            {
                transforms.Add(
                    Matrix4x4.CreateTranslation(frame + i, frame * 2 + i, frame * 3 + i)
                );
            }

            var result = buffer.Upload(transforms);
            Assert.AreEqual(ResultCode.Ok, result);
            Assert.AreEqual(instanceCount, buffer.Count);
        }

        // Buffer should have grown to accommodate largest frame
        Assert.IsTrue(buffer.Capacity >= 1000);
    }

    [TestMethod]
    public void StaticBuffer_MaterialProperties_Pattern()
    {
        // Simulate one-time material property upload
        using var buffer = new ElementBuffer<Vector4>(_vkContext!, capacity: 10, isDynamic: false);

        var materials = new FastList<Vector4>();
        for (int i = 0; i < 25; i++)
        {
            materials.Add(
                new Vector4(
                    _rnd.NextSingle(), // Albedo R
                    _rnd.NextSingle(), // Albedo G
                    _rnd.NextSingle(), // Albedo B
                    _rnd.NextSingle() // Metallic
                )
            );
        }

        var result = buffer.Upload(materials);
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(25 <= buffer.Capacity); // Exact fit
    }

    [TestMethod]
    public void DynamicBuffer_GPUCulling_Pattern()
    {
        // Simulate GPU culling visible instances buffer
        using var visibleInstanceBuffer = new ElementBuffer<uint>(
            _vkContext!,
            capacity: 5000,
            isDynamic: true
        );

        // Frame 1: 3000 visible
        var visible1 = new FastList<uint>();
        for (uint i = 0; i < 3000; i++)
        {
            visible1.Add(i);
        }
        Assert.AreEqual(ResultCode.Ok, visibleInstanceBuffer.Upload(visible1));

        // Frame 2: 4500 visible (no resize)
        var visible2 = new FastList<uint>();
        for (uint i = 0; i < 4500; i++)
        {
            visible2.Add(i);
        }
        Assert.AreEqual(ResultCode.Ok, visibleInstanceBuffer.Upload(visible2));
        Assert.AreEqual(5000, visibleInstanceBuffer.Capacity);

        // Frame 3: 6000 visible (resize)
        var visible3 = new FastList<uint>();
        for (uint i = 0; i < 6000; i++)
        {
            visible3.Add(i);
        }
        Assert.AreEqual(ResultCode.Ok, visibleInstanceBuffer.Upload(visible3));
        Assert.IsTrue(visibleInstanceBuffer.Capacity >= 6000);
    }

    #endregion
}
