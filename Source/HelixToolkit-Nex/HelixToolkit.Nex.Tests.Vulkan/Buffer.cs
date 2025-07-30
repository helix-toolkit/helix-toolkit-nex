using HelixToolkit.Nex.Maths;
using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Tests.Vulkan;

[TestClass]
public class Buffer
{
    private static IContext? vkContext;
    private static readonly Random rnd = new();

    [StructLayout(LayoutKind.Sequential)]
    struct DummyUniformData
    {
        public float Value1;
        public int Value2;
    }

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true
        };
        vkContext = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        vkContext?.Dispose();
    }

    [TestMethod]
    public void CreateStorageBuffer()
    {
        DummyUniformData data = new()
        {
            Value1 = rnd.NextFloat(-1, 1),
            Value2 = rnd.Next(-100, 100)
        };
        BufferResource? buffer = null; // Initialize the variable to avoid CS0165
        var result = vkContext?.CreateBuffer(new BufferDesc()
        {
            DataSize = NativeHelper.SizeOf(ref data),
            Usage = BufferUsageBits.Storage,
        }, out buffer, "TestBuffer");
        Assert.IsTrue(result == ResultCode.Ok, "Buffer creation failed with error: " + result.ToString());
        Assert.IsNotNull(buffer, "Buffer should not be null after creation.");
        Assert.IsTrue(buffer.Valid, "Buffer should be valid after creation.");
        result = vkContext?.Upload(buffer, 0, in data).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to storage buffer failed with error: " + result.ToString());
        DummyUniformData downloaded = default;
        result = vkContext?.Download(buffer, out downloaded).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to storage buffer failed with error: " + result.ToString());
        buffer.Dispose(); // Clean up the buffer after the test
        Assert.AreEqual(data, downloaded, "Downloaded Value1 does not match the original data.");
    }

    [TestMethod]
    public unsafe void CreateUniformBuffer()
    {
        DummyUniformData data = new()
        {
            Value1 = rnd.NextFloat(-1, 1),
            Value2 = rnd.Next(-100, 100)
        };

        BufferResource? buffer = null; // Initialize the variable to avoid CS0165
        var result = vkContext?.CreateBuffer(new BufferDesc()
        {
            DataSize = NativeHelper.SizeOf(ref data),
            Usage = BufferUsageBits.Uniform,
        }, out buffer, "TestUniformBuffer");
        Assert.IsTrue(result == ResultCode.Ok, "Uniform buffer creation failed with error: " + result.ToString());
        Assert.IsNotNull(buffer, "Uniform buffer should not be null after creation.");
        Assert.IsTrue(buffer.Valid, "Uniform buffer should be valid after creation.");
        result = vkContext?.Upload(buffer, 0, in data).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to uniform buffer failed with error: " + result.ToString());
        DummyUniformData downloaded = default;
        result = vkContext?.Download(buffer, out downloaded).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to uniform buffer failed with error: " + result.ToString());
        buffer.Dispose(); // Clean up the buffer after the test
        Assert.AreEqual(data, downloaded, "Downloaded Value1 does not match the original data.");
        buffer.Dispose(); // Clean up the buffer after the test
    }

    [DataTestMethod]
    [DataRow(10)]
    [DataRow(1024)]
    [DataRow(1024 * 100)]
    public unsafe void CreateVertexBuffer(int vertexCount)
    {
        var vertices = Enumerable.Range(0, vertexCount)
            .Select(_ => new Vector3(rnd.NextFloat(-1, 1), rnd.NextFloat(-1, 1), rnd.NextFloat(-1, 1)))
            .ToArray();
        var size = (size_t)vertices.Length * NativeHelper.SizeOf<Vector3>();
        BufferResource? buffer = null; // Initialize the variable to avoid CS0165
        var result = vkContext?.CreateBuffer(new BufferDesc()
        {
            DataSize = size,
            Usage = BufferUsageBits.Vertex,
        }, out buffer, "TestVertexBuffer");
        Assert.IsTrue(result == ResultCode.Ok, "Vertex buffer creation failed with error: " + result.ToString());
        Assert.IsNotNull(buffer, "Vertex buffer should not be null after creation.");
        Assert.IsTrue(buffer.Valid, "Vertex buffer should be valid after creation.");
        using var pVerts = vertices.Pin(); // Pin the array to prevent garbage collection

        result = vkContext?.Upload(buffer, 0, (nint)pVerts.Pointer, size).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to vertex buffer failed with error: " + result.ToString());

        var downloaded = new Vector3[vertices.Length];
        using var pDownloaded = downloaded.Pin(); // Pin the array to prevent garbage collection  
        result = vkContext?.Download(buffer, (nint)pDownloaded.Pointer, size).CheckResult(); // Download the data from the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data download from vertex buffer failed with error: " + result.ToString());
        Assert.IsTrue(downloaded.SequenceEqual(vertices), "Downloaded vertices do not match the original data.");
        buffer.Dispose(); // Clean up the buffer after the test
    }

    [DataTestMethod]
    [DataRow(10)]
    [DataRow(1024)]
    [DataRow(1024 * 100)]
    public unsafe void CreateIndexBuffer(int indexCount)
    {
        var indices = Enumerable.Range(0, indexCount).ToArray();
        var size = (size_t)indices.Length * NativeHelper.SizeOf<int>();
        BufferResource? buffer = null;
        var result = vkContext?.CreateBuffer(new BufferDesc()
        {
            DataSize = size,
            Usage = BufferUsageBits.Index,
        }, out buffer, "TestIndexBuffer");
        Assert.IsTrue(result == ResultCode.Ok, "Index buffer creation failed with error: " + result.ToString());
        Assert.IsNotNull(buffer, "Index buffer should not be null after creation.");
        Assert.IsTrue(buffer.Valid, "Index buffer should be valid after creation.");

        using var pIndices = indices.Pin(); // Pin the array to prevent garbage collection
        result = vkContext?.Upload(buffer, 0, (nint)pIndices.Pointer, size).CheckResult(); // Upload the data to the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data upload to index buffer failed with error: " + result.ToString());

        var downloaded = new int[indices.Length];
        using var pDownloaded = downloaded.Pin(); // Pin the array to prevent garbage collection
        result = vkContext?.Download(buffer, (nint)pDownloaded.Pointer, size).CheckResult(); // Download the data from the buffer
        Assert.IsTrue(result == ResultCode.Ok, "Data download from index buffer failed with error: " + result.ToString());
        Assert.IsTrue(downloaded.SequenceEqual(indices), "Downloaded indices do not match the original data.");
        buffer.Dispose(); // Clean up the buffer after the test
    }

    [TestMethod]
    public void CreateIndirectBuffer()
    {
        BufferResource? buffer = null; // Initialize the variable to avoid CS0165
        var result = vkContext?.CreateBuffer(new BufferDesc()
        {
            DataSize = 512,
            Usage = BufferUsageBits.Indirect,
        }, out buffer, "TestIndirectBuffer");
        Assert.IsTrue(result == ResultCode.Ok, "Indirect buffer creation failed with error: " + result.ToString());
        Assert.IsNotNull(buffer, "Indirect buffer should not be null after creation.");
        Assert.IsTrue(buffer.Valid, "Indirect buffer should be valid after creation.");
        buffer.Dispose(); // Clean up the buffer after the test
    }
}