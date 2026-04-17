#version 450
#include "HxHeaders/HeaderPackEntity.glsl"

layout(location = 6) in flat uvec2 fragEntityId;

layout(location = 0) out vec2 idOut;

void main()
{
    uint primID = uint(gl_PrimitiveID);
    idOut = packPrimitiveId(fragEntityId, primID);
}
