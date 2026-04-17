#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/VertStruct.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/MeshDraw.glsl"
#include "HxHeaders/MeshInfo.glsl"
#include "HxHeaders/Instancing.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(location = 0) out vec3 fragWorldPos;
layout(location = 1) out flat uint materialId;
layout(location = 2) out vec4 fragColor;
#ifndef EXCLUDE_MESH_PROPS
layout(location = 3) out vec3 fragNormal;
layout(location = 4) out vec3 fragTangent;
layout(location = 5) out vec2 fragTexCoord;
#endif
#ifdef OUTPUT_DRAW_ID
layout(location = 6) out flat uvec2 fragEntityId;
#endif

layout(push_constant) uniform Pc {
    MeshDrawPushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
    vec4 value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexPropsBuffer {
    GpuVertexProps value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexColorBuffer {
    vec4 colors[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstancingBuffer {
    InstanceTransform instancing[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer InstancingIndexBuffer {
    uint value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshDrawBuffer {
    MeshDraw draws[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshInfoBuffer {
    MeshInfo value[];
};

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

uint meshDrawIndex = gl_DrawID + pc.value.drawCommandIdxOffset;

MeshDraw meshDraw = MeshDrawBuffer(fpConst.meshDrawBufferAddress).draws[meshDrawIndex];

MeshInfo meshInfo = MeshInfoBuffer(fpConst.meshInfoBufferAddress).value[meshDraw.meshId];

uint getInstancingIndex() {
    if (meshDraw.instancingBufferAddress == 0) {
        return 0;
    }
    if (meshDraw.cullable != 0 && meshDraw.instancingIndexBufferAddress != 0) {
        InstancingIndexBuffer instancingIdx = InstancingIndexBuffer(meshDraw.instancingIndexBufferAddress);
        return instancingIdx.value[gl_InstanceIndex];
    }
    return gl_InstanceIndex;
}

InstanceTransform instIdentity = InstanceTransform(vec4(0.0, 0.0, 0.0, 1.0), vec3(0.0), 1.0);
InstanceTransform getInstancingMatrix(uint index) {
    if (meshDraw.instancingBufferAddress == 0) {
        return instIdentity;
    }
    InstancingBuffer instancingBuf = InstancingBuffer(meshDraw.instancingBufferAddress);
    return instancingBuf.instancing[index];
}

GpuVertexProps emptyProps;

vec4 getVertex() {
    if (meshInfo.vertexBufferAddress == 0) {
        return vec4(0.0);
    }
    VertexBuffer vertexBuf = VertexBuffer(meshInfo.vertexBufferAddress);
    return vec4(vertexBuf.value[gl_VertexIndex].xyz, 1);
}

GpuVertexProps getVertexProps() {
    if (meshInfo.vertexPropsBufferAddress == 0) {
        return emptyProps;
    }
    VertexPropsBuffer propsBuf = VertexPropsBuffer(meshInfo.vertexPropsBufferAddress);
    return propsBuf.value[gl_VertexIndex];
}

vec4 getVertexColor() {
    if (meshInfo.vertexColorBufferAddress == 0) {
        return vec4(1.0);
    }
    VertexColorBuffer colorBuf = VertexColorBuffer(meshInfo.vertexColorBufferAddress);
    return colorBuf.colors[gl_VertexIndex];
}

// Template function to calculate vertex output
void calVertexOutput(in uint index, out vec4 pos, out vec3 wp, out vec3 normal, out vec3 tangent, out vec4 color, out vec2 texCoord) {
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_START*/
    vec4 position = getVertex();
    InstanceTransform instance = getInstancingMatrix(index);
    mat3 instanceRot = quatToMat3(instance.quaternion);

    vec4 worldPos = meshDraw.transform * position;
    worldPos = vec4(transformCoord(worldPos.xyz, instanceRot, instance.scale, instance.translation), 1);

    wp = worldPos.xyz;
    pos = fpConst.viewProjection * worldPos;
#ifndef EXCLUDE_MESH_PROPS
    GpuVertexProps vertProps = getVertexProps();
    normal = mat3(meshDraw.transform) * vertProps.normal;
    normal = normalize(instanceRot * normal);
    tangent = mat3(meshDraw.transform) * vertProps.tangent;
    tangent = normalize(instanceRot * tangent);

    texCoord = vertProps.texCoord;
#endif
    color = getVertexColor();
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_END*/
}

void main() {
    materialId = meshDraw.materialId;
#ifdef EXCLUDE_MESH_PROPS
    vec3 fragNormal;
    vec3 fragTangent;
    vec2 fragTexCoord;
#endif
    uint idx = getInstancingIndex();
    calVertexOutput(idx, gl_Position, fragWorldPos, fragNormal, fragTangent, fragColor, fragTexCoord);
#ifdef OUTPUT_DRAW_ID
    fragEntityId = packObjectInfo(meshDraw.worldId, meshDraw.entityId, idx);
#endif
}

