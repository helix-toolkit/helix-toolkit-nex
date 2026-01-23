#include "Headers/HeaderCompute.glsl"
#include "Headers/LightStruct.glsl"

#extension GL_KHR_shader_subgroup_basic : enable
#extension GL_KHR_shader_subgroup_ballot : enable
#extension GL_KHR_shader_subgroup_arithmetic : enable

@code_gen
struct LightCullingConstants {
    mat4 viewMatrix;
    mat4 inverseProjection;
    vec2 screenDimensions;
    uint tileCountX;
    uint tileCountY;
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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LightCullingConst {
    LightCullingConstants value;
};

layout(buffer_reference, scalar) buffer LightGridBuffer {
    uvec2 tiles[]; // x=lightCount, y=lightIndexOffset
};

layout(buffer_reference, scalar) buffer LightIndexBuffer {
    uint indices[];
};

layout(buffer_reference, scalar) buffer GlobalCounterBuffer {
    uint globalLightIndexCounter;
};

layout(push_constant) uniform LightCullingPC {
  uint64_t lightCullingConstAddress;
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

LightCullingConst cullingConst = LightCullingConst(pc.lightCullingConstAddress);

// Convert depth buffer value to view space Z
// Note: This assumes standard OpenGL -Z View Space
float depthToViewZ(float depth) {
    // Handling BOTH standard Z [0..1] and Inverse Z [1..0]
    // The projection matrix (or inverse proj) handles the depth mapping.
    // If using Vulkan Convention [0..1] with InvZ: Near->1, Far->0.
    // If using Vulkan Convention [0..1] with StdZ: Near->0, Far->1.
    //
    // The "clipPos" should reconstruct NDC Z correctly. 
    // BUT 'depth' is [0, 1] raw depth from texture.
    // In Vulkan/DX, Z_ndc = depth.
    
    // NEW CODE: Using [0, 1] Z input.
    // Whether this is "Standard" or "Inverse" is baked into 'inverseProjection'.
    // If InverseZ was used, depth=1 maps to Near, depth=0 maps to Far via InvProj.
    
    vec4 clipPos = vec4(0.0, 0.0, depth, 1.0); 
    vec4 viewPos = cullingConst.value.inverseProjection * clipPos; 
    return viewPos.z / viewPos.w;
}

// Build frustum for tile in VIEW SPACE (Camera at 0,0,0)
Frustum createTileFrustum(uvec2 tileID) {
    Frustum frustum;
    
    vec2 minScreen = vec2(tileID) * TILE_SIZE / cullingConst.value.screenDimensions;
    vec2 maxScreen = vec2(tileID + 1) * TILE_SIZE / cullingConst.value.screenDimensions;
    
    vec2 minNDC = minScreen * 2.0 - 1.0;
    vec2 maxNDC = maxScreen * 2.0 - 1.0;
    
    // Create frustum corners in View Space
    mat4 inverseProjection = cullingConst.value.inverseProjection;
    
    // For Z, we pick 0.0 and 1.0 representing the Near/Far planes in Clip Space [0,1]
    // The inverse projection will unproject them to the correct View Z (Near/Far).
    // If Inverse Z is used: 1.0 -> Near View Z, 0.0 -> Far View Z.
    // If Standard Z is used: 0.0 -> Near View Z, 1.0 -> Far View Z.
    // We define frustum planes based on the SIDES, so the Z depth of these points 
    // matters less for the side planes, but strictly speaking we need valid points.
    // Using 0.0 (Near-ish/Far-ish) and 1.0 (Far-ish/Near-ish).
    
    vec4 corners[4];
    // Using simple depth 1.0 for "far end" of the ray reconstruction or 0.0?
    // Actually, to build the side planes (left/right/top/bottom), we need rays from eye.
    // Eye is at 0,0,0 in View Space.
    // We can unproject a point at Z_clip=1.0 (or anything non-WEIRD like w=0).
    
    corners[0] = inverseProjection * vec4(minNDC.x, minNDC.y, 1.0, 1.0); // BL
    corners[1] = inverseProjection * vec4(maxNDC.x, minNDC.y, 1.0, 1.0); // BR
    corners[2] = inverseProjection * vec4(maxNDC.x, maxNDC.y, 1.0, 1.0); // TR
    corners[3] = inverseProjection * vec4(minNDC.x, maxNDC.y, 1.0, 1.0); // TL
    
    for (int i = 0; i < 4; ++i) {
        corners[i] /= corners[i].w;
    }
    
    // Use Eye (0,0,0) to corners to build planes
    // Left plane (O, BL, TL) -> (0, c[0], c[3])
    // The previous code used corners[0]..corners[3] as "points on the far plane".
    // Left: plane through O, c[3], c[0]
    // Normal = cross(corners[0].xyz, corners[3].xyz)
    
    // Check winding:
    // BL (bottom-left) to TL (top-left).
    // cross(BL, TL-BL)
    // BL is (-x, -y, -z). TL is (-x, +y, -z).
    // TL-BL is (0, +y, 0).
    // (-x,-y,-z) x (0,1,0) = (z, 0, -x).
    // If z is negative (View Space), z is -, -x is positive.
    // Normal (-, 0, +). Points Right-ish/Inward?
    // Left plane normal should point INWARD to the frustum.
    // Yes, seems consistent with the original code logic.
    
    // Left plane
    vec3 edge = normalize(corners[3].xyz - corners[0].xyz);
    frustum.planes[0] = vec4(normalize(cross(corners[0].xyz, edge)), 0.0);
    
    // Right plane
    edge = normalize(corners[1].xyz - corners[2].xyz);
    frustum.planes[1] = vec4(normalize(cross(corners[2].xyz, edge)), 0.0);
    
    // Top plane
    edge = normalize(corners[2].xyz - corners[3].xyz);
    frustum.planes[2] = vec4(normalize(cross(corners[3].xyz, edge)), 0.0);
    
    // Bottom plane
    edge = normalize(corners[0].xyz - corners[1].xyz);
    frustum.planes[3] = vec4(normalize(cross(corners[1].xyz, edge)), 0.0);
    
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
    vec2 texCoord = (pixelPos + vec2(0.5)) / cullingConst.value.screenDimensions;
    
    float depth = textureBindless2D(cullingConst.value.depthTextureIndex, cullingConst.value.samplerIndex, texCoord).r;
    float viewZ = depthToViewZ(depth);
    
    // Note: FloatBitsToUint maintains order for positive floats.
    // For negative floats (View Z), larger magnitude = larger Uint.
    // atomicMin finds SMALLEST Uint -> Smallest magnitude negative -> Near Z.
    // atomicMax finds LARGEST Uint -> Largest magnitude negative -> Far Z.
    // This logic works for standard Negative-Z View Space.

    // OPTIMIZATION: Use subgroup reductions to reduce contention on shared memory
    // viewZ is negative in standard view space. 
    // subgroupMin(-100, -0.1) -> -100 (Far). subgroupMax(-100, -0.1) -> -0.1 (Near).
    // atomicMin on uint rep of negative floats wants Smallest Uint -> Smallest Magnitude -> Near Z.
    // atomicMax on uint rep of negative floats wants Largest Uint -> Largest Magnitude -> Far Z.
    
    float farZ = subgroupMin(viewZ); // Most negative
    float nearZ = subgroupMax(viewZ); // Least negative
    
    if (subgroupElect()) {
        atomicMin(minDepthInt, floatBitsToUint(nearZ));
        atomicMax(maxDepthInt, floatBitsToUint(farZ));
    }
    
    barrier();
    
    if (localIndex == 0) {
        tileFrustum.minDepth = uintBitsToFloat(minDepthInt);
        tileFrustum.maxDepth = uintBitsToFloat(maxDepthInt);
    }
    
    barrier();
    
    // 3. Cull lights against tile frustum
    LightBuffer lightBuffer = LightBuffer(cullingConst.value.lightBufferAddress);

    // OPTIMIZATION: Stride loop by workgroup size and use subgroup ballot
    uint workGroupSize = TILE_SIZE * TILE_SIZE; 

    for (uint i = 0; i < cullingConst.value.lightCount; i += workGroupSize) {
        uint lightIndex = i + localIndex;
        bool isVisible = false;

        if (lightIndex < cullingConst.value.lightCount) {
            Light light = lightBuffer.lights[lightIndex];
            if (light.type == 0) {
                // Directional lights affect all tiles
                isVisible = true;
            } else {
                // Convert Range Light Position (World) to Frustum Space (View)
                vec3 lightPosView = (cullingConst.value.viewMatrix * vec4(light.position, 1.0)).xyz;
                
                if (sphereInsideFrustum(lightPosView, light.range, tileFrustum)) {
                    isVisible = true;
                }
            }
        }

        uvec4 ballot = subgroupBallot(isVisible);
        uint count = subgroupBallotBitCount(ballot);

        if (count > 0) {
            uint baseIndex = 0;
            if (subgroupElect()) {
                baseIndex = atomicAdd(visibleLightCount, count);
            }
            baseIndex = subgroupBroadcastFirst(baseIndex);

            if (isVisible) {
                uint offset = subgroupBallotExclusiveBitCount(ballot);
                uint idx = baseIndex + offset;
                if (idx < MAX_LIGHTS_PER_TILE) {
                    visibleLightIndices[idx] = lightIndex;
                }
            }
        }
    }
    
    barrier();
    
    // 4. Write results
    GlobalCounterBuffer glCounterBuf = GlobalCounterBuffer(cullingConst.value.globalCounterBufferAddress);
    LightGridBuffer lightGridBuffer = LightGridBuffer(cullingConst.value.lightGridBufferAddress);
    LightIndexBuffer lightIndexBuffer = LightIndexBuffer(cullingConst.value.lightIndexBufferAddress);
    
    uint count = min(visibleLightCount, MAX_LIGHTS_PER_TILE);
    
    if (localIndex == 0) {
        uint tileIndex = tileID.y * uint(cullingConst.value.tileCountX) + tileID.x;
        globalOffset = atomicAdd(glCounterBuf.globalLightIndexCounter, count);
        lightGridBuffer.tiles[tileIndex] = uvec2(count, globalOffset);
    }
    
    barrier();
    
    // Write light indices
    for (uint i = localIndex; i < count; i += TILE_SIZE * TILE_SIZE) {
        lightIndexBuffer.indices[globalOffset + i] = visibleLightIndices[i];
    }
}
