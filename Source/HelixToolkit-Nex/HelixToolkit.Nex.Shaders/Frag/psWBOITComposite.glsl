#include "HxHeaders/HeaderFrag.glsl"

// Weighted Blended Order-Independent Transparency (WBOIT) composite shader.
// References:
//   McGuire & Bavoil, "Weighted Blended Order-Independent Transparency", JCGT 2013
//
// Reads the accumulation texture (RGBA16F) and revealage texture (R16F) produced by
// the transparent geometry pass, then composites the result over the opaque color buffer
// using the standard WBOIT resolve formula:
//
//   color = accum.rgb / max(accum.a, 1e-5)
//   alpha = 1.0 - revealage
//   result = vec4(color, alpha)    (blended over dst with ONE_MINUS_SRC_ALPHA / SRC_ALPHA)

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

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

void main() {
    vec4 accum = textureBindless2D(pc.value.accumTextureId, pc.value.samplerId, inTexCoord);
    float revealage = textureBindless2D(pc.value.revealTextureId, pc.value.samplerId, inTexCoord).r;

    // If revealage is ~1.0, no transparent fragments were written — discard to keep opaque color.
    if (revealage >= 1.0) {
        discard;
    }

    // Resolve WBOIT: premultiplied-alpha color divided by the sum of weights.
    vec3 averageColor = accum.rgb / max(accum.a, 1e-5);

    // Final alpha is 1 - revealage (i.e., total coverage of transparent fragments).
    outColor = vec4(averageColor, 1.0 - revealage);
}
