
@code_gen
struct MeshDraw {
    uint64_t vertexBufferAddress;
    uint64_t vertexPropsBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint64_t instancingBufferAddress;
    uint modelId;
    uint materialId;
};

@code_gen
struct MeshDrawPushConstant {
    uint64_t fpConstAddress;
    uint meshDrawId;
    uint _padding;
};
