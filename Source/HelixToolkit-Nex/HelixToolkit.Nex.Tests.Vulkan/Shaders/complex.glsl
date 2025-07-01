#ifdef VERTEX_SHADER

// Vertex input attributes
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inBinormal;
layout(location = 3) in vec2 inTexCoord;

// Uniforms
layout(set = 0, binding = 0) uniform UBO
{
    mat4 model;
    mat4 view;
    mat4 proj;
} ubo;

// Outputs to fragment shader
layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec3 fragBinormal;
layout(location = 2) out vec2 fragTexCoord;

void main()
{
    // Transform position
    gl_Position = ubo.proj * ubo.view * ubo.model * vec4(inPosition, 1.0);

    // Transform normal and binormal (ignore translation)
    mat3 normalMatrix = mat3(transpose(inverse(ubo.model)));
    fragNormal = normalize(normalMatrix * inNormal);
    fragBinormal = normalize(normalMatrix * inBinormal);

    // Pass through texture coordinates
    fragTexCoord = inTexCoord;
}

#endif

#ifdef FRAGMENT_SHADER

// Inputs from vertex shader
layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec3 fragBinormal;
layout(location = 2) in vec2 fragTexCoord;

layout(set = 0, binding = 1) uniform UBO
{
    uint fragTextureId;
    uint fragSamplerId;
} material;

// Output color
layout(location = 0) out vec4 outColor;

// Complex fragment shader using texture sampling functions
void main()
{
    // IDs for demonstration (should be set by uniform in real use)
    uint tex2DId = material.fragTextureId;
    uint samplerId = material.fragSamplerId;
    uint cubeId = 0;

    // Sample base color from 2D texture
    vec4 baseColor = textureBindless2D(tex2DId, samplerId, fragTexCoord);

    // Sample with LOD for detail
    float lod = 2.0;
    vec4 detailColor = textureBindless2DLod(tex2DId, samplerId, fragTexCoord * 2.0, lod);

    // Shadow lookup (simulate shadow mapping)
    float shadow = textureBindless2DShadow(0, 0, vec3(fragTexCoord, 0.5));

    // Combine colors in a complex way
    vec3 finalColor = mix(baseColor.rgb, detailColor.rgb, 0.5);
    finalColor *= shadow * 0.8 + 0.2;

    // Add a simple lighting effect
    float lighting = max(dot(normalize(fragNormal), normalize(vec3(0.3, 0.7, 0.5))), 0.0);
    finalColor *= lighting * 0.7 + 0.3;

    outColor = vec4(finalColor, 1.0);
}

#endif