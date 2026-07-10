#include "HxHeaders/HeaderFrag.glsl"
#include "HxHeaders/ForwardPlusConstants.glsl"   // FPConstants (camera matrices)

// SSAO (Screen-Space Ambient Occlusion) post-processing shader.
//
// A single fragment shader drives up to four full-screen passes selected via the
// SSAO_STAGE specialization constant (occlusion → separable blur → composite),
// mirroring the multi-stage structure of psSmaa.glsl. Surface normals are
// reconstructed from the depth buffer, so no additional G-buffer is required.
//
// The push-constant struct below is code-generated into a matching C# struct
// (SsaoPushConstants) that SsaoPostEffect uploads each frame. The stage
// implementation (specialization constants, main(), and the SSAO math) follows
// it and mirrors the pure CPU reference in SsaoMath.cs.
//
// Paired vertex shader: Vert/vsFullScreenQuad.glsl (full-screen triangle).

@code_gen
struct SsaoPushConstants {
    uint depthTextureId;         // OCCLUSION: TextureDepthF32
    uint depthSamplerId;         // point/nearest clamp
    uint aoTextureId;            // BLUR/COMPOSITE input (AO or AO_Blur)
    uint aoSamplerId;            // point (full-res) or linear (half-res upsample)
    uint sceneTextureId;         // COMPOSITE: ping-pong read slot
    uint sceneSamplerId;         // scene colour sampler
    uint64_t fpConstAddress;     // BufferForwardPlusConstants device address
    vec2 texelSize;              // 1/width, 1/height of the target being written
    vec2 invAoSize;              // 1/AO width, 1/AO height (for half-res sampling)
    float radius;                // occlusion sample radius (view-space world units)
    float intensity;             // darkening strength multiplier
    float bias;                  // self-occlusion suppression offset
    float power;                 // occlusion falloff contrast exponent
    uint sampleCount;            // dynamic hemisphere sample-loop bound
    float blurDepthThreshold;    // edge-aware blur depth-difference cutoff
};

layout(push_constant) uniform PushConstants {
    SsaoPushConstants value;
} pc;

// --------------------------------------------------------------------------
// I/O
// --------------------------------------------------------------------------
layout(location = 0) in  vec2 inTexCoord;   // full-screen-triangle UV, y=0 at top
layout(location = 0) out vec4 outColor;

// --------------------------------------------------------------------------
// Specialization constants (mirror the psSmaa.glsl multi-stage pattern)
// --------------------------------------------------------------------------
// Which of the three passes this pipeline variant runs.
#define SSAO_OCCLUSION 0
#define SSAO_BLUR      1
#define SSAO_COMPOSITE 2
layout(constant_id = 0) const uint SSAO_STAGE = 0;

// Separable-blur axis (only meaningful when SSAO_STAGE == SSAO_BLUR).
#define SSAO_BLUR_H 0u
#define SSAO_BLUR_V 1u
layout(constant_id = 1) const uint SSAO_BLUR_AXIS = 0;

// Composite debug mode (only meaningful when SSAO_STAGE == SSAO_COMPOSITE).
#define SSAO_DEBUG_NORMAL 0u
#define SSAO_DEBUG_RAWAO  1u
layout(constant_id = 2) const uint SSAO_DEBUG = 0;

// --------------------------------------------------------------------------
// Camera constants (read through the push-constant device address)
// --------------------------------------------------------------------------
layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer FPBuffer {
    FPConstants fpConstants;
};

// Reverse-Z far-plane depth value: geometry at the far plane reads exactly 0.0.
const float SSAO_FAR_PLANE = 0.0;
const float SSAO_PI        = 3.14159265358979323846;

// --------------------------------------------------------------------------
// Shared helpers
// --------------------------------------------------------------------------

// Sample the depth buffer with edge clamping (Req 2.6). The bound sampler is a
// point/nearest clamp sampler; the explicit clamp guards any UV that a projected
// hemisphere sample pushes slightly out of [0,1].
float sampleDepth(vec2 uv) {
    return textureBindless2DLod(
        pc.value.depthTextureId, pc.value.depthSamplerId, clamp(uv, vec2(0.0), vec2(1.0)), 0.0
    ).r;
}

