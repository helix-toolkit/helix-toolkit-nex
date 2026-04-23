#extension GL_EXT_buffer_reference : require
#extension GL_EXT_buffer_reference_uvec2 : require
#extension GL_EXT_debug_printf : enable
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
#extension GL_EXT_shader_explicit_arithmetic_types_int64   : require
#extension GL_EXT_scalar_block_layout : require
#extension GL_EXT_buffer_reference2 : require

layout (set = 0, binding = 0) uniform texture2D kTextures2D[];
layout (set = 0, binding = 1) uniform sampler kSamplers[];

vec4 textureBindless2D(uint textureid, uint samplerid, in vec2 uv) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, 0);
}

vec4 textureBindless2DLod(uint textureid, uint samplerid, in vec2 uv, float lod) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, lod);
}
