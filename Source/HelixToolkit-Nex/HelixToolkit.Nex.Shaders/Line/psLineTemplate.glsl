#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Line/LineStructs.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(location = 0) in vec2  v_uv;
layout(location = 1) in vec4  v_color;
layout(location = 2) in noperspective float v_screenSize;
layout(location = 3) in flat uvec2  v_entityId;
layout(location = 4) in flat uint  v_textureIndex;
layout(location = 5) in flat uint  v_samplerIndex;
layout(location = 6) in vec3  v_fragWorldPos;

layout(location = 0) out vec4 outColor;
#ifdef OUTPUT_DRAW_ID
layout(location = 1) out vec2 outEntityId;
#endif

layout(push_constant) uniform PC {
    LineRenderPC value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

/*UTILITY_FUNCTIONS_BEGIN*/
vec2 getUV() {
    return v_uv;
}

vec4 getColor() {
    return v_color;
}

float getLineWidth() {
    return v_screenSize;
}

uint getTextureId() {
    return v_textureIndex;
}

uint getSamplerId() {
    return v_samplerIndex;
}

uint64_t getTimeMs() {
    return fpConst.timeMs;
}

mat4 getViewProjection() {
    return fpConst.viewProjection;
}

mat4 getInvViewProjection() {
    return fpConst.inverseViewProjection;
}

mat4 getView() {
    return fpConst.view;
}

mat4 getInvView() {
    return fpConst.inverseView;
}

vec3 getCameraPosition() {
    return fpConst.cameraPosition;
}

vec2 getScreenSize() {
    return fpConst.screenDimensions;
}

bool isPointerRingEnabled() {
    return fpConst.pointerRing.enabled != 0;
}

vec3 getPointerRayDirection() {
    return fpConst.pointerRing.rayDirection;
}

vec3 getPointerRayOrigin() {
    return fpConst.pointerRing.rayOrigin;
}

float getPointerRingOuterDistThreshold() {
    return fpConst.pointerRing.outerDistThreshold;
}

float getPointerRingInnerDistThreshold() {
    return fpConst.pointerRing.innerDistThreshold;
}

float getPointerRingColorMix() {
    return fpConst.pointerRing.colorMix;
}

vec3 getPointerRingColor() {
    return fpConst.pointerRing.color;
}

float getFragToPointerRayDistance() {
    vec3 rayOrigin = getPointerRayOrigin();
    vec3 rayDir = normalize(getPointerRayDirection());
    vec3 toFrag = v_fragWorldPos - rayOrigin;
    float t = dot(toFrag, rayDir);
    vec3 closestPoint = rayOrigin + rayDir * max(t, 0.0);
    return length(v_fragWorldPos - closestPoint);
}

bool isInPointerRing() {
    float dist = getFragToPointerRayDistance();
    return dist >= getPointerRingInnerDistThreshold() && dist <= getPointerRingOuterDistThreshold();
}

vec4 mixWithPointerRing(in vec4 color) {
    if (isPointerRingEnabled() && isInPointerRing()) {
        vec3 ringColor = getPointerRingColor();
        color.rgb = mix(color.rgb, ringColor, getPointerRingColorMix());
    }
    return color;
}
/*UTILITY_FUNCTIONS_END*/

layout (constant_id = 0) const uint MATERIAL_TYPE = 0;
// --- User-overridable functions ---

// Returns the line color. Override this in a custom shader to apply texture sampling,
// custom shading, etc.
// Default implementation: the interpolated vertex color, optionally modulated by a bindless
// texture, with edge feathering applied, output as premultiplied alpha.
vec4 outputColor() {
    // Base color: the interpolated vertex color, modulated by a bindless texture when one is
    // bound. v_textureIndex == 0 means no texture, so the vertex color is used unchanged
    // (Requirements 8.1, 8.2, 8.3, 8.4).
    vec4 color = v_color;
    if (v_textureIndex > 0u) {
        vec2 texUv = v_uv * 0.5 + 0.5; // Quad_Corner_UV [-1,1] -> [0,1]
        vec4 texColor = textureBindless2D(v_textureIndex, v_samplerIndex, texUv);
        color *= texColor; // component-wise multiply with the interpolated vertex color
    }

    // Edge coverage measured in screen pixels. v_screenSize is the (screen-linear interpolated)
    // geometry width, already limited by the vertex shader (screen: [1,64]; world: >= 1).
    // Distance of this fragment from the centre line, and the half width, in pixels.
    float halfWidthPx = max(v_screenSize, 1.0) * 0.5;
    float distPx = abs(v_uv.y) * v_screenSize * 0.5;

    // Pixels remaining to the outer edge: >= 1 inside the inner band, 0 at the outer edge
    // (Requirement 4.1).
    float edgePx = halfWidthPx - distPx;

    // Fragment alpha: 1.0 inside the inner band, interpolating to 0.0 across the one-pixel
    // Feather_Band (Requirements 4.2, 4.3). Fragments beyond the half width get alpha 0.
    // The feather is one pixel, but never wider than the half width, so 1px lines stay fully
    // opaque at their centre (matches the previous uv-space feather clamp).
    float alpha = smoothstep(0.0, min(1.0, halfWidthPx), edgePx);
    color.a *= alpha;

    return color;
}

// --- Main ---
/*TEMPLATE_CUSTOM_MAIN_START*/
void main() {
    vec4 color = outputColor();
    if (color.a < 1e-4) discard;

    outColor    = color;
#ifdef OUTPUT_DRAW_ID
    uint primID = uint(gl_PrimitiveID);
    outEntityId = packPrimitiveId(v_entityId, primID);
#endif
}
/*TEMPLATE_CUSTOM_MAIN_END*/
