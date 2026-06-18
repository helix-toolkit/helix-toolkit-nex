// Forward+ light structures
// Note: struct Light is defined in PBRFunctions.glsl which must be included

@code_gen
struct LightGridTile {
    uint lightCount;
    uint lightIndexOffset;
};

uint packLightCount(uint opaqueCount, uint transparentCount) {
    return (opaqueCount & 0xFFu) | ((transparentCount & 0xFFu) << 8);
}

void unpackLightCount(uint packedCount, out uint opaqueCount, out uint transparentCount) {
    opaqueCount = packedCount & 0xFFu;
    transparentCount = (packedCount >> 8) & 0xFFu;
}

uint unpackOpaqueLightCount(uint packedCount) {
    return packedCount & 0xFFu;
}

uint unpackTransparentLightCount(uint packedCount) {
    return (packedCount >> 8) & 0xFFu;
}
