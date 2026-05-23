#include "HxHeaders/HeaderFrag.glsl"


layout(location = 0) out vec4 outColor;

layout(location = 0) noperspective in vec3 vBarycentric;
layout(location = 1) flat in vec4 vColor;


void main() {
    vec3 d = fwidth(vBarycentric);

    float lineThickness = 1.0; // Base line thickness in pixels// 2. Calculate the anti-aliased edge factor.

    // We use smoothstep to create a gradient exactly 1.5 pixels wide for anti-aliasing.

    vec3 edgeWidth = d * lineThickness;

    vec3 smoothing = d * 1.5; 

    vec3 a3 = smoothstep(edgeWidth - smoothing, edgeWidth + smoothing, vBarycentric);

    

    // The closest edge determines our wireframe line. 

    // 0.0 means we are exactly on the line, 1.0 means we are inside the triangle.

    float edgeFactor = min(min(a3.x, a3.y), a3.z);

    

    // 3. Distance/Density Fading (Moiré Prevention)

    // As the triangle gets smaller in screen space (further away), 'd' increases.

    // We sum the derivatives to get a general measure of how small the triangle is.

    float density = length(d);

    

    // Calculate a fade multiplier (1.0 = full wireframe, 0.0 = completely faded out)

    float fadeStart = 0.4;

    float fadeEnd = 0.8;

    float wireAlpha = 1.0 - smoothstep(fadeStart, fadeEnd, density);

    

    // 4. Color Mixing

    // Mix the line color and fill color based on the edge factor

    vec4 baseWireColor = mix(vColor, vec4(0.0), edgeFactor);

    

    // Finally, fade the entire wireframe back into the fill color at extreme distances

    outColor = mix(vColor, baseWireColor, wireAlpha);
}
