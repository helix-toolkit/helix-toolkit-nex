/// Shared GPU structs for the billboard rendering pipeline.
/// The @code_gen annotation generates matching C# structs via the source generator.

/// Per-billboard input vertex data stored in a single interleaved buffer.
/// Written by the CPU (BillboardGeometry), read by the compute expand shader.
@code_gen
struct BillboardVertex {
    vec4  position;        // xyz = world-space centre, w = 1
    vec2  size;            // x = width, y = height (world-space or pixels if fixedSize)
    uint  infoIndex;       // Index into BillboardInfo buffer, automatically set internally.
    uint  _padding1;       // Padding for vec4 alignment
    vec4  uvRect;          // Texture atlas sub-region (u_min, v_min, u_max, v_max)
    vec4  color;           // RGBA per-billboard color (0,0,0,0 = use uniform color from push constants)
};

/// Per-visible-billboard data written by the compute shader and read by the vertex shader.
@code_gen
struct BillboardDrawData {
    vec3  worldPos;        // World-space anchor position (shared for all glyphs in a text string)
    float screenWidth;     // Projected width in pixels
    vec4  color;           // RGBA color
    vec2  packedEntityId;  // Packed entity ID for GPU picking
    float screenHeight;    // Projected height in pixels
    uint  textureIndex;    // Bindless texture index (0 = no texture)
    uint  samplerIndex;    // Bindless sampler index
    uint  sdfAemrangePacked;    // packHalf2x16(aemrange) — precomputed MSDF em-range
    uint  sdfAtlasSizePacked;   // (uint(height) << 16) | uint(width) — atlas dimensions
    uint  sdfGlyphCellSizeBits; // floatBitsToUint(glyphCellSize) — glyph cell size
    vec4  uvRect;          // Texture atlas sub-region (u_min, v_min, u_max, v_max)
    vec2  pixelOffset;     // Per-glyph pixel offset from anchor position (in screen pixels)
    vec2  _drawPadding;    // Padding for vec4 alignment
};

/// Indirect draw arguments for DrawIndirect (triangle strip, 4 verts, N instances).
@code_gen
struct BillboardDrawIndirectArgs {
    uint vertexCount;      // Always 4 (triangle strip quad)
    uint instanceCount;    // Visible billboard count (atomically incremented by compute)
    uint firstVertex;      // Always 0
    uint firstInstance;    // Always 0
};

/// Shared camera state for the billboard expansion compute shader.
@code_gen
struct BillboardExpandArgs {
    mat4  viewProjection;  // View * Projection matrix
    vec3  cameraPosition;  // Camera world-space position
    float minScreenSize;   // Minimum screen size in pixels to render
    vec3  cameraRight;     // Camera right vector (world-space)
    float screenHeight;    // Viewport height in pixels
    vec3  cameraUp;        // Camera up vector (world-space)
    float fovY;            // Vertical field of view in radians
};

@code_gen
struct BillboardInfo {
    uint     fixedSize;                 // Whether size is fixed in screen space (ignore perspective and use width/height as pixels)
    uint     worldId;                   // World ID for all billboards in this dispatch (used for GPU picking)
    uint     entityId;                  // Entity ID for all billboards in this dispatch
    uint     _padding0;

    uint     textureIndex;              // Bindless texture index (0 = no texture)
    uint     samplerIndex;              // Bindless sampler index
    uint     axisConstrained;           // Whether to use axis-constrained orientation (0 = screen-aligned, non-zero = axis-constrained)
    float    sdfGlyphCellSize;          // MSDF atlas glyph cell size in atlas pixels (msdf-atlas-gen size)

    vec4     color;                     // Uniform color for all billboards (overridden by per-billboard color if color.a > 0)

    vec3     constraintAxis;            // World-space axis for axis-constrained mode
    float    sdfDistanceRange;          // MSDF atlas distance range in atlas pixels (msdf-atlas-gen distanceRange)

    float    sdfDistanceRangeMiddle;    // MSDF atlas distance range middle value (msdf-atlas-gen distanceRangeMiddle)
    float    sdfAtlasWidth;             // MSDF atlas texture width in pixels
    float    sdfAtlasHeight;            // MSDF atlas texture height in pixels
    uint     _padding1;                 // Padding for vec4 alignment

    mat4     worldTransform;            // Entity's WorldTransform matrix (identity for non-text billboards)
};

/// Push constants for the billboard expansion compute shader.
@code_gen
struct BillboardExpandPC {
    uint64_t argsAddress;               // GPU address of BillboardExpandArgs buffer
    uint64_t billboardVertexAddress;    // GPU address of BillboardVertex input buffer (single interleaved buffer)
    uint64_t drawDataAddress;           // GPU address of BillboardDrawData output buffer
    uint64_t indirectArgsAddress;       // GPU address of BillboardDrawIndirectArgs buffer
    uint64_t billboardInfoAddress;      // GPU address of BillboardInfo buffer
    uint     billboardCount;            // Total number of input billboards
    uint     _padding0;
    uint     _padding1;
    uint     _padding2;
};

/// Push constants for the billboard render vertex/fragment shaders.
@code_gen
struct BillboardRenderPC {
    uint64_t drawDataAddress;        // GPU address of BillboardDrawData buffer
    uint64_t fpConstAddress;         // GPU address of FPConstants buffer (for lighting shaders)
};
