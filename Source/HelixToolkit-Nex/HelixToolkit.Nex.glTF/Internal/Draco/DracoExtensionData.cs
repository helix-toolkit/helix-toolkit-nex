using Newtonsoft.Json.Linq;

namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Reason a <see cref="DracoExtensionData.TryParse"/> attempt failed. <see cref="None"/> is used
/// only on success.
/// </summary>
internal enum DracoParseError
{
    /// <summary>Parsing succeeded; no error.</summary>
    None,

    /// <summary>The <c>bufferView</c> property is missing, not an integer, or negative (Requirement 1.4).</summary>
    MissingOrInvalidBufferView,

    /// <summary>The <c>attributes</c> map is absent or contains zero entries (Requirement 1.5).</summary>
    MissingOrEmptyAttributes,

    /// <summary>The <c>attributes</c> map does not include a <c>POSITION</c> semantic (Requirement 4.1).</summary>
    MissingPosition,
}

/// <summary>
/// Parsed and validated representation of a <c>KHR_draco_mesh_compression</c> extension object
/// attached to a glTF <c>MeshPrimitive</c>. Carries the compressed <c>bufferView</c> index and the
/// mapping from glTF attribute semantic (for example <c>POSITION</c>, <c>NORMAL</c>) to the Draco
/// attribute unique id used to extract that attribute from the decoded bitstream.
/// </summary>
/// <param name="BufferView">Index of the bufferView holding the Draco bitstream (>= 0).</param>
/// <param name="Attributes">Semantic name → Draco attribute unique id map (non-empty, includes POSITION).</param>
internal sealed record DracoExtensionData(
    int BufferView,
    IReadOnlyDictionary<string, int> Attributes
)
{
    /// <summary>The glTF extension name this data represents.</summary>
    public const string ExtensionName = "KHR_draco_mesh_compression";

    /// <summary>The required POSITION attribute semantic.</summary>
    private const string PositionSemantic = "POSITION";

    /// <summary>
    /// Parses and validates the extension JObject. Returns <see langword="false"/> (with a
    /// <see cref="DracoParseError"/> reason) when the <c>bufferView</c> is missing, non-integer, or
    /// negative; when the <c>attributes</c> map is absent or empty; or when <c>POSITION</c> is
    /// absent from the <c>attributes</c> map. Only attribute entries whose value is an integer are
    /// retained in <see cref="Attributes"/>.
    /// </summary>
    /// <param name="raw">The raw extension object from the primitive's extensions map.</param>
    /// <param name="data">The parsed data on success; <see langword="null"/> on failure.</param>
    /// <param name="error">The reason for failure; <see cref="DracoParseError.None"/> on success.</param>
    /// <returns><see langword="true"/> when parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(
        JObject raw,
        out DracoExtensionData? data,
        out DracoParseError error
    )
    {
        data = null;

        // Requirement 1.4: bufferView must be an integer >= 0.
        JToken? bufferViewToken = raw["bufferView"];
        if (bufferViewToken is not { Type: JTokenType.Integer })
        {
            error = DracoParseError.MissingOrInvalidBufferView;
            return false;
        }

        int bufferView = bufferViewToken.Value<int>();
        if (bufferView < 0)
        {
            error = DracoParseError.MissingOrInvalidBufferView;
            return false;
        }

        // Requirement 1.5: attributes map must be present and non-empty.
        if (raw["attributes"] is not JObject attributesObj)
        {
            error = DracoParseError.MissingOrEmptyAttributes;
            return false;
        }

        var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in attributesObj.Properties())
        {
            if (property.Value.Type == JTokenType.Integer)
            {
                attributes[property.Name] = property.Value.Value<int>();
            }
        }

        if (attributes.Count == 0)
        {
            error = DracoParseError.MissingOrEmptyAttributes;
            return false;
        }

        // Requirement 4.1: POSITION semantic must be present in the attributes map.
        if (!attributes.ContainsKey(PositionSemantic))
        {
            error = DracoParseError.MissingPosition;
            return false;
        }

        data = new DracoExtensionData(bufferView, attributes);
        error = DracoParseError.None;
        return true;
    }
}
