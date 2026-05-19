#include "HxHeaders/HeaderCompute.glsl"
#include "Billboard/BillboardStructs.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BillboardVertexBuffer {
    BillboardVertex vertices[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BillboardInfoBuffer {
    BillboardInfo info[];
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

BillboardVertexBuffer billboardVertices = BillboardVertexBuffer(pc.value.billboardVertexAddress);
BillboardInfoBuffer billboardInfo = BillboardInfoBuffer(pc.value.billboardInfoAddress);

void main() {
    uint idx = gl_GlobalInvocationID.x;

    if (idx >= pc.value.billboardCount) return;

    // Read all per-billboard data from the single interleaved vertex buffer
    BillboardVertex v = billboardVertices.vertices[idx];
    BillboardInfo info = billboardInfo.info[v.infoIndex];

    float width = v.size.x;
    float height = v.size.y;
    if (width <= 0.0 || height <= 0.0) return; // Cull zero/negative size billboards early

    IndirectArgsBuffer      cmdBuf = IndirectArgsBuffer(pc.value.indirectArgsAddress);
    BillboardDrawDataBuffer outBuf = BillboardDrawDataBuffer(pc.value.drawDataAddress);

    // The anchor is at local (0,0,0), so worldTransform * (0,0,0,1) = 4th column of the matrix.
    // Avoids a full mat4×vec4 multiply.
    vec3 anchorWorldXYZ = info.worldTransform[3].xyz;

    // Distance cull
    float dist = distance(args.cameraPosition, anchorWorldXYZ);
    float cullDistance = info.cullDistance;
    if (cullDistance > 0.00001 && dist > cullDistance) return;

    // --- Compute projFactor and pixelsPerUnit once ---
    // projFactor = screenHeight / (2 * tan(fovY/2))  [pixels per unit at unit distance]
    float projFactor    = args.screenHeight / (2.0 * tan(args.fovY * 0.5));
    float distSafe      = max(dist, 0.001);
    float pixelsPerUnit = projFactor / distSafe;

    // --- Resolve pixel dimensions and pixel offset in one pass ---
    vec2 localOffsetXY = v.position.xy;
    float outScreenW, outScreenH;
    vec2  pixelOff;
    float halfPixW, halfPixH;
    if (info.fixedSize != 0u) {
        outScreenW  = width;
        outScreenH  = height;
        pixelOff    = localOffsetXY;
        halfPixW    = width  * 0.5 + 2.0;
        halfPixH    = height * 0.5 + 2.0;
    } else {
        outScreenW  = width  * pixelsPerUnit;
        outScreenH  = height * pixelsPerUnit;
        pixelOff    = localOffsetXY * pixelsPerUnit;
        halfPixW    = outScreenW * 0.5 + 2.0;
        halfPixH    = outScreenH * 0.5 + 2.0;
    }

    // Screen-size cull
    if (max(outScreenW, outScreenH) < args.minScreenSize) return;

    // --- Frustum cull in screen-pixel space ---
    // Replicates the vertex shader exactly: project anchor to pixels, then add linear pixel offset.
    vec4 anchorClip = args.viewProjection * vec4(anchorWorldXYZ, 1.0);
    float w = anchorClip.w;
    if (w <= 0.0) return; // Behind camera

    vec2 anchorPixels = (anchorClip.xy / w * 0.5 + 0.5) * vec2(args.screenWidth, args.screenHeight);
    vec2 glyphPixels  = anchorPixels + pixelOff;

    if (glyphPixels.x + halfPixW < 0.0 || glyphPixels.x - halfPixW > args.screenWidth)  return;
    if (glyphPixels.y + halfPixH < 0.0 || glyphPixels.y - halfPixH > args.screenHeight) return;

    // Use per-billboard color if alpha > 0, otherwise use uniform color
    vec4 color = v.color.a > 0.0 ? v.color : info.color;

    // --- Pack entity ID ---
    uvec2 objId = packObjectInfo(info.worldId, info.entityId, idx);
    vec2 packedId = packPrimitiveId(objId, idx);

    // --- Allocate output slot ---
    uint slot = atomicAdd(cmdBuf.args.instanceCount, 1);

    // --- Write draw data ---
    BillboardDrawData d;
    d.worldPos       = anchorWorldXYZ;
    d.screenWidth    = outScreenW;
    d.color          = color;
    d.packedEntityId = packedId;
    d.screenHeight   = outScreenH;
    d.uvRect         = v.uvRect;
    d.pixelOffset    = pixelOff;
    d.type           = v.type;
    d.infoIndex      = v.infoIndex;

    outBuf.data[slot] = d;
}
