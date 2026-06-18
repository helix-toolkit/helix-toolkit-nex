// PBR (Physically Based Rendering) Functions for HelixToolkit.Nex.Shaders
// This file provides modular PBR shading functions that can be used by custom shaders
// Based on the Cook-Torrance BRDF model with GGX/Trowbridge-Reitz distribution

// Fixed PBR Functions
#define PI 3.14159265359
#define RECIPROCAL_PI 0.31830988618
#define EPSILON 1e-4 // Increased epsilon for better stability

#include "HxHeaders/LightStruct.glsl"

// ============================================================================
// Material Structure
// ============================================================================

struct PBRMaterial {
    vec3 albedo;               // Base color (sRGB)
    float metallic;            // Metallic factor [0..1]
    vec3 ambient;              // Ambient color
    float roughness;           // Roughness factor [0..1]
    vec3 normal;               // World-space normal (normalized)
    float ao;                  // Ambient occlusion [0..1]
    vec3 emissive;             // Emissive color
    float opacity;             // Opacity [0..1]
    float clearCoatStrength;   // Clear coat layer strength [0..1]
    float clearCoatRoughness;  // Clear coat layer roughness [0..1]
    float reflectance;         // Fresnel reflectance at normal incidence for dielectrics (default 0.04)
    float thickness;           // Local object thickness [0..1], used for subsurface/transmission (0 = thin, 1 = thick)
};



// ============================================================================
// Fresnel Functions
// ============================================================================

// Schlick's approximation of Fresnel reflectance
vec3 fresnelSchlick(float cosTheta, in vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}


