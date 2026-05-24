#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/VertStruct.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/NodeInfo.glsl"
#include "HxHeaders/Instancing.glsl"

invariant gl_Position;

layout(location = 0) noperspective out vec3 vBarycentric;
layout(location = 1) flat out vec4 vColor;

@code_gen
struct WireframePushConstants {
vec4 color;

uint nodeIndex;
uint64_t fpConstantBufferAddress;

uint64_t vertexBufferAddress;
uint64_t indexBufferAddress;

uint64_t nodeInfoBufferAddress;
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

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer IndexBuffer {
    uint data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer NodeInfoBuffer {
    GpuNodeInfo value[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstancingBuffer {
    InstanceTransform instancing[];
};

VertexBuffer vertexBuffer = VertexBuffer(pc.value.vertexBufferAddress);
IndexBuffer indexBuffer = IndexBuffer(pc.value.indexBufferAddress);
FPBuffer fpBuffer = FPBuffer(pc.value.fpConstantBufferAddress);

GpuNodeInfo nodeInfo = NodeInfoBuffer(fpBuffer.value.nodeInfoBufferAddress).value[pc.value.nodeIndex];

void main() {
    // gl_VertexIndex now increments perfectly from 0 to indexCount
    // We manually fetch the true vertex index
    uint realIndex = indexBuffer.data[gl_VertexIndex];
    
    // Fetch the vertex data manually
    vec4 position = vertexBuffer.data[realIndex];

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
