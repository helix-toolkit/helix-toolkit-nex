#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/LightStruct.glsl"
#include "HxHeaders/PBRFunctions.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"
#include "HxHeaders/ForwardPlusTile.glsl"
#include "HxHeaders/MeshDraw.glsl"
#include "HxHeaders/PBRProperties.glsl"
#include "HxHeaders/HeaderPackEntity.glsl"

layout(location = 0) in vec3 fragWorldPos;
layout(location = 1) in flat uint materialId;
layout(location = 2) in vec4 fragColor;
#ifdef WIREFRAME_PASS
#else
#ifndef EXCLUDE_MESH_PROPS
layout(location = 3) in vec3 fragNormal;
layout(location = 4) in vec4 fragTangent;
layout(location = 5) in vec2 fragTexCoord;
#endif
#endif

// Forces the GPU to check if a pixel is visible *before* running this expensive lighting shader
layout(early_fragment_tests) in; 

#ifdef OUTPUT_DRAW_ID
layout(location = 6) in flat uvec2 fragEntityId;
#endif
// Ensure FragCoord (0,0) is Top-Left to match Compute Shader tile generation
// layout(origin_upper_left) in vec4 gl_FragCoord;

layout(location = 0) out vec4 outColor;

#ifdef OUTPUT_DRAW_ID
layout(location = 1) out vec2 idOut;
#endif

#ifdef TRANSPARENT_PASS
layout(location = 2) out vec4 outAccum;
layout(location = 3) out float outRevealage;
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
PBRProperties getPBRProperties()
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

mat4 getView() {
    return fpConst.view;
}

