#extension GL_EXT_multiview : require
#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/Atmosphere.glsl"

// Sky atmosphere image-based-lighting bake.
// ---------------------------------------------------------------------------
// Renders the sky (HxHeaders/Atmosphere.glsl, atmosphereSkyRadiance) into all six faces of a
// cubemap in a single multiview draw: SkyAtmosphereIblNode renders with view mask 0b111111,
// so each view (gl_ViewIndex) writes one face. The node then builds the mip chain, which the
// PBR shader samples for diffuse (coarsest mip) and rough specular lighting.
//
//   - The sun disc is left out: the sun's direct light already comes from directional
//     light 0, and the tiny, very bright disc would leave sparkles in the blurred mips.
//   - The space backdrop is left out as well.
//   - The viewer sits at the fixed ground altitude rather than the camera, so the lighting
//     does not change as the camera moves.
//   - The ground below the horizon is kept and acts as bounce light from below.
//
// Each texel maps to a direction through the Vulkan cube face table, so sampling the cube
// with a world-space direction returns the sky seen along that direction.

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

@code_gen
struct SkyAtmosphereBakePushConstants {
    uint64_t fpConstAddress; // Device address of the Forward+ constants buffer (FPConstants).
    uint faceSize;           // Face width and height in texels.
    uint _padding0;
    vec3 groundColor;        // Ground tint below the horizon, multiplied by the horizon sky radiance.
    float groundBlendSin;    // Sine of the angle below the horizon over which the sky fades into the ground.
};

layout(push_constant) uniform PushConstants {
    SkyAtmosphereBakePushConstants value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

// World-space direction through texel coordinate st in [0, 1]^2 (t = 0 at the first row)
// of the given cube face, inverting the Vulkan cube map face selection table.
vec3 cubeFaceDirection(uint face, vec2 st) {
    float sc = st.x * 2.0 - 1.0;
    float tc = st.y * 2.0 - 1.0;
    vec3 dir;
    switch (face) {
        case 0u: dir = vec3(1.0, -tc, -sc); break;  // +X
        case 1u: dir = vec3(-1.0, -tc, sc); break;  // -X
        case 2u: dir = vec3(sc, 1.0, tc); break;    // +Y
        case 3u: dir = vec3(sc, -1.0, -tc); break;  // -Y
        case 4u: dir = vec3(sc, -tc, 1.0); break;   // +Z
        default: dir = vec3(-sc, -tc, -1.0); break; // -Z
    }
    return normalize(dir);
}

void main() {
    FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;
    AtmosphereConstants atm = atmosphereResolveSun(fpConst);

    vec2 st = gl_FragCoord.xy / float(max(pc.value.faceSize, 1u));
    // One view per cube face: 0..5 = +X, -X, +Y, -Y, +Z, -Z.
    vec3 rayDir = cubeFaceDirection(uint(gl_ViewIndex), st);

    vec3 transmittance;
    float groundMask;
    vec3 color = atmosphereSkyRadiance(
        atm,
        rayDir,
        atm.groundAltitude,
        0.5,
        pc.value.groundColor,
        pc.value.groundBlendSin,
        transmittance,
        groundMask
    );

    outColor = vec4(color, 1.0);
}
