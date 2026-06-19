using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.glTF;

/// <summary>
/// Configuration options for the glTF importer.
/// </summary>
public sealed class ImporterConfig
{
    /// <summary>
    /// Gets or sets the default PBR shading mode for imported materials.
    /// </summary>
    /// <remarks>
    /// This setting determines the default shading mode applied to all materials
    /// during import. Individual materials may override this if specified in the glTF file.
    /// <list type="bullet">
    ///   <item><description><see cref="PBRShadingMode.PBR"/> - Full physically-based rendering (default)</description></item>
    ///   <item><description><see cref="PBRShadingMode.Unlit"/> - No lighting calculations, albedo only</description></item>
    ///   <item><description><see cref="PBRShadingMode.CAD"/> - CAD-style shading with head light and rim enhancement</description></item>
    ///   <item><description><see cref="PBRShadingMode.Flat"/> - Flat shading using geometric normals</description></item>
    ///   <item><description><see cref="PBRShadingMode.Normal"/> - Visualize normals as colors</description></item>
    /// </list>
    /// </remarks>
    public PBRShadingMode DefaultShadingMode { get; set; } = PBRShadingMode.PBR;

    /// <summary>
    /// Gets or sets the default range for point lights when the glTF file does not specify a range.
    /// </summary>
    public float DefaultPointLightRange { get; set; } = 10f;

    /// <summary>
    /// Gets or sets the default range for spot lights when the glTF file does not specify a range.
    /// </summary>
    public float DefaultSpotLightRange { get; set; } = 10f;

    /// <summary>
    /// Gets or sets a value indicating whether point light meshes should be created.
    /// </summary>
    public bool CreatePointLightMeshes { get; set; } = true;

    /// <summary>
    /// Gets or sets the world-space scale applied to the point-light visualization sphere mesh.
    /// </summary>
    public float PointLightMeshSize { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets a value indicating whether the importer decodes
    /// <c>KHR_draco_mesh_compression</c> primitives. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableDracoDecompression { get; set; } = true;

    /// <summary>
    /// Gets a default configuration with standard settings.
    /// </summary>
    public static ImporterConfig Default => new();
}
