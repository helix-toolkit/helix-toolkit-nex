#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_arithmetic : enable
#extension GL_EXT_debug_printf : enable
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
    return cullingConst.value.projection[3][2] / (depth + cullingConst.value.projection[2][2]);
}

vec3 screenToView(vec2 screenPos, float zView) {
    vec2 ndc = (screenPos / cullingConst.value.screenDimensions) * 2.0 - 1.0;
    // Vulkan NDC Y is usually pointing down, handle based on your proj matrix
    return vec3(ndc.x / cullingConst.value.projection[0][0], ndc.y / cullingConst.value.projection[1][1], -1) * -zView;
}

bool sqSphereAABBIntersect(in vec3 center, float radius, in AABB tile) {
    float sqDist = 0.0;
    for (int i = 0; i < 3; ++i) {
        float v = center[i];
        if (v < tile.minBounds[i]) {
            float d = tile.minBounds[i] - v;
            sqDist += d * d;
        }
        if (v > tile.maxBounds[i]) {
            float d = v - tile.maxBounds[i];
            sqDist += d * d;
        }
    }
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
    if (threadIdx == 0) {
        sharedMinDepth = 0xFFFFFFFF; // Reversed-Z: Far is 0.0
        sharedMaxDepth = 0;          // Reversed-Z: Near is 1.0
        sharedLightCount = 0;
    }

    if (threadIdx < 4) {
        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);
        
        // Corners in View Space at Z = -1.0
        vec3 tl = screenToView(vec2(tilePixelsMin.x, tilePixelsMax.y), 1);
        vec3 tr = screenToView(vec2(tilePixelsMax.x, tilePixelsMax.y), 1);
        vec3 bl = screenToView(vec2(tilePixelsMin.x, tilePixelsMin.y), 1);
        vec3 br = screenToView(vec2(tilePixelsMax.x, tilePixelsMin.y), 1);

        if (threadIdx == 0) sharedFrustumPlanes[0] = vec4(normalize(cross(bl, tl)), 0); // Left
        if (threadIdx == 1) sharedFrustumPlanes[1] = vec4(normalize(cross(tr, br)), 0); // Right
        if (threadIdx == 2) sharedFrustumPlanes[2] = vec4(normalize(cross(tl, tr)), 0); // Top
        if (threadIdx == 3) sharedFrustumPlanes[3] = vec4(normalize(cross(br, bl)), 0); // Bottom
    }
    barrier();

    // 2. Depth Reduction (Atomic)
    float depth = textureBindless2D(cullingConst.value.depthTextureIndex, cullingConst.value.samplerIndex, pixelCoord).r;
    if (depth > 0.0) { // Skip skybox/background
        uint zInt = floatBitsToUint(depth);
        atomicMin(sharedMinDepth, zInt);
        atomicMax(sharedMaxDepth, zInt);
    } else {
        return;
    }
    barrier();

    if (threadIdx == 0) {
        // 3. Construct AABB for Tile
        float minDepth = uintBitsToFloat(sharedMinDepth);
        float maxDepth = uintBitsToFloat(sharedMaxDepth);
    
        float viewZFar = -depthToViewZ(minDepth);
        float viewZNear = -depthToViewZ(maxDepth);

        vec2 tilePixelsMin = vec2(tileID * TILE_SIZE);
        vec2 tilePixelsMax = vec2((tileID + 1) * TILE_SIZE);

        // Get XY bounds at near and far planes
        vec3 p0 = screenToView(tilePixelsMin, viewZNear);
        vec3 p1 = screenToView(tilePixelsMax, viewZNear);
        vec3 p2 = screenToView(tilePixelsMin, viewZFar);
        vec3 p3 = screenToView(tilePixelsMax, viewZFar);

        AABB tileAABB;
        tileAABB.minBounds = vec3(min(min(p0.x, p1.x), min(p2.x, p3.x)),
                                  min(min(p0.y, p1.y), min(p2.y, p3.y)),
                                  viewZFar); // View-Z is negative, so Far is Min
        tileAABB.maxBounds = vec3(max(max(p0.x, p1.x), max(p2.x, p3.x)),
                                  max(max(p0.y, p1.y), max(p2.y, p3.y)),
                                  viewZNear);
        sharedTileAABB = tileAABB;
    }
    barrier();
    
    // 3. Cull lights against tile frustum
    LightBuffer lightBuffer = LightBuffer(cullingConst.value.lightBufferAddress);
    uint count = 0;
    for (uint i = threadIdx; i < cullingConst.value.lightCount; i += (TILE_SIZE * TILE_SIZE)) {
        bool inside = true;

        Light light = lightBuffer.lights[i];
        if (light.type == 0) { // Directional Light
            continue; // Directional lights are processed separately
        }
        // Convert Range Light Position (World) to Frustum Space (View)
        vec3 lightPosView = (cullingConst.value.viewMatrix * vec4(light.position, 1.0)).xyz;
        for (int j = 0; j < 4; ++j) {
            if (dot(sharedFrustumPlanes[j].xyz, lightPosView) < -light.range) {
                inside = false;
                break;
            }
        }

        if (inside && sqSphereAABBIntersect(lightPosView, light.range, sharedTileAABB)) {
            uint index = atomicAdd(sharedLightCount, 1);
            if (index < MAX_LIGHTS_PER_TILE) {
                sharedLightIndices[index] = i;
            }
        }
    }
    barrier();
    if (threadIdx == 0) {
        GlobalCounterBuffer glCounterBuf = GlobalCounterBuffer(cullingConst.value.globalCounterBufferAddress);
        LightGridBuffer lightGridBuffer = LightGridBuffer(cullingConst.value.lightGridBufferAddress);
        LightIndexBuffer lightIndexBuffer = LightIndexBuffer(cullingConst.value.lightIndexBufferAddress);

        uint tileIdx = tileID.y * cullingConst.value.tileCountX + tileID.x;
        LightGridTile tile;
        tile.lightCount = min(sharedLightCount, MAX_LIGHTS_PER_TILE);;
        tile.lightIndexOffset = tileIdx * MAX_LIGHTS_PER_TILE;
        lightGridBuffer.tiles[tileIdx] = tile;

        for (uint i = 0; i < tile.lightCount; ++i) {
            lightIndexBuffer.indices[tileIdx * MAX_LIGHTS_PER_TILE + i] = sharedLightIndices[i];
        }
    }
}
