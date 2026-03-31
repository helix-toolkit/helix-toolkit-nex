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
//
// Convention:
//   R = 1  ⟹  there is an edge on the LEFT  side of this pixel  (between this pixel and its left  neighbour)
//   G = 1  ⟹  there is an edge on the TOP   side of this pixel  (between this pixel and its top   neighbour)
void EdgeDetection() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    float lumC = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb);
    float lumL = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-ts.x, 0.0)).rgb);
    float lumT = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2( 0.0,-ts.y)).rgb);

    float edgeH = step(pc.value.edgeThreshold, abs(lumC - lumL));
    float edgeV = step(pc.value.edgeThreshold, abs(lumC - lumT));

    outColor = vec4(edgeH, edgeV, 0.0, 1.0);
}

// --------------------------------------------------------------------------
// Stage 1 – Blending Weight Computation
// --------------------------------------------------------------------------
// For each pixel that was marked as an edge we search along the edge direction
// for the ends of the line feature, then compute a fractional blending weight
// based on a simple trapezoid area approximation (simplified MLAA).
//
// Weight layout in the output RGBA texture:
//   R (.x) = weight for blending LEFT   (vertical   edge on left side of this pixel)
//   G (.y) = weight for blending RIGHT  (always 0 – reserved; the right neighbour's .x carries this)
//   B (.z) = weight for blending UP     (horizontal edge on top  side of this pixel)
//   A (.w) = weight for blending DOWN   (always 0 – reserved; the bottom neighbour's .z carries this)
void BlendingWeights() {
    vec2 ts    = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 edges = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, inTexCoord).rg;

    // Maximum search distance in pixels (quality / performance trade-off).
    const int MAX_SEARCH = 8;

    float weightL = 0.0;
    float weightT = 0.0;

    // --- Horizontal edge (G channel: edge on top side of this pixel) ----------
    // The edge lies between this pixel and the one directly above.  We search
    // left/right along the row of G-edges to find how long the edge segment is,
    // then derive a weight that is strongest at the centre of the segment and
    // fades toward the endpoints (triangle-area approximation).
    if (edges.g > 0.5) {
        int searchL = 0;
        int searchR = 0;
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            float e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                        inTexCoord + vec2(-float(i) * ts.x, 0.0)).g;
            if (e < 0.5) break;
            searchL = i;
        }
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            float e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                        inTexCoord + vec2( float(i) * ts.x, 0.0)).g;
            if (e < 0.5) break;
            searchR = i;
        }
        float span      = float(searchL + searchR) + 1.0;
        float posInSpan  = (float(searchL) + 0.5) / span;
        weightT = 0.5 * (1.0 - abs(posInSpan * 2.0 - 1.0));
    }

    // --- Vertical edge (R channel: edge on left side of this pixel) -----------
    // The edge lies between this pixel and the one directly to the left.  We
    // search up/down along the column of R-edges.
    if (edges.r > 0.5) {
        int searchU = 0;
        int searchD = 0;
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            float e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                        inTexCoord + vec2(0.0, -float(i) * ts.y)).r;
            if (e < 0.5) break;
            searchU = i;
        }
        for (int i = 1; i <= MAX_SEARCH; ++i) {
            float e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                        inTexCoord + vec2(0.0,  float(i) * ts.y)).r;
            if (e < 0.5) break;
            searchD = i;
        }
        float span      = float(searchU + searchD) + 1.0;
        float posInSpan  = (float(searchU) + 0.5) / span;
        weightL = 0.5 * (1.0 - abs(posInSpan * 2.0 - 1.0));
    }

    outColor = vec4(weightL, 0.0, weightT, 0.0);
}

// --------------------------------------------------------------------------
// Stage 2 – Neighbourhood Blending
// --------------------------------------------------------------------------
// Uses the precomputed blending weights to shift the sampling position along
// the dominant edge direction, producing a single sub-pixel blend instead of
// a multi-tap weighted average.  This avoids the double-line artefact that
// occurs when both sides of an edge are independently averaged.
//
// For each pixel we gather four weights:
//   wL = this   pixel's leftward  weight  (weights[P].x)
//   wR = right  pixel's leftward  weight  (weights[P+(1,0)].x)  – contributes as our rightward pull
//   wT = this   pixel's upward    weight  (weights[P].z)
//   wB = bottom pixel's upward    weight  (weights[P+(0,1)].z)  – contributes as our downward pull
//
// We choose the dominant axis (horizontal vs vertical) based on which pair
// has the larger total weight, compute a signed sub-pixel offset, and perform
// a single texture fetch at the shifted position.
void NeighbourhoodBlend() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    // Gather the four relevant weights.
    vec4 wC = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord);
    vec4 wR = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2( ts.x, 0.0));
    vec4 wB = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2( 0.0, ts.y));

    float wLeft  = wC.x;   // blend toward left
    float wRight = wR.x;   // right neighbour blends toward us (= our rightward pull)
    float wTop   = wC.z;   // blend toward top
    float wBot   = wB.z;   // bottom neighbour blends toward us (= our downward pull)

    // Horizontal contribution (left/right weights → shift along X).
    float hWeight = wLeft + wRight;
    // Vertical contribution (top/bottom weights → shift along Y).
    float vWeight = wTop + wBot;

    // If no blending is needed, output the original colour directly.
    if (hWeight + vWeight < 1e-5) {
        outColor = vec4(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb, 1.0);
        return;
    }

    // Choose the dominant direction and compute a signed sub-pixel offset.
    vec2 offset = vec2(0.0);
    float blendFactor = 0.0;

    if (hWeight >= vWeight) {
        // Horizontal blending: shift the sample along X.
        // wRight pulls us rightward (+X), wLeft pulls us leftward (−X).
        float signedW = wRight - wLeft;
        offset = vec2(sign(signedW) * ts.x, 0.0);
        blendFactor = hWeight;
    } else {
        // Vertical blending: shift the sample along Y.
        // wBot pulls us downward (+Y), wTop pulls us upward (−Y).
        float signedW = wBot - wTop;
        offset = vec2(0.0, sign(signedW) * ts.y);
        blendFactor = vWeight;
    }

    // Clamp the blend factor to [0, 1] for safety.
    blendFactor = clamp(blendFactor, 0.0, 1.0);

    // Fetch the original colour and the colour at the shifted position,
    // then linearly interpolate.
    vec3 colorC = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb;
    vec3 colorN = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + offset).rgb;

    outColor = vec4(mix(colorC, colorN, blendFactor), 1.0);
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
