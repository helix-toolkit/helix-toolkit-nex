
@code_gen
struct PointerRing {
    vec3 rayOrigin; // Pointer ray origin in world space.
    uint enabled; // 0 = disabled, 1 = enabled
    vec3 rayDirection; // Pointer ray direction in world space.
    float outerDistThreshold; // Distance threshold for fragment world position to pointer ray.
    vec3 color; // Color to use for highlighting fragments near the pointer ray.
    float innerDistThreshold; // Inner distance threshold for fragment world position to pointer ray.
    float colorMix; // Mix factor for pointer ray color (0 = no highlight, 1 = full pointer ray color).
    uint _padding0;
    uint _padding1;
    uint _padding2;
};

@code_gen
struct EnvironmentMapConstants {
    uint envTexIndex;      // Bindless index of the environment cubemap.
    uint samplerIndex;     // Bindless index of the cubemap sampler.
    float intensity;       // Linear radiance multiplier applied to the sampled colour.
    float mipLevel;        // Background LOD to sample (0 = sharpest). Enables a soft/blurred backdrop.

    float rotationY;       // Environment yaw rotation about the world Y axis, in radians.
    uint renderCubeMap;    // 0 = skip PBR cubemap reflections, 1 = sample the cubemap in the PBR shader.
    uint _padding0;
    uint _padding1;
};

@code_gen
struct AtmosphereConstants {
    vec3 rayleighBeta;            // Rayleigh scattering coefficient per km (RGB).
    float rayleighScaleHeight;    // Rayleigh density scale height in km.

    vec3 ozoneBetaAbs;            // Ozone absorption coefficient per km (RGB).
    float mieScaleHeight;         // Mie (aerosol) density scale height in km.

    vec3 sunDirection;            // Normalized world-space direction toward the sun (used when not taken from a directional light).
    float sunIntensity;           // Sun radiance multiplier driving sky and haze brightness.

    float mieBeta;                // Mie scattering coefficient per km.
    float mieBetaExt;             // Mie extinction coefficient per km.
    float mieG;                   // Mie phase anisotropy in [0, 1). Higher values give a tighter glow around the sun.
    float atmosphereHeight;       // Atmosphere thickness in km.

    float groundAltitude;         // Altitude of world Y = 0 above the ground, in km.
    float kmPerUnit;              // World units to km conversion (0.001 when one unit is one meter).
    float sunDiscSize;            // Sun disc radius scale (1 = default size).
    float aerialPerspectiveScale; // Multiplier applied to geometry distance for the haze (1 = physical).

    uint enabled;                 // 0 = atmosphere disabled, 1 = enabled.
    uint mieEnabled;              // 0 = skip Mie (aerosol) scattering, 1 = include it.
    uint ozoneEnabled;            // 0 = skip ozone absorption, 1 = include it.
    uint sunFromDirectionalLight; // 1 = sun direction follows directional light 0 and that light is tinted by the atmosphere.

    uint sunIntensityFromDirectionalLight; // 1 = sunIntensity is replaced by directional light 0's intensity times directionalLightIntensityScale.
    float directionalLightIntensityScale;  // Sky sun intensity per unit of directional light intensity.
    uint _padding0;
    uint _padding1;

    vec3 sunColor;                     // Linear RGB sun colour multiplying sky, sun disc and haze radiance.
    uint sunColorFromDirectionalLight; // 1 = sunColor is replaced by directional light 0's colour.
};

@code_gen
struct FPConstants {
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 view;
    mat4 inverseView;

    vec3 cameraPosition;
    float dpiScale;

    vec2 screenDimensions;
    uint lightCount;
    uint tileSize;

    uint tileCountX;
    uint tileCountY;
    uint maxLightsPerTile;
    uint enabled;

    uint64_t timeMs;
    uint64_t meshInfoBufferAddress;

    uint64_t lightBufferAddress;
    uint64_t lightGridBufferAddress;

    uint64_t lightIndexBufferAddress;
    uint64_t materialBufferAddress;

    uint64_t nodeInfoBufferAddress;
    uint64_t directionalLightsBufferAddress;

    vec4 wireframeColor; // RGBA color for global mesh wireframe mode (if enabled)
    PointerRing pointerRing;
    EnvironmentMapConstants environmentMap;
    AtmosphereConstants atmosphere;
};
