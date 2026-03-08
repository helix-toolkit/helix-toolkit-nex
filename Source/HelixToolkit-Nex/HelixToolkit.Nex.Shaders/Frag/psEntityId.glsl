#version 450

layout(location = 6) in flat vec2 fragEntityId;

layout(location = 0) out vec2 idOut;

void main()
{
    idOut = fragEntityId;
}
