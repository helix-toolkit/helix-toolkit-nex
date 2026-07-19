#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

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
// The environment parameters (camera matrices, cubemap index, intensity, blur,
// rotation) are read from the shared Forward+ constants buffer (FPConstants) via
// its device address, so the same values drive both this background pass and the
// PBR cubemap reflections. See EnvironmentMapConstants in ForwardPlusConstants.glsl.
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
    uint64_t fpConstAddress; // Device address of the Forward+ constants buffer (FPConstants).
};

layout(push_constant) uniform PushConstants {
    EnvironmentMapPushConstants value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

void main() {
    FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;
    EnvironmentMapConstants env = fpConst.environmentMap;

    // Reconstruct the pixel's normalized device coordinates from the full-screen
    // triangle's texture coordinates (vsFullScreenQuad flips Y for texture space).
    vec2 ndc = vec2(inTexCoord.x * 2.0 - 1.0, 1.0 - inTexCoord.y * 2.0);

    // Un-project the far-plane point (reversed-Z far = 0) to world space and form
    // the view ray direction from the camera through this pixel.
    vec4 farPoint = fpConst.inverseViewProjection * vec4(ndc, 0.0, 1.0);
    vec3 worldFar = farPoint.xyz / farPoint.w;
    vec3 dir = normalize(worldFar - fpConst.cameraPosition);

    // Optional yaw rotation of the environment about the world Y axis.
    float s = sin(env.rotationY);
    float c = cos(env.rotationY);
    dir = vec3(c * dir.x + s * dir.z, dir.y, -s * dir.x + c * dir.z);

    // Clamp the requested LOD to the cubemap's available mip range so a large
    // "blur" value degrades gracefully to the coarsest mip instead of clamping
    // to an undefined level.
    float maxLod = float(max(textureBindlessQueryLevelsCube(env.envTexIndex) - 1, 0));
    float lod = clamp(env.mipLevel, 0.0, maxLod);

    vec3 color = textureBindlessCubeLod(env.envTexIndex, env.samplerIndex, dir, lod).rgb;
    outColor = vec4(color * env.intensity, 1.0);
}
