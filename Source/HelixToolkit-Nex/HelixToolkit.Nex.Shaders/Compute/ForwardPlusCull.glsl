#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_arithmetic : enable
#extension GL_EXT_debug_printf : enable
@code_gen
/*
 * Forward+ Light Culling Compute Shader
 * -------------------------------------
 * Performs tile-based light culling using a Compute Shader.
 * 
 * Optimization Strategies Used:
 * 1. Parallel Reduction: Tiled organization allows massive parallelism.
 * 2. Min/Max Depth Reduction: Atomic operations find the depth range of the tile surface.
 * 3. Frustum/AABB Intersections: Efficient geometric tests to cull lights.
 * 4. Subgroup Intrinsics: Uses subgroup operations (ballot, arithmetic) to reduce atomic memory contention on shared memory.
 * 5. Branchless Loop Logic: Vectorized AABB checks.
 */

struct LightCullingConstants {
    mat4 viewMatrix;
    mat4 projection;
    mat4 inverseProjection;
    vec2 screenDimensions;
    uint tileCountX; // Total tile counts on X axis
    uint tileCountY; // Total tile counts on Y axis
    uint lightCount; // Light counts
    float zNear;
    float zFar;
    uint depthTextureIndex;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t globalCounterBufferAddress;
    uint samplerIndex;
    uint maxLightsPerTile;
    vec2 _padding;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LightCullingConst {
    LightCullingConstants value;
};

layout(buffer_reference, scalar) buffer LightGridBuffer {
    LightGridTile tiles[];
};

layout(buffer_reference, scalar) buffer LightIndexBuffer {
    uint indices[];
};

layout(buffer_reference, scalar) buffer GlobalCounterBuffer {
    uint value;
};

layout(push_constant) uniform LightCullingPC {
  uint64_t lightCullingConstAddress;
} pc;

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

LightCullingConst cullingConst = LightCullingConst(pc.lightCullingConstAddress);

struct AABB {
    vec3 minBounds;
    vec3 maxBounds;
};


// --- Shared Memory ---
shared uint sharedMinDepth;
shared uint sharedMaxDepth;
shared uint sharedLightCount;
shared uint sharedLightIndices[MAX_LIGHTS_PER_TILE];
shared AABB sharedTileAABB;
shared vec4 sharedFrustumPlanes[4]; // Left, Right, Top, Bottom

// --- Helper Functions ---

float depthToViewZ(float depth) {
    // Reversed-Z Infinite Perspective: vZ = n / d (assuming P[3][2] is 'n')
    // We use matrix elements for robustness across different projection types
    return -cullingConst.value.projection[3][2] / (depth + cullingConst.value.projection[2][2]);
}

vec3 screenToView(vec2 screenPos, float zView) {
    vec2 ndc = (screenPos / cullingConst.value.screenDimensions) * 2.0 - 1.0;
    // Vulkan NDC Y is usually pointing down, handle based on your proj matrix
    return vec3(ndc.x / cullingConst.value.projection[0][0], ndc.y / cullingConst.value.projection[1][1], -1) * -zView;
}

// Optimized sphere-AABB intersection test (branchless)
// Returns true if a sphere intersects or is inside an AABB
bool sqSphereAABBIntersect(in vec3 center, float radius, in AABB tile) {
    // Calculate the vector from the closest point on the AABB to the center
    // max(0, v) ensures we only accumulate distance if the center is outside the bounds
    vec3 d = max(vec3(0.0), tile.minBounds - center) + max(vec3(0.0), center - tile.maxBounds);
    
    // Squared distance check avoids expensive square root
    float sqDist = dot(d, d);
    return sqDist <= (radius * radius);
}
// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------
void main() {
    vec2 pixelCoord = (vec2(gl_GlobalInvocationID.xy) + vec2(0.5)) / cullingConst.value.screenDimensions;
    ivec2 tileID = ivec2(gl_WorkGroupID.xy);
    uint threadIdx = gl_LocalInvocationIndex;

    // 1. Initialize Shared Data
    // Thread 0 initializes the shared values for the tile
    if (threadIdx == 0) {
        sharedMinDepth = 0xFFFFFFFF; // Initialize with max uint (acts as +inf for min reduction)
        sharedMaxDepth = 0;          // Initialize with 0 (acts as 0.0 for max reduction)
        sharedLightCount = 0;
    }

    // Calculate view-space frustum planes for the tile (Thread 0-3)
    if (threadIdx < 4) {
        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);
        
        // Corners in View Space at Z = -1.0
        vec3 tl = screenToView(vec2(tilePixelsMin.x, tilePixelsMax.y), 1);
        vec3 tr = screenToView(vec2(tilePixelsMax.x, tilePixelsMax.y), 1);
        vec3 bl = screenToView(vec2(tilePixelsMin.x, tilePixelsMin.y), 1);
        vec3 br = screenToView(vec2(tilePixelsMax.x, tilePixelsMin.y), 1);

        // Plane normals pointing inside the frustum
        if (threadIdx == 0) sharedFrustumPlanes[0] = vec4(normalize(cross(bl, tl)), 0); // Left
        if (threadIdx == 1) sharedFrustumPlanes[1] = vec4(normalize(cross(tr, br)), 0); // Right
        if (threadIdx == 2) sharedFrustumPlanes[2] = vec4(normalize(cross(tl, tr)), 0); // Top
        if (threadIdx == 3) sharedFrustumPlanes[3] = vec4(normalize(cross(br, bl)), 0); // Bottom
    }
    barrier();

    // 2. Depth Reduction (Atomic)
    // Sample depth buffer to find min/max depth for the tile
    float depth = textureBindless2D(cullingConst.value.depthTextureIndex, cullingConst.value.samplerIndex, pixelCoord).r;
    
