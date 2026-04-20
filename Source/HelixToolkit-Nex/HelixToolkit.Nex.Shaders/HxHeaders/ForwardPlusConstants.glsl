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

    vec3 pointerRayOrigin; // Pointer ray origin in world space.
    uint pointerRayEnabled; // 0 = disabled, 1 = enabled
    vec3 pointerRayDirection; // Pointer ray direction in world space.
    float pointerRayDistThreshold; // Distance threshold for fragment world position to pointer ray.

    uint64_t timeMs;

    uint64_t meshInfoBufferAddress;
    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;
    uint64_t lightIndexBufferAddress;
    uint64_t materialBufferAddress;
    uint64_t meshDrawBufferAddress;
    uint64_t directionalLightsBufferAddress;
};
