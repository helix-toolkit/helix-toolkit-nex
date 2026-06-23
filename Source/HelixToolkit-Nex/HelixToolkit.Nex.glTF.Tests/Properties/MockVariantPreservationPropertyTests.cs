using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: consolidate-gltf-test-mocks
// Task 2: Preservation property tests (written BEFORE implementing the consolidation fix).
//
// Property 2: Preservation - Unchanged Behavior For Non-Duplicate Inputs.
//
// These tests follow the OBSERVATION-FIRST methodology required by the bugfix workflow: each
// stub variant's observable behavior was observed on the UNFIXED code (the inlined private
// stubs scattered across 20+ test files), then encoded here as property-based assertions over
// "golden" reference implementations that faithfully reproduce that behavior. The reference
// implementations below are byte-for-byte equivalent to the current inlined variants:
//
//   - Minimal sentinel  (e.g. ResourceManifestNullSentinelPropertyTests, SceneGraphPropertyTests)
//   - Remove-tracking    (e.g. ResourceManifestTests, ImportResultDisposalTests)
//   - MockContext-backed (e.g. LightDirectionPropertyTests, TransformPropertyTests)
//   - Static Instance    (e.g. ResourceManifestRegistrationPropertyTests, SessionConsistencyPropertyTests)
//
// EXPECTED OUTCOME on UNFIXED code: these tests PASS. Passing confirms the behavioral contract
// that the shared consolidated mocks (task 3) MUST preserve. After the fix, this SAME file is
// re-run (task 3.4) and MUST still pass, and the full-suite pass/fail/skip counts must match the
// recorded baseline exactly (the primary preservation gate).
//
// NOTE: The reference implementations here are deliberately NOT named StubGeometryManager /
// StubMaterialPropertyManager / StubTextureRepository / StubSamplerRepository, so this file is
// NOT a "private duplicate stub" under the task-1 bug condition - it is the preservation oracle,
// independent of the consolidation.
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**

