#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/MeshDraw.glsl"


layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    MeshDrawPushConstant meshDrawPushConstant;
    vec4 color;
} pc;

void main() {
    outColor = pc.color;
}
