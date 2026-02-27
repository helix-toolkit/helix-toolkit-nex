#include "HxHeaders/HeaderFrag.glsl"

// Fragment shader to sample texture.
#define DEBUG_MESH_ID 0
#define DEBUG_DEPTH 1

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

@code_gen
struct SampleTexturePushConstants {
    uint textureId;
    uint samplerId;
    float minValue; // For depth visualization, the near plane distance for linearization
    float maxValue; // For depth visualization, the far plane distance for linearization
};

layout (constant_id = 0) const uint SAMPLE_MODE = 0;

layout(push_constant) uniform PushConstants {
    SampleTexturePushConstants value;
} pc;


void main() {
    if (SAMPLE_MODE == DEBUG_MESH_ID) {
        vec2 entity = textureBindless2D(pc.value.textureId, pc.value.samplerId, inTexCoord).rg;
        // Convert uint to RGB color representation
        // Break down the uint value into color channels for better visualization
        uint r_u = floatBitsToUint(entity.r);
        float red = float((r_u >> 16) & 0xFFu) / 255.0;
        float green = float((r_u >> 8) & 0xFFu) / 255.0;
        float blue = float(r_u & 0xFFu) / 255.0;
        outColor = vec4(red, green, blue, 1.0);
    }
    if (SAMPLE_MODE == DEBUG_DEPTH) {
        float near = pc.value.minValue;
        float far = pc.value.maxValue;
        float depth = textureBindless2D(pc.value.textureId, pc.value.samplerId, inTexCoord).r;
        // Formula for Reversed-Z Perspective:
        float linearDepth = (near * far) / (depth * (far - near) + near);

        // To visualize as 0-1 (0 = near, 1 = far):
        float visualDepth = (linearDepth - near) / (far - near);
        outColor = vec4(visualDepth, visualDepth, visualDepth, 1.0);
    }
}
