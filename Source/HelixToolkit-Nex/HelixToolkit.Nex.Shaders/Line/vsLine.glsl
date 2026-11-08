#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/MeshInfo.glsl"
#include "HxHeaders/NodeInfo.glsl"
#include "HxHeaders/HeaderPackEntity.glsl" // packObjectInfo() (Req 6.1)
#include "Line/LineStructs.glsl"
#include "Line/LineShape.glsl"
#include "HxHeaders/DrawTypeCheck.glsl"
// ------------------------------------------------------------------
// VARYINGS (must match psLineTemplate.glsl exactly: location, type, flat)
// ------------------------------------------------------------------
// Quad_Corner_UV: x = -1 at the start endpoint, +1 at the end endpoint (extrapolated beyond them
// over the cap/join extension); y = ±1 at the nominal line edge (slightly beyond on the AA pad).
layout(location = 0) out vec2  v_uv;
layout(location = 1) out vec4  v_color;         // interpolated RGBA endpoint color
// noperspective: the width in pixels varies linearly in SCREEN space between endpoints (offset =
// width * w / w), so it must not be perspective-corrected.
layout(location = 2) out noperspective float v_screenSize;    // Line_Width in pixels (screen mode: clamped to [1,64]; world mode: at least 1, no upper limit)
layout(location = 3) out flat uvec2  v_entityId;       // packed GPU-picking entity id
layout(location = 4) out flat uint  v_textureIndex;   // bindless texture index (0 = none)
layout(location = 5) out flat uint  v_samplerIndex;   // bindless sampler index
layout(location = 6) out vec3  v_fragWorldPos;   // per-fragment world-space position
// Cap / join shaping inputs (see Line/LineShape.glsl). Identical for all four corners.
layout(location = 7) out flat vec4  v_segPx;      // segment start.xy, end.xy in line px
layout(location = 8) out flat vec4  v_dirHw;      // unit segment direction.xy, start / end half width (px)
layout(location = 9) out flat vec4  v_adjDir;     // neighbour direction pointing away from the start / end joint
layout(location = 10) out flat uint v_endModes;   // LINE_END_* of the start | end << 8

