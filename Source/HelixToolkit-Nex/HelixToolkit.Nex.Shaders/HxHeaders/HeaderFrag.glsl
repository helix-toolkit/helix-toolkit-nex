#include "HxHeaders/HeaderDefines.glsl"

vec4 textureBindless2D(uint textureid, uint samplerid, in vec2 uv) {
    return texture(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv);
}

vec4 textureBindless2DLod(uint textureid, uint samplerid, in vec2 uv, float lod) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, lod);
}

float textureBindless2DShadow(uint textureid, uint samplerid, in vec3 uvw) {
    return texture(nonuniformEXT(sampler2DShadow(kTextures2DShadow[textureid], kSamplersShadow[samplerid])), uvw);
}

ivec2 textureBindlessSize2D(uint textureid) {
    return textureSize(nonuniformEXT(kTextures2D[textureid]), 0);
}

vec4 textureBindlessCube(uint textureid, uint samplerid, in vec3 uvw) {
    return texture(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw);
}

vec4 textureBindlessCubeLod(uint textureid, uint samplerid, in vec3 uvw, float lod) {
    return textureLod(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw, lod);
}

int textureBindlessQueryLevels2D(uint textureid) {
    return textureQueryLevels(nonuniformEXT(kTextures2D[textureid]));
}

int textureBindlessQueryLevelsCube(uint textureid) {
    return textureQueryLevels(nonuniformEXT(kTexturesCube[textureid]));
}
