#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/PBRFunctions.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#include "HxHeaders/MeshDraw.glsl"

layout(location = 0) in vec3 fragWorldPos;
layout(location = 1) in flat uint materialId;
layout(location = 2) in vec4 fragColor;
#ifndef EXCLUDE_MESH_PROPS
layout(location = 3) in vec3 fragNormal;
layout(location = 4) in vec3 fragTangent;
layout(location = 5) in vec2 fragTexCoord;
#endif

// Ensure FragCoord (0,0) is Top-Left to match Compute Shader tile generation
// layout(origin_upper_left) in vec4 gl_FragCoord;

layout(location = 0) out vec4 outColor;

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
    uint albedoTexIndex;
    uint normalTexIndex;
    uint metallicRoughnessTexIndex;
    uint samplerIndex;
    uint _padding0;
    uint _padding1;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MaterialBuffer {
    PBRProperties materials[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) readonly buffer DirectionalLightBuffer {
    DirectionalLights value;
};

layout(buffer_reference, scalar) readonly buffer LightGridBuffer {
    LightGridTile tiles[];
};

layout(buffer_reference, scalar) readonly buffer LightIndexBuffer {
    uint indices[];
};


layout(push_constant) uniform Pc {
    MeshDrawPushConstant value;
} pc;

layout (constant_id = 0) const uint MATERIAL_TYPE = 0; 

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

// Utility Functions
PBRProperties getPBRMaterial()
{
    MaterialBuffer materialBuf = MaterialBuffer(fpConst.materialBufferAddress);
    return materialBuf.materials[materialId];
}

float getTime() {
    return fpConst.time;
}

mat4 getViewProjection() {
    return fpConst.viewProjection;
}

mat4 getInvViewProjection() {
    return fpConst.inverseViewProjection;
}

vec3 getCameraPosition() {
    return fpConst.cameraPosition;
}

vec2 getScreenSize() {
    return fpConst.screenDimensions;
}

// Custom code injection point
// TEMPLATE_CUSTOM_CODE

vec4 forwardPlusLighting(in PBRMaterial material)
{
    // Forward+ tiled lighting
    vec3 viewDir = normalize(fpConst.cameraPosition - fragWorldPos);
    vec3 finalC = material.ambient * material.albedo * material.ao;
    if (fpConst.lightCount > 0 && fpConst.lightBufferAddress != 0) {
        LightBuffer lightBuf = LightBuffer(fpConst.lightBufferAddress);
        if (fpConst.enabled == 0) {
            for (uint i = 0; i < fpConst.lightCount; ++i) {
                Light light = lightBuf.lights[i];
                vec3 lightContribution = calculatePBRLighting(material, light, fragWorldPos, viewDir);
                finalC += lightContribution;
            }
        } else {
            // Calculate tile coordinates
            // The compute shader flips pixelCoord.y (1.0 - y) before sampling depth,
            // so tile row 0 processes the bottom of the screen.
            // We must flip the fragment's pixel Y in the same pixel space before
            // dividing by tile size. This avoids the asymmetry that arises when
            // screenDimensions is not a multiple of tileSize (the partial tile row
            // must stay on the same screen edge in both shaders).
            uvec2 flippedPixel = uvec2(gl_FragCoord.x, fpConst.screenDimensions.y - 1.0 - gl_FragCoord.y);
            uvec2 tileCoord = flippedPixel / uvec2(fpConst.tileSize);
            tileCoord = min(tileCoord, uvec2(fpConst.tileCountX - 1, fpConst.tileCountY - 1));
            uint tileIndex = tileCoord.y * fpConst.tileCountX + tileCoord.x;

            // Get light list for this tile
            LightGridBuffer lightGrid = LightGridBuffer(fpConst.lightGridBufferAddress);
            LightIndexBuffer lightIndices = LightIndexBuffer(fpConst.lightIndexBufferAddress);
            LightGridTile tile = lightGrid.tiles[tileIndex];
            // Process lights in this tile
            for (uint i = 0; i < tile.lightCount; ++i) {
                uint lightIndex = lightIndices.indices[tile.lightIndexOffset + i];
                Light light = lightBuf.lights[lightIndex];
                vec3 lightContribution = calculatePBRLighting(material, light, fragWorldPos, viewDir);
               finalC += lightContribution;
            }
        }
    }

    if (fpConst.directionalLightsBufferAddress != 0) {
        DirectionalLightBuffer dirLightBuf = DirectionalLightBuffer(fpConst.directionalLightsBufferAddress);
        for (uint i = 0; i < dirLightBuf.value.lightCount; ++i) {
            Light dirLight = DirectionLightToLight(dirLightBuf.value.lights[i]);
            vec3 lightContribution = calculatePBRLighting(material, dirLight, fragWorldPos, viewDir);
            finalC += lightContribution;
        }
    }
    finalC += material.emissive;

    return vec4(finalC, material.opacity);
}

vec4 debugTileLighting()
{
    // Calculate tile coordinates — flip in pixel space to match compute shader
    uvec2 flippedPixel = uvec2(gl_FragCoord.x, fpConst.screenDimensions.y - 1.0 - gl_FragCoord.y);
    uvec2 tileCoord = flippedPixel / uvec2(fpConst.tileSize);
    tileCoord = min(tileCoord, uvec2(fpConst.tileCountX - 1, fpConst.tileCountY - 1));
    uint tileIndex = tileCoord.y * fpConst.tileCountX + tileCoord.x;
    // Get light list for this tile
    LightGridBuffer lightGrid = LightGridBuffer(fpConst.lightGridBufferAddress);
    LightIndexBuffer lightIndices = LightIndexBuffer(fpConst.lightIndexBufferAddress);
    LightGridTile tile = lightGrid.tiles[tileIndex];
    // Visualize number of lights in the tile
    float lightCountNormalized = float(tile.lightCount) / float(fpConst.maxLightsPerTile);
    if (tile.lightCount == 0) {
        return vec4(0.0, 0.0, 0.0, 1.0);
    } else {
        // Gradient: Blue -> Green -> Red
        vec3 blue = vec3(0.0, 0.0, 1.0);
        vec3 green = vec3(0.0, 1.0, 0.0);
        vec3 red = vec3(1.0, 0.0, 0.0);
        
        vec3 color;
        if (lightCountNormalized < 0.5) {
            color = mix(blue, green, lightCountNormalized * 2.0);
        } else {
            color = mix(green, red, (lightCountNormalized - 0.5) * 2.0);
        }
        return vec4(color, 1.0);
    }
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
    material.ambient = props.ambient;
#ifndef EXCLUDE_MESH_PROPS
    if (props.albedoTexIndex > 0)
    {
        material.albedo = material.albedo * texture(sampler2D(kTextures2D[props.albedoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).rgb;
    }
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
#else
    {
        material.normal = normalize(cross(dFdy(fragWorldPos), dFdx(fragWorldPos)));
    }
#endif
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);
    return material;
/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
}

vec4 nonLitOutputColor(in PBRMaterial material)
{
    return vec4(material.albedo + material.emissive, material.opacity);
}


// Template function to create final color
vec4 outputColor()
{
    if (MATERIAL_TYPE == 1u) {
        PBRMaterial material = createPBRMaterial();
        return forwardPlusLighting(material);
    }
    if (MATERIAL_TYPE == 2u) {
        PBRMaterial material = createPBRMaterial();
        return nonLitOutputColor(material);
    }
    if (MATERIAL_TYPE == 3u) {
        // Default to PBR lighting
        return debugTileLighting();
    }
    if (MATERIAL_TYPE == 4u) {
        // Unlit with vertex color
        return vec4(fragNormal, 1.0);
    }
    {
        return vec4(1.0, 0.0, 1.0, 1.0); // Magenta for unsupported shading model
    }
}

/*TEMPLATE_CUSTOM_MAIN_START*/
void main() {
    outColor = outputColor();
}
/*TEMPLATE_CUSTOM_MAIN_END*/
