#include "HxHeaders/HeaderFrag.glsl"

#ifdef WBOIT_SUBPASS
// WBOIT composite shader using Vulkan input attachments (subpass path).
// Reads accumulation and revealage from tile-local memory via subpassLoad.

layout(input_attachment_index = 0, set = 1, binding = 0) uniform subpassInput inAccum;
layout(input_attachment_index = 1, set = 1, binding = 1) uniform subpassInput inRevealage;
#else
layout(location = 0) in vec2 inTexCoord;

@code_gen
struct WBOITCompositePushConstants {
    uint accumTextureId;   // Bindless texture index for the WBOIT accumulation texture
    uint revealTextureId;  // Bindless texture index for the WBOIT revealage texture
    uint samplerId;        // Bindless sampler index
    uint pad0;
};

layout(push_constant) uniform PushConstants {
    WBOITCompositePushConstants value;
} pc;
#endif
layout(location = 0) out vec4 outColor;

void main() {
#ifdef WBOIT_SUBPASS
    vec4 accum = subpassLoad(inAccum);
    float revealage = subpassLoad(inRevealage).r;
#else
    vec4 accum = textureBindless2D(pc.value.accumTextureId, pc.value.samplerId, inTexCoord);
    float revealage = textureBindless2D(pc.value.revealTextureId, pc.value.samplerId, inTexCoord).r;
#endif

    // No transparent fragments written — discard to preserve opaque color.
    if (revealage >= 1.0) {
       discard;
    }

    // Resolve WBOIT: premultiplied-alpha color divided by sum of weights.
    vec3 averageColor = accum.rgb / max(accum.a, 1e-5);

    // Final alpha is 1 - revealage (total coverage of transparent fragments).
    outColor = vec4(averageColor, 1.0 - revealage);
}
