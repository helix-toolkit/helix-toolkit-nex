#include "HxHeaders/HeaderCompute.glsl"
#include "HxHeaders/VertStruct.glsl"
#include "HxHeaders/Skinning.glsl"
#include "HxHeaders/Utils.glsl"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PaletteBuffer {
    mat4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer SrcVertexBuffer {
    vec4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer SrcPropsBuffer {
    GpuVertexProps value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InfluenceBuffer {
    GpuJointInfluence value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) writeonly buffer DstVertexBuffer {
    vec4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) writeonly buffer DstPropsBuffer {
    GpuVertexProps value[];
};

layout(push_constant) uniform PC {
    SkinVertexPC value;
} pc;

mat4 fetchJoint(PaletteBuffer palette, uint joint) {
    // Out-of-range joints contribute identity so malformed data stays in bind pose.
    return joint < pc.value.jointCount ? palette.value[joint] : mat4(1.0);
}

void main() {
    uint idx = gl_GlobalInvocationID.x;
    if (idx >= pc.value.vertexCount) {
        return;
    }

    GpuJointInfluence influence = InfluenceBuffer(pc.value.influenceAddress).value[idx];
    PaletteBuffer palette = PaletteBuffer(pc.value.paletteAddress);

    // Linear blend skinning.
    mat4 skin = fetchJoint(palette, influence.joint0) * influence.weights.x
        + fetchJoint(palette, influence.joint1) * influence.weights.y
        + fetchJoint(palette, influence.joint2) * influence.weights.z
        + fetchJoint(palette, influence.joint3) * influence.weights.w;

    vec4 position = SrcVertexBuffer(pc.value.srcVertexAddress).value[idx];
    DstVertexBuffer(pc.value.dstVertexAddress).value[idx] = vec4((skin * vec4(position.xyz, 1.0)).xyz, position.w);

    if (pc.value.srcPropsAddress == 0 || pc.value.dstPropsAddress == 0) {
        return;
    }

    GpuVertexProps props = SrcPropsBuffer(pc.value.srcPropsAddress).value[idx];
    // Normals use the inverse-transpose so non-uniformly scaled joints keep them perpendicular;
    // tangents lie in the surface and use the linear transform.
    mat3 skin3 = mat3(skin);
    vec3 normal = normalMatrix(skin3) * props.normal;
    props.normal = dot(normal, normal) > 0.0 ? normalize(normal) : props.normal;
    vec3 tangent = skin3 * props.tangent.xyz;
    props.tangent.xyz = dot(tangent, tangent) > 0.0 ? normalize(tangent) : props.tangent.xyz;
    DstPropsBuffer(pc.value.dstPropsAddress).value[idx] = props;
}
