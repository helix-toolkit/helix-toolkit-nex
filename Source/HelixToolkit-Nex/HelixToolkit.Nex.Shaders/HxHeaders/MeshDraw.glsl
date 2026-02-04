
@code_gen
struct MeshDraw {
    uint64_t vertexBufferAddress;
    uint64_t vertexPropsBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint64_t instancingBufferAddress; // For GPU driven instancing
    uint64_t instancingIndexBufferAddress; // Used to get the instancing matrix from instancing buffer.
    uint meshId;
    uint modelId;
    uint materialId;
    uint materialType;
    vec2 _padding;
};

@code_gen
struct MeshDrawPushConstant {
    uint64_t fpConstAddress;
    uint drawCommandIdxOffset;
    uint meshDrawId;
};
