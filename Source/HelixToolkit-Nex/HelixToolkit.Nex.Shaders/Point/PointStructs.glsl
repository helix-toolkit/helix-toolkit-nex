/// Shared GPU structs for the point rendering pipeline.
/// The @code_gen annotation generates matching C# structs via the source generator.

/// Per-visible-point data written by the compute shader and read by the vertex shader.
@code_gen
struct PointDrawData {
    vec3  worldPos;       // World-space centre
    float screenSize;     // Final size in pixels (after projection)
    vec4  color;          // RGBA color
    vec2  packedEntityId; // Packed entity ID + point index for GPU picking
    uint  textureIndex;   // Bindless texture index (0 = no texture)
    uint  samplerIndex;   // Bindless sampler index
};

/// Indirect draw arguments for DrawIndirect (triangle strip, 4 verts, N instances).
@code_gen
struct PointDrawIndirectArgs {
    uint vertexCount;     // Always 4 (triangle strip quad)
    uint instanceCount;   // Visible point count (atomically incremented by compute)
    uint firstVertex;     // Always 0
    uint firstInstance;   // Always 0
};

/// Push constants for the point expansion compute shader.
@code_gen
struct PointExpandArgs {
    mat4     viewProjection;         // View * Projection matrix
    vec3     cameraPosition;         // Camera world-space position
    float    minScreenSize;          // Minimum screen size in pixels to render
    vec3     cameraRight;            // Camera right vector (world-space)
    float    screenHeight;           // Viewport height in pixels
    vec3     cameraUp;               // Camera up vector (world-space)
    float    fovY;                   // Vertical field of view in radians    
};

@code_gen
struct PointExpandPC {
    uint64_t argsAddress;            // GPU address of PointExpandArgs buffer
    uint64_t pointPosAddress;        // GPU address of point position input buffer
    uint64_t pointColorAddress;      // GPU address of point color input buffer
    uint64_t drawDataAddress;        // GPU address of PointDrawData output buffer
    uint64_t indirectArgsAddress;    // GPU address of PointDrawIndirectArgs buffer
    uint     pointCount;             // Total number of input points
    uint     fixedSize;              // Whether point size is fixed in screen space (ignore perspective and use size as pixels)
    uint     entityId;               // Entity ID for all points in this dispatch
    uint     entityVer;              // Entity version for all points in this dispatch
    uint     textureIndex;           // Bindless texture index (0 = no texture)
    uint     samplerIndex;           // Bindless sampler index
    vec4     color;                  // Uniform color for all points (overridden by per-point color if pointColorAddress is non-zero)
    float    size;                   // Uniform size for all points (in world units if fixedSize=0, or pixels if fixedSize=1)
    uint     _padding0;
    uint     _padding1;
    uint     _padding2;
};

/// Push constants for the point render vertex/fragment shaders.
@code_gen
struct PointRenderPC {
    uint64_t drawDataAddress;        // GPU address of PointDrawData buffer
    uint64_t fpConstAddress;         // GPU address of FPConstants buffer (for lighting shaders)
};
