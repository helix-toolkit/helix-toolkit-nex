#include "HxHeaders/HeaderCompute.glsl"
#include "Point/PointStructs.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointPosBuffer {
    vec4 points[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointColorBuffer {
    vec4 colors[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) writeonly buffer PointDrawDataBuffer {
    PointDrawData data[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) buffer IndirectArgsBuffer {
    PointDrawIndirectArgs args;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointExpandBuffer {
    PointExpandArgs value;
};

layout(push_constant) uniform PC {
    PointExpandPC value;       // GPU address of PointExpandArgs buffer
} pc;

PointExpandArgs args = PointExpandBuffer(pc.value.argsAddress).value;

void main() {
    uint idx = gl_GlobalInvocationID.x;

    if (idx >= pc.value.pointCount) return;
    float size = pc.value.size;
    if (size <= 0.0) return; // Cull zero-size points early

    IndirectArgsBuffer   cmdBuf = IndirectArgsBuffer(pc.value.indirectArgsAddress);
    PointDrawDataBuffer  outBuf = PointDrawDataBuffer(pc.value.drawDataAddress);
    PointPosBuffer      posBuf  = PointPosBuffer(pc.value.pointPosAddress);

    vec4 p = vec4(posBuf.points[idx].xyz, 1);

    vec4 color = pc.value.color;
    if (pc.value.pointColorAddress != 0u) {
        PointColorBuffer colBuf = PointColorBuffer(pc.value.pointColorAddress);
        color = colBuf.colors[idx];
    }

    // --- Frustum cull (clip-space) ---
    vec4 clip = args.viewProjection * p;
    float w = clip.w;
    // Behind camera (reversed-Z: w < 0 means behind near plane)
    if (w <= 0.0) return;
    // Outside frustum (with padding for point size)
    float padding = size * 2.0; // generous padding
    vec4 clipAbs = abs(clip);
    if (clipAbs.x > w + padding || clipAbs.y > w + padding) return;

    // --- Compute screen-space size ---
    float screenSize;
    if (pc.value.fixedSize != 0u) {
        // Fixed-size mode: size is already in pixels, no perspective projection.
        screenSize = size;
    } else {
        // World-space mode: project world-space diameter to pixels.
        // screenSize = (worldSize / dist) * (screenHeight / (2 * tan(fovY/2)))
        float dist = distance(args.cameraPosition, p.xyz);
        float projFactor = args.screenHeight / (2.0 * tan(args.fovY * 0.5));
        screenSize = (size / max(dist, 0.001)) * projFactor;
    }

    // Screen-size cull
    if (screenSize < args.minScreenSize) return;

    // --- Pack entity ID ---
    uvec2 packedId = packObjectInfo(pc.value.worldId, pc.value.entityId, idx);
  
    // --- Allocate output slot ---

    uint slot = atomicAdd(cmdBuf.args.instanceCount, 1);

    // --- Write draw data ---
    
    PointDrawData d;
    d.worldPos      = p.xyz;
    d.screenSize    = screenSize;
    d.color         = color;
    d.packedEntityId = vec2(uintBitsToFloat(packedId.x), uintBitsToFloat(packedId.y));
    d.textureIndex  = pc.value.textureIndex;
    d.samplerIndex  = pc.value.samplerIndex;
    outBuf.data[slot] = d;
}
