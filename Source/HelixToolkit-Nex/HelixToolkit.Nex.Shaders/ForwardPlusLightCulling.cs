using System.Text;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Generates compute shaders for Forward+ light culling.
/// </summary>
public static class ForwardPlusLightCulling
{
    /// <summary>
    /// Generates a compute shader for tile-based light culling.
    /// </summary>
    /// <param name="config">Forward+ configuration</param>
    /// <returns>GLSL compute shader source code</returns>
    public static string GenerateLightCullingComputeShader(ForwardPlusConfig config)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#version 460");
        sb.AppendLine();
        sb.AppendLine($"#define TILE_SIZE {config.TileSize}");
        sb.AppendLine($"#define MAX_LIGHTS_PER_TILE {config.MaxLightsPerTile}");
        sb.AppendLine();
        sb.AppendLine(
            "layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;"
        );
        sb.AppendLine();

        // Light structure
        sb.AppendLine("struct GpuLight {");
        sb.AppendLine("    vec3 position;");
        sb.AppendLine("    uint type;");
        sb.AppendLine("    vec3 direction;");
        sb.AppendLine("    float range;");
        sb.AppendLine("    vec3 color;");
        sb.AppendLine("    float intensity;");
        sb.AppendLine("    vec2 spotAngles;");
        sb.AppendLine("    vec2 _padding;");
        sb.AppendLine("};");
        sb.AppendLine();

        // Tile frustum structure
        sb.AppendLine("struct Frustum {");
        sb.AppendLine("    vec4 planes[4]; // left, right, top, bottom");
        sb.AppendLine("    float minDepth;");
        sb.AppendLine("    float maxDepth;");
        sb.AppendLine("};");
        sb.AppendLine();

        // Push constants
        sb.AppendLine("layout(push_constant) uniform LightCullingConstants {");
        sb.AppendLine("    mat4 inverseProjection;");
        sb.AppendLine("    vec2 screenDimensions;");
        sb.AppendLine("    vec2 tileCount;");
        sb.AppendLine("    uint lightCount;");
        sb.AppendLine("    float zNear;");
        sb.AppendLine("    float zFar;");
        sb.AppendLine("} pc;");
        sb.AppendLine();

        // Input/output buffers
        sb.AppendLine("layout(set = 0, binding = 0) readonly buffer LightBuffer {");
        sb.AppendLine("    GpuLight lights[];");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("layout(set = 0, binding = 1) buffer LightGridBuffer {");
        sb.AppendLine("    uvec2 tiles[]; // x=lightCount, y=lightIndexOffset");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("layout(set = 0, binding = 2) buffer LightIndexBuffer {");
        sb.AppendLine("    uint indices[];");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("layout(set = 0, binding = 3) buffer GlobalCounterBuffer {");
        sb.AppendLine("    uint globalLightIndexCounter;");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("layout(set = 0, binding = 4) uniform sampler2D depthTexture;");
        sb.AppendLine();

        // Shared memory for tile
        sb.AppendLine("shared uint visibleLightCount;");
        sb.AppendLine("shared uint visibleLightIndices[MAX_LIGHTS_PER_TILE];");
        sb.AppendLine("shared float minDepthTile;");
        sb.AppendLine("shared float maxDepthTile;");
        sb.AppendLine("shared Frustum tileFrustum;");
        sb.AppendLine();

