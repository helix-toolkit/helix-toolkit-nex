namespace HelixToolkit.Nex.Shaders;

public static class Templates
{
    #region Shader Templates

    public const string BindlessVertexStruct = """
        // Bindless vertex buffer structure
        struct GpuVertex {{
            vec3 position;
            float _padding0;
            vec3 normal;
            float _padding1;
            vec2 texCoord;
            vec2 _padding2;
            vec3 tangent;
            float _padding3;
        }};

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {{
            GpuVertex vertices[];
        }};

        """;
    public const string ForwardPlusStructs = """
        // Forward+ light structures
        // Note: struct Light is defined in PBRFunctions.glsl which must be included

        struct LightGridTile {
            uint lightCount;
            uint lightIndexOffset;
        };

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LightBuffer {
            Light lights[];
        };

        layout(buffer_reference, scalar) readonly buffer LightGridBuffer {
            LightGridTile tiles[]; // x=lightCount, y=lightIndexOffset
        };

        layout(buffer_reference, scalar) readonly buffer LightIndexBuffer {
            uint indices[];
        };

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelMatrixBuffer {
            mat4 models[];
        };
        """;

    public const string BindlessInputs = """
        layout(location = 0) in flat uint vertexIndex;
        layout(location = 1) in vec3 fragPosition;
        layout(location = 2) in vec3 fragNormal;
        layout(location = 3) in vec2 fragTexCoord;
        layout(location = 4) in vec3 fragTangent;
        """;
    public const string OutputDef = """
        layout(location = 0) out vec4 outColor;

        """;
    public const string ForwardPlusPushConstant = """
        layout(push_constant) uniform ForwardPlusConstants {
            mat4 viewProjection;
            mat4 inverseViewProjection;
            vec3 cameraPosition;
            float time;
            uint64_t vertexBufferAddress;
            uint64_t lightBufferAddress;
            uint64_t lightGridBufferAddress;
            uint64_t lightIndexBufferAddress;
            uint64_t modelMatrixBufferAddress;
            uint lightCount;
            uint tileSize;
            vec2 screenDimensions;
            vec2 tileCount;
            uint baseColorTexIndex;
            uint metallicRoughnessTexIndex;
            uint normalTexIndex;
            uint samplerIndex;
        } pc;

        """;

