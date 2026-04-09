#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/HeaderVertex.glsl"
#include "Point/PointStructs.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PointDataBuffer {
    PointData points[];
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

    IndirectArgsBuffer   cmdBuf = IndirectArgsBuffer(args.indirectArgsAddress);
    PointDrawDataBuffer  outBuf = PointDrawDataBuffer(args.drawDataAddress);
    PointDataBuffer      inBuf  = PointDataBuffer(pc.value.pointDataAddress);

    PointData p = inBuf.points[idx];

    // --- Frustum cull (clip-space) ---
    vec4 clip = args.viewProjection * vec4(p.position, 1.0);
    float w = clip.w;
    // Behind camera (reversed-Z: w < 0 means behind near plane)
    if (w <= 0.0) return;
    // Outside frustum (with padding for point size)
    float padding = p.size * 2.0; // generous padding
    vec4 clipAbs = abs(clip);
    if (clipAbs.x > w + padding || clipAbs.y > w + padding) return;

    // --- Compute screen-space size ---
    float screenSize;
    if (pc.value.fixedSize != 0u) {
        // Fixed-size mode: p.size is already in pixels, no perspective projection.
        screenSize = p.size;
    } else {
        // World-space mode: project world-space diameter to pixels.
        // screenSize = (worldSize / dist) * (screenHeight / (2 * tan(fovY/2)))
        float dist = distance(args.cameraPosition, p.position);
        float projFactor = args.screenHeight / (2.0 * tan(args.fovY * 0.5));
        screenSize = (p.size / max(dist, 0.001)) * projFactor;
    }

    // Screen-size cull
    if (screenSize < args.minScreenSize) return;

    // --- Pack entity ID ---
    vec2 packedId = packEntityIdAndIndex(pc.value.entityId, pc.value.entityVer, idx);
  
    // --- Allocate output slot ---

    uint slot = atomicAdd(cmdBuf.args.instanceCount, 1);

    // --- Write draw data ---
    
    PointDrawData d;
    d.worldPos      = p.position;
    d.screenSize    = screenSize;
    d.color         = p.color;
    d.packedEntityId = packedId;
    d.textureIndex  = pc.value.textureIndex;
    d.samplerIndex  = pc.value.samplerIndex;
    outBuf.data[slot] = d;
}
