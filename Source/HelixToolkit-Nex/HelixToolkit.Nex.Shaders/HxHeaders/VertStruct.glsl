// Bindless vertex buffer structure
@code_gen
struct GpuVertexProps {
    vec3 normal;
    uint _padding0;
    vec2 texCoord;
    uint _padding1;
    uint _padding2;
    vec4 tangent; // xyz = tangent vector, w = handedness (+1 or -1) per glTF TANGENT.w
};