    // Subgroup Optimization for Depth Reduction
    // -----------------------------------------
    // Instead of every thread performing atomicMin/Max on shared memory (high contention),
    // we use subgroup intrinsics to reduce values within the warp/wavefront first.
    // This reduces the number of atomic operations to shared memory by a factor of 32 or 64.
    
    if (depth > 0.0) { // Skip skybox/background
        uint zInt = floatBitsToUint(depth);
        
        // Reduce within the subgroup (only considers active threads that passed the if-check)
        uint sMin = subgroupMin(zInt);
        uint sMax = subgroupMax(zInt);
        
        // One thread per subgroup updates the shared memory
        if (subgroupElect()) {
            atomicMin(sharedMinDepth, sMin);
            atomicMax(sharedMaxDepth, sMax);
        }
    }
    // We don't return early here if depth is 0, because this thread still participates in light culling loop
    barrier();

    if (threadIdx == 0) {
        // 3. Construct AABB for Tile
        // Convert reduced depth values back to View Space
        float minDepth = uintBitsToFloat(sharedMinDepth);
        float maxDepth = uintBitsToFloat(sharedMaxDepth);
        
        // Note: minDepth (smallest value) corresponds to Far plane in Reversed-Z (1.0=Near, 0.0=Far)
        float viewZFar = depthToViewZ(minDepth); 
        float viewZNear = depthToViewZ(maxDepth);

        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);

        // Get View Space XY bounds at near and far planes determined by tile depth range
        vec3 p0 = screenToView(tilePixelsMin, viewZNear);
        vec3 p1 = screenToView(tilePixelsMax, viewZNear);
        vec3 p2 = screenToView(tilePixelsMin, viewZFar);
        vec3 p3 = screenToView(tilePixelsMax, viewZFar);

        AABB tileAABB;
        // Compute AABB encompassing the frustum slice
        tileAABB.minBounds = vec3(min(min(p0.x, p1.x), min(p2.x, p3.x)),
                                  min(min(p0.y, p1.y), min(p2.y, p3.y)),
                                  viewZFar); // View-Z is negative, so Far is Min (-large)
        tileAABB.maxBounds = vec3(max(max(p0.x, p1.x), max(p2.x, p3.x)),
                                  max(max(p0.y, p1.y), max(p2.y, p3.y)),
                                  viewZNear); // View-Z is negative, so Near is Max (-small)
        sharedTileAABB = tileAABB;
    }
    barrier();
    
    // 3. Cull lights against tile frustum
    LightBuffer lightBuffer = LightBuffer(cullingConst.value.lightBufferAddress);
    
    // Iterate over lights in parallel
    // TILE_SIZE*TILE_SIZE is the total number of threads in the workgroup
    for (uint i = threadIdx; i < cullingConst.value.lightCount; i += (TILE_SIZE * TILE_SIZE)) {
        Light light = lightBuffer.lights[i];
        
        bool visible = false;

        if (light.type != 0) { // Skip Directional Light (0)
            // Convert Range Light Position (World) to Frustum Space (View)
            vec3 lightPosView = (cullingConst.value.viewMatrix * vec4(light.position, 1.0)).xyz;
            
            bool insideFrustum = true;
            // Test against 4 frustum planes
            for (int j = 0; j < 4; ++j) {
                if (dot(sharedFrustumPlanes[j].xyz, lightPosView) < -light.range) {
                    insideFrustum = false;
                    break;
                }
            }

            // Test against Tile AABB (Depth bounds)
            if (insideFrustum && sqSphereAABBIntersect(lightPosView, light.range, sharedTileAABB)) {
                visible = true;
            }
        }

        // --- Subgroup Optimization ---
        // Instead of atomicAdd per active thread, we aggregate active threads in the subgroup
        // and perform a single atomicAdd per subgroup.
        
        uvec4 ballot = subgroupBallot(visible);
        uint count = subgroupBallotBitCount(ballot);
        uint indexInSubgroup = subgroupBallotExclusiveBitCount(ballot);
        
        uint baseIndex = 0;
        // The first active lane in the subgroup performs the atomic allocation
        if (subgroupElect()) {
            if (count > 0) {
                baseIndex = atomicAdd(sharedLightCount, count);
            }
        }
        // Broadcast the base index to all lanes in the subgroup
        baseIndex = subgroupBroadcastFirst(baseIndex);
        
        if (visible) {
            uint dstIndex = baseIndex + indexInSubgroup;
            if (dstIndex < MAX_LIGHTS_PER_TILE) {
                sharedLightIndices[dstIndex] = i;
            }
        }
    }
    barrier();

    // 4. Write Light Grid
    // Parallelize the write of indices to global memory instead of using a single thread
    uint tileIdx = tileID.y * cullingConst.value.tileCountX + tileID.x;
    uint visibleCount = min(sharedLightCount, MAX_LIGHTS_PER_TILE);

    if (threadIdx == 0) {
        // Only thread 0 writes the tile header (count and offset)
        LightGridBuffer lightGridBuffer = LightGridBuffer(cullingConst.value.lightGridBufferAddress);
        
        LightGridTile tile;
        tile.lightCount = visibleCount;
        tile.lightIndexOffset = tileIdx * MAX_LIGHTS_PER_TILE;
        lightGridBuffer.tiles[tileIdx] = tile;
    }

    // All threads participate in writing the indices
    LightIndexBuffer lightIndexBuffer = LightIndexBuffer(cullingConst.value.lightIndexBufferAddress);
    uint globalOffset = tileIdx * MAX_LIGHTS_PER_TILE;

    for (uint i = threadIdx; i < visibleCount; i += (TILE_SIZE * TILE_SIZE)) {
        lightIndexBuffer.indices[globalOffset + i] = sharedLightIndices[i];
    }
}
