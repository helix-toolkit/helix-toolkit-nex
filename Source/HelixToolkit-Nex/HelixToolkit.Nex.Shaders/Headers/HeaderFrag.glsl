#version 460
#extension GL_EXT_buffer_reference : require
#extension GL_EXT_buffer_reference_uvec2 : require
#extension GL_EXT_debug_printf : enable
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require

layout (set = 0, binding = 0) uniform texture2D kTextures2D[];
layout (set = 1, binding = 0) uniform texture3D kTextures3D[];
layout (set = 2, binding = 0) uniform textureCube kTexturesCube[];
layout (set = 3, binding = 0) uniform texture2D kTextures2DShadow[];
layout (set = 0, binding = 1) uniform sampler kSamplers[];
layout (set = 3, binding = 1) uniform samplerShadow kSamplersShadow[];

layout (set = 0, binding = 3) uniform sampler2D kSamplerYUV[];

vec4 textureBindless2D(uint textureid, uint samplerid, vec2 uv) {
    return texture(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv);
}
vec4 textureBindless2DLod(uint textureid, uint samplerid, vec2 uv, float lod) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, lod);
}
float textureBindless2DShadow(uint textureid, uint samplerid, vec3 uvw) {
    return texture(nonuniformEXT(sampler2DShadow(kTextures2DShadow[textureid], kSamplersShadow[samplerid])), uvw);
}
ivec2 textureBindlessSize2D(uint textureid) {
    return textureSize(nonuniformEXT(kTextures2D[textureid]), 0);
}
vec4 textureBindlessCube(uint textureid, uint samplerid, vec3 uvw) {
    return texture(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw);
}
vec4 textureBindlessCubeLod(uint textureid, uint samplerid, vec3 uvw, float lod) {
    return textureLod(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw, lod);
}
int textureBindlessQueryLevels2D(uint textureid) {
    return textureQueryLevels(nonuniformEXT(kTextures2D[textureid]));
}
int textureBindlessQueryLevelsCube(uint textureid) {
    return textureQueryLevels(nonuniformEXT(kTexturesCube[textureid]));
}