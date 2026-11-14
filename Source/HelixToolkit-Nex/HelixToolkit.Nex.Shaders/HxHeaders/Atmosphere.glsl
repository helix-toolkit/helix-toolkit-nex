// ============================================================================
// Sky atmosphere (single scattering, flat-ground model)
// ============================================================================
// Shared by the sky background pass (Frag/psSkyAtmosphere.glsl) and the PBR template
// (aerial perspective on geometry and sunlight tinting). Adapted from the raymarched
// atmosphere in Maxime Heckel's "On Rendering the Sky, Sunsets, and Planets":
//
//   - Rayleigh, Mie and ozone participating media with exponential / layered densities.
//   - A primary raymarch along the view ray accumulating in-scattered sunlight, with a
//     short secondary march toward the sun for the sunlight transmittance.
//   - Flat-ground model with world +Y as up: altitude h = observerAltitude + t * dir.y.
//
// All lengths are in kilometers. World-space distances are converted with
// AtmosphereConstants.kmPerUnit. The output is linear HDR radiance; tone mapping is done
// later by ToneMappingNode.

#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"

const float ATMOSPHERE_PI = 3.14159265;
const int ATMOSPHERE_LIGHT_STEPS = 4;

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer AtmosphereSunLightBuffer {
    DirectionalLights value;
};

float atmosphereRayleighDensity(in AtmosphereConstants a, float h) {
    return exp(-max(h, 0.0) / max(a.rayleighScaleHeight, 1e-4));
}

float atmosphereMieDensity(in AtmosphereConstants a, float h) {
    float altitude = max(h, 0.0);
    float boundaryAerosols = exp(altitude / -max(a.mieScaleHeight, 1e-4));
    float upperHaze = 0.07 * exp(altitude / -8.0);
    return boundaryAerosols + upperHaze;
}

float atmosphereOzoneDensity(in AtmosphereConstants a, float h) {
    float x = clamp(h / max(a.atmosphereHeight, 1e-4), 0.0, 1.0);
    return smoothstep(0.15, 0.45, x) * (1.0 - smoothstep(0.55, 0.9, x));
}

float atmosphereRayleighPhase(float mu) {
    return 3.0 / (16.0 * ATMOSPHERE_PI) * (1.0 + mu * mu);
}

// Cornette-Shanks phase function.
float atmosphereMiePhase(in AtmosphereConstants a, float mu) {
    float g = a.mieG;
    float gg = g * g;
    float num = 3.0 * (1.0 - gg) * (1.0 + mu * mu);
    float den = (8.0 * ATMOSPHERE_PI) * (2.0 + gg) * pow(max(1.0 + gg - 2.0 * g * mu, 1e-4), 1.5);
    return num / den;
}

// Extinction for optical depths packed as (rayleigh, mie, ozone).
vec3 atmosphereExtinction(in AtmosphereConstants a, vec3 opticalDepth) {
    vec3 tau = a.rayleighBeta * opticalDepth.x
             + a.mieBetaExt * opticalDepth.y
             + a.ozoneBetaAbs * opticalDepth.z;
    return exp(-tau);
}

// Optical depth (rayleigh, mie, ozone) from altitude startHeight toward the sun.
vec3 atmosphereLightMarch(in AtmosphereConstants a, float startHeight, float muSun) {
    float denom = max(muSun + 0.15, 0.04);
    float maxDist = (a.atmosphereHeight - startHeight) / denom;
    float stepSize = max(maxDist, 0.0) / float(ATMOSPHERE_LIGHT_STEPS);
    vec3 od = vec3(0.0);

    for (int i = 0; i < ATMOSPHERE_LIGHT_STEPS; i++) {
        float t = (float(i) + 0.5) * stepSize;
        float h = startHeight + t * muSun;
        if (h < 0.0 || h > a.atmosphereHeight) {
            continue;
        }
        od.x += atmosphereRayleighDensity(a, h) * stepSize;
        if (a.mieEnabled != 0u) od.y += atmosphereMieDensity(a, h) * stepSize;
        if (a.ozoneEnabled != 0u) od.z += atmosphereOzoneDensity(a, h) * stepSize;
    }
    return od;
}

// Returns the atmosphere constants with the sun resolved against directional light 0 (when
// the scene has one): sunDirection becomes the reverse of the light's direction when
// sunFromDirectionalLight is set, sunIntensity becomes the light's intensity times
// directionalLightIntensityScale when sunIntensityFromDirectionalLight is set, and sunColor
// becomes the light's colour when sunColorFromDirectionalLight is set. Otherwise the
// configured values are kept.
AtmosphereConstants atmosphereResolveSun(in FPConstants fp) {
    AtmosphereConstants a = fp.atmosphere;
    a.sunDirection = normalize(a.sunDirection);
    bool followDirection = a.sunFromDirectionalLight != 0u;
    bool followIntensity = a.sunIntensityFromDirectionalLight != 0u;
    bool followColor = a.sunColorFromDirectionalLight != 0u;
    if ((followDirection || followIntensity || followColor) && fp.directionalLightsBufferAddress != 0) {
        AtmosphereSunLightBuffer buf = AtmosphereSunLightBuffer(fp.directionalLightsBufferAddress);
        if (buf.value.lightCount > 0u) {
            DirectionalLight sun = buf.value.lights[0];
            if (followDirection) {
                a.sunDirection = normalize(-sun.direction);
            }
            if (followIntensity) {
                a.sunIntensity = sun.intensity * a.directionalLightIntensityScale;
            }
            if (followColor) {
                a.sunColor = sun.color;
            }
        }
    }
    return a;
}

