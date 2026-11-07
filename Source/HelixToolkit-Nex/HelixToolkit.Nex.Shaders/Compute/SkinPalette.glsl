#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/Skinning.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MatrixBuffer {
    mat4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) writeonly buffer PaletteBuffer {
    mat4 value[];
};

layout(push_constant) uniform PC {
    SkinPalettePC value;
} pc;

void main() {
    uint idx = gl_GlobalInvocationID.x;
    if (idx >= pc.value.jointCount) {
        return;
    }
    mat4 joint = MatrixBuffer(pc.value.jointMatrixAddress).value[idx];
    mat4 inverseBind = MatrixBuffer(pc.value.inverseBindAddress).value[idx];
    PaletteBuffer(pc.value.paletteAddress).value[idx] = joint * inverseBind;
}