mat4 getInvView() {
    return fpConst.inverseView;
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

    // Transmission parameters — only relevant for the transparent pass.
#ifdef TRANSPARENT_PASS
    PBRProperties props = getPBRProperties();
    float transmissionDistortion = props.transmissionDistortion;
    float transmissionPower      = props.transmissionPower;
    float transmissionScale      = props.transmissionScale;
    // Only compute transmission for non-opaque, non-metallic fragments that have
    // a non-zero transmissionScale (set from glTF transmissionFactor).
    bool hasTransmission = transmissionScale > 0.0 && material.metallic < 0.5;
    // Per KHR_materials_transmission: transmissionFactor replaces the diffuse lobe.
    // Scale ambient (which is a diffuse approximation) by (1 - transmissionScale).
    if (hasTransmission) {
        finalC *= (1.0 - transmissionScale);
    }
    // Beer-Lambert volumetric absorption (KHR_materials_volume):
    // T(x) = attenuationColor ^ (thickness / attenuationDistance)
    // The ratio is clamped to prevent complete saturation in thick regions.
    // attenuationDistance <= 0 means no absorption (transparent glass with no tint).
    vec3 sssColor = material.albedo;
    if (hasTransmission && props.attenuationDistance > 0.0) {
        vec3 ac = clamp(props.attenuationColor, vec3(1e-6), vec3(1.0));
        // Clamp ratio to keep visible color variation even for thick volumes.
        // A ratio of 1.0 means the light has traveled exactly one attenuationDistance,
        // producing attenuationColor as the tint. Values above 1 darken further.
        float MAX_ABSORPTION_RATIO = 2.0;
        float ratio = min(material.thickness / props.attenuationDistance, MAX_ABSORPTION_RATIO);
        sssColor = sssColor * pow(ac, vec3(ratio));
    }
#endif
    if (fpConst.lightCount > 0 && fpConst.lightBufferAddress != 0) {
        LightBuffer lightBuf = LightBuffer(fpConst.lightBufferAddress);
        if (fpConst.enabled == 0) {
            for (uint i = 0; i < fpConst.lightCount; ++i) {
                Light light = lightBuf.lights[i];
#ifdef TRANSPARENT_PASS
                finalC += hasTransmission
                    ? calculatePBRLightingTransmissive(material, light, fragWorldPos, viewDir, transmissionScale)
                    : calculatePBRLighting(material, light, fragWorldPos, viewDir);
                if (hasTransmission)
                    finalC += calculateTransmission(material, light, fragWorldPos, viewDir, sssColor, transmissionDistortion, transmissionPower, transmissionScale);
#else
                finalC += calculatePBRLighting(material, light, fragWorldPos, viewDir);
#endif
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
#ifdef TRANSPARENT_PASS
                finalC += hasTransmission
                    ? calculatePBRLightingTransmissive(material, light, fragWorldPos, viewDir, transmissionScale)
                    : calculatePBRLighting(material, light, fragWorldPos, viewDir);
                if (hasTransmission)
                    finalC += calculateTransmission(material, light, fragWorldPos, viewDir, sssColor, transmissionDistortion, transmissionPower, transmissionScale);
#else
                finalC += calculatePBRLighting(material, light, fragWorldPos, viewDir);
#endif
            }
        }
    }

    if (fpConst.directionalLightsBufferAddress != 0) {
        DirectionalLightBuffer dirLightBuf = DirectionalLightBuffer(fpConst.directionalLightsBufferAddress);
        for (uint i = 0; i < dirLightBuf.value.lightCount; ++i) {
            Light dirLight = DirectionLightToLight(dirLightBuf.value.lights[i]);
#ifdef TRANSPARENT_PASS
            finalC += hasTransmission
                ? calculatePBRLightingTransmissive(material, dirLight, fragWorldPos, viewDir, transmissionScale)
                : calculatePBRLighting(material, dirLight, fragWorldPos, viewDir);
            if (hasTransmission)
                finalC += calculateTransmission(material, dirLight, fragWorldPos, viewDir, sssColor, transmissionDistortion, transmissionPower, transmissionScale);
#else
            finalC += calculatePBRLighting(material, dirLight, fragWorldPos, viewDir);
#endif
        }
    }

#ifdef TRANSPARENT_PASS
    // For transmissive materials in WBOIT, reduce opacity so the composite allows
    // the opaque scene behind to show through. The transmission color contribution
    // is already in finalC via calculateTransmission.
    float finalOpacity = material.opacity;
    if (hasTransmission) {
        // Thickness modulates how see-through the surface is:
        // thin (thickness≈0) → more transparent, thick → more opaque.
        float thicknessFrac = 1.0 / (1.0 + material.thickness);
        finalOpacity = material.opacity * (1.0 - transmissionScale * thicknessFrac);
    }
    return vec4(finalC, finalOpacity);
#else
    return vec4(finalC, material.opacity);
#endif
}

// ============================================================================
// CAD-Style Shading
// ============================================================================
// Provides a clean, technical visualization style commonly used in CAD/CAM software.
// Features:
// - Simplified Blinn-Phong lighting model
// - Two-sided lighting (normal already flipped for back faces)
// - Camera-aligned head light for consistent illumination
// - Optional rim/silhouette enhancement for better shape perception
// - Flat or smooth shading support

