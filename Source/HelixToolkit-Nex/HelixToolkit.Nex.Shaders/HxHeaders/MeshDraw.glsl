
@code_gen
struct MeshDraw {
    uint64_t vertexBufferAddress;
    uint64_t vertexPropsBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint64_t instancingBufferAddress; // For GPU driven instancing
    uint64_t instancingIndexBufferAddress; // Used to get the instancing matrix from instancing buffer.
    uint meshId; // Unique geometry id, used for fetching bounding box, etc for frustum test.
    uint modelId; // The model id this mesh belongs to, used for fetching model matrix.
    uint materialId; // The material id this mesh uses, used for fetching material properties.
    uint materialType; // The material type, used for shader permutation.
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    float _padding;
};

@code_gen
struct MeshDrawPushConstant {
    uint64_t fpConstAddress;
    uint drawCommandIdxOffset;
    uint meshDrawId;
};
