using System.Globalization;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Configuration for SDF font material variants with optional outline and drop shadow effects.
/// <para>
/// Use this struct to define custom SDF font materials with outlines and/or shadows,
/// then register them via <see cref="RegisterVariant"/> to obtain a <see cref="MaterialTypeId"/>
/// for use with <c>BillboardComponent.BillboardMaterialId</c>.
/// </para>
/// </summary>
public struct SDFFontMaterialConfig
{
    /// <summary>
    /// Gets or sets the outline tint color.
    /// Defaults to opaque black (0, 0, 0, 1).
    /// </summary>
    public Color4 OutlineColor { get; set; } = new Color4(0f, 0f, 0f, 1f);

    /// <summary>
    /// Gets or sets the outline thickness in SDF distance units.
    /// A value of 0 means no outline is rendered.
    /// </summary>
    public float OutlineWidth { get; set; } = 0f;

    /// <summary>
    /// Gets or sets the drop shadow tint color.
    /// Defaults to semi-transparent black (0, 0, 0, 0.5).
    /// </summary>
    public Color4 ShadowColor { get; set; } = new Color4(0f, 0f, 0f, 0.5f);

    /// <summary>
    /// Gets or sets the drop shadow displacement in UV space.
    /// Defaults to zero (no shadow displacement).
    /// </summary>
    public Vector2 ShadowOffset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Gets or sets the drop shadow blur radius in SDF distance units.
    /// A value of 0 produces a hard shadow edge.
    /// </summary>
    public float ShadowSoftness { get; set; } = 0f;

    /// <summary>
    /// Gets or sets the edge threshold for the SDF.
    /// Default is 0.5, representing the glyph boundary in a normalized distance field.
    /// </summary>
    public float EdgeThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Initializes a new instance of the <see cref="SDFFontMaterialConfig"/> struct with default values.
    /// </summary>
    public SDFFontMaterialConfig() { }

    /// <summary>
    /// Generates the GLSL <c>outputColor()</c> implementation string based on this configuration.
    /// Uses MSDF median(r,g,b) for the distance field reconstruction.
    /// </summary>
    public static string GenerateGlsl(SDFFontMaterialConfig config)
    {
        bool hasOutline = config.OutlineWidth > 0f;
        bool hasShadow = config.ShadowOffset != Vector2.Zero;

        if (hasOutline && hasShadow)
            return GenerateShadowOutlineFillGlsl(config);
        else if (hasOutline)
            return GenerateOutlineFillGlsl(config);
        else if (hasShadow)
            return GenerateShadowFillGlsl(config);
        else
            return GenerateBasicFillGlsl(config);
    }

    /// <summary>
    /// Registers a custom SDF font material variant with the <see cref="BillboardMaterialRegistry"/>.
    /// </summary>
    public static MaterialTypeId RegisterVariant(string name, SDFFontMaterialConfig config)
    {
        string glsl = GenerateGlsl(config);
        return BillboardMaterialRegistry.Register(name, glsl);
    }

    // MSDF median helper — inlined in each variant to avoid cross-function dependencies
    private const string MsdfMedian = """
                vec3 _s = textureBindless2D(getTextureId(), getSamplerId(), getUV()).rgb;
                float dist = max(min(_s.r, _s.g), min(max(_s.r, _s.g), _s.b));
        """;

    private static string MsdfMedianAt(string uvExpr) =>
        $"""
                    vec3 _ss = textureBindless2D(getTextureId(), getSamplerId(), {uvExpr}).rgb;
                    float shadowDist = max(min(_ss.r, _ss.g), min(max(_ss.r, _ss.g), _ss.b));
            """;

    private static string GenerateBasicFillGlsl(SDFFontMaterialConfig config)
    {
        string threshold = FormatFloat(config.EdgeThreshold);

        return $$"""
                vec3 _s = textureBindless2D(getTextureId(), getSamplerId(), getUV()).rgb;
                float dist = max(min(_s.r, _s.g), min(max(_s.r, _s.g), _s.b));
                float edgeWidth = fwidth(dist);
                float threshold = {{threshold}};

                float fillAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);
                vec4 color = getColor();
                color.a *= fillAlpha;
                return color;
            """;
    }

