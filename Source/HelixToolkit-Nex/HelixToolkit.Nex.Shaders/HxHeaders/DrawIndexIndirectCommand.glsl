@code_gen
struct DrawIndexedIndirectCommand {
    uint    indexCount;
    uint    instanceCount;
    uint    firstIndex;
    int     vertexOffset;
    uint    firstInstance;
    uint    meshDrawIndex;
    vec2    _padding;
};
