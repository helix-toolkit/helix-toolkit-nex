#include "../Headers/HeaderFrag.glsl"
#include "../Headers/LightStruct.glsl"
#include "../Headers/PBRFunctions.glsl"
#include "../Headers/ForwardPlusConstants.glsl"
#include "../Headers/ForwardPlusGridBuffers.glsl"
#include "../Headers/MeshDraw.glsl"

layout(location = 0) in flat uint vertexIndex;
layout(location = 1) in vec3 fragPosition;
layout(location = 2) in vec3 fragNormal;
layout(location = 3) in vec2 fragTexCoord;
layout(location = 4) in vec3 fragTangent;
layout(location = 5) in vec4 fragColor;

layout(location = 0) out vec4 outColor;

@code_gen
struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    vec3 emissive;         // Emissive color
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    float opacity;         // Opacity/alpha [0..1]
    float vertexColorMix; // Vertex color mix factor [0..1], 0 = no vertex color, 1 = full vertex color
    uint albedoTexIndex;
    uint normalTexIndex;
    uint metallicRoughnessTexIndex;
    uint samplerIndex;
    float _padding;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MaterialBuffer {
    PBRProperties materials[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelMatrixBuffer {
    mat4 models[];
};


layout(push_constant) uniform Pc {
    MeshDraw value;
} pc;

FPConstants getFPConstants() {
    FPBuffer buf = FPBuffer(pc.value.forwardPlusConstantsAddress);
    return buf.fpConstants;
}

PBRProperties getPBRMaterial()
{
    MaterialBuffer materialBuf = MaterialBuffer(getFPConstants().materialBufferAddress);
    return materialBuf.materials[pc.value.materialId];
}

// Custom code injection point
// TEMPLATE_CUSTOM_CODE

void forwardPlusLighting(in PBRMaterial material, out vec4 outFinalColor)
{
    FPConstants fpConst = getFPConstants();
    // Forward+ tiled lighting
    vec3 viewDir = normalize(fpConst.cameraPosition - fragPosition);
    vec3 albedo = material.albedo; // Local var to avoid modifying input if const
    vec3 finalC = vec3(0.03) * albedo * material.ao; // Ambient

    // Calculate tile coordinates
    ivec2 tileCoord = ivec2(gl_FragCoord.xy) / ivec2(fpConst.tileSize);
    uint tileIndex = uint(tileCoord.y) * uint(fpConst.tileCount.x) + uint(tileCoord.x);

    // Get light list for this tile
    LightBuffer lightBuf = LightBuffer(fpConst.lightBufferAddress);
    LightGridBuffer lightGrid = LightGridBuffer(fpConst.lightGridBufferAddress);
    LightIndexBuffer lightIndices = LightIndexBuffer(fpConst.lightIndexBufferAddress);
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
    if (props.albedoTexIndex > 0)
    {
        material.albedo = material.albedo * texture(sampler2D(kTextures2D[props.albedoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).rgb;
    }
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);

    if (props.metallicRoughnessTexIndex > 0)
    {
        vec2 metallicRoughness = texture(sampler2D(kTextures2D[props.metallicRoughnessTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).bg;
        material.metallic = metallicRoughness.r;
        material.roughness = metallicRoughness.g;
    }

    material.normal = normalize(fragNormal);
    if (props.normalTexIndex > 0) {
        vec3 normalMap = texture(sampler2D(kTextures2D[props.normalTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).xyz * 2.0 - 1.0;
        vec3 N = normalize(fragNormal);
        vec3 T = normalize(fragTangent);
        vec3 B = cross(N, T);
        mat3 TBN = mat3(T, B, N);
        material.normal = normalize(TBN * normalMap);
    }

    return material;
/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
}

void nonLitOutputColor(in PBRMaterial material, out vec4 finalColor)
{
    finalColor = vec4(material.albedo + material.emissive, material.opacity);
}

// Shading model selection(0: PBR, 1: Non-Lit)
layout (constant_id = 0) const uint shadingModel = 0; 

// Template function to create final color
void outputColor(in PBRMaterial material, out vec4 finalColor)
{
/*TEMPLATE_OUTPUT_COLOR_IMPL_START*/
    if (shadingModel == 0u) {
        forwardPlusLighting(material, finalColor);
        return;
    } else if (shadingModel == 1u) {
        nonLitOutputColor(material, finalColor);
        return;
    } else {
        // Default to PBR lighting
        forwardPlusLighting(material, finalColor);
        return;
    }
/*TEMPLATE_OUTPUT_COLOR_IMPL_END*/
}

void main() {
    PBRMaterial material = createPBRMaterial();
    outputColor(material, outColor);
}
