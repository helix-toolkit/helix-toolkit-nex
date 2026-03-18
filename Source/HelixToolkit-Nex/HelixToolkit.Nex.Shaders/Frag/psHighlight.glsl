#include "HxHeaders/HeaderFrag.glsl"

// Fragment shader for border-highlight post-processing.
// Stage is selected via specialization constant HIGHLIGHT_STAGE:
//   0 = MASK      : flat white output; rasterises the silhouette mask for highlighted meshes.
//   1 = COMPOSITE : 3×3 edge-detect on the mask, then blends the highlight colour onto the scene.

#define MASK      0
#define COMPOSITE 1

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

layout (constant_id = 0) const uint HIGHLIGHT_STAGE = 0;

@code_gen
struct HighlightPushConstants {
    uint  sceneTextureId;   // scene colour (COMPOSITE stage)
    uint  sceneSamplerId;   // sampler for scene colour (COMPOSITE stage)
    uint  maskTextureId;    // silhouette mask (COMPOSITE stage)
    uint  maskSamplerId;    // sampler for the mask (COMPOSITE stage)
    float texelWidth;       // 1.0 / mask texture width  (COMPOSITE stage)
    float texelHeight;      // 1.0 / mask texture height (COMPOSITE stage)
    float r;                // highlight colour – red   channel
    float g;                // highlight colour – green channel
    float b;                // highlight colour – blue  channel
    float a;                // highlight colour – alpha channel
    float thickness;        // edge half-width in texels (COMPOSITE stage)
    float _pad;
};

layout(push_constant) uniform PushConstants {
    HighlightPushConstants value;
} pc;

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
void main() {
    // ----- Stage 0: Silhouette Mask -----
    // The vertex shader is the regular mesh vertex shader; we only need to
    // output white so every rasterised pixel marks itself as "highlighted".
    if (HIGHLIGHT_STAGE == MASK) {
        outColor = vec4(1.0, 1.0, 1.0, 1.0);
        return;
    }

    // ----- Stage 1: Edge-detect + Composite -----
    if (HIGHLIGHT_STAGE == COMPOSITE) {
        vec3 sceneColor = textureBindless2D(pc.value.sceneTextureId, pc.value.sceneSamplerId, inTexCoord).rgb;

        // 3×3 neighbourhood maximum — any neighbour being white means we are
        // near a silhouette edge.
        float thickness = max(1.0, pc.value.thickness);
        vec2  ts        = vec2(pc.value.texelWidth, pc.value.texelHeight) * thickness;
        float edge      = 0.0;

        // Sample a cross of 4 neighbours (fast, good enough for thin outlines).
        edge = max(edge, textureBindless2D(pc.value.maskTextureId, pc.value.maskSamplerId,
                                          inTexCoord + vec2( ts.x,  0.0)).r);
        edge = max(edge, textureBindless2D(pc.value.maskTextureId, pc.value.maskSamplerId,
                                          inTexCoord + vec2(-ts.x,  0.0)).r);
        edge = max(edge, textureBindless2D(pc.value.maskTextureId, pc.value.maskSamplerId,
                                          inTexCoord + vec2( 0.0,  ts.y)).r);
        edge = max(edge, textureBindless2D(pc.value.maskTextureId, pc.value.maskSamplerId,
                                          inTexCoord + vec2( 0.0, -ts.y)).r);

        // Pixels already inside the silhouette are excluded from the outline so
        // the interior of the mesh remains unaffected.
        float inside  = textureBindless2D(pc.value.maskTextureId, pc.value.maskSamplerId, inTexCoord).r;
        float outline = edge * (1.0 - inside);

        vec4  highlightColor = vec4(pc.value.r, pc.value.g, pc.value.b, pc.value.a);
        vec3  blended        = mix(sceneColor, highlightColor.rgb, outline * highlightColor.a);
        outColor = vec4(blended, 1.0);
        return;
    }

    // Fallback (should never be reached).
    outColor = vec4(1.0, 0.0, 1.0, 1.0);
}
