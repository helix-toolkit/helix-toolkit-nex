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
    uint gamma;
    vec2 _padding;
};

layout(push_constant) uniform PushConstants {
    ToneGammaPushConstants value;
} pc;

// ACES Filmic Tone Mapping
vec3 ACESFilm(vec3 x) {
    const float a = 2.51f;
    const float b = 0.03f;
    const float c = 2.43f;
    const float d = 0.59f;
    const float e = 0.14f;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

// Uncharted 2 Tone Mapping
vec3 Uncharted2ToneMap(vec3 x) {
    const float A = 0.15;
    const float B = 0.50;
    const float C = 0.10;
    const float D = 0.20;
    const float E = 0.02;
    const float F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

void main() {
    // Sample HDR texture
    vec4 hdrColor = textureBindless2D(pc.value.hdrTextureId, pc.value.samplerId, inTexCoord);
    if (pc.value.enabled == 0u) {
        // Tone mapping disabled, output white
        outColor = hdrColor;
        return;
    }
    // Apply exposure
    hdrColor *= pc.value.exposure;
    
    // Tone mapping
    vec3 mapped;
    if (pc.value.tonemapMode == 0u) {
        // ACES (best for most cases)
        mapped = ACESFilm(hdrColor.rgb);
    } else if (pc.value.tonemapMode == 1u) {
        // Reinhard
        mapped = hdrColor.rgb / (hdrColor.rgb + vec3(1.0));
    } else {
        // Uncharted 2
        const float exposureBias = 2.0;
        vec3 curr = Uncharted2ToneMap(hdrColor.rgb * exposureBias);
        vec3 whiteScale = 1.0 / Uncharted2ToneMap(vec3(11.2));
        mapped = curr * whiteScale;
    }
    if (pc.value.gamma != 0u) {
        // Gamma correction (linear to sRGB)
        mapped = pow(mapped, vec3(1.0 / 2.2));
    }
    outColor = vec4(mapped, 1.0);
}