// Fresnel with roughness (for IBL)
vec3 fresnelSchlickRoughness(float cosTheta, in vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// ============================================================================
// Refined Geometry & Distribution
// ============================================================================

float distributionGGX(in vec3 N, in vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    
    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return nom / (PI * denom * denom + 0.0001); // Added bias to prevent NaN
}

float geometrySchlickGGX(float NdotV, float roughness) {
    // Standard mapping for direct lighting
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    
    return NdotV / (NdotV * (1.0 - k) + k + 0.0001);
}

float geometrySmith(in vec3 N, in vec3 V, in vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return geometrySchlickGGX(NdotV, roughness) * geometrySchlickGGX(NdotL, roughness);
}

// ============================================================================
// Lighting Calculation
// ============================================================================

// Point light attenuation (inverse square law with smooth cutoff)

float getPointLightAttenuation(in vec3 lightPos, in vec3 fragPos, float range) {
    float distance = length(lightPos - fragPos);
    float attenuation = 1.0 / (distance * distance + 0.0001); // Add epsilon to prevent division by zero

    // Smooth cutoff at range
    float cutoff = 1.0 - smoothstep(range * 0.75, range, distance);
    return attenuation * cutoff;
}

// ============================================================================
// Transmission / Subsurface Scattering (thickness-map driven)
// ============================================================================

// Transmission approximation for KHR_materials_volume (glTF PBR).
// Transmits light from behind the surface, attenuated by thickness.
// Uses a 1/(1+t) curve for gentle thickness-to-transmission mapping, ensuring
// per-pixel thickness variation from thickness textures is visible.
//   material.thickness: 0 = thin-walled (full transmission), >0 = volumetric
//                       (less transmission with increasing thickness, world-space units)
//   subsurfaceColor: the tint of transmitted light after Beer-Lambert absorption
//   distortionStrength: how much the surface normal perturbs the back-light direction [0..1]
//   transmissionPower: sharpness of the forward-scatter lobe [1..20]
//   transmissionScale: overall transmission brightness from KHR_materials_transmission [0..1]
vec3 calculateTransmission(in PBRMaterial material, in Light light, in vec3 fragPos,
                           in vec3 viewDir, in vec3 subsurfaceColor,
                           float distortionStrength, float transmissionPower, float transmissionScale)
{
    vec3 L;
    float attenuation = 1.0;

    if (light.type == 0) {
        L = normalize(-light.direction);
    } else {
        L = normalize(light.position - fragPos);
        attenuation = getPointLightAttenuation(light.position, fragPos, light.range);
        if (light.type == 2) {
            float theta = dot(L, normalize(-light.direction));
            float epsilon = light.spotAngles.x - light.spotAngles.y;
            float spotIntensity = clamp((theta - light.spotAngles.y) / (epsilon + 0.0001), 0.0, 1.0);
            attenuation *= spotIntensity;
        }
    }

    vec3 N = normalize(material.normal);
    vec3 V = normalize(viewDir);

    // Perturb the back-light direction with the surface normal so curved surfaces
    // scatter more naturally.
    vec3 Lback = normalize(L + N * distortionStrength);

    // VdotL in the "back" direction: how directly the camera looks into the light
    // coming from behind the surface.
    float VdotLback = max(dot(V, -Lback), 0.0);

    // Thickness-to-transmission mapping.
    // Beer-Lambert (applied to subsurfaceColor before this function is called) already
    // handles the thickness-dependent color absorption. Here we only need a mild intensity
    // reduction for very thick regions to maintain energy plausibility.
    // When thickness=0 (thin-walled): thicknessFraction=1.0 (full transmission).
    float thicknessFraction = 1.0 / (1.0 + material.thickness * 0.5);

    // Transmission has two components:
    // 1. Directional scatter: narrow lobe for light visible through the object (view-dependent)
    // 2. Diffuse transmission: broad wrap lighting that simulates light diffusing through the volume
    // For WBOIT with closed meshes, light from any direction can transmit through the
    // object. Use the absolute NdotL so both front and back lights contribute to
    // transmission on both face orientations. This prevents the asymmetry where only
    // back faces show transmitted color.
    float wrapNdotL = max((abs(dot(N, L)) + 0.5) / 1.5, 0.0);

    float directionalScatter = pow(VdotLback, transmissionPower);
    float diffuseTransmission = wrapNdotL;

    // Combine: the diffuse wrap term dominates to provide broad volumetric coloring.
    // The Beer-Lambert tinted subsurfaceColor provides the thickness-dependent color variation.
    float transmission = (directionalScatter * 0.3 + diffuseTransmission * 0.7) * transmissionScale * thicknessFraction;

    vec3 radiance = light.color * light.intensity * attenuation;
    return subsurfaceColor * transmission * radiance;
}

// Per KHR_materials_transmission: transmissionFactor replaces the diffuse lobe with
// transmission. The diffuse contribution is scaled by (1 - transmissionFactor) so that
// fully transmissive materials (e.g., colored glass) show no diffuse reflection — only
// specular reflection and the transmitted light (computed separately via calculateTransmission).
vec3 calculatePBRLightingTransmissive(in PBRMaterial material, in Light light, in vec3 fragPos, in vec3 viewDir, float transmissionFactor) {
    vec3 L;
    float attenuation = 1.0;
    
    if (light.type == 0) {
        L = normalize(-light.direction);
    } else {
        L = normalize(light.position - fragPos);
        attenuation = getPointLightAttenuation(light.position, fragPos, light.range);
        
        if (light.type == 2) {
            // Fix: Assume spotAngles contains Cosine values
            // innerAngle (x) should be > outerAngle (y) in cosine space
            float theta = dot(L, normalize(-light.direction));
            float epsilon = light.spotAngles.x - light.spotAngles.y;
            float spotIntensity = clamp((theta - light.spotAngles.y) / (epsilon + 0.0001), 0.0, 1.0);
            attenuation *= spotIntensity;
        }
    }

    vec3 N = normalize(material.normal);
    vec3 V = normalize(viewDir);
    vec3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);

    // Calculate F0 using reflectance for dielectrics (default 0.04)
    vec3 F0 = mix(vec3(material.reflectance), material.albedo, material.metallic);

    // Fresnel (kS) calculated once for both Specular and Diffuse
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    // Specular Term (Cook-Torrance)
    float NDF = distributionGGX(N, H, material.roughness);
    float G   = geometrySmith(N, V, L, material.roughness);
    // Optimization: NdotL in the denominator cancels with the NdotL in the final radiance multiplication
    // specular var here represents (fs * NdotL)
    vec3 specular = (NDF * G * F) / (4.0 * NdotV + 0.0001);

    // Diffuse Term (Energy Conservation)
    // Per KHR_materials_transmission: diffuse is reduced by transmissionFactor since that
    // energy is redirected into the transmission lobe (computed in calculateTransmission).
    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - material.metallic) * (1.0 - transmissionFactor);
    vec3 diffuse = kD * material.albedo * RECIPROCAL_PI;

    vec3 radiance = light.color * light.intensity * attenuation;

    // Clear Coat Layer (thin dielectric lobe on top of the base material)
    // Uses a fixed F0 of 0.04 (air-to-polyurethane) and its own roughness
    vec3 clearCoatF0 = vec3(0.04);
    vec3 Fc = fresnelSchlick(max(dot(H, V), 0.0), clearCoatF0);
    float clearCoatNDF = distributionGGX(N, H, material.clearCoatRoughness);
    float clearCoatG   = geometrySmith(N, V, L, material.clearCoatRoughness);
    // Same NdotL optimisation as base specular
    vec3 clearCoatSpec = (clearCoatNDF * clearCoatG * Fc) / (4.0 * NdotV + 0.0001);
    // Attenuate the base layer by the clear coat Fresnel so energy is conserved
    float clearCoatAttenuation = 1.0 - material.clearCoatStrength * Fc.r;

    // Combine terms. Note: 'specular' variable already contains the NdotL factor due to the optimization above.
    // We only need to multiply diffuse by NdotL.
    // Scale by PI to cancel out the 1/PI factor in the BRDF terms, making intensity=1.0 result in expected brightness
    return ((diffuse * NdotL + specular) * clearCoatAttenuation + clearCoatSpec * material.clearCoatStrength) * radiance * PI;
}

vec3 calculatePBRLighting(in PBRMaterial material, in Light light, in vec3 fragPos, in vec3 viewDir) {
    return calculatePBRLightingTransmissive(material, light, fragPos, viewDir, 0.0);
}

vec3 pbrShadingSimple(PBRMaterial material, vec3 fragPos, vec3 viewDir, 
                Light light, vec3 ambientColor) {
    vec3 Lo = calculatePBRLighting(material, light, fragPos, viewDir);
    
    // Simplified Ambient (ao applied here)
    vec3 ambient = ambientColor * material.albedo * material.ao;
    vec3 color = ambient + Lo + material.emissive;
    
    return color;
}
