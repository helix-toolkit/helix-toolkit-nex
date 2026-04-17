uvec2 packObjectInfo(uint worldId, uint entityId, uint instanceIndex) {
    if (entityId == 0) {
        return uvec2(0); // Return zero for invalid entity ID
    }
    uint x = (worldId & 0xFu) | 
             ((entityId & 0xFFFFu) << 4u) | 
             ((instanceIndex & 0xFFFu) << 20u);

    // Pack the remaining 10 bits of InstanceID into Y, leaving 22 bits for PrimitiveID
    uint y = ((instanceIndex >> 12u) & 0x3FFu);

    return uvec2(x, y);
}

vec2 packPrimitiveId(in uvec2 objectInfo, in uint primId) {
    uint y = objectInfo.y | ((primId & 0x3FFFFFu) << 10u);
    return vec2(uintBitsToFloat(objectInfo.x), uintBitsToFloat(y));
}