        // Helper functions
        sb.AppendLine("// Convert depth buffer value to view space Z");
        sb.AppendLine("float depthToViewZ(float depth) {");
        sb.AppendLine("    vec4 clipPos = vec4(0.0, 0.0, depth * 2.0 - 1.0, 1.0);");
        sb.AppendLine("    vec4 viewPos = pc.inverseProjection * clipPos;");
        sb.AppendLine("    return viewPos.z / viewPos.w;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("// Build frustum for tile");
        sb.AppendLine("Frustum createTileFrustum(uvec2 tileID) {");
        sb.AppendLine("    Frustum frustum;");
        sb.AppendLine("    ");
        sb.AppendLine("    // Compute screen space bounds for this tile");
        sb.AppendLine("    vec2 minScreen = vec2(tileID) * TILE_SIZE / pc.screenDimensions;");
        sb.AppendLine("    vec2 maxScreen = vec2(tileID + 1) * TILE_SIZE / pc.screenDimensions;");
        sb.AppendLine("    ");
        sb.AppendLine("    // Convert to NDC");
        sb.AppendLine("    vec2 minNDC = minScreen * 2.0 - 1.0;");
        sb.AppendLine("    vec2 maxNDC = maxScreen * 2.0 - 1.0;");
        sb.AppendLine("    ");
        sb.AppendLine("    // Create frustum planes in view space");
        sb.AppendLine("    vec4 corners[4];");
        sb.AppendLine(
            "    corners[0] = pc.inverseProjection * vec4(minNDC.x, minNDC.y, -1.0, 1.0);"
        );
        sb.AppendLine(
            "    corners[1] = pc.inverseProjection * vec4(maxNDC.x, minNDC.y, -1.0, 1.0);"
        );
        sb.AppendLine(
            "    corners[2] = pc.inverseProjection * vec4(maxNDC.x, maxNDC.y, -1.0, 1.0);"
        );
        sb.AppendLine(
            "    corners[3] = pc.inverseProjection * vec4(minNDC.x, maxNDC.y, -1.0, 1.0);"
        );
        sb.AppendLine("    ");
        sb.AppendLine("    for (int i = 0; i < 4; ++i) {");
        sb.AppendLine("        corners[i] /= corners[i].w;");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    // Left plane");
        sb.AppendLine("    vec3 edge = normalize(corners[3].xyz - corners[0].xyz);");
        sb.AppendLine("    frustum.planes[0] = vec4(cross(edge, corners[0].xyz), 0.0);");
        sb.AppendLine("    ");
        sb.AppendLine("    // Right plane");
        sb.AppendLine("    edge = normalize(corners[1].xyz - corners[2].xyz);");
        sb.AppendLine("    frustum.planes[1] = vec4(cross(corners[2].xyz, edge), 0.0);");
        sb.AppendLine("    ");
        sb.AppendLine("    // Top plane");
        sb.AppendLine("    edge = normalize(corners[2].xyz - corners[3].xyz);");
        sb.AppendLine("    frustum.planes[2] = vec4(cross(corners[3].xyz, edge), 0.0);");
        sb.AppendLine("    ");
        sb.AppendLine("    // Bottom plane");
        sb.AppendLine("    edge = normalize(corners[0].xyz - corners[1].xyz);");
        sb.AppendLine("    frustum.planes[3] = vec4(cross(edge, corners[0].xyz), 0.0);");
        sb.AppendLine("    ");
        sb.AppendLine("    return frustum;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("// Test if sphere intersects frustum");
        sb.AppendLine("bool sphereInsideFrustum(vec3 center, float radius, Frustum frustum) {");
        sb.AppendLine("    // Test against depth bounds");
        sb.AppendLine(
            "    if (center.z - radius > frustum.minDepth || center.z + radius < frustum.maxDepth) {"
        );
        sb.AppendLine("        return false;");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    // Test against frustum planes");
        sb.AppendLine("    for (int i = 0; i < 4; ++i) {");
        sb.AppendLine(
            "        float distance = dot(frustum.planes[i].xyz, center) + frustum.planes[i].w;"
        );
        sb.AppendLine("        if (distance < -radius) {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    return true;");
        sb.AppendLine("}");
        sb.AppendLine();

        // Main compute shader
        sb.AppendLine("void main() {");
        sb.AppendLine("    uvec2 tileID = gl_WorkGroupID.xy;");
        sb.AppendLine("    uvec2 localID = gl_LocalInvocationID.xy;");
        sb.AppendLine("    uint localIndex = localID.y * TILE_SIZE + localID.x;");
        sb.AppendLine("    ");
        sb.AppendLine("    // Initialize shared memory");
        sb.AppendLine("    if (localIndex == 0) {");
        sb.AppendLine("        visibleLightCount = 0;");
        sb.AppendLine("        minDepthTile = 1.0;");
        sb.AppendLine("        maxDepthTile = 0.0;");
        sb.AppendLine("        tileFrustum = createTileFrustum(tileID);");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    barrier();");
        sb.AppendLine("    ");
        sb.AppendLine("    // Calculate min/max depth for this tile");
        sb.AppendLine("    vec2 pixelPos = vec2(tileID * TILE_SIZE + localID);");
        sb.AppendLine("    vec2 texCoord = pixelPos / pc.screenDimensions;");
        sb.AppendLine("    float depth = texture(depthTexture, texCoord).r;");
        sb.AppendLine("    float viewZ = depthToViewZ(depth);");
        sb.AppendLine("    ");
        sb.AppendLine("    atomicMin(floatBitsToUint(minDepthTile), floatBitsToUint(viewZ));");
        sb.AppendLine("    atomicMax(floatBitsToUint(maxDepthTile), floatBitsToUint(viewZ));");
        sb.AppendLine("    ");
        sb.AppendLine("    barrier();");
        sb.AppendLine("    ");
        sb.AppendLine("    if (localIndex == 0) {");
        sb.AppendLine(
            "        tileFrustum.minDepth = uintBitsToFloat(floatBitsToUint(minDepthTile));"
        );
        sb.AppendLine(
            "        tileFrustum.maxDepth = uintBitsToFloat(floatBitsToUint(maxDepthTile));"
        );
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    barrier();");
        sb.AppendLine("    ");
        sb.AppendLine("    // Cull lights against tile frustum");
        sb.AppendLine(
            "    uint threadLightCount = (pc.lightCount + (TILE_SIZE * TILE_SIZE) - 1) / (TILE_SIZE * TILE_SIZE);"
        );
        sb.AppendLine("    uint lightStart = localIndex * threadLightCount;");
        sb.AppendLine("    uint lightEnd = min(lightStart + threadLightCount, pc.lightCount);");
        sb.AppendLine("    ");
        sb.AppendLine("    for (uint i = lightStart; i < lightEnd; ++i) {");
        sb.AppendLine("        GpuLight light = lights[i];");
        sb.AppendLine("        ");
        sb.AppendLine("        // Skip directional lights (they affect all tiles)");
        sb.AppendLine("        if (light.type == 0) {");
        sb.AppendLine("            uint index = atomicAdd(visibleLightCount, 1);");
        sb.AppendLine("            if (index < MAX_LIGHTS_PER_TILE) {");
        sb.AppendLine("                visibleLightIndices[index] = i;");
        sb.AppendLine("            }");
        sb.AppendLine("            continue;");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        // Test point and spot lights");
        sb.AppendLine(
            "        if (sphereInsideFrustum(light.position, light.range, tileFrustum)) {"
        );
        sb.AppendLine("            uint index = atomicAdd(visibleLightCount, 1);");
        sb.AppendLine("            if (index < MAX_LIGHTS_PER_TILE) {");
        sb.AppendLine("                visibleLightIndices[index] = i;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    barrier();");
        sb.AppendLine("    ");
        sb.AppendLine("    // Write results");
        sb.AppendLine("    if (localIndex == 0) {");
        sb.AppendLine("        uint tileIndex = tileID.y * uint(pc.tileCount.x) + tileID.x;");
        sb.AppendLine("        uint count = min(visibleLightCount, MAX_LIGHTS_PER_TILE);");
        sb.AppendLine("        uint offset = atomicAdd(globalLightIndexCounter, count);");
        sb.AppendLine("        ");
        sb.AppendLine("        tiles[tileIndex] = uvec2(count, offset);");
        sb.AppendLine("        ");
        sb.AppendLine("        // Write light indices");
        sb.AppendLine("        for (uint i = 0; i < count; ++i) {");
        sb.AppendLine("            indices[offset + i] = visibleLightIndices[i];");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
