#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Line/LineStructs.glsl"
#include "Line/LineShape.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(location = 0) in vec2  v_uv;
layout(location = 1) in vec4  v_color;
layout(location = 2) in noperspective float v_screenSize;
layout(location = 3) in flat uvec2  v_entityId;
layout(location = 4) in flat uint  v_textureIndex;
layout(location = 5) in flat uint  v_samplerIndex;
layout(location = 6) in vec3  v_fragWorldPos;
layout(location = 7) in flat vec4  v_segPx;
layout(location = 8) in flat vec4  v_dirHw;
layout(location = 9) in flat vec4  v_adjDir;
layout(location = 10) in flat uint v_endModes;

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

// Distance returned for fragments that belong to a neighbouring segment's half of a join.
const float LINE_OUTSIDE = 1e30;

// Fragment position in line pixel space (NDC * 0.5 * screen size, +y up), matching vsLine.glsl.
// Inverts the Y-flipped viewport. Derived from gl_FragCoord so that both segments of a joint see
// bit-identical positions for the shared bisector test.
vec2 getFragLinePx() {
    vec2 screen = fpConst.screenDimensions;
    return vec2(gl_FragCoord.x - 0.5 * screen.x, 0.5 * screen.y - gl_FragCoord.y);
}

// True when the fragment lies on the neighbour's side of a join's angle bisector. q = fragment
// relative to the joint, u = this segment's direction out of the joint, v = the neighbour's
// direction away from it. The end of segment s and the start of segment s+1 evaluate the same
// value with opposite sign; opposite strictness assigns a fragment exactly on the bisector to
// exactly one of them.
bool isBeyondJoin(uint mode, vec2 q, vec2 u, vec2 v, bool isSegmentEnd) {
    if (!isLineJoinMode(mode)) {
        return false;
    }
    float side = dot(q, u + v);
    return isSegmentEnd ? side >= 0.0 : side > 0.0;
}

// Applies one end's cap / join shape to the body distance d (see isBeyondJoin for q, u, v).
float shapeLineEnd(float d, uint mode, vec2 q, vec2 u, vec2 v, float halfWidth) {
    float beyond = dot(q, u); // distance past the endpoint along the segment
    if (mode == LINE_END_BUTT) {
        return max(d, beyond);
    }
    if (mode == LINE_END_SQUARE) {
        return max(d, beyond - halfWidth);
    }
    if (mode == LINE_END_ROUND || mode == LINE_END_ROUND_JOIN) {
        return min(length(q) - halfWidth, max(d, beyond));
    }
    if (mode == LINE_END_BEVEL_JOIN) {
        // Bevel edge: the line through both outer corners, at halfWidth * cos(turnAngle / 2)
        // from the joint along the outward corner direction normalize(u - v).
        // A straight continuation (u == v) has no outer corner: the body strip is already exact.
        vec2 corner = u - v;
        float cornerLen = length(corner);
        if (cornerLen <= 1e-6) {
            return d;
        }
        float cosHalf = 0.5 * length(u + v);
        return max(d, dot(q, corner / cornerLen) - halfWidth * cosHalf);
    }
    // Miter: the body strip extended up to the bisector forms the miter tip.
    return d;
}

// Signed distance (px) from the fragment to the edge of this segment's share of the stroke,
// including caps and joins; negative inside. LINE_OUTSIDE when a join assigns it to a neighbour.
float getLineShapeDistance() {
    vec2 p = getFragLinePx();
    vec2 start = v_segPx.xy;
    vec2 end = v_segPx.zw;
    vec2 dir = v_dirHw.xy;
    uint startMode = v_endModes & 0xFFu;
    uint endMode = (v_endModes >> 8) & 0xFFu;

    vec2 q0 = p - start;
    vec2 q1 = p - end;
    if (isBeyondJoin(startMode, q0, -dir, v_adjDir.xy, false)
        || isBeyondJoin(endMode, q1, dir, v_adjDir.zw, true)) {
        return LINE_OUTSIDE;
    }

    // Body: half width interpolated along the segment (differs per end in world render space).
    float segLen = length(end - start);
    float t = segLen > 1e-6 ? clamp(dot(q0, dir) / segLen, 0.0, 1.0) : 0.0;
    float halfWidth = mix(v_dirHw.z, v_dirHw.w, t);
    float d = abs(dot(q0, vec2(-dir.y, dir.x))) - halfWidth;

    d = shapeLineEnd(d, startMode, q0, -dir, v_adjDir.xy, v_dirHw.z);
    d = shapeLineEnd(d, endMode, q1, dir, v_adjDir.zw, v_dirHw.w);
    return d;
}

// Anti-aliased coverage of the stroke shape: 1 inside, 0 outside, a 1px ramp centred on the edge.
float getLineCoverage() {
    return clamp(0.5 - getLineShapeDistance(), 0.0, 1.0);
}
/*UTILITY_FUNCTIONS_END*/

layout (constant_id = 0) const uint MATERIAL_TYPE = 0;
// --- User-overridable functions ---

// Returns the line color. Override this in a custom shader to apply texture sampling,
// custom shading, etc.
// Default implementation: the interpolated vertex color, optionally modulated by a bindless
// texture. Edge anti-aliasing and the cap / join shape are applied afterwards by main() via
// getLineCoverage(), so implementations do not need to feather the line edge themselves.
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
    return color;
}

// --- Main ---
/*TEMPLATE_CUSTOM_MAIN_START*/
void main() {
    // Cap / join shape coverage is applied for every material, after its outputColor().
    // outputColor() returns STRAIGHT (non-premultiplied) alpha to match the default line blend
    // state (SrcAlpha / OneMinusSrcAlpha), so coverage scales alpha only. A material that sets a
    // premultiplied BlendConfig must not rely on this scaling for its RGB.
    float coverage = getLineCoverage();
    if (coverage <= 0.0) discard;

    vec4 color = outputColor();
    color.a *= coverage;
    if (color.a < 1e-4) discard;

    outColor    = color;
#ifdef OUTPUT_DRAW_ID
    uint primID = uint(gl_PrimitiveID);
    outEntityId = packPrimitiveId(v_entityId, primID);
#endif
}
/*TEMPLATE_CUSTOM_MAIN_END*/