vec4 cadStyleLighting(in PBRMaterial material)
{
    vec3 N = normalize(material.normal);
    vec3 V = normalize(fpConst.cameraPosition - fragWorldPos);

    // CAD lighting parameters (can be made configurable via uniforms)
    float ambientStrength = 0.3;
    float diffuseStrength = 0.6;
    float specularStrength = 0.4;
    float specularShininess = 32.0;
    float rimStrength = 0.15;       // Silhouette/rim light intensity
    float rimPower = 3.0;           // Rim light falloff

    // -------------------------------------------------------------------------
    // Head Light (camera-aligned directional light)
    // -------------------------------------------------------------------------
    // This ensures the model is always lit from the viewer's perspective,
    // which is standard in CAD applications for consistent visualization.
    vec3 headLightDir = V; // Light comes from camera direction

    // Lambertian diffuse
    float NdotL = max(dot(N, headLightDir), 0.0);
    vec3 diffuse = material.albedo * NdotL * diffuseStrength;

    // Blinn-Phong specular
    vec3 H = normalize(V + headLightDir);
    float NdotH = max(dot(N, H), 0.0);
    float spec = pow(NdotH, specularShininess);
    vec3 specular = vec3(spec) * specularStrength;

    // -------------------------------------------------------------------------
    // Secondary Fill Light (from opposite direction, softer)
    // -------------------------------------------------------------------------
    // Provides subtle fill to prevent completely dark areas
    vec3 fillLightDir = normalize(-V + vec3(0.3, 0.5, 0.0)); // Offset from behind
    float fillNdotL = max(dot(N, fillLightDir), 0.0);
    vec3 fillDiffuse = material.albedo * fillNdotL * 0.15;

    // -------------------------------------------------------------------------
    // Rim/Silhouette Enhancement
    // -------------------------------------------------------------------------
    // Highlights edges and silhouettes for better shape perception
    float NdotV = max(dot(N, V), 0.0);
    float rim = pow(1.0 - NdotV, rimPower) * rimStrength;
    vec3 rimColor = material.albedo * rim;

    // -------------------------------------------------------------------------
    // Ambient
    // -------------------------------------------------------------------------
    vec3 ambient = material.albedo * material.ambient * ambientStrength * material.ao;

    // -------------------------------------------------------------------------
    // Combine all lighting components
    // -------------------------------------------------------------------------
    vec3 finalColor = ambient + diffuse + fillDiffuse + specular + rimColor + material.emissive;

    return vec4(finalColor, material.opacity);
}

// CAD-style lighting with advanced shape definition and energy conservation
vec4 cadStyleLightingAdvanced(in PBRMaterial material, 
                              float ambientStrength,
                              float diffuseStrength,
                              float specularStrength,
                              float specularShininess,
                              float rimStrength,
                              float rimPower,
                              vec3 headLightColor,
                              vec3 fillLightColor)
{
    vec3 N = normalize(material.normal);
    vec3 V = normalize(fpConst.cameraPosition - fragWorldPos);
    float NdotV = max(dot(N, V), 0.0);

    // -------------------------------------------------------------------------
    // 1. Better Light Rigging: Over-the-shoulder Key Light & Fill Light
    // -------------------------------------------------------------------------
    // Offsetting the headlight slightly from V prevents flat specular blowouts
    vec3 keyLightDir = normalize(V + vec3(-0.2, 0.2, 0.1)); 
    vec3 fillLightDir = normalize(-V + vec3(0.5, 0.6, -0.2));

    // -------------------------------------------------------------------------
    // 2. Gooch-style Diffuse Wrap (Warm / Cool Shading)
    // -------------------------------------------------------------------------
    float keyHalfDot = dot(N, keyLightDir) * 0.5 + 0.5; // Map [-1, 1] to [0, 1]
    vec3 coolTone = vec3(0.15, 0.2, 0.3) * fillLightColor;
    vec3 warmTone = headLightColor * diffuseStrength;
    
    // Blend the base albedo through the Gooch ramp
    vec3 diffuseTerm = mix(coolTone, warmTone, keyHalfDot) * material.albedo;

    // Secondary soft fill
    float fillNdotL = max(dot(N, fillLightDir), 0.0);
    vec3 fillDiffuse = material.albedo * fillLightColor * fillNdotL * 0.25;

    // -------------------------------------------------------------------------
    // 3. Energy Conserving Blinn-Phong Specular
    // -------------------------------------------------------------------------
    vec3 H = normalize(V + keyLightDir);
    float NdotH = max(dot(N, H), 0.0);
    
    // Normalization factor for Blinn-Phong energy conservation
    float normFactor = (specularShininess + 2.0) / 8.0;
    float spec = pow(NdotH, specularShininess) * normFactor;
    
    // Simple Fresnel approximation for a crisp metallic edge look if specified
    float F0 = mix(0.04, 0.6, material.metallic); 
    float fresnel = F0 + (1.0 - F0) * pow(1.0 - NdotV, 5.0);
    
    vec3 specularTerm = headLightColor * spec * specularStrength * fresnel;

    // -------------------------------------------------------------------------
    // 4. Rim Enhancement (Darken or Lighten edges depending on CAD mode)
    // -------------------------------------------------------------------------
    // Tip: In CAD, a *dark* rim can act as a cheap silhouette pencil line 
    // if you don't have a post-processing pass. Let's make it an edge highlight here.
    float rim = pow(1.0 - NdotV, rimPower) * rimStrength;
    vec3 rimColor = mix(vec3(1.0), material.albedo, 0.5) * rim * (1.0 - material.metallic);

    // -------------------------------------------------------------------------
    // 5. Ambient & Emissive
    // -------------------------------------------------------------------------
    // Metallic surfaces drop their diffuse component
    vec3 finalDiffuse = (diffuseTerm + fillDiffuse) * (1.0 - material.metallic);
    vec3 ambient = material.albedo * material.ambient * ambientStrength * material.ao;

    // Combine with safe clamping/energy distribution
    vec3 finalColor = ambient + finalDiffuse + specularTerm + rimColor + material.emissive;

    return vec4(finalColor, material.opacity);
}

