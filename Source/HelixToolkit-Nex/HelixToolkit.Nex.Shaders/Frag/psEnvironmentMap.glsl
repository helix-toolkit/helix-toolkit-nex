#include "HxHeaders/HeaderFrag.glsl"

// Environment map (skybox) background shader.
// ---------------------------------------------------------------------------
// Draws an HDR environment cubemap as the scene background using a modern,
// geometry-free full-screen pass:
//
//   1. A single full-screen triangle (see Vert/vsFullScreenQuad.glsl) covers the
//      viewport. The vertex shader emits clip-space Z = 0 which, under this
//      engine's reversed-Z convention, is the far plane.
//   2. Per fragment we reconstruct the world-space view ray by un-projecting the
//      pixel's NDC position through the inverse view-projection matrix. This is
//      resolution independent and free of any cube-mesh seams or distortion.
//   3. The ray is optionally rotated about the world Y axis so the environment
//      can be oriented without re-baking the cubemap, then used to sample the
//      cubemap. A selectable mip level provides a cheap pre-blurred background
//      (useful for a soft/defocused backdrop or a low-roughness reflection look).
//
// The shader writes linear HDR radiance into the F16 scene colour target. Tone
// mapping / gamma are handled later by ToneMappingNode, so the background stays
// consistent with lit geometry. The pipeline uses a reversed-Z GREATER_EQUAL
// depth test with depth-writes disabled, so the background only fills pixels that
// no opaque geometry has covered.

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

@code_gen
struct EnvironmentMapPushConstants {
    mat4 invViewProj;      // Inverse view-projection matrix (world <- clip).
    vec3 cameraPosition;   // Camera position in world space.
    uint envTexIndex;      // Bindless index of the environment cubemap.
    uint samplerIndex;     // Bindless index of the sampler.
    float intensity;       // Linear radiance multiplier applied to the sampled colour.
    float mipLevel;        // Explicit cubemap LOD to sample (0 = sharpest). Enables a soft/blurred background.
    float rotationY;       // Environment yaw rotation about the world Y axis, in radians.
    uint flipMask;         // Cubemap axis-flip correction: bit0 = -X, bit1 = -Y, bit2 = -Z (handedness/orientation fix).
    uint _padding0;
    uint _padding1;
    uint _padding2;
};

layout(push_constant) uniform PushConstants {
    EnvironmentMapPushConstants value;
} pc;

void main() {
    // Reconstruct the pixel's normalized device coordinates from the full-screen
    // triangle's texture coordinates (vsFullScreenQuad flips Y for texture space).
    vec2 ndc = vec2(inTexCoord.x * 2.0 - 1.0, 1.0 - inTexCoord.y * 2.0);

    // Un-project the far-plane point (reversed-Z far = 0) to world space and form
    // the view ray direction from the camera through this pixel.
    vec4 farPoint = pc.value.invViewProj * vec4(ndc, 0.0, 1.0);
    vec3 worldFar = farPoint.xyz / farPoint.w;
    vec3 dir = normalize(worldFar - pc.value.cameraPosition);

    // Optional yaw rotation of the environment about the world Y axis.
    float s = sin(pc.value.rotationY);
    float c = cos(pc.value.rotationY);
    dir = vec3(c * dir.x + s * dir.z, dir.y, -s * dir.x + c * dir.z);

    // Cubemap orientation correction. Vulkan/D3D cube sampling uses a left-handed
    // convention; a right-handed world may need one or more axes negated so the
    // environment appears upright and un-mirrored. Driven from EnvironmentMapConfig
    // so it can be corrected per-asset without re-authoring the cubemap.
    dir *= vec3(
        (pc.value.flipMask & 1u) != 0u ? -1.0 : 1.0,
        (pc.value.flipMask & 2u) != 0u ? -1.0 : 1.0,
        (pc.value.flipMask & 4u) != 0u ? -1.0 : 1.0
    );

    // Clamp the requested LOD to the cubemap's available mip range so a large
    // "blur" value degrades gracefully to the coarsest mip instead of clamping
    // to an undefined level.
    float maxLod = float(max(textureBindlessQueryLevelsCube(pc.value.envTexIndex) - 1, 0));
    float lod = clamp(pc.value.mipLevel, 0.0, maxLod);

    vec3 color = textureBindlessCubeLod(pc.value.envTexIndex, pc.value.samplerIndex, dir, lod).rgb;
    outColor = vec4(color * pc.value.intensity, 1.0);
}
