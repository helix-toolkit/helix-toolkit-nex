#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/ModelMatrixStruct.glsl"
#include "HxHeaders/MeshDraw.glsl"
// Enable subgroup extensions for efficient output compaction if allowed
#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_vote : enable

// ------------------------------------------------------------------
// STRUCTURES
// ------------------------------------------------------------------

// Indirect Draw Command (matches VkDrawIndexedIndirectCommand)
@code_gen
struct DrawIndexedIndirectCommand {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int vertexOffset;
    uint firstInstance;
};

@code_gen
struct MeshBoundData {
    vec3 boxMin;        // Local Space
    float _padding0;
    vec3 boxMax;        // Local Space
    float _padding1;
    vec3 sphereCenter;  // Local Space
    float sphereRadius; // Local Space
};

// Culling Constants
@code_gen
struct CullingConstants {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 viewProjectionMatrix;
    
    vec4 frustumPlanes[6];  // Left, Right, Top, Bottom, Near, Far
    
    uint instanceCount;     // Total instances to process
    uint cullingEnabled;    // 1 = Enable Frustum Culling, 0 = Pass through
    uint occlusionEnabled;  // 1 = Enable Occlusion Culling (reserved)
    uint planeCount;        // Number of planes to test (e.g., 5 for Infinite Far Plane, 6 for Standard)

    float maxDrawDistance;  // Max distance from camera (0.0 = disabled)
    float minScreenSize;    // Min projected size in NDC/Screen ratio (0.0 = disabled)
    uint _pad1;
    uint _pad2;

    // Buffer Addresses
    uint64_t meshIdBufferAddress;       // Input: uint[]
    uint64_t meshBoundBufferAddress;     // Input: MeshBoundData[]
    uint64_t modelMatrixBufferAddress;    // Input: Model Matrices
    uint64_t drawCommandBufferAddress;    // In/Out: DrawIndexedIndirectCommand[]
    uint64_t visibilityBufferAddress;     // Output: uint[] (visible instance indices, optional)
    uint64_t drawCountBufferAddress;      // Output: uint (visible count)
};

// ------------------------------------------------------------------
// BUFFER REFERENCES
// ------------------------------------------------------------------

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer CullingConstBuffer {
    CullingConstants value;
};

