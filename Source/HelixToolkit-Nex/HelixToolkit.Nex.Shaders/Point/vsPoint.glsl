#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/MeshInfo.glsl"
#include "HxHeaders/NodeInfo.glsl"
#include "HxHeaders/HeaderPackEntity.glsl" // packObjectInfo() (Req 6.1)
#include "Point/PointStructs.glsl"
#include "HxHeaders/DrawTypeCheck.glsl"    // isHitable() (Req 6.1)

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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointDrawBuffer {
    PointDraw draws[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshInfoBuffer {
    MeshInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer NodeInfoBuffer {
    GpuNodeInfo value[];
};

layout(push_constant) uniform PC {
    PointRenderPC value;
} pc;

// ------------------------------------------------------------------
// PER-DRAW DATA RESOLUTION
// ------------------------------------------------------------------
FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

// Read this draw's LineDraw record at gl_DrawID + drawCommandIdxOffset (Req 9.5).
uint drawIndex = gl_DrawID + pc.value.drawCommandIdxOffset;

PointDraw meshDraw = PointDrawBuffer(pc.value.meshDrawBufferAddress).draws[drawIndex];

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
// DEGENERATE-POINT GUARDS (Task 2.14 — Requirement 7)
// ------------------------------------------------------------------
// POINT_EPSILON is the smallest magnitude treated as non-zero; every divisor in this shader
// (the distance-scaled size divisor and the screenDimensions pixel→NDC divisor) is guarded to a
// magnitude >= POINT_EPSILON so no division ever uses a near-zero divisor and all four quad
// vertices stay finite (no NaN/Inf) (Req 7.2). guardW substitutes a sign-preserving ±POINT_EPSILON
// for any |w| < POINT_EPSILON, mirroring LineShaderMirror.GuardW exactly so the CPU mirror and the
// GLSL agree. The real (unguarded) clipCenter.w is still used as the homogeneous anchor for the
// final clip position so points at or behind the near plane (w <= 0) stay finite and are clipped by
// the rasterizer rather than producing undefined output (Req 7.1).
const float POINT_EPSILON = 1e-6;

// Sign-preserving guard for a value used as a divisor: clamps the magnitude to >= POINT_EPSILON
// while keeping the original sign. Mirrors LineShaderMirror.GuardW.
float guardW(float w) {
    if (abs(w) < POINT_EPSILON) {
        return w < 0.0 ? -POINT_EPSILON : POINT_EPSILON;
    }
    return w;
}

void main() {
    // gl_InstanceIndex = point index within this draw (0 .. instanceCount-1)
    // gl_VertexIndex   = quad corner (0..3)
    // Task 2.1: Z-pattern quad-corner selection (Req 1.2, 1.3). The four QUAD_UVS
    // corners form one triangle-strip quad in Z-pattern order
    // (v0 BL, v1 BR, v3 TL, v2 TR).
    vec2 corner = QUAD_UVS[gl_VertexIndex];

    // Task 2.1: single-vertex point fetch via gl_InstanceIndex (Req 1.4, 1.5).
    // Point layout: the vertex buffer holds ONE vertex per point [P0, P1, P2, ...].
    // Each instance expands one point, so point s occupies the SINGLE vertex at index
    // s — NOT a 2s / 2s+1 endpoint pair (that is the line-list layout). instanceCount
    // therefore equals pointCount (one quad instance per point), and firstInstance = 0
    // (the point index is surfaced via gl_InstanceIndex, not firstInstance).
    uint s = uint(gl_InstanceIndex);   // point index within the draw
    VertexBuffer vertices = VertexBuffer(meshInfo.vertexBufferAddress);
    vec4 P = vertices.value[s];         // single point position (vec4)

    // Task 2.9: color resolution and uniform corner color (Req 3.1, 3.2, 3.3).
    // Resolve a SINGLE point color:
    //   - When MeshInfo.vertexColorBufferAddress != 0 (Req 3.1) read the per-vertex color from
    //     VertexColorBuffer at the SINGLE point index s (not 2s / 2s+1 — points use the one-vertex-
    //     per-point layout, mirroring the position buffer). Each component is already a normalized
    //     vec4 in [0,1].
    //   - Otherwise (Req 3.2) PointDraw.pointColor is a packed uint unpacked into a normalized RGBA
    //     vec4 via unpackUnorm4x8 (R = lowest byte, A = highest byte).
    // Unlike vsLine.glsl (which anchors two endpoint colors and lets the rasterizer interpolate),
    // a point has ONE color emitted identically to all four corners, so v_color is constant across
    // the quad with no interpolation (Req 3.3).
    vec4 color = unpackUnorm4x8(meshDraw.pointColor);  // packed uniform point color
    if (meshInfo.vertexColorBufferAddress != 0) {
        VertexColorBuffer vcolors = VertexColorBuffer(meshInfo.vertexColorBufferAddress);
        color *= vcolors.colors[s];                  // per-vertex point color
    }

    // Task 2.1: clip-space transform composition (Req 8.5, 8.6, 9.1).
    // Compose the model-view-projection as viewProjection * nodeTransform, then
    // transform the single point position into clip space. This mirrors vsLine.glsl's
    // clipMVP composition but for the one centered point position.
    mat4 clipMVP = fpConst.viewProjection * nodeInfo.transform;
    vec4 clipCenter = clipMVP * vec4(P.xyz, 1.0);   // projected point center in clip space

    // Task 2.1: centered billboard quad expansion (Req 1.2, 1.3, 1.6, 2.1, 2.2).
    // Unlike a line (which offsets PERPENDICULAR to a segment direction), a point's four
    // corners sit SYMMETRICALLY around the one projected center, offset by
    // corner.xy * halfSizePx in BOTH screen axes — no segment-direction / perpendicular
    // computation. The pixel offset is converted to an NDC offset via screenDimensions and
    // then back to clip space by multiplying by clipCenter.w (so the perspective divide in
    // the rasterizer yields the intended pixel offset):
    //   offsetPixels = corner.xy * halfSizePx
    //   offsetNdc    = offsetPixels * 2 / screenDimensions
    //   clip.xy     += offsetNdc * clip.w
    //
    // Task 2.5: point-size resolution — fixed-pixel + distance-scaled branches (Req 2.3-2.6).
    // fixedSize != 0 : pointSize is already a screen-pixel diameter, constant with camera
    //                  distance (Req 2.4).
    // fixedSize == 0 : pointSize is a world-space diameter projected to screen pixels (Req 2.5).
    //                  FPConstants exposes no standalone projection matrix, so recover it as
    //                  projection = viewProjection * inverseView (since viewProjection =
    //                  projection * view). Its [1][1] element is the vertical projection scale
    //                  1/tan(fovY/2). A world diameter D at guarded clip depth w projects to
    //                  D * projScaleY * screenDimensions.y / (2 * w) pixels, so screen size
    //                  shrinks as the point recedes. guardW keeps the divisor magnitude >= 1e-6 so
    //                  the distance-scaled divide stays finite (Task 2.14, Req 7.2).
    float pointSizePx;
    if (meshDraw.fixedSize != 0u) {
        pointSizePx = meshDraw.pointSize;
    } else {
        mat4 projection = fpConst.viewProjection * fpConst.inverseView;
        float projScaleY = projection[1][1];
        float w = guardW(clipCenter.w);
        pointSizePx = meshDraw.pointSize * projScaleY * fpConst.screenDimensions.y / (2.0 * w);
    }

    // Clamp the resolved screen-pixel diameter to [1.0, 64.0] (Req 2.3) and derive the half-size
    // used for the centered corner offset.
    //
    // Task 2.14 (Req 7.3): capture whether the resolved size is non-positive BEFORE the clamp so a
    // non-positive size collapses the Point_Quad to zero screen-space area (quadAreaScale = 0.0) and
    // rasterizes nothing, without producing NaN/Inf. The clamp below floors the size at 1.0, so a
    // true <= 0 size is unreachable from the shader's own math; this guard is retained defensively
    // for any upstream path that forces a non-positive size.
    float quadAreaScale = pointSizePx > 0.0 ? 1.0 : 0.0;

    pointSizePx = clamp(pointSizePx, 1.0, 64.0);
    float halfSizePx = pointSizePx * 0.5;

    // Keep the real (unguarded) clipCenter.w as the homogeneous anchor so points at/behind the
    // near plane (w <= 0) stay finite and are clipped by the rasterizer (Req 7.1).
    //
    // Task 2.14 (Req 7.2): guard the screenDimensions divisor to magnitude >= POINT_EPSILON
    // (sign-preserving via guardW) so the pixel->NDC conversion never divides by a near-zero
    // component and produces only finite output for all four quad vertices.
    // Task 2.14 (Req 7.3): scale the corner offset by quadAreaScale so a non-positive resolved size
    // collapses the quad to zero screen-space area.
    vec2 guardedScreen = vec2(guardW(fpConst.screenDimensions.x), guardW(fpConst.screenDimensions.y));
    vec2 offsetPixels  = corner.xy * halfSizePx * quadAreaScale;
    vec2 offsetNdc     = offsetPixels * 2.0 / guardedScreen;

    // The offset back-conversion to clip space MULTIPLIES by the unguarded clipCenter.w (the
    // homogeneous anchor) — a multiply, not a divide, so it stays finite even at w = 0 (Req 7.1).
    vec4 positionClip = clipCenter;
    positionClip.xy  += offsetNdc * clipCenter.w;

    // Task 2.11: world-position varying, entity-ID packing + hitable gating, texture/sampler
    // forwarding (Req 5.1, 5.2, 5.4, 6.1, 9.6).

    // World-space position of the per-fragment varying (Req 5.1). P.xyz is the single OBJECT-space
    // point position; transform it to world space as nodeTransform * vec4(P.xyz, 1.0). Mirrors
    // vsLine.glsl's worldPos derivation but for the one centered point (no atStart anchor — a point
    // has a single position, so all four corners emit the same world position and the rasterizer
    // interpolates a constant).
    vec3 worldPos = (nodeInfo.transform * vec4(P.xyz, 1.0)).xyz;

    // Entity-ID varying for GPU picking (Req 6.1). Only meaningful under OUTPUT_DRAW_ID; the packed
    // object info is forced to all-zero when the draw is not hitable. Mirrors vsLine.glsl exactly:
    // packObjectInfo returns a uvec2 that is multiplied by uint(isHitable(...)) so a non-hitable
    // draw yields uvec2(0u). Resolved from the owning node (nodeInfo.worldId / nodeInfo.entityId,
    // Req 9.6) and the per-point instance index.
    uvec2 entityId;
#ifdef OUTPUT_DRAW_ID
    uvec2 packed = packObjectInfo(nodeInfo.worldId, nodeInfo.entityId, gl_InstanceIndex);
    entityId = uvec2(packed * uint(isHitable(meshDraw.drawType)));
#else
    entityId = uvec2(0u);
#endif

    // main() assigns gl_Position, v_uv, v_screenSize, v_color, and the Task 2.11 varyings
    // (v_entityId, v_textureIndex, v_samplerIndex, v_fragWorldPos) — every declared output (Req 5.4).
    gl_Position  = positionClip;
    v_uv         = corner;
    // Task 2.5: emit the resolved, clamped screen-pixel diameter for the fragment circular SDF
    // edge-width (Req 2.6).
    v_screenSize = pointSizePx;
    // Task 2.9: emit the single resolved color identically to all four corners — the rasterizer
    // receives a constant v_color across the quad (no interpolation), satisfying Req 3.3.
    v_color      = color;
    // Task 2.11: entity-id (gated), bindless texture/sampler indices forwarded from PointDraw
    // (Req 5.2, 6.1), and the per-fragment world position (Req 5.1).
    v_entityId     = entityId;
    v_textureIndex = meshDraw.textureId;
    v_samplerIndex = meshDraw.samplerId;
    v_fragWorldPos = worldPos;
}
