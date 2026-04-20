
@code_gen
struct PointerRing {
    vec3 rayOrigin; // Pointer ray origin in world space.
    uint enabled; // 0 = disabled, 1 = enabled
    vec3 rayDirection; // Pointer ray direction in world space.
    float outerDistThreshold; // Distance threshold for fragment world position to pointer ray.
    vec3 color; // Color to use for highlighting fragments near the pointer ray.
    float innerDistThreshold; // Inner distance threshold for fragment world position to pointer ray.
    float colorMix; // Mix factor for pointer ray color (0 = no highlight, 1 = full pointer ray color).
    uint _padding0;
    uint _padding1;
    uint _padding2;
};

@code_gen
struct FPConstants {
    mat4 viewProjection;
    mat4 inverseViewProjection;

    vec3 cameraPosition;
    float dpiScale;

    uint lightCount;
    uint tileSize;
    vec2 screenDimensions;

    uint tileCountX;
    uint tileCountY;
    uint maxLightsPerTile;
    uint enabled;

    PointerRing pointerRing;

    uint64_t timeMs;

    uint64_t meshInfoBufferAddress;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t materialBufferAddress;
    uint64_t meshDrawBufferAddress;
    uint64_t directionalLightsBufferAddress;
};
