// PBR (Physically Based Rendering) Functions for HelixToolkit.Nex.Shaders
// This file provides modular PBR shading functions that can be used by custom shaders
// Based on the Cook-Torrance BRDF model with GGX/Trowbridge-Reitz distribution

// Constants
#define PI 3.14159265359
#define RECIPROCAL_PI 0.31830988618
#define EPSILON 1e-6

// ============================================================================
// Material Structure
// ============================================================================
struct PBRMaterial {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    vec3 normal;           // World-space normal (normalized)
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity/alpha [0..1]
};

// ============================================================================
// Light Structure
// ============================================================================
struct Light {
    vec3 position;         // Light position (world space)
    uint type;              // Light type: 0=directional, 1=point, 2=spot
    vec3 direction;        // Light direction (for directional/spot lights)
    float range;           // Light range (for point/spot lights)
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
    float innerConeAngle;  // Inner cone angle (for spot lights)
    float outerConeAngle;  // Outer cone angle (for spot lights)
    vec2 _padding;          // Padding for alignment
};

// ============================================================================
// Utility Functions
// ============================================================================

// Convert roughness to alpha (perceptually linear roughness to alpha-squared)
float roughnessToAlpha(float roughness) {
    return roughness * roughness;
}

// Clamp a value to avoid numerical issues
vec3 safeDivide(vec3 numerator, float denominator) {
    return numerator / max(denominator, EPSILON);
}

// ============================================================================
// Fresnel Functions
// ============================================================================

// Schlick's approximation of Fresnel reflectance
vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Fresnel with roughness (for IBL)
vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// ============================================================================
// Normal Distribution Function (NDF)
// ============================================================================

// GGX/Trowbridge-Reitz Normal Distribution Function
float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    
    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    
    return nom / max(denom, EPSILON);
}

// ============================================================================
// Geometry Functions
// ============================================================================

// Schlick-GGX Geometry function
float geometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    
    float nom = NdotV;
    float denom = NdotV * (1.0 - k) + k;
    
    return nom / max(denom, EPSILON);
}

// Smith's Geometry function (combines view and light directions)
float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = geometrySchlickGGX(NdotV, roughness);
    float ggx1 = geometrySchlickGGX(NdotL, roughness);
    
    return ggx1 * ggx2;
}

// ============================================================================
// BRDF Functions
// ============================================================================

// Cook-Torrance BRDF specular term
vec3 cookTorranceBRDF(vec3 N, vec3 V, vec3 L, vec3 H, vec3 F0, float roughness) {
    float NDF = distributionGGX(N, H, roughness);
    float G = geometrySmith(N, V, L, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 numerator = NDF * G * F;
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float denominator = 4.0 * NdotV * NdotL;
    
    return safeDivide(numerator, denominator);
}

// Lambertian diffuse BRDF
vec3 lambertianDiffuse(vec3 albedo) {
    return albedo * RECIPROCAL_PI;
}

// ============================================================================
// Light Attenuation Functions
// ============================================================================

// Point light attenuation (inverse square law with smooth cutoff)
float getPointLightAttenuation(vec3 lightPos, vec3 fragPos, float range) {
    float distance = length(lightPos - fragPos);
    float attenuation = 1.0 / (distance * distance);
    
    // Smooth cutoff at range
    float cutoff = 1.0 - smoothstep(range * 0.75, range, distance);
    return attenuation * cutoff;
}

// Spot light attenuation
float getSpotLightAttenuation(vec3 lightPos, vec3 lightDir, vec3 fragPos, 
                               float innerAngle, float outerAngle, float range) {
    vec3 L = normalize(lightPos - fragPos);
    float theta = dot(L, normalize(-lightDir));
    float epsilon = innerAngle - outerAngle;
    float spotIntensity = clamp((theta - outerAngle) / epsilon, 0.0, 1.0);
    
    return spotIntensity * getPointLightAttenuation(lightPos, fragPos, range);
}

// ============================================================================
// Main PBR Lighting Calculation
// ============================================================================

// Calculate PBR lighting contribution from a single light source
vec3 calculatePBRLighting(PBRMaterial material, Light light, vec3 fragPos, vec3 viewDir) {
    // Calculate light direction based on light type
    vec3 L;
    float attenuation = 1.0;
    
    if (light.type == 0) {
        // Directional light
        L = normalize(-light.direction);
    } else if (light.type == 1) {
        // Point light
        L = normalize(light.position - fragPos);
        attenuation = getPointLightAttenuation(light.position, fragPos, light.range);
    } else if (light.type == 2) {
        // Spot light
        L = normalize(light.position - fragPos);
        attenuation = getSpotLightAttenuation(light.position, light.direction, fragPos,
                                               light.innerConeAngle, light.outerConeAngle, light.range);
    }
    
    vec3 N = normalize(material.normal);
    vec3 V = normalize(viewDir);
    vec3 H = normalize(V + L);
    
    // Calculate reflectance at normal incidence (F0)
    // For dielectric materials, use 0.04 as default
    // For metals, use albedo as F0
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, material.albedo, material.metallic);
    
    // Cook-Torrance BRDF
    vec3 specular = cookTorranceBRDF(N, V, L, H, F0, material.roughness);
    
    // Fresnel term (energy conservation)
    vec3 kS = fresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 kD = vec3(1.0) - kS;
    
    // Metallic materials have no diffuse lighting
    kD *= 1.0 - material.metallic;
    
    // Diffuse term
    vec3 diffuse = kD * lambertianDiffuse(material.albedo);
    
    // Combine diffuse and specular
    float NdotL = max(dot(N, L), 0.0);
    vec3 radiance = light.color * light.intensity * attenuation;
    
    return (diffuse + specular) * radiance * NdotL;
}

// ============================================================================
// Main PBR Shading Function (Final Output)
// ============================================================================

// Main PBR shading function that can be called from any fragment shader
// Supports multiple lights and includes ambient term
vec3 pbrShading(PBRMaterial material, vec3 fragPos, vec3 viewDir, 
                Light lights[16], int numLights, vec3 ambientColor) {
    vec3 Lo = vec3(0.0);
    
    // Accumulate lighting from all light sources
    for (int i = 0; i < numLights && i < 16; ++i) {
        Lo += calculatePBRLighting(material, lights[i], fragPos, viewDir);
    }
    
    // Ambient lighting (simplified)
    vec3 ambient = ambientColor * material.albedo * material.ao;
    
    // Emissive contribution
    vec3 emissive = material.emissive;
    
    // Combine all lighting terms
    vec3 color = ambient + Lo + emissive;
    
    return color;
}

// Simplified PBR shading with single directional light
vec3 pbrShadingSimple(PBRMaterial material, vec3 lightDir, vec3 lightColor, 
                      float lightIntensity, vec3 viewDir, vec3 ambientColor) {
    Light light;
    light.type = 0; // Directional
    light.direction = lightDir;
    light.color = lightColor;
    light.intensity = lightIntensity;
    light.position = vec3(0.0);
    light.range = 0.0;
    light.innerConeAngle = 0.0;
    light.outerConeAngle = 0.0;
    
    vec3 Lo = calculatePBRLighting(material, light, vec3(0.0), viewDir);
    vec3 ambient = ambientColor * material.albedo * material.ao;
    vec3 color = ambient + Lo + material.emissive;
    
    return color;
}
