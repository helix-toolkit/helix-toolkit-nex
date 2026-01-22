#include "Headers/HeaderCompute.glsl"
#include "Headers/LightStruct.glsl"

layout(buffer_reference, scalar) buffer LightGridBuffer {
    uvec2 tiles[]; // x=lightCount, y=lightIndexOffset
};

layout(buffer_reference, scalar) buffer LightIndexBuffer {
    uint indices[];
};

layout(buffer_reference, scalar) buffer GlobalCounterBuffer {
    uint globalLightIndexCounter;
};

@code_gen
struct LightCullingConstants {
    mat4 viewMatrix;
    mat4 inverseProjection;
    vec2 screenDimensions;
    vec2 tileCount;
    uint lightCount;
    float zNear;
    float zFar;
    uint depthTextureIndex;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t globalCounterBufferAddress;
    uint samplerIndex;
    vec3 _padding;
};

layout(push_constant) uniform LightCullingPC {
  LightCullingConstants value;
} pc;

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

// Tile frustum structure
struct Frustum {
    vec4 planes[4]; // left, right, top, bottom
    float minDepth;
    float maxDepth;
};

// ------------------------------------------------------------------
// SHARED MEMORY
// ------------------------------------------------------------------
shared uint visibleLightCount;
shared uint visibleLightIndices[MAX_LIGHTS_PER_TILE];
shared uint minDepthInt;
shared uint maxDepthInt;
shared Frustum tileFrustum;
shared uint globalOffset;

// ------------------------------------------------------------------
// HELPER FUNCTIONS
// ------------------------------------------------------------------

