#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Point/PointStructs.glsl"

layout(location = 0) out vec2  v_uv;
layout(location = 1) out vec4  v_color;
layout(location = 2) out float v_screenSize;
layout(location = 3) out flat vec2  v_entityId;
layout(location = 4) out flat uint  v_textureIndex;
layout(location = 5) out flat uint  v_samplerIndex;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointDrawDataBuffer {
    PointDrawData data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(push_constant) uniform PC {
    PointRenderPC value;
} pc;

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

void main() {
    // gl_InstanceIndex = which visible point
    // gl_VertexIndex   = which corner of the quad (0..3)

    PointDrawDataBuffer buf = PointDrawDataBuffer(pc.value.drawDataAddress);
    PointDrawData d = buf.data[gl_InstanceIndex];

    vec2 uv = QUAD_UVS[gl_VertexIndex];

    // Expand quad in clip space (screen-aligned billboard)
    vec4 clipCenter = FPBuffer(pc.value.fpConstAddress).fpConstants.viewProjection * vec4(d.worldPos, 1.0);

    // Half-size in pixels → NDC offset
    vec2 screenDims = FPBuffer(pc.value.fpConstAddress).fpConstants.screenDimensions;
    vec2 pixelSize = vec2(d.screenSize) / screenDims;

    // Offset in clip space (multiply by w to stay in homogeneous coords)
    clipCenter.xy += uv * pixelSize * clipCenter.w;

    gl_Position    = clipCenter;
    v_uv           = uv;
    v_color        = d.color;
    v_screenSize   = d.screenSize;
    v_entityId     = d.packedEntityId;
    v_textureIndex = d.textureIndex;
    v_samplerIndex = d.samplerIndex;
}
