#include "HxHeaders/HeaderFrag.glsl"

// SMAA (Subpixel Morphological Anti-Aliasing) post-processing shader.
// Three stages are selected via specialization constant SMAA_STAGE:
//
//   0 = EDGE_DETECTION      : luminance/colour edge detection → RG edge mask
//   1 = BLENDING_WEIGHTS    : pattern search & weight computation → RGBA weight texture
//   2 = NEIGHBOURHOOD_BLEND : final colour blend using computed weights
//
// This is a full SMAA 1x implementation: it includes orthogonal (horizontal /
// vertical) pattern handling, diagonal pattern detection and sharp-corner
// rounding, matching the reference SMAA "high/ultra" feature set (minus the
// temporal / multisample variants).
//
// Based on the reference SMAA implementation by Jorge Jimenez et al.
// Uses precomputed AreaTex (160×560, RG) and SearchTex (66×33, R).

#define EDGE_DETECTION      0
#define BLENDING_WEIGHTS    1
#define NEIGHBOURHOOD_BLEND 2

// Edge-detection modes (SmaaPushConstants.edgeMode).
#define EDGE_MODE_LUMA  0u
#define EDGE_MODE_COLOR 1u

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

layout(constant_id = 0) const uint SMAA_STAGE = 0;

// Debug visualisation (specialization constant 1), applied in the
// NEIGHBOURHOOD_BLEND stage. Lets you see each intermediate stage on screen.
#define SMAA_DEBUG_NONE    0u
#define SMAA_DEBUG_EDGES   1u   // show the edge mask (R = left edge, G = top edge)
#define SMAA_DEBUG_WEIGHTS 2u   // show the blending-weight magnitude (white = strong)
#define SMAA_DEBUG_BLEND   3u   // heat-map of the final per-pixel blend strength
layout(constant_id = 1) const uint SMAA_DEBUG = 0;

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
    float edgeThreshold;      // luminance/colour contrast threshold for edge detection
    uint areaTextureId;       // precomputed area texture (160×560, RG)
    uint areaSamplerId;       // linear sampler for area texture
    uint searchTextureId;     // precomputed search texture (66×33, R)
    uint searchSamplerId;     // linear sampler for search texture
    uint edgeMode;            // EDGE_MODE_LUMA (0) or EDGE_MODE_COLOR (1)
    uint diagDetection;       // 0 = disable diagonal pattern detection, non-zero = enable
    uint cornerDetection;     // 0 = disable sharp-corner rounding, non-zero = enable
};

layout(push_constant) uniform PushConstants {
    SmaaPushConstants value;
} pc;

// --------------------------------------------------------------------------
// SMAA Constants (from reference implementation)
// --------------------------------------------------------------------------

#define SMAA_MAX_SEARCH_STEPS          16
#define SMAA_MAX_SEARCH_STEPS_DIAG     16
#define SMAA_AREATEX_MAX_DISTANCE      16
#define SMAA_AREATEX_MAX_DISTANCE_DIAG 20
#define SMAA_AREATEX_PIXEL_SIZE    (vec2(1.0 / 160.0, 1.0 / 560.0))
#define SMAA_AREATEX_SUBTEX_SIZE   (1.0 / 7.0)
#define SMAA_SEARCHTEX_SIZE        vec2(66.0, 33.0)
#define SMAA_SEARCHTEX_PACKED_SIZE vec2(64.0, 16.0)
#define SMAA_CORNER_ROUNDING       25
#define SMAA_CORNER_ROUNDING_NORM  (float(SMAA_CORNER_ROUNDING) / 100.0)
#define SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR 2.0

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

float luminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// Conditional move: copy 'value' into 'variable' component-wise where 'cond' is true.
void SMAAMovc(bvec2 cond, inout vec2 variable, vec2 value) {
    if (cond.x) variable.x = value.x;
    if (cond.y) variable.y = value.y;
}

