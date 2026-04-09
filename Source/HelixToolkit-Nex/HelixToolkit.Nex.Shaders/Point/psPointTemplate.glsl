#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Point/PointStructs.glsl"

layout(location = 0) in vec2  v_uv;
layout(location = 1) in vec4  v_color;
layout(location = 2) in float v_screenSize;
layout(location = 3) in flat vec2  v_entityId;
layout(location = 4) in flat uint  v_textureIndex;
layout(location = 5) in flat uint  v_samplerIndex;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec2 outEntityId;

layout(push_constant) uniform PC {
    PointRenderPC value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

// --- User-overridable functions ---

// Returns the point color. Override this in a custom shader to sample textures, apply lighting, etc.
// Default implementation: circle SDF with optional texture sampling.
/*TEMPLATE_POINT_COLOR_START*/
vec4 getPointColor() {
    float dist = dot(v_uv, v_uv);
    if (dist > 1.0) discard;

    float edgeWidth = 2.0 / max(v_screenSize, 1.0);
    float alpha = 1.0 - smoothstep(1.0 - edgeWidth, 1.0, dist);

    vec4 color = v_color;

    // Optional texture sampling via bindless
    if (v_textureIndex > 0u) {
        vec2 texUv = v_uv * 0.5 + 0.5; // [-1,1] -> [0,1]
        vec4 texColor = textureBindless2D(v_textureIndex, v_samplerIndex, texUv);
        color *= texColor;
    }

    color.a *= alpha;
    return color;
}
/*TEMPLATE_POINT_COLOR_END*/

// --- Main ---

void main() {
    vec4 color = getPointColor();
    if (color.a < 1e-4) discard;

    outColor    = color;
    outEntityId = v_entityId;
}
