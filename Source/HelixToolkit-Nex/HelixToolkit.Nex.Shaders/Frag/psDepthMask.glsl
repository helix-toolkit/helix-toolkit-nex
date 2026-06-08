#version 450
#include "HxHeaders/HeaderPackEntity.glsl"
#include "HxHeaders/HeaderFrag.glsl"

layout(location = 1) in flat uint materialId;
layout(location = 6) in flat uvec2 fragEntityId;

layout(location = 0) out vec2 idOut;

#ifdef ALPHA_MASK
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

// TEMPLATE_CUSTOM_STRUCTS

FPConstants fpConst = FPBuffer(pc.value.fpConstAddress).fpConstants;

/*UTILITY_FUNCTIONS_BEGIN*/
PBRProperties getPBRProperties()
{
    MaterialBuffer materialBuf = MaterialBuffer(fpConst.materialBufferAddress);
    return materialBuf.materials[materialId];
}

PBRProperties props = getAlbedoTexture();
#endif

void main()
{
#ifdef ALPHA_MASK
    if (props.albedoTexIndex > 0)
    {
        float alpha = textureBindless2D(props.albedoTexIndex, props.samplerIndex, fragTexCoord).a;
        if (alpha < props.alphaCutoff)
        {
            discard;
        }
    }
#endif

    uint primID = uint(gl_PrimitiveID);
    idOut = packPrimitiveId(fragEntityId, primID);
}