// Bindless edge fetch at LOD 0 with an integer texel offset.
vec4 sampleEdgeOffset(vec2 uv, vec2 texelOffset, vec2 ts) {
    return textureBindless2DLod(
        pc.value.edgeTextureId, pc.value.edgeSamplerId, uv + texelOffset * ts, 0.0
    );
}

// --------------------------------------------------------------------------
// Stage 0 – Edge Detection
// --------------------------------------------------------------------------
// Reference SMAA luma / colour edge detection with local contrast adaptation.
//
// Convention:
//   R = 1  ⟹  there is an edge on the LEFT  side of this pixel
//   G = 1  ⟹  there is an edge on the TOP   side of this pixel

void LumaEdgeDetection() {
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

// Colour edge detection: uses the maximum per-channel colour delta instead of
// luminance. Detects edges between iso-luminant colours that luma detection
// misses, at the cost of two extra channels per tap.
void ColorEdgeDetection() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);

    vec3 C = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord).rgb;

    vec4 delta;

    vec3 Cleft = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-ts.x, 0.0)).rgb;
    vec3 t = abs(C - Cleft);
    delta.x = max(max(t.r, t.g), t.b);

    vec3 Ctop = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, -ts.y)).rgb;
    t = abs(C - Ctop);
    delta.y = max(max(t.r, t.g), t.b);

    vec2 edges = step(vec2(pc.value.edgeThreshold), delta.xy);

    if (dot(edges, vec2(1.0)) == 0.0) {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec3 Cright = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(ts.x, 0.0)).rgb;
    t = abs(C - Cright);
    delta.z = max(max(t.r, t.g), t.b);

    vec3 Cbottom = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, ts.y)).rgb;
    t = abs(C - Cbottom);
    delta.w = max(max(t.r, t.g), t.b);

    vec2 maxDelta = max(delta.xy, delta.zw);

    vec3 Cleftleft = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(-2.0 * ts.x, 0.0)).rgb;
    t = abs(C - Cleftleft);
    delta.z = max(max(t.r, t.g), t.b);

    vec3 Ctoptop = textureBindless2D(pc.value.colorTextureId, pc.value.colorSamplerId, inTexCoord + vec2(0.0, -2.0 * ts.y)).rgb;
    t = abs(C - Ctoptop);
    delta.w = max(max(t.r, t.g), t.b);

    maxDelta = max(maxDelta, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);

    edges *= step(finalDelta, SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);

    outColor = vec4(edges, 0.0, 1.0);
}

void EdgeDetection() {
    if (pc.value.edgeMode == EDGE_MODE_COLOR) {
        ColorEdgeDetection();
    } else {
        LumaEdgeDetection();
    }
}

// --------------------------------------------------------------------------
// Stage 1 – Blending Weight Computation (Reference SMAA with Area/Search Tex)
// --------------------------------------------------------------------------
// Uses precomputed AreaTex and SearchTex for accurate weight computation.

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
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord, 0.0).rg;
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
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord, 0.0).rg;
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
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord, 0.0).rg;
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
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, texcoord, 0.0).rg;
        texcoord += vec2(0.0, 2.0) * ts;
    }
    float offset = -(255.0 / 127.0) * SMAASearchLength(e.gr, 0.5) + 3.25;
    return -ts.y * offset + texcoord.y;
}

// Area texture lookup (orthogonal): returns blending weights for a given
// distance pair and crossing-edge configuration.
vec2 SMAAArea(vec2 dist, float e1, float e2) {
    vec2 texcoord = vec2(SMAA_AREATEX_MAX_DISTANCE) * round(4.0 * vec2(e1, e2)) + dist;

    // Scale and bias for mapping to texel space
    texcoord = SMAA_AREATEX_PIXEL_SIZE * texcoord + 0.5 * SMAA_AREATEX_PIXEL_SIZE;

    // Subsample offset (0 for SMAA 1x)
    texcoord.y += SMAA_AREATEX_SUBTEX_SIZE * 0.0;

    return textureBindless2DLod(pc.value.areaTextureId, pc.value.areaSamplerId, texcoord, 0.0).rg;
}

