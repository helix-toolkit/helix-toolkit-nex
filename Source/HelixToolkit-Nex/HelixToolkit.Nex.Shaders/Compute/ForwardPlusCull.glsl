#include "Headers/HeaderCompute.glsl"
#include "Headers/LightStructs.glsl"

layout(buffer_reference, scalar) buffer LightGridBuffer {
    uvec2 tiles[]; // x=lightCount, y=lightIndexOffset
};

layout(buffer_reference, scalar) buffer LightIndexBuffer {
    uint indices[];
};

layout(buffer_reference, scalar) buffer GlobalCounterBuffer {
    uint globalLightIndexCounter;
};

layout(push_constant) uniform LightCullingConstants {
    mat4 inverseProjection;
    vec2 screenDimensions;
    vec2 tileCount;
    uint lightCount;
    float zNear;
    float zFar;
    uint depthTextureIndex;
    uint samplerIndex;
    uint64_t lightBuffer;
    uint64_t lightGridBuffer;
    uint64_t lightIndexBuffer;
    uint64_t globalCounterBuffer;
} pc;

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

// Tile frustum structure
struct Frustum {
    vec4 planes[4]; // left, right, top, bottom
    float minDepth;
    float maxDepth;
};

// Shared memory for tile
shared uint visibleLightCount;
shared uint visibleLightIndices[MAX_LIGHTS_PER_TILE];
shared uint minDepthInt;
shared uint maxDepthInt;
shared Frustum tileFrustum;
shared uint globalOffset;

// Helper functions
// Convert depth buffer value to view space Z
float depthToViewZ(float depth) {
    vec4 clipPos = vec4(0.0, 0.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = transpose(pc.inverseProjection) * clipPos;
    return viewPos.z / viewPos.w;
}

// Build frustum for tile
Frustum createTileFrustum(uvec2 tileID) {
    Frustum frustum;
            
    // Compute screen space bounds for this tile
    vec2 minScreen = vec2(tileID) * TILE_SIZE / pc.screenDimensions;
    vec2 maxScreen = vec2(tileID + 1) * TILE_SIZE / pc.screenDimensions;
            
    // Convert to NDC
    vec2 minNDC = minScreen * 2.0 - 1.0;
    vec2 maxNDC = maxScreen * 2.0 - 1.0;
            
    // Create frustum planes in view space
    vec4 corners[4];
    mat4 inverseProjection = transpose(pc.inverseProjection);
    corners[0] = inverseProjection * vec4(minNDC.x, minNDC.y, -1.0, 1.0);
    corners[1] = inverseProjection * vec4(maxNDC.x, minNDC.y, -1.0, 1.0);
    corners[2] = inverseProjection * vec4(maxNDC.x, maxNDC.y, -1.0, 1.0);
    corners[3] = inverseProjection * vec4(minNDC.x, maxNDC.y, -1.0, 1.0);
            
    for (int i = 0; i < 4; ++i) {
        corners[i] /= corners[i].w;
    }
            
    // Left plane
    vec3 edge = normalize(corners[3].xyz - corners[0].xyz);
    frustum.planes[0] = vec4(cross(edge, corners[0].xyz), 0.0);
            
    // Right plane
    edge = normalize(corners[1].xyz - corners[2].xyz);
    frustum.planes[1] = vec4(cross(corners[2].xyz, edge), 0.0);
            
    // Top plane
    edge = normalize(corners[2].xyz - corners[3].xyz);
    frustum.planes[2] = vec4(cross(corners[3].xyz, edge), 0.0);
            
    // Bottom plane
    edge = normalize(corners[0].xyz - corners[1].xyz);
    frustum.planes[3] = vec4(cross(edge, corners[0].xyz), 0.0);
            
    return frustum;
}

// Test if sphere intersects frustum
bool sphereInsideFrustum(vec3 center, float radius, Frustum frustum) {
    // Test against depth bounds
    if (center.z - radius > frustum.minDepth || center.z + radius < frustum.maxDepth) {
        return false;
    }
            
    // Test against frustum planes
    for (int i = 0; i < 4; ++i) {
        float distance = dot(frustum.planes[i].xyz, center) + frustum.planes[i].w;
        if (distance < -radius) {
            return false;
        }
    }
            
    return true;
}

// Main compute shader
void main() {
    uvec2 tileID = gl_WorkGroupID.xy;
    uvec2 localID = gl_LocalInvocationID.xy;
    uint localIndex = localID.y * TILE_SIZE + localID.x;
            
    // Initialize shared memory
    if (localIndex == 0) {
        visibleLightCount = 0;
        minDepthInt = 0xFFFFFFFF;
        maxDepthInt = 0;
        tileFrustum = createTileFrustum(tileID);
    }
            
    barrier();
            
    // Calculate min/max depth for this tile
    vec2 pixelPos = vec2(tileID * TILE_SIZE + localID);
    vec2 texCoord = pixelPos / pc.screenDimensions;
            
    float depth = textureBindless2D(pc.depthTextureIndex, pc.samplerIndex, texCoord).r;
    float viewZ = depthToViewZ(depth);
            
    atomicMin(minDepthInt, floatBitsToUint(viewZ));
    atomicMax(maxDepthInt, floatBitsToUint(viewZ));
            
    barrier();
            
    if (localIndex == 0) {
        tileFrustum.minDepth = uintBitsToFloat(minDepthInt);
        tileFrustum.maxDepth = uintBitsToFloat(maxDepthInt);
    }
            
    barrier();
            
    // Cull lights against tile frustum
    uint threadLightCount = (pc.lightCount + (TILE_SIZE * TILE_SIZE) - 1) / (TILE_SIZE * TILE_SIZE);
    uint lightStart = localIndex * threadLightCount;
    uint lightEnd = min(lightStart + threadLightCount, pc.lightCount);
            
    for (uint i = lightStart; i < lightEnd; ++i) {
        Light light = pc.lightBuffer.lights[i];
                
        // Skip directional lights (they affect all tiles)
        if (light.type == 0) {
            uint index = atomicAdd(visibleLightCount, 1);
            if (index < MAX_LIGHTS_PER_TILE) {
                visibleLightIndices[index] = i;
            }
            continue;
        }
                
        // Test point and spot lights
        if (sphereInsideFrustum(light.position, light.range, tileFrustum)) {
            uint index = atomicAdd(visibleLightCount, 1);
            if (index < MAX_LIGHTS_PER_TILE) {
                visibleLightIndices[index] = i;
            }
        }
    }
            
    barrier();
            
    // Write results
    uint count = min(visibleLightCount, MAX_LIGHTS_PER_TILE);
    if (localIndex == 0) {
        uint tileIndex = tileID.y * uint(pc.tileCount.x) + tileID.x;
        globalOffset = atomicAdd(pc.globalCounterBuffer.globalLightIndexCounter, count);
        pc.lightGridBuffer.tiles[tileIndex] = uvec2(count, globalOffset);
    }
    barrier();
    // Write light indices
    for (uint i = localIndex; i < count; i += TILE_SIZE * TILE_SIZE) {
        pc.lightIndexBuffer.indices[globalOffset + i] = visibleLightIndices[i];
    }
}
