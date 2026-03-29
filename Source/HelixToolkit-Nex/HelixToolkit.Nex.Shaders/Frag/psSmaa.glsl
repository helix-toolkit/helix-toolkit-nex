#include "HxHeaders/HeaderFrag.glsl"

// SMAA (Subpixel Morphological Anti-Aliasing) post-processing shader.
// Three stages are selected via specialization constant SMAA_STAGE:
//
//   0 = EDGE_DETECTION      : luminance-based edge detection → RG edge mask
//   1 = BLENDING_WEIGHTS    : pattern search & weight computation → RGBA weight texture
//   2 = NEIGHBOURHOOD_BLEND : final colour blend using computed weights

#define EDGE_DETECTION      0
#define BLENDING_WEIGHTS    1
#define NEIGHBOURHOOD_BLEND 2

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

layout(constant_id = 0) const uint SMAA_STAGE = 0;

@code_gen
struct SmaaPushConstants {
    uint colorTextureId;      // scene colour input (EDGE_DETECTION / NEIGHBOURHOOD_BLEND)
    uint colorSamplerId;
    uint edgeTextureId;       // edge mask (BLENDING_WEIGHTS / NEIGHBOURHOOD_BLEND)
    uint edgeSamplerId;
    uint weightTextureId;     // blending-weight texture (NEIGHBOURHOOD_BLEND)
    uint weightSamplerId;
    float texelWidth;         // 1.0 / render-target width
    float texelHeight;        // 1.0 / render-target height
    float edgeThreshold;      // luminance contrast threshold for edge detection
    float _pad0;
    float _pad1;
    float _pad2;
};

layout(push_constant) uniform PushConstants {
    SmaaPushConstants value;
} pc;

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

float luminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// --------------------------------------------------------------------------
// Stage 0 – Luminance Edge Detection
// --------------------------------------------------------------------------
// Computes the absolute luminance contrast between the current pixel and its
// left/top neighbours.  Any edge whose contrast exceeds edgeThreshold is kept;
// both components are written to the RG channels of the edge mask texture.
void EdgeDetection() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    float lumC  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord              ).rgb);
    float lumL  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-ts.x, 0.0)).rgb);
    float lumT  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( 0.0,-ts.y)).rgb);
    float lumR  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( ts.x, 0.0)).rgb);
    float lumB  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( 0.0, ts.y)).rgb);

    // Local contrast = difference between the maximum and minimum luminance
    // in the 3×3 neighbourhood. This suppresses detections in flat areas.
    float localMax = max(max(lumL, lumT), max(lumR, max(lumB, lumC)));
    float localMin = min(min(lumL, lumT), min(lumR, min(lumB, lumC)));
    float localContrast = localMax - localMin;

    float threshold = max(pc.value.edgeThreshold, localContrast * 0.2);

    float edgeH = step(threshold, abs(lumC - lumL));
    float edgeV = step(threshold, abs(lumC - lumT));

    outColor = vec4(edgeH, edgeV, 0.0, 1.0);
}

// --------------------------------------------------------------------------
// Stage 1 – Blending Weight Computation
// --------------------------------------------------------------------------
// For each pixel that was marked as an edge we search along the edge direction
// for the ends of the line feature, then compute a fractional blending weight
// based on a simple trapezoid area approximation (simplified MLAA).
void BlendingWeights() {
    vec2 ts     = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 edges  = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, inTexCoord).rg;

    // Maximum search distance in pixels (quality / performance trade-off).
    const int MAX_SEARCH = 8;

    float weightL = 0.0;
    float weightR = 0.0;
    float weightT = 0.0;
    float weightB = 0.0;

    // --- Horizontal edge (stored in G channel of the edge above this pixel) ---
    if (edges.g > 0.5) {
        // Search left and right along the horizontal edge.
        int searchL = 0;
        int searchR = 0;
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            vec2 edgeL = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                           inTexCoord + vec2(-float(i) * ts.x, 0.0)).rg;
            if (edgeL.g < 0.5) break;
            searchL = i;
        }
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            vec2 edgeR = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                           inTexCoord + vec2( float(i) * ts.x, 0.0)).rg;
            if (edgeR.g < 0.5) break;
            searchR = i;
        }
        float spanH = float(searchL + searchR) + 1.0;
        // Triangle area: weight peaks at the centre of the span, tails at the ends.
        float posInSpan = (float(searchL) + 0.5) / spanH;
        float w = 1.0 - abs(posInSpan * 2.0 - 1.0);
        weightT = w * 0.5;
        weightB = w * 0.5;
    }

    // --- Vertical edge (stored in R channel) ---
    if (edges.r > 0.5) {
        // Search up and down along the vertical edge.
        int searchU = 0;
        int searchD = 0;
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            vec2 edgeU = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                           inTexCoord + vec2(0.0, -float(i) * ts.y)).rg;
            if (edgeU.r < 0.5) break;
            searchU = i;
        }
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            vec2 edgeD = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                           inTexCoord + vec2(0.0,  float(i) * ts.y)).rg;
            if (edgeD.r < 0.5) break;
            searchD = i;
        }
        float spanV = float(searchU + searchD) + 1.0;
        float posInSpan = (float(searchU) + 0.5) / spanV;
        float w = 1.0 - abs(posInSpan * 2.0 - 1.0);
        weightL = w * 0.5;
        weightR = w * 0.5;
    }

    outColor = vec4(weightL, weightR, weightT, weightB);
}

// --------------------------------------------------------------------------
// Stage 2 – Neighbourhood Blending
// --------------------------------------------------------------------------
// Fetches the precomputed blending weights for the current pixel and its
// direct neighbours, then reconstructs the anti-aliased colour by linearly
// blending the original colour with the neighbours according to those weights.
void NeighbourhoodBlend() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    // Weights stored as (left, right, top, bottom) for the current pixel.
    vec4  weightsC = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord);
    // Also read the neighbouring weight textures: the right neighbour's left-
    // weight contributes here, and the bottom neighbour's top-weight contributes.
    vec4  weightsR = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2( ts.x, 0.0));
    vec4  weightsB = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2( 0.0, ts.y));

    float wLeft  = weightsC.x;   // current pixel blends leftward
    float wRight = weightsR.x;   // right neighbour blends into us
    float wTop   = weightsC.z;   // current pixel blends upward
    float wBot   = weightsB.z;   // bottom neighbour blends into us

    vec3 colorC = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord              ).rgb;
    vec3 colorL = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-ts.x,  0.0)).rgb;
    vec3 colorR = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( ts.x,  0.0)).rgb;
    vec3 colorT = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( 0.0, -ts.y)).rgb;
    vec3 colorB = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( 0.0,  ts.y)).rgb;

    // Normalise so the total contribution sums to 1.
    float totalW = 1.0 - wLeft - wRight - wTop - wBot;
    vec3 result  = colorC * max(totalW, 0.0)
                 + colorL * wLeft
                 + colorR * wRight
                 + colorT * wTop
                 + colorB * wBot;

    outColor = vec4(result, 1.0);
}

// --------------------------------------------------------------------------
// Main
// --------------------------------------------------------------------------
void main() {
    if (SMAA_STAGE == EDGE_DETECTION) {
        EdgeDetection();
        return;
    }
    if (SMAA_STAGE == BLENDING_WEIGHTS) {
        BlendingWeights();
        return;
    }
    if (SMAA_STAGE == NEIGHBOURHOOD_BLEND) {
        NeighbourhoodBlend();
        return;
    }

    // Fallback – should never be reached.
    outColor = vec4(1.0, 0.0, 1.0, 1.0);
}
