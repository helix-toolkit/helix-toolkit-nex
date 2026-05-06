#include "HxHeaders/HeaderFrag.glsl"

// Fragment shader with tone mapping

layout(location = 0) in vec2 inTexCoord;
layout(location = 0) out vec4 outColor;

@code_gen
struct ToneGammaPushConstants {
    uint enabled;
    uint hdrTextureId;
    uint samplerId;
    float exposure;
    uint tonemapMode; // 0=ACES, 1=Reinhard, 2=Uncharted2
    uint gammaEnabled;
    uint _padding0;
    uint _padding1;
};

layout(push_constant) uniform PushConstants {
    ToneGammaPushConstants value;
} pc;

// ACES Filmic Tone Mapping
vec3 ACESFilm(in vec3 x) {
    const float a = 2.51f;
    const float b = 0.03f;
    const float c = 2.43f;
    const float d = 0.59f;
    const float e = 0.14f;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

// Uncharted 2 Tone Mapping
vec3 Uncharted2ToneMap(in vec3 x) {
    const float A = 0.15;
    const float B = 0.50;
    const float C = 0.10;
    const float D = 0.20;
    const float E = 0.02;
    const float F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

vec3 Reinhard(in vec3 x) {
    float luminance = dot(x, vec3(0.2126, 0.7152, 0.0722));
    if (luminance <= 0.000001) {
        return x; // Avoid division by zero, return original color if black
    }
    float mappedLuminance = luminance / (1.0 + luminance);
    return x * (mappedLuminance / luminance);
}

void main() {
    // Sample HDR texture
    vec4 hdrColor = textureBindless2D(pc.value.hdrTextureId, pc.value.samplerId, inTexCoord);
    if (pc.value.enabled == 0u) {
        // Tone mapping disabled, output original color.
        outColor = hdrColor;
        return;
    }
    // Apply exposure
    hdrColor *= pc.value.exposure;
    
    // Tone mapping
    vec3 mapped = hdrColor.rgb;
    switch(pc.value.tonemapMode) {
        case 0u:
            mapped = ACESFilm(hdrColor.rgb);
            break;
        case 1u:
            mapped = Reinhard(hdrColor.rgb);
            break;
        case 2u:
            const float exposureBias = 2.0;
            vec3 curr = Uncharted2ToneMap(hdrColor.rgb * exposureBias);
            vec3 whiteScale = 1.0 / Uncharted2ToneMap(vec3(11.2));
            mapped = curr * whiteScale;
            break;
    }
    if (pc.value.gammaEnabled != 0u) {
        // Gamma correction (linear to sRGB)
        mapped = pow(mapped, vec3(1.0 / 2.2));
    }
    outColor = vec4(mapped, hdrColor.a);
}
