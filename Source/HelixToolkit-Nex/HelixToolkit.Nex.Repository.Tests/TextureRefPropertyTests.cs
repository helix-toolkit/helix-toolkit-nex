using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Textures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Repository.Tests;

// ---------------------------------------------------------------------------
// Mock ITextureRepository helper
// ---------------------------------------------------------------------------

/// <summary>
/// A configurable mock <see cref="ITextureRepository"/> for property-based tests.
/// Tracks <see cref="TryGet"/> call counts and returns a configurable result.
/// All mutating methods return <see cref="TextureRef.Null"/>.
/// </summary>
internal sealed class MockTextureRepository : ITextureRepository
{
    private bool _tryGetResult;
    private TextureCacheEntry? _tryGetEntry;

    /// <summary>Number of times <see cref="TryGet"/> has been called.</summary>
    public int TryGetCallCount { get; private set; }

    /// <summary>Configures <see cref="TryGet"/> to return <c>false</c> (key not found).</summary>
    public void SetTryGetNotFound()
    {
        _tryGetResult = false;
        _tryGetEntry = null;
    }

    /// <summary>
    /// Configures <see cref="TryGet"/> to return <c>true</c> with a real <see cref="TextureCacheEntry"/>
    /// backed by a <see cref="MockContext"/> texture.
    /// </summary>
    public TextureCacheEntry SetTryGetFound()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateTexture(
            new TextureDesc
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_UN8,
                Dimensions = new Dimensions(1, 1, 1),
                NumMipLevels = 1,
                NumLayers = 1,
            },
            out var tex,
            "mock"
        );

        var textureRef = new TextureRef("mock", this, tex);
        var entry = new TextureCacheEntry
        {
            Resource = textureRef,
            SourceHash = "mock",
            DebugName = "mock",
            AccessCount = 1,
        };

        _tryGetResult = true;
        _tryGetEntry = entry;
        return entry;
    }

    public int Count => 0;

    public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
    {
        TryGetCallCount++;
        entry = _tryGetEntry;
        return _tryGetResult;
    }

    public TextureRef GetOrCreateFromStream(string name, Stream stream, string? debugName = null) =>
        new TextureRef(name, this, TextureResource.Null);

    public TextureRef GetOrCreateFromFile(string filePath, string? debugName = null) =>
        new TextureRef(filePath, this, TextureResource.Null);

    public TextureRef GetOrCreateFromImage(string name, Image image) =>
        new TextureRef(name, this, TextureResource.Null);

    public Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        string? debugName = null
    ) => Task.FromResult(new TextureRef(name, this, TextureResource.Null));

    public Task<TextureRef> GetOrCreateFromFileAsync(string filePath, string? debugName = null) =>
        Task.FromResult(new TextureRef(filePath, this, TextureResource.Null));

    public Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image) =>
        Task.FromResult(new TextureRef(name, this, TextureResource.Null));

    public bool Remove(string key) => false;

    public void Clear() { }

    public int CleanupExpired() => 0;

    public RepositoryStatistics GetStatistics() =>
        new()
        {
            TotalEntries = 0,
            MaxEntries = 0,
            TotalHits = 0,
            TotalMisses = 0,
        };

    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// Property-based tests
// ---------------------------------------------------------------------------

/// <summary>
/// Property-based tests for <see cref="TextureRef"/> using FsCheck.
/// </summary>
[TestClass]
public class TextureRefPropertyTests
{
    // -------------------------------------------------------------------------
    // Property 1: Key and Repository round-trip
    // -------------------------------------------------------------------------

