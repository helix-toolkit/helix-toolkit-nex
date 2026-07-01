using Newtonsoft.Json.Linq;
using Gltf = glTFLoader.Schema.Gltf;
using GltfNode = glTFLoader.Schema.Node;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Reason a <see cref="InstancingExtensionParser.TryParse"/> attempt failed. <see cref="None"/> is
/// used only on success.
/// </summary>
internal enum InstancingParseError
{
    /// <summary>Parsing succeeded; no error.</summary>
    None,

    /// <summary>The extension key value is null or not a JSON object (Requirement 1.3).</summary>
    ExtensionValueNotObject,

    /// <summary>The <c>attributes</c> property is present but not a JSON object (Requirement 2.3).</summary>
    AttributesNotObject,

    /// <summary>
    /// A recognized attribute value is not a non-negative integer accessor index within the model's
    /// accessor range (Requirement 2.4).
    /// </summary>
    InvalidAttributeAccessor,

    /// <summary>
    /// Two or more present, valid attribute accessors report different element counts
    /// (Requirement 3.2).
    /// </summary>
    ConflictingInstanceCounts,
}

/// <summary>
/// Validated <c>EXT_mesh_gpu_instancing</c> data for a single node: the recognized attribute
/// accessor indices (absent = <see langword="null"/>), the resolved instance count, and the list of
/// ignored (unrecognized) attribute keys.
/// </summary>
/// <param name="Translation">Accessor index for the <c>TRANSLATION</c> attribute, or <see langword="null"/> when absent.</param>
/// <param name="Rotation">Accessor index for the <c>ROTATION</c> attribute, or <see langword="null"/> when absent.</param>
/// <param name="Scale">Accessor index for the <c>SCALE</c> attribute, or <see langword="null"/> when absent.</param>
/// <param name="InstanceCount">The resolved number of instances (shared element count of present valid accessors).</param>
/// <param name="IgnoredKeys">The <c>attributes</c> keys that did not match a recognized attribute name.</param>
internal sealed record InstancingExtensionData(
    int? Translation,
    int? Rotation,
    int? Scale,
    int InstanceCount,
    IReadOnlyList<string> IgnoredKeys
);

/// <summary>
/// Pure parsing/validation layer for the <c>EXT_mesh_gpu_instancing</c> glTF 2.0 extension on a
/// node. Detects the extension object on a node's <c>extensions</c> map and validates the
/// <c>attributes</c> accessor references, mirroring the convention established by
/// <see cref="Draco.DracoExtensionData"/>.
/// </summary>
internal static class InstancingExtensionParser
{
    /// <summary>The glTF extension name this parser handles.</summary>
    public const string ExtensionName = "EXT_mesh_gpu_instancing";

    /// <summary>
    /// Determines whether the node's <c>extensions</c> map declares the
    /// <c>EXT_mesh_gpu_instancing</c> key, using a case-sensitive
    /// (<see cref="StringComparison.Ordinal"/>) match, regardless of whether the value is a valid
    /// JSON object. Used by the disabled-instancing path to decide whether the required-extension
    /// warning applies (Requirement 10.4).
    /// </summary>
    /// <param name="node">The source glTF node.</param>
    /// <returns>
    /// <see langword="true"/> when the node declares the extension key; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool NodeDeclaresExtension(GltfNode node)
    {
        if (node.Extensions is null)
        {
            return false;
        }

        return node
            .Extensions.Where(pair =>
                string.Equals(pair.Key, ExtensionName, StringComparison.Ordinal)
            )
            .Any();
    }

