#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

// Procedural solid-handle vertex shader for gizmo rendering.
//
// Mirrors the push-constant pattern used by vsBoundingBox.glsl (an FPConstants
// buffer address plus a per-draw payload). One procedural draw is issued per
// solid handle by GizmoRenderNode; the vertex shader generates the handle
// geometry from gl_VertexIndex, applies constant-screen-size scaling, and
// transforms it into clip space.
//
// The push-constant block below MUST match the byte layout of the C# struct
// HelixToolkit.Nex.Rendering.Gizmos.GizmoPushConstant. That struct is authored
// by hand (it is NOT @code_gen) and is arranged for the std430 push-constant
// layout used here: the mat4 comes first so it is 16-byte aligned, and the
// 8-byte forward-plus buffer address comes last after an explicit pad word.
//
// This block is hand-authored and kept in lock-step with the C# struct; it is
// deliberately NOT @code_gen (the C# GizmoPushConstant, Pack=16, is the single
// hand-authored source of truth). encodedR / encodedG replace the previous
// single entityId word: they carry the pre-packed pick words (encoding-type
// discriminator + owning entity id in R, packed GizmoHandleId in G) that the
// fragment shader writes straight into the RG_F32 entity-id target (Req 4.2, 4.4).
@code_gen
struct GizmoPushConstant {
    mat4 modelTransform;    // gizmo origin * handle local transform (world placement)
    vec4 color;             // handle display color
    uint encodedR;          // pre-packed pick word R (encoding-type + owning entity id)
    uint encodedG;          // pre-packed pick word G (packed GizmoHandleId: mode + axis)
    float screenScale;      // world units per pixel at the gizmo origin (Req 2.4)
    uint shapeFlags;        // packed shape id (low 8 bits) + occlusion bits
    uint _padding0;         // pad so fpConstAddress is 8-byte aligned
    uint64_t fpConstAddress;
};

