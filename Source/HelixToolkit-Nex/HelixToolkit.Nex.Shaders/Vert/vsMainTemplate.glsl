#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/VertStruct.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/MeshDraw.glsl"
#include "HxHeaders/DrawIndexIndirectCommand.glsl"
layout(location = 0) out flat uint vertexIndex;
layout(location = 1) out vec3 fragPosition;
layout(location = 2) out vec3 fragNormal;
layout(location = 3) out vec2 fragTexCoord;
layout(location = 4) out vec3 fragTangent;
layout(location = 5) out vec4 fragColor;
layout(location = 6) out flat uint materialId;

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

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelMatrixBuffer {
    mat4 models[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstancingBuffer {
    mat4 instancing[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer InstancingIndexBuffer {
    uint value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MeshDrawBuffer {
    MeshDraw draws[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer DrawCmdsBuffer {
    DrawIndexedIndirectCommand commands[];
};

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

uint getMeshDrawId() {
     if (fpConst.drawCmdBufferAddress == 0) {
        return pc.value.meshDrawId;
     }
     DrawCmdsBuffer cmds = DrawCmdsBuffer(fpConst.drawCmdBufferAddress);
     return cmds.commands[gl_DrawID].meshDrawIndex;
}

MeshDraw meshDraw = MeshDrawBuffer(fpConst.meshDrawBufferAddress).draws[getMeshDrawId()];

mat4 getInstancingMatrix() {
    if (meshDraw.instancingBufferAddress == 0) {
        return mat4(1.0);
    }
    InstancingBuffer instancingBuf = InstancingBuffer(meshDraw.instancingBufferAddress);
    if (meshDraw.instancingIndexBufferAddress != 0) {
        InstancingIndexBuffer instancingIdx = InstancingIndexBuffer(meshDraw.instancingIndexBufferAddress);
        uint idx = instancingIdx.value[gl_InstanceIndex];
        return instancingBuf.instancing[idx];
    }
    return instancingBuf.instancing[gl_InstanceIndex];
}

mat4 getModelMatrix() {
    ModelMatrixBuffer modelBuf = ModelMatrixBuffer(fpConst.modelMatrixBufferAddress);
    return modelBuf.models[meshDraw.modelId];
}

GpuVertexProps emptyProps;

vec4 getVertex() {
    VertexBuffer vertexBuf = VertexBuffer(meshDraw.vertexBufferAddress);
    return vec4(vertexBuf.value[gl_VertexIndex].xyz, 1);
}

GpuVertexProps getVertexProps() {
    if (meshDraw.vertexPropsBufferAddress == 0) {
        return emptyProps;
    }
    VertexPropsBuffer propsBuf = VertexPropsBuffer(meshDraw.vertexPropsBufferAddress);
    return propsBuf.value[gl_VertexIndex];
}

vec4 getVertexColor() {
    if (meshDraw.vertexColorBufferAddress == 0) {
        return vec4(1.0);
    }
    VertexColorBuffer colorBuf = VertexColorBuffer(meshDraw.vertexColorBufferAddress);
    return colorBuf.colors[gl_VertexIndex];
}

// Template function to calculate vertex output
void calVertexOutput(out vec4 pos, out vec3 wp, out vec3 normal, out vec3 tangent, out vec4 color, out vec2 texCoord) {
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_START*/
    vec4 position = getVertex();
    GpuVertexProps vertProps = getVertexProps();
    mat4 model = getModelMatrix();
    mat4 instance = getInstancingMatrix();
    model = instance * model;

    vec4 worldPos = model * position;
    wp = worldPos.xyz;
    pos = fpConst.viewProjection * worldPos;
    normal = mat3(model) * vertProps.normal;
    tangent = mat3(model) * vertProps.tangent;
    color = getVertexColor();
    texCoord = vertProps.texCoord;
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_END*/
}

void main() {
    materialId = meshDraw.materialId;
    calVertexOutput(gl_Position, fragPosition, fragNormal, fragTangent, fragColor, fragTexCoord);
}

