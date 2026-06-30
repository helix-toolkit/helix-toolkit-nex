using FsCheck;
using FsCheck.Fluent;
using Newtonsoft.Json.Linq;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// FsCheck generators producing <c>EXT_mesh_gpu_instancing</c> extension values as Newtonsoft
/// <see cref="JToken"/>/<see cref="JObject"/> instances for the parser properties (Properties 1–7).
/// Covers the full malformed-input space the parser must classify: well-formed attribute subsets,
/// non-object extension values, non-object <c>attributes</c>, invalid accessor-index token forms
/// (null / non-numeric / fractional / negative / out-of-range), and extra unrecognized keys.
/// </summary>
internal static class InstancingExtensionObjectGenerators
{
    /// <summary>The case-sensitive recognized attribute keys.</summary>
    public static readonly string[] RecognizedKeys =
    [
        InstancingModelBuilder.TranslationKey,
        InstancingModelBuilder.RotationKey,
        InstancingModelBuilder.ScaleKey,
    ];

    /// <summary>
    /// Generates a non-empty subset of the three recognized attribute keys (order-insensitive),
    /// e.g. {TRANSLATION}, {ROTATION, SCALE}, {TRANSLATION, ROTATION, SCALE}.
    /// </summary>
    public static Gen<string[]> RecognizedKeySubset() =>
        from includeTranslation in Gen.Elements(true, false)
        from includeRotation in Gen.Elements(true, false)
        from includeScale in Gen.Elements(true, false)
            // Guarantee at least one recognized key is present.
        let t = includeTranslation || (!includeRotation && !includeScale)
        select BuildSubset(t, includeRotation, includeScale);

    /// <summary>
    /// Generates a well-formed extension <see cref="JObject"/> with an <c>attributes</c> object whose
    /// recognized keys all point to valid in-range accessor indices in <c>[0, accessorCount)</c>.
    /// </summary>
    /// <param name="accessorCount">The number of accessors in the model (must be at least 1).</param>
    public static Gen<JObject> ValidExtensionObject(int accessorCount) =>
        from keys in RecognizedKeySubset()
        from indices in Gen.ArrayOf(Gen.Choose(0, Math.Max(1, accessorCount) - 1), keys.Length)
        select BuildExtensionObject(Zip(keys, indices));

    /// <summary>
    /// Generates an extension value that is null or a non-object JSON token (string, number, boolean,
    /// or array). Feeds the malformed-extension-value path (Property 2).
    /// </summary>
    public static Gen<JToken> NonObjectExtensionValue() =>
        Gen.OneOf(
            Gen.Constant<JToken>(JValue.CreateNull()),
            Gen.Choose(-1000, 1000).Select(i => (JToken)new JValue(i)),
            Gen.Elements("translation", "EXT", "").Select(s => (JToken)new JValue(s)),
            Gen.Elements(true, false).Select(b => (JToken)new JValue(b)),
            Gen.Choose(0, 5).Select(n => (JToken)new JArray(Enumerable.Range(0, n).ToArray()))
        );

    /// <summary>
    /// Generates an <c>attributes</c> value that is null or a non-object JSON token, wrapped in an
    /// otherwise well-formed extension object. Feeds the malformed-<c>attributes</c> path (Property 3).
    /// </summary>
    public static Gen<JObject> ExtensionObjectWithNonObjectAttributes() =>
        from attributes in NonObjectExtensionValue()
        select new JObject { ["attributes"] = attributes };

    /// <summary>
    /// Generates a token that is NOT a valid accessor index for <paramref name="accessorCount"/>
    /// accessors: null, a non-numeric string, a fractional number, a negative integer, or an integer
    /// greater than or equal to <paramref name="accessorCount"/>. Feeds Property 4.
    /// </summary>
    /// <param name="accessorCount">The number of accessors in the model.</param>
    public static Gen<JToken> InvalidAccessorIndexToken(int accessorCount) =>
        Gen.OneOf(
            Gen.Constant<JToken>(JValue.CreateNull()),
            Gen.Elements("0", "one", "TRANSLATION").Select(s => (JToken)new JValue(s)),
            Gen.Choose(1, 1000).Select(i => (JToken)new JValue(i + 0.5)),
            Gen.Choose(-1000, -1).Select(i => (JToken)new JValue(i)),
            Gen.Choose(accessorCount, accessorCount + 1000).Select(i => (JToken)new JValue(i))
        );

