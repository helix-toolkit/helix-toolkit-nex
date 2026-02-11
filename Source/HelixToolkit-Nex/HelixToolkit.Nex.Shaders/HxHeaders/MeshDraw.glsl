
@code_gen
struct MeshDraw {
    uint64_t instancingBufferAddress; // For GPU driven instancing
    uint64_t instancingIndexBufferAddress; // Used to get the instancing matrix from instancing buffer.
    uint meshId; // Unique geometry id, used for fetching bounding box.
    uint materialId; // The material id this mesh uses, used for fetching material properties.
    uint materialType; // The material type, used for shader permutation.
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    mat4 transform; // World transform of the model.
};

@code_gen
struct MeshDrawPushConstant {
    uint64_t fpConstAddress;
    uint drawCommandIdxOffset;
    uint meshDrawId;
};
