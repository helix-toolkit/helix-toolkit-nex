@code_gen
struct FPConstants {
    mat4 viewProjection;
    mat4 inverseViewProjection;
    vec3 cameraPosition;
    float time;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t modelMatrixBufferAddress;
    uint64_t materialBufferAddress;
    uint64_t perModelParamsBufferAddress;
    uint64_t meshDrawBufferAddress;
    uint64_t drawCmdBufferAddress;
    uint lightCount;
    uint tileSize;
    vec2 screenDimensions;
    uint tileCountX;
    uint tileCountY;
};
