#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "Billboard/BillboardStructs.glsl"

layout(location = 0) out vec2  v_uv;
layout(location = 1) out vec4  v_color;
layout(location = 2) out float v_screenWidth;
layout(location = 3) out float v_screenHeight;
layout(location = 4) out flat vec2  v_entityId;
layout(location = 5) out flat uint  v_textureIndex;
layout(location = 6) out flat uint  v_samplerIndex;
layout(location = 7) out flat vec3  v_fragWorldPos;
layout(location = 8) out flat uint  v_sdfAemrangePacked;
layout(location = 9) out flat uint  v_sdfAtlasSizePacked;
layout(location = 10) out flat uint v_sdfGlyphCellSizeBits;

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

    // 2. Convert NDC to Screen Space (Pixels)
    vec2 pixelPos = (ndc * 0.5 + 0.5) * screenDims;

    // 3. SNAP anchor to nearest pixel. This kills the "shaking".
    pixelPos = floor(pixelPos) + 0.5; 

    // 4. Apply per-glyph pixel offset from anchor
    pixelPos += d.pixelOffset;

    // 5. Convert back to NDC
    vec2 snappedNDC = (pixelPos / screenDims) * 2.0 - 1.0;
    
    // Reconstruct clip space position
    clipCenter.xy = snappedNDC * clipCenter.w;

    // Use snapped sizes to keep the texels 1:1 with pixels as much as possible
    float pixelSizeX = max(round(d.screenWidth), 1.0) / screenDims.x;
    float pixelSizeY = max(round(d.screenHeight), 1.0) / screenDims.y;

    vec2 uv = QUAD_UVS[gl_VertexIndex];
    clipCenter.xy += vec2(uv.x * pixelSizeX, uv.y * pixelSizeY) * clipCenter.w;

    gl_Position = clipCenter;

    // Map UV coordinates from uvRect: [-1,1] → [u_min, u_max] / [v_min, v_max]
    float texU = mix(d.uvRect.x, d.uvRect.z, uv.x * 0.5 + 0.5);
    float texV = mix(d.uvRect.y, d.uvRect.w, uv.y * 0.5 + 0.5);
    v_uv           = vec2(texU, texV);

    v_color        = d.color;
    v_screenWidth  = d.screenWidth;
    v_screenHeight = d.screenHeight;
    v_entityId     = d.packedEntityId;
    v_textureIndex = d.textureIndex;
    v_samplerIndex = d.samplerIndex;
    v_fragWorldPos = d.worldPos;
    v_sdfAemrangePacked    = d.sdfAemrangePacked;
    v_sdfAtlasSizePacked   = d.sdfAtlasSizePacked;
    v_sdfGlyphCellSizeBits = d.sdfGlyphCellSizeBits;
}