// ------------------------------------------------------------------
// BUFFER REFERENCES (each declared exactly once)
// ------------------------------------------------------------------
layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
    vec4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexColorBuffer {
    vec4 colors[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LineDrawBuffer {
    LineDraw draws[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshInfoBuffer {
    MeshInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer NodeInfoBuffer {
    GpuNodeInfo value[];
};

// ------------------------------------------------------------------
// PUSH CONSTANT
// ------------------------------------------------------------------
// NOTE: LineRenderPC is defined in Line/LineStructs.glsl (shared @code_gen type).
// Do NOT redeclare it here — a duplicate definition breaks compilation (Req 11.1).
layout(push_constant) uniform PC {
    LineRenderPC value;
} pc;

// ------------------------------------------------------------------
// PER-DRAW DATA RESOLUTION
// ------------------------------------------------------------------
FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

// Read this draw's LineDraw record at gl_DrawID + drawCommandIdxOffset (Req 9.5).
uint drawIndex = gl_DrawID + pc.value.drawCommandIdxOffset;

LineDraw meshDraw = LineDrawBuffer(pc.value.meshDrawBufferAddress).draws[drawIndex];

// Resolve the owning node via nodeInfoIndex into the node info buffer (Req 9.6).
GpuNodeInfo nodeInfo = NodeInfoBuffer(fpConst.nodeInfoBufferAddress).value[meshDraw.nodeInfoIndex];

// Resolve the geometry record via meshId (provides vertex / color buffer addresses).
MeshInfo meshInfo = MeshInfoBuffer(fpConst.meshInfoBufferAddress).value[meshDraw.meshId];

// Triangle strip quad corners: 4 vertices → 2 triangles
//   v3 (TL) ── v2 (TR)
//    │  \       │
//   v0 (BL) ── v1 (BR)
// Strip order: v0, v1, v3, v2  (Z-pattern)
const vec2 QUAD_UVS[4] = vec2[4](
    vec2(-1.0, -1.0),   // v0: bottom-left
    vec2( 1.0, -1.0),   // v1: bottom-right
    vec2(-1.0,  1.0),   // v3: top-left
    vec2( 1.0,  1.0)    // v2: top-right
);

// ------------------------------------------------------------------
// DEGENERATE-SEGMENT GUARDS (Task 3.4 — Req 1.6, 2.5, 10.1, 10.2, 10.3)
// ------------------------------------------------------------------
// Smallest magnitude treated as non-zero.
const float LINE_EPSILON = 1e-6;

// Guard a clip-space w against a divisor smaller than LINE_EPSILON before the perspective
// divide, preserving its sign: the NDC divide never uses a divisor < 1e-6, so no NaN/Inf is
// produced (Req 10.1). The real (unguarded) w is still used as the homogeneous anchor for w <= 0
// (at/behind near-plane) endpoints so the rasterizer clips them against the near plane (Req 10.3).
float guardW(float w) {
    if (abs(w) < LINE_EPSILON) {
        return w < 0.0 ? -LINE_EPSILON : LINE_EPSILON;
    }
    return w;
}

// Project a clip-space position to NDC xy via the guarded perspective divide.
vec2 ndcXY(vec4 clip) {
    float w = guardW(clip.w);
    return clip.xy / w;
}

// Project a clip-space position to line pixel space (NDC * 0.5 * screenDimensions).
// The joint math in this file is marked `precise` so a segment and its neighbour derive
// bit-identical joint positions and directions (the fragment bisector split depends on it).
vec2 linePx(vec4 clip) {
    precise vec2 px = ndcXY(clip) * 0.5 * fpConst.screenDimensions;
    return px;
}

// Near-plane clipping of a clip-space segment BEFORE any projected direction / width / offset math.
// An endpoint behind the camera has w <= 0, which would give a negative (clamped to 1px)
// world-space width and a flipped screen direction that the rasterizer would then carry into
// the visible part of the segment. Reverse-Z: a point is inside the near plane when
// clip.z <= clip.w. t0/t1 are the parametric positions (0 = start, 1 = end) of the clipped start
// and end. Only an endpoint that is actually clipped is replaced, so an unclipped endpoint stays
// bit-identical to what a neighbouring segment computes for the same shared vertex.
void clipNear(inout vec4 c0, inout vec4 c1, out float t0, out float t1) {
    t0 = 0.0;
    t1 = 1.0;
    float dNear0 = c0.w - c0.z;
    float dNear1 = c1.w - c1.z;
    if (dNear0 < 0.0 && dNear1 >= 0.0) {
        t0 = dNear0 / (dNear0 - dNear1);
        c0 = mix(c0, c1, t0);
    } else if (dNear1 < 0.0 && dNear0 >= 0.0) {
        t1 = dNear0 / (dNear0 - dNear1);
        c1 = mix(c0, c1, t1);
    }
}

// Unit pixel-space direction from a to b. When the projected length is <= 1e-6 (coincident
// endpoints or a zero-length projected segment) a fixed default unit direction (1, 0) is
// substituted instead of dividing by a divisor < 1e-6, and false is returned.
bool unitDirPx(vec2 a, vec2 b, out vec2 dir, out float len) {
    precise vec2 d = b - a;
    precise float l = length(d);
    precise vec2 u = (l <= LINE_EPSILON) ? vec2(1.0, 0.0) : d / l;
    dir = u;
    len = l;
    return l > LINE_EPSILON;
}

// Projected, near-plane-clipped pixel direction of an object-space segment a -> b. Used for this
// segment and for its neighbours so both sides of a joint derive the same direction.
bool segmentDirPx(mat4 mvp, vec3 a, vec3 b, out vec2 dir) {
    precise vec4 c0 = mvp * vec4(a, 1.0);
    precise vec4 c1 = mvp * vec4(b, 1.0);
    float t0, t1;
    clipNear(c0, c1, t0, t1);
    float len;
    return unitDirPx(linePx(c0), linePx(c1), dir, len);
}

// Task 3.3b: line-width resolution — fixed-pixel + distance-scaled branches, mirroring
// vsPoint.glsl's point-size resolution.
// renderSpace == 0 (Screen): lineWidth is already a screen-pixel width, constant with
//                            camera distance.
// renderSpace == 1 (World):  lineWidth is a world-space diameter projected to screen pixels
//                            at the endpoint's depth. FPConstants exposes no standalone
//                            projection matrix, so recover it as
//                            projection = viewProjection * inverseView (since viewProjection
//                            = projection * view). Its [1][1] element is the vertical
//                            projection scale 1/tan(fovY/2). A world diameter D at guarded
//                            clip depth w projects to D * projScaleY * screenDimensions.y /
//                            (2 * w) pixels, so the near endpoint of a segment renders thicker
//                            than the far endpoint, matching a true world-space diameter.
//
// Clamp Line_Width to [1.0, 64.0] px (Req 2.3).
// Screen mode: the width is constant along the segment, so clamping per endpoint is exact.
// World mode: the width is a physical size, so it has no upper limit (it keeps growing as the
// camera approaches) and only the 1px lower bound is applied.
float resolveWidthPx(vec4 clip) {
    if (meshDraw.renderSpace == 0u) {
        return clamp(meshDraw.lineWidth, 1.0, 64.0);
    }
    mat4 projection = fpConst.viewProjection * fpConst.inverseView;
    float projScaleY = projection[1][1];
    float w = guardW(clip.w);
    return max(meshDraw.lineWidth * projScaleY * fpConst.screenDimensions.y / (2.0 * w), 1.0);
}

// Effective shape of one segment end. u = this segment's direction pointing out of the end,
// v = the neighbour's direction pointing away from the joint. Both segments sharing a joint
// evaluate this with (u, v) swapped and negated, which yields the same u + v up to sign, so they
// always agree on the mode.
uint resolveEndMode(bool connected, vec2 u, vec2 v) {
    if (connected && meshDraw.lineJoin != LINE_JOIN_NONE) {
        float cosHalf = 0.5 * length(u + v); // cos(turnAngle / 2)
        if (cosHalf > LINE_JOIN_MIN_COS_HALF) {
            if (meshDraw.lineJoin == LINE_JOIN_MITER) {
                // Miter length / width = 1 / cos(turnAngle / 2) (SVG stroke-miterlimit).
                return cosHalf * max(meshDraw.miterLimit, 1.0) >= 1.0
                    ? LINE_END_MITER_JOIN
                    : LINE_END_BEVEL_JOIN;
            }
            return meshDraw.lineJoin == LINE_JOIN_BEVEL ? LINE_END_BEVEL_JOIN : LINE_END_ROUND_JOIN;
        }
    }
    return min(meshDraw.lineCap, LINE_END_ROUND);
}

// How far (px) the quad must reach beyond an endpoint, excluding the AA pad.
float endExtentPx(uint mode, vec2 u, vec2 v, float halfWidth) {
    if (mode == LINE_END_BUTT) {
        return 0.0;
    }
    if (mode == LINE_END_MITER_JOIN) {
        // The miter tip lies halfWidth * tan(turnAngle / 2) beyond the joint along u.
        return halfWidth * length(u - v) / max(length(u + v), LINE_EPSILON);
    }
    return halfWidth;
}

void main() {
    // gl_InstanceIndex = segment index within this draw (0 .. instanceCount-1)
    // gl_VertexIndex   = quad corner (0..3)
    vec2 corner = QUAD_UVS[gl_VertexIndex];

    // Task 2.2: segment endpoint fetch via gl_InstanceIndex (Req 1.4, 1.5).
    // Line-list layout: the vertex buffer holds DISJOINT 2-vertex segments
    // [A0,A1, B0,B1, ...]. Each instance expands one segment, so segment s occupies
    // the vertex pair (2s, 2s+1). instanceCount therefore equals lineCount (one quad instance per
    // segment), NOT lineCount*2.
    uint s = uint(gl_InstanceIndex);   // segment index within the draw
    uint n = meshDraw.lineCount;
    VertexBuffer vertices = VertexBuffer(meshInfo.vertexBufferAddress);
    vec4 P0 = vertices.value[2u * s];        // start endpoint (vec4 position)
    vec4 P1 = vertices.value[2u * s + 1u];   // end endpoint   (vec4 position)

    // Task 3.1: clip-space transform composition (Req 2.4).
    // clip = (viewProjection * nodeTransform) * vec4(P.xyz, 1.0).
    mat4 clipMVP = fpConst.viewProjection * nodeInfo.transform;
    precise vec4 clip0 = clipMVP * vec4(P0.xyz, 1.0);   // start endpoint in clip space
    precise vec4 clip1 = clipMVP * vec4(P1.xyz, 1.0);   // end endpoint   in clip space

    // Near-plane clipping (see clipNear). Position, color and clip coordinates are all re-derived
    // from tc0/tc1 so every attribute stays consistent.
    float tc0, tc1;
    clipNear(clip0, clip1, tc0, tc1);

    // Task 3.3: project to line pixel space via the guarded perspective divide (Req 10.1, 10.3)
    // and build the unit screen-space direction with a degenerate guard (Req 1.6, 2.5, 10.2).
    vec2 px0 = linePx(clip0);
    vec2 px1 = linePx(clip1);
    vec2 dirUnit;
    float segLenPx;
    bool segValid = unitDirPx(px0, px1, dirUnit, segLenPx);

    // Screen-space perpendicular: rotate the unit direction 90° -> (-y, x).
    vec2 perp = vec2(-dirUnit.y, dirUnit.x);

    float width0 = resolveWidthPx(clip0);
    float width1 = resolveWidthPx(clip1);
    float hw0 = width0 * 0.5;
    float hw1 = width1 * 0.5;

    // ------------------------------------------------------------------
    // Neighbour detection for joins. Segment s is connected to s-1 when the previous segment's
    // end vertex equals P0, and to s+1 when the next segment's start vertex equals P1 (exact
    // equality: polyline builders duplicate the shared vertex). Index wrap-around (first <-> last)
    // joins closed loops. A joint that was moved by near-plane clipping is never joined.
    // ------------------------------------------------------------------
    vec2 prevDir = dirUnit;   // previous segment direction (towards the start joint)
    vec2 nextDir = dirUnit;   // next segment direction (away from the end joint)
    bool startConnected = false;
    bool endConnected = false;
    if (meshDraw.lineJoin != LINE_JOIN_NONE && segValid && n > 1u) {
        bool wrap = n > 2u;
        if (tc0 == 0.0 && (s > 0u || wrap)) {
            uint p = (s > 0u ? s : n) - 1u;
            vec4 prevStart = vertices.value[2u * p];
            vec4 prevEnd = vertices.value[2u * p + 1u];
            if (prevEnd.xyz == P0.xyz) {
                startConnected = segmentDirPx(clipMVP, prevStart.xyz, prevEnd.xyz, prevDir);
            }
        }
        if (tc1 == 1.0 && (s + 1u < n || wrap)) {
            uint q = (s + 1u < n) ? s + 1u : 0u;
            vec4 nextStart = vertices.value[2u * q];
            vec4 nextEnd = vertices.value[2u * q + 1u];
            if (nextStart.xyz == P1.xyz) {
                endConnected = segmentDirPx(clipMVP, nextStart.xyz, nextEnd.xyz, nextDir);
            }
        }
    }

    // Outward directions at each end: u = out of this segment, v = into the neighbour (away from
    // the joint). For the start end the previous segment points TOWARDS the joint, so negate it.
    vec2 uStart = -dirUnit;
    vec2 vStart = -prevDir;
    vec2 uEnd = dirUnit;
    vec2 vEnd = nextDir;
    uint startMode = resolveEndMode(startConnected, uStart, vStart);
    uint endMode = resolveEndMode(endConnected, uEnd, vEnd);

    // Task 3.2: Z-pattern quad-corner expansion (Req 1.2, 1.3). corner.x selects the anchoring
    // endpoint (start when < 0), corner.y the side of the line.
    bool atStart = corner.x < 0.0;
    vec4 anchorClip  = atStart ? clip0 : clip1;   // anchored clip-space position
    // anchored OBJECT-space endpoint (-> world in 5.1), after near-plane clipping
    vec3 anchorWorld = atStart ? mix(P0.xyz, P1.xyz, tc0) : mix(P0.xyz, P1.xyz, tc1);
    float anchorHw = atStart ? hw0 : hw1;
    float width = atStart ? width0 : width1;
    float extentPx = atStart
        ? endExtentPx(startMode, uStart, vStart, hw0)
        : endExtentPx(endMode, uEnd, vEnd, hw1);

    // Offset the anchored corner along the segment by the cap/join extent and across it by the
    // half width, both padded so the 1px anti-aliased edge fits inside the quad. A zero-length
    // segment with butt caps has no area and is collapsed so it does not rasterize; with square or
    // round caps it renders as a dot like SVG.
    vec2 offsetPixels = dirUnit * corner.x * (extentPx + LINE_AA_PAD)
                      + perp * corner.y * (anchorHw + LINE_AA_PAD);
    if (!segValid && meshDraw.lineCap == LINE_END_BUTT) {
        offsetPixels = vec2(0.0);
    }

    // Convert the pixel offset to an NDC offset via screenDimensions, then back to clip space by
    // multiplying by w (Req 2.1, 2.2). Keep the real (unguarded) anchorClip.w as the homogeneous
    // anchor so that w <= 0 (at/behind near-plane) endpoints stay finite and are clipped against
    // the near plane by the rasterizer (Req 10.3).
    vec2 offsetNdc    = offsetPixels * 2.0 / fpConst.screenDimensions;
    vec4 positionClip = anchorClip;
    positionClip.xy  += offsetNdc * anchorClip.w;

    // UV: x = ±1 at the endpoints, extrapolated over the extension; y = ±1 at the nominal edge.
    float safeLenPx = max(segLenPx, LINE_EPSILON);
    vec2 uv = vec2(
        corner.x * (1.0 + 2.0 * (extentPx + LINE_AA_PAD) / safeLenPx),
        corner.y * (anchorHw + LINE_AA_PAD) / anchorHw);

    // Task 4.1: per-line / per-vertex color selection and interpolation setup
    // (Req 3.1, 3.2, 3.3).
    //
    // Resolve the two endpoint colors:
    //   - When MeshInfo.vertexColorBufferAddress != 0 (Req 3.1) the per-vertex colors are
    //     read from VertexColorBuffer at the segment's endpoint indices 2s and 2s+1 (the
    //     same disjoint line-list layout as the position buffer). Each component is already
    //     a normalized vec4 in [0,1].
    //   - Otherwise (Req 3.2) LineDraw.lineColor is a packed uint unpacked into a normalized
    //     RGBA vec4 via unpackUnorm4x8 (R = lowest byte, A = highest byte) and the single
    //     color is applied to both endpoints.
    vec4 startColor = unpackUnorm4x8(meshDraw.lineColor);
    vec4 endColor = startColor;   // default to the same color for both endpoints
    if (meshInfo.vertexColorBufferAddress != 0) {
        VertexColorBuffer vcolors = VertexColorBuffer(meshInfo.vertexColorBufferAddress);
        startColor *= vcolors.colors[2u * s];        // start endpoint color
        endColor   *= vcolors.colors[2u * s + 1u];   // end endpoint color
    }

    // Each corner emits its anchored endpoint color and the rasterizer interpolates across the
    // quad (Req 3.3). Colors follow the near-plane-clipped endpoints (tc0/tc1) so the gradient
    // stays consistent; cap/join extensions keep the endpoint color.
    vec4 clippedStartColor = mix(startColor, endColor, tc0);
    vec4 clippedEndColor   = mix(startColor, endColor, tc1);
    vec4 cornerColor = atStart ? clippedStartColor : clippedEndColor;

    // Task 5.1: entity-ID packing + hitable gating, world-position varying
    // (Req 6.1, 6.2, 7.1, 11.5).

    // World-space position of the per-fragment varying (Req 7.1): the anchored endpoint
    // transformed by the node transform, interpolated by the rasterizer across the quad.
    vec3 worldPos = (nodeInfo.transform * vec4(anchorWorld, 1.0)).xyz;

    // Entity-ID varying for GPU picking (Req 6.1, 6.2). Only meaningful under OUTPUT_DRAW_ID; the
    // packed object info is forced to all-zero when the draw is not hitable.
    uvec2 entityId;
#ifdef OUTPUT_DRAW_ID
    uvec2 packed = packObjectInfo(nodeInfo.worldId, nodeInfo.entityId, gl_InstanceIndex);
    entityId = uvec2(packed * uint(isHitable(meshDraw.drawType)));
#else
    entityId = uvec2(0u);
#endif

    // main() assigns gl_Position and EVERY declared v_* varying (Req 11.5).
    gl_Position    = positionClip;
    v_uv           = uv;
    v_color        = cornerColor;
    v_screenSize   = width;     // Line_Width in pixels: screen mode [1,64], world mode >= 1 (Req 2.3)
    v_entityId     = entityId;
    v_textureIndex = meshDraw.textureId;
    v_samplerIndex = meshDraw.samplerId;
    v_fragWorldPos = worldPos;
    v_segPx        = vec4(px0, px1);
    v_dirHw        = vec4(dirUnit, hw0, hw1);
    v_adjDir       = vec4(vStart, vEnd);
    v_endModes     = startMode | (endMode << 8);
}
