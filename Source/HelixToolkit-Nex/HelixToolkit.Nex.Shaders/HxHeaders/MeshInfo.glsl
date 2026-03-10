@code_gen
struct MeshInfo {
    uint64_t vertexBufferAddress;
    uint64_t vertexPropsBufferAddress;
    uint64_t vertexColorBufferAddress;
    uint64_t indexBufferAddress;
    vec3 boxMin;        // Local Space
    uint _padding0;
    vec3 boxMax;        // Local Space
    uint _padding1;
    vec3 sphereCenter;  // Local Space
    float sphereRadius; // Local Space
};
