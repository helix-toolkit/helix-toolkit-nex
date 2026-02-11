@code_gen
struct MeshInfo {
    uint64_t vertexBufferAddress;
    uint64_t vertexPropsBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint64_t _padding0;
    vec3 boxMin;        // Local Space
    float _padding1;
    vec3 boxMax;        // Local Space
    float _padding2;
    vec3 sphereCenter;  // Local Space
    float sphereRadius; // Local Space
};
