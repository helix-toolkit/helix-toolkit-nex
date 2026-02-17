#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/MeshDraw.glsl"

layout(buffer_reference, std430, buffer_reference_align = 16) buffer MeshDrawBuffer {
    MeshDraw draws[];
};
// Constants
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// ------------------------------------------------------------------
// PUSH CONSTANTS
// ------------------------------------------------------------------
@code_gen
struct ResetMeshDrawInstanceCountPC {
    uint64_t meshDrawBufferAddress;
    uint meshDrawCount;
    uint meshDrawOffset;
};

layout(push_constant) uniform PC {
    ResetMeshDrawInstanceCountPC value;
} pc;

void main() {
    uint gID = gl_GlobalInvocationID.x;
    if (gID >= pc.value.meshDrawCount) {
        return;
    }
    MeshDrawBuffer buf = MeshDrawBuffer(pc.value.meshDrawBufferAddress);
    buf.draws[gID + pc.value.meshDrawOffset].instanceCount = 0;
}