layout(buffer_reference, scalar) readonly buffer MeshIdBuffer {
    uint value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshBoundBuffer {
    MeshBoundData value[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) buffer DrawCommandBuffer {
    DrawIndexedIndirectCommand commands[];
};

layout(buffer_reference, scalar) writeonly buffer VisibilityBuffer {
    uint visibleIndices[];
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
// FRUSTUM CULLING FUNCTIONS
// ------------------------------------------------------------------

// Plane: .xyz = normal, .w = distance
// Distance from point to plane = dot(plane.xyz, point) + plane.w
// If distance < -radius, object is outside

bool IsVisibleByDistance(in vec3 viewCenter, float radius, float maxDistance) {
    if (maxDistance <= 0.0) return true;
    // Simple check: closest point on sphere vs max distance
    // Using simple center distance is usually enough, but strictly: center - radius
    float dist = length(viewCenter);
    // If the closest point of the sphere is further than maxDistance
    return (dist - radius) < maxDistance;
}

// Check if projected sphere size is too small
bool IsVisibleByScreenSize(in vec3 viewCenter, float radius, float minScreenSize, float geometricMeanFov) {
    if (minScreenSize <= 0.0) return true;
    
    float dist = length(viewCenter);
    // Approximate projected radius: (radius / distance) * scaling_factor
    // geometricMeanFov comes from projection matrix e.g. (P[0][0] + P[1][1]) * 0.5 or passed in constants
    // For now, simpler approximation: solid angle or just projected size
    
    // Projected Diameter approx ~ (2 * radius) / z
    // We check against threshold
    // Use abs(viewCenter.z) for depth preventing division by zero
    float depth = abs(viewCenter.z);
    
    // If we're too close (inside sphere), don't cull
    if (depth < radius) return true;

    // Simple projection ratio: Radius / Depth
    // If (Radius / Depth) < Threshold, it's too small
    return (radius / depth) >= minScreenSize;
}

bool IsSphereVisible(in vec3 center, float radius, in vec4 planes[6], uint planeCount) {
    // Clamp to 6 just in case
    uint count = min(planeCount, 6);
    for (uint i = 0; i < count; i++) {
        if (dot(planes[i].xyz, center) + planes[i].w < -radius) {
            return false;
        }
    }
    return true;
}

// OBB Culling against planes
// Based on: transforming AABB center to world, and projecting extents onto plane normal
bool IsBoxVisible(in vec3 center, in vec3 extents, in mat4 world, in vec4 planes[6], uint planeCount) {
    // Transform center to world space
    vec3 worldCenter = (world * vec4(center, 1.0)).xyz;
    
    // Right, Up, Forward vectors from World Matrix (scaled)
    vec3 right = world[0].xyz;
    vec3 up    = world[1].xyz;
    vec3 fwd   = world[2].xyz;

    uint count = min(planeCount, 6);
    for (uint i = 0; i < count; i++) {
        vec3 normal = planes[i].xyz;
        
        // Project extents onto plane normal
        // This gives us the "radius" of the box along the normal direction
        float projectedRadius = 
            abs(dot(normal, right)) * extents.x +
            abs(dot(normal, up))    * extents.y +
            abs(dot(normal, fwd))   * extents.z;
            
        // Distance from box center to plane
        float dist = dot(normal, worldCenter) + planes[i].w;
        
        // If center is behind plane by more than projected radius, it is cullled
        if (dist < -projectedRadius) {
            return false;
        }
    }
    return true;
}

// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------

void main() {
    uint gID = gl_GlobalInvocationID.x;

    // Access Constants
    CullingConstBuffer cullingConst = CullingConstBuffer(pc.cullingConstAddress);
    
    if (gID >= cullingConst.value.instanceCount) {
        return;
    }

    // Access Buffers
    DrawCommandBuffer drawCmdBuf = DrawCommandBuffer(cullingConst.value.drawCommandBufferAddress);
    ModelMatrixBuffer modelMatrixBuf = ModelMatrixBuffer(cullingConst.value.modelMatrixBufferAddress);
    MeshIdBuffer meshIdBuf = MeshIdBuffer(cullingConst.value.meshIdBufferAddress);
    MeshBoundBuffer meshBoundBuf = MeshBoundBuffer(cullingConst.value.meshBoundBufferAddress);
    
    // Fetch Instance
    uint meshId = meshIdBuf.value[gID];
    MeshBoundData bound = meshBoundBuf.value[meshId];
    
    bool isVisible = true;

    // Frustum Culling
    if (cullingConst.value.cullingEnabled != 0) {
        mat4 worldMatrix = modelMatrixBuf.models[gID];
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
            
            // Output to Draw Command Buffer (assuming indirect cull)
            // We update instanceCount from 0 (if reset) to 1, or just fill a compact buffer of commands
            
            // Option A: Write visible instances to a packed Draw Command Buffer
            // This assumes we are regenerating the draw buffer every frame
            DrawIndexedIndirectCommand cmd;
            cmd.indexCount = 0; // Filled from Mesh Info/Template or separate buffer?
            // Usually we copy a template command or we just output the instance index
            // If fetching mesh info is needed (indexCount, firstIndex), we need a MeshBuffer.
            
            // Option B: Multi-Draw Indirect with Instance Count = 1 or 0
            // We just update the 'instanceCount' field of existing commands?
            // If gID maps 1:1 to draw commands.
            
            // For the requested "Frustum Culling", a common output is a list of Draw Commands 
            // OR a compacted list of Instance Indices for instanced rendering.
            
            // Implementation: Copy command from template or update count.
            // Let's assume we copy the command from the source (if provided) or just write visibility index.
            
            // If using Visibility Buffer (List of Instances):
            if (cullingConst.value.visibilityBufferAddress != 0) {
                VisibilityBuffer visBuf = VisibilityBuffer(cullingConst.value.visibilityBufferAddress);
                visBuf.visibleIndices[outIndex] = gID;
            }
            
            // If updating Draw Commands:
            if (cullingConst.value.drawCommandBufferAddress != 0) {
                 // Example: Just setting instanceCount = 1 for visible, 0 for invisible.
                 // This requires 1:1 mapping and no compaction.
                 // If compaction is desired, we write to drawCmdBuf.commands[outIndex].
                 
                 // Since we don't have a template commands buffer in input, 
                 // we assume we might be generating them for "MeshId" if we had that info.
                 
                 // For now, let's write to the VisibilityBuffer as the primary output,
                 // which is typical for "GPU Driven Rendering" where a subsequent pass consumes indices.
            }
        }
    }
    
    // Non-compacted output (if we just want to zero out invisible draw commands)
    // Only works if we have 1:1 mapping gID -> DrawCommand
    if (cullingConst.value.drawCommandBufferAddress != 0 && cullingConst.value.visibilityBufferAddress == 0) {
        // If we are modifying commands in place (simplest form avoiding atomic compaction):
        drawCmdBuf.commands[gID].instanceCount = isVisible ? 1 : 0;
    }
}
