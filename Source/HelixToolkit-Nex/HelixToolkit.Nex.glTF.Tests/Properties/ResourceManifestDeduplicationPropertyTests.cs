using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 2: Deduplication Invariant

/// <summary>
/// Property-based tests for the Deduplication Invariant (Property 2).
/// Verifies that for any sequence of AddTexture calls containing duplicate cache keys,
/// or AddSampler calls containing duplicate references, the resulting collection contains
/// each unique resource exactly once.
/// **Validates: Requirements 1.6, 2.3, 3.2**
/// </summary>
[TestClass]
public class ResourceManifestDeduplicationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Property 2: For any sequence of AddTexture calls with duplicate cache keys,
    /// the TextureCount SHALL equal the number of distinct keys.
    /// **Validates: Requirements 1.6, 2.3**
    /// </summary>
    [TestMethod]
    public void AddTexture_WithDuplicateKeys_CollectionCountEqualsDistinctKeys()
    {
        // Generator: a non-empty list of non-empty strings (texture keys), allowing duplicates
        var keysGen = Gen.NonEmptyListOf(Gen.Elements("texA", "texB", "texC", "texD", "texE"));

        Prop.ForAll(
                Arb.From(keysGen),
                (List<string> keys) =>
                {
                    var manifest = new ResourceManifest();
                    var repo = StubTextureRepository.Instance;

                    // Add textures with potentially duplicate keys
                    foreach (var key in keys)
                    {
                        var textureRef = new TextureRef(key, repo, TextureResource.Null);
                        manifest.AddTexture(textureRef);
                    }

                    // The count should equal the number of distinct keys
                    var expectedCount = keys.Distinct(StringComparer.Ordinal).Count();
                    return manifest.TextureCount == expectedCount
                        && manifest.Textures.Count == expectedCount;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2: For any sequence of AddTexture calls with the same key repeated N times,
    /// the collection SHALL contain exactly one entry.
    /// **Validates: Requirements 1.6, 2.3**
    /// </summary>
    [TestMethod]
    public void AddTexture_SameKeyRepeated_CollectionContainsExactlyOne()
    {
        // Generator: a key and a repeat count
        var keyGen = Gen.Elements("alpha", "beta", "gamma");
        var repeatGen = Gen.Choose(1, 20);

        Prop.ForAll(
                Arb.From(keyGen),
                Arb.From(repeatGen),
                (string key, int repeatCount) =>
                {
                    var manifest = new ResourceManifest();
                    var repo = StubTextureRepository.Instance;

                    for (int i = 0; i < repeatCount; i++)
                    {
                        var textureRef = new TextureRef(key, repo, TextureResource.Null);
                        manifest.AddTexture(textureRef);
                    }

                    return manifest.TextureCount == 1
                        && manifest.Textures.Count == 1
                        && manifest.Textures[0].Key == key;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2: For any sequence of AddSampler calls with duplicate references (same instance),
    /// the SamplerCount SHALL equal the number of distinct references.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void AddSampler_WithDuplicateReferences_CollectionCountEqualsDistinctReferences()
    {
        // Create a fixed pool of distinct SamplerRef instances
        var repo = StubSamplerRepository.Instance;
        var distinctSamplers = new[]
        {
            new SamplerRef("sampler_0", repo, SamplerResource.Null),
            new SamplerRef("sampler_1", repo, SamplerResource.Null),
            new SamplerRef("sampler_2", repo, SamplerResource.Null),
            new SamplerRef("sampler_3", repo, SamplerResource.Null),
        };

        // Generator: a non-empty list of indices into the pool (allowing duplicates)
        var indicesGen = Gen.NonEmptyListOf(Gen.Choose(0, distinctSamplers.Length - 1));

        Prop.ForAll(
                Arb.From(indicesGen),
                (List<int> indices) =>
                {
                    var manifest = new ResourceManifest();

                    // Add samplers using indices (same reference may be added multiple times)
                    foreach (var idx in indices)
                    {
                        manifest.AddSampler(distinctSamplers[idx]);
                    }

                    // Count should equal number of distinct references used
                    var expectedCount = indices.Distinct().Count();
                    return manifest.SamplerCount == expectedCount
                        && manifest.Samplers.Count == expectedCount;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2: For any sequence of AddSampler calls with the same reference repeated N times,
    /// the collection SHALL contain exactly one entry.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void AddSampler_SameReferenceRepeated_CollectionContainsExactlyOne()
    {
        var repo = StubSamplerRepository.Instance;
        var singleSampler = new SamplerRef("shared_sampler", repo, SamplerResource.Null);

        // Generator: repeat count 1..20
        var repeatGen = Gen.Choose(1, 20);

        Prop.ForAll(
                Arb.From(repeatGen),
                (int repeatCount) =>
                {
                    var manifest = new ResourceManifest();

                    for (int i = 0; i < repeatCount; i++)
                    {
                        manifest.AddSampler(singleSampler);
                    }

                    return manifest.SamplerCount == 1
                        && manifest.Samplers.Count == 1
                        && ReferenceEquals(manifest.Samplers[0], singleSampler);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2: Sampler deduplication is by reference identity, NOT by key.
    /// Two different SamplerRef instances with the same Key string are treated as distinct.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void AddSampler_DifferentInstancesSameKey_BothAreTracked()
    {
        var repo = StubSamplerRepository.Instance;

        // Generator: number of distinct instances (all with same key) 1..5
        var countGen = Gen.Choose(2, 5);

        Prop.ForAll(
                Arb.From(countGen),
                (int instanceCount) =>
                {
                    var manifest = new ResourceManifest();

                    // Create multiple distinct SamplerRef instances with the same key
                    for (int i = 0; i < instanceCount; i++)
                    {
                        var sampler = new SamplerRef("same_key", repo, SamplerResource.Null);
                        manifest.AddSampler(sampler);
                    }

                    // Each is a distinct reference, so all should be tracked
                    return manifest.SamplerCount == instanceCount
                        && manifest.Samplers.Count == instanceCount;
                }
            )
            .Check(FsCheckConfig);
    }
}
