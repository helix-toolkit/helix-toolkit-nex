#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

// Push constants for bounding box visualization.
// Uses the same FPConstants buffer address pattern as the main vertex shader.
@code_gen
struct BBoxPushConstant {
    vec4 color;
    vec3 boxMin;
    uint _padding0;
    vec3 boxMax;
    uint _padding1;
    mat4 modelTransform;
    uint64_t fpConstAddress;
};

layout(push_constant) uniform PC {
    BBoxPushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

layout(location = 0) out flat vec4 color;

// 12 edges of a box, each defined by two corner indices (0..7).
// Corner index bits: bit0 = X, bit1 = Y, bit2 = Z
//   0 = (min, min, min), 1 = (max, min, min), 2 = (min, max, min), 3 = (max, max, min)
//   4 = (min, min, max), 5 = (max, min, max), 6 = (min, max, max), 7 = (max, max, max)
const uvec2 EDGES[12] = uvec2[12](
    // Bottom face (Z = min)
    uvec2(0, 1), uvec2(1, 3), uvec2(3, 2), uvec2(2, 0),
    // Top face (Z = max)
    uvec2(4, 5), uvec2(5, 7), uvec2(7, 6), uvec2(6, 4),
    // Vertical edges
    uvec2(0, 4), uvec2(1, 5), uvec2(2, 6), uvec2(3, 7)
);

void main() {
    FPBuffer fpBuf = FPBuffer(pc.value.fpConstAddress);

    // Decode edge and endpoint from gl_VertexIndex (0..23).
    // 24 vertices = 12 edges × 2 endpoints per edge.
    uint edgeIdx = uint(gl_VertexIndex) / 2u;
    uint endpointIdx = uint(gl_VertexIndex) % 2u;
    uint cornerIdx = EDGES[edgeIdx][endpointIdx];

    // Generate corner position from boxMin/boxMax using bit pattern.
    vec3 corner = vec3(
        ((cornerIdx & 1u) != 0u) ? pc.value.boxMax.x : pc.value.boxMin.x,
        ((cornerIdx & 2u) != 0u) ? pc.value.boxMax.y : pc.value.boxMin.y,
        ((cornerIdx & 4u) != 0u) ? pc.value.boxMax.z : pc.value.boxMin.z
    );

    gl_Position = fpBuf.value.viewProjection * pc.value.modelTransform * vec4(corner, 1.0);
    color = pc.value.color;
}