// Reconstruct a view-space position from a UV + reverse-Z depth using the
// recovered inverse-projection matrix. UV (y=0 top) maps to Vulkan NDC directly:
// ndc = uv * 2 - 1 (the framebuffer's y-down NDC matches the y-down UV here).
vec3 reconstructViewPos(vec2 uv, float depth, mat4 invProj) {
    vec4 clip = vec4(uv * 2.0 - 1.0, depth, 1.0);
    vec4 view = invProj * clip;
    return view.xyz / view.w;
}

// Reconstruct a unit-length view-space normal from the four (left/right/down/up)
// adjacent depth samples (Req 2.4). For each axis the neighbour with the smaller
// depth discontinuity is chosen, so on curved surfaces and near silhouettes the
// derivative is not a one-sided forward difference that would snap into faceted,
// quantized orientations (the source of patchy AO on smooth spheres). The result
// is flipped to face the camera (view-space camera is at the origin, so a
// front-facing surface normal must oppose the surface's view-space position).
vec3 reconstructNormal(vec2 uv, vec3 centerViewPos, mat4 invProj, vec2 depthTexel) {
    vec2 uvL = clamp(uv - vec2(depthTexel.x, 0.0), vec2(0.0), vec2(1.0));
    vec2 uvR = clamp(uv + vec2(depthTexel.x, 0.0), vec2(0.0), vec2(1.0));
    vec2 uvD = clamp(uv - vec2(0.0, depthTexel.y), vec2(0.0), vec2(1.0));
    vec2 uvU = clamp(uv + vec2(0.0, depthTexel.y), vec2(0.0), vec2(1.0));

    vec3 pL = reconstructViewPos(uvL, sampleDepth(uvL), invProj);
    vec3 pR = reconstructViewPos(uvR, sampleDepth(uvR), invProj);
    vec3 pD = reconstructViewPos(uvD, sampleDepth(uvD), invProj);
    vec3 pU = reconstructViewPos(uvU, sampleDepth(uvU), invProj);

    // Best-neighbour derivative: pick the side whose depth is closest to centre.
    vec3 ddx = (abs(pR.z - centerViewPos.z) < abs(centerViewPos.z - pL.z))
        ? (pR - centerViewPos)
        : (centerViewPos - pL);
    vec3 ddy = (abs(pU.z - centerViewPos.z) < abs(centerViewPos.z - pD.z))
        ? (pU - centerViewPos)
        : (centerViewPos - pD);

    vec3 n = cross(ddx, ddy);
    float len2 = dot(n, n);
    if (len2 < 1e-12) {
        return vec3(0.0, 0.0, 1.0); // degenerate (collinear) samples
    }
    n *= inversesqrt(len2);
    if (dot(n, centerViewPos) > 0.0) {
        n = -n;
    }
    return n;
}

// Hammersley low-discrepancy 2D point (radical inverse of i over n).
vec2 hammersley(uint i, uint n) {
    uint bits = i;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    float rdi = float(bits) * 2.3283064365386963e-10; // / 2^32
    return vec2(float(i) / float(n), rdi);
}

// Cosine-weighted hemisphere direction in tangent space (z = up).
vec3 hemisphereSampleTS(vec2 xi) {
    float phi      = 2.0 * SSAO_PI * xi.x;
    float cosTheta = sqrt(1.0 - xi.y);
    float sinTheta = sqrt(xi.y);
    return vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
}

