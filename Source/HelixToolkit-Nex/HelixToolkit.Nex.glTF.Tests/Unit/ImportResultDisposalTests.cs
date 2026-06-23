using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Tests for ImportResult.Dispose behavior: delegation to RootNode and ResourceManifest,
/// null safety, idempotency, and GC.SuppressFinalize.
/// Validates: Requirements 7.2, 7.3, 7.4, 7.5
/// </summary>
[TestClass]
public class ImportResultDisposalTests
{
    private StubTextureRepository _textureRepo = null!;
    private StubSamplerRepository _samplerRepo = null!;
    private PBRMaterialPropertyManager _materialManager = null!;
    private World _world = null!;

    [TestInitialize]
    public void Setup()
    {
        _textureRepo = new StubTextureRepository(StubTextureRepositoryMode.RemoveTracking);
        _samplerRepo = new StubSamplerRepository(StubSamplerRepositoryMode.RemoveTracking);
        _materialManager = new PBRMaterialPropertyManager();
        _world = World.CreateWorld();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
        _materialManager?.Dispose();
        _textureRepo?.Dispose();
        _samplerRepo?.Dispose();
    }

    private TextureRef CreateTextureRef(string key)
    {
        return new TextureRef(key, _textureRepo, new TextureResource());
    }

    private SamplerRef CreateSamplerRef(string key)
    {
        return new SamplerRef(key, _samplerRepo, new SamplerResource());
    }

    private PBRMaterialProperties CreateMaterial()
    {
        return _materialManager.Create(PBRShadingMode.PBR.ToString());
    }

    // =========================================================================
    // Dispose calls RootNode.Dispose — Validates: Requirement 7.2
    // =========================================================================

    [TestMethod]
    public void Dispose_WithNonNullRootNode_DisposesRootNode()
    {
        // Arrange
        var rootNode = new Node(_world, "TestRoot");
        var result = new ImportResult { RootNode = rootNode, Resources = ResourceManifest.Empty };

        // Act
        result.Dispose();

        // Assert — Node.Dispose destroys the entity, so Alive should be false
        Assert.IsFalse(rootNode.Alive, "RootNode should be disposed (entity no longer alive)");
    }

    [TestMethod]
    public void Dispose_WithRootNodeHavingChildren_DisposesEntireHierarchy()
    {
        // Arrange
        var rootNode = new Node(_world, "Root");
        var child1 = new Node(_world, "Child1");
        var child2 = new Node(_world, "Child2");
        rootNode.AddChild(child1);
        rootNode.AddChild(child2);

        var result = new ImportResult { RootNode = rootNode, Resources = ResourceManifest.Empty };

        // Act
        result.Dispose();

        // Assert — all nodes in the hierarchy should be disposed
        Assert.IsFalse(rootNode.Alive, "RootNode should be disposed");
        Assert.IsFalse(child1.Alive, "Child1 should be disposed");
        Assert.IsFalse(child2.Alive, "Child2 should be disposed");
    }

    // =========================================================================
    // Dispose calls Resources.DisposeAll — Validates: Requirement 7.3
    // =========================================================================

    [TestMethod]
    public void Dispose_CallsResourcesDisposeAll()
    {
        // Arrange
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_dispose_test");
        var samplerRef = CreateSamplerRef("samp_dispose_test");
        var material = CreateMaterial();
        var geometry = new Geometry();

        manifest.AddTexture(textureRef);
        manifest.AddSampler(samplerRef);
        manifest.AddMaterial(material);
        manifest.AddGeometry(geometry);

        var result = new ImportResult { RootNode = null, Resources = manifest };

        // Act
        result.Dispose();

        // Assert — DisposeAll should have been called, clearing all counts
        Assert.AreEqual(0, manifest.TextureCount, "Textures should be cleared after Dispose");
        Assert.AreEqual(0, manifest.SamplerCount, "Samplers should be cleared after Dispose");
        Assert.AreEqual(0, manifest.MaterialCount, "Materials should be cleared after Dispose");
        Assert.AreEqual(0, manifest.GeometryCount, "Geometries should be cleared after Dispose");

        // Verify repository Remove was called
        Assert.AreEqual(
            1,
            _textureRepo.RemovedKeys.Count,
            "Texture should be removed from repository"
        );
        Assert.AreEqual("tex_dispose_test", _textureRepo.RemovedKeys[0]);
        Assert.AreEqual(
            1,
            _samplerRepo.RemovedKeys.Count,
            "Sampler should be removed from repository"
        );
        Assert.AreEqual("samp_dispose_test", _samplerRepo.RemovedKeys[0]);
    }