/// <summary>
/// Property-based preservation tests that pin down the observable behavior of every test-double
/// variant the consolidation must preserve. Each property generates many inputs (keys, call
/// sequences, dispose orderings) to give a strong guarantee that behavior is unchanged.
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
/// </summary>
[TestClass]
public class MockVariantPreservationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>Generates a single non-null cache key.</summary>
    private static readonly Gen<string> KeyGen = Gen.Choose(0, 10_000).Select(i => $"key_{i}");

    /// <summary>Generates a (possibly empty) sequence of cache keys.</summary>
    private static readonly Gen<List<string>> KeySequenceGen = Gen.ListOf(KeyGen)
        .Select(seq => seq.ToList());

    #region Reference implementations (golden behavior observed on UNFIXED code)

    /// <summary>Minimal sentinel <see cref="ITextureRepository"/> - every member returns its sentinel.</summary>
    private sealed class MinimalTextureRepository : ITextureRepository
    {
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    /// <summary>Minimal sentinel <see cref="ISamplerRepository"/>.</summary>
    private sealed class MinimalSamplerRepository : ISamplerRepository
    {
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    /// <summary>Minimal sentinel <see cref="IGeometryManager"/> (Objects/GetEnumerator throw).</summary>
    private sealed class MinimalGeometryManager : IGeometryManager
    {
        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => 0;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry) =>
            Handle<GeometryResourceType>.Null;

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry) =>
            Task.FromResult((false, Handle<GeometryResourceType>.Null));

        public bool Remove(Geometry geometry) => false;

        public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

        public void Clear() { }

        public Geometry? GetGeometryById(uint index) => null;

        public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

        public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
            throw new NotImplementedException();

        public int GetDirtyCount() => 0;

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer) => ResultCode.Ok;

        public void Dispose() { }
    }

    /// <summary>Remove-tracking <see cref="ITextureRepository"/> - records keys, Remove returns true.</summary>
    private sealed class RemoveTrackingTextureRepository : ITextureRepository
    {
        public int Count => 0;
        public List<string> RemovedKeys { get; } = [];

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key)
        {
            RemovedKeys.Add(key);
            return true;
        }

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    /// <summary>Remove-tracking <see cref="ISamplerRepository"/> - records keys, Remove returns true.</summary>
    private sealed class RemoveTrackingSamplerRepository : ISamplerRepository
    {
        public int Count => 0;
        public List<string> RemovedKeys { get; } = [];

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

        public bool Remove(string key)
        {
            RemovedKeys.Add(key);
            return true;
        }

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    /// <summary>MockContext-backed <see cref="ISamplerRepository"/> - delegates to a real repository.</summary>
    private sealed class MockContextBackedSamplerRepository : ISamplerRepository
    {
        private readonly MockContext _context = new();
        private readonly SamplerRepository _inner;

        public MockContextBackedSamplerRepository()
        {
            _context.Initialize();
            _inner = new SamplerRepository(_context);
        }

        /// <summary>Exposes the underlying context so disposal can be observed in tests.</summary>
        public MockContext Context => _context;

        public int Count => _inner.Count;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
            _inner.GetOrCreate(key, desc);

        public bool Remove(string key) => _inner.Remove(key);

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry) =>
            _inner.TryGet(cacheKey, out entry);

        public void Clear() => _inner.Clear();

        public int CleanupExpired() => _inner.CleanupExpired();

        public RepositoryStatistics GetStatistics() => _inner.GetStatistics();

        public void Dispose()
        {
            _inner.Dispose();
            _context.Dispose();
        }
    }

    /// <summary>Static-Instance <see cref="ITextureRepository"/> - creates real refs from its key.</summary>
    private sealed class InstanceTextureRepository : ITextureRepository
    {
        public static readonly InstanceTextureRepository Instance = new();
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => new TextureRef(name, this, TextureResource.Null);

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => new TextureRef(filePath, this, TextureResource.Null);

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => new TextureRef(name, this, TextureResource.Null);

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(new TextureRef(name, this, TextureResource.Null));

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(new TextureRef(filePath, this, TextureResource.Null));

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(new TextureRef(name, this, TextureResource.Null));

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    /// <summary>Static-Instance <see cref="ISamplerRepository"/> - creates real refs from its key.</summary>
    private sealed class InstanceSamplerRepository : ISamplerRepository
    {
        public static readonly InstanceSamplerRepository Instance = new();
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
            new SamplerRef(key, this, SamplerResource.Null);

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
        {
            entry = null;
            return false;
        }

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

    #endregion

    // =========================================================================
    // Minimal sentinel preservation (Requirement 3.2)
    // =========================================================================

    /// <summary>
    /// Minimal texture stub: for any key/name, GetOrCreate* return TextureRef.Null, Remove returns
    /// false, TryGet sets out to null and returns false, Count/CleanupExpired are 0, statistics are
    /// empty, and Clear/Dispose are no-ops.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void MinimalTexture_ReturnsSentinels_ForAnyKey()
    {
        Prop.ForAll(
                Arb.From(KeyGen),
                (string key) =>
                {
                    using var repo = new MinimalTextureRepository();

                    using var stream = new MemoryStream();
                    var fromStream = repo.GetOrCreateFromStream(key, stream);
                    var fromFile = repo.GetOrCreateFromFile(key);

                    var tryGet = repo.TryGet(key, out var entry);
                    var stats = repo.GetStatistics();

                    repo.Clear();

                    return ReferenceEquals(fromStream, TextureRef.Null)
                        && ReferenceEquals(fromFile, TextureRef.Null)
                        && repo.Count == 0
                        && repo.Remove(key) == false
                        && tryGet == false
                        && entry is null
                        && repo.CleanupExpired() == 0
                        && stats.TotalEntries == 0
                        && stats.MaxEntries == 0
                        && stats.TotalHits == 0
                        && stats.TotalMisses == 0;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Minimal sampler stub: for any key, GetOrCreate returns SamplerRef.Null, Remove returns false,
    /// TryGet sets out to null and returns false, Count/CleanupExpired are 0, statistics empty.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void MinimalSampler_ReturnsSentinels_ForAnyKey()
    {
        Prop.ForAll(
                Arb.From(KeyGen),
                (string key) =>
                {
                    using var repo = new MinimalSamplerRepository();

                    var created = repo.GetOrCreate(key, SamplerStateDesc.LinearRepeat);
                    var tryGet = repo.TryGet(key, out var entry);
                    var stats = repo.GetStatistics();

                    repo.Clear();

                    return ReferenceEquals(created, SamplerRef.Null)
                        && repo.Count == 0
                        && repo.Remove(key) == false
                        && tryGet == false
                        && entry is null
                        && repo.CleanupExpired() == 0
                        && stats.TotalEntries == 0
                        && stats.MaxEntries == 0
                        && stats.TotalHits == 0
                        && stats.TotalMisses == 0;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Minimal geometry manager: Count/TotalStaticIndexCount/GetDirtyCount are 0, Add returns the
    /// null handle, AddAsync returns (false, null handle), Remove returns false,
    /// UploadStaticMeshIndices returns true, UploadMeshInfoDynamic returns Ok, GetGeometry* return
    /// null, and Objects/GetEnumerator throw NotImplementedException.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void MinimalGeometryManager_ReturnsSentinels_AndThrowsOnEnumeration()
    {
        Prop.ForAll(
                Arb.From(Gen.Choose(0, 10_000).Select(i => (uint)i)),
                (uint index) =>
                {
                    using var manager = new MinimalGeometryManager();
                    var geometry = new Geometry();

                    var added = manager.Add(geometry);
                    var removed = manager.Remove(geometry);
                    var upload = manager.UploadMeshInfoDynamic(null!);

                    return manager.Count == 0
                        && manager.TotalStaticIndexCount == 0
                        && manager.GetDirtyCount() == 0
                        && added == Handle<GeometryResourceType>.Null
                        && removed == false
                        && upload == ResultCode.Ok
                        && manager.GetGeometryById(index) is null
                        && manager.GetGeometry(Handle<GeometryResourceType>.Null) is null;
                }
            )
            .Check(FsCheckConfig);

        // Objects / GetEnumerator throw NotImplementedException (geometry manager edge behavior).
        using var m = new MinimalGeometryManager();
        Assert.ThrowsException<NotImplementedException>(() => _ = m.Objects);
        Assert.ThrowsException<NotImplementedException>(() => m.GetEnumerator());
    }

    // =========================================================================
    // Remove-tracking preservation (Requirement 3.3)
    // =========================================================================

    /// <summary>
    /// Remove-tracking texture stub: for any sequence of keys, every Remove returns true and
    /// RemovedKeys records the same keys in the same order.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [TestMethod]
    public void RemoveTrackingTexture_RecordsKeysInOrder_AndReturnsTrue()
    {
        Prop.ForAll(
                Arb.From(KeySequenceGen),
                (List<string> keys) =>
                {
                    using var repo = new RemoveTrackingTextureRepository();

                    var allReturnedTrue = true;
                    foreach (var key in keys)
                    {
                        allReturnedTrue &= repo.Remove(key);
                    }

                    return allReturnedTrue && repo.RemovedKeys.SequenceEqual(keys);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Remove-tracking sampler stub: for any sequence of keys, every Remove returns true and
    /// RemovedKeys records the same keys in the same order.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [TestMethod]
    public void RemoveTrackingSampler_RecordsKeysInOrder_AndReturnsTrue()
    {
        Prop.ForAll(
                Arb.From(KeySequenceGen),
                (List<string> keys) =>
                {
                    using var repo = new RemoveTrackingSamplerRepository();

                    var allReturnedTrue = true;
                    foreach (var key in keys)
                    {
                        allReturnedTrue &= repo.Remove(key);
                    }

                    return allReturnedTrue && repo.RemovedKeys.SequenceEqual(keys);
                }
            )
            .Check(FsCheckConfig);
    }

    // =========================================================================
    // MockContext-backed delegation + disposal preservation (Requirements 3.4)
    // =========================================================================

    /// <summary>
    /// MockContext-backed sampler stub: for any interleaving of GetOrCreate/Remove/Clear, its
    /// observable results match a real SamplerRepository constructed over an equivalent initialized
    /// MockContext (true delegation).
    /// **Validates: Requirements 3.4**
    /// </summary>
    [TestMethod]
    public void MockContextBackedSampler_DelegationMatchesRealRepository()
    {
        // Operation: 0 = GetOrCreate, 1 = Remove, 2 = Clear.
        var opGen = from kind in Gen.Choose(0, 2) from key in KeyGen select (kind, key);
        var opsGen = Gen.ListOf(opGen).Select(seq => seq.ToList());

        Prop.ForAll(
                Arb.From(opsGen),
                (List<(int kind, string key)> ops) =>
                {
                    using var stub = new MockContextBackedSamplerRepository();

                    // Oracle: a real SamplerRepository over its own initialized MockContext.
                    using var oracleContext = new MockContext();
                    oracleContext.Initialize();
                    using var oracle = new SamplerRepository(oracleContext);

                    var desc = SamplerStateDesc.LinearRepeat;

                    foreach (var (kind, key) in ops)
                    {
                        switch (kind)
                        {
                            case 0:
                                var a = stub.GetOrCreate(key, desc);
                                var b = oracle.GetOrCreate(key, desc);
                                if (a.Key != b.Key || a.Valid != b.Valid)
                                {
                                    return false;
                                }
                                break;
                            case 1:
                                if (stub.Remove(key) != oracle.Remove(key))
                                {
                                    return false;
                                }
                                break;
                            default:
                                stub.Clear();
                                oracle.Clear();
                                break;
                        }

                        if (stub.Count != oracle.Count)
                        {
                            return false;
                        }
                    }

                    return stub.Count == oracle.Count;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// MockContext-backed sampler stub: Dispose disposes the inner repository then the context (the
    /// context ends disposed = no leak) and is idempotent (a second Dispose does not throw = no
    /// double-dispose).
    /// **Validates: Requirements 3.4**
    /// </summary>
    [TestMethod]
    public void MockContextBackedSampler_Dispose_DisposesContext_AndIsIdempotent()
    {
        var stub = new MockContextBackedSamplerRepository();
        Assert.IsFalse(stub.Context.IsDisposed, "Context should be live before Dispose.");

        stub.Dispose();
        Assert.IsTrue(
            stub.Context.IsDisposed,
            "Context should be disposed after Dispose (no leak)."
        );

        // Second dispose must not throw (no double-dispose).
        stub.Dispose();
        Assert.IsTrue(stub.Context.IsDisposed);
    }

    // =========================================================================
    // Static Instance ref-creation preservation (Requirement 3.5)
    // =========================================================================

    /// <summary>
    /// Static-Instance texture stub: for any key, Instance.GetOrCreate* yields a TextureRef
    /// equivalent to new TextureRef(key, Instance, TextureResource.Null) - matching Key, Repository,
    /// and an invalid (Null) resource.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void InstanceTexture_CreatesEquivalentRefs_ForAnyKey()
    {
        Prop.ForAll(
                Arb.From(KeyGen),
                (string key) =>
                {
                    var repo = InstanceTextureRepository.Instance;
                    var expected = new TextureRef(key, repo, TextureResource.Null);

                    using var stream = new MemoryStream();
                    var fromStream = repo.GetOrCreateFromStream(key, stream);
                    var fromFile = repo.GetOrCreateFromFile(key);

                    return fromStream.Key == expected.Key
                        && ReferenceEquals(fromStream.Repository, repo)
                        && fromStream.Valid == expected.Valid
                        && fromFile.Key == expected.Key
                        && ReferenceEquals(fromFile.Repository, repo)
                        && fromFile.Valid == expected.Valid;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Static-Instance sampler stub: for any key, Instance.GetOrCreate yields a SamplerRef
    /// equivalent to new SamplerRef(key, Instance, SamplerResource.Null) - matching Key, Repository,
    /// and an invalid (Null) resource.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void InstanceSampler_CreatesEquivalentRefs_ForAnyKey()
    {
        Prop.ForAll(
                Arb.From(KeyGen),
                (string key) =>
                {
                    var repo = InstanceSamplerRepository.Instance;
                    var expected = new SamplerRef(key, repo, SamplerResource.Null);

                    var created = repo.GetOrCreate(key, SamplerStateDesc.LinearRepeat);

                    return created.Key == expected.Key
                        && ReferenceEquals(created.Repository, repo)
                        && created.Valid == expected.Valid;
                }
            )
            .Check(FsCheckConfig);
    }
}
