#include "HxHeaders/HeaderVertex.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

// A single bone of the skeleton visualization, in world space.
@code_gen
struct BoneLine {
    vec3 start;
    uint _padding0;
    vec3 end;
    uint _padding1;
    vec4 color;
};

// Push constants for skeleton bone visualization. All bones are drawn in one instanced draw:
// one instance per bone (read from bonesAddress), each a procedural line list.
// style: 0 = line (2 vertices), 1 = arrow (18 vertices), 2 = octahedral (24 vertices).
// headSize: arrow head length / octahedron waist position, as a fraction of the bone length.
@code_gen
struct BoneLinePushConstant {
    uint64_t bonesAddress;
    uint64_t fpConstAddress;
    uint style;
    float headSize;
};

layout(push_constant) uniform PC {
    BoneLinePushConstant value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants value;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer BoneBuffer {
    BoneLine bones[];
};

layout(location = 0) out flat vec4 color;

const uint STYLE_ARROW = 1u;
const uint STYLE_OCTAHEDRAL = 2u;

// Corner k (0..3) of a square ring of the given radius around the bone axis at center.
vec3 ringPoint(vec3 center, vec3 u, vec3 v, float radius, uint k) {
    const vec2 CORNERS[4] = vec2[4](vec2(1, 0), vec2(0, 1), vec2(-1, 0), vec2(0, -1));
    vec2 c = CORNERS[k & 3u] * radius;
    return center + u * c.x + v * c.y;
}

vec3 bonePosition(vec3 start, vec3 end, uint segment, uint endpoint) {
    vec3 dir = end - start;
    float len = length(dir);
    if (pc.value.style == 0u || len < 1e-6) {
        return endpoint == 0u ? start : end;
    }
    vec3 axis = dir / len;
    vec3 up = abs(axis.y) < 0.99 ? vec3(0, 1, 0) : vec3(1, 0, 0);
    vec3 u = normalize(cross(axis, up));
    vec3 v = cross(axis, u);
    float head = clamp(pc.value.headSize, 0.0, 1.0) * len;

    if (pc.value.style == STYLE_ARROW) {
        // Segment 0: shaft. 1..4: ring -> tip. 5..8: ring edges.
        if (segment == 0u) {
            return endpoint == 0u ? start : end;
        }
        vec3 base = end - axis * head;
        float radius = head * 0.4;
        if (segment <= 4u) {
            return endpoint == 0u ? ringPoint(base, u, v, radius, segment - 1u) : end;
        }
        uint k = segment - 5u;
        return ringPoint(base, u, v, radius, endpoint == 0u ? k : k + 1u);
    }

    // Octahedral: 0..3: start -> ring. 4..7: ring -> end. 8..11: ring edges.
    vec3 waist = start + axis * head;
    float radius = head;
    if (segment < 4u) {
        return endpoint == 0u ? start : ringPoint(waist, u, v, radius, segment);
    }
    if (segment < 8u) {
        return endpoint == 0u ? ringPoint(waist, u, v, radius, segment - 4u) : end;
    }
    uint k = segment - 8u;
    return ringPoint(waist, u, v, radius, endpoint == 0u ? k : k + 1u);
}

void main() {
    FPBuffer fpBuf = FPBuffer(pc.value.fpConstAddress);
    BoneLine bone = BoneBuffer(pc.value.bonesAddress).bones[gl_InstanceIndex];
    uint segment = uint(gl_VertexIndex) / 2u;
    uint endpoint = uint(gl_VertexIndex) % 2u;
    vec3 position = bonePosition(bone.start, bone.end, segment, endpoint);
    gl_Position = fpBuf.value.viewProjection * vec4(position, 1.0);
    color = bone.color;
}
