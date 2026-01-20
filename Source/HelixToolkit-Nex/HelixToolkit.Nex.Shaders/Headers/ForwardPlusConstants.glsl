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
    uint lightCount;
    uint tileSize;
    vec2 screenDimensions;
    vec2 tileCount;
};

struct MeshDraw {
    uint64_t forwardPlusConstantsAddress;
    uint64_t vertexBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint modelId;
    uint materialId;
};
