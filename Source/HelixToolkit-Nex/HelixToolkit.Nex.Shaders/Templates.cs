namespace HelixToolkit.Nex.Shaders;

public static class Templates
{
    #region Shader Templates

    public const string BindlessVertexStruct = """
        // Bindless vertex buffer structure
        struct GpuVertex {
            vec3 position;
            float _padding0;
            vec3 normal;
            float _padding1;
            vec2 texCoord;
            vec2 _padding2;
            vec4 tangent;
        };

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
            GpuVertex vertices[];
        };

        """;
    public const string ForwardPlusStructs = """
        // Forward+ light structures
        struct GpuLight {
            vec3 position;
            uint type;
            vec3 direction;
            float range;
            vec3 color;
            float intensity;
            vec2 spotAngles;
            vec2 _padding;
        };

        struct LightGridTile {
            uint lightCount;
            uint lightIndexOffset;
        };

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer LightBuffer {
            GpuLight lights[];
        };

        layout(set = 1, binding = 0) readonly buffer LightGridBuffer {
            LightGridTile tiles[];
        } lightGrid;

        layout(set = 1, binding = 1) readonly buffer LightIndexBuffer {
            uint indices[];
        } lightIndices;

        """;
    public const string StandardInputs = """
        layout(location = 0) in vec3 fragPosition;
        layout(location = 1) in vec3 fragNormal;
        layout(location = 2) in vec2 fragTexCoord;
        layout(location = 3) in vec3 fragTangent;

        """;
    public const string BindlessInputs = """
        layout(location = 0) in flat uint vertexIndex;
        layout(location = 1) in vec3 fragPosition;

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
            {0}
            uint lightBufferAddress;
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
    public const string StandardPushConstant = """
        layout(push_constant) uniform MaterialConstants {
            vec3 cameraPosition;
            float time;
            uint baseColorTexIndex;
            uint metallicRoughnessTexIndex;
            uint normalTexIndex;
            uint samplerIndex;
        } pc;