// Altitude of a world-space position in km.
float atmosphereAltitude(in AtmosphereConstants a, vec3 worldPos) {
    return a.groundAltitude + worldPos.y * a.kmPerUnit;
}

// Single-scattering integral along a view ray.
//   rayDir        normalized world-space view direction
//   rayLength     march length in km
//   observerAlt   altitude of the ray origin in km
//   primarySteps  number of primary samples
//   jitter        sample offset within each step, in [0, 1)
//   skyRay        true for background rays: stops at the ground plane and fades density
//                 near the ground to avoid an over-thick horizon over the flat ground
// Outputs the in-scattered radiance and the view transmittance (RGB).
void integrateAtmosphere(
    in AtmosphereConstants a,
    vec3 rayDir,
    vec3 sunDir,
    float rayLength,
    float observerAlt,
    int primarySteps,
    float jitter,
    bool skyRay,
    out vec3 inscatter,
    out vec3 transmittance)
{
    float sunElevation = sunDir.y;
    float skyLightFactor = smoothstep(-0.16, 0.06, sunElevation);

    float rayEnd = max(rayLength, 0.0);
    if (skyRay && rayDir.y < -1e-5) {
        float tGround = max(observerAlt, 0.0) / max(-rayDir.y, 1e-4);
        rayEnd = min(rayEnd, tGround);
    }
    float stepSize = rayEnd / float(max(primarySteps, 1));

    vec3 viewOD = vec3(0.0);
    vec3 sumR = vec3(0.0);
    vec3 sumM = vec3(0.0);
    float mu = dot(rayDir, sunDir);

    for (int i = 0; i < primarySteps; i++) {
        float t = (float(i) + jitter) * stepSize;
        float h = observerAlt + t * rayDir.y;
        if (h > a.atmosphereHeight) break;
        if (skyRay && h < 0.0) break;

        float groundFade = skyRay ? smoothstep(0.0, 2.0, h) : 1.0;
        float dR = atmosphereRayleighDensity(a, h) * groundFade;
        float dM = a.mieEnabled != 0u ? atmosphereMieDensity(a, h) * groundFade : 0.0;
        float dO = a.ozoneEnabled != 0u ? atmosphereOzoneDensity(a, h) * groundFade : 0.0;
        viewOD += vec3(dR, dM, dO) * stepSize;

        vec3 sunOD = atmosphereLightMarch(a, max(h, 0.0), sunElevation);
        vec3 sampleTransmittance = atmosphereExtinction(a, sunOD + viewOD);
        float altitude01 = clamp(h / max(a.atmosphereHeight, 1e-4), 0.0, 1.0);
        float minSunElevationForLight = mix(0.0, -0.16, altitude01);
        float sunlightVisibility = smoothstep(
            minSunElevationForLight - 0.02,
            minSunElevationForLight + 0.02,
            sunElevation
        );

        vec3 contribution = sampleTransmittance * sunlightVisibility * stepSize;
        sumR += dR * contribution;
        sumM += dM * contribution;
    }

    inscatter = a.sunColor * a.sunIntensity * skyLightFactor * (
        atmosphereRayleighPhase(mu) * a.rayleighBeta * sumR +
        atmosphereMiePhase(a, mu) * a.mieBeta * sumM
    );
    transmittance = atmosphereExtinction(a, viewOD);
}

// Fraction of direct sunlight reaching the given altitude (RGB), including the fade-out
// as the sun sinks below the horizon. Used to tint the sun directional light.
vec3 atmosphereSunTransmittance(in AtmosphereConstants a, vec3 sunDir, float altitude) {
    float directSunFactor = smoothstep(-0.08, 0.03, sunDir.y);
    return atmosphereExtinction(a, atmosphereLightMarch(a, max(altitude, 0.0), sunDir.y)) * directSunFactor;
}

// Soft sun disc with limb darkening and a faint halo; theta is measured from the sun center.
float atmosphereSunDisc(vec3 viewDir, vec3 sunDir, float discScale) {
    const float BASE_RADIUS = 0.005;
    float radius = BASE_RADIUS * max(discScale, 0.1);
    float theta = acos(clamp(dot(viewDir, sunDir), -1.0, 1.0));

    float core = exp(-pow(theta / max(radius * 0.7, 1e-5), 2.0));
    float limb = smoothstep(radius * 1.3, radius * 0.2, theta);
    float halo = exp(-theta / max(radius * 18.0, 1e-5));
    return core * limb + 0.25 * halo;
}

// Interleaved gradient noise (Jimenez 2014): a cheap, static per-pixel dither used to
// hide raymarch banding without a noise texture.
float atmosphereIGN(vec2 pixel) {
    return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}
