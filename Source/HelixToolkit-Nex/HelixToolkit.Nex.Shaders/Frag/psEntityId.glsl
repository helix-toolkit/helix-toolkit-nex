#version 450

layout(location = 6) in flat uint fragEntityId;

layout(location = 0) out uint idOut;

void main()
{
    idOut = fragEntityId;
}
