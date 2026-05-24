#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/VertStruct.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/NodeInfo.glsl"
#include "HxHeaders/Instancing.glsl"
#include "HxHeaders/PBRProperties.glsl"

invariant gl_Position;

layout(location = 0) noperspective out vec3 vBarycentric;
layout(location = 1) flat out vec4 vColor;

@code_gen
struct WireframePushConstants {
vec4 color;

uint nodeIndex;
uint materialId;
uint64_t fpConstantBufferAddress;

uint64_t vertexBufferAddress;
uint64_t vertexPropsBufferAddress;

uint64_t indexBufferAddress;
uint64_t instancingBufferAddress;
};

layout(push_constant) uniform Pc {
    WireframePushConstants value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
    vec4 data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexPropsBuffer {
    GpuVertexProps value[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer IndexBuffer {
    uint data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer NodeInfoBuffer {
    GpuNodeInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstancingBuffer {
    InstanceTransform instancing[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MaterialBuffer {
    PBRProperties materials[];
};

VertexBuffer vertexBuffer = VertexBuffer(pc.value.vertexBufferAddress);
IndexBuffer indexBuffer = IndexBuffer(pc.value.indexBufferAddress);
FPBuffer fpBuffer = FPBuffer(pc.value.fpConstantBufferAddress);

GpuNodeInfo nodeInfo = NodeInfoBuffer(fpBuffer.value.nodeInfoBufferAddress).value[pc.value.nodeIndex];

GpuVertexProps emptyProps;

GpuVertexProps getVertexProps(uint index) {
    if (pc.value.vertexPropsBufferAddress == 0) {
        return emptyProps;
    }
    VertexPropsBuffer propsBuf = VertexPropsBuffer(pc.value.vertexPropsBufferAddress);
    return propsBuf.value[index];
}

void getDisplaceTex(uint materialId, out uint dispTex, out uint dispSampler, out float dispScale, out float dispBase) {
    if (fpBuffer.value.materialBufferAddress == 0) {
        dispTex = 0;
        dispSampler = 0;
        dispScale = 1.0;
        dispBase = 0.5;
        return;
    }
    MaterialBuffer materialBuf = MaterialBuffer(fpBuffer.value.materialBufferAddress);
    dispTex = materialBuf.materials[materialId].displaceTexIndex;
    dispSampler = materialBuf.materials[materialId].displaceSamplerIndex;
    dispScale = materialBuf.materials[materialId].displaceScale;
    dispBase = materialBuf.materials[materialId].displaceBase;
}

void main() {
    // gl_VertexIndex now increments perfectly from 0 to indexCount
    // We manually fetch the true vertex index
    uint realIndex = indexBuffer.data[gl_VertexIndex];
    
    // Fetch the vertex data manually
    vec4 position = vertexBuffer.data[realIndex];

    GpuVertexProps vertProps = getVertexProps(realIndex);

    uint displaceTex = 0; 
    uint displaceSampler = 0;
    float displaceScale = 1.0;
    float displaceBase = 0.5;
    getDisplaceTex(pc.value.materialId, displaceTex, displaceSampler, displaceScale, displaceBase);

    if (displaceTex != 0 && displaceSampler != 0 && displaceScale != 0) {        
        float h = textureBindless2D(displaceTex, displaceSampler, vertProps.texCoord).r - displaceBase;
        position.xyz += vertProps.normal * h * displaceScale;
    }

    vec4 worldPos = nodeInfo.transform * position;

    if (pc.value.instancingBufferAddress != 0) {
        InstanceTransform instance = InstancingBuffer(pc.value.instancingBufferAddress).instancing[gl_InstanceIndex];
        
        worldPos = vec4(transformCoordQuaternion(worldPos.xyz, instance.quaternion, instance.scale, instance.translation), 1);
    }
    
    gl_Position = fpBuffer.value.viewProjection * worldPos;
    
    // Because we are drawing non-indexed, gl_VertexIndex % 3 works perfectly!
    const vec3 barycentrics[3] = vec3[](
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, 1.0, 0.0),
        vec3(0.0, 0.0, 1.0)
    );
    vBarycentric = barycentrics[gl_VertexIndex % 3];
    vColor = pc.value.color;
}
