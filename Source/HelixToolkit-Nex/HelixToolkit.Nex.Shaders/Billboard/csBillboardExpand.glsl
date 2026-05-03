#include "HxHeaders/HeaderCompute.glsl"
#include "Billboard/BillboardStructs.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BillboardVertexBuffer {
    BillboardVertex vertices[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) writeonly buffer BillboardDrawDataBuffer {
    BillboardDrawData data[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) buffer IndirectArgsBuffer {
    BillboardDrawIndirectArgs args;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BillboardExpandBuffer {
    BillboardExpandArgs value;
};

layout(push_constant) uniform PC {
    BillboardExpandPC value;
} pc;

BillboardExpandArgs args = BillboardExpandBuffer(pc.value.argsAddress).value;

void main() {
    uint idx = gl_GlobalInvocationID.x;

    if (idx >= pc.value.billboardCount) return;

    // Read all per-billboard data from the single interleaved vertex buffer
    BillboardVertex v = BillboardVertexBuffer(pc.value.billboardVertexAddress).vertices[idx];

    float width = v.size.x;
    float height = v.size.y;
    if (width <= 0.0 || height <= 0.0) return; // Cull zero/negative size billboards early

    IndirectArgsBuffer       cmdBuf = IndirectArgsBuffer(pc.value.indirectArgsAddress);
    BillboardDrawDataBuffer  outBuf = BillboardDrawDataBuffer(pc.value.drawDataAddress);

    // Transform the anchor point (origin) to world space.
    // For text billboards, the anchor is at local (0,0,0); for non-text billboards
    // with identity worldTransform, this equals (0,0,0) and glyph positions pass through.
    vec4 anchorWorld = pc.value.worldTransform * vec4(0.0, 0.0, 0.0, 1.0);

    // Use per-billboard color if alpha > 0, otherwise use uniform color from push constants
    vec4 color = v.color.a > 0.0 ? v.color : pc.value.color;

    vec4 uvRect = v.uvRect;

    // --- Frustum cull using anchor world position (not per-glyph) ---
    // All glyphs in a text string share the same cull decision based on the anchor.
    vec4 clip = args.viewProjection * anchorWorld;
    float w = clip.w;
    // Behind camera (reversed-Z: w < 0 means behind near plane)
    if (w <= 0.0) return;
    // Outside frustum (with padding for billboard size)
    float padding = max(width, height) * 2.0; // generous padding
    vec4 clipAbs = abs(clip);
    if (clipAbs.x > w + padding || clipAbs.y > w + padding) return;

    // --- Compute screen-space size using anchor distance (shared across all glyphs) ---
    float screenWidth;
    float screenHeight;
    float dist = distance(args.cameraPosition, anchorWorld.xyz);
    float projFactor = args.screenHeight / (2.0 * tan(args.fovY * 0.5));
    if (pc.value.fixedSize != 0u) {
        // Fixed-size mode: width/height are already in pixels, no perspective projection.
        screenWidth = width;
        screenHeight = height;
    } else {
        // World-space mode: project world-space dimensions to pixels.
        // Use anchor distance so all glyphs in a text string get consistent sizing.
        // screenSize = (worldSize / dist) * (screenHeight / (2 * tan(fovY/2)))
        screenWidth = (width / max(dist, 0.001)) * projFactor;
        screenHeight = (height / max(dist, 0.001)) * projFactor;
    }

    // --- Compute per-glyph pixel offset from anchor ---
    // The local offset (v.position.xyz) is in the text's local coordinate system.
    // Convert it to a pixel offset so the vertex shader can apply it after pixel-snapping the anchor.
    vec3 localOffset = v.position.xyz;
    vec2 pixelOff;
    if (pc.value.fixedSize != 0u) {
        // Fixed-size: local offsets are already in pixel units
        pixelOff = localOffset.xy;
    } else {
        // World-space: project local offset to pixels using the same projection factor
        float pixelsPerUnit = projFactor / max(dist, 0.001);
        pixelOff = localOffset.xy * pixelsPerUnit;
    }

    // Screen-size cull
    if (max(screenWidth, screenHeight) < args.minScreenSize) return;

    // --- Pack entity ID ---
    uvec2 objId = packObjectInfo(pc.value.worldId, pc.value.entityId, idx);
    vec2 packedId = packPrimitiveId(objId, idx);

    // --- Allocate output slot ---
    uint slot = atomicAdd(cmdBuf.args.instanceCount, 1);

    // --- Write draw data ---
    BillboardDrawData d;
    d.worldPos       = anchorWorld.xyz;
    d.screenWidth    = screenWidth;
    d.color          = color;
    d.packedEntityId = packedId;
    d.screenHeight   = screenHeight;
    d.textureIndex   = pc.value.textureIndex;
    d.samplerIndex   = pc.value.samplerIndex;
    d.uvRect         = uvRect;
    d.pixelOffset    = pixelOff;
    d._drawPadding   = vec2(0.0);

    // --- Precompute and pack SDF atlas parameters ---
    float halfRange = pc.value.sdfDistanceRange * 0.5;
    float aemrangeMin = (pc.value.sdfDistanceRangeMiddle - halfRange) / pc.value.sdfGlyphCellSize;
    float aemrangeMax = (pc.value.sdfDistanceRangeMiddle + halfRange) / pc.value.sdfGlyphCellSize;
    d.sdfAemrangePacked    = packHalf2x16(vec2(aemrangeMin, aemrangeMax));
    d.sdfAtlasSizePacked   = (uint(pc.value.sdfAtlasHeight) << 16) | uint(pc.value.sdfAtlasWidth);
    d.sdfGlyphCellSizeBits = floatBitsToUint(pc.value.sdfGlyphCellSize);

    outBuf.data[slot] = d;
}
