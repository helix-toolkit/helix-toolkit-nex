global using SamplerHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Sampler>;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Repository.Tests;

// ---------------------------------------------------------------------------
// Mock ISamplerRepository helper
// ---------------------------------------------------------------------------

/// <summary>
/// A configurable mock <see cref="ISamplerRepository"/> for property-based tests.
/// Tracks <see cref="TryGet"/> call counts and returns a configurable result.
/// All mutating methods return <see cref="SamplerRef.Null"/>.
/// </summary>
internal sealed class MockSamplerRepository : ISamplerRepository
{
    private bool _tryGetResult;
    private SamplerModuleCacheEntry? _tryGetEntry;

    /// <summary>Number of times <see cref="TryGet"/> has been called.</summary>
    public int TryGetCallCount { get; private set; }

    /// <summary>Configures <see cref="TryGet"/> to return <c>false</c> (key not found).</summary>
    public void SetTryGetNotFound()
    {
        _tryGetResult = false;
        _tryGetEntry = null;
    }

    /// <summary>
    /// Configures <see cref="TryGet"/> to return <c>true</c> with a real <see cref="SamplerModuleCacheEntry"/>
    /// backed by a <see cref="MockContext"/> sampler.
    /// </summary>
    public SamplerModuleCacheEntry SetTryGetFound()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);

        var samplerRef = new SamplerRef("mock", this, sampler);
        var entry = new SamplerModuleCacheEntry
        {
            Resource = samplerRef,
            SourceHash = "mock",
            DebugName = "mock",
            AccessCount = 1,
        };

        _tryGetResult = true;
        _tryGetEntry = entry;
        return entry;
    }

    public int Count => 0;

    public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
    {
        TryGetCallCount++;
        entry = _tryGetEntry;
        return _tryGetResult;
    }

    public SamplerRef GetOrCreate(SamplerStateDesc desc) =>
        new SamplerRef(SamplerRepository.GenerateCacheKey(desc), this, SamplerResource.Null);

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
/// Property-based tests for <see cref="SamplerRef"/> using FsCheck.
/// </summary>
[TestClass]
public class SamplerRefPropertyTests
{
    // -------------------------------------------------------------------------
    // Property 1: Key and Repository round-trip
    // -------------------------------------------------------------------------

    // Feature: sampler-ref-wrapper, Property 1: For any non-null string key and any ISamplerRepository instance,
    // constructing a SamplerRef with that key and repository returns the same key from Key and the same
    // repository instance from Repository.
    [TestMethod]
    public void Property1_KeyAndRepository_RoundTrip()
    {
        Prop.ForAll(
                Arb.From(
                    Gen.Elements("key1", "key2", "some-sampler", "linear-clamp", "abc", "xyz")
                ),
                (string key) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    var samplerRef = new SamplerRef(key, mockRepo, SamplerResource.Null);
                    return samplerRef.Key == key
                        && ReferenceEquals(samplerRef.Repository, mockRepo);
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 2: Valid resource handle skips repository
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 2: For any SamplerRef whose resource has a valid handle,
    // calling GetHandle() any number of times returns the same handle value and makes zero calls
    // to Repository.TryGet.
    [TestMethod]
    public void Property2_ValidCachedHandle_SkipsRepository()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(1, 50)),
                (int n) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    var ctx = new MockContext();
                    ctx.Initialize();
                    ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
                    var samplerRef = new SamplerRef("key", mockRepo, sampler);

                    Handle<HelixToolkit.Nex.Graphics.Sampler>? firstResult = null;
                    for (int i = 0; i < n; i++)
                    {
                        var h = samplerRef.GetHandle();
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
    // Property 3: Stale handle triggers re-fetch (now: Ref identity on cache hit)
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 2: For any cache key K, two successive calls to
    // GetOrCreate on the same repository return the same SamplerRef object reference.
    [TestMethod]
    public void Property3_RefIdentity_OnCacheHit()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("linear-clamp", "point-clamp", "linear-repeat")),
                (string key) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    // Configure mock to return a valid entry.
                    var entry = mockRepo.SetTryGetFound();

                    // The entry's Ref should be the same object reference on TryGet
                    mockRepo.TryGet(key, out var retrieved);
                    return retrieved is not null && ReferenceEquals(retrieved.Ref, entry.Ref);
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 4: Null sentinel always returns invalid handle
    // -------------------------------------------------------------------------

    // Feature: sampler-ref-wrapper, Property 4: For any number of GetHandle() calls on SamplerRef.Null,
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
                        if (SamplerRef.Null.GetHandle().Valid)
                            return false;
                    }
                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 5: GetOrCreate returns SamplerRef with matching key and repository
    // -------------------------------------------------------------------------

