#include "HxHeaders/HeaderFrag.glsl"

// Fragment shader for bloom post-processing.
// Stages are selected via specialization constant BLOOM_STAGE:
//   0 = BRIGHTNESS_EXTRACT : isolate pixels above the luminance threshold
//   1 = BLUR_HORIZONTAL    : 9-tap Gaussian blur (horizontal pass)
//   2 = BLUR_VERTICAL      : 9-tap Gaussian blur (vertical pass)
//   3 = COMPOSITE          : additively blend bloom onto the scene color

#define BRIGHTNESS_EXTRACT 0
#define BLUR_HORIZONTAL    1
#define BLUR_VERTICAL      2
#define COMPOSITE          3

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

layout (constant_id = 0) const uint BLOOM_STAGE = 0;

@code_gen
struct BloomPushConstants {
    uint textureId;      // scene color (BRIGHTNESS_EXTRACT / COMPOSITE) or blur input (BLUR_*)
    uint samplerId;
    uint bloomTextureId; // blurred bloom texture used in COMPOSITE stage
    uint bloomSamplerId; // sampler for the bloom texture in COMPOSITE stage
    float threshold;     // luminance threshold for BRIGHTNESS_EXTRACT
    float intensity;     // bloom intensity multiplier for COMPOSITE
    float texelWidth;    // 1.0 / texture width  (used in BLUR_HORIZONTAL)
    float texelHeight;   // 1.0 / texture height (used in BLUR_VERTICAL)
};

layout(push_constant) uniform PushConstants {
    BloomPushConstants value;
} pc;

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

// Perceptual luminance (Rec. 709 coefficients).
float luminance(vec3 color) {
    return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

// Soft-knee threshold: returns the portion of a color that exceeds the threshold.
vec3 applyThreshold(vec3 color, float threshold) {
    float knee    = threshold * 0.5;
    float lum     = luminance(color);
    float rq      = clamp(lum - threshold + knee, 0.0, 2.0 * knee);
    float weight  = (lum > 0.0) ? (rq * rq) / (4.0 * knee * lum + 1e-5) : 0.0;
    return color * max(weight, step(threshold, lum));
}

// 9-tap Gaussian weights (sigma ≈ 2).
const float GAUSSIAN_WEIGHTS[9] = float[](
    0.0093, 0.028, 0.065, 0.1200, 0.1762,
    0.1200, 0.065, 0.028, 0.0093
);

// --------------------------------------------------------------------------
// Main
// --------------------------------------------------------------------------
void main() {
    // ----- Stage 0: Brightness Extract -----
    if (BLOOM_STAGE == BRIGHTNESS_EXTRACT) {
        vec3 sceneColor = textureBindless2D(pc.value.textureId, pc.value.samplerId, inTexCoord).rgb;
        vec3 bright = applyThreshold(sceneColor, pc.value.threshold);
        outColor = vec4(bright, 1.0);
        return;
    }

    // ----- Stage 1: Horizontal Gaussian Blur -----
    if (BLOOM_STAGE == BLUR_HORIZONTAL) {
        vec3 result = vec3(0.0);
        for (int i = 0; i < 9; ++i) {
            float offset = float(i - 4) * pc.value.texelWidth;
            vec2 uv = inTexCoord + vec2(offset, 0.0);
            result += textureBindless2D(pc.value.textureId, pc.value.samplerId, uv).rgb
                      * GAUSSIAN_WEIGHTS[i];
        }
        outColor = vec4(result, 1.0);
        return;
    }

    // ----- Stage 2: Vertical Gaussian Blur -----
    if (BLOOM_STAGE == BLUR_VERTICAL) {
        vec3 result = vec3(0.0);
        for (int i = 0; i < 9; ++i) {
            float offset = float(i - 4) * pc.value.texelHeight;
            vec2 uv = inTexCoord + vec2(0.0, offset);
            result += textureBindless2D(pc.value.textureId, pc.value.samplerId, uv).rgb
                      * GAUSSIAN_WEIGHTS[i];
        }
        outColor = vec4(result, 1.0);
        return;
    }

    // ----- Stage 3: Composite -----
    if (BLOOM_STAGE == COMPOSITE) {
        vec3 sceneColor = textureBindless2D(pc.value.textureId, pc.value.samplerId, inTexCoord).rgb;
        vec3 bloomColor = textureBindless2D(pc.value.bloomTextureId, pc.value.bloomSamplerId, inTexCoord).rgb;
        outColor = vec4(sceneColor + bloomColor * pc.value.intensity, 1.0);
        return;
    }

    // Fallback (should never be reached).
    outColor = vec4(1.0, 0.0, 1.0, 1.0);
}
