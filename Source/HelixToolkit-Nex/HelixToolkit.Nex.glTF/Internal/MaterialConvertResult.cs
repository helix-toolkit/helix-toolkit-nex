using HelixToolkit.Nex.Material;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// The result of converting a glTF material, containing both the engine material
/// properties and metadata that must be applied to the MeshNode by the SceneBuilder.
/// </summary>
/// <param name="Material">The PBR material properties for the mesh.</param>
/// <param name="Metadata">
/// Metadata about alpha mode and double-sided rendering that the SceneBuilder
/// applies to the MeshNode (e.g., IsTransparent, backface culling).
/// </param>
internal readonly record struct MaterialConvertResult(
    PBRMaterialProperties Material,
    MaterialMetadata Metadata
);