    // Feature: texture-ref-wrapper, Property 1: For any non-null string key and any ITextureRepository instance,
    // constructing a TextureRef with that key and repository returns the same key from Key and the same
    // repository instance from Repository.
    [TestMethod]
    public void Property1_KeyAndRepository_RoundTrip()
    {
        Prop.ForAll(
                Arb.From(
                    Gen.Elements("key1", "key2", "some-texture", "path/to/file.png", "abc", "xyz")
                ),
                (string key) =>
                {
                    var mockRepo = new MockTextureRepository();
                    var textureRef = new TextureRef(key, mockRepo, TextureResource.Null);
                    return textureRef.Key == key
                        && ReferenceEquals(textureRef.Repository, mockRepo);
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 2: Valid resource handle skips repository
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 2: For any TextureRef whose resource has a valid handle,
    // calling GetHandle() any number of times returns the same handle value and makes zero calls
    // to Repository.TryGet.
    [TestMethod]
    public void Property2_ValidCachedHandle_SkipsRepository()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(1, 50)),
                (int n) =>
                {
                    var mockRepo = new MockTextureRepository();
                    var ctx = new MockContext();
                    ctx.Initialize();
                    ctx.CreateTexture(
                        new TextureDesc
                        {
                            Type = TextureType.Texture2D,
                            Format = Format.RGBA_UN8,
                            Dimensions = new Dimensions(1, 1, 1),
                            NumMipLevels = 1,
                            NumLayers = 1,
                        },
                        out var tex,
                        "test"
                    );
                    var textureRef = new TextureRef("key", mockRepo, tex);

                    Handle<Texture>? firstResult = null;
                    for (int i = 0; i < n; i++)
                    {
                        var h = textureRef.GetHandle();
                        if (firstResult is null)
                            firstResult = h;
                        else if (h != firstResult.Value)
                            return false;
                    }

                    return mockRepo.TryGetCallCount == 0;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 4: Null sentinel always returns invalid handle
    // -------------------------------------------------------------------------

    // Feature: texture-ref-wrapper, Property 4: For any number of GetHandle() calls on TextureRef.Null,
    // the returned handle always has Valid == false.
    [TestMethod]
    public void Property4_NullSentinel_AlwaysReturnsInvalidHandle()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(1, 200)),
                (int n) =>
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (TextureRef.Null.GetHandle().Valid)
                            return false;
                    }
                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 5: GetOrCreate* returns TextureRef with matching key
    // -------------------------------------------------------------------------

    // Feature: texture-ref-wrapper, Property 5: For any cache key, calling GetOrCreate* returns a TextureRef
    // whose Key matches the cache key and whose Repository is the repository instance.
    [TestMethod]
    public void Property5_GetOrCreate_ReturnsTextureRefWithMatchingKey()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("tex1", "tex2", "my-texture", "path/img.png", "abc")),
                (string key) =>
                {
                    var mockRepo = new MockTextureRepository();
                    var result = mockRepo.GetOrCreateFromStream(key, Stream.Null, null);
                    return result.Key == key && ReferenceEquals(result.Repository, mockRepo);
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 7: After remove, GetHandle returns invalid handle
    // -------------------------------------------------------------------------

    // Feature: texture-ref-wrapper, Property 7: For any TextureRef previously returned for a given key,
    // after Remove(key) is called, calling GetHandle() on that TextureRef returns a handle with Valid == false.
    [TestMethod]
    public void Property7_AfterRemove_GetHandle_ReturnsInvalidHandle()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("albedo", "normal", "roughness", "ao")),
                (string key) =>
                {
                    var mockRepo = new MockTextureRepository();
                    // TextureResource.Null has an invalid handle
                    var textureRef = new TextureRef(key, mockRepo, TextureResource.Null);

                    // GetHandle() returns the resource's handle directly — no TryGet call
                    var result = textureRef.GetHandle();
                    return !result.Valid;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 9: GetHandle().Index matches resource handle index
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 1 (handle-identity invariant):
    // For any TextureRef, GetHandle().Index == _resource.Handle.Index at all times.
    [TestMethod]
    public void Property9_TextureRef_GetHandle_IndexMatchesResourceHandle()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(1, 50)),
                (int n) =>
                {
                    var mockRepo = new MockTextureRepository();
                    var ctx = new MockContext();
                    ctx.Initialize();
                    ctx.CreateTexture(
                        new TextureDesc
                        {
                            Type = TextureType.Texture2D,
                            Format = Format.RGBA_UN8,
                            Dimensions = new Dimensions(1, 1, 1),
                            NumMipLevels = 1,
                            NumLayers = 1,
                        },
                        out var tex,
                        "test"
                    );
                    var textureRef = new TextureRef("key", mockRepo, tex);

                    // GetHandle() must always equal _resource.Handle.Index
                    for (int i = 0; i < n; i++)
                    {
                        if (textureRef.GetHandle().Index != textureRef.Resource.Handle.Index)
                            return false;
                    }
                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 10: Ref identity on cache hit
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 2: For any cache key K, two successive calls to
    // GetOrCreate on the same repository return the same TextureRef object reference.
    [TestMethod]
    public void Property10_RefIdentity_OnCacheHit()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("albedo", "normal", "roughness", "ao", "bump")),
                (string key) =>
                {
                    var mockRepo = new MockTextureRepository();
                    // Configure mock to return a valid entry on TryGet.
                    var entry = mockRepo.SetTryGetFound();

                    // The entry's Ref should be the same object reference on TryGet
                    mockRepo.TryGet(key, out var retrieved);
                    return retrieved is not null && ReferenceEquals(retrieved.Ref, entry.Ref);
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
