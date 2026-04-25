#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/PBRFunctions.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#include "HxHeaders/MeshDraw.glsl"
#include "HxHeaders/PBRProperties.glsl"

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
#ifdef TRANSPARENT_PASS
layout(location = 0) out vec4 outAccum;
layout(location = 1) out float outRevealage;
#else
layout(location = 0) out vec4 outColor;
#endif

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

// TEMPLATE_CUSTOM_STRUCTS

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

// Returns the GPU address of the custom material buffer for this material type.
// Returns 0 if no custom buffer is bound.
uint64_t getCustomMaterialBufferAddress() {
    return pc.value.customMaterialBufferAddress;
}

/*UTILITY_FUNCTIONS_BEGIN*/
PBRProperties getPBRMaterial()
{
    MaterialBuffer materialBuf = MaterialBuffer(fpConst.materialBufferAddress);
    return materialBuf.materials[materialId];
}

uint64_t getTimeMs() {
    return fpConst.timeMs;
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

bool isPointerRingEnabled() {
    return fpConst.pointerRing.enabled != 0;
}

vec3 getPointerRayDirection() {
    return fpConst.pointerRing.rayDirection;
}

vec3 getPointerRayOrigin() {
    return fpConst.pointerRing.rayOrigin;
}

float getPointerRingOuterDistThreshold() {
    return fpConst.pointerRing.outerDistThreshold;
}

float getPointerRingInnerDistThreshold() {
    return fpConst.pointerRing.innerDistThreshold;
}

float getPointerRingColorMix() {
    return fpConst.pointerRing.colorMix;
}

vec3 getPointerRingColor() {
    return fpConst.pointerRing.color;
}

float getFragToPointerRayDistance() {
    vec3 rayOrigin = getPointerRayOrigin();
    vec3 rayDir = normalize(getPointerRayDirection());
    vec3 toFrag = fragWorldPos - rayOrigin;
    float t = dot(toFrag, rayDir);
    vec3 closestPoint = rayOrigin + rayDir * max(t, 0.0);
    return length(fragWorldPos - closestPoint);
}

bool isInPointerRing() {
    float dist = getFragToPointerRayDistance();
    return dist >= getPointerRingInnerDistThreshold() && dist <= getPointerRingOuterDistThreshold();
}


vec4 mixWithPointerRing(in vec4 color) {
    if (isPointerRingEnabled() && isInPointerRing()) {
        vec3 ringColor = getPointerRingColor();
#ifndef EXCLUDE_MESH_PROPS
        // Modulate the ring brightness based on the surface normal vs. view direction,
        // so the ring shades naturally across uneven geometry.
        vec3 N = normalize(fragNormal);
        vec3 V = normalize(getCameraPosition() - fragWorldPos);
        float NdotV = max(dot(N, V), 0.0);
        ringColor = ringColor * mix(0.2, 1.0, NdotV);
#endif
        color.rgb = mix(color.rgb, ringColor, getPointerRingColorMix());
    }
    return color;
}
/*UTILITY_FUNCTIONS_END*/

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
    material.clearCoatStrength = props.clearCoatStrength;
    material.clearCoatRoughness = props.clearCoatRoughness;
    material.reflectance = max(props.reflectance, 0.0);
#ifndef EXCLUDE_MESH_PROPS
    if (props.albedoTexIndex > 0)
    {
        material.albedo = material.albedo * texture(sampler2D(kTextures2D[props.albedoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).rgb;
    }
    if (props.metallicRoughnessTexIndex > 0)
    {
        vec2 omr = texture(sampler2D(kTextures2D[props.metallicRoughnessTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).gb;
        // glTF channel packing: G = Roughness, B = Metallic
        material.roughness = omr.x;
        material.metallic  = omr.y;
    }
    if (props.aoTexIndex > 0)
    {
        // glTF channel packing: R = Ambient Occlusion
        material.ao = material.ao * texture(sampler2D(kTextures2D[props.aoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).r;
    }
    vec3 baseNormal = normalize(fragNormal);
    vec3 finalNormal = baseNormal;

    if (props.normalTexIndex > 0) {
        vec3 normalMap = texture(sampler2D(kTextures2D[props.normalTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).xyz * 2.0 - 1.0;
        vec3 N = normalize(fragNormal);
        vec3 T = normalize(fragTangent - dot(fragTangent, N) * N);
        vec3 B = cross(N, T);
        mat3 TBN = mat3(T, B, N);
        finalNormal = normalize(TBN * normalMap);
    }
    if (props.bumpTexIndex > 0 )
    {
        float h = texture(sampler2D(kTextures2D[props.bumpTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).r;
        // 1. Get screen-space derivatives of world position and height
        vec3 dpdx = dFdx(fragWorldPos);
        vec3 dpdy = dFdy(fragWorldPos);
        float dhdx = dFdx(h);
        float dhdy = dFdy(h);

        // 2. Construct local geometry basis (Right-Handed)
        // r1 and r2 represent the 'tilt' directions on the plane of the triangle
        vec3 r1 = cross(dpdy, baseNormal);
        vec3 r2 = cross(baseNormal, dpdx);

        // 3. Calculate the surface gradient 
        // We divide by the determinant (dot(dpdx, r1)) to keep the scale consistent 
        // regardless of distance or perspective distortion.
        float det = dot(dpdx, r1);
        // Prevent division by zero while preserving the sign of det to avoid
        // flipping the gradient direction on degenerate/back-facing geometry.
        float epsilon = 0.000001;
        float safeDet = abs(det) < epsilon ? (det >= 0.0 ? epsilon : -epsilon) : det;
        vec3 grad = (r1 * dhdx + r2 * dhdy) / safeDet;

        // 4. Combine the results
        // We perturb the current normal (which might already have normal mapping) 
        // by the bump gradient calculated from the vertex normal basis.
        finalNormal = normalize(finalNormal - props.bumpScale * grad);
    }
    material.normal = finalNormal;
#else
    {
        material.normal = normalize(cross(dFdy(fragWorldPos), dFdx(fragWorldPos)));
    }
#endif
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);
    return material;
/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
}

// Template function to create final PBR material properties with flat normal
PBRMaterial createPBRMaterialFlatNormal()
{
    PBRProperties props = getPBRMaterial();
    PBRMaterial material;
    material.albedo = props.albedo;
    material.roughness = props.roughness;
    material.metallic = props.metallic;
    material.ao = props.ao;
    material.emissive = props.emissive;
    material.opacity = props.opacity;
    material.ambient = props.ambient;
    material.clearCoatStrength = props.clearCoatStrength;
    material.clearCoatRoughness = props.clearCoatRoughness;
    material.reflectance = max(props.reflectance, 0.0);
#ifndef EXCLUDE_MESH_PROPS
    if (props.albedoTexIndex > 0)
    {
        material.albedo = material.albedo * texture(sampler2D(kTextures2D[props.albedoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).rgb;
    }
    if (props.metallicRoughnessTexIndex > 0)
    {
        vec2 omr = texture(sampler2D(kTextures2D[props.metallicRoughnessTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).gb;
        // glTF channel packing: G = Roughness, B = Metallic
        material.roughness = omr.x;
        material.metallic  = omr.y;
    }
    if (props.aoTexIndex > 0)
    {
        // glTF channel packing: R = Ambient Occlusion
        material.ao = material.ao * texture(sampler2D(kTextures2D[props.aoTexIndex], kSamplers[props.samplerIndex]), fragTexCoord).r;
    }
#endif
    material.normal = normalize(cross(dFdy(fragWorldPos), dFdx(fragWorldPos)));
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);
    return material;
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
        vec4 color = forwardPlusLighting(material);
        color.rgb += material.emissive; // Add emissive after lighting
        return color;
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
    if (MATERIAL_TYPE == 5u) {
        PBRMaterial material = createPBRMaterialFlatNormal();
        vec4 color = forwardPlusLighting(material);
        color.rgb += material.emissive; // Add emissive after lighting
        return color;
    }
    {
        return vec4(1.0, 0.0, 1.0, 1.0); // Magenta for unsupported shading model
    }
}


/*TEMPLATE_CUSTOM_MAIN_START*/
void main() {
    vec4 color = outputColor();
#ifdef TRANSPARENT_PASS
    // Weighted Blended OIT (McGuire & Bavoil 2013)
    // Weight function uses alpha and view-space depth for depth-sensitive ordering.
    float alpha = color.a;
    if (alpha < 1e-4) {
        discard; // Skip fully transparent fragments to avoid polluting the buffers.
    }
    // gl_FragCoord.z is in [0, 1] (reversed-Z: 1 = near, 0 = far).
    // Convert to a linear-ish metric for the weight function.
    float z = gl_FragCoord.z;
    float w = clamp(
        pow(min(1.0, alpha * 10.0) + 0.01, 3.0) * 1e8
        * pow(1e-5 + abs(1.0 - z) / 200.0, -3.0),
        1e-2, 3e3
    );
    // RT0 (accum): additive blend (ONE / ONE). Store premultiplied weighted color and weighted alpha.
    outAccum = vec4(color.rgb * alpha * w, alpha * w);
    // RT1 (revealage): blend is ZERO / ONE_MINUS_SRC_COLOR, buffer cleared to 1.
    // Output alpha so the blend hardware computes: dst = dst * (1 - alpha).
    outRevealage = alpha;
#else
    outColor = color;
#endif
}
/*TEMPLATE_CUSTOM_MAIN_END*/
