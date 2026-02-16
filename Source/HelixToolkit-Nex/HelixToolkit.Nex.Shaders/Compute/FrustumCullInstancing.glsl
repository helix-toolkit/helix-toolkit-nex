#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/FrustumCullingCommon.glsl"
#include "HxHeaders/MeshDraw.glsl"
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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshInfoBuffer {
    MeshInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) buffer MeshDrawBuffer {
    MeshDraw draws[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstancingBuffer {
    mat4 instances[];
};

layout(buffer_reference, scalar) writeonly buffer VisableInstanceIndexBuffer {
    uint indices[];
};

// ------------------------------------------------------------------
// PUSH CONSTANTS
// ------------------------------------------------------------------
@code_gen
struct FrustumCullInstancingPC {
    uint64_t cullingConstAddress;
    uint drawCommandIdx;
    uint instanceCount;
};

layout(push_constant) uniform CullingPC {
    FrustumCullInstancingPC value;
} pc;

// Constants
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------

void main() {
    uint gID = gl_GlobalInvocationID.x;

    // Access Constants
    CullingConstBuffer cullingConst = CullingConstBuffer(pc.value.cullingConstAddress);
    if (cullingConst.value.cullingEnabled == 0) {
       return;
    }
    
    if (gID >= pc.value.instanceCount) {
        return;
    }
    // Access Buffers
    MeshDrawBuffer meshDrawBuf = MeshDrawBuffer(cullingConst.value.meshDrawBufferAddress);
    MeshDraw draw = meshDrawBuf.draws[pc.value.drawCommandIdx];
    if (draw.instancingBufferAddress == 0) {
        return;
    }

    bool isVisible = true;

    InstancingBuffer instBuf = InstancingBuffer(draw.instancingBufferAddress);
    MeshInfoBuffer meshInfoBuf = MeshInfoBuffer(cullingConst.value.meshInfoBufferAddress);
    MeshInfo bound = meshInfoBuf.value[draw.meshId];

    // Frustum Culling
    mat4 worldMatrix = instBuf.instances[gID] * draw.transform;
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
    // We can use subgroup ops to compact atomic writes
    uvec4 ballot = subgroupBallot(isVisible);
    uint count = subgroupBallotBitCount(ballot);
    if (count > 0) {
        if (draw.instancingIndexBufferAddress == 0) {
            // Error: Instancing Index Buffer is required for output
            return;
        }
        // Leader (first active thread) allocates space in output buffer
        uint baseIndex = 0;
        if (subgroupElect()) {
            baseIndex =  atomicAdd(meshDrawBuf.draws[pc.value.drawCommandIdx].instanceCount, count);
        }
        baseIndex = subgroupBroadcastFirst(baseIndex);

        if (isVisible) {
            uint offset = subgroupBallotExclusiveBitCount(ballot);
            uint outIndex = baseIndex + offset;
            
            // If using Visibility Buffer (List of Instances):
            if (draw.instancingIndexBufferAddress != 0) {
                VisableInstanceIndexBuffer visBuf = VisableInstanceIndexBuffer(draw.instancingIndexBufferAddress);
                visBuf.indices[outIndex] = gID;
            }
        }
    }
}
