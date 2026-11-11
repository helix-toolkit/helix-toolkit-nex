#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/Skinning.glsl"

// One morphed mesh of the morph displacement visualization. Each instance draws one line from a
// vertex's un-morphed position to its deformed position, both skinned when the mesh is skinned, so
// the line shows the displacement contributed by the morph targets.
@code_gen
struct MorphLineMesh {
    mat4 modelTransform;
    vec4 color;
    uint64_t srcVertexAddress;    // Base positions (vec4[]).
    uint64_t dstVertexAddress;    // Deformed positions written by the deformation pass (vec4[]).
    uint64_t paletteAddress;      // Skinning palette (mat4[]), 0 when the mesh is not skinned.
    uint64_t influenceAddress;    // Joint influences (GpuJointInfluence[]), 0 when not skinned.
    uint vertexCount;
    uint jointCount;
    float minLength;              // Shorter world-space displacements are not drawn.
    float lengthScale;            // Displacements are scaled by this factor.
};

@code_gen
struct MorphLinePushConstant {
    uint64_t meshAddress;         // The MorphLineMesh of this draw.
    uint64_t fpConstAddress;
};

layout(push_constant) uniform PC {
    MorphLinePushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshBuffer {
    MorphLineMesh value;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
    vec4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer PaletteBuffer {
    mat4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InfluenceBuffer {
    GpuJointInfluence value[];
};

layout(location = 0) out flat vec4 color;

mat4 fetchJoint(PaletteBuffer palette, uint joint, uint jointCount) {
    return joint < jointCount ? palette.value[joint] : mat4(1.0);
}

void main() {
    MorphLineMesh mesh = MeshBuffer(pc.value.meshAddress).value;
    FPBuffer fpBuf = FPBuffer(pc.value.fpConstAddress);
    uint idx = uint(gl_InstanceIndex);
    color = mesh.color;

    vec3 base = VertexBuffer(mesh.srcVertexAddress).value[idx].xyz;
    if (mesh.paletteAddress != 0) {
        // Skin the un-morphed position like the deformation pass, so only the morph shows.
        GpuJointInfluence influence = InfluenceBuffer(mesh.influenceAddress).value[idx];
        PaletteBuffer palette = PaletteBuffer(mesh.paletteAddress);
        mat4 skin = fetchJoint(palette, influence.joint0, mesh.jointCount) * influence.weights.x
            + fetchJoint(palette, influence.joint1, mesh.jointCount) * influence.weights.y
            + fetchJoint(palette, influence.joint2, mesh.jointCount) * influence.weights.z
            + fetchJoint(palette, influence.joint3, mesh.jointCount) * influence.weights.w;
        base = (skin * vec4(base, 1.0)).xyz;
    }
    vec3 start = (mesh.modelTransform * vec4(base, 1.0)).xyz;
    vec3 deformed = (mesh.modelTransform * vec4(VertexBuffer(mesh.dstVertexAddress).value[idx].xyz, 1.0)).xyz;
    vec3 displacement = deformed - start;

    if (dot(displacement, displacement) <= mesh.minLength * mesh.minLength) {
        // Not displaced: emit a point outside the clip volume so nothing is rasterized.
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        return;
    }
    vec3 position = gl_VertexIndex == 0 ? start : start + displacement * mesh.lengthScale;
    gl_Position = fpBuf.value.viewProjection * vec4(position, 1.0);
}