// Convert depth buffer value to view space Z
// Note: This assumes standard OpenGL -Z View Space
float depthToViewZ(float depth) {
    vec4 clipPos = vec4(0.0, 0.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = pc.value.inverseProjection * clipPos; 
    return viewPos.z / viewPos.w;
}

// Build frustum for tile in VIEW SPACE (Camera at 0,0,0)
Frustum createTileFrustum(uvec2 tileID) {
    Frustum frustum;
    
    vec2 minScreen = vec2(tileID) * TILE_SIZE / pc.value.screenDimensions;
    vec2 maxScreen = vec2(tileID + 1) * TILE_SIZE / pc.value.screenDimensions;
    
    vec2 minNDC = minScreen * 2.0 - 1.0;
    vec2 maxNDC = maxScreen * 2.0 - 1.0;
    
    // Create frustum corners in View Space
    mat4 inverseProjection = pc.value.inverseProjection;
    
    vec4 corners[4];
    corners[0] = inverseProjection * vec4(minNDC.x, minNDC.y, -1.0, 1.0); // BL
    corners[1] = inverseProjection * vec4(maxNDC.x, minNDC.y, -1.0, 1.0); // BR
    corners[2] = inverseProjection * vec4(maxNDC.x, maxNDC.y, -1.0, 1.0); // TR
    corners[3] = inverseProjection * vec4(minNDC.x, maxNDC.y, -1.0, 1.0); // TL
    
    for (int i = 0; i < 4; ++i) {
        corners[i] /= corners[i].w;
    }
    
    // Left plane
    vec3 edge = normalize(corners[3].xyz - corners[0].xyz);
    frustum.planes[0] = vec4(cross(corners[0].xyz, edge), 0.0);
    
    // Right plane
    edge = normalize(corners[1].xyz - corners[2].xyz);
    frustum.planes[1] = vec4(cross(corners[2].xyz, edge), 0.0);
    
    // Top plane
    edge = normalize(corners[2].xyz - corners[3].xyz);
    frustum.planes[2] = vec4(cross(corners[3].xyz, edge), 0.0);
    
    // Bottom plane
    edge = normalize(corners[0].xyz - corners[1].xyz);
    frustum.planes[3] = vec4(cross(corners[1].xyz, edge), 0.0);
    
    return frustum;
}

// Test if sphere intersects frustum
bool sphereInsideFrustum(vec3 center, float radius, Frustum frustum) {
    // Note: In standard View Space, Z is negative (e.g., -10).
    // minDepth is the "Near" Z (closest to 0, e.g. -2)
    // maxDepth is the "Far" Z (furthest from 0, e.g. -100)
    
    // Check if object is closer than Near Plane (center.z > minDepth)
    // or further than Far Plane (center.z < maxDepth)
    if (center.z - radius > frustum.minDepth || center.z + radius < frustum.maxDepth) {
        return false;
    }
    
    // Test against frustum planes
    for (int i = 0; i < 4; ++i) {
        float distance = dot(frustum.planes[i].xyz, center) + frustum.planes[i].w;
        // If distance is negative, we are outside the plane (because normal points In)
        if (distance < -radius) {
            return false;
        }
    }
    return true;
}

// ------------------------------------------------------------------
// MAIN
// ------------------------------------------------------------------
void main() {
    uvec2 tileID = gl_WorkGroupID.xy;
    uvec2 localID = gl_LocalInvocationID.xy;
    uint localIndex = localID.y * TILE_SIZE + localID.x;
    
    // 1. Initialize shared memory
    if (localIndex == 0) {
        visibleLightCount = 0;
        minDepthInt = 0xFFFFFFFF; // Init to Max Uint
        maxDepthInt = 0;          // Init to Min Uint
        tileFrustum = createTileFrustum(tileID);
    }
    
    barrier();
    
    // 2. Calculate min/max depth for this tile
    // [FIX]: Sample pixel center (+0.5) to avoid edge artifacts
    vec2 pixelPos = vec2(tileID * TILE_SIZE + localID);
    vec2 texCoord = (pixelPos + vec2(0.5)) / pc.value.screenDimensions;
    
    float depth = textureBindless2D(pc.value.depthTextureIndex, pc.value.samplerIndex, texCoord).r;
    float viewZ = depthToViewZ(depth);
    
    // Note: FloatBitsToUint maintains order for positive floats.
    // For negative floats (View Z), larger magnitude = larger Uint.
    // atomicMin finds SMALLEST Uint -> Smallest magnitude negative -> Near Z.
    // atomicMax finds LARGEST Uint -> Largest magnitude negative -> Far Z.
    // This logic works for standard Negative-Z View Space.
    atomicMin(minDepthInt, floatBitsToUint(viewZ));
    atomicMax(maxDepthInt, floatBitsToUint(viewZ));
    
    barrier();
    
    if (localIndex == 0) {
        tileFrustum.minDepth = uintBitsToFloat(minDepthInt);
        tileFrustum.maxDepth = uintBitsToFloat(maxDepthInt);
    }
    
    barrier();
    
    // 3. Cull lights against tile frustum
    uint threadLightCount = (pc.value.lightCount + (TILE_SIZE * TILE_SIZE) - 1) / (TILE_SIZE * TILE_SIZE);
    uint lightStart = localIndex * threadLightCount;
    uint lightEnd = min(lightStart + threadLightCount, pc.value.lightCount);

    LightBuffer lightBuffer = LightBuffer(pc.value.lightBufferAddress);
    
    for (uint i = lightStart; i < lightEnd; ++i) {
        Light light = lightBuffer.lights[i];
        
        // Skip directional lights (handled in lighting shader usually)
        if (light.type == 0) {
            // Optional: If you really want them here, uncomment this.
            // But usually directional lights are global.
            uint index = atomicAdd(visibleLightCount, 1);
            if (index < MAX_LIGHTS_PER_TILE)
                 visibleLightIndices[index] = i;
            continue;
        }
        
        // Convert Light Position (World) to Frustum Space (View)
        // Optimization: viewMatrix is usually rigid, so length/radius is preserved.
        // We do not need to transform the radius.
        vec3 lightPosView = (pc.value.viewMatrix * vec4(light.position, 1.0)).xyz;
        
        // Test point and spot lights
        // Note: For Spot lights, this only tests the bounding sphere.
        // For tighter culling, you would check the cone against the frustum here.
        if (sphereInsideFrustum(lightPosView, light.range, tileFrustum)) {
            uint index = atomicAdd(visibleLightCount, 1);
            if (index < MAX_LIGHTS_PER_TILE) {
                visibleLightIndices[index] = i;
            }
        }
    }
    
    barrier();
    
    // 4. Write results
    GlobalCounterBuffer glCounterBuf = GlobalCounterBuffer(pc.value.globalCounterBufferAddress);
    LightGridBuffer lightGridBuffer = LightGridBuffer(pc.value.lightGridBufferAddress);
    LightIndexBuffer lightIndexBuffer = LightIndexBuffer(pc.value.lightIndexBufferAddress);
    
    uint count = min(visibleLightCount, MAX_LIGHTS_PER_TILE);
    
    if (localIndex == 0) {
        uint tileIndex = tileID.y * uint(pc.value.tileCount.x) + tileID.x;
        globalOffset = atomicAdd(glCounterBuf.globalLightIndexCounter, count);
        lightGridBuffer.tiles[tileIndex] = uvec2(count, globalOffset);
    }
    
    barrier();
    
    // Write light indices
    for (uint i = localIndex; i < count; i += TILE_SIZE * TILE_SIZE) {
        lightIndexBuffer.indices[globalOffset + i] = visibleLightIndices[i];
    }
}
