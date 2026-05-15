#include "HxHeaders/HeaderFrag.glsl"

// SMAA (Subpixel Morphological Anti-Aliasing) post-processing shader.
// Three stages are selected via specialization constant SMAA_STAGE:
//
//   0 = EDGE_DETECTION      : luminance-based edge detection → RG edge mask
//   1 = BLENDING_WEIGHTS    : pattern search & weight computation → RGBA weight texture
//   2 = NEIGHBOURHOOD_BLEND : final colour blend using computed weights
//
// Based on the reference SMAA implementation by Jorge Jimenez et al.
// Uses precomputed AreaTex (160×560, RG8/RG16) and SearchTex (66×33, R8).

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
    uint areaTextureId;       // precomputed area texture (160×560, RG)
    uint areaSamplerId;       // linear sampler for area texture
    uint searchTextureId;     // precomputed search texture (66×33, R)
    uint searchSamplerId;     // linear sampler for search texture
    float _pad0;
    float _pad1;
    float _pad2;
};

layout(push_constant) uniform PushConstants {
    SmaaPushConstants value;
} pc;

// --------------------------------------------------------------------------
// SMAA Constants (from reference implementation)
// --------------------------------------------------------------------------

#define SMAA_MAX_SEARCH_STEPS      16
#define SMAA_AREATEX_MAX_DISTANCE  16
#define SMAA_AREATEX_PIXEL_SIZE    (vec2(1.0 / 160.0, 1.0 / 560.0))
#define SMAA_AREATEX_SUBTEX_SIZE   (1.0 / 7.0)
#define SMAA_SEARCHTEX_SIZE        vec2(66.0, 33.0)
#define SMAA_SEARCHTEX_PACKED_SIZE vec2(64.0, 16.0)
#define SMAA_CORNER_ROUNDING       25
#define SMAA_CORNER_ROUNDING_NORM  (float(SMAA_CORNER_ROUNDING) / 100.0)

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

float luminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// --------------------------------------------------------------------------
// Stage 0 – Luminance Edge Detection
// --------------------------------------------------------------------------
// Reference SMAA luma edge detection with local contrast adaptation.
//
// Convention:
//   R = 1  ⟹  there is an edge on the LEFT  side of this pixel
//   G = 1  ⟹  there is an edge on the TOP   side of this pixel

#define SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR 2.0

void EdgeDetection() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    // Sample luminances: center, left, top
    float L      = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb);
    float Lleft  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-ts.x, 0.0)).rgb);
    float Ltop   = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, -ts.y)).rgb);

    // Initial threshold test
    vec4 delta;
    delta.xy = abs(vec2(L) - vec2(Lleft, Ltop));
    vec2 edges = step(vec2(pc.value.edgeThreshold), delta.xy);

    // Early discard if no edge
    if (dot(edges, vec2(1.0)) == 0.0) {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    // Sample right and bottom for local contrast adaptation
    float Lright  = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(ts.x, 0.0)).rgb);
    float Lbottom = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, ts.y)).rgb);
    delta.zw = abs(vec2(L) - vec2(Lright, Lbottom));

    // Maximum delta in direct neighbourhood
    vec2 maxDelta = max(delta.xy, delta.zw);

    // Sample left-left and top-top
    float Lleftleft = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-2.0 * ts.x, 0.0)).rgb);
    float Ltoptop   = luminance(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, -2.0 * ts.y)).rgb);
    delta.zw = abs(vec2(Lleft, Ltop) - vec2(Lleftleft, Ltoptop));

    // Final maximum delta
    maxDelta = max(maxDelta, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);

    // Local contrast adaptation (reference SMAA formula):
    // Keep edge only if finalDelta <= FACTOR * edge_delta
    edges *= step(finalDelta, SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);

    outColor = vec4(edges, 0.0, 1.0);
}

// --------------------------------------------------------------------------
// Stage 1 – Blending Weight Computation (Reference SMAA with Area/Search Tex)
// --------------------------------------------------------------------------
// Uses precomputed AreaTex and SearchTex for accurate weight computation.
//
// Weight layout in the output RGBA texture:
//   R (.x) = weight for the pixel on the LEFT  side of a vertical edge
//   G (.y) = weight for the pixel on the RIGHT side of a vertical edge
//   B (.z) = weight for the pixel on the TOP   side of a horizontal edge
//   A (.w) = weight for the pixel on the BOTTOM side of a horizontal edge

