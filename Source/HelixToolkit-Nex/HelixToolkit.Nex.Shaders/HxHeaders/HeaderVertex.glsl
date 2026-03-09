#extension GL_EXT_buffer_reference : require
#extension GL_EXT_buffer_reference_uvec2 : require
#extension GL_EXT_debug_printf : enable
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
#extension GL_EXT_shader_explicit_arithmetic_types_int64   : require

vec2 packEntityIdAndIndex(uint entityId, uint entityVer, uint instanceIndex) {
    uint id = entityId & 0xFFFFFFu;            // 24 bits, Max 16,777,215
    uint inst = instanceIndex & 0xFFFFFu;      // 20 bits, Max 1,048,575
    uint ver = entityVer & 0xFFFFFu;       // 20 bits, Max 1,048,575

    // Pack R: [8 bits Instance Low] [24 bits ID]
    uint packedR = id | (inst << 24u);

    // Pack G: [20 bits Version] [12 bits Instance High]
    // We take the remaining 12 bits of the instance index (shifted right by 8)
    uint packedG = (inst >> 8u) | (ver << 12u);

    return vec2(uintBitsToFloat(packedR), uintBitsToFloat(packedG));
}
