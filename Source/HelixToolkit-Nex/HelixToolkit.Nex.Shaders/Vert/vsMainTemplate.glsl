#include "../Headers/HeaderVertex.glsl"
#include "../Headers/VertStruct.glsl"
#include "Headers/ForwardPlusConstants.glsl"

layout(location = 0) out flat uint vertexIndex;
layout(location = 1) out vec3 fragPosition;
layout(location = 2) out vec3 fragNormal;
layout(location = 3) out vec2 fragTexCoord;
layout(location = 4) out vec3 fragTangent;
layout(location = 5) out vec4 fragColor;

layout(push_constant) uniform Pc {
    MeshDraw value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
    GpuVertex vertices[];
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

FPConstants getFPConstants() {
    FPBuffer buf = FPBuffer(pc.value.forwardPlusConstantsAddress);
    return buf.fpConstants;
}

mat4 getModelMatrix() {
    ModelMatrixBuffer modelBuf = ModelMatrixBuffer(getFPConstants().modelMatrixBufferAddress);
    return modelBuf.models[pc.value.modelId];
}


GpuVertex getVertex() {
    VertexBuffer vertexBuf = VertexBuffer(pc.value.vertexBufferAddress);
    return vertexBuf.vertices[gl_VertexIndex];
}

vec4 getVertexColor() {
    if (pc.value.vertexColorBufferAddress == 0u) {
        return vec4(1.0);
    }
    VertexColorBuffer colorBuf = VertexColorBuffer(pc.value.vertexColorBufferAddress);
    return colorBuf.colors[gl_VertexIndex];
}

// Custom code injection point
// TEMPLATE_CUSTOM_CODE

// Template function to calculate vertex output
void calVertexOutput(out vec4 pos, out vec3 wp, out vec3 normal, out vec3 tangent, out vec4 color) {
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_START*/
    GpuVertex vertex = getVertex();
    mat4 model = getModelMatrix();

    vec4 worldPos = model * vec4(vertex.position, 1.0);
    wp = worldPos.xyz;
    pos = getFPConstants().viewProjection * worldPos;
    normal = mat3(model) * vertex.normal;
    tangent = mat3(model) * vertex.tangent;
    color = getVertexColor();
/*TEMPLATE_CALCULATE_VERTEX_OUTPUT_IMPL_END*/
}

void main() {
    calVertexOutput(gl_Position, fragPosition, fragNormal, fragTangent, fragColor);
    fragTexCoord = getVertex().texCoord;
    vertexIndex = gl_VertexIndex;
}