// Search texture lookup: determines sub-pixel refinement at edge endpoints.
float SMAASearchLength(vec2 e, float offset) {
    // The texture is flipped vertically, with left and right cases taking half
    // of the space horizontally.
    vec2 scale = SMAA_SEARCHTEX_SIZE * vec2(0.5, -1.0);
    vec2 bias  = SMAA_SEARCHTEX_SIZE * vec2(offset, 1.0);

    // Scale and bias to access texel centers
    scale += vec2(-1.0, 1.0);
    bias  += vec2(0.5, -0.5);

    // Convert from pixel coordinates to texcoords
    scale *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    bias  *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;

    return textureBindless2D(pc.value.searchTextureId, pc.value.searchSamplerId, scale * e + bias).r;
}

// Horizontal search functions
float SMAASearchXLeft(vec2 texcoord, float end) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 e = vec2(0.0, 1.0);
    while (texcoord.x > end &&
           e.g > 0.8281 &&
           e.r == 0.0) {
        e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord).rg;
        texcoord -= vec2(2.0, 0.0) * ts;
    }
    float offset = -(255.0 / 127.0) * SMAASearchLength(e, 0.0) + 3.25;
    return ts.x * offset + texcoord.x;
}

float SMAASearchXRight(vec2 texcoord, float end) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 e = vec2(0.0, 1.0);
    while (texcoord.x < end &&
           e.g > 0.8281 &&
           e.r == 0.0) {
        e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord).rg;
        texcoord += vec2(2.0, 0.0) * ts;
    }
    float offset = -(255.0 / 127.0) * SMAASearchLength(e, 0.5) + 3.25;
    return -ts.x * offset + texcoord.x;
}

// Vertical search functions
float SMAASearchYUp(vec2 texcoord, float end) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 e = vec2(1.0, 0.0);
    while (texcoord.y > end &&
           e.r > 0.8281 &&
           e.g == 0.0) {
        e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord).rg;
        texcoord -= vec2(0.0, 2.0) * ts;
    }
    float offset = -(255.0 / 127.0) * SMAASearchLength(e.gr, 0.0) + 3.25;
    return ts.y * offset + texcoord.y;
}

float SMAASearchYDown(vec2 texcoord, float end) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 e = vec2(1.0, 0.0);
    while (texcoord.y < end &&
           e.r > 0.8281 &&
           e.g == 0.0) {
        e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord).rg;
        texcoord += vec2(0.0, 2.0) * ts;
    }
    float offset = -(255.0 / 127.0) * SMAASearchLength(e.gr, 0.5) + 3.25;
    return -ts.y * offset + texcoord.y;
}

// Area texture lookup: returns blending weights for a given distance pair
// and crossing edge configuration.
vec2 SMAAArea(vec2 dist, float e1, float e2) {
    vec2 texcoord = vec2(SMAA_AREATEX_MAX_DISTANCE) * round(4.0 * vec2(e1, e2)) + dist;

    // Scale and bias for mapping to texel space
    texcoord = SMAA_AREATEX_PIXEL_SIZE * texcoord + 0.5 * SMAA_AREATEX_PIXEL_SIZE;

    // Subsample offset (0 for SMAA 1x)
    texcoord.y += SMAA_AREATEX_SUBTEX_SIZE * 0.0;

    return textureBindless2D(pc.value.areaTextureId, pc.value.areaSamplerId, texcoord).rg;
}

