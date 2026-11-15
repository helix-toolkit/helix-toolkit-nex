#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/Atmosphere.glsl"

// Sky atmosphere background shader.
// ---------------------------------------------------------------------------
// Draws a dynamic, physically based sky as the scene background with the same
// geometry-free full-screen pass as psEnvironmentMap.glsl:
//
//   1. A full-screen triangle (Vert/vsFullScreenQuad.glsl) is emitted at clip-space
//      Z = 0, the far plane under reversed-Z, and depth-tested GREATER_EQUAL without
//      depth writes so only background pixels are shaded.
//   2. The world-space view ray is reconstructed through the inverse view-projection.
//   3. The ray is raymarched through the atmosphere (HxHeaders/Atmosphere.glsl) to get
//      the in-scattered sky radiance and the view transmittance; a sun disc is added.
//
// When spaceBackdrop is set and an environment cubemap is assigned, the cubemap is
// treated as what lies beyond the atmosphere (e.g. a star field): it is attenuated by
// the view transmittance, so it shows through at night and is washed out by the
// daytime sky.
//
// Below the horizon the flat-ground model has no air in front of the ray (and a ray
// exactly along the ground stays inside the faded-out ground layer), so those rays are
// shaded with the sky just above the horizon instead, blended over groundBlendSin toward
// groundColor lit by that horizon radiance. The ground therefore follows the time of day.
//
// Output is linear HDR radiance into the F16 scene colour target; tone mapping is done
// later by ToneMappingNode.

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

@code_gen
struct SkyAtmospherePushConstants {
    uint64_t fpConstAddress; // Device address of the Forward+ constants buffer (FPConstants).
    uint spaceBackdrop;      // 1 = draw the environment cubemap behind the atmosphere.
    uint _padding0;
    vec3 groundColor;        // Ground tint below the horizon, multiplied by the horizon sky radiance.
    float groundBlendSin;    // Sine of the angle below the horizon over which the sky fades into the ground.
};

layout(push_constant) uniform PushConstants {
    SkyAtmospherePushConstants value;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

void main() {
    FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;
    AtmosphereConstants atm = atmosphereResolveSun(fpConst);
    EnvironmentMapConstants env = fpConst.environmentMap;

    // Reconstruct the view ray (vsFullScreenQuad flips Y for texture space) from two finite
    // depths, so it works for orthographic cameras and infinite far planes.
    vec2 ndc = vec2(inTexCoord.x * 2.0 - 1.0, 1.0 - inTexCoord.y * 2.0);
    vec3 rayOrigin;
    vec3 rayDir;
    atmosphereViewRay(fpConst.inverseViewProjection, ndc, rayOrigin, rayDir);

    vec3 sunDir = atm.sunDirection;
    float observerAlt = atmosphereAltitude(atm, rayOrigin);
    // Static per-pixel dither in [0, 0.33) to break up banding between samples.
    float jitter = atmosphereIGN(gl_FragCoord.xy) * 0.33;

    vec3 transmittance;
    float groundMask;
    vec3 color = atmosphereSkyRadiance(
        atm,
        rayDir,
        observerAlt,
        jitter,
        pc.value.groundColor,
        pc.value.groundBlendSin,
        transmittance,
        groundMask
    );

    // The ground hides the space backdrop and the sun disc.
    vec3 beyond = vec3(0.0);

    // Space backdrop from the environment cubemap, seen through the atmosphere.
    if (pc.value.spaceBackdrop != 0u && env.envTexIndex != 0u) {
        float s = sin(env.rotationY);
        float c = cos(env.rotationY);
        vec3 envDir = vec3(c * rayDir.x + s * rayDir.z, rayDir.y, -s * rayDir.x + c * rayDir.z);
        float maxLod = float(max(textureBindlessQueryLevelsCube(env.envTexIndex) - 1, 0));
        float lod = clamp(env.mipLevel, 0.0, maxLod);
        vec3 space = textureBindlessCubeLod(env.envTexIndex, env.samplerIndex, envDir, lod).rgb;
        beyond += space * env.intensity * transmittance;
    }

    // Sun disc, dimmed by the atmosphere along the view ray and below the horizon.
    float directSunFactor = smoothstep(-0.08, 0.03, sunDir.y);
    float sunAboveHorizon = smoothstep(-0.04, 0.04, sunDir.y);
    float sunShape = atmosphereSunDisc(rayDir, sunDir, atm.sunDiscSize);
    const vec3 SUN_TINT = vec3(1.0, 0.97, 0.92);
    beyond += SUN_TINT * atm.sunColor * atm.sunIntensity * directSunFactor * 12.0 * sunShape * transmittance * sunAboveHorizon;

    color += beyond * (1.0 - groundMask);
    outColor = vec4(color, 1.0);
}
