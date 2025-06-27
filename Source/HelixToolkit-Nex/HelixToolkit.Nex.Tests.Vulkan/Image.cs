namespace HelixToolkit.Nex.Tests.Vulkan;

[TestClass]
public class Image
{
    private static IContext? vkContext;
    private static readonly Random rnd = new();

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

    [DataTestMethod]
    [DataRow(1, 1, Format.RGBA_F32)]
    [DataRow(1024, 1, Format.RGBA_F32)]
    [DataRow(1, 1024, Format.RGBA_F32)]
    [DataRow(1024, 1024, Format.RGBA_F32)]
    public unsafe void CreateImage(int width, int height, Format format)
    {
        var colors = Enumerable.Range(0, width * height)
            .Select(i => new Color4(rnd.NextFloat(-1, 1), rnd.NextFloat(-1, 1), rnd.NextFloat(-1, 1), rnd.NextFloat(-1, 1)))
            .ToArray();
        using var pColors = colors.Pin(); // Pin the array to prevent garbage collection
        var size = (size_t)(colors.Length * NativeHelper.SizeOf<Color4>());
        var imageDesc = new TextureDesc
        {
            format = format,
            dimensions = new Dimensions((uint)width, (uint)height, 1),
            usage = TextureUsageBits.Sampled | TextureUsageBits.Storage | TextureUsageBits.Attachment,
            numMipLevels = 1,
            numLayers = 1,
            data = (nint)pColors.Pointer, // Use the pinned pointer for data
            dataSize = size,
        };
        TextureHolder? image = null;
        var result = vkContext?.CreateTexture(imageDesc, out image, "TestImage");
        Assert.IsTrue(result == ResultCode.Ok, "Image creation failed with error: " + result.ToString());
        Assert.IsNotNull(image, "Image should not be null after creation.");
        Assert.IsTrue(image.Valid, "Image should be valid after creation.");
        var downloadedColors = new Color4[width * height];
        using var pDownloadedColors = downloadedColors.Pin(); // Pin the array to prevent garbage collection
        result = vkContext?.Download(image, new TextureRangeDesc() { dimensions = imageDesc.dimensions }, (nint)pDownloadedColors.Pointer, size).CheckResult(); // Download the image data
        Assert.IsTrue(result == ResultCode.Ok, "Image download failed with error: " + result.ToString());
        // Verify that the downloaded colors match the original colors
        Assert.IsTrue(downloadedColors.SequenceEqual(colors), "Downloaded colors do not match the original colors.");
        // Clean up the image after the test
        image.Dispose();
    }

    [DataTestMethod]
    [DataRow(1, 1, Format.Z_F32)]
    [DataRow(1024, 1, Format.Z_F32)]
    [DataRow(1, 1024, Format.Z_F32)]
    [DataRow(1024, 1024, Format.Z_F32)]
    public unsafe void CreateDepthImage(int width, int height, Format format)
    {
        var data = Enumerable.Range(0, width * height)
            .Select(i => rnd.NextFloat(-1, 1))
            .ToArray();
        using var pData = data.Pin(); // Pin the array to prevent garbage collection
        var size = (size_t)(data.Length * NativeHelper.SizeOf<float>());
        var imageDesc = new TextureDesc
        {
            format = format,
            dimensions = new Dimensions((uint)width, (uint)height, 1),
            usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment,
            numMipLevels = 1,
            numLayers = 1,
            //data = (nint)pData.Pointer, // Use the pinned pointer for data
            //dataSize = size,
        };
        TextureHolder? image = null;
        var result = vkContext?.CreateTexture(imageDesc, out image, "TestDepth");
        Assert.IsTrue(result == ResultCode.Ok, "Depth Image creation failed with error: " + result.ToString());
        Assert.IsNotNull(image, "Depth Image should not be null after creation.");
        Assert.IsTrue(image.Valid, "Depth Image should be valid after creation.");
        var downloaded = new float[width * height];
        using var pDownloadedColors = downloaded.Pin(); // Pin the array to prevent garbage collection
        result = vkContext?.Download(image, new TextureRangeDesc() { dimensions = imageDesc.dimensions }, (nint)pDownloadedColors.Pointer, size).CheckResult(); // Download the image data
        Assert.IsTrue(result == ResultCode.Ok, "Depth data download failed with error: " + result.ToString());
        // Verify that the downloaded data match the original data
        Assert.IsTrue(downloaded.SequenceEqual(data), "Downloaded depth data do not match the original data.");
        // Clean up the image after the test
        image.Dispose();
    }
}
