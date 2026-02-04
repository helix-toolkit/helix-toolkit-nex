#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/ModelMatrixStruct.glsl"
#include "HxHeaders/DrawIndexIndirectCommand.glsl"
#include "HxHeaders/MeshDraw.glsl"
#include "HxHeaders/FrustumCullingCommon.glsl"
// Enable subgroup extensions for efficient output compaction if allowed
#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_vote : enable

// ------------------------------------------------------------------
// BUFFER REFERENCES
// ------------------------------------------------------------------

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer CullingConstBuffer {
    CullingConstants value;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshBoundBuffer {
    MeshBoundData value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) buffer DrawCommandBuffer {
    DrawIndexedIndirectCommand commands[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshDrawBuffer {
    MeshDraw draws[];
};

layout(buffer_reference, scalar) writeonly buffer VisableDrawCommandsBuffer {
    DrawIndexedIndirectCommand commands[];
};

layout(buffer_reference, scalar) buffer DrawCountBuffer {
    uint count;
};

// ------------------------------------------------------------------
// PUSH CONSTANTS
// ------------------------------------------------------------------

layout(push_constant) uniform CullingPC {
    uint64_t cullingConstAddress;
} pc;

// Constants
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------

void main() {
    uint gID = gl_GlobalInvocationID.x;

    // Access Constants
    CullingConstBuffer cullingConst = CullingConstBuffer(pc.cullingConstAddress);
    if (cullingConst.value.cullingEnabled == 0) { // Early out if culling is disabled
       return;
    }
    if (gID >= cullingConst.value.instanceCount) { // Ensure valid access
        return;
    }
    DrawCommandBuffer drawCmdBuf = DrawCommandBuffer(cullingConst.value.drawCommandBufferAddress);
    DrawIndexedIndirectCommand cmd = drawCmdBuf.commands[gID];
    // Access Buffers
    MeshDrawBuffer meshDrawBuf = MeshDrawBuffer(cullingConst.value.meshDrawBufferAddress);
    
    MeshDraw draw = meshDrawBuf.draws[cmd.meshDrawIndex];
    if (draw.instancingBufferAddress != 0) {
        // For instanced draws, we handle seperately.
        return;
    }

    ModelMatrixBuffer modelMatrixBuf = ModelMatrixBuffer(cullingConst.value.modelMatrixBufferAddress);
    MeshBoundBuffer meshBoundBuf = MeshBoundBuffer(cullingConst.value.meshBoundBufferAddress);

    MeshBoundData bound = meshBoundBuf.value[draw.meshId];
    bool isVisible = true;

    // Frustum Culling
    mat4 worldMatrix = modelMatrixBuf.models[draw.modelId];
    // 1. Sphere Culling (Cheap, fast reject)
    // Transform local sphere center to world
    // Note: Scale is baked into world matrix rows, so simple mult works for uniform scale
    // For non-uniform scale, this approximation might require max scale component
        
    vec3 worldSphereCenter = (worldMatrix * vec4(bound.sphereCenter, 1.0)).xyz;
        
    // Extract max scale from world matrix for radius scaling
    float maxScale = max(max(length(worldMatrix[0].xyz), length(worldMatrix[1].xyz)), length(worldMatrix[2].xyz));
    float worldRadius = bound.sphereRadius * maxScale;

    // Use planeCount from constants (default to 6 if not set properly, but usually 5 for infinite far)
    uint pCount = cullingConst.value.planeCount == 0 ? 6 : cullingConst.value.planeCount;

    // Transform to View Space for Distance/ScreenSize checks
    vec3 viewSphereCenter = (cullingConst.value.viewMatrix * vec4(worldSphereCenter, 1.0)).xyz;

    if (!IsVisibleByDistance(viewSphereCenter, worldRadius, cullingConst.value.maxDrawDistance)) {
        isVisible = false;
    }
    else if (!IsVisibleByScreenSize(viewSphereCenter, worldRadius, cullingConst.value.minScreenSize, 1.0)) {
        isVisible = false;
    }
    else if (!IsSphereVisible(worldSphereCenter, worldRadius, cullingConst.value.frustumPlanes, pCount)) {
        isVisible = false;
    } 
    else {
        // 2. Box Culling (More accurate for elongated objects)
        vec3 boxCenter = (bound.boxMin + bound.boxMax) * 0.5;
        vec3 boxExtents = (bound.boxMax - bound.boxMin) * 0.5;
            
        if (!IsBoxVisible(boxCenter, boxExtents, worldMatrix, cullingConst.value.frustumPlanes, pCount)) {
            isVisible = false;
        }
    }

    // Occlusion Culling (Placeholder / Suggestion)
    // ---------------------------------------------------------
    // if (isVisible && cullingConst.value.occlusionEnabled != 0) {
    //    // SUGGESTION: Hierarchical Z-Buffer (HZB) Occlusion Culling
    //    // 1. Project AABB to Screen Space AABB (minXY, maxXY, minZ).
    //    // 2. Calculate Screen Size (width, height).
    //    // 3. Select HZB mip level based on size.
    //    // 4. Bind HZB texture (bindless) and sample depth.
    //    // 5. Compare AABB minZ vs HZB Depth. If box is behind depth buffer, cull.
    // }
    // ---------------------------------------------------------

    // Output visibility
    if (cullingConst.value.culledDrawCommandBufferAddress == 0 || 
        cullingConst.value.drawCountBufferAddress == 0) {
        drawCmdBuf.commands[gID].instanceCount = isVisible ? 1 : 0;
        return;
    }
    // We can use subgroup ops to compact atomic writes
    uvec4 ballot = subgroupBallot(isVisible);
    uint count = subgroupBallotBitCount(ballot);
    if (count > 0) {
        // Leader (first active thread) allocates space in output buffer
        uint baseIndex = 0;
        if (subgroupElect()) {
            DrawCountBuffer countBuf = DrawCountBuffer(cullingConst.value.drawCountBufferAddress);
            baseIndex = atomicAdd(countBuf.count, count);
        }
        baseIndex = subgroupBroadcastFirst(baseIndex);

        if (isVisible) {
            uint offset = subgroupBallotExclusiveBitCount(ballot);
            uint outIndex = baseIndex + offset;
            
            VisableDrawCommandsBuffer visBuf = VisableDrawCommandsBuffer(cullingConst.value.culledDrawCommandBufferAddress);

            visBuf.commands[outIndex] = cmd;
        }
    }
}
