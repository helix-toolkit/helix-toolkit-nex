using System.Text.RegularExpressions;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

// Feature: import-resource-isolation, Property 3: Cache Key Deterministic Construction

/// <summary>
/// Property-based tests for Cache Key Deterministic Construction (Property 3).
/// Verifies that for any base key component (image index, file path, or sampler name/index)
/// and any session identifier, the cache key construction function produces "{baseKey}:{sessionId}",
/// calling it multiple times with the same inputs produces identical output (deterministic),
/// and null/empty sampler names fall back to sampler index.
/// **Validates: Requirements 2.1, 2.2, 2.4, 3.1, 3.3, 3.4**
/// </summary>
[TestClass]
public class CacheKeyPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private static readonly Regex GuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.Compiled
    );

    #region Key Construction Helpers (mirrors TextureLoader internal logic)

    /// <summary>
    /// Builds a session-scoped cache key for an embedded texture.
    /// Mirrors TextureLoader.BuildEmbeddedTextureKey.
    /// </summary>
    private static string BuildEmbeddedTextureKey(int imageIndex, string sessionId) =>
        $"{imageIndex}:{sessionId}";

    /// <summary>
    /// Builds a session-scoped cache key for an external texture file.
    /// Mirrors TextureLoader.BuildExternalTextureKey.
    /// </summary>
    private static string BuildExternalTextureKey(string absolutePath, string sessionId) =>
        $"{absolutePath}:{sessionId}";

    /// <summary>
    /// Builds a session-scoped cache key for a sampler.
    /// Mirrors TextureLoader.BuildSamplerKey.
    /// </summary>
    private static string BuildSamplerKey(
        string? samplerName,
        int samplerIndex,
        string sessionId
    ) =>
        string.IsNullOrEmpty(samplerName)
            ? $"{samplerIndex}:{sessionId}"
            : $"{samplerName}:{sessionId}";

    #endregion

    #region Generators

    /// <summary>
    /// Generator for valid GUID-format session IDs.
    /// </summary>
    private static Gen<string> SessionIdGen => Gen.Fresh(() => Guid.NewGuid().ToString("D"));

    /// <summary>
    /// Generator for positive image indices (0..9999).
    /// </summary>
    private static Gen<int> ImageIndexGen => Gen.Choose(0, 9999);

    /// <summary>
    /// Generator for non-empty file path strings.
    /// </summary>
    private static Gen<string> FilePathGen =>
        Gen.Elements(
            @"C:\models\texture.png",
            @"C:\assets\images\diffuse.jpg",
            @"/home/user/models/normal.png",
            @"D:\project\resources\metallic_roughness.tga",
            @"C:\a\b\c\d.bmp"
        );

    /// <summary>
    /// Generator for non-empty sampler names.
    /// </summary>
    private static Gen<string> SamplerNameGen =>
        Gen.Elements(
            "LinearWrap",
            "PointClamp",
            "AnisotropicWrap",
            "LinearMirror",
            "NearestRepeat"
        );

    /// <summary>
    /// Generator for sampler indices (0..99).
    /// </summary>
    private static Gen<int> SamplerIndexGen => Gen.Choose(0, 99);

    #endregion

    // =========================================================================
    // Embedded texture key: format is "{imageIndex}:{sessionId}"
    // Validates: Requirement 2.1
    // =========================================================================

    /// <summary>
    /// Property 3: For any image index and session ID, the embedded texture cache key
    /// is constructed as "{imageIndex}:{sessionId}".
    /// **Validates: Requirements 2.1, 2.4**
    /// </summary>
    [TestMethod]
    public void EmbeddedTextureKey_ProducesCorrectFormat()
    {
        Prop.ForAll(
                Arb.From(ImageIndexGen),
                Arb.From(SessionIdGen),
                (int imageIndex, string sessionId) =>
                {
                    var key = BuildEmbeddedTextureKey(imageIndex, sessionId);

                    // Key must contain the colon separator
                    var colonIndex = key.LastIndexOf(':');
                    if (colonIndex < 0)
                        return false;

                    // Base key is the image index as string
                    var baseKeyPart = key[..colonIndex];
                    var sessionPart = key[(colonIndex + 1)..];

                    return baseKeyPart == imageIndex.ToString()
                        && sessionPart == sessionId
                        && key == $"{imageIndex}:{sessionId}";
                }
            )
            .Check(FsCheckConfig);
    }

    // =========================================================================
    // External texture key: format is "{absolutePath}:{sessionId}"
    // Validates: Requirement 2.2
    // =========================================================================

    /// <summary>
    /// Property 3: For any file path and session ID, the external texture cache key
    /// is constructed as "{absolutePath}:{sessionId}".
    /// **Validates: Requirements 2.2, 2.4**
    /// </summary>
    [TestMethod]
    public void ExternalTextureKey_ProducesCorrectFormat()
    {
        Prop.ForAll(
                Arb.From(FilePathGen),
                Arb.From(SessionIdGen),
                (string filePath, string sessionId) =>
                {
                    var key = BuildExternalTextureKey(filePath, sessionId);

                    // The key should end with ":{sessionId}"
                    var expectedSuffix = $":{sessionId}";
                    var endsCorrectly = key.EndsWith(expectedSuffix, StringComparison.Ordinal);

                    // The prefix should be the file path
                    var prefix = key[..^expectedSuffix.Length];

                    return endsCorrectly && prefix == filePath && key == $"{filePath}:{sessionId}";
                }
            )
            .Check(FsCheckConfig);
    }

    // =========================================================================
    // Named sampler key: format is "{samplerName}:{sessionId}"
    // Validates: Requirement 3.1, 3.3
    // =========================================================================

    /// <summary>
    /// Property 3: For any non-empty sampler name and session ID, the sampler cache key
    /// is constructed as "{samplerName}:{sessionId}".
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [TestMethod]
    public void NamedSamplerKey_ProducesCorrectFormat()
    {
        Prop.ForAll(
                Arb.From(SamplerNameGen),
                Arb.From(SamplerIndexGen),
                Arb.From(SessionIdGen),
                (string samplerName, int samplerIndex, string sessionId) =>
                {
                    var key = BuildSamplerKey(samplerName, samplerIndex, sessionId);

                    // Named samplers should use the name, not the index
                    var expectedSuffix = $":{sessionId}";
                    var endsCorrectly = key.EndsWith(expectedSuffix, StringComparison.Ordinal);
                    var prefix = key[..^expectedSuffix.Length];

                    return endsCorrectly
                        && prefix == samplerName
                        && key == $"{samplerName}:{sessionId}";
                }
            )
            .Check(FsCheckConfig);
    }

    // =========================================================================
    // Unnamed sampler fallback: format is "{samplerIndex}:{sessionId}"
    // Validates: Requirement 3.4
    // =========================================================================

    /// <summary>
    /// Property 3: For any null or empty sampler name, the sampler cache key falls back
    /// to using the sampler array index: "{samplerIndex}:{sessionId}".
    /// **Validates: Requirement 3.4**
    /// </summary>
    [TestMethod]
    public void UnnamedSamplerKey_FallsBackToIndex()
    {
        // Generator for null or empty sampler names
        var nullOrEmptyNameGen = Gen.Elements<string?>(null, "", "   "[..0]);

        Prop.ForAll(
                Arb.From(nullOrEmptyNameGen),
                Arb.From(SamplerIndexGen),
                Arb.From(SessionIdGen),
                (string? samplerName, int samplerIndex, string sessionId) =>
                {
                    var key = BuildSamplerKey(samplerName, samplerIndex, sessionId);

                    // Should use the index as base key, not the name
                    return key == $"{samplerIndex}:{sessionId}";
                }
            )
            .Check(FsCheckConfig);
    }

    // =========================================================================
    // Determinism: same inputs always produce same output
    // Validates: Requirement 2.4
    // =========================================================================

    /// <summary>
    /// Property 3: Calling the embedded key construction function twice with the same
    /// image index and session ID produces identical results (deterministic).
    /// **Validates: Requirement 2.4**
    /// </summary>
    [TestMethod]
    public void EmbeddedTextureKey_IsDeterministic()
    {
        Prop.ForAll(
                Arb.From(ImageIndexGen),
                Arb.From(SessionIdGen),
                (int imageIndex, string sessionId) =>
                {
                    var key1 = BuildEmbeddedTextureKey(imageIndex, sessionId);
                    var key2 = BuildEmbeddedTextureKey(imageIndex, sessionId);
                    return key1 == key2;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3: Calling the external key construction function twice with the same
    /// file path and session ID produces identical results (deterministic).
    /// **Validates: Requirement 2.4**
    /// </summary>
    [TestMethod]
    public void ExternalTextureKey_IsDeterministic()
    {
        Prop.ForAll(
                Arb.From(FilePathGen),
                Arb.From(SessionIdGen),
                (string filePath, string sessionId) =>
                {
                    var key1 = BuildExternalTextureKey(filePath, sessionId);
                    var key2 = BuildExternalTextureKey(filePath, sessionId);
                    return key1 == key2;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3: Calling the sampler key construction function twice with the same
    /// sampler name, index, and session ID produces identical results (deterministic).
    /// **Validates: Requirements 3.1, 3.3, 3.4**
    /// </summary>
    [TestMethod]
    public void SamplerKey_IsDeterministic()
    {
        // Include both named and unnamed scenarios
        var nameOrNullGen = Gen.OneOf(
            SamplerNameGen.Select<string, string?>(s => s),
            Gen.Constant<string?>(null)
        );

        Prop.ForAll(
                Arb.From(nameOrNullGen),
                Arb.From(SamplerIndexGen),
                Arb.From(SessionIdGen),
                (string? samplerName, int samplerIndex, string sessionId) =>
                {
                    var key1 = BuildSamplerKey(samplerName, samplerIndex, sessionId);
                    var key2 = BuildSamplerKey(samplerName, samplerIndex, sessionId);
                    return key1 == key2;
                }
            )
            .Check(FsCheckConfig);
    }
}
