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
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    vec3 ambient;          // Ambient color
    float roughness;       // Roughness factor [0..1]
    vec3 normal;           // World-space normal (normalized)
    float ao;              // Ambient occlusion [0..1]
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity [0..1]
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

vec3 calculatePBRLighting(in PBRMaterial material, in Light light, in vec3 fragPos, in vec3 viewDir) {
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

    // Calculate F0
    vec3 F0 = mix(vec3(0.04), material.albedo, material.metallic);
    
    // Fresnel (kS) calculated once for both Specular and Diffuse
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    // Specular Term (Cook-Torrance)
    float NDF = distributionGGX(N, H, material.roughness);
    float G   = geometrySmith(N, V, L, material.roughness);
    // Optimization: NdotL in the denominator cancels with the NdotL in the final radiance multiplication
    // specular var here represents (fs * NdotL)
    vec3 specular = (NDF * G * F) / (4.0 * NdotV + 0.0001);
    
    // Diffuse Term (Energy Conservation)
    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - material.metallic);
    vec3 diffuse = kD * material.albedo * RECIPROCAL_PI;
    
    vec3 radiance = light.color * light.intensity * attenuation;
    
    // Combine terms. Note: 'specular' variable already contains the NdotL factor due to the optimization above.
    // We only need to multiply diffuse by NdotL.
    // Scale by PI to cancel out the 1/PI factor in the BRDF terms, making intensity=1.0 result in expected brightness
    return (diffuse * NdotL + specular) * radiance * PI;
}

vec3 pbrShadingSimple(PBRMaterial material, vec3 fragPos, vec3 viewDir, 
                Light light, vec3 ambientColor) {
    vec3 Lo = calculatePBRLighting(material, light, fragPos, viewDir);
    
    // Simplified Ambient (ao applied here)
    vec3 ambient = ambientColor * material.albedo * material.ao;
    vec3 color = ambient + Lo + material.emissive;
    
    return color;
}
