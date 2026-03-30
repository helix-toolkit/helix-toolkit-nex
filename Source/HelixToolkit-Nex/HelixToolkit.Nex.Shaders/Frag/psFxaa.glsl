#include "HxHeaders/HeaderFrag.glsl"

// FXAA (Fast Approximate Anti-Aliasing) post-processing shader.
// Implements Timothy Lottes' FXAA 3.11 algorithm as a single full-screen pass.
//
// Algorithm overview:
//   1. Sample a 3x3 neighbourhood of luma values around the current pixel.
//   2. Compute local contrast (max - min luma). If below threshold, skip.
//   3. Determine the dominant edge direction (horizontal vs vertical).
//   4. Walk along the perpendicular axis in both directions to find the end-
//      points of the edge span.
//   5. Blend the pixel with its neighbour across the edge by an amount that
//      is proportional to how far the pixel sits from the midpoint of the span.

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

// 0 = normal FXAA output
// 1 = edge mask  (red = pixel triggered AA, green = passed to blend)
// 2 = blend-amount heat map  (blue → red as finalOffset grows)
layout(constant_id = 0) const uint FXAA_DEBUG_MODE = 0;

@code_gen
struct FxaaPushConstants {
    uint colorTextureId;   // Input HDR scene colour
    uint samplerId;        // Linear clamp sampler
    float texelWidth;      // 1.0 / render-target width
    float texelHeight;     // 1.0 / render-target height
    float contrastThreshold;   // Minimum local contrast required to trigger AA (default 0.0312)
    float relativeThreshold;   // Maximum local contrast relative to brightest neighbour (default 0.125)
    float subpixelBlending;    // Amount of sub-pixel anti-aliasing blending [0..1] (default 0.75)
    float _pad0;
};

layout(push_constant) uniform PushConstants {
    FxaaPushConstants value;
} pc;

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

// Perceptual luma via the green channel approximation (fast, sufficient for AA).
float Luma(vec3 rgb) {
    return rgb.g * 0.587 + rgb.r * 0.299 + rgb.b * 0.114;
}

vec3 SampleColor(vec2 uv) {
    return textureBindless2D(pc.value.colorTextureId, pc.value.samplerId, uv).rgb;
}

float SampleLuma(vec2 uv) {
    return Luma(SampleColor(uv));
}

