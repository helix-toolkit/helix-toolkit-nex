using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Textures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Repository.Tests;

/// <summary>
/// Tests for <see cref="TextureRepository"/> lifecycle, cache behavior, and async creation.
/// </summary>
[TestClass]
public class TextureRepositoryTests
{
    // -------------------------------------------------------------------------
    // Helper: create a minimal 1x1 RGBA PNG stream
    // -------------------------------------------------------------------------

    private static MemoryStream CreateMinimalPngStream()
    {
        var img = Image.New2D(1, 1, 1, Format.RGBA_UN8);
        unsafe
        {
            var pb = img.GetPixelBuffer(0, 0);
            var ptr = (byte*)pb.DataPointer;
            ptr[0] = 255; // R
            ptr[1] = 0; // G
            ptr[2] = 0; // B
            ptr[3] = 255; // A
        }
        var ms = new MemoryStream();
        img.Save(ms, ImageFileType.Png);
        img.Dispose();
        ms.Position = 0;
        return ms;
    }

    // -------------------------------------------------------------------------
    // Example: Remove(key) fires OnDisposed on the previously returned ref
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Example_Remove_FiresOnDisposed_OnPreviouslyReturnedRef()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        using var repo = new TextureRepository(ctx);

        using var stream = CreateMinimalPngStream();
        var textureRef = repo.GetOrCreateFromStream("albedo", stream);

        bool fired = false;
        textureRef.OnDisposed += () => fired = true;

        repo.Remove("albedo");

        Assert.IsTrue(fired, "Remove(key) should fire OnDisposed on the previously returned ref.");
    }

    // -------------------------------------------------------------------------
    // Property 4: Cache-hit GetOrCreateFromStreamAsync returns completed task
    // Feature: resource-ref-lifecycle, Property 4: For any key K already in cache,
    // GetOrCreateFromStreamAsync returns a Task<TextureRef> where IsCompleted == true.
    // Validates: Requirements 6.4
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property4_CacheHit_GetOrCreateFromStreamAsync_ReturnsCompletedTask()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("albedo", "normal", "roughness", "ao", "bump")),
                (string key) =>
                {
                    var ctx = new MockContext();
                    ctx.Initialize();
                    using var repo = new TextureRepository(ctx);

                    // Populate the cache synchronously
                    using var stream1 = CreateMinimalPngStream();
                    repo.GetOrCreateFromStream(key, stream1);

                    // Now call async — should be a cache hit and return a completed task
                    using var stream2 = CreateMinimalPngStream();
                    var task = repo.GetOrCreateFromStreamAsync(key, stream2);

                    return task.IsCompleted;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Example: GetOrCreateFromFileAsync with missing file returns faulted task
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Example_GetOrCreateFromFileAsync_MissingFile_ReturnsFaultedTask()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        using var repo = new TextureRepository(ctx);

        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");

        // The method should throw FileNotFoundException (either synchronously or as a faulted task)
        bool gotFileNotFoundException = false;
        try
        {
            var task = repo.GetOrCreateFromFileAsync(missingPath);
            await task;
        }
        catch (FileNotFoundException)
        {
            gotFileNotFoundException = true;
        }

        Assert.IsTrue(
            gotFileNotFoundException,
            "GetOrCreateFromFileAsync with a missing file should result in FileNotFoundException."
        );
    }

    // -------------------------------------------------------------------------
    // Example: Ref stored in cache before async upload completes (TryGet returns true immediately)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Example_RefStoredInCache_BeforeAsyncUploadCompletes()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        using var repo = new TextureRepository(ctx);

        using var stream = CreateMinimalPngStream();
        const string key = "async-key";

        // Start the async creation
        var task = repo.GetOrCreateFromStreamAsync(key, stream);

        // TryGet should return true immediately (ref stored before upload completes)
        bool foundInCache = repo.TryGet(key, out var entry);

        // Await the task to completion
        await task;

        Assert.IsTrue(
            foundInCache,
            "TryGet should return true immediately after GetOrCreateFromStreamAsync is called, before the task completes."
        );
        Assert.IsNotNull(entry, "Cache entry should not be null.");
    }
}
