/// Per-visible-point data written by the compute shader and read by the vertex shader.
@code_gen
struct LineDraw {
    uint vertexCount;     // Always 4 (triangle strip quad)
    uint instanceCount;   // Rendered Line_Quad instance count = lineCount (one quad per disjoint 2-vertex segment) when rendered, 0 when culled/node-disabled (written by LineFrustumCull; NOT a 0/1 flag, NOT a vertex count).
    uint firstVertex;     // Always 0
    uint firstInstance;   // Always 0 (segment index s is surfaced via gl_InstanceIndex, not firstInstance).

    uint meshId; // Unique geometry id, used for fetching bounding box.
    uint lineColor; // The line color.
    uint materialType; // The material type, used for shader permutation.
    uint lineCount; // line count.

    uint cullable; // Whether this mesh is cullable, used for frustum culling.
    uint nodeInfoIndex;
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    float lineWidth; // Line width: screen-space pixels or world-space diameter, per renderSpace; resolved pixel width is clamped to [1,64] by the vertex shader.

    uint drawType; // Draw type
    uint textureId;
    uint samplerId;
    uint renderSpace; // 0 = Screen (fixed pixel width), 1 = World (world-space diameter, distance-scaled)
};

/// Push constants for the line render vertex/fragment shaders.
@code_gen
struct LineRenderPC {
    uint64_t fpConstAddress;         // GPU address of FPConstants buffer.
    uint64_t meshDrawBufferAddress;  // GPU address of the LineDraw buffer.
    uint     drawCommandIdxOffset;   // Offset added to gl_DrawID to index the LineDraw buffer.
    uint     _padding0;              // Padding for 8-byte alignment.
};