    /// <summary>
    /// Generates an extension object whose <c>attributes</c> contain one recognized key bound to an
    /// invalid accessor-index token (per <see cref="InvalidAccessorIndexToken"/>) along with the
    /// offending key name, feeding Property 4.
    /// </summary>
    /// <param name="accessorCount">The number of accessors in the model.</param>
    public static Gen<(JObject Extension, string OffendingKey)> ExtensionObjectWithInvalidAccessorIndex(
        int accessorCount
    ) =>
        from key in Gen.Elements(RecognizedKeys)
        from token in InvalidAccessorIndexToken(accessorCount)
        select (new JObject { ["attributes"] = new JObject { [key] = token } }, key);

    /// <summary>
    /// Generates a non-empty array of unrecognized attribute key names (never matching the three
    /// recognized keys, case-sensitive). Feeds the ignored-key path (Property 5).
    /// </summary>
    public static Gen<string[]> UnrecognizedKeys() =>
        from count in Gen.Choose(1, 4)
        from keys in Gen.ArrayOf(UnrecognizedKey(), count)
        select keys.Distinct().ToArray();

    /// <summary>
    /// Generates an extension object combining a valid recognized-attribute subset with one or more
    /// unrecognized keys bound to arbitrary accessor indices, feeding Property 5.
    /// </summary>
    /// <param name="accessorCount">The number of accessors in the model (must be at least 1).</param>
    public static Gen<(JObject Extension, string[] IgnoredKeys)> ExtensionObjectWithUnrecognizedKeys(
        int accessorCount
    ) =>
        from keys in RecognizedKeySubset()
        from indices in Gen.ArrayOf(Gen.Choose(0, Math.Max(1, accessorCount) - 1), keys.Length)
        from unrecognized in UnrecognizedKeys()
        from extraIndices in Gen.ArrayOf(Gen.Choose(0, Math.Max(1, accessorCount) - 1), unrecognized.Length)
        let unrecognizedMap = Zip(unrecognized, extraIndices)
        select (
            BuildWithUnrecognized(Zip(keys, indices), unrecognizedMap),
            unrecognizedMap.Keys.ToArray()
        );

    /// <summary>
    /// Builds a well-formed extension <see cref="JObject"/> from a recognized key → accessor-index map.
    /// </summary>
    /// <param name="attributes">The recognized key → accessor index pairs.</param>
    public static JObject BuildExtensionObject(IReadOnlyDictionary<string, int> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var attributesObj = new JObject();
        foreach (var pair in attributes)
        {
            attributesObj[pair.Key] = pair.Value;
        }

        return new JObject { ["attributes"] = attributesObj };
    }

    private static Gen<string> UnrecognizedKey() =>
        from baseName in Gen.Elements("WEIGHTS", "_CUSTOM", "translation", "Rotation", "scale", "COLOR_0")
        from suffix in Gen.Choose(0, 99)
        select RecognizedKeys.Contains(baseName) ? baseName + "_" + suffix : baseName;

    private static JObject BuildWithUnrecognized(
        IReadOnlyDictionary<string, int> recognized,
        IReadOnlyDictionary<string, int> unrecognized
    )
    {
        var attributesObj = new JObject();
        foreach (var pair in recognized)
        {
            attributesObj[pair.Key] = pair.Value;
        }

        foreach (var pair in unrecognized)
        {
            // Avoid clobbering a recognized key if a generator collision occurred.
            if (!attributesObj.ContainsKey(pair.Key))
            {
                attributesObj[pair.Key] = pair.Value;
            }
        }

        return new JObject { ["attributes"] = attributesObj };
    }

    private static string[] BuildSubset(bool t, bool r, bool s)
    {
        var keys = new List<string>(3);
        if (t)
        {
            keys.Add(InstancingModelBuilder.TranslationKey);
        }

        if (r)
        {
            keys.Add(InstancingModelBuilder.RotationKey);
        }

        if (s)
        {
            keys.Add(InstancingModelBuilder.ScaleKey);
        }

        return keys.ToArray();
    }

    private static Dictionary<string, int> Zip(string[] keys, int[] values)
    {
        var map = new Dictionary<string, int>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            map[keys[i]] = values[i];
        }

        return map;
    }
}
