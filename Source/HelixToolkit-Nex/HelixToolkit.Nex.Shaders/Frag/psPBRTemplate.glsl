#include "../Headers/HeaderFrag.glsl"
#include "../Headers/LightStruct.glsl"
#include "../Headers/PBRFunctions.glsl"
#include "../Headers/ForwardPlusConstants.glsl"
#include "../Headers/ForwardPlusGridBuffers.glsl"


layout(location = 0) in flat uint vertexIndex;
layout(location = 1) in vec3 fragPosition;
layout(location = 2) in vec3 fragNormal;
layout(location = 3) in vec2 fragTexCoord;
layout(location = 4) in vec3 fragTangent;
layout(location = 5) in vec4 fragColor;

layout(location = 0) out vec4 outColor;

struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity/alpha [0..1]
    uint albedoTexIndex;
    uint normalTexIndex;
    uint metallicRoughnessTexIndex;
    uint samplerIndex;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelParamsBuffer {
    ModelParams modelParams;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MaterialBuffer {
    PBRProperties materials[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelMatrixBuffer {
    mat4 models[];
};


layout(push_constant) uniform Pc {
    ForwardPlusConstants value;
} pc;

ModelParams getModelParams() {
    ModelParamsBuffer paramsBuf = ModelParamsBuffer(pc.value.perModelParamsBufferAddress);
    return paramsBuf.modelParams;
}

PBRProperties getPBRMaterial()
{
    MaterialBuffer materialBuf = MaterialBuffer(pc.value.materialBufferAddress);
    ModelParams modelParams = getModelParams();
    return materialBuf.materials[modelParams.materialId];
}

// Custom code injection point
// TEMPLATE_CUSTOM_CODE

void forwardPlusLighting(in PBRMaterial material, out vec4 outFinalColor)
{
    // Forward+ tiled lighting
    vec3 viewDir = normalize(pc.value.cameraPosition - fragPosition);
    vec3 albedo = material.albedo; // Local var to avoid modifying input if const
    vec3 finalC = vec3(0.03) * albedo * material.ao; // Ambient

    // Calculate tile coordinates
    ivec2 tileCoord = ivec2(gl_FragCoord.xy) / ivec2(pc.value.tileSize);
    uint tileIndex = uint(tileCoord.y) * uint(pc.value.tileCount.x) + uint(tileCoord.x);

    // Get light list for this tile
    LightBuffer lightBuf = LightBuffer(pc.value.lightBufferAddress);
    LightGridBuffer lightGrid = LightGridBuffer(pc.value.lightGridBufferAddress);
    LightIndexBuffer lightIndices = LightIndexBuffer(pc.value.lightIndexBufferAddress);
    LightGridTile tile = lightGrid.tiles[tileIndex];

    // Process lights in this tile
    for (uint i = 0; i < tile.lightCount; ++i) {
        uint lightIndex = lightIndices.indices[tile.lightIndexOffset + i];
        Light light = lightBuf.lights[lightIndex];
        vec3 lightContribution = calculatePBRLighting(material, light, fragPosition, viewDir);
        finalC += lightContribution;
    }

    finalC += material.emissive;

    // Tone mapping
    finalC = finalC / (finalC + vec3(1.0));

    // Gamma correction
    finalC = pow(finalC, vec3(1.0/2.2));

    outFinalColor = vec4(finalC, material.opacity);
}

// Template function to create final PBR material properties
PBRMaterial createPBRMaterial()
{

/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_START*/
    PBRProperties props = getPBRMaterial();
    PBRMaterial material;
    material.albedo = props.albedo;
    material.roughness = props.roughness;
    material.metallic = props.metallic;
    material.ao = props.ao;
    material.emissive = props.emissive;
    material.opacity = props.opacity;
    #ifdef USE_BASE_COLOR_TEXTURE
        material.albedo = props.albedo * texture(sampler2D(kTextures2D[pc.value.baseColorTexIndex], kSamplers[pc.value.samplerIndex]), fragTexCoord).rgb;
    #endif

    #ifdef USE_METALLIC_ROUGHNESS_TEXTURE
        vec2 metallicRoughness = texture(sampler2D(kTextures2D[pc.value.metallicRoughnessTexIndex], kSamplers[pc.value.samplerIndex]), fragTexCoord).bg;
        material.metallic = metallicRoughness.r;
        material.roughness = metallicRoughness.g;
    #endif

    #ifdef USE_NORMAL_TEXTURE
        vec3 normalMap = texture(sampler2D(kTextures2D[pc.value.normalTexIndex], kSamplers[pc.value.samplerIndex]), fragTexCoord).xyz * 2.0 - 1.0;
        vec3 N = normalize(fragNormal);
        vec3 T = normalize(fragTangent);
        vec3 B = cross(N, T);
        mat3 TBN = mat3(T, B, N);
        material.normal = normalize(TBN * normalMap);
    #else
        material.normal = normalize(fragNormal);
    #endif

    return material;
/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
}

// Template function to create final color
void outputColor(in PBRMaterial material, out vec4 finalColor)
{
    forwardPlusLighting(material, finalColor);
}

void main() {
    PBRMaterial material = createPBRMaterial();
    outputColor(material, outColor);
}
