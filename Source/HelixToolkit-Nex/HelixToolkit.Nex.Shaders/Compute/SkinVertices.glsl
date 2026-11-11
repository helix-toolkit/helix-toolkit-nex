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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MorphDeltaBuffer {
    GpuMorphDelta value[];
};

layout(buffer_reference, std430, buffer_reference_align = 8) readonly buffer MorphWeightBuffer {
    GpuMorphWeight value[];
};

layout(push_constant) uniform PC {
    SkinVertexPC value;
} pc;

mat4 fetchJoint(PaletteBuffer palette, uint joint) {
    // Out-of-range joints contribute identity so malformed data stays in bind pose.
    return joint < pc.value.jointCount ? palette.value[joint] : mat4(1.0);
}

// Adds the weighted deltas of the active morph targets to a base position.
vec3 morphPosition(MorphDeltaBuffer deltas, MorphWeightBuffer weights, uint vertex, vec3 position) {
    for (uint i = 0; i < pc.value.morphWeightCount; ++i) {
        GpuMorphWeight w = weights.value[i];
        position += deltas.value[w.target * pc.value.vertexCount + vertex].position * w.weight;
    }
    return position;
}

// Gram-Schmidt: the component of t perpendicular to the unit normal n. Falls back to any vector
// perpendicular to n when t is (nearly) parallel to it, so the result is never zero.
vec3 orthogonalTangent(vec3 t, vec3 n) {
    vec3 ortho = t - n * dot(n, t);
    if (dot(ortho, ortho) > 1e-12) {
        return normalize(ortho);
    }
    vec3 axis = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    return normalize(cross(n, axis));
}

void main() {
    uint idx = gl_GlobalInvocationID.x;
    if (idx >= pc.value.vertexCount) {
        return;
    }

    bool hasProps = pc.value.srcPropsAddress != 0 && pc.value.dstPropsAddress != 0;
    vec4 position = SrcVertexBuffer(pc.value.srcVertexAddress).value[idx];
    GpuVertexProps props;
    if (hasProps) {
        props = SrcPropsBuffer(pc.value.srcPropsAddress).value[idx];
    }

    // Morph targets: add the weighted deltas of the active targets to the bind pose.
    if (pc.value.morphDeltaAddress != 0 && pc.value.morphWeightCount > 0) {
        MorphDeltaBuffer deltas = MorphDeltaBuffer(pc.value.morphDeltaAddress);
        MorphWeightBuffer weights = MorphWeightBuffer(pc.value.morphWeightAddress);
        for (uint i = 0; i < pc.value.morphWeightCount; ++i) {
            GpuMorphWeight w = weights.value[i];
            GpuMorphDelta delta = deltas.value[w.target * pc.value.vertexCount + idx];
            position.xyz += delta.position * w.weight;
            if (hasProps) {
                props.normal += delta.normal * w.weight;
                props.tangent.xyz += delta.tangent * w.weight;
            }
        }

        // Generated flat normals follow the morphed triangle (counter-clockwise vertex order, as
        // NormalGenerator computes them).
        bool generatedTangents = hasProps && (pc.value.flags & SKIN_VERTEX_FLAG_GENERATED_TANGENTS) != 0;
        bool faceTangent = false;
        uint first = idx - idx % 3u;
        if (hasProps && (pc.value.flags & SKIN_VERTEX_FLAG_FLAT_NORMALS) != 0 && first + 2u < pc.value.vertexCount) {
            SrcVertexBuffer src = SrcVertexBuffer(pc.value.srcVertexAddress);
            vec3 p0 = morphPosition(deltas, weights, first, src.value[first].xyz);
            vec3 p1 = morphPosition(deltas, weights, first + 1u, src.value[first + 1u].xyz);
            vec3 p2 = morphPosition(deltas, weights, first + 2u, src.value[first + 2u].xyz);
            vec3 edge1 = p1 - p0;
            vec3 edge2 = p2 - p0;
            vec3 faceNormal = cross(edge1, edge2);
            if (dot(faceNormal, faceNormal) > 0.0) {
                props.normal = normalize(faceNormal);
                if (generatedTangents) {
                    // Face tangent of the morphed triangle, as TangentGenerator computes it (Lengyel;
                    // independent of the triangle winding).
                    SrcPropsBuffer srcProps = SrcPropsBuffer(pc.value.srcPropsAddress);
                    vec2 uv0 = srcProps.value[first].texCoord;
                    vec2 deltaUV1 = srcProps.value[first + 1u].texCoord - uv0;
                    vec2 deltaUV2 = srcProps.value[first + 2u].texCoord - uv0;
                    float denom = deltaUV1.x * deltaUV2.y - deltaUV2.x * deltaUV1.y;
                    if (abs(denom) >= 1e-8) {
                        float r = 1.0 / denom;
                        vec3 sDir = (deltaUV2.y * edge1 - deltaUV1.y * edge2) * r;
                        vec3 tDir = (deltaUV1.x * edge2 - deltaUV2.x * edge1) * r;
                        props.tangent.xyz = orthogonalTangent(sDir, props.normal);
                        props.tangent.w = dot(cross(props.normal, props.tangent.xyz), tDir) < 0.0 ? -1.0 : 1.0;
                        faceTangent = true;
                    }
                }
            }
        }
        // Other generated tangents stay perpendicular to the morphed normal. Authored tangents
        // (and their morph deltas) are used as is.
        if (generatedTangents && !faceTangent && dot(props.normal, props.normal) > 0.0) {
            props.tangent.xyz = orthogonalTangent(props.tangent.xyz, normalize(props.normal));
        }
    }

    // Linear blend skinning.
    mat4 skin = mat4(1.0);
    if (pc.value.paletteAddress != 0) {
        GpuJointInfluence influence = InfluenceBuffer(pc.value.influenceAddress).value[idx];
        PaletteBuffer palette = PaletteBuffer(pc.value.paletteAddress);
        skin = fetchJoint(palette, influence.joint0) * influence.weights.x
            + fetchJoint(palette, influence.joint1) * influence.weights.y
            + fetchJoint(palette, influence.joint2) * influence.weights.z
            + fetchJoint(palette, influence.joint3) * influence.weights.w;
    }

    DstVertexBuffer(pc.value.dstVertexAddress).value[idx] = vec4((skin * vec4(position.xyz, 1.0)).xyz, position.w);

    if (!hasProps) {
        return;
    }

    // Normals use the inverse-transpose so non-uniformly scaled joints keep them perpendicular;
    // tangents lie in the surface and use the linear transform. Both are renormalized after
    // morphing, which leaves them unnormalized.
    GpuVertexProps src = SrcPropsBuffer(pc.value.srcPropsAddress).value[idx];
    mat3 skin3 = mat3(skin);
    vec3 normal = normalMatrix(skin3) * props.normal;
    props.normal = dot(normal, normal) > 0.0 ? normalize(normal) : src.normal;
    vec3 tangent = skin3 * props.tangent.xyz;
    props.tangent.xyz = dot(tangent, tangent) > 0.0 ? normalize(tangent) : src.tangent.xyz;
    DstPropsBuffer(pc.value.dstPropsAddress).value[idx] = props;
}
