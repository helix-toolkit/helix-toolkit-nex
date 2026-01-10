// Example Fragment Shader using PBR Functions
// This demonstrates how to use the PBRFunctions.glsl in a custom shader

#version 460

// Include the PBR functions
// Note: In your shader pipeline, you would typically include this via your build system
// For example: #include "PBRFunctions.glsl"

layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;
layout(location = 3) in vec3 fragTangent;

layout(location = 0) out vec4 outColor;

// Push constants or uniform buffer for camera
layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    float time;
} pc;

// Texture samplers (using bindless textures as per HeaderFrag.glsl)
layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

// Material texture indices
layout(constant_id = 0) const uint albedoTexId = 0;
layout(constant_id = 1) const uint normalTexId = 1;
layout(constant_id = 2) const uint metallicRoughnessTexId = 2;
layout(constant_id = 3) const uint aoTexId = 3;
layout(constant_id = 4) const uint emissiveTexId = 4;
layout(constant_id = 5) const uint samplerId = 0;

void main() {
    // Sample textures
    vec4 albedoSample = texture(sampler2D(kTextures2D[albedoTexId], kSamplers[samplerId]), fragTexCoord);
    vec3 normalSample = texture(sampler2D(kTextures2D[normalTexId], kSamplers[samplerId]), fragTexCoord).xyz;
    vec2 metallicRoughness = texture(sampler2D(kTextures2D[metallicRoughnessTexId], kSamplers[samplerId]), fragTexCoord).bg;
    float ao = texture(sampler2D(kTextures2D[aoTexId], kSamplers[samplerId]), fragTexCoord).r;
    vec3 emissive = texture(sampler2D(kTextures2D[emissiveTexId], kSamplers[samplerId]), fragTexCoord).rgb;
    
    // Prepare PBR material
    PBRMaterial material;
    material.albedo = albedoSample.rgb;
    material.metallic = metallicRoughness.r;
    material.roughness = metallicRoughness.g;
    material.ao = ao;
    material.opacity = albedoSample.a;
    material.emissive = emissive;
    
    // Transform normal from tangent space to world space
    // (simplified - in production you'd use TBN matrix)
    vec3 N = normalize(fragNormal);
    vec3 normalMap = normalSample * 2.0 - 1.0;
    vec3 T = normalize(fragTangent);
    vec3 B = cross(N, T);
    mat3 TBN = mat3(T, B, N);
    material.normal = normalize(TBN * normalMap);
    
    // Calculate view direction
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    
    // Setup a simple directional light (e.g., sun)
    vec3 lightDirection = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 lightColor = vec3(1.0, 0.95, 0.9);
    float lightIntensity = 3.0;
    
    // Ambient lighting
    vec3 ambientColor = vec3(0.03);
    
    // Calculate PBR lighting (simplified version)
    vec3 finalColor = pbrShadingSimple(
        material,
        lightDirection,
        lightColor,
        lightIntensity,
        viewDir,
        ambientColor
    );
    
    // Tone mapping (simple Reinhard)
    finalColor = finalColor / (finalColor + vec3(1.0));
    
    // Gamma correction
    finalColor = pow(finalColor, vec3(1.0/2.2));
    
    // Output final color
    outColor = vec4(finalColor, material.opacity);
}