// --------------------------------------------------------------------------
// Diagonal pattern detection (reference SMAA)
// --------------------------------------------------------------------------

// Decode two binary values from a bilinear-filtered edge access.
vec2 SMAADecodeDiagBilinearAccess(vec2 e) {
    e.r = e.r * abs(5.0 * e.r - 5.0 * 0.75);
    return round(e);
}

vec4 SMAADecodeDiagBilinearAccess(vec4 e) {
    e.rb = e.rb * abs(5.0 * e.rb - 5.0 * 0.75);
    return round(e);
}

// Diagonal search: walk along the diagonal until the edge line breaks.
vec2 SMAASearchDiag1(vec2 texcoord, vec2 dir, out vec2 e) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec4 coord = vec4(texcoord, -1.0, 1.0);
    vec3 t = vec3(ts.x, ts.y, 1.0);
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) &&
           coord.w > 0.9) {
        coord.xyz = t * vec3(dir, 1.0) + coord.xyz;
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, coord.xy, 0.0).rg;
        coord.w = dot(e, vec2(0.5, 0.5));
    }
    return coord.zw;
}

vec2 SMAASearchDiag2(vec2 texcoord, vec2 dir, out vec2 e) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec4 coord = vec4(texcoord, -1.0, 1.0);
    coord.x += 0.25 * ts.x; // See @SearchDiag2Optimization
    vec3 t = vec3(ts.x, ts.y, 1.0);
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) &&
           coord.w > 0.9) {
        coord.xyz = t * vec3(dir, 1.0) + coord.xyz;

        // Fetch both edges at once using bilinear filtering:
        e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, coord.xy, 0.0).rg;
        e = SMAADecodeDiagBilinearAccess(e);

        coord.w = dot(e, vec2(0.5, 0.5));
    }
    return coord.zw;
}

// Area texture lookup for diagonal patterns (second half of the AreaTex).
vec2 SMAAAreaDiag(vec2 dist, vec2 e, float offset) {
    vec2 texcoord = vec2(SMAA_AREATEX_MAX_DISTANCE_DIAG) * e + dist;

    // Scale and bias for mapping to texel space
    texcoord = SMAA_AREATEX_PIXEL_SIZE * texcoord + 0.5 * SMAA_AREATEX_PIXEL_SIZE;

    // Diagonal areas are on the second half of the texture
    texcoord.x += 0.5;

    // Subsample offset (0 for SMAA 1x)
    texcoord.y += SMAA_AREATEX_SUBTEX_SIZE * offset;

    return textureBindless2DLod(pc.value.areaTextureId, pc.value.areaSamplerId, texcoord, 0.0).rg;
}