layout(push_constant) uniform PC {
    GizmoPushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

// Passed flat to the fragment shader: colour plus the two pre-packed pick words
// (R and G channels) that the fragment stage writes into the RG_F32 entity-id
// target for gizmo picking (Req 4.2, 4.4).
layout(location = 0) out flat vec4 v_color;
layout(location = 1) out flat uint v_encodedR;
layout(location = 2) out flat uint v_encodedG;

// Shape ids match the C# GizmoHandleShape enum ordering.
const uint SHAPE_ARROW = 0u;
const uint SHAPE_RING  = 1u;
const uint SHAPE_BOX   = 2u;
const uint SHAPE_PLANE = 3u;
const uint SHAPE_LINE  = 4u;

// Unit box: 12 triangles (36 vertices) referencing the 8 corners of a cube that
// spans [-0.5, 0.5] on every axis. Corner index bits: bit0 = X, bit1 = Y, bit2 = Z.
const uint BOX_INDICES[36] = uint[36](
    // -Z face
    0u, 2u, 1u,  1u, 2u, 3u,
    // +Z face
    4u, 5u, 6u,  5u, 7u, 6u,
    // -Y face
    0u, 1u, 4u,  1u, 5u, 4u,
    // +Y face
    2u, 6u, 3u,  3u, 6u, 7u,
    // -X face
    0u, 4u, 2u,  2u, 4u, 6u,
    // +X face
    1u, 3u, 5u,  3u, 7u, 5u
);

// Unit quad in the local XY plane: 2 triangles (6 vertices), spanning [-0.5, 0.5].
const vec3 PLANE_VERTS[6] = vec3[6](
    vec3(-0.5, -0.5, 0.0), vec3( 0.5, -0.5, 0.0), vec3(-0.5,  0.5, 0.0),
    vec3( 0.5, -0.5, 0.0), vec3( 0.5,  0.5, 0.0), vec3(-0.5,  0.5, 0.0)
);

// -------------------------------------------------------------------------
// Ring/line handle geometry (rotate-mode rings, plus general line handles).
//
// The solid pipeline uses a triangle topology, so line/ring handles are
// rasterized as thin, filled triangle bands rather than true GL lines. This
// keeps a single pipeline for every solid, ring, and line handle while still
// writing colour + entity id at every covered pixel so picking works.
//
// A ring is authored in the local XY plane (normal +Z); the handle's model
// transform rotates that plane so its normal aligns with the rotation axis
// (see GizmoModelBuilder.AppendRingHandle). The band is a flat annulus swept
// over RING_SEGMENTS segments, each segment a quad (6 vertices) between the
// inner and outer radius. RING_VERTEX_COUNT MUST equal RING_SEGMENTS * 6 and
// MUST match GizmoSolidHandlePipeline.RingVertexCount.
//
// A line is authored as a thin quad along the local +X axis spanning x in
// [0, 1] (canonical unit length, matching the arrow/line degeneracy check),
// with a fixed half-width in y. LINE_VERTEX_COUNT MUST match
// GizmoSolidHandlePipeline.LineVertexCount.
const uint  RING_SEGMENTS    = 64u;
const float RING_RADIUS      = 0.5;    // canonical outer radius before ScreenScale
const float RING_HALF_WIDTH  = 0.02;   // half the radial band thickness
const float LINE_HALF_WIDTH  = 0.02;   // half the line quad thickness (local units)
const float TWO_PI           = 6.28318530717958647692;

// Arrow (translate) and scale-handle geometry. Handles are authored extending from the gizmo
// origin along local +X to x = 1.0, so ScreenScale (applied in main) sizes both the handle
// length and its distance from the origin uniformly. CONE_SIDES controls the arrow-head fidelity.
const uint  CONE_SIDES        = 16u;   // radial segments of the arrow-head cone
const float ARROW_SHAFT_LEN   = 0.75;  // arrow shaft length along +X
const float ARROW_SHAFT_HW    = 0.02;  // arrow shaft half-width
const float ARROW_HEAD_BASE   = 0.70;  // arrow-head cone base position along +X
const float ARROW_HEAD_RADIUS = 0.08;  // arrow-head cone base radius
const float SCALE_SHAFT_LEN   = 0.82;  // scale-handle shaft length along +X
const float SCALE_SHAFT_HW    = 0.02;  // scale-handle shaft half-width
const float SCALE_KNOB_MIN    = 0.80;  // scale-handle end-cube min x
const float SCALE_KNOB_MAX    = 1.00;  // scale-handle end-cube max x
const float SCALE_KNOB_HW     = 0.09;  // scale-handle end-cube half-width

vec3 boxCorner(uint cornerIdx) {
    return vec3(
        ((cornerIdx & 1u) != 0u) ?  0.5 : -0.5,
        ((cornerIdx & 2u) != 0u) ?  0.5 : -0.5,
        ((cornerIdx & 4u) != 0u) ?  0.5 : -0.5
    );
}

// Generates one vertex of the flat annulus ring band from a linear vertex index.
// Six vertices per segment form two triangles between the inner and outer radius.
vec3 ringVertex(uint vertexIndex) {
    uint segment   = (vertexIndex / 6u) % RING_SEGMENTS;
    uint corner    = vertexIndex % 6u;

    float a0 = TWO_PI * float(segment)        / float(RING_SEGMENTS);
    float a1 = TWO_PI * float(segment + 1u)   / float(RING_SEGMENTS);

    float rInner = RING_RADIUS - RING_HALF_WIDTH;
    float rOuter = RING_RADIUS + RING_HALF_WIDTH;

    // Quad corners: (a0,inner)=0, (a0,outer)=1, (a1,inner)=2, (a1,outer)=3.
    // Triangles: 0,1,2 and 2,1,3.
    float ang;
    float rad;
    if (corner == 0u)      { ang = a0; rad = rInner; }
    else if (corner == 1u) { ang = a0; rad = rOuter; }
    else if (corner == 2u) { ang = a1; rad = rInner; }
    else if (corner == 3u) { ang = a1; rad = rInner; }
    else if (corner == 4u) { ang = a0; rad = rOuter; }
    else                   { ang = a1; rad = rOuter; }

    return vec3(cos(ang) * rad, sin(ang) * rad, 0.0);
}

// Generates one vertex of a thin line quad along local +X (x in [0, 1]).
vec3 lineVertex(uint vertexIndex) {
    // Quad corners: (0,-w)=0, (1,-w)=1, (0,+w)=2, (1,+w)=3.
    // Triangles: 0,1,2 and 2,1,3.
    uint corner = vertexIndex % 6u;
    float x;
    float y;
    if (corner == 0u)      { x = 0.0; y = -LINE_HALF_WIDTH; }
    else if (corner == 1u) { x = 1.0; y = -LINE_HALF_WIDTH; }
    else if (corner == 2u) { x = 0.0; y =  LINE_HALF_WIDTH; }
    else if (corner == 3u) { x = 0.0; y =  LINE_HALF_WIDTH; }
    else if (corner == 4u) { x = 1.0; y = -LINE_HALF_WIDTH; }
    else                   { x = 1.0; y =  LINE_HALF_WIDTH; }
    return vec3(x, y, 0.0);
}

// Returns one of the 36 vertices (via BOX_INDICES) of an axis-aligned box spanning x in [x0, x1]
// and y,z in [-hw, hw]. Used for arrow/scale shafts and the scale-handle end cube.
vec3 boxVertexRange(uint corner, float x0, float x1, float hw) {
    uint ci = BOX_INDICES[corner % 36u];
    float x = ((ci & 1u) != 0u) ? x1 : x0;
    float y = ((ci & 2u) != 0u) ?  hw : -hw;
    float z = ((ci & 4u) != 0u) ?  hw : -hw;
    return vec3(x, y, z);
}

// Generates one vertex of a cone whose base ring (radius r) sits at x = xb and whose tip is at
// x = xt, swept over CONE_SIDES segments. Layout: [0, CONE_SIDES*3) side triangles, then
// [CONE_SIDES*3, CONE_SIDES*6) the base cap. Total CONE_SIDES*6 vertices. (CullMode.None, so
// winding is irrelevant.)
vec3 coneVertex(uint idx, float xb, float xt, float r) {
    uint sideCount = CONE_SIDES * 3u;
    if (idx < sideCount) {
        uint s = idx / 3u;
        uint c = idx % 3u;
        float a0 = TWO_PI * float(s)      / float(CONE_SIDES);
        float a1 = TWO_PI * float(s + 1u) / float(CONE_SIDES);
        if (c == 0u) return vec3(xb, r * cos(a0), r * sin(a0));
        if (c == 1u) return vec3(xb, r * cos(a1), r * sin(a1));
        return vec3(xt, 0.0, 0.0);                                 // tip
    }
    uint j = idx - sideCount;
    uint s2 = j / 3u;
    uint c2 = j % 3u;
    float b0 = TWO_PI * float(s2)      / float(CONE_SIDES);
    float b1 = TWO_PI * float(s2 + 1u) / float(CONE_SIDES);
    if (c2 == 0u) return vec3(xb, 0.0, 0.0);                       // base center
    if (c2 == 1u) return vec3(xb, r * cos(b1), r * sin(b1));
    return vec3(xb, r * cos(b0), r * sin(b0));
}

// Arrow (translate handle): thin box shaft [0, ARROW_SHAFT_LEN] + cone head to x = 1.0.
// Vertices: 36 (shaft) + CONE_SIDES*6 (head). Must match GizmoSolidHandlePipeline.ArrowVertexCount.
vec3 arrowVertex(uint idx) {
    if (idx < 36u) {
        return boxVertexRange(idx, 0.0, ARROW_SHAFT_LEN, ARROW_SHAFT_HW);
    }
    return coneVertex(idx - 36u, ARROW_HEAD_BASE, 1.0, ARROW_HEAD_RADIUS);
}

// Scale handle: thin box shaft [0, SCALE_SHAFT_LEN] + a small end cube (knob).
// Vertices: 36 (shaft) + 36 (knob). Must match GizmoSolidHandlePipeline.ScaleVertexCount.
vec3 scaleVertex(uint idx) {
    if (idx < 36u) {
        return boxVertexRange(idx, 0.0, SCALE_SHAFT_LEN, SCALE_SHAFT_HW);
    }
    return boxVertexRange(idx - 36u, SCALE_KNOB_MIN, SCALE_KNOB_MAX, SCALE_KNOB_HW);
}

void main() {
    FPBuffer fpBuf = FPBuffer(pc.value.fpConstAddress);

    uint shapeId = pc.value.shapeFlags & 0xFFu;

    // Select the local-space handle geometry from the shape id. Arrow emits a shaft + cone-head,
    // Box (scale handle) emits a shaft + end cube, Ring emits a flat annulus band, Plane a unit
    // quad, Line a thin quad along +X, and any unrecognized shape falls back to the unit cube.
    uint vid = uint(gl_VertexIndex);
    vec3 localPos;
    if (shapeId == SHAPE_ARROW) {
        localPos = arrowVertex(vid);
    } else if (shapeId == SHAPE_BOX) {
        localPos = scaleVertex(vid);
    } else if (shapeId == SHAPE_RING) {
        localPos = ringVertex(vid);
    } else if (shapeId == SHAPE_PLANE) {
        localPos = PLANE_VERTS[vid % 6u];
    } else if (shapeId == SHAPE_LINE) {
        localPos = lineVertex(vid);
    } else {
        localPos = boxCorner(BOX_INDICES[vid % 36u]);
    }

    // Constant on-screen size (Req 2.4): the geometry is authored in gizmo-local
    // space and scaled here by ScreenScale (world units per pixel at the gizmo
    // origin) rather than baking the scale into the emitted geometry.
    vec3 scaledLocal = localPos * pc.value.screenScale;

    // Model placement then camera projection using the forward-plus constants.
    vec4 worldPos = pc.value.modelTransform * vec4(scaledLocal, 1.0);
    gl_Position = fpBuf.value.viewProjection * worldPos;

    v_color = pc.value.color;
    v_encodedR = pc.value.encodedR;
    v_encodedG = pc.value.encodedG;
}
