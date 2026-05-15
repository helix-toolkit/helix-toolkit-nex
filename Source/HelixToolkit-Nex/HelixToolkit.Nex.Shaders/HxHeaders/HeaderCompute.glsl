#include "HxHeaders/HeaderDefines.glsl"

vec4 textureBindless2D(uint textureid, uint samplerid, vec2 uv) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, 0);
}
vec4 textureBindless2DLod(uint textureid, uint samplerid, vec2 uv, float lod) {
    return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, lod);
}
