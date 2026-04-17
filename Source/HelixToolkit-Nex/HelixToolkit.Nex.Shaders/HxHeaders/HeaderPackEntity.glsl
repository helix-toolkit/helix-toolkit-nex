uvec2 packObjectInfo(uint worldId, uint entityId, uint instanceIndex) {
    if (entityId == 0) {
        return uvec2(0); // Return zero for invalid entity ID
    }
    uint x = (worldId & LIMITS_WORLD_ID_MASK) | 
             ((entityId & LIMITS_ENTITY_ID_MASK) << LIMITS_ENTITY_ID_SHIFT) | 
             ((instanceIndex & LIMITS_INSTANCE_LOW_MASK) << LIMITS_INSTANCE_LOW_SHIFT);

    // Pack the remaining 10 bits of InstanceID into Y, leaving 22 bits for PrimitiveID
    uint y = ((instanceIndex >> LIMITS_INSTANCE_LOW_BITS) & LIMITS_INSTANCE_HIGH_MASK);

    return uvec2(x, y);
}

vec2 packPrimitiveId(in uvec2 objectInfo, in uint primId) {
    uint y = objectInfo.y | ((primId & LIMITS_INDEX_COUNT_MASK) << LIMITS_INSTANCE_HIGH_SHIFT);
    return vec2(uintBitsToFloat(objectInfo.x), uintBitsToFloat(y));
}