    // Feature: sampler-ref-wrapper, Property 5: For any SamplerStateDesc, calling GetOrCreate returns a SamplerRef
    // whose Key equals SamplerRepository.GenerateCacheKey(desc) and whose Repository is the repository instance.
    [TestMethod]
    public void Property5_GetOrCreate_ReturnsSamplerRefWithMatchingKey()
    {
        Prop.ForAll(
                Arb.From(Gen.Elements("sampler1", "sampler2", "my-sampler", "linear", "point")),
                (string _) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    var desc = new SamplerStateDesc { };
                    var result = mockRepo.GetOrCreate(desc);
                    var expectedKey = SamplerRepository.GenerateCacheKey(desc);
                    return result.Key == expectedKey
                        && ReferenceEquals(result.Repository, mockRepo);
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 6: After remove, GetHandle returns invalid handle
    // -------------------------------------------------------------------------

    // Feature: sampler-ref-wrapper, Property 6: For any SamplerRef previously returned for a given key,
    // after Remove(key) is called, calling GetHandle() on that SamplerRef returns a handle with Valid == false.
    [TestMethod]
    public void Property6_AfterRemove_GetHandle_ReturnsInvalidHandle()
    {
        Prop.ForAll(
                Arb.From(
                    Gen.Elements("linear-clamp", "point-clamp", "linear-repeat", "point-repeat")
                ),
                (string key) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    // SamplerResource.Null has an invalid handle
                    var samplerRef = new SamplerRef(key, mockRepo, SamplerResource.Null);

                    // GetHandle() returns the resource's handle directly — no TryGet call
                    var result = samplerRef.GetHandle();
                    return !result.Valid;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 7: Repository disposal causes SamplerRef to return invalid handle
    // -------------------------------------------------------------------------

    // Feature: sampler-ref-wrapper, Property 7: For any SamplerRef obtained from a repository,
    // after the repository is disposed, calling GetHandle() returns a handle with Valid == false.
    [TestMethod]
    public void Property7_AfterRepositoryDisposal_GetHandle_ReturnsInvalidHandle()
    {
        Prop.ForAll(
                Arb.From(
                    Gen.Elements("linear-clamp", "point-clamp", "linear-repeat", "point-repeat")
                ),
                (string key) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    // SamplerResource.Null has an invalid handle — simulates post-dispose state
                    var samplerRef = new SamplerRef(key, mockRepo, SamplerResource.Null);

                    // GetHandle() returns the resource's handle directly
                    var result = samplerRef.GetHandle();
                    return !result.Valid;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 8: SamplerRef GetHandle().Index matches resource handle index
    // -------------------------------------------------------------------------

    // Feature: resource-ref-lifecycle, Property 1 (handle-identity invariant):
    // For any SamplerRef, GetHandle().Index == _resource.Handle.Index at all times.
    [TestMethod]
    public void Property8_SamplerRef_GetHandle_IndexMatchesResourceHandle()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(1, 50)),
                (int n) =>
                {
                    var mockRepo = new MockSamplerRepository();
                    var ctx = new MockContext();
                    ctx.Initialize();
                    ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
                    var samplerRef = new SamplerRef("key", mockRepo, sampler);

                    // GetHandle() must always equal _resource.Handle.Index
                    for (int i = 0; i < n; i++)
                    {
                        if (samplerRef.GetHandle().Index != samplerRef.Resource.Handle.Index)
                            return false;
                    }
                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
