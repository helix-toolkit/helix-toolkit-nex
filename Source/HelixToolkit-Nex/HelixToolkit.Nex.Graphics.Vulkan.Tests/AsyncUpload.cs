using System.Runtime.InteropServices;
using HelixToolkit.Nex.Maths;

namespace HelixToolkit.Nex.Tests.Vulkan;

[TestClass]
[TestCategory("GPURequired")]
public class AsyncUpload
{
    private static IContext? _vkContext;
    private static readonly Random _rnd = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct Payload
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        var config = new VulkanContextConfig { TerminateOnValidationError = true };
        _vkContext = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _vkContext?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IContext Context =>
        _vkContext ?? throw new InvalidOperationException("Context not initialized.");

    private static BufferResource CreateStorageBuffer(uint byteSize, string name)
    {
        var result = Context.CreateBuffer(
            new BufferDesc { DataSize = byteSize, Usage = BufferUsageBits.Storage },
            out var buf,
            name
        );
        Assert.AreEqual(ResultCode.Ok, result, $"Buffer '{name}' creation failed: {result}");
        Assert.IsNotNull(buf);
        return buf;
    }

    private static Payload[] RandomPayloads(int count) =>
        Enumerable
            .Range(0, count)
            .Select(_ => new Payload
            {
                X = _rnd.NextFloat(-1, 1),
                Y = _rnd.NextFloat(-1, 1),
                Z = _rnd.NextFloat(-1, 1),
                W = _rnd.NextFloat(-1, 1),
            })
            .ToArray();

    // -------------------------------------------------------------------------
    // Dedicated transfer queue — report which code path is exercised
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Context_ReportsDedicatedTransferQueueCapability()
    {
        // Capability depends on the physical device — just make it observable in output.
        Console.WriteLine($"HasDedicatedTransferQueue = {Context.HasDedicatedTransferQueue}");
    }

    // -------------------------------------------------------------------------
    // Buffer async upload — IsCompleted polling
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BufferUploadAsync_HandleCompletesAndIsCompleted()
    {
        var data = RandomPayloads(64);
        var byteSize = (uint)(data.Length * NativeHelper.SizeOf<Payload>());
        using var buf = CreateStorageBuffer(byteSize, "AsyncBuf_IsCompleted");

        var handle = Context.UploadAsync(buf.Handle, 0u, data, (uint)data.Length);

        Assert.IsNotNull(handle, "UploadAsync should return a non-null handle.");

        handle.Task.Wait();

        Assert.IsTrue(handle.IsCompleted, "Handle should report IsCompleted after the task finishes.");
        Assert.AreEqual(ResultCode.Ok, handle.Result, $"Unexpected result: {handle.Result}");
        Assert.AreEqual(buf.Handle, handle.ResourceHandle, "ResourceHandle should match the destination buffer handle.");
    }

    // -------------------------------------------------------------------------
    // Buffer async upload — direct await
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task BufferUploadAsync_AwaitReturnsCorrectResultAndHandle()
    {
        var data = RandomPayloads(256);
        var byteSize = (uint)(data.Length * NativeHelper.SizeOf<Payload>());
        using var buf = CreateStorageBuffer(byteSize, "AsyncBuf_Await");

        var (result, handle) = await Context.UploadAsync(buf.Handle, 0u, data, (uint)data.Length);

        Assert.AreEqual(ResultCode.Ok, result, $"Async buffer upload failed: {result}");
        Assert.AreEqual(buf.Handle, handle, "Awaited handle should match the destination buffer.");
    }