void BlendingWeights() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 pixcoord = inTexCoord / ts; // pixel coordinates

    vec2 e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, inTexCoord).rg;

    vec4 weights = vec4(0.0);

    // --- Horizontal edge (G channel: edge at north/top) ---
    if (e.g > 0.0) {
        // Compute search offsets (reference SMAA vertex shader precomputation)
        vec2 searchOffsetL = inTexCoord + vec2(-0.25, -0.125) * ts;
        vec2 searchOffsetR = inTexCoord + vec2( 1.25, -0.125) * ts;
        float searchEndL = searchOffsetL.x - 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.x;
        float searchEndR = searchOffsetR.x + 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.x;

        // Find distances to left and right endpoints
        vec2 d;
        d.x = SMAASearchXLeft(searchOffsetL, searchEndL);
        d.y = SMAASearchXRight(searchOffsetR, searchEndR);

        // Fetch crossing edges at endpoints
        float e1 = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                     vec2(d.x, inTexCoord.y - 0.25 * ts.y)).r;
        float e2 = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                     vec2(d.y, inTexCoord.y - 0.25 * ts.y) + vec2(ts.x, 0.0)).r;

        // Convert to pixel distances
        d = abs(round(vec2(1.0 / ts.x) * d - pixcoord.xx));

        // Area texture lookup (uses sqrt for quadratic compression)
        vec2 sqrt_d = sqrt(d);
        weights.rg = SMAAArea(sqrt_d, e1, e2);
    }

    // --- Vertical edge (R channel: edge at west/left) ---
    if (e.r > 0.0) {
        // Compute search offsets
        vec2 searchOffsetU = inTexCoord + vec2(-0.125, -0.25) * ts;
        vec2 searchOffsetD = inTexCoord + vec2(-0.125,  1.25) * ts;
        float searchEndU = searchOffsetU.y - 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.y;
        float searchEndD = searchOffsetD.y + 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.y;

        // Find distances to top and bottom endpoints
        vec2 d;
        d.x = SMAASearchYUp(searchOffsetU, searchEndU);
        d.y = SMAASearchYDown(searchOffsetD, searchEndD);

        // Fetch crossing edges at endpoints
        float e1 = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                     vec2(inTexCoord.x - 0.25 * ts.x, d.x)).g;
        float e2 = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId,
                                     vec2(inTexCoord.x - 0.25 * ts.x, d.y) + vec2(0.0, ts.y)).g;

        // Convert to pixel distances
        d = abs(round(vec2(1.0 / ts.y) * d - pixcoord.yy));

        // Area texture lookup
        vec2 sqrt_d = sqrt(d);
        weights.ba = SMAAArea(sqrt_d, e1, e2);
    }

    outColor = weights;
}

// --------------------------------------------------------------------------
// Stage 2 – Neighbourhood Blending (Reference SMAA)
// --------------------------------------------------------------------------
// Gathers blending weights and produces the final anti-aliased colour.
// Uses the reference SMAA approach: two offset samples with normalized weights.

void NeighbourhoodBlend() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    // Fetch blending weights (reference SMAA gathering pattern).
    // offset.xy = texcoord + (ts.x, 0)  → right neighbour
    // offset.zw = texcoord + (0, ts.y)  → bottom neighbour
    vec4 a;
    a.x = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2(ts.x, 0.0)).a;  // Right
    a.y = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2(0.0, ts.y)).g;  // Bottom
    a.wz = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord).xz;                  // Current

    // Early out if no blending needed.
    if (dot(a, vec4(1.0)) < 1e-5) {
        outColor = vec4(textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb, 1.0);
        return;
    }

    // Determine dominant axis: max(horizontal) > max(vertical)
    bool h = max(a.x, a.z) > max(a.y, a.w);

    // Calculate blending offsets and weights.
    vec4 blendingOffset = vec4(0.0, a.y, 0.0, a.w);
    vec2 blendingWeight = a.yw;
    if (h) {
        blendingOffset = vec4(a.x, 0.0, a.z, 0.0);
        blendingWeight = a.xz;
    }
    blendingWeight /= dot(blendingWeight, vec2(1.0));

    // Calculate texture coordinates (reference: mad(offset, float4(ts, -ts), texcoord.xyxy))
    vec4 blendingCoord = vec4(
        inTexCoord + blendingOffset.xy * ts,
        inTexCoord - blendingOffset.zw * ts
    );

    // Blend using bilinear filtering to mix current pixel with neighbour.
    vec3 color = blendingWeight.x * textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, blendingCoord.xy).rgb;
    color += blendingWeight.y * textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, blendingCoord.zw).rgb;

    outColor = vec4(color, 1.0);
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