// Flat shaded CAD-style lighting (uses geometric normal instead of interpolated normal)
vec4 cadStyleLightingFlat(in PBRMaterial material){
    PBRMaterial flatMaterial = material;
    
    // More robust screen-space geometric normal extraction
    vec3 dX = dFdx(fragWorldPos);
    vec3 dY = dFdy(fragWorldPos);
    vec3 geomNormal = normalize(cross(dX, dY));
    
    // Ensure the generated face normal points toward the viewer
    vec3 V = normalize(fpConst.cameraPosition - fragWorldPos);
    if (dot(geomNormal, V) < 0.0) {
        geomNormal = -geomNormal;
    }
    
    flatMaterial.normal = geomNormal;
    return cadStyleLightingAdvanced(flatMaterial,
                                    0.15,               // ambientStrength
                                    0.80,               // diffuseStrength
                                    0.40,               // specularStrength
                                    32.0,               // specularShininess
                                    0.30,               // rimStrength
                                    4.0,                // rimPower
                                    vec3(1.0),          // headLightColor (White)
                                    vec3(0.8,0.85,0.95) // fillLightColor (Soft Ice Blue)
                                    );
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
    PBRProperties props = getPBRProperties();
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
        vec4 albedo = textureBindless2D(props.albedoTexIndex, props.samplerIndex, fragTexCoord);
        // Per glTF spec: final alpha = baseColorFactor.a * baseColorTexture.a
        // Apply texture alpha to material opacity for correct alpha masking/blending
        material.opacity = material.opacity * albedo.a;
#ifdef ALPHA_MASK
        if (material.opacity < props.alphaCutoff)
        {
            discard;
        }
#endif
        material.albedo = material.albedo * albedo.rgb;
    }
    if (props.metallicRoughnessTexIndex > 0)
    {
        vec2 omr = textureBindless2D(props.metallicRoughnessTexIndex, props.samplerIndex, fragTexCoord).gb;
        // glTF channel packing: G = Roughness, B = Metallic
        material.roughness = omr.x;
        material.metallic  = omr.y;
    }
    if (props.aoTexIndex > 0)
    {
        // glTF channel packing: R = Ambient Occlusion
        material.ao = material.ao * textureBindless2D(props.aoTexIndex, props.samplerIndex, fragTexCoord).r;
    }
    if (props.emissiveTexIndex > 0)
    {
        material.emissive = material.emissive * textureBindless2D(props.emissiveTexIndex, props.samplerIndex, fragTexCoord).rgb;
    }
    // Thickness: G channel * thicknessFactor (glTF spec: stored in G, multiplied with factor).
    // 0 = thin-walled (max transmission), >0 = volumetric. Kept in mesh/world-space units.
    material.thickness = (props.thicknessTexIndex > 0)
        ? textureBindless2D(props.thicknessTexIndex, props.samplerIndex, fragTexCoord).g * props.thicknessFactor
        : props.thicknessFactor;
    vec3 baseNormal = normalize(fragNormal);
    vec3 finalNormal = baseNormal;

    if (props.normalTexIndex > 0) {
        vec3 normalMap = textureBindless2D(props.normalTexIndex, props.samplerIndex, fragTexCoord).xyz * 2.0 - 1.0;
        vec3 N = normalize(fragNormal);
        vec3 T = normalize(fragTangent.xyz - dot(fragTangent.xyz, N) * N);
        vec3 B = cross(N, T) * fragTangent.w;
        mat3 TBN = mat3(T, B, N);
        finalNormal = normalize(TBN * normalMap);
    }
    if (props.bumpTexIndex > 0 )
    {
        float h = textureBindless2D(props.bumpTexIndex, props.samplerIndex, fragTexCoord).r;
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
    // Flip normal for back faces to support double-sided rendering
    if (!gl_FrontFacing) {
        finalNormal = -finalNormal;
    }
    material.normal = finalNormal;
#else
    {
        vec3 geomNormal = normalize(cross(dFdy(fragWorldPos), dFdx(fragWorldPos)));
        // Flip normal for back faces to support double-sided rendering
        material.normal = gl_FrontFacing ? geomNormal : -geomNormal;
    }
#endif
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);
    return material;
