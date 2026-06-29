// Forward+ light structures
// Note: struct Light is defined in PBRFunctions.glsl which must be included

@code_gen
struct LightGridTile {
    uint lightCount; // Packed light count: lower 16 bits = opaque light count, upper 16 bits = transparent light count
    uint lightIndexOffset;
};

uint packLightCount(uint opaqueCount, uint transparentCount) {
    return (opaqueCount & 0xFFFFu) | ((transparentCount & 0xFFFFu) << 16);
}

void unpackLightCount(uint packedCount, out uint opaqueCount, out uint transparentCount) {
    opaqueCount = packedCount & 0xFFFFu;
    transparentCount = (packedCount >> 16) & 0xFFFFu;
}

uint unpackOpaqueLightCount(uint packedCount) {
    return packedCount & 0xFFFFu;
}

uint unpackTransparentLightCount(uint packedCount) {
    return (packedCount >> 16) & 0xFFFFu;
}
