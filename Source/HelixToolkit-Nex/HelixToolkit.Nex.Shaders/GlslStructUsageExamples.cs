using System.Numerics;

namespace HelixToolkit.Nex.Shaders.Examples;

/// <summary>
/// Examples demonstrating the usage of auto-generated GLSL structs.
/// These structs are automatically generated from GLSL header files during build.
/// </summary>
public static class GlslStructUsageExamples
{
    /// <summary>
    /// Example: Creating a PBR material for rendering
    /// </summary>
    public static PBRMaterial CreateMetallicMaterial()
    {
        return new PBRMaterial
        {
            Albedo = new Vector3(0.8f, 0.2f, 0.1f), // Reddish base color
            Metallic = 0.9f, // Highly metallic
            Roughness = 0.2f, // Fairly smooth
            Ao = 1.0f, // No ambient occlusion
            Normal = new Vector3(0, 1, 0), // Up-facing normal
            Emissive = Vector3.Zero, // No emission
            Opacity = 1.0f, // Fully opaque
        };
    }

    /// <summary>
    /// Example: Creating a rough dielectric material
    /// </summary>
    public static PBRMaterial CreateRoughPlastic()
    {
        return new PBRMaterial
        {
            Albedo = new Vector3(0.2f, 0.6f, 0.9f), // Blue plastic
            Metallic = 0.0f, // Non-metallic
            Roughness = 0.8f, // Rough surface
            Ao = 1.0f,
            Normal = new Vector3(0, 1, 0),
            Emissive = Vector3.Zero,
            Opacity = 1.0f,
        };
    }

    /// <summary>
    /// Example: Creating a directional light (sun)
    /// </summary>
    public static Light CreateDirectionalLight()
    {
        return new Light
        {
            Position = Vector3.Zero, // Not used for directional
            Direction = Vector3.Normalize(new Vector3(-1, -1, -0.5f)),
            Color = new Vector3(1.0f, 0.95f, 0.8f), // Warm sunlight
            Intensity = 1.5f,
            Type = 0, // 0 = Directional
            Range = 0.0f, // Infinite range
            InnerConeAngle = 0.0f, // Not used
            OuterConeAngle = 0.0f, // Not used
        };
    }

    /// <summary>
    /// Example: Creating a point light
    /// </summary>
    public static Light CreatePointLight(Vector3 position, float range = 10.0f)
    {
        return new Light
        {
            Position = position,
            Direction = Vector3.Zero, // Not used for point lights
            Color = new Vector3(1.0f, 1.0f, 1.0f), // White light
            Intensity = 2.0f,
            Type = 1, // 1 = Point light
            Range = range,
            InnerConeAngle = 0.0f, // Not used
            OuterConeAngle = 0.0f, // Not used
        };
    }

    /// <summary>
    /// Example: Creating a spot light
    /// </summary>
    public static Light CreateSpotLight(Vector3 position, Vector3 direction)
    {
        return new Light
        {
            Position = position,
            Direction = Vector3.Normalize(direction),
            Color = new Vector3(1.0f, 0.9f, 0.7f), // Warm spotlight
            Intensity = 3.0f,
            Type = 2, // 2 = Spot light
            Range = 15.0f,
            InnerConeAngle = 0.3f, // ~17 degrees inner cone
            OuterConeAngle = 0.5f, // ~28 degrees outer cone
        };
    }

    /// <summary>
    /// Example: Creating an emissive material (glowing object)
    /// </summary>
    public static PBRMaterial CreateEmissiveMaterial()
    {
        return new PBRMaterial
        {
            Albedo = new Vector3(0.1f, 0.1f, 0.1f), // Dark base
            Metallic = 0.0f,
            Roughness = 0.5f,
            Ao = 1.0f,
            Normal = new Vector3(0, 1, 0),
            Emissive = new Vector3(2.0f, 1.5f, 0.3f), // Bright orange glow
            Opacity = 1.0f,
        };
    }

    /// <summary>
    /// Example: Creating a transparent glass material
    /// </summary>
    public static PBRMaterial CreateGlassMaterial()
    {
        return new PBRMaterial
        {
            Albedo = new Vector3(0.95f, 0.95f, 0.98f), // Slightly blue tint
            Metallic = 0.0f, // Glass is dielectric
            Roughness = 0.05f, // Very smooth
            Ao = 1.0f,
            Normal = new Vector3(0, 1, 0),
            Emissive = Vector3.Zero,
            Opacity = 0.3f, // Mostly transparent
        };
    }

    /// <summary>
    /// Example: How these structs can be used with GPU buffers
    /// </summary>
    /// <remarks>
    /// These structs have StructLayout(LayoutKind.Sequential) which ensures
    /// the memory layout matches the GLSL struct layout for uploading to GPU.
    ///
    /// Usage with buffers:
    /// <code>
    /// var material = CreateMetallicMaterial();
    /// var buffer = device.CreateBuffer(sizeof(PBRMaterial), BufferUsage.Uniform);
    /// buffer.Update(ref material);
    /// </code>
    /// </remarks>
    public static void ExampleBufferUsage()
    {
        // These structs are designed to be directly uploaded to GPU uniform buffers
        var material = CreateMetallicMaterial();
        var light = CreateDirectionalLight();

        // The StructLayout attribute ensures the C# struct layout matches
        // the GLSL struct layout byte-for-byte
        var materialSize = System.Runtime.InteropServices.Marshal.SizeOf<PBRMaterial>();
        var lightSize = System.Runtime.InteropServices.Marshal.SizeOf<Light>();

        // You can now upload these to GPU buffers safely
        // buffer.SetData(ref material);
        // lightBuffer.SetData(ref light);
    }
}
