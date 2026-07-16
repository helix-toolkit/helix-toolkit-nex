#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

// Push constants for camera-frustum visualization.
//
// The 8 corners of the visualized camera's frustum are reconstructed by
// transforming the corners of the NDC cube by that camera's inverse
// view-projection matrix (NDC -> world), then projected by the *current*
// render camera (from FPConstants) so the frustum is drawn from the active
// viewpoint. No mesh / vertex buffers are required.
//
// This engine uses a reverse-Z right-handed projection, so the Vulkan NDC
// depth range maps as:  z = 1 -> near plane,  z = 0 -> far plane.
@code_gen
struct CameraFrustumPushConstant {
    mat4 inverseViewProjection;
    vec4 color;
    vec4 params; // x = far-plane clamp distance in world units (<= 0 => draw the true far plane)
    uint64_t fpConstAddress;
    uint64_t _padding;
};

layout(push_constant) uniform PC {
    CameraFrustumPushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

layout(location = 0) out flat vec4 color;

// 12 edges of the frustum, each defined by two cube-corner indices (0..7).
// Corner index bits: bit0 = X, bit1 = Y, bit2 = plane (0 = near, 1 = far).
//   0 = (-1,-1,near) 1 = (+1,-1,near) 2 = (-1,+1,near) 3 = (+1,+1,near)
//   4 = (-1,-1,far)  5 = (+1,-1,far)  6 = (-1,+1,far)  7 = (+1,+1,far)
const uvec2 EDGES[12] = uvec2[12](
    // Near plane
    uvec2(0, 1), uvec2(1, 3), uvec2(3, 2), uvec2(2, 0),
    // Far plane
    uvec2(4, 5), uvec2(5, 7), uvec2(7, 6), uvec2(6, 4),
    // Connecting edges (near -> far)
    uvec2(0, 4), uvec2(1, 5), uvec2(2, 6), uvec2(3, 7)
);

// Reconstruct a world-space position from an NDC coordinate using the
// visualized camera's inverse view-projection (matches the engine's own
// NDC->world convention: world_row = ndc_row * inverseViewProjection).
vec3 ndcToWorld(vec2 xy, float z) {
    vec4 w = pc.value.inverseViewProjection * vec4(xy, z, 1.0);
    return w.xyz / w.w;
}

void main() {
    FPBuffer fpBuf = FPBuffer(pc.value.fpConstAddress);

    // Decode edge and endpoint from gl_VertexIndex (0..23).
    // 24 vertices = 12 edges x 2 endpoints per edge.
    uint edgeIdx = uint(gl_VertexIndex) / 2u;
    uint endpointIdx = uint(gl_VertexIndex) % 2u;
    uint cornerIdx = EDGES[edgeIdx][endpointIdx];

    vec2 xy = vec2(
        ((cornerIdx & 1u) != 0u) ? 1.0 : -1.0,
        ((cornerIdx & 2u) != 0u) ? 1.0 : -1.0
    );
    bool isFar = (cornerIdx & 4u) != 0u;

    // Reverse-Z: near plane is at NDC z = 1.
    vec3 nearWorld = ndcToWorld(xy, 1.0);

    vec3 pos;
    if (isFar) {
        // Reverse-Z: far plane is at NDC z = 0.
        vec3 farWorld = ndcToWorld(xy, 0.0);

        // Optionally clamp the far plane to a finite distance so cameras with a
        // very large (or near-infinite) far plane still produce a readable
        // bounded frustum rather than edges shooting off to the horizon.
        float farDist = pc.value.params.x;
        if (farDist > 0.0) {
            vec3 dir = farWorld - nearWorld;
            float len = length(dir);
            pos = (len > 1e-6) ? nearWorld + dir * (min(len, farDist) / len) : farWorld;
        } else {
            pos = farWorld;
        }
    } else {
        pos = nearWorld;
    }

    gl_Position = fpBuf.value.viewProjection * vec4(pos, 1.0);
    color = pc.value.color;
}
