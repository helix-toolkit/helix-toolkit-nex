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

    vec4 p = vec4(v.position.xyz, 1);

    // Use per-billboard color if alpha > 0, otherwise use uniform color from push constants
    vec4 color = v.color.a > 0.0 ? v.color : pc.value.color;

    vec4 uvRect = v.uvRect;

    // --- Frustum cull (clip-space) ---
    vec4 clip = args.viewProjection * p;
    float w = clip.w;
    // Behind camera (reversed-Z: w < 0 means behind near plane)
    if (w <= 0.0) return;
    // Outside frustum (with padding for billboard size)
    float padding = max(width, height) * 2.0; // generous padding
    vec4 clipAbs = abs(clip);
    if (clipAbs.x > w + padding || clipAbs.y > w + padding) return;

    // --- Compute screen-space size ---
    float screenWidth;
    float screenHeight;
    if (pc.value.fixedSize != 0u) {
        // Fixed-size mode: width/height are already in pixels, no perspective projection.
        screenWidth = width;
        screenHeight = height;
    } else {
        // World-space mode: project world-space dimensions to pixels.
        // screenSize = (worldSize / dist) * (screenHeight / (2 * tan(fovY/2)))
        float dist = distance(args.cameraPosition, p.xyz);
        float projFactor = args.screenHeight / (2.0 * tan(args.fovY * 0.5));
        screenWidth = (width / max(dist, 0.001)) * projFactor;
        screenHeight = (height / max(dist, 0.001)) * projFactor;
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
    d.worldPos       = p.xyz;
    d.screenWidth    = screenWidth;
    d.color          = color;
    d.packedEntityId = packedId;
    d.screenHeight   = screenHeight;
    d.textureIndex   = pc.value.textureIndex;
    d.samplerIndex   = pc.value.samplerIndex;
    d.uvRect         = uvRect;
    outBuf.data[slot] = d;
}
