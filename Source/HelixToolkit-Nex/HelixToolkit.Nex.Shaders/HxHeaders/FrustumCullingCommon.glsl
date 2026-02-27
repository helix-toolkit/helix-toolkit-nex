#include "HxHeaders/MeshInfo.glsl"

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
    uint meshDrawIdxOffset; // Offset in MeshDraw buffer for this culling batch (if processing in chunks)
    uint _padding1;

    uint64_t meshInfoBufferAddress;     // Input: MeshInfo[]
    uint64_t meshDrawBufferAddress;      // Input: MeshDraw[]
};

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
