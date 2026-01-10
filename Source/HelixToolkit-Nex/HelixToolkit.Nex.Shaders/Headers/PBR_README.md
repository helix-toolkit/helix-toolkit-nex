# PBR Shading Functions for HelixToolkit.Nex.Shaders

This module provides a complete, modular Physically Based Rendering (PBR) implementation in GLSL that can be easily integrated into custom shaders.

## Overview

The PBR implementation is based on the **Cook-Torrance BRDF** model with:
- **GGX/Trowbridge-Reitz** normal distribution function
- **Schlick-GGX** geometry function (Smith's method)
- **Schlick's approximation** for Fresnel reflectance
- **Lambertian diffuse** for energy conservation

## Files

- **PBRFunctions.glsl** - Core PBR shading library with all functions
- **ExamplePBRShader.frag** - Example fragment shader demonstrating usage

## Features

### Material Properties
The `PBRMaterial` struct supports:
- **Albedo** - Base color (RGB)
- **Metallic** - Metallic factor (0-1)
- **Roughness** - Surface roughness (0-1)
- **Ambient Occlusion** - AO factor (0-1)
- **Normal** - World-space surface normal
- **Emissive** - Self-illumination color
- **Opacity** - Transparency/alpha (0-1)

### Light Types
The `Light` struct supports three light types:
1. **Directional** (type = 0) - Sunlight, constant direction
2. **Point** (type = 1) - Omnidirectional with attenuation
3. **Spot** (type = 2) - Cone-shaped with inner/outer angles

### Key Functions

#### Main Shading Functions

```glsl
// Full PBR shading with multiple lights
vec3 pbrShading(PBRMaterial material, vec3 fragPos, vec3 viewDir, 
                Light lights[16], int numLights, vec3 ambientColor);

// Simplified PBR with single directional light
vec3 pbrShadingSimple(PBRMaterial material, vec3 lightDir, vec3 lightColor, 
                      float lightIntensity, vec3 viewDir, vec3 ambientColor);
```

#### BRDF Components

```glsl
// Fresnel reflectance (Schlick's approximation)
vec3 fresnelSchlick(float cosTheta, vec3 F0);

// GGX Normal Distribution Function
float distributionGGX(vec3 N, vec3 H, float roughness);

// Smith's Geometry function
float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness);

// Cook-Torrance specular BRDF
vec3 cookTorranceBRDF(vec3 N, vec3 V, vec3 L, vec3 H, vec3 F0, float roughness);

// Lambertian diffuse BRDF
vec3 lambertianDiffuse(vec3 albedo);
```

## Usage Example

### Basic Integration

```glsl
#version 460

// Include PBR functions (adjust path as needed)
#include "PBRFunctions.glsl"

layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
} pc;

void main() {
    // Setup material
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.1, 0.1);  // Red base color
    material.metallic = 0.0;                 // Non-metallic
    material.roughness = 0.5;                // Medium roughness
    material.ao = 1.0;                       // No occlusion
    material.normal = normalize(fragNormal);
    material.emissive = vec3(0.0);
    material.opacity = 1.0;
    
    // Setup light
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 lightColor = vec3(1.0);
    float lightIntensity = 3.0;
    
    // Calculate view direction
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    
    // Render with PBR
    vec3 color = pbrShadingSimple(
        material, lightDir, lightColor, lightIntensity,
        viewDir, vec3(0.03)
    );
    
    // Tone mapping + gamma correction
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0/2.2));
    
    outColor = vec4(color, 1.0);
}
```

### Multiple Lights Example

```glsl
void main() {
    // ... setup material as above ...
    
    // Define multiple lights
    Light lights[3];
    
    // Directional light (sun)
    lights[0].type = 0;
    lights[0].direction = normalize(vec3(-0.5, -1.0, -0.3));
    lights[0].color = vec3(1.0, 0.95, 0.9);
    lights[0].intensity = 3.0;
    
    // Point light 1
    lights[1].type = 1;
    lights[1].position = vec3(5.0, 3.0, 2.0);
    lights[1].color = vec3(1.0, 0.5, 0.2);
    lights[1].intensity = 10.0;
    lights[1].range = 15.0;
    
    // Spot light
    lights[2].type = 2;
    lights[2].position = vec3(-2.0, 5.0, 0.0);
    lights[2].direction = normalize(vec3(0.5, -1.0, 0.0));
    lights[2].color = vec3(0.2, 0.5, 1.0);
    lights[2].intensity = 20.0;
    lights[2].range = 20.0;
    lights[2].innerConeAngle = cos(radians(15.0));
    lights[2].outerConeAngle = cos(radians(25.0));
    
    // Render with multiple lights
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    vec3 color = pbrShading(material, fragPosition, viewDir, lights, 3, vec3(0.03));
    
    // Post-processing...
    outColor = vec4(color, material.opacity);
}
```

### Texture-Based Materials

```glsl
// Sample material properties from textures
vec4 albedoSample = texture(sampler2D(albedoTex, linearSampler), fragTexCoord);
vec2 metallicRoughness = texture(sampler2D(mrTex, linearSampler), fragTexCoord).bg;
float ao = texture(sampler2D(aoTex, linearSampler), fragTexCoord).r;

PBRMaterial material;
material.albedo = albedoSample.rgb;
material.metallic = metallicRoughness.r;
material.roughness = metallicRoughness.g;
material.ao = ao;
material.normal = getNormalFromMap(normalTex, fragTexCoord, fragNormal, fragTangent);
material.emissive = vec3(0.0);
material.opacity = albedoSample.a;
```

## Technical Details

### Energy Conservation
The implementation ensures proper energy conservation through:
- Fresnel term (kS) for specular reflection
- Complementary diffuse term (kD = 1 - kS)
- Metallic materials have kD = 0 (no diffuse)

### Metallic Workflow
- **Dielectric materials** (metallic = 0): F0 = 0.04, colored diffuse
- **Metallic materials** (metallic = 1): F0 = albedo, no diffuse
- **In-between values**: Linear interpolation

### Roughness Model
- Roughness is perceptually linear (0 = mirror, 1 = rough)
- Internally converted to alpha-squared for NDF calculations
- Ensures smooth transitions across roughness values

## Integration with HelixToolkit.Nex

This shader can be integrated with the existing bindless texture system:

```glsl
// Use existing texture bindings from HeaderFrag.glsl
layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

// Sample using bindless approach
vec4 albedo = textureBindless2D(albedoTexId, samplerId, fragTexCoord);
```

## Performance Considerations

1. **Light Loops**: Limited to 16 lights maximum to avoid excessive fragment shader complexity
2. **Branching**: Light type selection uses if-else which may impact performance on older GPUs
3. **Optimization**: Consider using light culling or deferred shading for many lights
4. **Precision**: Uses `float` precision; consider `mediump` for mobile if needed

## Future Enhancements

Potential additions:
- Image-Based Lighting (IBL) with environment maps
- Subsurface scattering
- Clear coat layer
- Anisotropic reflections
- Shadow mapping integration
- Screen-space reflections

## References

- [Real Shading in Unreal Engine 4](https://blog.selfshadow.com/publications/s2013-shading-course/karis/s2013_pbs_epic_notes_v2.pdf) - Epic Games
- [Physically Based Shading at Disney](https://media.disneyanimation.com/uploads/production/publication_asset/48/asset/s2012_pbs_disney_brdf_notes_v3.pdf) - Disney
- [LearnOpenGL PBR Theory](https://learnopengl.com/PBR/Theory) - Joey de Vries

## License

MIT License - Same as HelixToolkit.Nex project

## Author

Created for HelixToolkit.Nex.Shaders - 2024