    // -------------------------------------------------------------------------
    // Buffer async upload — data round-trip
    // -------------------------------------------------------------------------

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(64)]
    [DataRow(1024)]
    [DataRow(1024 * 16)]
    public unsafe void BufferUploadAsync_DataRoundTrip(int count)
    {
        var data = RandomPayloads(count);
        var byteSize = (uint)(count * NativeHelper.SizeOf<Payload>());
        using var buf = CreateStorageBuffer(byteSize, $"AsyncBuf_RoundTrip_{count}");

        var handle = Context.UploadAsync(buf.Handle, 0u, data, (uint)count);
        handle.Task.Wait();

        Assert.AreEqual(ResultCode.Ok, handle.Result, $"Async upload failed for count={count}: {handle.Result}");

        var downloaded = new Payload[count];
        using var pDst = downloaded.Pin();
        var dlResult = Context.Download(buf.Handle, (nint)pDst.Pointer, byteSize);
        Assert.AreEqual(ResultCode.Ok, dlResult, $"Download failed for count={count}: {dlResult}");
        Assert.IsTrue(downloaded.SequenceEqual(data), $"Round-trip data mismatch for count={count}.");
    }

    // -------------------------------------------------------------------------
    // Buffer async upload — zero-count fast path (pre-completed)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BufferUploadAsync_ZeroCount_ReturnsPreCompleted()
    {
        var data = RandomPayloads(4);
        var byteSize = (uint)(data.Length * NativeHelper.SizeOf<Payload>());
        using var buf = CreateStorageBuffer(byteSize, "AsyncBuf_ZeroCount");

        var handle = Context.UploadAsync(buf.Handle, 0u, data, 0u);

        Assert.IsTrue(handle.IsCompleted, "Zero-count upload should be immediately completed.");
        Assert.AreEqual(ResultCode.Ok, handle.Result, "Zero-count upload should succeed.");
        Assert.AreEqual(buf.Handle, handle.ResourceHandle, "ResourceHandle should match even for zero-count uploads.");
    }

    // -------------------------------------------------------------------------
    // Buffer async upload — multiple concurrent uploads via Task.WhenAll
    // -------------------------------------------------------------------------

    [TestMethod]
    public unsafe void BufferUploadAsync_MultipleConcurrentUploads_AllSucceed()
    {
        const int uploadCount = 8;
        const int elementsPerBuffer = 512;
        var byteSize = (uint)(elementsPerBuffer * NativeHelper.SizeOf<Payload>());

        var buffers = Enumerable
            .Range(0, uploadCount)
            .Select(i => CreateStorageBuffer(byteSize, $"AsyncBuf_Concurrent_{i}"))
            .ToArray();

        try
        {
            var datasets = buffers.Select(_ => RandomPayloads(elementsPerBuffer)).ToArray();

            var handles = buffers
                .Select((buf, i) => Context.UploadAsync(buf.Handle, 0u, datasets[i], (uint)elementsPerBuffer))
                .ToArray();

            Task.WhenAll(handles.Select(h => h.Task)).Wait();

            for (int i = 0; i < uploadCount; i++)
            {
                Assert.AreEqual(ResultCode.Ok, handles[i].Result, $"Upload {i} failed: {handles[i].Result}");
                Assert.AreEqual(buffers[i].Handle, handles[i].ResourceHandle, $"Handle mismatch for upload {i}.");

                var downloaded = new Payload[elementsPerBuffer];
                using var pDst = downloaded.Pin();
                var dlResult = Context.Download(buffers[i].Handle, (nint)pDst.Pointer, byteSize);
                Assert.AreEqual(ResultCode.Ok, dlResult, $"Download failed for buffer {i}: {dlResult}");
                Assert.IsTrue(downloaded.SequenceEqual(datasets[i]), $"Data mismatch for buffer {i}.");
            }
        }
        finally
        {
            foreach (var buf in buffers)
                buf.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Texture async upload — IsCompleted polling
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TextureUploadAsync_HandleCompletesAndIsCompleted()
    {
        const int width = 64, height = 64;
        var pixels = RandomPayloads(width * height);
        var byteSize = (uint)(pixels.Length * NativeHelper.SizeOf<Payload>());

        using var tex = Context.CreateTexture2D(
            Format.RGBA_F32,
            (uint)width,
            (uint)height,
            TextureUsageBits.Sampled | TextureUsageBits.Storage,
            StorageType.Device,
            debugName: "AsyncTex_IsCompleted"
        );

        var range = new TextureRangeDesc { MipLevel = 0, Layer = 0, NumMipLevels = 1, NumLayers = 1 };
        var handle = Context.UploadAsync(tex.Handle, range, pixels, (uint)pixels.Length);

        Assert.IsNotNull(handle, "Texture UploadAsync should return a non-null handle.");

        handle.Task.Wait();

        Assert.IsTrue(handle.IsCompleted, "Handle should report IsCompleted after the task finishes.");
        Assert.AreEqual(ResultCode.Ok, handle.Result, $"Unexpected texture upload result: {handle.Result}");
        Assert.AreEqual(tex.Handle, handle.ResourceHandle, "ResourceHandle should match the destination texture handle.");
    }

    // -------------------------------------------------------------------------
    // Texture async upload — direct await
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TextureUploadAsync_AwaitReturnsCorrectResultAndHandle()
    {
        const int width = 32, height = 32;
        var pixels = RandomPayloads(width * height);

        using var tex = Context.CreateTexture2D(
            Format.RGBA_F32,
            (uint)width,
            (uint)height,
            TextureUsageBits.Sampled | TextureUsageBits.Storage,
            StorageType.Device,
            debugName: "AsyncTex_Await"
        );

        var range = new TextureRangeDesc { MipLevel = 0, Layer = 0, NumMipLevels = 1, NumLayers = 1 };
        var (result, handle) = await Context.UploadAsync(tex.Handle, range, pixels, (uint)pixels.Length);

        Assert.AreEqual(ResultCode.Ok, result, $"Async texture upload failed: {result}");
        Assert.AreEqual(tex.Handle, handle, "Awaited handle should match the destination texture.");
    }

    // -------------------------------------------------------------------------
    // Texture async upload — zero-count fast path (pre-completed)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TextureUploadAsync_ZeroCount_ReturnsPreCompleted()
    {
        const int width = 8, height = 8;
        var pixels = RandomPayloads(width * height);

        using var tex = Context.CreateTexture2D(
            Format.RGBA_F32,
            (uint)width,
            (uint)height,
            TextureUsageBits.Sampled | TextureUsageBits.Storage,
            StorageType.Device,
            debugName: "AsyncTex_ZeroCount"
        );

        var range = new TextureRangeDesc { MipLevel = 0, Layer = 0, NumMipLevels = 1, NumLayers = 1 };
        var handle = Context.UploadAsync(tex.Handle, range, pixels, 0u);

        Assert.IsTrue(handle.IsCompleted, "Zero-count texture upload should be immediately completed.");
        Assert.AreEqual(ResultCode.Ok, handle.Result, "Zero-count texture upload should succeed.");
        Assert.AreEqual(tex.Handle, handle.ResourceHandle, "ResourceHandle should match even for zero-count uploads.");
    }
}