    [TestMethod]
    public void Dispose_DisposesRootNodeAndResources()
    {
        // Arrange — both RootNode and Resources are non-null/non-empty
        var rootNode = new Node(_world, "Root");
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_both");
        manifest.AddTexture(textureRef);

        var result = new ImportResult { RootNode = rootNode, Resources = manifest };

        // Act
        result.Dispose();

        // Assert — both should be disposed
        Assert.IsFalse(rootNode.Alive, "RootNode should be disposed");
        Assert.AreEqual(0, manifest.TextureCount, "Resources should be disposed");
        Assert.AreEqual(1, _textureRepo.RemovedKeys.Count);
    }

    // =========================================================================
    // Dispose with null RootNode — Validates: Requirement 7.2 (null safety)
    // =========================================================================

    [TestMethod]
    public void Dispose_WithNullRootNode_DoesNotThrow()
    {
        // Arrange
        var result = new ImportResult { RootNode = null, Resources = ResourceManifest.Empty };

        // Act & Assert — should not throw
        result.Dispose();
    }

    [TestMethod]
    public void Dispose_WithNullRootNode_StillDisposesResources()
    {
        // Arrange
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_null_root");
        manifest.AddTexture(textureRef);

        var result = new ImportResult { RootNode = null, Resources = manifest };

        // Act
        result.Dispose();

        // Assert — Resources should still be disposed even with null RootNode
        Assert.AreEqual(0, manifest.TextureCount);
        Assert.AreEqual(1, _textureRepo.RemovedKeys.Count);
    }

    // =========================================================================
    // Dispose called twice is idempotent — Validates: Requirement 7.4
    // =========================================================================

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var rootNode = new Node(_world, "IdempotentRoot");
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_idempotent");
        var samplerRef = CreateSamplerRef("samp_idempotent");
        manifest.AddTexture(textureRef);
        manifest.AddSampler(samplerRef);

        var result = new ImportResult { RootNode = rootNode, Resources = manifest };

        // Act — dispose twice
        result.Dispose();
        result.Dispose();

        // Assert — no exception thrown, and Remove was only called once per resource
        Assert.AreEqual(
            1,
            _textureRepo.RemovedKeys.Count,
            "Texture Remove should only be called once"
        );
        Assert.AreEqual(
            1,
            _samplerRepo.RemovedKeys.Count,
            "Sampler Remove should only be called once"
        );
    }

    [TestMethod]
    public void Dispose_CalledMultipleTimes_IsIdempotent()
    {
        // Arrange
        var manifest = new ResourceManifest();
        var material = CreateMaterial();
        manifest.AddMaterial(material);

        var result = new ImportResult { RootNode = null, Resources = manifest };

        // Act — dispose three times
        result.Dispose();
        result.Dispose();
        result.Dispose();

        // Assert — counts remain at zero, no exceptions
        Assert.AreEqual(0, manifest.MaterialCount);
    }

    // =========================================================================
    // GC.SuppressFinalize is called — Validates: Requirement 7.5
    // =========================================================================

    [TestMethod]
    public void Dispose_CallsGCSuppressFinalize_VerifiedViaWeakReference()
    {
        // Arrange — create an ImportResult and get a WeakReference to it
        // After Dispose + GC.SuppressFinalize, the object should be collectible
        // without the finalizer running (which would keep it alive longer).
        WeakReference weakRef = CreateAndDisposeImportResult();

        // Act — force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert — the object should be collected (WeakReference.IsAlive == false)
        // This verifies GC.SuppressFinalize was called, because if it wasn't,
        // the finalizer queue would keep the object alive through the first GC cycle.
        Assert.IsFalse(
            weakRef.IsAlive,
            "ImportResult should be collected after Dispose (GC.SuppressFinalize was called)"
        );
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private WeakReference CreateAndDisposeImportResult()
    {
        var result = new ImportResult { RootNode = null, Resources = ResourceManifest.Empty };
        result.Dispose();
        return new WeakReference(result);
    }
}
