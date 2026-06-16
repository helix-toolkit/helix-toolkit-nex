#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/MeshInfo.glsl"
#include "HxHeaders/NodeInfo.glsl"
#include "HxHeaders/HeaderPackEntity.glsl" // packObjectInfo() (Req 6.1)
#include "Line/LineStructs.glsl"
#include "HxHeaders/DrawTypeCheck.glsl"
// ------------------------------------------------------------------
// VARYINGS (must match psLineTemplate.glsl exactly: location, type, flat)
// ------------------------------------------------------------------
layout(location = 0) out vec2  v_uv;            // Quad_Corner_UV in [-1,1]
layout(location = 1) out vec4  v_color;         // interpolated RGBA endpoint color
layout(location = 2) out float v_screenSize;    // clamped Line_Width in pixels
layout(location = 3) out flat uvec2  v_entityId;       // packed GPU-picking entity id
layout(location = 4) out flat uint  v_textureIndex;   // bindless texture index (0 = none)
layout(location = 5) out flat uint  v_samplerIndex;   // bindless sampler index
layout(location = 6) out vec3  v_fragWorldPos;   // per-fragment world-space position

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
// Smallest magnitude treated as non-zero. Matches LineShaderMirror.Epsilon and every 1e-6
// guard in the CPU mirror.
const float LINE_EPSILON = 1e-6;

// Guard a clip-space w against a divisor smaller than LINE_EPSILON before the perspective
// divide, preserving its sign. Mirrors LineShaderMirror.GuardW exactly (Req 10.1): the NDC
// divide never uses a divisor < 1e-6, so no NaN/Inf is produced. The real (unguarded) w is
// still used as the homogeneous anchor for w <= 0 (at/behind near-plane) endpoints so the
// rasterizer clips them against the near plane (Req 10.3).
float guardW(float w) {
    if (abs(w) < LINE_EPSILON) {
        return w < 0.0 ? -LINE_EPSILON : LINE_EPSILON;
    }
    return w;
}

// Project a clip-space position to NDC xy via the guarded perspective divide. Mirrors
// LineShaderMirror.NdcXY exactly.
vec2 ndcXY(vec4 clip) {
    float w = guardW(clip.w);
    return clip.xy / w;
}