// --------------------------------------------------------------------------
// Main
// --------------------------------------------------------------------------
void main() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    // -----------------------------------------------------------------------
    // 1. Sample a 3x3 luma neighbourhood
    // -----------------------------------------------------------------------
    float lumaC  = SampleLuma(inTexCoord);
    float lumaN  = SampleLuma(inTexCoord + vec2( 0.0,  ts.y));
    float lumaS  = SampleLuma(inTexCoord + vec2( 0.0, -ts.y));
    float lumaE  = SampleLuma(inTexCoord + vec2( ts.x,  0.0));
    float lumaW  = SampleLuma(inTexCoord + vec2(-ts.x,  0.0));

    float lumaNE = SampleLuma(inTexCoord + vec2( ts.x,  ts.y));
    float lumaNW = SampleLuma(inTexCoord + vec2(-ts.x,  ts.y));
    float lumaSE = SampleLuma(inTexCoord + vec2( ts.x, -ts.y));
    float lumaSW = SampleLuma(inTexCoord + vec2(-ts.x, -ts.y));

    // -----------------------------------------------------------------------
    // 2. Local contrast / early-out
    // -----------------------------------------------------------------------
    float lumaMin = min(lumaC, min(min(lumaN, lumaS), min(lumaE, lumaW)));
    float lumaMax = max(lumaC, max(max(lumaN, lumaS), max(lumaE, lumaW)));
    float lumaRange = lumaMax - lumaMin;

    // Skip pixels with too little contrast (flat regions).
    if (lumaRange < max(pc.value.contrastThreshold, lumaMax * pc.value.relativeThreshold)) {
        outColor = vec4(SampleColor(inTexCoord), 1.0);
        return;
    }

    // -----------------------------------------------------------------------
    // 3. Blend factor from 3x3 average  (sub-pixel AA)
    // -----------------------------------------------------------------------
    float lumaL   = (lumaN + lumaS + lumaE + lumaW) * 0.25;
    float rangeL  = abs(lumaL - lumaC);
    float blendL  = max(0.0, (rangeL / lumaRange) - 0.0) * pc.value.subpixelBlending;
    blendL = smoothstep(0.0, 1.0, blendL);
    float pixelBlend = blendL * blendL;

    // -----------------------------------------------------------------------
    // 4. Edge direction
    // -----------------------------------------------------------------------
    float edgeH = abs(lumaN + lumaS - 2.0 * lumaC) * 2.0
                + abs(lumaNE + lumaSE - 2.0 * lumaE)
                + abs(lumaNW + lumaSW - 2.0 * lumaW);

    float edgeV = abs(lumaE + lumaW - 2.0 * lumaC) * 2.0
                + abs(lumaNE + lumaNW - 2.0 * lumaN)
                + abs(lumaSE + lumaSW - 2.0 * lumaS);

    bool isHorizontal = (edgeH >= edgeV);

    // The two luma values that straddle the edge.
    float luma1 = isHorizontal ? lumaS : lumaW;
    float luma2 = isHorizontal ? lumaN : lumaE;

    // Gradient magnitudes across the edge.
    float gradient1 = abs(luma1 - lumaC);
    float gradient2 = abs(luma2 - lumaC);
    bool  steepest1 = (gradient1 >= gradient2);
    float stepSize  = isHorizontal ? ts.y : ts.x;

    // Step toward the steeper side.
    float lumaLocalAvg;
    if (!steepest1) {
        stepSize     = -stepSize;
        lumaLocalAvg = 0.5 * (luma2 + lumaC);
    } else {
        lumaLocalAvg = 0.5 * (luma1 + lumaC);
    }

    // Move to the edge midpoint.
    vec2 currentUV = inTexCoord;
    if (isHorizontal) {
        currentUV.y += stepSize * 0.5;
    } else {
        currentUV.x += stepSize * 0.5;
    }

    // -----------------------------------------------------------------------
    // 5. Endpoint search along the edge (up to 12 steps each side)
    // -----------------------------------------------------------------------
    vec2 offset = isHorizontal ? vec2(ts.x, 0.0) : vec2(0.0, ts.y);

    vec2 uv1 = currentUV - offset;
    vec2 uv2 = currentUV + offset;

    float lumaEnd1 = SampleLuma(uv1) - lumaLocalAvg;
    float lumaEnd2 = SampleLuma(uv2) - lumaLocalAvg;

    bool reached1 = abs(lumaEnd1) >= gradient1 * 0.25;
    bool reached2 = abs(lumaEnd2) >= gradient1 * 0.25;
    bool reachedBoth = reached1 && reached2;

    if (!reached1) uv1 -= offset;
    if (!reached2) uv2 += offset;

    const int SEARCH_STEPS = 12;

    for (int i = 0; i < SEARCH_STEPS; i++) {
        if (reachedBoth) break;

        if (!reached1) {
            lumaEnd1 = SampleLuma(uv1) - lumaLocalAvg;
        }
        if (!reached2) {
            lumaEnd2 = SampleLuma(uv2) - lumaLocalAvg;
        }

        reached1    = abs(lumaEnd1) >= gradient1 * 0.25;
        reached2    = abs(lumaEnd2) >= gradient1 * 0.25;
        reachedBoth = reached1 && reached2;

        if (!reached1) uv1 -= offset;
        if (!reached2) uv2 += offset;
    }

    // -----------------------------------------------------------------------
    // 6. Blend the pixel toward the edge-perpendicular neighbour
    // -----------------------------------------------------------------------
    float dist1 = isHorizontal ? (inTexCoord.x - uv1.x) : (inTexCoord.y - uv1.y);
    float dist2 = isHorizontal ? (uv2.x - inTexCoord.x) : (uv2.y - inTexCoord.y);

    // Guard against floating-point overshoot making a distance negative.
    dist1 = max(0.0, dist1);
    dist2 = max(0.0, dist2);

    bool isDir1     = dist1 < dist2;
    float distFinal = min(dist1, dist2);
    float edgeLen   = dist1 + dist2;

    float pixelOffset = -distFinal / edgeLen + 0.5;

    // Accept the span-based blend when lumaC and the nearer endpoint sit on the
    // SAME side of lumaLocalAvg.  Both lumaC_lt_avg and (lumaEnd < 0.0) express
    // "is this value below lumaLocalAvg", so they must agree (==) for the blend
    // to be in the smoothing direction.  The previous != inverted the gate,
    // rejecting every valid pixel.
    bool lumaC_lt_avg = lumaC < lumaLocalAvg;
    float nearLumaEnd = isDir1 ? lumaEnd1 : lumaEnd2;
    bool correctVar   = (nearLumaEnd < 0.0) == lumaC_lt_avg;

    float finalOffset = correctVar ? max(pixelBlend, pixelOffset) : pixelBlend;

    // -----------------------------------------------------------------------
    // 7. Final sample with the computed offset
    // -----------------------------------------------------------------------
    vec2 finalUV = inTexCoord;
    if (isHorizontal) {
        finalUV.y += finalOffset * stepSize;
    } else {
        finalUV.x += finalOffset * stepSize;
    }

    vec3 result = SampleColor(finalUV);

    if (FXAA_DEBUG_MODE == 1u) {
        // Red overlay: every pixel that passed the contrast gate (AA was attempted).
        outColor = vec4(1.0, result.g * 0.25, result.b * 0.25, 1.0);
        return;
    }
    if (FXAA_DEBUG_MODE == 2u) {
        // Heat map:
        //   red   = finalOffset (span-based blend — should be non-zero on edges)
        //   green = pixelBlend  (sub-pixel blend contribution)
        //   blue  = 1 - finalOffset (zero blend = pure blue)
        outColor = vec4(finalOffset, pixelBlend, 1.0 - finalOffset, 1.0);
        return;
    }

    outColor = vec4(result, 1.0);
}
