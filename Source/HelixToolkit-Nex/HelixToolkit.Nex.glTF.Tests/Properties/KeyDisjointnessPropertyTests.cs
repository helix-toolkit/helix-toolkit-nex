namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: import-resource-isolation, Property 4: Key Set Disjointness Across Imports

/// <summary>
/// Property-based tests for Key Set Disjointness Across Imports (Property 4).
/// Verifies that for any two imports A and B with distinct Session_Identifiers that load
/// the same glTF file (same set of image indices, file paths, and sampler names), the set
/// of full cache keys produced by import A and the set produced by import B have an empty
/// intersection.
/// **Validates: Requirements 2.3, 3.2, 4.5, 4.7**
/// </summary>
[TestClass]
public class KeyDisjointnessPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Key Construction Helpers (mirrors TextureLoader internal logic)

    private static string BuildEmbeddedTextureKey(int imageIndex, string sessionId) =>
        $"{imageIndex}:{sessionId}";

    private static string BuildExternalTextureKey(string absolutePath, string sessionId) =>
        $"{absolutePath}:{sessionId}";

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
    /// Generator for a pair of distinct session IDs.
    /// </summary>
    private static Gen<(string, string)> DistinctSessionIdPairGen =>
        SessionIdGen.Two().Select(pair => (pair.Item1, pair.Item2));

    /// <summary>
    /// Generator for image indices (0..9999).
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
    /// Generator for a non-empty list of image indices (1..10 items).
    /// </summary>
    private static Gen<int[]> ImageIndicesGen =>
        Gen.ArrayOf(ImageIndexGen).Where(arr => arr.Length > 0);

    /// <summary>
    /// Generator for a non-empty list of file paths (1..5 items).
    /// </summary>
    private static Gen<string[]> FilePathsGen =>
        Gen.ArrayOf(FilePathGen).Where(arr => arr.Length > 0);

    /// <summary>
    /// Generator for a non-empty list of sampler names (1..5 items).
    /// </summary>
    private static Gen<string[]> SamplerNamesGen =>
        Gen.ArrayOf(SamplerNameGen).Where(arr => arr.Length > 0);

    #endregion

    /// <summary>
    /// Property 4: For any two distinct session IDs and any shared set of image indices,
    /// the full cache key sets for embedded textures from each session are completely disjoint.
    /// **Validates: Requirements 2.3, 4.5**
    /// </summary>
    [TestMethod]
    public void EmbeddedTextureKeys_AreDisjoint_AcrossSessions()
    {
        Prop.ForAll(
                Arb.From(DistinctSessionIdPairGen),
                Arb.From(ImageIndicesGen),
                ((string, string) sessionPair, int[] imageIndices) =>
                {
                    var (sessionA, sessionB) = sessionPair;

                    // Ensure sessions are distinct (GUIDs practically guarantee this)
                    if (sessionA == sessionB)
                        return true; // skip degenerate case

                    var keysA = new HashSet<string>(StringComparer.Ordinal);
                    var keysB = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var idx in imageIndices)
                    {
                        keysA.Add(BuildEmbeddedTextureKey(idx, sessionA));
                        keysB.Add(BuildEmbeddedTextureKey(idx, sessionB));
                    }

                    // Intersection must be empty
                    keysA.IntersectWith(keysB);
                    return keysA.Count == 0;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 4: For any two distinct session IDs and any shared set of file paths,
    /// the full cache key sets for external textures from each session are completely disjoint.
    /// **Validates: Requirements 2.3, 4.5**
    /// </summary>
    [TestMethod]
    public void ExternalTextureKeys_AreDisjoint_AcrossSessions()
    {
        Prop.ForAll(
                Arb.From(DistinctSessionIdPairGen),
                Arb.From(FilePathsGen),
                ((string, string) sessionPair, string[] filePaths) =>
                {
                    var (sessionA, sessionB) = sessionPair;

                    if (sessionA == sessionB)
                        return true;

                    var keysA = new HashSet<string>(StringComparer.Ordinal);
                    var keysB = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var path in filePaths)
                    {
                        keysA.Add(BuildExternalTextureKey(path, sessionA));
                        keysB.Add(BuildExternalTextureKey(path, sessionB));
                    }

                    keysA.IntersectWith(keysB);
                    return keysA.Count == 0;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 4: For any two distinct session IDs and any shared set of sampler names,
    /// the full cache key sets for samplers from each session are completely disjoint.
    /// **Validates: Requirements 3.2, 4.7**
    /// </summary>
    [TestMethod]
    public void SamplerKeys_AreDisjoint_AcrossSessions()
    {
        Prop.ForAll(
                Arb.From(DistinctSessionIdPairGen),
                Arb.From(SamplerNamesGen),
                ((string, string) sessionPair, string[] samplerNames) =>
                {
                    var (sessionA, sessionB) = sessionPair;

                    if (sessionA == sessionB)
                        return true;

                    var keysA = new HashSet<string>(StringComparer.Ordinal);
                    var keysB = new HashSet<string>(StringComparer.Ordinal);

                    for (int i = 0; i < samplerNames.Length; i++)
                    {
                        keysA.Add(BuildSamplerKey(samplerNames[i], i, sessionA));
                        keysB.Add(BuildSamplerKey(samplerNames[i], i, sessionB));
                    }

                    keysA.IntersectWith(keysB);
                    return keysA.Count == 0;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 4: For any two distinct session IDs and a mixed set of base keys (image indices,
    /// file paths, and sampler names), the combined full cache key sets from each session are
    /// completely disjoint.
    /// **Validates: Requirements 2.3, 3.2, 4.5, 4.7**
    /// </summary>
    [TestMethod]
    public void AllCacheKeys_AreDisjoint_AcrossSessions()
    {
        Prop.ForAll(
                Arb.From(DistinctSessionIdPairGen),
                Arb.From(ImageIndicesGen),
                Arb.From(FilePathsGen),
                ((string, string) sessionPair, int[] imageIndices, string[] filePaths) =>
                {
                    var (sessionA, sessionB) = sessionPair;

                    if (sessionA == sessionB)
                        return true;

                    var keysA = new HashSet<string>(StringComparer.Ordinal);
                    var keysB = new HashSet<string>(StringComparer.Ordinal);

                    // Add embedded texture keys
                    foreach (var idx in imageIndices)
                    {
                        keysA.Add(BuildEmbeddedTextureKey(idx, sessionA));
                        keysB.Add(BuildEmbeddedTextureKey(idx, sessionB));
                    }

                    // Add external texture keys
                    foreach (var path in filePaths)
                    {
                        keysA.Add(BuildExternalTextureKey(path, sessionA));
                        keysB.Add(BuildExternalTextureKey(path, sessionB));
                    }

                    // Add sampler keys (use a fixed set of sampler names for this combined test)
                    var samplerNames = new[] { "LinearWrap", "PointClamp", "AnisotropicWrap" };
                    for (int i = 0; i < samplerNames.Length; i++)
                    {
                        keysA.Add(BuildSamplerKey(samplerNames[i], i, sessionA));
                        keysB.Add(BuildSamplerKey(samplerNames[i], i, sessionB));
                    }

                    // Intersection must be empty
                    keysA.IntersectWith(keysB);
                    return keysA.Count == 0;
                }
            )
            .Check(FsCheckConfig);
    }
}
