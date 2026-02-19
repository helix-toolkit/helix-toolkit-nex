#version 450

layout(location = 6) in flat uvec2 fragEntityId;

layout(location = 0) out vec2 idOut;

void main()
{
    idOut = vec2(uintBitsToFloat(fragEntityId.x), uintBitsToFloat(fragEntityId.y));
}
