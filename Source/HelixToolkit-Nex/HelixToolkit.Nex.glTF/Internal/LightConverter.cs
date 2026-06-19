using System.Numerics;
using glTFLoader.Schema;
using Newtonsoft.Json.Linq;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Pure parsing/validation layer for the <c>KHR_lights_punctual</c> glTF 2.0 extension.
/// Converts the document-level <c>extensions.KHR_lights_punctual.lights</c> array into an
/// engine-agnostic list of <see cref="ParsedLight"/> values and emits
/// <see cref="ImportDiagnostic"/> entries for malformed-but-recoverable data.
/// </summary>
/// <remarks>
/// This component has no dependency on the ECS world or scene <c>Node</c>; it only produces
/// data. That isolation is what makes the conversion logic property-testable.
/// </remarks>
internal sealed class LightConverter
{
    /// <summary>
    /// The document-level extension key for punctual lights: <c>"KHR_lights_punctual"</c>.
    /// </summary>
    public const string ExtensionName = "KHR_lights_punctual";

    /// <summary>
    /// The name of the lights array property within the extension object.
    /// </summary>
    private const string LightsPropertyName = "lights";

    /// <summary>
    /// The property name for a light's type discriminator.
    /// </summary>
    private const string TypePropertyName = "type";

    /// <summary>
    /// The property name for a light's color array.
    /// </summary>
    private const string ColorPropertyName = "color";

    /// <summary>
    /// The property name for a light's intensity scalar.
    /// </summary>
    private const string IntensityPropertyName = "intensity";

    /// <summary>
    /// The property name for a point/spot light's range scalar.
    /// </summary>
    private const string RangePropertyName = "range";

    /// <summary>
    /// The property name for a spot light's nested cone-angle object.
    /// </summary>
    private const string SpotPropertyName = "spot";

    /// <summary>
    /// The property name for a spot light's inner cone angle (within the <c>spot</c> object).
    /// </summary>
    private const string InnerConeAnglePropertyName = "innerConeAngle";

    /// <summary>
    /// The property name for a spot light's outer cone angle (within the <c>spot</c> object).
    /// </summary>
    private const string OuterConeAnglePropertyName = "outerConeAngle";

    /// <summary>
    /// The case-sensitive <c>type</c> string for a directional light.
    /// </summary>
    private const string DirectionalTypeValue = "directional";

    /// <summary>
    /// The case-sensitive <c>type</c> string for a point light.
    /// </summary>
    private const string PointTypeValue = "point";

    /// <summary>
    /// The case-sensitive <c>type</c> string for a spot light.
    /// </summary>
    private const string SpotTypeValue = "spot";

    /// <summary>
    /// The number of channels required for a valid <c>color</c> array.
    /// </summary>
    private const int ColorChannelCount = 3;

    private readonly List<ImportDiagnostic> _diagnostics;