    private static string GenerateOutlineFillGlsl(SDFFontMaterialConfig config)
    {
        string threshold = FormatFloat(config.EdgeThreshold);
        string outlineWidth = FormatFloat(config.OutlineWidth);
        string outlineColor = FormatColor4(config.OutlineColor);

        return $$"""
                vec3 _s = textureBindless2D(getTextureId(), getSamplerId(), getUV()).rgb;
                float dist = max(min(_s.r, _s.g), min(max(_s.r, _s.g), _s.b));
                float edgeWidth = fwidth(dist);
                float threshold = {{threshold}};
                float outlineWidth = {{outlineWidth}};
                vec4 outlineColor = vec4({{outlineColor}});

                float outlineOuter = threshold - outlineWidth;
                float outlineAlpha = smoothstep(outlineOuter - edgeWidth, outlineOuter + edgeWidth, dist);
                float fillAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

                vec4 fill = getColor();
                fill.a *= fillAlpha;
                vec4 outline = outlineColor;
                outline.a *= outlineAlpha;
                vec4 result = mix(outline, fill, fillAlpha);
                return result;
            """;
    }

    private static string GenerateShadowFillGlsl(SDFFontMaterialConfig config)
    {
        string threshold = FormatFloat(config.EdgeThreshold);
        string shadowColor = FormatColor4(config.ShadowColor);
        string shadowOffset = FormatVector2(config.ShadowOffset);
        string shadowSoftness = FormatFloat(config.ShadowSoftness);

        return $$"""
                vec2 uv = getUV();
                vec3 _s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                float dist = max(min(_s.r, _s.g), min(max(_s.r, _s.g), _s.b));
                float edgeWidth = fwidth(dist);
                float threshold = {{threshold}};
                vec4 shadowColor = vec4({{shadowColor}});
                vec2 shadowOffset = vec2({{shadowOffset}});
                float shadowSoftness = {{shadowSoftness}};

                vec3 _ss = textureBindless2D(getTextureId(), getSamplerId(), uv - shadowOffset).rgb;
                float shadowDist = max(min(_ss.r, _ss.g), min(max(_ss.r, _ss.g), _ss.b));
                float shadowAlpha = smoothstep(threshold - shadowSoftness - edgeWidth,
                                                threshold + edgeWidth, shadowDist);
                vec4 shadow = shadowColor;
                shadow.a *= shadowAlpha;

                float fillAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);
                vec4 fill = getColor();
                fill.a *= fillAlpha;

                vec4 result = mix(shadow, fill, fillAlpha);
                return result;
            """;
    }

    private static string GenerateShadowOutlineFillGlsl(SDFFontMaterialConfig config)
    {
        string threshold = FormatFloat(config.EdgeThreshold);
        string outlineWidth = FormatFloat(config.OutlineWidth);
        string outlineColor = FormatColor4(config.OutlineColor);
        string shadowColor = FormatColor4(config.ShadowColor);
        string shadowOffset = FormatVector2(config.ShadowOffset);
        string shadowSoftness = FormatFloat(config.ShadowSoftness);

        return $$"""
                vec2 uv = getUV();
                vec3 _s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                float dist = max(min(_s.r, _s.g), min(max(_s.r, _s.g), _s.b));
                float edgeWidth = fwidth(dist);
                float threshold = {{threshold}};
                float outlineWidth = {{outlineWidth}};
                vec4 outlineColor = vec4({{outlineColor}});
                vec4 shadowColor = vec4({{shadowColor}});
                vec2 shadowOffset = vec2({{shadowOffset}});
                float shadowSoftness = {{shadowSoftness}};

                vec3 _ss = textureBindless2D(getTextureId(), getSamplerId(), uv - shadowOffset).rgb;
                float shadowDist = max(min(_ss.r, _ss.g), min(max(_ss.r, _ss.g), _ss.b));
                float shadowAlpha = smoothstep(threshold - shadowSoftness - edgeWidth,
                                                threshold + edgeWidth, shadowDist);
                vec4 shadow = shadowColor;
                shadow.a *= shadowAlpha;

                float outlineOuter = threshold - outlineWidth;
                float outlineAlpha = smoothstep(outlineOuter - edgeWidth, outlineOuter + edgeWidth, dist);
                vec4 outline = outlineColor;
                outline.a *= outlineAlpha;

                float fillAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);
                vec4 fill = getColor();
                fill.a *= fillAlpha;

                vec4 result = mix(shadow, outline, outlineAlpha);
                result = mix(result, fill, fillAlpha);
                return result;
            """;
    }

    private static string FormatFloat(float value) =>
        value.ToString("G", CultureInfo.InvariantCulture);

    private static string FormatColor4(Color4 color) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}, {1}, {2}, {3}",
            color.Red,
            color.Green,
            color.Blue,
            color.Alpha
        );

    private static string FormatVector2(Vector2 vec) =>
        string.Format(CultureInfo.InvariantCulture, "{0}, {1}", vec.X, vec.Y);
}