/*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
}

// Template function to create final PBR material properties with flat normal
PBRMaterial createPBRMaterialFlatNormal()
{
    PBRProperties props = getPBRProperties();
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
        material.albedo = material.albedo * textureBindless2D(props.albedoTexIndex, props.samplerIndex, fragTexCoord).rgb;
    }
    if (props.metallicRoughnessTexIndex > 0)
    {
        vec2 omr = textureBindless2D(props.metallicRoughnessTexIndex, props.samplerIndex, fragTexCoord).gb;
        // glTF channel packing: G = Roughness, B = Metallic
        material.roughness = omr.x;
        material.metallic  = omr.y;
    }
    if (props.aoTexIndex > 0)
    {
        // glTF channel packing: R = Ambient Occlusion
        material.ao = material.ao * textureBindless2D(props.aoTexIndex, props.samplerIndex, fragTexCoord).r;
    }
    material.thickness = (props.thicknessTexIndex > 0)
        ? textureBindless2D(props.thicknessTexIndex, props.samplerIndex, fragTexCoord).g * props.thicknessFactor
        : props.thicknessFactor;
#endif
    // Geometric normal from screen-space derivatives always faces the camera
    vec3 geomNormal = normalize(cross(dFdy(fragWorldPos), dFdx(fragWorldPos)));
    // Flip normal for back faces to support double-sided rendering
    material.normal = gl_FrontFacing ? geomNormal : -geomNormal;
    material.albedo = mix(material.albedo, fragColor.rgb, props.vertexColorMix);
    return material;
}

vec4 nonLitOutputColor(in PBRMaterial material)
{
    return vec4(material.albedo + material.emissive, material.opacity);
}

#ifdef WIREFRAME_PASS
vec4 wireframeColor() {
    return fpConst.wireframeColor;
}
#endif


// Template function to create final color
vec4 outputColor()
{
    if (MATERIAL_TYPE == 1u) {
        PBRMaterial material = createPBRMaterial();
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
#ifdef OUTPUT_DRAW_ID
    uint primID = uint(gl_PrimitiveID);
    idOut = packPrimitiveId(fragEntityId, primID);
#endif
#ifdef WIREFRAME_PASS
    vec4 color = wireframeColor();
#else
    vec4 color = outputColor();
#endif
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