// Searches for diagonal patterns and returns the corresponding weights.
vec2 SMAACalculateDiagWeights(vec2 texcoord, vec2 e, vec4 subsampleIndices) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 weights = vec2(0.0, 0.0);

    // Search for the line ends (top-left / bottom-right diagonal):
    vec4 d;
    vec2 end;
    if (e.r > 0.0) {
        d.xz = SMAASearchDiag1(texcoord, vec2(-1.0, 1.0), end);
        d.x += float(end.y > 0.9);
    } else {
        d.xz = vec2(0.0, 0.0);
    }
    d.yw = SMAASearchDiag1(texcoord, vec2(1.0, -1.0), end);

    if (d.x + d.y > 2.0) { // d.x + d.y + 1 > 3
        // Fetch the crossing edges:
        vec4 coords = vec4(-d.x + 0.25, d.x, d.y, -d.y - 0.25) * ts.xyxy + texcoord.xyxy;
        vec4 c;
        c.xy = sampleEdgeOffset(coords.xy, vec2(-1.0, 0.0), ts).rg;
        c.zw = sampleEdgeOffset(coords.zw, vec2( 1.0, 0.0), ts).rg;
        c.yxwz = SMAADecodeDiagBilinearAccess(c.xyzw);

        // Merge crossing edges at each side into a single value:
        vec2 cc = vec2(2.0, 2.0) * c.xz + c.yw;

        // Remove the crossing edge if we didn't find the end of the line:
        SMAAMovc(bvec2(step(vec2(0.9), d.zw)), cc, vec2(0.0, 0.0));

        weights += SMAAAreaDiag(d.xy, cc, subsampleIndices.z);
    }

    // Search for the line ends (top-right / bottom-left diagonal):
    d.xz = SMAASearchDiag2(texcoord, vec2(-1.0, -1.0), end);
    if (sampleEdgeOffset(texcoord, vec2(1.0, 0.0), ts).r > 0.0) {
        d.yw = SMAASearchDiag2(texcoord, vec2(1.0, 1.0), end);
        d.y += float(end.y > 0.9);
    } else {
        d.yw = vec2(0.0, 0.0);
    }

    if (d.x + d.y > 2.0) { // d.x + d.y + 1 > 3
        // Fetch the crossing edges:
        vec4 coords = vec4(-d.x, -d.x, d.y, d.y) * ts.xyxy + texcoord.xyxy;
        vec4 c;
        c.x  = sampleEdgeOffset(coords.xy, vec2(-1.0,  0.0), ts).g;
        c.y  = sampleEdgeOffset(coords.xy, vec2( 0.0, -1.0), ts).r;
        c.zw = sampleEdgeOffset(coords.zw, vec2( 1.0,  0.0), ts).gr;
        vec2 cc = vec2(2.0, 2.0) * c.xz + c.yw;

        // Remove the crossing edge if we didn't find the end of the line:
        SMAAMovc(bvec2(step(vec2(0.9), d.zw)), cc, vec2(0.0, 0.0));

        weights += SMAAAreaDiag(d.xy, cc, subsampleIndices.w).gr;
    }

    return weights;
}

// --------------------------------------------------------------------------
// Sharp-corner detection (reference SMAA)
// --------------------------------------------------------------------------

void SMAADetectHorizontalCornerPattern(inout vec2 weights, vec4 texcoord, vec2 d) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 leftRight = step(d.xy, d.yx);
    vec2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;

    rounding /= leftRight.x + leftRight.y; // Reduce blending for pixels in the center of a line.

    vec2 factor = vec2(1.0, 1.0);
    factor.x -= rounding.x * sampleEdgeOffset(texcoord.xy, vec2(0.0,  1.0), ts).r;
    factor.x -= rounding.y * sampleEdgeOffset(texcoord.zw, vec2(1.0,  1.0), ts).r;
    factor.y -= rounding.x * sampleEdgeOffset(texcoord.xy, vec2(0.0, -2.0), ts).r;
    factor.y -= rounding.y * sampleEdgeOffset(texcoord.zw, vec2(1.0, -2.0), ts).r;

    weights *= clamp(factor, 0.0, 1.0);
}

void SMAADetectVerticalCornerPattern(inout vec2 weights, vec4 texcoord, vec2 d) {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 leftRight = step(d.xy, d.yx);
    vec2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;

    rounding /= leftRight.x + leftRight.y;

    vec2 factor = vec2(1.0, 1.0);
    factor.x -= rounding.x * sampleEdgeOffset(texcoord.xy, vec2( 1.0, 0.0), ts).g;
    factor.x -= rounding.y * sampleEdgeOffset(texcoord.zw, vec2( 1.0, 1.0), ts).g;
    factor.y -= rounding.x * sampleEdgeOffset(texcoord.xy, vec2(-2.0, 0.0), ts).g;
    factor.y -= rounding.y * sampleEdgeOffset(texcoord.zw, vec2(-2.0, 1.0), ts).g;

    weights *= clamp(factor, 0.0, 1.0);
}

