/// Per-visible-point data written by the compute shader and read by the vertex shader.
@code_gen
struct PointDraw {
    uint vertexCount;     // Always 4 (triangle strip quad)
    uint instanceCount;   // Rendered Point_Quad instance count = pointCount (one centered quad per single-vertex point) when rendered, 0 when culled/node-disabled (written by PointFrustumCull; NOT a 0/1 flag, NOT a vertex count).
    uint firstVertex;     // Always 0
    uint firstInstance;   // Always 0 (carries no point index; point index s is surfaced via gl_InstanceIndex, not firstInstance).

    uint meshId; // Unique geometry id, used for fetching bounding box.
    uint pointColor; // The point color.
    uint materialType; // The material type, used for shader permutation.
    uint pointCount; // point count.

    uint cullable; // Whether this mesh is cullable, used for frustum culling.
    uint nodeInfoIndex;
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    float pointSize; // Point Size

    uint drawType; // Draw type
    uint textureId;
    uint samplerId;
    uint fixedSize;
};

/// Push constants for the point render vertex/fragment shaders.
@code_gen
struct PointRenderPC {
    uint64_t fpConstAddress;         // GPU address of FPConstants buffer.
    uint64_t meshDrawBufferAddress;  // GPU address of the PointDraw buffer.
    uint     drawCommandIdxOffset;   // Offset added to gl_DrawID to index the PointDraw buffer.
    uint     _padding0;              // Padding for 8-byte alignment.
};
