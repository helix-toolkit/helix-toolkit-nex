
@code_gen
struct MeshDraw {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int  vertexOffset;
    uint firstInstance;
    uint meshId; // Unique geometry id, used for fetching bounding box.
    uint materialId; // The material id this mesh uses, used for fetching material properties.
    uint materialType; // The material type, used for shader permutation.
    uint worldId; // The world id this mesh belongs to, used for GPU picking.
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    uint cullable; // Whether this mesh is cullable, used for frustum culling.
    uint drawType; // Encoded information about mesh draw type. [0x1] IsDynamic. [0x2] IsInstancing.
    uint64_t instancingBufferAddress; // For GPU driven instancing
    uint64_t instancingIndexBufferAddress; // Used to get the instancing matrix from instancing buffer.
    mat4 transform; // World transform of the model.
};

@code_gen
struct MeshDrawPushConstant {
    uint64_t fpConstAddress;
    uint64_t customMaterialBufferAddress; // Address of custom material properties buffer (set per material type)
    uint drawCommandIdxOffset;
    uint meshDrawId;
    uint _padding0;
    uint _padding1;
};
