#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Billboard/BillboardStructs.glsl"

layout(location = 0) in vec2  v_uv;
layout(location = 1) in vec4  v_color;
layout(location = 2) in float v_screenWidth;
layout(location = 3) in float v_screenHeight;
layout(location = 4) in flat vec2  v_entityId;
layout(location = 5) in flat uint  v_textureIndex;
layout(location = 6) in flat uint  v_samplerIndex;
layout(location = 7) in flat vec3  v_fragWorldPos;
layout(location = 8) in flat uint  v_sdfAemrangePacked;
layout(location = 9) in flat uint  v_sdfAtlasSizePacked;
layout(location = 10) in flat uint v_sdfGlyphCellSizeBits;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec2 outEntityId;

layout(push_constant) uniform PC {
    BillboardRenderPC value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

vec2 getUV() {
    return v_uv;
}

vec4 getColor() {
    return v_color;
}

uint getTextureId() {
    return v_textureIndex;
}

uint getSamplerId() {
    return v_samplerIndex;
}

float getBillboardWidth() {
    return v_screenWidth;
}

float getBillboardHeight() {
    return v_screenHeight;
}

vec2 getSdfAemrange() {
    return unpackHalf2x16(v_sdfAemrangePacked);
}

vec2 getSdfAtlasSize() {
    return vec2(float(v_sdfAtlasSizePacked & 0xFFFFu), float(v_sdfAtlasSizePacked >> 16));
}

float getSdfGlyphCellSize() {
    return uintBitsToFloat(v_sdfGlyphCellSizeBits);
}

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

/*UTILITY_FUNCTIONS_BEGIN*/
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

// Returns the billboard color. Override this in a custom shader to apply custom shading.
// Default implementation: sample bindless texture when textureIndex > 0, multiply by vertex color.
/*TEMPLATE_BILLBOARD_COLOR_START*/
vec4 outputColor() {
    vec4 color = v_color;

    // Optional texture sampling via bindless
    if (v_textureIndex > 0u) {
        vec4 texColor = textureBindless2D(v_textureIndex, v_samplerIndex, v_uv);
        color *= texColor;
    }

    return color;
}
/*TEMPLATE_BILLBOARD_COLOR_END*/

// --- Main ---
/*TEMPLATE_CUSTOM_MAIN_START*/
void main() {
    vec4 color = outputColor();
    if (color.a < 1e-4) discard;

    outColor    = color;
    outEntityId = v_entityId;
}
/*TEMPLATE_CUSTOM_MAIN_END*/