    public const string DefaultMainHeader = """
        void main() {{
        {0}
            PBRMaterial material;

            #ifdef USE_BASE_COLOR_TEXTURE
                material.albedo = texture(sampler2D(kTextures2D[pc.baseColorTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).rgb;
            #else
                material.albedo = vec3(0.9, 0.9, 0.9);
            #endif

            #ifdef USE_METALLIC_ROUGHNESS_TEXTURE
                vec2 metallicRoughness = texture(sampler2D(kTextures2D[pc.metallicRoughnessTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).bg;
                material.metallic = metallicRoughness.r;
                material.roughness = metallicRoughness.g;
            #else
                material.metallic = 0.9;
                material.roughness = 0.6;
            #endif

            material.ao = 1.0;
            material.opacity = 1.0;
            material.emissive = vec3(0);

            #ifdef USE_NORMAL_TEXTURE
                vec3 normalMap = texture(sampler2D(kTextures2D[pc.normalTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).xyz * 2.0 - 1.0;
                vec3 N = normalize(fragNormal);
                vec3 T = normalize(fragTangent);
                vec3 B = cross(N, T);
                mat3 TBN = mat3(T, B, N);
                material.normal = normalize(TBN * normalMap);
            #else
                material.normal = normalize(fragNormal);
            #endif

        """;
    public const string BindlessVertexFetch = """
            VertexBuffer vertexBuf = VertexBuffer(pc.vertexBufferAddress);
            ModelMatrixBuffer modelBuf = ModelMatrixBuffer(pc.modelMatrixBufferAddress);
            GpuVertex vertex = vertexBuf.vertices[vertexIndex];
        """;
    public const string SimpleLighting = """
            // Simple directional light
            vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
            vec3 lightColor = vec3(1.0, 0.95, 0.9);
            float lightIntensity = 3.0;
            vec3 ambientColor = vec3(0.0);
            Light light;
            light.type = 0;
            light.direction = vec3(0, 0, 1);
            light.color = lightColor;
            light.intensity = lightIntensity;
            vec3 finalColor = pbrShadingSimple(material, fragPosition, viewDir, light, ambientColor);
        """;
    public const string Diffuse = """
            vec3 N = normalize(fragNormal);
            vec3 dir = normalize(pc.cameraPosition - fragPosition);
            float f = clamp(0.5 + 0.5 * abs(dot(dir, N)), 0, 1);
            vec3 finalColor = material.albedo * f;

        """;
    public const string ForwardPlusLighting = """
            // Forward+ tiled lighting
            vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
            vec3 ambientColor = vec3(0.03);
            vec3 finalColor = ambientColor * material.albedo * material.ao;

            // Calculate tile coordinates
            ivec2 tileCoord = ivec2(gl_FragCoord.xy) / ivec2(pc.tileSize);
            uint tileIndex = uint(tileCoord.y) * uint(pc.tileCount.x) + uint(tileCoord.x);

            // Get light list for this tile
            LightBuffer lightBuf = LightBuffer(pc.lightBufferAddress);
            LightGridBuffer lightGrid = LightGridBuffer(pc.lightGridBufferAddress);
            LightIndexBuffer lightIndices = LightIndexBuffer(pc.lightIndexBufferAddress);
            LightGridTile tile = lightGrid.tiles[tileIndex];

            // Process lights in this tile
            for (uint i = 0; i < tile.lightCount; ++i) {
                uint lightIndex = lightIndices.indices[tile.lightIndexOffset + i];
                Light light = lightBuf.lights[lightIndex];
                finalColor += calculatePBRLighting(material, light, fragPosition, viewDir);
            }

            finalColor += material.emissive;

        """;
    public const string DefaultMainFooter = """
            // Tone mapping
            finalColor = finalColor / (finalColor + vec3(1.0));

            // Gamma correction
            finalColor = pow(finalColor, vec3(1.0/2.2));

            outColor = vec4(finalColor, material.opacity);
        }}
        """;
    public const string VertexShaderBindlessTemplate = """
        // Bindless vertex shader - vertices fetched in fragment shader
        layout(location = 0) out flat uint vertexIndex;
        layout(location = 1) out vec3 fragPosition;
        layout(location = 2) out vec3 fragNormal;
        layout(location = 3) out vec2 fragTexCoord;
        layout(location = 4) out vec3 fragTangent;
        layout(location = 5) out vec4 fragColor;

        {0}

        struct GpuVertex {{
            vec3 position;
            float _padding0;
            vec3 normal;
            float _padding1;
            vec2 texCoord;
            vec2 _padding2;
            vec3 tangent;
            float _padding3;
        }};

        layout(buffer_reference, std430, buffer_reference_align = 16)readonly buffer VertexBuffer {{
            GpuVertex vertices[];
        }};

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer ModelMatrixBuffer {{
            mat4 models[];
        }};

        void main() {{
            VertexBuffer vertexBuf = VertexBuffer(pc.vertexBufferAddress);
            ModelMatrixBuffer modelBuf = ModelMatrixBuffer(pc.modelMatrixBufferAddress);
            GpuVertex vertex = vertexBuf.vertices[gl_VertexIndex];
            vertexIndex = uint(gl_VertexIndex);
            {1}
        }}
        """;
    public const string VertexShaderBindlessForwardPlusPushConstants = ForwardPlusPushConstant;

    public const string VertexShaderBindlessForwardPlusMain = """
            mat4 model = modelBuf.models[0];
            vec4 wp = model * vec4(vertex.position, 1);
            fragPosition = wp.xyz;
            gl_Position = pc.viewProjection * wp;
            fragNormal = mat3(model) * vertex.normal;
            fragTexCoord = vertex.texCoord;
            fragTangent = mat3(model) * vertex.tangent;
        """;
    #endregion
}