        """;
    public const string DefaultMainHeader = """
        void main() {
        {0}
            PBRMaterial material;

            #ifdef USE_BASE_COLOR_TEXTURE
                material.albedo = texture(sampler2D(kTextures2D[pc.baseColorTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).rgb;
            #else
                material.albedo = vec3(0.8, 0.8, 0.8);
            #endif

            #ifdef USE_METALLIC_ROUGHNESS_TEXTURE
                vec2 metallicRoughness = texture(sampler2D(kTextures2D[pc.metallicRoughnessTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).bg;
                material.metallic = metallicRoughness.r;
                material.roughness = metallicRoughness.g;
            #else
                material.metallic = 0.5;
                material.roughness = 0.5;
            #endif

            material.ao = 1.0;
            material.opacity = 1.0;
            material.emissive = vec3(0.0);

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
            GpuVertex vertex = vertexBuf.vertices[vertexIndex];
            vec3 fragNormal = vertex.normal;
            vec2 fragTexCoord = vertex.texCoord;
            vec3 fragTangent = vertex.tangent.xyz;

        """;
    public const string SimpleLighting = """
            // Simple directional light
            vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
            vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
            vec3 lightColor = vec3(1.0, 0.95, 0.9);
            float lightIntensity = 3.0;
            vec3 ambientColor = vec3(0.03);

            vec3 finalColor = pbrShadingSimple(material, lightDir, lightColor, lightIntensity, viewDir, ambientColor);

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
            LightGridTile tile = lightGrid.tiles[tileIndex];
            LightBuffer lightBuf = LightBuffer(pc.lightBufferAddress);

            // Process lights in this tile
            for (uint i = 0; i < tile.lightCount; ++i) {
                uint lightIndex = lightIndices.indices[tile.lightIndexOffset + i];
                GpuLight light = lightBuf.lights[lightIndex];

                vec3 L;
                float attenuation = 1.0;

                if (light.type == 0u) {
                    // Directional light
                    L = normalize(-light.direction);
                } else if (light.type == 1u) {
                    // Point light
                    vec3 lightVec = light.position - fragPosition;
                    float distance = length(lightVec);
                    L = lightVec / distance;
                    attenuation = 1.0 / (distance * distance + 1.0);
                    attenuation *= smoothstep(light.range, light.range * 0.5, distance);
                } else if (light.type == 2u) {
                    // Spot light
                    vec3 lightVec = light.position - fragPosition;
                    float distance = length(lightVec);
                    L = lightVec / distance;
                    float theta = dot(L, normalize(-light.direction));
                    float epsilon = light.spotAngles.x - light.spotAngles.y;
                    float spotIntensity = clamp((theta - light.spotAngles.y) / epsilon, 0.0, 1.0);
                    attenuation = spotIntensity / (distance * distance + 1.0);
                    attenuation *= smoothstep(light.range, light.range * 0.5, distance);
                }

                vec3 N = material.normal;
                vec3 V = viewDir;
                vec3 H = normalize(V + L);

                // PBR calculations
                vec3 F0 = mix(vec3(0.04), material.albedo, material.metallic);
                vec3 specular = cookTorranceBRDF(N, V, L, H, F0, material.roughness);
                vec3 kS = fresnelSchlick(max(dot(H, V), 0.0), F0);
                vec3 kD = (vec3(1.0) - kS) * (1.0 - material.metallic);
                vec3 diffuse = kD * lambertianDiffuse(material.albedo);

                float NdotL = max(dot(N, L), 0.0);
                vec3 radiance = light.color * light.intensity * attenuation;
                finalColor += (diffuse + specular) * radiance * NdotL;
            }

            // Add emissive
            finalColor += material.emissive;

        """;
    public const string DefaultMainFooter = """
            // Tone mapping
            finalColor = finalColor / (finalColor + vec3(1.0));

            // Gamma correction
            finalColor = pow(finalColor, vec3(1.0/2.2));

            outColor = vec4(finalColor, material.opacity);
        }
        """;
    public const string VertexShaderBindlessTemplate = """
        // Bindless vertex shader - vertices fetched in fragment shader
        layout(location = 0) out flat uint vertexIndex;
        layout(location = 1) out vec3 fragPosition;

        {0}

        #extension GL_EXT_buffer_reference : require

        struct GpuVertex {
            vec3 position;
            float _padding0;
            vec3 normal;
            float _padding1;
            vec2 texCoord;
            vec2 _padding2;
            vec4 tangent;
        };

        layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer VertexBuffer {
            GpuVertex vertices[];
        };

        void main() {
            VertexBuffer vertexBuf = VertexBuffer(transform.vertexBufferAddress);
            GpuVertex vertex = vertexBuf.vertices[gl_VertexIndex];
            vertexIndex = uint(gl_VertexIndex);
        {1}
        }
        """;
    public const string VertexShaderBindlessForwardPlusPushConstants = """
        layout(push_constant) uniform ForwardPlusConstants {
            mat4 viewProjection;
            mat4 inverseViewProjection;
            // ...other fields...
            uint vertexBufferAddress;
        } transform;
        """;
    public const string VertexShaderBindlessStandardPushConstants = """
        layout(push_constant) uniform TransformConstants {
            mat4 modelViewProjection;
            mat4 model;
            uint vertexBufferAddress;
        } transform;
        """;
    public const string VertexShaderBindlessForwardPlusMain = """
            fragPosition = vertex.position;
            gl_Position = transform.viewProjection * vec4(vertex.position, 1.0);
        """;
    public const string VertexShaderBindlessStandardMain = """
            fragPosition = (transform.model * vec4(vertex.position, 1.0)).xyz;
            gl_Position = transform.modelViewProjection * vec4(vertex.position, 1.0);
        """;
    public const string VertexShaderLegacy = """
        layout(location = 0) in vec3 inPosition;
        layout(location = 1) in vec3 inNormal;
        layout(location = 2) in vec2 inTexCoord;
        layout(location = 3) in vec3 inTangent;

        layout(location = 0) out vec3 fragPosition;
        layout(location = 1) out vec3 fragNormal;
        layout(location = 2) out vec2 fragTexCoord;
        layout(location = 3) out vec3 fragTangent;

        layout(push_constant) uniform TransformConstants {
            mat4 modelViewProjection;
            mat4 model;
        } transform;

        void main() {
            fragPosition = (transform.model * vec4(inPosition, 1.0)).xyz;
            fragNormal = mat3(transform.model) * inNormal;
            fragTexCoord = inTexCoord;
            fragTangent = mat3(transform.model) * inTangent;
            gl_Position = transform.modelViewProjection * vec4(inPosition, 1.0);
        }
        """;
    #endregion
}
