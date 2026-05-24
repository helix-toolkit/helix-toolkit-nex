#include "HxHeaders/HeaderFrag.glsl"


layout(location = 0) out vec4 outColor;

layout(location = 0) noperspective in vec3 vBarycentric;
layout(location = 1) flat in vec4 vColor;


void main() {
    vec3 d = fwidth(vBarycentric);

    // Screen-space density metric: how many pixels does one barycentric unit span?
    // Higher density = triangles are smaller on screen = more prone to moiré.
    float density = max(max(d.x, d.y), d.z);

    // 1. Adaptive line thickness
    // Base thickness in pixels. As density grows (triangles shrink on screen),
    // we reduce effective thickness so lines don't merge into solid fill.
    float baseThickness = 1;
    // Thin the line when triangles get small. The clamp prevents lines from
    // disappearing entirely at moderate distances.
    float adaptiveThickness = baseThickness * clamp(1.0 - density * 2.0, 0.3, 1.0);

    // 2. Anti-aliased edge detection
    vec3 a3 = smoothstep(vec3(0.0), d * adaptiveThickness, vBarycentric);
    float edgeFactor = min(min(a3.x, a3.y), a3.z);
    float lineAlpha = 1.0 - edgeFactor;

    // 3. Smooth distance/density fade
    // Use a wider, gentler transition to avoid the abrupt on/off that causes moiré.
    // When density > ~0.3, the triangle is so small that individual edges are
    // sub-pixel and should be fully faded out.
    float fadeStart = 0.08;
    float fadeEnd = 0.35;
    float distanceAlpha = 1.0 - smoothstep(fadeStart, fadeEnd, density);

    // 4. Final alpha with energy conservation
    // At high density, lineAlpha approaches 1.0 everywhere (all pixels are "edge").
    // The distanceAlpha gracefully fades the entire contribution to zero,
    // preventing the solid-fill moiré look.
    float finalAlpha = lineAlpha * distanceAlpha;

    // 5. Early discard for fully transparent fragments
    if (finalAlpha < 0.01) {
        discard;
    }

    outColor = vec4(vColor.rgb, finalAlpha);
}
