// ============================================================================
// Light Structure
// ============================================================================
@code_gen
struct Light {
    vec3 position;         // Light position (world space)
    uint type;             // Light type: 0 == directional light, 1=point, 2=spot
    vec3 direction;        // Light direction (for spot lights)
    float range;           // Light range (for point/spot lights)
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
    vec2 spotAngles;       // x=inner, y=outer cone angles
    uint _padding0;          // Padding for alignment
    uint _padding1;
};

layout(buffer_reference, buffer_reference_align = 16) readonly buffer LightBuffer {
    Light lights[];
};

#define MAX_DIRECTIONAL_LIGHTS 3

@code_gen
struct DirectionalLight {
    vec3 direction;        // Light direction (for spot lights)
    uint _padding1;       // Padding for alignment
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
};

@code_gen
struct DirectionalLights {
    DirectionalLight lights[3];
    uint lightCount;
};

Light DirectionLightToLight(in DirectionalLight dirLight) {
    Light light;
    light.type = 0u; // Directional
    light.direction = dirLight.direction;
    light.color = dirLight.color;
    light.intensity = dirLight.intensity;
    return light;
}
