#include "../Headers/HeaderVertex.glsl"

layout(location = 0) out vec2 outTexCoord;

void main() {
    // Fullscreen Triangle Optimization Technique
    // ------------------------------------------
    // This shader uses a single oversized triangle instead of a quad (2 triangles)
    // to cover the entire screen with just 3 vertices instead of 6.
    //
    // The triangle extends beyond the [-1, 1] clip space boundaries:
    //   - Vertex 0: (-1, -1) - Bottom-left corner (inside clip space)
    //   - Vertex 1: ( 3, -1) - Extends 2 units beyond the right edge
    //   - Vertex 2: (-1,  3) - Extends 2 units beyond the top edge
    //
    // The GPU automatically clips the triangle to the screen boundaries, efficiently
    // covering the entire viewport with no diagonal seam between triangles.
    //
    // Benefits:
    //   - Fewer vertices (3 vs 6)
    //   - No diagonal edge artifacts
    //   - Better cache coherency
    //   - Industry-standard technique for post-processing effects
    
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),  // Bottom-left
        vec2( 3.0, -1.0),  // Bottom-right (extended beyond screen)
        vec2(-1.0,  3.0)   // Top-left (extended beyond screen)
    );
    
    // Texture coordinates are scaled to match the oversized triangle.
    // Y-coordinates are flipped to match Vulkan's texture coordinate system:
    //   - Clip space: Y = -1 (bottom) to Y = +1 (top)
    //   - Texture space: Y = 1 (bottom) to Y = 0 (top)
    //
    // Mapping:
    //   Position (-1, -1) → TexCoord (0, 1) - Bottom-left corner
    //   Position ( 3, -1) → TexCoord (2, 1) - Extended right edge
    //   Position (-1,  3) → TexCoord (0, -1) - Extended top edge
    //
    // The fragment shader will only see the [0, 1] range after clipping.
    vec2 texCoords[3] = vec2[](
        vec2(0.0, 1.0),   // Bottom-left
        vec2(2.0, 1.0),   // Bottom-right (extended)
        vec2(0.0, -1.0)   // Top-left (extended)
    );
    
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    outTexCoord = texCoords[gl_VertexIndex];
}