// --------------------------------------------------------------------------
// Stage 0 - Occlusion
// --------------------------------------------------------------------------
// Reconstructs view position/normal from depth, takes exactly `sampleCount`
// hemisphere samples oriented by the normal, and writes the raw occlusion factor
// (1.0 = fully lit, 0.0 = fully occluded) with the Power exponent applied.
void occlusionPass() {
    float centerDepth = sampleDepth(inTexCoord);

    // Far-plane pixels have no geometry: fully lit, skip normal reconstruction
    // (Req 2.5, 3.6).
    if (centerDepth == SSAO_FAR_PLANE) {
        outColor = vec4(1.0);
        return;
    }

    FPConstants fp = FPBuffer(pc.value.fpConstAddress).fpConstants;
    // Recover projection / inverse-projection (Req 2.2) - FPConstants exposes no
    // standalone projection matrix (same recovery as vsPoint.glsl).
    mat4 proj    = fp.viewProjection * fp.inverseView;
    mat4 invProj = fp.view * fp.inverseViewProjection;

    vec3 centerViewPos = reconstructViewPos(inTexCoord, centerDepth, invProj);

    vec2 depthTexel = 1.0 / vec2(textureBindlessSize2D(pc.value.depthTextureId));
    vec3 normal     = reconstructNormal(inTexCoord, centerViewPos, invProj, depthTexel);

    // Per-pixel rotated tangent basis using interleaved gradient noise (IGN) in
    // pixel space. IGN is high-frequency (decorrelated per pixel), so the separable
    // blur removes it cleanly. A UV-derived hash, by contrast, degenerates into
    // low-frequency structured patterns at high resolution and shows up as patches.
    float ign       = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    float rotAngle  = ign * 2.0 * SSAO_PI;
    vec3  randomVec = vec3(cos(rotAngle), sin(rotAngle), 0.0);
    vec3  tangent   = normalize(randomVec - normal * dot(randomVec, normal));
    vec3  bitangent = cross(normal, tangent);
    mat3  TBN       = mat3(tangent, bitangent, normal);

    uint  count   = clamp(pc.value.sampleCount, 1u, 256u); // Req 3.3 bound [1,256]
    float centerDist = -centerViewPos.z;                   // view-space camera distance
    float occluded   = 0.0;

    for (uint i = 0u; i < count; i++) {
        vec2 xi         = hammersley(i, count);
        vec3 dirView    = TBN * hemisphereSampleTS(xi);
        // Distribute sample distances within the radius (denser near the origin).
        float t     = float(i) / float(count);
        float scale = mix(0.1, 1.0, t * t);
        vec3  samplePosView = centerViewPos + dirView * pc.value.radius * scale;

        // Project the sample into screen space to find its neighbour pixel.
        vec4 clip = proj * vec4(samplePosView, 1.0);
        if (clip.w <= 0.0) {
            continue;
        }
        vec2 sampleUV = (clip.xy / clip.w) * 0.5 + 0.5;

        // Neighbour outside the buffer bounds is excluded (Req 3.5).
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 ||
            sampleUV.y < 0.0 || sampleUV.y > 1.0) {
            continue;
        }

        float nd = sampleDepth(sampleUV);
        if (nd == SSAO_FAR_PLANE) {
            continue; // background, no occluding geometry
        }

        vec3 neighborViewPos = reconstructViewPos(sampleUV, nd, invProj);

        // Range check: neighbour geometry must lie within Radius of the pixel
        // (Req 3.5).
        if (length(neighborViewPos - centerViewPos) > pc.value.radius) {
            continue;
        }

        // Occluder rule: compare the stored surface depth at this screen location
        // against the hemisphere SAMPLE POINT's depth (not the centre pixel). Real
        // geometry occludes only when it sits in front of the sample point, i.e. it
        // fills the hemisphere volume. Comparing against the centre (a plain
        // neighbour-depth test) reports false occlusion on any surface tilted in view
        // - including flat planes seen at an angle - because their neighbours are
        // legitimately closer to the camera. That is the flat-surface artifact.
        //
        // A smooth range check attenuates occluders whose depth separation exceeds
        // the radius, so each contribution is a continuous weight in [0,1] instead of
        // a hard 0/1 step (removes quantization banding on smooth gradients).
        float sampleDist   = -samplePosView.z;   // expected depth of the sample point
        float neighborDist = -neighborViewPos.z; // actual stored surface depth here
        if (neighborDist < sampleDist - pc.value.bias) {
            float rangeCheck = smoothstep(0.0, 1.0, pc.value.radius / max(abs(centerDist - neighborDist), 1e-4));
            occluded += rangeCheck;
        }
    }

    // 1.0 = fully lit; apply the Power exponent before use (Req 3.1, 3.7).
    float factor = 1.0 - occluded / float(count);
    factor = pow(clamp(factor, 0.0, 1.0), pc.value.power);
    outColor = vec4(factor, factor, factor, 1.0);
}

