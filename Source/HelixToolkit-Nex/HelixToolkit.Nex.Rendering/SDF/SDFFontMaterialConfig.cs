using System.Globalization;

namespace HelixToolkit.Nex.Rendering.SDF;

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

    /// <summary>
    /// Emits the shared MSDF preamble GLSL containing atlas parameter accessor calls,
    /// the em-space threshold conversion, UV retrieval, and screen-pixel-scale computation.
    /// </summary>
    private static string EmitMsdfPreamble(float edgeThreshold)
    {
        string edgeThresholdStr = FormatFloat(edgeThreshold);

        return $"""
                    vec2 aemrange = getSdfAemrange();
                    vec2 atlas_size = getSdfAtlasSize();
                    float glyph_cell_size = getSdfGlyphCellSize();
                    const float SUPERSAMPLE_THRESHOLD = 20.0;
                    float threshold_em = mix(aemrange[1], aemrange[0], {edgeThresholdStr});

                    vec2 uv = getUV();
                    float screen_px_scale = max(length(atlas_size * fwidth(uv)), 1.0);
                    float inverse_width = screen_px_scale * glyph_cell_size;
            """;
    }

    private static string GenerateBasicFillGlsl(SDFFontMaterialConfig config)
    {
        string preamble = EmitMsdfPreamble(config.EdgeThreshold);

        return $$"""
            {{preamble}}

                    float opacity;
                    float maxDim = max(getBillboardWidth(), getBillboardHeight());

                    if (maxDim < SUPERSAMPLE_THRESHOLD) {
                        vec2 step = fwidth(uv) * 0.25;
                        float sum = 0.0;
                        for (int dy = -1; dy <= 1; dy += 2) {
                            for (int dx = -1; dx <= 1; dx += 2) {
                                vec2 suv = uv + vec2(float(dx), float(dy)) * step;
                                vec3 s = textureBindless2D(getTextureId(), getSamplerId(), suv).rgb;
                                float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                                float dist_em = mix(aemrange[1], aemrange[0], texel);
                                sum += clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                            }
                        }
                        opacity = sum * 0.25;
                    } else {
                        vec3 s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                        float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                        float dist_em = mix(aemrange[1], aemrange[0], texel);
                        opacity = clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                    }

                    vec4 color = getColor();
                    color.a *= opacity;
                    color.rgb *= color.a;
                    return color;
            """;
    }

    private static string GenerateOutlineFillGlsl(SDFFontMaterialConfig config)
    {
        string preamble = EmitMsdfPreamble(config.EdgeThreshold);
        string outlineWidthStr = FormatFloat(config.OutlineWidth);
        string outlineColorStr = FormatColor4(config.OutlineColor);

        return $$"""
            {{preamble}}
                    const float outlineWidth = {{outlineWidthStr}};
                    const vec4 outlineColor = vec4({{outlineColorStr}});
                    float outlineOuter_em = threshold_em + outlineWidth;

                    float fillAlpha;
                    float outlineAlpha;
                    float maxDim = max(getBillboardWidth(), getBillboardHeight());

                    if (maxDim < SUPERSAMPLE_THRESHOLD) {
                        vec2 step = fwidth(uv) * 0.25;
                        float fillSum = 0.0;
                        float outlineSum = 0.0;
                        for (int dy = -1; dy <= 1; dy += 2) {
                            for (int dx = -1; dx <= 1; dx += 2) {
                                vec2 suv = uv + vec2(float(dx), float(dy)) * step;
                                vec3 s = textureBindless2D(getTextureId(), getSamplerId(), suv).rgb;
                                float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                                float dist_em = mix(aemrange[1], aemrange[0], texel);
                                fillSum += clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                                outlineSum += clamp((outlineOuter_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                            }
                        }
                        fillAlpha = fillSum * 0.25;
                        outlineAlpha = outlineSum * 0.25;
                    } else {
                        vec3 s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                        float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                        float dist_em = mix(aemrange[1], aemrange[0], texel);
                        fillAlpha = clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                        outlineAlpha = clamp((outlineOuter_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                    }

                    vec4 fill = getColor();
                    fill.a *= fillAlpha;
                    vec4 outline = outlineColor;
                    outline.a *= outlineAlpha;
                    vec4 result = mix(outline, fill, fillAlpha);
                    result.rgb *= result.a;
                    return result;
            """;
    }

    private static string GenerateShadowFillGlsl(SDFFontMaterialConfig config)
    {
        string preamble = EmitMsdfPreamble(config.EdgeThreshold);
        string shadowColorStr = FormatColor4(config.ShadowColor);
        string shadowOffsetStr = FormatVector2(config.ShadowOffset);
        string shadowSoftnessStr = FormatFloat(config.ShadowSoftness);

        return $$"""
            {{preamble}}
                    const vec4 shadowColor = vec4({{shadowColorStr}});
                    const vec2 shadowOffset = vec2({{shadowOffsetStr}});
                    const float shadowSoftness = {{shadowSoftnessStr}};

                    float fillAlpha;
                    float shadowAlpha;
                    float maxDim = max(getBillboardWidth(), getBillboardHeight());

                    if (maxDim < SUPERSAMPLE_THRESHOLD) {
                        vec2 step = fwidth(uv) * 0.25;
                        float fillSum = 0.0;
                        float shadowSum = 0.0;
                        for (int dy = -1; dy <= 1; dy += 2) {
                            for (int dx = -1; dx <= 1; dx += 2) {
                                vec2 suv = uv + vec2(float(dx), float(dy)) * step;
                                vec3 s = textureBindless2D(getTextureId(), getSamplerId(), suv).rgb;
                                float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                                float dist_em = mix(aemrange[1], aemrange[0], texel);
                                fillSum += clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);

                                vec2 shadowUv = suv - shadowOffset;
                                vec3 ss = textureBindless2D(getTextureId(), getSamplerId(), shadowUv).rgb;
                                float shadowTexel = max(min(ss.r, ss.g), min(max(ss.r, ss.g), ss.b));
                                float shadowDist_em = mix(aemrange[1], aemrange[0], shadowTexel);
                                shadowSum += clamp((threshold_em + shadowSoftness - shadowDist_em) * inverse_width + 0.5, 0.0, 1.0);
                            }
                        }
                        fillAlpha = fillSum * 0.25;
                        shadowAlpha = shadowSum * 0.25;
                    } else {
                        vec3 s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                        float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                        float dist_em = mix(aemrange[1], aemrange[0], texel);
                        fillAlpha = clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);

                        vec2 shadowUv = uv - shadowOffset;
                        vec3 ss = textureBindless2D(getTextureId(), getSamplerId(), shadowUv).rgb;
                        float shadowTexel = max(min(ss.r, ss.g), min(max(ss.r, ss.g), ss.b));
                        float shadowDist_em = mix(aemrange[1], aemrange[0], shadowTexel);
                        shadowAlpha = clamp((threshold_em + shadowSoftness - shadowDist_em) * inverse_width + 0.5, 0.0, 1.0);
                    }

                    vec4 fill = getColor();
                    fill.a *= fillAlpha;
                    vec4 shadow = shadowColor;
                    shadow.a *= shadowAlpha;
                    vec4 result = mix(shadow, fill, fillAlpha);
                    result.rgb *= result.a;
                    return result;
            """;
    }

    private static string GenerateShadowOutlineFillGlsl(SDFFontMaterialConfig config)
    {
        string preamble = EmitMsdfPreamble(config.EdgeThreshold);
        string outlineWidthStr = FormatFloat(config.OutlineWidth);
        string outlineColorStr = FormatColor4(config.OutlineColor);
        string shadowColorStr = FormatColor4(config.ShadowColor);
        string shadowOffsetStr = FormatVector2(config.ShadowOffset);
        string shadowSoftnessStr = FormatFloat(config.ShadowSoftness);

        return $$"""
            {{preamble}}
                    const float outlineWidth = {{outlineWidthStr}};
                    const vec4 outlineColor = vec4({{outlineColorStr}});
                    const vec4 shadowColor = vec4({{shadowColorStr}});
                    const vec2 shadowOffset = vec2({{shadowOffsetStr}});
                    const float shadowSoftness = {{shadowSoftnessStr}};
                    float outlineOuter_em = threshold_em + outlineWidth;

                    float fillAlpha;
                    float outlineAlpha;
                    float shadowAlpha;
                    float maxDim = max(getBillboardWidth(), getBillboardHeight());

                    if (maxDim < SUPERSAMPLE_THRESHOLD) {
                        vec2 step = fwidth(uv) * 0.25;
                        float fillSum = 0.0;
                        float outlineSum = 0.0;
                        float shadowSum = 0.0;
                        for (int dy = -1; dy <= 1; dy += 2) {
                            for (int dx = -1; dx <= 1; dx += 2) {
                                vec2 suv = uv + vec2(float(dx), float(dy)) * step;
                                vec3 s = textureBindless2D(getTextureId(), getSamplerId(), suv).rgb;
                                float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                                float dist_em = mix(aemrange[1], aemrange[0], texel);
                                fillSum += clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                                outlineSum += clamp((outlineOuter_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);

                                vec2 shadowUv = suv - shadowOffset;
                                vec3 ss = textureBindless2D(getTextureId(), getSamplerId(), shadowUv).rgb;
                                float shadowTexel = max(min(ss.r, ss.g), min(max(ss.r, ss.g), ss.b));
                                float shadowDist_em = mix(aemrange[1], aemrange[0], shadowTexel);
                                shadowSum += clamp((threshold_em + shadowSoftness - shadowDist_em) * inverse_width + 0.5, 0.0, 1.0);
                            }
                        }
                        fillAlpha = fillSum * 0.25;
                        outlineAlpha = outlineSum * 0.25;
                        shadowAlpha = shadowSum * 0.25;
                    } else {
                        vec3 s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                        float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                        float dist_em = mix(aemrange[1], aemrange[0], texel);
                        fillAlpha = clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                        outlineAlpha = clamp((outlineOuter_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);

                        vec2 shadowUv = uv - shadowOffset;
                        vec3 ss = textureBindless2D(getTextureId(), getSamplerId(), shadowUv).rgb;
                        float shadowTexel = max(min(ss.r, ss.g), min(max(ss.r, ss.g), ss.b));
                        float shadowDist_em = mix(aemrange[1], aemrange[0], shadowTexel);
                        shadowAlpha = clamp((threshold_em + shadowSoftness - shadowDist_em) * inverse_width + 0.5, 0.0, 1.0);
                    }

                    vec4 fill = getColor();
                    fill.a *= fillAlpha;
                    vec4 outline = outlineColor;
                    outline.a *= outlineAlpha;
                    vec4 shadow = shadowColor;
                    shadow.a *= shadowAlpha;

                    vec4 result = mix(shadow, outline, outlineAlpha);
                    result = mix(result, fill, fillAlpha);
                    result.rgb *= result.a;
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
