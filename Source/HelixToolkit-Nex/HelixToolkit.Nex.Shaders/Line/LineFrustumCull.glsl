#include "HxHeaders/HeaderCompute.glsl"
#include "Line/LineStructs.glsl"
#include "HxHeaders/FrustumCullingCommon.glsl"
#include "HxHeaders/NodeInfo.glsl"
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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer NodeInfoBuffer {
    GpuNodeInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) buffer DrawBuffer {
    LineDraw draws[];
};

// ------------------------------------------------------------------
// PUSH CONSTANTS
// ------------------------------------------------------------------

layout(push_constant) uniform CullingPC {
    FrustumCullPC value;
} pc;

CullingConstBuffer cullingConst = CullingConstBuffer(pc.value.cullingConstAddress);
DrawBuffer meshDrawBuf = DrawBuffer(pc.value.meshDrawBufferAddress);

MeshInfoBuffer meshInfoBuf = MeshInfoBuffer(cullingConst.value.meshInfoBufferAddress);
NodeInfoBuffer nodeInfoBuf = NodeInfoBuffer(cullingConst.value.nodeInfoBufferAddress);

// Constants
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------

void main() {
    uint gID = gl_GlobalInvocationID.x;

    // Access Constants
    if (gID >= pc.value.instanceCount) { // Ensure valid access
        return;
    }

    // Access Buffers
  
    uint drawIdx = gID + pc.value.meshDrawIdxOffset;
    
    LineDraw draw = meshDrawBuf.draws[drawIdx];

    GpuNodeInfo info = nodeInfoBuf.value[draw.nodeInfoIndex];

    if (info.enabled == 0) {
        // If node is disabled, we can skip culling and set instance count to 0
        meshDrawBuf.draws[drawIdx].instanceCount = 0;
        return;
    }   

    if (cullingConst.value.cullingEnabled == 0 || draw.cullable == 0) {
        // Not cullable: render every segment. One quad instance per disjoint 2-vertex
        // segment, so instanceCount = lineCount.
        meshDrawBuf.draws[drawIdx].instanceCount = draw.lineCount;
        return;
    }


    MeshInfo bound = meshInfoBuf.value[draw.meshId];
    bool isVisible = true;

    // Frustum Culling
    mat4 worldMatrix = info.transform;
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

    // Output visibility: one quad instance per disjoint 2-vertex segment.
    meshDrawBuf.draws[drawIdx].instanceCount = isVisible ? draw.lineCount : 0;
}
