// ============================================================================
// Light Structure
// ============================================================================
@code_gen
struct Light {
    vec3 position;         // Light position (world space)
    uint type;              // Light type: 0=directional, 1=point, 2=spot
    vec3 direction;        // Light direction (for directional/spot lights)
    float range;           // Light range (for point/spot lights)
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
    vec2 spotAngles;       // x=inner, y=outer cone angles
    vec2 _padding;          // Padding for alignment
};

layout(buffer_reference, buffer_reference_align = 16) readonly buffer LightBuffer {
    Light lights[];
};
