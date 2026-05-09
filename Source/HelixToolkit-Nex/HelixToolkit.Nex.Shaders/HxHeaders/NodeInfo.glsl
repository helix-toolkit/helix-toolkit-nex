@code_gen
struct GpuNodeInfo {
    mat4 transform;
    uint enabled;
    uint worldId; // The world id this mesh belongs to, used for GPU picking.
    uint entityId; // The entity id this mesh belongs to, used for GPU picking.
    uint renderMask;
};