void BlendingWeights() {
    vec2 ts = vec2(pc.value.texelWidth, pc.value.texelHeight);
    vec2 pixcoord = inTexCoord / ts; // pixel coordinates
    vec4 subsampleIndices = vec4(0.0); // SMAA 1x has no subsample offsets

    vec2 e = textureBindless2D(pc.value.edgeTextureId, pc.value.edgeSamplerId, inTexCoord).rg;

    vec4 weights = vec4(0.0);

    // --- Horizontal edge (G channel: edge at north/top) ---
    if (e.g > 0.0) {
        // Diagonals share both a north and a west edge, so we look for them
        // here first and give them priority over orthogonal patterns.
        if (pc.value.diagDetection != 0u) {
            weights.rg = SMAACalculateDiagWeights(inTexCoord, e, subsampleIndices);
        }

        if (weights.r == -weights.g) { // weights.r + weights.g == 0.0 → no diagonal
            // Compute search offsets (reference SMAA vertex shader precomputation)
            vec2 searchOffsetL = inTexCoord + vec2(-0.25, -0.125) * ts;
            vec2 searchOffsetR = inTexCoord + vec2( 1.25, -0.125) * ts;
            float searchEndL = searchOffsetL.x - 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.x;
            float searchEndR = searchOffsetR.x + 2.0 * float(SMAA_MAX_SEARCH_STEPS) * ts.x;

            // Find distances to left and right endpoints
            vec2 d;
            d.x = SMAASearchXLeft(searchOffsetL, searchEndL);
            d.y = SMAASearchXRight(searchOffsetR, searchEndR);

            // Preserve endpoint texcoords for corner detection.
            vec2 endpoints = d;

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

            // Round sharp corners.
            if (pc.value.cornerDetection != 0u) {
                vec4 cornerCoords = vec4(endpoints.x, inTexCoord.y, endpoints.y, inTexCoord.y);
                SMAADetectHorizontalCornerPattern(weights.rg, cornerCoords, d);
            }
        } else {
            e.r = 0.0; // diagonal found — cancel vertical processing
        }
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

        // Preserve endpoint texcoords for corner detection.
        vec2 endpoints = d;

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

        // Round sharp corners.
        if (pc.value.cornerDetection != 0u) {
            vec4 cornerCoords = vec4(inTexCoord.x, endpoints.x, inTexCoord.x, endpoints.y);
            SMAADetectVerticalCornerPattern(weights.ba, cornerCoords, d);
        }
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

    // --- Debug visualisations ------------------------------------------------
    if (SMAA_DEBUG == SMAA_DEBUG_EDGES) {
        vec2 e = textureBindless2DLod(pc.value.edgeTextureId, pc.value.edgeSamplerId, inTexCoord, 0.0).rg;
        outColor = vec4(e, 0.0, 1.0);
        return;
    }
    if (SMAA_DEBUG == SMAA_DEBUG_WEIGHTS) {
        vec4 w = textureBindless2DLod(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord, 0.0);
        // Weights peak around 0.5, so scale by 2 to use the full [0,1] range.
        float m = max(max(w.r, w.g), max(w.b, w.a));
        outColor = vec4(vec3(m * 2.0), 1.0);
        return;
    }

    // Fetch blending weights (reference SMAA gathering pattern).
    // offset.xy = texcoord + (ts.x, 0)  → right neighbour
    // offset.zw = texcoord + (0, ts.y)  → bottom neighbour
    vec4 a;
    a.x = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2(ts.x, 0.0)).a;  // Right
    a.y = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord + vec2(0.0, ts.y)).g;  // Bottom
    a.wz = textureBindless2D(pc.value.weightTextureId, pc.value.weightSamplerId, inTexCoord).xz;                  // Current

    if (SMAA_DEBUG == SMAA_DEBUG_BLEND) {
        // Heat-map: blue (no blend) → red (max blend).
        float strength = clamp(dot(a, vec4(1.0)), 0.0, 1.0);
        outColor = vec4(strength, 0.0, 1.0 - strength, 1.0);
        return;
    }

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
