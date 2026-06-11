/// Per-visible-point data written by the compute shader and read by the vertex shader.
@code_gen
struct LineDraw {
    uint vertexCount;     // Always 4 (triangle strip quad)
    uint instanceCount;   // Vertices count for line drawing.
    uint firstVertex;     // Always 0
    uint firstInstance;   // Always 0

    uint meshId; // Unique geometry id, used for fetching bounding box.
    uint lineColor; // The line color.
    uint materialType; // The material type, used for shader permutation.
    uint _padding;

    uint cullable; // Whether this mesh is cullable, used for frustum culling.
    uint drawType; // Encoded information about line type
    uint nodeInfoIndex;
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
};