    /// <summary>
    /// Detects whether the node declares a non-null <c>EXT_mesh_gpu_instancing</c> object. Uses a
    /// case-sensitive (<see cref="StringComparison.Ordinal"/>) key match. Returns
    /// <see langword="false"/> when the extensions map is absent, the key is absent, or the value is
    /// not a <see cref="JObject"/>. When the key is present but the value is not a
    /// <see cref="JObject"/> (null, string, number, boolean, or array), <paramref name="keyPresentButInvalid"/>
    /// is set to <see langword="true"/> (Requirement 1.3).
    /// </summary>
    /// <param name="node">The source glTF node.</param>
    /// <param name="extensionObj">The extension object on success; <see langword="null"/> otherwise.</param>
    /// <param name="keyPresentButInvalid">
    /// <see langword="true"/> when the extension key is present but its value is not a JSON object.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a non-null extension object was found; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryGetExtensionObject(
        GltfNode node,
        out JObject? extensionObj,
        out bool keyPresentButInvalid
    )
    {
        extensionObj = null;
        keyPresentButInvalid = false;

        // Requirement 1.1: absent extensions map → non-instanced node, no diagnostic.
        if (node.Extensions is null)
        {
            return false;
        }

        // Requirement 1.1: case-sensitive (Ordinal) key match. The key must be present exactly.
        object? rawValue = null;
        var keyPresent = false;
        foreach (var pair in node.Extensions)
        {
            if (string.Equals(pair.Key, ExtensionName, StringComparison.Ordinal))
            {
                rawValue = pair.Value;
                keyPresent = true;
                break;
            }
        }

        if (!keyPresent)
        {
            return false;
        }

        // Requirement 1.2 / 1.3: the value must be a non-null JSON object.
        if (rawValue is JObject obj)
        {
            extensionObj = obj;
            return true;
        }

        keyPresentButInvalid = true;
        return false;
    }

    /// <summary>
    /// Parses and validates the extension object: resolves <c>TRANSLATION</c>/<c>ROTATION</c>/<c>SCALE</c>
    /// accessor indices, validates them against the model's accessor count, collects ignored keys,
    /// and resolves the instance count. Returns <see langword="false"/> (with a reason and an
    /// offending-detail payload) when the structure is malformed per Requirements 2.3, 2.4, and 3.2.
    /// </summary>
    /// <remarks>
    /// Full resolution logic is implemented in task 2.2.
    /// </remarks>
    public static bool TryParse(
        Gltf model,
        JObject extensionObj,
        out InstancingExtensionData? data,
        out InstancingParseError error,
        out string? offendingDetail
    )
    {
        data = null;
        error = InstancingParseError.None;
        offendingDetail = null;

        // Requirement 2.2: an absent `attributes` property → zero recognized attributes →
        // zero-instance behavior (success with all indices null, InstanceCount 0).
        var attributesToken = extensionObj["attributes"];
        if (attributesToken is null || attributesToken.Type == JTokenType.Null)
        {
            data = new InstancingExtensionData(
                Translation: null,
                Rotation: null,
                Scale: null,
                InstanceCount: 0,
                IgnoredKeys: Array.Empty<string>()
            );
            return true;
        }

        // Requirement 2.3: `attributes` present but not a JSON object → Error, skip instancing.
        if (attributesToken is not JObject attributes)
        {
            error = InstancingParseError.AttributesNotObject;
            offendingDetail = "attributes";
            return false;
        }

        // Requirement 2.5: collect every key not matching a recognized attribute name.
        var ignoredKeys = new List<string>();
        foreach (var pair in attributes)
        {
            if (
                !string.Equals(pair.Key, AttributeTranslation, StringComparison.Ordinal)
                && !string.Equals(pair.Key, AttributeRotation, StringComparison.Ordinal)
                && !string.Equals(pair.Key, AttributeScale, StringComparison.Ordinal)
            )
            {
                ignoredKeys.Add(pair.Key);
            }
        }

        int accessorCount = model.Accessors?.Length ?? 0;

        // Requirement 2.1 / 2.4: resolve and validate each present recognized attribute. Returns at
        // the first invalid attribute so exactly one diagnostic is produced for the node.
        if (
            !TryResolveAttribute(
                attributes,
                AttributeTranslation,
                accessorCount,
                out int? translation,
                out offendingDetail
            )
        )
        {
            error = InstancingParseError.InvalidAttributeAccessor;
            return false;
        }

        if (
            !TryResolveAttribute(
                attributes,
                AttributeRotation,
                accessorCount,
                out int? rotation,
                out offendingDetail
            )
        )
        {
            error = InstancingParseError.InvalidAttributeAccessor;
            return false;
        }

        if (
            !TryResolveAttribute(
                attributes,
                AttributeScale,
                accessorCount,
                out int? scale,
                out offendingDetail
            )
        )
        {
            error = InstancingParseError.InvalidAttributeAccessor;
            return false;
        }

        // Requirement 2.2: no recognized attribute present → zero-instance behavior.
        if (translation is null && rotation is null && scale is null)
        {
            data = new InstancingExtensionData(
                Translation: null,
                Rotation: null,
                Scale: null,
                InstanceCount: 0,
                IgnoredKeys: ignoredKeys
            );
            return true;
        }

        // Requirements 3.1, 3.2, 3.3: resolve the instance count from present valid accessors.
        int? resolvedCount = null;
        var present = new (string Name, int Index)[3];
        int presentCount = 0;
        if (translation is int t)
        {
            present[presentCount++] = (AttributeTranslation, t);
        }
        if (rotation is int r)
        {
            present[presentCount++] = (AttributeRotation, r);
        }
        if (scale is int s)
        {
            present[presentCount++] = (AttributeScale, s);
        }

        for (int i = 0; i < presentCount; i++)
        {
            int count = model.Accessors![present[i].Index].Count;
            if (resolvedCount is null)
            {
                resolvedCount = count;
            }
            else if (resolvedCount.Value != count)
            {
                // Requirement 3.2: conflicting element counts → Error listing each accessor + count.
                error = InstancingParseError.ConflictingInstanceCounts;
                offendingDetail = string.Join(
                    ", ",
                    Enumerable
                        .Range(0, presentCount)
                        .Select(j =>
                            $"{present[j].Name} (accessor {present[j].Index}) = {model.Accessors![present[j].Index].Count}"
                        )
                );
                return false;
            }
        }

        data = new InstancingExtensionData(
            Translation: translation,
            Rotation: rotation,
            Scale: scale,
            InstanceCount: resolvedCount ?? 0,
            IgnoredKeys: ignoredKeys
        );
        return true;
    }

    /// <summary>
    /// Resolves a single recognized attribute key. When the key is absent, <paramref name="index"/>
    /// is <see langword="null"/> and the method returns <see langword="true"/>. When the key is
    /// present, its value must be a non-negative integer JSON token strictly less than
    /// <paramref name="accessorCount"/> (Requirement 2.4); otherwise the method returns
    /// <see langword="false"/> with <paramref name="offendingDetail"/> set to the attribute name.
    /// </summary>
    private static bool TryResolveAttribute(
        JObject attributes,
        string attributeName,
        int accessorCount,
        out int? index,
        out string? offendingDetail
    )
    {
        index = null;
        offendingDetail = null;

        var token = attributes[attributeName];
        if (token is null)
        {
            // Attribute absent → nothing to validate.
            return true;
        }

        // Requirement 2.4: only an integer token within [0, accessorCount) is valid. Reject null,
        // fractional (Float), string, boolean, etc.
        if (token.Type != JTokenType.Integer)
        {
            offendingDetail = attributeName;
            return false;
        }

        long value = token.Value<long>();
        if (value < 0 || value >= accessorCount)
        {
            offendingDetail = attributeName;
            return false;
        }

        index = (int)value;
        return true;
    }

    /// <summary>Recognized per-instance attribute key for translation (VEC3 FLOAT).</summary>
    private const string AttributeTranslation = "TRANSLATION";

    /// <summary>Recognized per-instance attribute key for rotation (VEC4 quaternion).</summary>
    private const string AttributeRotation = "ROTATION";

    /// <summary>Recognized per-instance attribute key for scale (VEC3 FLOAT).</summary>
    private const string AttributeScale = "SCALE";
}
