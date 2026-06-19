#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_arithmetic : enable
#extension GL_EXT_debug_printf : enable
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
@code_gen
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
    uint samplerIndex;
    uint maxLightsPerTile;
    uint enableAABBCulling;
    uint enableDepthMaskCulling;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t _padding; // keeps the struct 16-byte aligned (272 bytes total)
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LightCullingConst {
    LightCullingConstants value;
};

layout(buffer_reference, scalar, buffer_reference_align = 8) buffer LightGridBuffer {
    LightGridTile tiles[];
};

layout(buffer_reference, scalar, buffer_reference_align = 2) buffer LightIndexBuffer {
    uint16_t indices[];
};

layout(buffer_reference, scalar) buffer GlobalCounterBuffer {
    uint value;
};

layout(push_constant) uniform LightCullingPC {
  uint64_t lightCullingConstAddress;
} pc;

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

LightCullingConst cullingConst = LightCullingConst(pc.lightCullingConstAddress);

LightBuffer lightBuffer = LightBuffer(cullingConst.value.lightBufferAddress);
LightIndexBuffer lightIndexBuffer = LightIndexBuffer(cullingConst.value.lightIndexBufferAddress);
LightGridBuffer lightGridBuffer = LightGridBuffer(cullingConst.value.lightGridBufferAddress);

struct AABB {
    vec3 minBounds;
    vec3 maxBounds;
};


// --- Shared Memory ---
shared uint sharedMinDepth;
shared uint sharedMaxDepth;
shared uint sharedLightCount;
shared uint sharedDepthMask;
shared uint sharedLightIndices[MAX_LIGHTS_PER_TILE];
shared AABB sharedTileAABB;
shared vec4 sharedFrustumPlanes[4]; // Left, Right, Top, Bottom

// Transparent-only (frustum-only, full-depth-range) accumulator.
// Holds the frustum-visible Range_Light indices that are NOT in the opaque set,
// built from the same tile frustum planes spanning the full near-to-far depth
// range, completely independent of the depth buffer. At write-out this list is
// emitted directly after the opaque prefix so the combined region holds the full
// frustum-visible union (opaque-visible first, then transparent-only).
shared uint sharedLightCountTransparent;
shared uint sharedLightIndicesTransparent[MAX_LIGHTS_PER_TILE];
// Tile bound spanning the full frustum depth range (zNear..zFar), used by the
// transparent spot-cone test so it never depends on the opaque depth data.
shared AABB sharedTileAABBFull;

// --- Helper Functions ---

float depthToViewZ(float depth) {
    // Reversed-Z Infinite Perspective: vZ = n / d (assuming P[3][2] is 'n')
    // We use matrix elements for robustness across different projection types
    return -cullingConst.value.projection[3][2] / (depth + cullingConst.value.projection[2][2]);
}

float viewZToDepth(float z) {
    // Inverse of depthToViewZ
    // depth = -P[3][2] / z - P[2][2]
    return -cullingConst.value.projection[3][2] / z - cullingConst.value.projection[2][2];
}

vec3 screenToView(vec2 screenPos, float zView) {
    vec2 ndc = (screenPos / cullingConst.value.screenDimensions) * 2.0 - 1.0;
    // Vulkan NDC Y is usually pointing down, handle based on your proj matrix
    return vec3(ndc.x / cullingConst.value.projection[0][0], ndc.y / cullingConst.value.projection[1][1], -1) * -zView;
}

