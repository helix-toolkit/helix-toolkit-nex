@code_gen
struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    vec3 emissive;         // Emissive color
    float roughness;       // Roughness factor [0..1]
    vec3 ambient;           // Ambient color
    float ao;              // Ambient occlusion [0..1]
    float opacity;         // Opacity/alpha [0..1]
    float vertexColorMix; // Vertex color mix factor [0..1], 0 = no vertex color, 1 = full vertex color
    float clearCoatStrength; // Clear coat layer strength [0..1]
    float clearCoatRoughness; // Clear coat layer roughness [0..1]

    float reflectance; // Fresnel reflectance at normal incidence (used if no albedo texture, typically 0.04 for dielectrics)
    uint albedoTexIndex; // Index into texture array for albedo map, 0 if not used
    uint normalTexIndex; // Index into texture array for normal map, 0 if not used
    uint metallicRoughnessTexIndex; // Index into texture array for metallic-roughness map, 0 if not used. R=metallic, G=roughness

    uint samplerIndex; // Index into sampler array for all textures, assuming same sampler is used for all material textures
    uint aoTexIndex; // Index into texture array for ambient occlusion map, 0 if not used
    uint bumpTexIndex; // Index into texture array for bump map, 0 if not used
    float bumpScale; // Scale of bump mapping effect, typically [0..1]

    uint displaceTexIndex; // Index into texture array for displacement map, 0 if not used
    uint displaceSamplerIndex; // Index into sampler array for displacement map, 0 if not used
    float displaceScale; // Scale of displacement mapping effect, typically [0..1]
    float displaceBase;  // Base height for displacement mapping, typically 0.5
};