void main() {
    // gl_InstanceIndex = segment index within this draw (0 .. instanceCount-1)
    // gl_VertexIndex   = quad corner (0..3)
    vec2 corner = QUAD_UVS[gl_VertexIndex];

    // Task 2.2: segment endpoint fetch via gl_InstanceIndex (Req 1.4, 1.5).
    // Line-list layout: the vertex buffer holds DISJOINT 2-vertex segments
    // [A0,A1, B0,B1, ...]. Each instance expands one segment, so segment s occupies
    // the vertex pair (2s, 2s+1) — no shared or cross-line vertices. instanceCount
    // therefore equals lineCount (one quad instance per segment), NOT lineCount*2.
    uint s = uint(gl_InstanceIndex);   // segment index within the draw
    VertexBuffer vertices = VertexBuffer(meshInfo.vertexBufferAddress);
    vec4 P0 = vertices.value[2u * s];        // start endpoint (vec4 position)
    vec4 P1 = vertices.value[2u * s + 1u];   // end endpoint   (vec4 position)

    // P0/P1 are made available for the subsequent transform / expansion tasks
    // (3.x). Their .xyz components carry the world-space endpoint positions.

    // Task 3.1: clip-space transform composition (Req 2.4).
    // Compose the model-view-projection as viewProjection * nodeTransform, then
    // transform both endpoint positions. This mirrors LineShaderMirror.TransformToClip
    // exactly: clip = (viewProjection * nodeTransform) * vec4(P.xyz, 1.0).
    mat4 clipMVP = fpConst.viewProjection * nodeInfo.transform;
    vec4 clip0 = clipMVP * vec4(P0.xyz, 1.0);   // start endpoint in clip space
    vec4 clip1 = clipMVP * vec4(P1.xyz, 1.0);   // end endpoint   in clip space

    // clip0/clip1 are consumed by the subsequent expansion tasks (3.2 quad-corner
    // selection, 3.3 perpendicular screen-space offset, 3.4 degenerate handling).

    // Task 3.2: Z-pattern quad-corner expansion (Req 1.2, 1.3).
    // The four QUAD_UVS corners form one triangle-strip quad. The corner's x
    // component selects which endpoint the corner is anchored to: the START
    // endpoint (clip0 / P0) when corner.x < 0, otherwise the END endpoint
    // (clip1 / P1). This mirrors LineShaderMirror.ExpandCorner exactly
    // (uv.X < 0 -> clip0/p0, else clip1/p1).
    bool atStart = corner.x < 0.0;
    vec4 anchorClip  = atStart ? clip0 : clip1;   // anchored clip-space position
    vec3 anchorWorld = atStart ? P0.xyz : P1.xyz; // anchored OBJECT-space endpoint (-> world in 5.1)

    // Task 3.3: perpendicular screen-space offset and width clamp (Req 2.1, 2.2, 2.3).
    // Mirrors LineShaderMirror.ExpandCorner: project both endpoints to NDC, build the
    // unit screen-space (pixel) direction of the segment, rotate it 90° to get the
    // perpendicular, clamp the width, and offset the anchored corner along ±perp by half
    // the width. screenDimensions converts between pixel units and NDC.

    // Project to NDC via the guarded perspective divide. ndcXY/guardW (Task 3.4) substitute a
    // sign-preserving 1e-6 for any |w| < 1e-6 so the divide never produces NaN/Inf, including for
    // endpoints with w <= 0 at or behind the near plane (Req 10.1, 10.3). Mirrors
    // LineShaderMirror.NdcXY / GuardW.
    vec2 ndc0 = ndcXY(clip0);
    vec2 ndc1 = ndcXY(clip1);

    // dirPixels = (ndc1 - ndc0) * 0.5 * screenDimensions  (pixel-space segment direction).
    vec2 dirPixels = (ndc1 - ndc0) * 0.5 * fpConst.screenDimensions;

    // Unit screen-space direction with a degenerate guard (Task 3.4 — Req 1.6, 2.5, 10.1, 10.2).
    // When the projected pixel length is <= 1e-6 (coincident endpoints or a zero-length projected
    // segment) a fixed default unit direction (1, 0) is substituted instead of dividing by a
    // divisor < 1e-6. This keeps dirUnit (and therefore every quad vertex) finite, and — because
    // the substituted ndc0/ndc1 then coincide — collapses the emitted quad to zero screen-space
    // area so it does not rasterize. Mirrors LineShaderMirror.ProjectedDirectionUnitPixels exactly.
    float dirLen = length(dirPixels);
    vec2 dirUnit = (dirLen <= LINE_EPSILON) ? vec2(1.0, 0.0) : dirPixels / dirLen;

    // Screen-space perpendicular: rotate the unit direction 90° -> (-y, x).
    vec2 perp = vec2(-dirUnit.y, dirUnit.x);

    // Clamp Line_Width to [1.0, 64.0] px (Req 2.3).
    float width = clamp(meshDraw.lineWidth, 1.0, 64.0);

    // Offset the anchored corner perpendicular to the segment by half the width, scaled by
    // the corner's cross-segment component. Convert the pixel offset to an NDC offset via
    // screenDimensions, then back to clip space by multiplying by w (Req 2.1, 2.2):
    //   offsetPixels = perp * (width * 0.5) * corner.y
    //   offsetNdc    = offsetPixels * 2 / screenDimensions
    //   clip.xy     += offsetNdc * clip.w
    vec2 offsetPixels = perp * (width * 0.5) * corner.y;
    vec2 offsetNdc    = offsetPixels * 2.0 / fpConst.screenDimensions;

    // Offset in clip space: keep the real (unguarded) anchorClip.w as the homogeneous anchor so
    // that w <= 0 (at/behind near-plane) endpoints stay finite and are clipped against the near
    // plane by the rasterizer (Req 10.3). Mirrors LineShaderMirror.ExpandCorner.
    vec4 positionClip = anchorClip;
    positionClip.xy  += offsetNdc * anchorClip.w;

    // Task 4.1: per-line / per-vertex color selection and interpolation setup
    // (Req 3.1, 3.2, 3.3).
    //
    // Resolve the two endpoint colors:
    //   - When MeshInfo.vertexColorBufferAddress != 0 (Req 3.1) the per-vertex colors are
    //     read from VertexColorBuffer at the segment's endpoint indices 2s and 2s+1 (the
    //     same disjoint line-list layout as the position buffer). Each component is already
    //     a normalized vec4 in [0,1].
    //   - Otherwise (Req 3.2) LineDraw.lineColor is a packed uint unpacked into a normalized
    //     RGBA vec4 via unpackUnorm4x8 — this matches LineShaderMirror.UnpackColor exactly
    //     (R = lowest byte, A = highest byte) — and the single color is applied to both
    //     endpoints.
    vec4 startColor = unpackUnorm4x8(meshDraw.lineColor);
    vec4 endColor = startColor;   // default to the same color for both endpoints
    if (meshInfo.vertexColorBufferAddress != 0) {
        VertexColorBuffer vcolors = VertexColorBuffer(meshInfo.vertexColorBufferAddress);
        startColor *= vcolors.colors[2u * s];        // start endpoint color
        endColor   *= vcolors.colors[2u * s + 1u];   // end endpoint color
    }

    // Set up v_color so the fragment receives mix(startColor, endColor, t) with
    // t = clamp(v_uv.x * 0.5 + 0.5, 0, 1) (Req 3.3). v_uv.x is the along-segment axis: -1 at
    // the start endpoint, +1 at the end endpoint, so t = 0 at the start and t = 1 at the end.
    // Because the rasterizer interpolates v_color linearly across the quad, emitting each
    // corner's anchored endpoint color (start corners -> startColor, end corners -> endColor)
    // reproduces (1 - t) * startColor + t * endColor automatically — matching
    // LineShaderMirror.InterpolateColor / ColorParameterFromUV (Property 9). No explicit mix
    // is needed in the vertex shader. atStart was selected above from corner.x < 0.
    vec4 cornerColor = atStart ? startColor : endColor;

    // Task 5.1: entity-ID packing + hitable gating, world-position varying
    // (Req 6.1, 6.2, 7.1, 11.5).

    // World-space position of the per-fragment varying (Req 7.1). anchorWorld is the
    // OBJECT-space anchored endpoint (P0.xyz when atStart, else P1.xyz); transform it to world
    // space as nodeTransform * vec4(P, 1.0). Mirrors LineShaderMirror.TransformToWorld exactly.
    // Emitting each corner's anchored endpoint world position lets the rasterizer interpolate the
    // world position across the quad.
    vec3 worldPos = (nodeInfo.transform * vec4(anchorWorld, 1.0)).xyz;

    // Entity-ID varying for GPU picking (Req 6.1, 6.2). Only meaningful under OUTPUT_DRAW_ID; the
    // packed object info is forced to all-zero when the draw is not hitable. Mirrors
    // LineShaderMirror.EntityIdVarying exactly: vec2(packObjectInfo(...) * uint(isHitable(...))).
    // packObjectInfo returns a uvec2 that is numerically converted to the flat vec2 varying.
    uvec2 entityId;
#ifdef OUTPUT_DRAW_ID
    uvec2 packed = packObjectInfo(nodeInfo.worldId, nodeInfo.entityId, gl_InstanceIndex);
    entityId = uvec2(packed * uint(isHitable(meshDraw.drawType)));
#else
    entityId = uvec2(0u);
#endif

    // Texture / sampler indices (Req 8 source). Neither LineDraw (LineStructs.glsl), MeshInfo, nor
    // the line material structs currently carry a bindless texture/sampler index, so there is no
    // per-draw texture source to surface. They are emitted as 0u meaning "no texture", which the
    // fragment shader treats as the no-texture path (v_textureIndex == 0 -> use interpolated vertex
    // color, Req 8.2). When a per-draw / per-material texture index field is added, source
    // v_textureIndex / v_samplerIndex from it here.

    // main() assigns gl_Position and EVERY declared v_* varying (Req 11.5).
    gl_Position    = positionClip;
    v_uv           = corner;
    v_color        = cornerColor;
    v_screenSize   = width;     // clamped Line_Width in pixels (Req 2.3)
    v_entityId     = entityId;
    v_textureIndex = meshDraw.textureId;
    v_samplerIndex = meshDraw.samplerId;
    v_fragWorldPos = worldPos;
}