bool frustumSphereIntersect(in vec3 center, float radius, in vec4 planes[4]) {
    // Test sphere against 4 frustum planes
    for (int i = 0; i < 4; ++i) {
        float distance = dot(planes[i].xyz, center);
        if (distance < -radius) {
            return false; // Outside this plane
        }
    }
    return true; // Inside or intersecting all planes
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

// Spot-cone visibility test against an arbitrary tile bound.
// Returns false only when the bounding sphere of the tile is provably outside
// the spot light's outer cone. Passing a larger bound (e.g. the full-depth tile
// bound) makes the test strictly more permissive, which preserves the
// containment relationship between the opaque and transparent lists.
bool spotConeVisible(in vec3 lightPosView, in Light light, in AABB bound) {
    vec3 boundCenter = (bound.minBounds + bound.maxBounds) * 0.5;
    float boundRadius = length(bound.maxBounds - boundCenter);

    vec3 lightDirView = normalize(mat3(cullingConst.value.viewMatrix) * light.direction);
    vec3 V = boundCenter - lightPosView;
    float d2 = dot(V, V);
    float d = sqrt(d2);

    if (d > boundRadius) {
        float cosTheta = dot(V / d, lightDirView);
        float cosPhi = light.spotAngles.y; // Outer cone cosine

        float sinPhi = sqrt(max(0.0, 1.0 - cosPhi * cosPhi));
        float sinAlpha = boundRadius / d;
        float cosAlpha = sqrt(max(0.0, 1.0 - sinAlpha * sinAlpha));

        if (cosTheta < (cosPhi * cosAlpha - sinPhi * sinAlpha)) {
            return false;
        }
    }
    return true;
}

// 2.5D depth-mask overlap test (opaque list only).
// Returns true when the light's view-space depth extent overlaps at least one
// set slice of the tile's segmented depth mask. Matches the legacy behavior of
// leaving the light visible when the light range falls outside the tile range or
// when the tile depth range is degenerate.
bool depthMaskOverlap(in vec3 lightPosView, float range) {
    // Light bounds in view space Z (view-space Z is negative looking down -Z).
    float lightZNear = lightPosView.z + range; // Closer to camera
    float lightZFar = lightPosView.z - range;  // Farther from camera

    float d1 = viewZToDepth(lightZNear);
    float d2 = viewZToDepth(lightZFar);

    float lightDepthMin = min(d1, d2);
    float lightDepthMax = max(d1, d2);

    float tMin = uintBitsToFloat(sharedMinDepth);
    float tMax = uintBitsToFloat(sharedMaxDepth);

    // Intersect light depth range with tile depth range.
    float intersectionMin = max(lightDepthMin, tMin);
    float intersectionMax = min(lightDepthMax, tMax);

    if (intersectionMin <= intersectionMax) {
        float range2 = tMax - tMin;
        if (range2 > 1e-6) {
            uint bitMin = uint(clamp((intersectionMin - tMin) / range2 * 32.0, 0.0, 31.0));
            uint bitMax = uint(clamp((intersectionMax - tMin) / range2 * 32.0, 0.0, 31.0));

            uint numBits = bitMax - bitMin + 1;
            uint lightMask = (numBits == 32) ? 0xFFFFFFFF : ((1u << numBits) - 1u) << bitMin;

            if ((sharedDepthMask & lightMask) == 0u) {
                return false;
            }
        }
    }
    return true;
}
// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------
void main() {
    vec2 pixelCoord = (vec2(gl_GlobalInvocationID.xy) + vec2(0.5)) / cullingConst.value.screenDimensions;
    pixelCoord.y = 1.0 - pixelCoord.y; // Flip Y for texture lookup
    ivec2 tileID = ivec2(gl_WorkGroupID.xy);
    uint threadIdx = gl_LocalInvocationIndex;

    // 1. Initialize Shared Data
    // Thread 0 initializes the shared values for the tile
    if (threadIdx == 0) {
        sharedMinDepth = 0xFFFFFFFF; // Initialize with max uint (acts as +inf for min reduction)
        sharedMaxDepth = 0;          // Initialize with 0 (acts as 0.0 for max reduction)
        sharedLightCount = 0;
        sharedDepthMask = 0;
        sharedLightCountTransparent = 0;
    }

    // Calculate view-space frustum planes for the tile (Thread 0-3)
    if (threadIdx < 4) {
        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);

        // Clamp tile pixel bounds to actual screen dimensions so that
        // edge tiles don't produce an oversized frustum.
        tilePixelsMax = min(tilePixelsMax, cullingConst.value.screenDimensions);
        
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

    // 2.5 Construct Depth Mask (Segmented Bitmask)
    // Now that we have the full min/max depth range for the tile, we can build the bitmask.
    // Each bit represents a slice of the depth range (minDepth to maxDepth).
    
    float minDepth = uintBitsToFloat(sharedMinDepth);
    float maxDepth = uintBitsToFloat(sharedMaxDepth);

    if (depth > 0.0) {
        // Map current pixel's depth to [0, 31] range within the tile's min/max bounds.
        // Reversed-Z: minDepth is farthest (smaller value), maxDepth is nearest (larger value).
        // Wait, normally minDepth (uint) corresponds to smaller float value which is Far in Reversed-Z.
        // Let's stick to the float values: minDepth (Far), maxDepth (Near).
        
        // Normalize depth to [0, 1] relative to tile range
        // If range is zero (flat surface), all go to bucket 0
        float range = maxDepth - minDepth;
        if (range > 1e-6) {
             // reversed-Z: 0 is far, 1 is near. 
             // We map (depth - minDepth) / range. 
             // minDepth is smaller value (closer to 0.0).
             float normalizedDepth = (depth - minDepth) / range;
             uint bitIndex = uint(min(normalizedDepth * 32.0, 31.0));
              
             // Atomically OR the bit into the mask
             atomicOr(sharedDepthMask, 1u << bitIndex);
        } else {
             atomicOr(sharedDepthMask, 1u);
        }
    }
    barrier();

    if (threadIdx == 0) {
        // 3. Construct AABB for Tile
        // Convert reduced depth values back to View Space
        // float minDepth = uintBitsToFloat(sharedMinDepth); // Already declared above
        // float maxDepth = uintBitsToFloat(sharedMaxDepth); // Already declared above
        
        // Note: minDepth (smallest value) corresponds to Far plane in Reversed-Z (1.0=Near, 0.0=Far)
        float viewZFar = depthToViewZ(minDepth); 
        float viewZNear = depthToViewZ(maxDepth);

        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);

        // Clamp tile pixel bounds to actual screen dimensions for AABB construction
        tilePixelsMax = min(tilePixelsMax, cullingConst.value.screenDimensions);

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

        // Build a second tile bound that spans the FULL frustum depth range
        // (camera near..far), independent of the depth buffer. This is the bound
        // used by the transparent list's spot-cone test so it never depends on the
        // opaque depth reduction.
        float viewZNearFull = -cullingConst.value.zNear; // View-Z is negative
        float viewZFarFull = -cullingConst.value.zFar;

        vec3 f0 = screenToView(tilePixelsMin, viewZNearFull);
        vec3 f1 = screenToView(tilePixelsMax, viewZNearFull);
        vec3 f2 = screenToView(tilePixelsMin, viewZFarFull);
        vec3 f3 = screenToView(tilePixelsMax, viewZFarFull);

        AABB tileAABBFull;
        tileAABBFull.minBounds = vec3(min(min(f0.x, f1.x), min(f2.x, f3.x)),
                                      min(min(f0.y, f1.y), min(f2.y, f3.y)),
                                      viewZFarFull); // Far is Min (-large)
        tileAABBFull.maxBounds = vec3(max(max(f0.x, f1.x), max(f2.x, f3.x)),
                                      max(max(f0.y, f1.y), max(f2.y, f3.y)),
                                      viewZNearFull); // Near is Max (-small)
        sharedTileAABBFull = tileAABBFull;
    }
    barrier();
    



    // Iterate over lights in parallel
    // TILE_SIZE*TILE_SIZE is the total number of threads in the workgroup
    for (uint i = threadIdx; i < cullingConst.value.lightCount; i += (TILE_SIZE * TILE_SIZE)) {
        Light light = lightBuffer.lights[i];

        // frustumVisible drives the transparent list (frustum planes + spot cone over
        // the full-depth tile bound, depth-buffer independent).
        // opaqueVisible drives the opaque list and additionally applies the toggle-gated
        // AABB and depth-mask tests plus the tile-AABB spot-cone test.
        bool frustumVisible = false;
        bool opaqueVisible = false;

        if (light.type != 0) { // Skip Directional Light (0) for both lists
            // Convert Range Light Position (World) to Frustum Space (View)
            vec3 lightPosView = (cullingConst.value.viewMatrix * vec4(light.position, 1.0)).xyz;

            // Shared test: cull lights against the tile frustum side planes.
            bool insideFrustum = frustumSphereIntersect(lightPosView, light.range, sharedFrustumPlanes);

            if (insideFrustum) {
                // --- Transparent path ---
                // Spot cone evaluated against the full-depth tile bound (depth independent).
                frustumVisible = true;
                if (light.type == 2) { // Spot Light
                    frustumVisible = spotConeVisible(lightPosView, light, sharedTileAABBFull);
                }

                // --- Opaque path ---
                // opaqueVisible = frustumVisible && AABB (toggle) && depthMask (toggle) && tile-AABB spot cone.
                opaqueVisible = frustumVisible;

                if (opaqueVisible && cullingConst.value.enableAABBCulling != 0) {
                    opaqueVisible = sqSphereAABBIntersect(lightPosView, light.range, sharedTileAABB);
                }

                if (opaqueVisible && cullingConst.value.enableDepthMaskCulling != 0) {
                    opaqueVisible = depthMaskOverlap(lightPosView, light.range);
                }

                // Opaque list keeps its tile-AABB-based spot-cone test.
                if (opaqueVisible && light.type == 2) {
                    opaqueVisible = spotConeVisible(lightPosView, light, sharedTileAABB);
                }
            }
        }

        // --- Subgroup Optimization (independent compaction per list) ---
        // Instead of atomicAdd per active thread, aggregate active threads in the
        // subgroup and perform a single atomicAdd per subgroup, separately for each list.

        // Transparent-only list compaction (frustum-visible but NOT opaque-visible).
        // Classifying here lets the write-out emit the opaque list followed by the
        // transparent-only list as a single combined region where the opaque sub-list
        // is a strict prefix and the union (opaque + transparent-only) equals the full
        // frustum-visible set with no duplicates.
        bool transparentOnly = frustumVisible && !opaqueVisible;
        {
            uvec4 ballotT = subgroupBallot(transparentOnly);
            uint countT = subgroupBallotBitCount(ballotT);
            uint indexInSubgroupT = subgroupBallotExclusiveBitCount(ballotT);

            uint baseIndexT = 0;
            if (subgroupElect()) {
                if (countT > 0) {
                    baseIndexT = atomicAdd(sharedLightCountTransparent, countT);
                }
            }
            baseIndexT = subgroupBroadcastFirst(baseIndexT);

            if (transparentOnly) {
                uint dstIndex = baseIndexT + indexInSubgroupT;
                if (dstIndex < MAX_LIGHTS_PER_TILE) {
                    sharedLightIndicesTransparent[dstIndex] = i;
                }
            }
        }

        // Opaque list compaction (driven by opaqueVisible).
        {
            uvec4 ballot = subgroupBallot(opaqueVisible);
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

            if (opaqueVisible) {
                uint dstIndex = baseIndex + indexInSubgroup;
                if (dstIndex < MAX_LIGHTS_PER_TILE) {
                    sharedLightIndices[dstIndex] = i;
                }
            }
        }
    }
    barrier();

    // 4. Write Light Grid (single combined region)
    // The opaque-visible indices are written as a strict prefix, immediately
    // followed by the transparent-only indices, so the per-tile region holds the
    // full frustum-visible union ordered opaque-first. Both sub-counts are packed
    // into the single LightGridTile.lightCount (opaque in the low byte, transparent
    // union total in the high byte). Everything lives in one
    // [tileIdx*MAX_LIGHTS_PER_TILE, +MAX_LIGHTS_PER_TILE) range.
    uint tileIdx = tileID.y * cullingConst.value.tileCountX + tileID.x;
    uint offset = tileIdx * MAX_LIGHTS_PER_TILE;

    // Opaque count, truncated to the tile capacity.
    uint opaqueCount = min(sharedLightCount, MAX_LIGHTS_PER_TILE);
    // Transparent-only entries stored after the opaque prefix, clamped to the
    // remaining capacity so the combined region never exceeds MAX_LIGHTS_PER_TILE.
    uint remainingCapacity = MAX_LIGHTS_PER_TILE - opaqueCount;
    uint transparentOnlyCount = min(sharedLightCountTransparent, remainingCapacity);
    // Transparent (union) total: opaque prefix + transparent-only suffix.
    uint transparentCount = opaqueCount + transparentOnlyCount;

    if (threadIdx == 0) {
        // Only thread 0 writes the tile header. The opaque count occupies the low
        // byte and the transparent (union) count the high byte.     
        LightGridTile tile;
        tile.lightCount = packLightCount(opaqueCount, transparentCount);
        tile.lightIndexOffset = offset;
        lightGridBuffer.tiles[tileIdx] = tile;
    }

    // All threads participate in writing the combined index region.


    // Opaque-visible indices -> [offset, offset + opaqueCount).
    for (uint i = threadIdx; i < opaqueCount; i += (TILE_SIZE * TILE_SIZE)) {
        lightIndexBuffer.indices[offset + i] = uint16_t(sharedLightIndices[i]);
    }

    // Transparent-only indices -> [offset + opaqueCount, offset + transparentCount).
    for (uint i = threadIdx; i < transparentOnlyCount; i += (TILE_SIZE * TILE_SIZE)) {
        lightIndexBuffer.indices[offset + opaqueCount + i] = uint16_t(sharedLightIndicesTransparent[i]);
    }
}