// --------------------------------------------------------------------------
// Stage 1 - Edge-aware separable blur
// --------------------------------------------------------------------------
// Depth-weighted average over a 2-pixel kernel along one axis. Neighbours whose
// view-space depth differs from the centre by more than blurDepthThreshold get a
// weight of 0.0; when every off-centre neighbour is excluded the centre's
// unblurred value is emitted (the centre always has weight, so this falls out
// naturally, and the wsum guard covers any degenerate case).
void blurPass() {
    FPConstants fp = FPBuffer(pc.value.fpConstAddress).fpConstants;
    mat4 invProj = fp.view * fp.inverseViewProjection;

    float centerAO    = textureBindless2DLod(pc.value.aoTextureId, pc.value.aoSamplerId, inTexCoord, 0.0).r;
    float centerViewZ = reconstructViewPos(inTexCoord, sampleDepth(inTexCoord), invProj).z;

    vec2 axis = (SSAO_BLUR_AXIS == SSAO_BLUR_V)
        ? vec2(0.0, pc.value.texelSize.y)
        : vec2(pc.value.texelSize.x, 0.0);

    // Gaussian-ish weights for offsets -2..+2.
    float kernel[5] = float[5](1.0, 2.0, 3.0, 2.0, 1.0);

    float sum  = 0.0;
    float wsum = 0.0;
    for (int k = -2; k <= 2; k++) {
        vec2  uv = clamp(inTexCoord + axis * float(k), vec2(0.0), vec2(1.0));
        float ao = textureBindless2DLod(pc.value.aoTextureId, pc.value.aoSamplerId, uv, 0.0).r;
        float nvz = reconstructViewPos(uv, sampleDepth(uv), invProj).z;

        float w = kernel[k + 2];
        if (abs(nvz - centerViewZ) > pc.value.blurDepthThreshold) {
            w = 0.0; // exclude across a depth discontinuity (Req 6.4)
        }
        sum  += ao * w;
        wsum += w;
    }

    float result = (wsum > 0.0) ? (sum / wsum) : centerAO; // all excluded -> centre (Req 6.5)
    result = clamp(result, 0.0, 1.0);                      // stay in [0,1] (Req 6.1)
    outColor = vec4(result, result, result, 1.0);
}

// --------------------------------------------------------------------------
// Stage 2 - Composite
// --------------------------------------------------------------------------
// Reads the AO factor (blurred when the blur pass ran) and the scene colour, and
// either writes the multiplicative darkening or, in RawAO debug mode, the
// grayscale AO. Alpha is always preserved. In half-resolution mode the AO sampler
// is linear, so the fetch upsamples; the factor is clamped to [0,1].
void compositePass() {
    float factor = clamp(
        textureBindless2DLod(pc.value.aoTextureId, pc.value.aoSamplerId, inTexCoord, 0.0).r,
        0.0, 1.0
    );
    vec4 scene = textureBindless2DLod(pc.value.sceneTextureId, pc.value.sceneSamplerId, inTexCoord, 0.0);

    if (SSAO_DEBUG == SSAO_DEBUG_RAWAO) {
        // Grayscale AO to RGB, original alpha preserved (Req 9.2, 9.3).
        outColor = vec4(factor, factor, factor, scene.a);
        return;
    }

    // Multiplicative darkening: rgb * clamp(1 - intensity*(1 - factor), 0, 1),
    // alpha preserved (Req 7.2, 7.3, 7.4). factor == 1.0 leaves rgb unchanged.
    float darken = clamp(1.0 - pc.value.intensity * (1.0 - factor), 0.0, 1.0);
    outColor = vec4(scene.rgb * darken, scene.a);
}

// --------------------------------------------------------------------------
// Main
// --------------------------------------------------------------------------
void main() {
    if (SSAO_STAGE == SSAO_OCCLUSION) {
        occlusionPass();
        return;
    }
    if (SSAO_STAGE == SSAO_BLUR) {
        blurPass();
        return;
    }
    if (SSAO_STAGE == SSAO_COMPOSITE) {
        compositePass();
        return;
    }

    // Fallback - should never be reached.
    outColor = vec4(1.0, 0.0, 1.0, 1.0);
}
