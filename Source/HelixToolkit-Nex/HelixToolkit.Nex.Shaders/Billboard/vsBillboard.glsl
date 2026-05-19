#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Billboard/BillboardStructs.glsl"

layout(location = 0) out vec2  v_uv;
layout(location = 1) out flat vec2 v_screenDim;
layout(location = 2) out flat vec4 v_color;
layout(location = 3) out flat vec2 v_entityId;
layout(location = 4) out flat uint v_billboardType;
layout(location = 5) out flat uint v_infoIndex;
layout(location = 6) out flat vec3 v_fragWorldPos;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BillboardDrawDataBuffer {
    BillboardDrawData data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(push_constant) uniform PC {
    BillboardRenderPC value;
} pc;

// Triangle strip quad corners: 4 vertices → 2 triangles
//   v3 (TL) ── v2 (TR)
//    │  \       │
//   v0 (BL) ── v1 (BR)
// Strip order: v0, v1, v3, v2  (Z-pattern)
const vec2 QUAD_UVS[4] = vec2[4](
    vec2(-1.0, -1.0),   // v0: bottom-left
    vec2( 1.0, -1.0),   // v1: bottom-right
    vec2(-1.0,  1.0),   // v3: top-left
    vec2( 1.0,  1.0)    // v2: top-right
);

void main() {
    BillboardDrawDataBuffer buf = BillboardDrawDataBuffer(pc.value.drawDataAddress);
    BillboardDrawData d = buf.data[gl_InstanceIndex];

    vec2 screenDims = FPBuffer(pc.value.fpConstAddress).fpConstants.screenDimensions;
    
    // 1. Project the anchor world position to NDC
    vec4 clipCenter = FPBuffer(pc.value.fpConstAddress).fpConstants.viewProjection * vec4(d.worldPos, 1.0);
    vec2 ndc = clipCenter.xy / clipCenter.w;

    // 2. Convert NDC to Screen Space (Pixels) to get the raw continuous position
    vec2 rawPixelPos = (ndc * 0.5 + 0.5) * screenDims;
    
    // Apply per-glyph pixel offset
    rawPixelPos += d.pixelOffset;

    // Get exact integer pixel dimensions
    vec2 size = max(round(vec2(d.screenWidth, d.screenHeight)), vec2(1.0));
    vec2 halfSize = size * 0.5;

    // 3. Snap the corner to a pixel boundary (integer)
    // This guarantees that both even and odd sized quads perfectly align with pixel edges.
    vec2 cornerPos = rawPixelPos - halfSize;
    vec2 snappedCorner = round(cornerPos); 
    
    // Re-derive the snapped center from the perfectly aligned corner
    vec2 snappedCenter = snappedCorner + halfSize;

    // 4. Convert snapped center back to NDC
    vec2 snappedNDC = (snappedCenter / screenDims) * 2.0 - 1.0;
    
    // Reconstruct clip space position
    clipCenter.xy = snappedNDC * clipCenter.w;

    // 5. Use the exact snapped sizes for vertex offsets
    float pixelSizeX = size.x / screenDims.x;
    float pixelSizeY = size.y / screenDims.y;

    vec2 uv = QUAD_UVS[gl_VertexIndex];
    clipCenter.xy += vec2(uv.x * pixelSizeX, uv.y * pixelSizeY) * clipCenter.w;

    gl_Position = clipCenter;

    // Map UV coordinates from uvRect: [-1,1] → [u_min, u_max] / [v_min, v_max]
    float texU = mix(d.uvRect.x, d.uvRect.z, uv.x * 0.5 + 0.5);
    float texV = mix(d.uvRect.y, d.uvRect.w, uv.y * 0.5 + 0.5);
    v_uv           = vec2(texU, texV);

    v_color        = d.color;
    v_screenDim    = size; // Use the snapped size here too
    v_entityId     = d.packedEntityId;
    v_fragWorldPos = d.worldPos;
    v_billboardType = d.type;
    v_infoIndex    = d.infoIndex;
}