    private readonly ImporterConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="LightConverter"/> class.
    /// </summary>
    /// <param name="diagnostics">The diagnostics list to append warnings/errors to.</param>
    /// <param name="config">The importer configuration.</param>
    public LightConverter(List<ImportDiagnostic> diagnostics, ImporterConfig config)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Parses the document-level <c>KHR_lights_punctual</c> lights array.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <returns>
    /// A list parallel to the glTF lights array; a <c>null</c> entry means the definition at
    /// that index was not convertible (e.g., unknown type) and must not be attached. Returns an
    /// empty list when the extension is absent, the lights array is empty/absent, or the lights
    /// value is malformed (a diagnostic is added in the malformed case).
    /// </returns>
    public IReadOnlyList<ParsedLight?> ParseLights(Gltf model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Requirement 1.2: extension absent under document-level extensions → no lights, no diagnostic.
        if (
            model.Extensions is null
            || !model.Extensions.TryGetValue(ExtensionName, out var extensionRaw)
            || extensionRaw is not JObject extensionObj
        )
        {
            return [];
        }

        // Requirement 1.3: lights property absent (or explicit null) → no lights, no diagnostic.
        if (
            !extensionObj.TryGetValue(LightsPropertyName, out var lightsToken)
            || lightsToken is null
            || lightsToken.Type == JTokenType.Null
        )
        {
            return [];
        }

        // Requirement 1.4: lights present but not a valid array → no lights, retain non-light data,
        // add a malformed-data diagnostic.
        if (lightsToken is not JArray lightsArray)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"KHR_lights_punctual '{LightsPropertyName}' is present but is not a valid array. No lights were imported.",
                    "Light",
                    -1
                )
            );
            return [];
        }

        // Requirement 1.3: empty lights array → no lights, no diagnostic.
        if (lightsArray.Count == 0)
        {
            return [];
        }

        // Requirement 1.1: parse each entry of the lights array, producing one slot per entry so the
        // returned list is positionally parallel to the input array.
        var result = new ParsedLight?[lightsArray.Count];
        for (int i = 0; i < lightsArray.Count; i++)
        {
            result[i] = ParseLight(lightsArray[i], i);
        }

        return result;
    }

    /// <summary>
    /// Parses a single light definition into a <see cref="ParsedLight"/>, or <c>null</c> when the
    /// definition is not convertible.
    /// </summary>
    /// <param name="lightToken">The JSON token for a single entry of the lights array.</param>
    /// <param name="index">The index of the light definition within the lights array.</param>
    /// <returns>The parsed light, or <c>null</c> when the definition cannot be converted.</returns>
    private ParsedLight? ParseLight(JToken lightToken, int index)
    {
        // A light definition must be a JSON object to carry the required `type` discriminator
        // and any optional fields. Anything else is treated as an unconvertible definition.
        if (lightToken is not JObject lightObj)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"KHR_lights_punctual light definition at index {index} is not a JSON object and was skipped.",
                    "Light",
                    index
                )
            );
            return null;
        }

        // Requirement 2.1 / 3.1 / 4.1 / 6.2: map the case-sensitive `type` discriminator to a
        // LightKind. An unknown (or missing/non-string) type yields a Warning diagnostic and a
        // null slot so the definition is not attached.
        if (!TryParseKind(lightObj, index, out var kind))
        {
            return null;
        }

        // Requirements 2.2-2.4 / 3.2-3.3 / 6.4: parse the color with defaulting/validation.
        var color = ParseColor(lightObj, index);

        // Requirements 2.5-2.7 / 3.4-3.5 / 6.5: parse intensity with defaulting/validation.
        var intensity = ParseIntensity(lightObj, index);

        // Requirements 3.6-3.7 / 6.6: parse range (point/spot only) with defaulting/validation.
        // Directional lights have no range; they return 0 and no range diagnostic is emitted for
        // them.
        var range = ParseRange(lightObj, index, kind);

        // Spot cone angles (Requirements 4.2-4.8 / 6.3). Cone angles apply only to spot lights;
        // for directional and point lights the model defaults are used and ignored downstream.
        var innerConeAngle = ParsedLight.DefaultInnerConeAngle;
        var outerConeAngle = ParsedLight.DefaultOuterConeAngle;
        if (kind == LightKind.Spot)
        {
            (innerConeAngle, outerConeAngle) = ParseSpotAngles(lightObj, index);
        }

        return new ParsedLight(kind, color, intensity, range, innerConeAngle, outerConeAngle);
    }

    /// <summary>
    /// Reads the case-sensitive <c>type</c> discriminator and maps it to a <see cref="LightKind"/>.
    /// </summary>
    /// <param name="lightObj">The light definition object.</param>
    /// <param name="index">The light definition index, used in diagnostics.</param>
    /// <param name="kind">The mapped light kind when the type is recognized.</param>
    /// <returns>
    /// <c>true</c> when the <c>type</c> is exactly <c>"directional"</c>, <c>"point"</c>, or
    /// <c>"spot"</c>; otherwise <c>false</c> (a Warning diagnostic is added per Requirement 6.2).
    /// </returns>
    private bool TryParseKind(JObject lightObj, int index, out LightKind kind)
    {
        var typeToken = lightObj[TypePropertyName];
        var typeValue = typeToken is { Type: JTokenType.String } ? typeToken.Value<string>() : null;

        switch (typeValue)
        {
            case DirectionalTypeValue:
                kind = LightKind.Directional;
                return true;
            case PointTypeValue:
                kind = LightKind.Point;
                return true;
            case SpotTypeValue:
                kind = LightKind.Spot;
                return true;
            default:
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"KHR_lights_punctual light at index {index} has an unrecognized type '{typeValue ?? "<missing>"}' and was skipped.",
                        "Light",
                        index
                    )
                );
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Parses the optional <c>color</c> array with defaulting and validation.
    /// </summary>
    /// <param name="lightObj">The light definition object.</param>
    /// <param name="index">The light definition index, used in diagnostics.</param>
    /// <returns>
    /// The parsed linear RGB color when <c>color</c> is exactly three numeric values each in
    /// <c>[0, 1]</c>; <see cref="ParsedLight.DefaultColor"/> when <c>color</c> is omitted (no
    /// diagnostic); or <see cref="ParsedLight.DefaultColor"/> with a Warning diagnostic when the
    /// array has the wrong length or contains an out-of-range/non-numeric value.
    /// </returns>
    private Vector3 ParseColor(JObject lightObj, int index)
    {
        var colorToken = lightObj[ColorPropertyName];

        // Requirements 2.3 / 3.3: omitted (or explicit null) color defaults to (1,1,1) silently.
        if (colorToken is null || colorToken.Type == JTokenType.Null)
        {
            return ParsedLight.DefaultColor;
        }

        // Requirements 2.4 / 6.4: a color that is not an array of exactly three values is invalid.
        if (colorToken is not JArray colorArray || colorArray.Count != ColorChannelCount)
        {
            AddInvalidColorDiagnostic(index);
            return ParsedLight.DefaultColor;
        }

        Span<float> channels = stackalloc float[ColorChannelCount];
        for (int c = 0; c < ColorChannelCount; c++)
        {
            var channelToken = colorArray[c];

            // Requirement 2.4 / 6.4: each channel must be a numeric value within [0, 1].
            if (!IsNumeric(channelToken))
            {
                AddInvalidColorDiagnostic(index);
                return ParsedLight.DefaultColor;
            }

            var value = channelToken.Value<float>();
            if (float.IsNaN(value) || value < 0.0f || value > 1.0f)
            {
                AddInvalidColorDiagnostic(index);
                return ParsedLight.DefaultColor;
            }

            channels[c] = value;
        }

        return new Vector3(channels[0], channels[1], channels[2]);
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a malformed <c>color</c> value.
    /// </summary>
    private void AddInvalidColorDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has an invalid 'color'; expected three numeric values each in [0, 1]. Using default color (1, 1, 1).",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Parses the optional <c>intensity</c> scalar with defaulting and validation.
    /// </summary>
    /// <param name="lightObj">The light definition object.</param>
    /// <param name="index">The light definition index, used in diagnostics.</param>
    /// <returns>
    /// The parsed intensity when <c>intensity</c> is numeric and <c>&gt;= 0</c>;
    /// <see cref="ParsedLight.DefaultIntensity"/> when <c>intensity</c> is omitted (no
    /// diagnostic); or <see cref="ParsedLight.DefaultIntensity"/> with a Warning diagnostic when
    /// the value is non-numeric or negative.
    /// </returns>
    private float ParseIntensity(JObject lightObj, int index)
    {
        var intensityToken = lightObj[IntensityPropertyName];

        // Requirements 2.6 / 3.5: omitted (or explicit null) intensity defaults to 1.0 silently.
        if (intensityToken is null || intensityToken.Type == JTokenType.Null)
        {
            return ParsedLight.DefaultIntensity;
        }

        // Requirements 2.7 / 6.5: a non-numeric intensity is invalid → diagnostic + default.
        if (!IsNumeric(intensityToken))
        {
            AddInvalidIntensityDiagnostic(index);
            return ParsedLight.DefaultIntensity;
        }

        var value = intensityToken.Value<float>();

        // Requirements 2.7 / 6.5: NaN or negative intensity is invalid → diagnostic + default.
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.0f)
        {
            AddInvalidIntensityDiagnostic(index);
            return ParsedLight.DefaultIntensity;
        }

        // Requirements 2.5 / 3.4: numeric and >= 0 → use the value.
        return value;
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a malformed <c>intensity</c> value.
    /// </summary>
    private void AddInvalidIntensityDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has an invalid 'intensity'; expected a numeric value >= 0. Using default intensity 1.0.",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Parses the optional <c>range</c> scalar with defaulting and validation. Range applies only to point and spot lights; directional lights ignore it.
    /// </summary>
    /// <param name="lightObj">The light definition object.</param>
    /// <param name="index">The light definition index, used in diagnostics.</param>
    /// <param name="kind">The light kind; range applies only to point and spot lights.</param>
    /// <returns>
    /// The parsed range when <c>range</c> is numeric and <c>&gt; 0</c>; the configured finite
    /// default (<c>DefaultPointLightRange</c> for point lights, <c>DefaultSpotLightRange</c> for
    /// spot lights) when <c>range</c> is omitted (no diagnostic); or that same configured finite
    /// default with a Warning diagnostic when the value is <c>&lt;= 0</c> or non-numeric.
    /// Directional lights always return <c>0</c> without parsing or emitting a diagnostic, since
    /// range does not apply to them.
    /// </returns>
    private float ParseRange(JObject lightObj, int index, LightKind kind)
    {
        // Range applies only to point and spot lights; directional lights ignore it and never
        // emit a range diagnostic.
        if (kind == LightKind.Directional)
        {
            return 0;
        }

        var rangeToken = lightObj[RangePropertyName];

        // Requirement 3.7: omitted (or explicit null) range defaults to the configured finite
        // default for the light kind silently.
        if (rangeToken is null || rangeToken.Type == JTokenType.Null)
        {
            return kind == LightKind.Point
                ? _config.DefaultPointLightRange
                : _config.DefaultSpotLightRange;
        }

        // Requirement 6.6: a non-numeric or non-positive range is invalid → diagnostic + configured default.
        if (!IsNumeric(rangeToken))
        {
            AddInvalidRangeDiagnostic(index);
            return kind == LightKind.Point
                ? _config.DefaultPointLightRange
                : _config.DefaultSpotLightRange;
        }

        var value = rangeToken.Value<float>();

        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0.0f)
        {
            AddInvalidRangeDiagnostic(index);
            return kind == LightKind.Point
                ? _config.DefaultPointLightRange
                : _config.DefaultSpotLightRange;
        }

        // Requirement 3.6: numeric and > 0 → use the value.
        return value;
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a malformed <c>range</c> value.
    /// </summary>
    private void AddInvalidRangeDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has an invalid 'range'; expected a numeric value > 0. Using the configured default range.",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Parses the optional nested <c>spot</c> cone-angle object (spot lights only) with
    /// defaulting and validation.
    /// </summary>
    /// <param name="lightObj">The light definition object.</param>
    /// <param name="index">The light definition index, used in diagnostics.</param>
    /// <returns>
    /// A tuple of the inner and outer cone angles in radians. A missing <c>spot</c> object yields
    /// the defaults <c>(0.0, PI/4)</c> with no diagnostic (Requirement 6.3). Each angle is parsed
    /// independently: an omitted angle defaults silently (inner → <c>0.0</c>, outer → <c>PI/4</c>),
    /// and an invalid angle defaults with a Warning diagnostic (Requirements 4.7, 4.8).
    /// </returns>
    private (float Inner, float Outer) ParseSpotAngles(JObject lightObj, int index)
    {
        var spotToken = lightObj[SpotPropertyName];

        // Requirement 6.3: a spot light missing the entire `spot` object uses the default cone
        // angles (0.0, PI/4) without adding a diagnostic.
        if (
            spotToken is null
            || spotToken.Type == JTokenType.Null
            || spotToken is not JObject spotObj
        )
        {
            return (ParsedLight.DefaultInnerConeAngle, ParsedLight.DefaultOuterConeAngle);
        }

        var innerToken = spotObj[InnerConeAnglePropertyName];
        var outerToken = spotObj[OuterConeAnglePropertyName];

        var innerPresent = innerToken is not null && innerToken.Type != JTokenType.Null;
        var outerPresent = outerToken is not null && outerToken.Type != JTokenType.Null;

        // A present angle is usable only when it is a finite numeric value.
        float? innerValue =
            innerPresent && IsNumeric(innerToken) && !float.IsNaN(innerToken!.Value<float>())
                ? innerToken!.Value<float>()
                : null;
        float? outerValue =
            outerPresent && IsNumeric(outerToken) && !float.IsNaN(outerToken!.Value<float>())
                ? outerToken!.Value<float>()
                : null;

        // The validity of each angle depends on the other. Compare against the provided value of
        // the counterpart where available, otherwise its effective default.
        var comparisonOuter = outerValue ?? ParsedLight.DefaultOuterConeAngle;
        var comparisonInner = innerValue ?? ParsedLight.DefaultInnerConeAngle;

        // Inner cone angle (Requirements 4.2, 4.3, 4.7).
        float inner;
        if (!innerPresent)
        {
            // Requirement 4.3: omitted inner cone angle defaults to 0.0 with no diagnostic.
            inner = ParsedLight.DefaultInnerConeAngle;
        }
        else if (innerValue is { } iv && iv >= 0.0f && iv < comparisonOuter)
        {
            // Requirement 4.2: >= 0 and < outer cone angle → use the value (radians).
            inner = iv;
        }
        else
        {
            // Requirement 4.7: negative or not less than the outer cone angle → diagnostic + 0.0.
            AddInvalidInnerConeAngleDiagnostic(index);
            inner = ParsedLight.DefaultInnerConeAngle;
        }

        // Outer cone angle (Requirements 4.4, 4.5, 4.8).
        float outer;
        if (!outerPresent)
        {
            // Requirement 4.5: omitted outer cone angle defaults to PI/4 with no diagnostic.
            outer = ParsedLight.DefaultOuterConeAngle;
        }
        else if (
            outerValue is { } ov
            && ov > comparisonInner
            && ov <= ParsedLight.MaxOuterConeAngle
        )
        {
            // Requirement 4.4: > inner cone angle and <= PI/2 → use the value (radians).
            outer = ov;
        }
        else
        {
            // Requirement 4.8: not greater than the inner cone angle or > PI/2 → diagnostic + PI/4.
            AddInvalidOuterConeAngleDiagnostic(index);
            outer = ParsedLight.DefaultOuterConeAngle;
        }

        // Requirement 2.7: the independent inner/outer validation above compares each angle
        // against the counterpart's value-or-default, so a provided angle accepted against an
        // invalid counterpart (later reset to its small default) can leave the resulting pair with
        // inner >= outer. Re-validate the resolved pair and fall back to the documented defaults
        // (0.0, PI/4) when the invariant is violated so the returned (inner, outer) always
        // satisfies inner < outer. Valid pairs with inner < outer are preserved verbatim.
        if (inner >= outer)
        {
            AddInvalidConeAnglePairDiagnostic(index);
            inner = ParsedLight.DefaultInnerConeAngle;
            outer = ParsedLight.DefaultOuterConeAngle;
        }

        return (inner, outer);
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a malformed <c>spot.innerConeAngle</c> value.
    /// </summary>
    private void AddInvalidInnerConeAngleDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has an invalid 'spot.innerConeAngle'; expected a numeric value >= 0 and < outerConeAngle. Using default inner cone angle 0.0.",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a malformed <c>spot.outerConeAngle</c> value.
    /// </summary>
    private void AddInvalidOuterConeAngleDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has an invalid 'spot.outerConeAngle'; expected a numeric value > innerConeAngle and <= PI/2. Using default outer cone angle PI/4.",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Adds the standard Warning diagnostic for a resolved spot cone-angle pair that violates the
    /// <c>inner &lt; outer</c> invariant after independent validation, indicating the documented
    /// defaults <c>(0.0, PI/4)</c> were applied.
    /// </summary>
    private void AddInvalidConeAnglePairDiagnostic(int index)
    {
        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"KHR_lights_punctual light at index {index} has spot cone angles where innerConeAngle is not less than outerConeAngle. Using default cone angles (0.0, PI/4).",
                "Light",
                index
            )
        );
    }

    /// <summary>
    /// Determines whether a token is a JSON numeric value (integer or float).
    /// </summary>
    private static bool IsNumeric(JToken? token) =>
        token is { Type: JTokenType.Integer or JTokenType.Float };
}
