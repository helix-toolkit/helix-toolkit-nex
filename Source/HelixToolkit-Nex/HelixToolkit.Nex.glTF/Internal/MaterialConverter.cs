using glTFLoader.Schema;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders.Frag;
using Newtonsoft.Json.Linq;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Maps glTF PBR metallic-roughness materials to PBRMaterialProperties.
/// Delegates texture loading to TextureLoader.
/// </summary>
internal sealed class MaterialConverter
{
    private readonly IPBRMaterialPropertyManager _materialPropsManager;
    private readonly TextureLoader _textureLoader;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly ResourceManifest _manifest;
    private readonly PBRShadingMode _defaultShadingMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialConverter"/> class.
    /// </summary>
    /// <param name="materialManager">The material property manager for creating PBR materials.</param>
    /// <param name="textureLoader">The texture loader for resolving and loading glTF textures.</param>
    /// <param name="diagnostics">The diagnostics list to append warnings/errors to.</param>
    /// <param name="manifest">The resource manifest to register created materials with.</param>
    /// <param name="defaultShadingMode">The default shading mode to apply to imported materials.</param>
    public MaterialConverter(
        IPBRMaterialPropertyManager materialManager,
        TextureLoader textureLoader,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest manifest,
        PBRShadingMode defaultShadingMode = PBRShadingMode.PBR
    )
    {
        _materialPropsManager =
            materialManager ?? throw new ArgumentNullException(nameof(materialManager));
        _textureLoader = textureLoader ?? throw new ArgumentNullException(nameof(textureLoader));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        _defaultShadingMode = defaultShadingMode;
    }

    /// <summary>
    /// Converts a glTF material at the specified index into engine PBRMaterialProperties.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="materialIndex">The index of the material in the glTF model's Materials array.</param>
    /// <returns>The created <see cref="PBRMaterialProperties"/> instance.</returns>
    public PBRMaterialProperties ConvertMaterial(Gltf model, int materialIndex)
    {
        return ConvertMaterialWithMetadata(model, materialIndex).Material;
    }

    /// <summary>
    /// Converts a glTF material at the specified index into engine PBRMaterialProperties
    /// along with metadata about alpha mode and double-sided rendering.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="materialIndex">The index of the material in the glTF model's Materials array.</param>
    /// <returns>A <see cref="MaterialConvertResult"/> containing the material and its metadata.</returns>
    public MaterialConvertResult ConvertMaterialWithMetadata(Gltf model, int materialIndex)
    {
        if (model.Materials == null || materialIndex < 0 || materialIndex >= model.Materials.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Material index {materialIndex} is out of range. Using default material.",
                    "Material",
                    materialIndex
                )
            );
            return new MaterialConvertResult(GetDefaultMaterial(), MaterialMetadata.Default);
        }

        var gltfMaterial = model.Materials[materialIndex];
        string materialName = gltfMaterial.Name ?? $"Material_{materialIndex}";

        // Determine shading mode - use Unlit if glTF material specifies KHR_materials_unlit extension
        var shadingMode = _defaultShadingMode;
        if (gltfMaterial.Extensions?.ContainsKey("KHR_materials_unlit") == true)
        {
            shadingMode = PBRShadingMode.Unlit;
        }

        // Create the material via the manager with the determined shading mode
        var material = _materialPropsManager.Create(shadingMode.ToString());
        material.Name = materialName;

        // glTF materials default to non-transmissive (no KHR_materials_transmission).
        // Override the engine default (0.5) so that only materials with the extension get transmission.
        material.TransmissionScale = 0f;
        material.Reflectance = 0.04f;

        // Apply PBR metallic-roughness properties
        ApplyPbrFactors(material, gltfMaterial, materialIndex);

        // Load and assign textures
        ApplyTextures(material, gltfMaterial, materialIndex);

        // Apply KHR_materials_transmission and KHR_materials_volume extensions
        ApplyExtensions(material, gltfMaterial, materialIndex);

        // Extract material metadata for MeshNode configuration
        var metadata = ExtractMetadata(gltfMaterial);

        _manifest.AddMaterial(material);

        return new MaterialConvertResult(material, metadata);
    }

    /// <summary>
    /// Asynchronously converts a glTF material at the specified index into engine PBRMaterialProperties.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="materialIndex">The index of the material in the glTF model's Materials array.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="PBRMaterialProperties"/> instance.</returns>
    public async Task<PBRMaterialProperties> ConvertMaterialAsync(
        Gltf model,
        int materialIndex,
        CancellationToken ct = default
    )
    {
        var result = await ConvertMaterialWithMetadataAsync(model, materialIndex, ct)
            .ConfigureAwait(false);
        return result.Material;
    }

    /// <summary>
    /// Asynchronously converts a glTF material at the specified index into engine PBRMaterialProperties
    /// along with metadata about alpha mode and double-sided rendering.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="materialIndex">The index of the material in the glTF model's Materials array.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MaterialConvertResult"/> containing the material and its metadata.</returns>
    public async Task<MaterialConvertResult> ConvertMaterialWithMetadataAsync(
        Gltf model,
        int materialIndex,
        CancellationToken ct = default
    )
    {
        if (model.Materials == null || materialIndex < 0 || materialIndex >= model.Materials.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Material index {materialIndex} is out of range. Using default material.",
                    "Material",
                    materialIndex
                )
            );
            return new MaterialConvertResult(GetDefaultMaterial(), MaterialMetadata.Default);
        }

        var gltfMaterial = model.Materials[materialIndex];
        string materialName = gltfMaterial.Name ?? $"Material_{materialIndex}";

        // Determine shading mode - use Unlit if glTF material specifies KHR_materials_unlit extension
        var shadingMode = _defaultShadingMode;
        if (gltfMaterial.Extensions?.ContainsKey("KHR_materials_unlit") == true)
        {
            shadingMode = PBRShadingMode.Unlit;
        }

        // Create the material via the manager with the determined shading mode
        var material = _materialPropsManager.Create(shadingMode.ToString());
        material.Name = materialName;

        // glTF materials default to non-transmissive (no KHR_materials_transmission).
        // Override the engine default (0.5) so that only materials with the extension get transmission.
        material.TransmissionScale = 0f;
        material.Reflectance = 0.04f;

        // Apply PBR metallic-roughness properties
        ApplyPbrFactors(material, gltfMaterial, materialIndex);

        // Load and assign textures asynchronously
        await ApplyTexturesAsync(material, gltfMaterial, materialIndex, ct).ConfigureAwait(false);

        // Apply KHR_materials_transmission and KHR_materials_volume extensions
        await ApplyExtensionsAsync(material, gltfMaterial, materialIndex, ct).ConfigureAwait(false);

        // Extract material metadata for MeshNode configuration
        var metadata = ExtractMetadata(gltfMaterial);

        _manifest.AddMaterial(material);

        return new MaterialConvertResult(material, metadata);
    }

    /// <summary>
    /// Returns a default material with glTF default PBR values for primitives
    /// that do not reference a material.
    /// </summary>
    /// <returns>A <see cref="PBRMaterialProperties"/> with default values:
    /// Albedo(1,1,1), Metallic 1.0, Roughness 1.0, Opacity 1.0, Emissive(0,0,0).</returns>
    public PBRMaterialProperties GetDefaultMaterial()
    {
        var material = _materialPropsManager.Create(_defaultShadingMode.ToString());
        material.Name = "Default";

        // glTF default values
        material.Albedo = new Color4(1.0f, 1.0f, 1.0f, 1.0f);
        material.Opacity = 1.0f;
        material.Metallic = 1.0f;
        material.Roughness = 1.0f;
        material.Emissive = new Color4(0.0f, 0.0f, 0.0f, 1.0f);
        material.TransmissionDistortion = 0.1f;
        material.TransmissionPower = 12.0f;
        material.TransmissionScale = 0f;
        material.Reflectance = 0.04f;

        _manifest.AddMaterial(material);

        return material;
    }

    public PBRMaterialProperties CreateMaterialProps(string materialName)
    {
        var material = _materialPropsManager.Create(materialName);
        material.Name = materialName;
        _manifest.AddMaterial(material);
        return material;
    }

    /// <summary>
    /// Extracts material metadata (alpha mode, alpha cutoff, double-sided) from the glTF material.
    /// This metadata is used by the SceneBuilder to configure MeshNode properties.
    /// </summary>
    private static MaterialMetadata ExtractMetadata(glTFLoader.Schema.Material gltfMaterial)
    {
        var alphaMode = gltfMaterial.AlphaMode switch
        {
            glTFLoader.Schema.Material.AlphaModeEnum.BLEND => AlphaMode.Blend,
            glTFLoader.Schema.Material.AlphaModeEnum.MASK => AlphaMode.Mask,
            _ => AlphaMode.Opaque,
        };

        // KHR_materials_transmission: any non-zero transmissionFactor requires alpha blending
        if (
            alphaMode == AlphaMode.Opaque
            && gltfMaterial.Extensions != null
            && gltfMaterial.Extensions.TryGetValue(
                "KHR_materials_transmission",
                out var transmissionExt
            )
            && transmissionExt is JObject transObj
            && transObj.TryGetValue("transmissionFactor", out var tfToken)
            && (tfToken.Type == JTokenType.Float || tfToken.Type == JTokenType.Integer)
            && tfToken.Value<float>() > 0f
        )
        {
            alphaMode = AlphaMode.Blend;
        }

        // glTF spec: alphaCutoff defaults to 0.5 when alphaMode is MASK
        float alphaCutoff = gltfMaterial.AlphaCutoff;

        bool doubleSided = gltfMaterial.DoubleSided;

        return new MaterialMetadata(alphaMode, alphaCutoff, doubleSided);
    }

    /// <summary>
    /// Applies PBR metallic-roughness factor values from the glTF material to the engine material.
    /// </summary>
    private void ApplyPbrFactors(
        PBRMaterialProperties material,
        glTFLoader.Schema.Material gltfMaterial,
        int materialIndex
    )
    {
        var pbr = gltfMaterial.PbrMetallicRoughness;

        if (pbr != null)
        {
            // baseColorFactor → Albedo (RGB) + Opacity (A)
            if (pbr.BaseColorFactor != null && pbr.BaseColorFactor.Length >= 4)
            {
                float r = Math.Clamp(pbr.BaseColorFactor[0], 0f, 1f);
                float g = Math.Clamp(pbr.BaseColorFactor[1], 0f, 1f);
                float b = Math.Clamp(pbr.BaseColorFactor[2], 0f, 1f);
                float a = Math.Clamp(pbr.BaseColorFactor[3], 0f, 1f);

                material.Albedo = new Color4(r, g, b, 1.0f);
                material.Opacity = a;
            }
            else
            {
                // Default: white albedo, full opacity
                material.Albedo = new Color4(1.0f, 1.0f, 1.0f, 1.0f);
                material.Opacity = 1.0f;
            }

            // metallicFactor → Metallic, clamped to [0,1]
            material.Metallic = Math.Clamp(pbr.MetallicFactor, 0f, 1f);

            // roughnessFactor → Roughness, clamped to [0,1]
            material.Roughness = Math.Clamp(pbr.RoughnessFactor, 0f, 1f);
        }
        else
        {
            // No PBR properties specified — use glTF defaults
            material.Albedo = new Color4(1.0f, 1.0f, 1.0f, 1.0f);
            material.Opacity = 1.0f;
            material.Metallic = 1.0f;
            material.Roughness = 1.0f;
        }

        // emissiveFactor → Emissive (RGB)
        if (gltfMaterial.EmissiveFactor != null && gltfMaterial.EmissiveFactor.Length >= 3)
        {
            float er = gltfMaterial.EmissiveFactor[0];
            float eg = gltfMaterial.EmissiveFactor[1];
            float eb = gltfMaterial.EmissiveFactor[2];

            material.Emissive = new Color4(er, eg, eb, 1.0f);
        }
        else
        {
            // Default: no emission
            material.Emissive = new Color4(0.0f, 0.0f, 0.0f, 1.0f);
        }
        if (gltfMaterial.AlphaMode == glTFLoader.Schema.Material.AlphaModeEnum.MASK)
        {
            material.AlphaCutoff = gltfMaterial.AlphaCutoff;
        }
    }

    /// <summary>
    /// Loads and assigns textures from the glTF material to the engine material (synchronous).
    /// </summary>
    private void ApplyTextures(
        PBRMaterialProperties material,
        glTFLoader.Schema.Material gltfMaterial,
        int materialIndex
    )
    {
        var pbr = gltfMaterial.PbrMetallicRoughness;

        // baseColorTexture → AlbedoMap
        if (pbr?.BaseColorTexture != null)
        {
            var textureRef = _textureLoader.LoadTexture(pbr.BaseColorTexture.Index);
            material.AlbedoMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} baseColorTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // metallicRoughnessTexture → MetallicRoughnessMap
        if (pbr?.MetallicRoughnessTexture != null)
        {
            var textureRef = _textureLoader.LoadTexture(pbr.MetallicRoughnessTexture.Index);
            material.MetallicRoughnessMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} metallicRoughnessTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // normalTexture → NormalMap
        if (gltfMaterial.NormalTexture != null)
        {
            var textureRef = _textureLoader.LoadTexture(gltfMaterial.NormalTexture.Index);
            material.NormalMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} normalTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // occlusionTexture → AoMap
        if (gltfMaterial.OcclusionTexture != null)
        {
            var textureRef = _textureLoader.LoadTexture(gltfMaterial.OcclusionTexture.Index);
            material.AoMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} occlusionTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // emissiveTexture — engine does not currently have an EmissiveMap property,
        // so we load the texture but cannot assign it. Add a diagnostic note.
        if (gltfMaterial.EmissiveTexture != null)
        {
            var textureRef = _textureLoader.LoadTexture(gltfMaterial.EmissiveTexture.Index);
            material.EmissiveMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} emissiveTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }
    }

    /// <summary>
    /// Parses KHR_materials_transmission and KHR_materials_volume extension data from the glTF
    /// material and applies it to the engine material (synchronous).
    /// </summary>
    private void ApplyExtensions(
        PBRMaterialProperties material,
        glTFLoader.Schema.Material gltfMaterial,
        int materialIndex
    )
    {
        if (gltfMaterial.Extensions == null)
            return;

        // ── KHR_materials_transmission ────────────────────────────────────────────
        // https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_transmission
        if (
            gltfMaterial.Extensions.TryGetValue(
                "KHR_materials_transmission",
                out var transmissionRaw
            ) && transmissionRaw is JObject transElem
        )
        {
            // transmissionFactor [0..1]: how much light passes through the surface.
            // Map directly to TransmissionScale so the scatter lobe brightness matches.
            if (
                transElem.TryGetValue("transmissionFactor", out var tfProp)
                && (tfProp.Type == JTokenType.Float || tfProp.Type == JTokenType.Integer)
            )
            {
                material.TransmissionScale = Math.Clamp(tfProp.Value<float>(), 0f, 1f);
            }

            // A transmissive surface with no volume thickness texture is treated as fully thin
            // (ThicknessFactor = 0) so that calculateTransmission() produces non-zero output.
            material.ThicknessFactor = 0f;

            // transmissionTexture: R channel — store in ThicknessMap for per-pixel scatter.
            // Volume thicknessTexture below will overwrite if present.
            if (
                transElem.TryGetValue("transmissionTexture", out var ttProp)
                && ttProp.Type == JTokenType.Object
                && ttProp["index"] != null
                && int.TryParse(ttProp["index"]!.ToString(), out int ttIdx)
            )
            {
                var textureRef = _textureLoader.LoadTexture(ttIdx);
                if (textureRef != TextureRef.Null)
                    material.ThicknessMap = textureRef;
                else
                    _diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Material {materialIndex} KHR_materials_transmission transmissionTexture could not be loaded.",
                            "Material",
                            materialIndex
                        )
                    );
            }
        }

        // ── KHR_materials_volume ──────────────────────────────────────────────────
        // https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_volume
        if (
            gltfMaterial.Extensions.TryGetValue("KHR_materials_volume", out var volumeRaw)
            && volumeRaw is JObject volElem
        )
        {
            // thicknessFactor: world-space metres [0, +inf). Not clamped — the shader needs the real scale.
            if (
                volElem.TryGetValue("thicknessFactor", out var thickProp)
                && (thickProp.Type == JTokenType.Float || thickProp.Type == JTokenType.Integer)
            )
            {
                material.ThicknessFactor = Math.Max(thickProp.Value<float>(), 0f);
            }

            // thicknessTexture: G channel stores per-pixel thickness, multiplied by thicknessFactor in shader.
            if (
                volElem.TryGetValue("thicknessTexture", out var thickTexProp)
                && thickTexProp.Type == JTokenType.Object
                && thickTexProp["index"] != null
                && int.TryParse(thickTexProp["index"]!.ToString(), out int thickTexIdx)
            )
            {
                var textureRef = _textureLoader.LoadTexture(thickTexIdx);
                if (textureRef != TextureRef.Null)
                    material.ThicknessMap = textureRef;
                else
                    _diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Material {materialIndex} KHR_materials_volume thicknessTexture could not be loaded.",
                            "Material",
                            materialIndex
                        )
                    );
            }

            // attenuationColor: the color white light becomes after traveling attenuationDistance.
            // Passed to the GPU for Beer-Lambert: T(x) = attenuationColor ^ (x / attenuationDistance).
            if (
                volElem.TryGetValue("attenuationColor", out var acProp)
                && acProp is JArray acArr
                && acArr.Count >= 3
            )
            {
                float r = acArr[0].Value<float>();
                float g = acArr[1].Value<float>();
                float b = acArr[2].Value<float>();
                material.AttenuationColor = new Color4(r, g, b, 1f);
            }

            // attenuationDistance: mean free path in world space. Default +inf (no absorption).
            if (
                volElem.TryGetValue("attenuationDistance", out var adProp)
                && (adProp.Type == JTokenType.Float || adProp.Type == JTokenType.Integer)
                && adProp.Value<float>() > 0f
            )
            {
                material.AttenuationDistance = adProp.Value<float>();
            }
        }

        // ── KHR_materials_ior ─────────────────────────────────────────────────────
        // https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_ior
        if (
            gltfMaterial.Extensions.TryGetValue("KHR_materials_ior", out var iorRaw)
            && iorRaw is JObject iorElem
        )
        {
            float ior = 1.5f; // glTF default
            if (
                iorElem.TryGetValue("ior", out var iorProp)
                && (iorProp.Type == JTokenType.Float || iorProp.Type == JTokenType.Integer)
            )
            {
                ior = iorProp.Value<float>();
            }
            material.Reflectance = IorToF0(ior, materialIndex);
        }
    }

    /// <summary>
    /// Parses KHR_materials_transmission and KHR_materials_volume extension data from the glTF
    /// material and applies it to the engine material (asynchronous).
    /// </summary>
    private async Task ApplyExtensionsAsync(
        PBRMaterialProperties material,
        glTFLoader.Schema.Material gltfMaterial,
        int materialIndex,
        CancellationToken ct
    )
    {
        if (gltfMaterial.Extensions == null)
            return;

        // ── KHR_materials_transmission ────────────────────────────────────────────
        if (
            gltfMaterial.Extensions.TryGetValue(
                "KHR_materials_transmission",
                out var transmissionRaw
            ) && transmissionRaw is JObject transElem
        )
        {
            // transmissionFactor [0..1]: how much light passes through the surface.
            if (
                transElem.TryGetValue("transmissionFactor", out var tfProp)
                && (tfProp.Type == JTokenType.Float || tfProp.Type == JTokenType.Integer)
            )
            {
                material.TransmissionScale = Math.Clamp(tfProp.Value<float>(), 0f, 1f);
            }

            // Treat as fully thin (max scatter) until volume provides a thicknessFactor.
            material.ThicknessFactor = 0f;

            if (
                transElem.TryGetValue("transmissionTexture", out var ttProp)
                && ttProp.Type == JTokenType.Object
                && ttProp["index"] != null
                && int.TryParse(ttProp["index"]!.ToString(), out int ttIdx)
            )
            {
                ct.ThrowIfCancellationRequested();
                var textureRef = await _textureLoader
                    .LoadTextureAsync(ttIdx, ct)
                    .ConfigureAwait(false);
                if (textureRef != TextureRef.Null)
                    material.ThicknessMap = textureRef;
                else
                    _diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Material {materialIndex} KHR_materials_transmission transmissionTexture could not be loaded.",
                            "Material",
                            materialIndex
                        )
                    );
            }
        }

        // ── KHR_materials_volume ──────────────────────────────────────────────────
        if (
            gltfMaterial.Extensions.TryGetValue("KHR_materials_volume", out var volumeRaw)
            && volumeRaw is JObject volElem
        )
        {
            if (
                volElem.TryGetValue("thicknessFactor", out var thickProp)
                && (thickProp.Type == JTokenType.Float || thickProp.Type == JTokenType.Integer)
            )
            {
                material.ThicknessFactor = Math.Max(thickProp.Value<float>(), 0f);
            }

            if (
                volElem.TryGetValue("thicknessTexture", out var thickTexProp)
                && thickTexProp.Type == JTokenType.Object
                && thickTexProp["index"] != null
                && int.TryParse(thickTexProp["index"]!.ToString(), out int thickTexIdx)
            )
            {
                ct.ThrowIfCancellationRequested();
                var textureRef = await _textureLoader
                    .LoadTextureAsync(thickTexIdx, ct)
                    .ConfigureAwait(false);
                if (textureRef != TextureRef.Null)
                    material.ThicknessMap = textureRef;
                else
                    _diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Material {materialIndex} KHR_materials_volume thicknessTexture could not be loaded.",
                            "Material",
                            materialIndex
                        )
                    );
            }

            if (
                volElem.TryGetValue("attenuationColor", out var acProp)
                && acProp is JArray acArr
                && acArr.Count >= 3
            )
            {
                material.AttenuationColor = new Color4(
                    acArr[0].Value<float>(),
                    acArr[1].Value<float>(),
                    acArr[2].Value<float>(),
                    1f
                );
            }

            if (
                volElem.TryGetValue("attenuationDistance", out var adProp)
                && (adProp.Type == JTokenType.Float || adProp.Type == JTokenType.Integer)
                && adProp.Value<float>() > 0f
            )
            {
                material.AttenuationDistance = adProp.Value<float>();
            }
        }

        // ── KHR_materials_ior ─────────────────────────────────────────────────────
        // https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_ior
        if (
            gltfMaterial.Extensions.TryGetValue("KHR_materials_ior", out var iorRawAsync)
            && iorRawAsync is JObject iorElemAsync
        )
        {
            float ior = 1.5f; // glTF default
            if (
                iorElemAsync.TryGetValue("ior", out var iorPropAsync)
                && (
                    iorPropAsync.Type == JTokenType.Float || iorPropAsync.Type == JTokenType.Integer
                )
            )
            {
                ior = iorPropAsync.Value<float>();
            }
            material.Reflectance = IorToF0(ior, materialIndex);
        }
    }

    /// <summary>
    /// Loads and assigns textures from the glTF material to the engine material (asynchronous).
    /// </summary>
    private async Task ApplyTexturesAsync(
        PBRMaterialProperties material,
        glTFLoader.Schema.Material gltfMaterial,
        int materialIndex,
        CancellationToken ct
    )
    {
        var pbr = gltfMaterial.PbrMetallicRoughness;

        // baseColorTexture → AlbedoMap
        if (pbr?.BaseColorTexture != null)
        {
            ct.ThrowIfCancellationRequested();
            var textureRef = await _textureLoader
                .LoadTextureAsync(pbr.BaseColorTexture.Index, ct)
                .ConfigureAwait(false);
            material.AlbedoMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} baseColorTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // metallicRoughnessTexture → MetallicRoughnessMap
        if (pbr?.MetallicRoughnessTexture != null)
        {
            ct.ThrowIfCancellationRequested();
            var textureRef = await _textureLoader
                .LoadTextureAsync(pbr.MetallicRoughnessTexture.Index, ct)
                .ConfigureAwait(false);
            material.MetallicRoughnessMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} metallicRoughnessTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // normalTexture → NormalMap
        if (gltfMaterial.NormalTexture != null)
        {
            ct.ThrowIfCancellationRequested();
            var textureRef = await _textureLoader
                .LoadTextureAsync(gltfMaterial.NormalTexture.Index, ct)
                .ConfigureAwait(false);
            material.NormalMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} normalTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // occlusionTexture → AoMap
        if (gltfMaterial.OcclusionTexture != null)
        {
            ct.ThrowIfCancellationRequested();
            var textureRef = await _textureLoader
                .LoadTextureAsync(gltfMaterial.OcclusionTexture.Index, ct)
                .ConfigureAwait(false);
            material.AoMap = textureRef;

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} occlusionTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }

        // emissiveTexture — engine does not currently have an EmissiveMap property
        if (gltfMaterial.EmissiveTexture != null)
        {
            ct.ThrowIfCancellationRequested();
            var textureRef = await _textureLoader
                .LoadTextureAsync(gltfMaterial.EmissiveTexture.Index, ct)
                .ConfigureAwait(false);

            if (textureRef == TextureRef.Null)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} emissiveTexture could not be loaded.",
                        "Material",
                        materialIndex
                    )
                );
            }
            else
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Material {materialIndex} emissiveTexture loaded but engine does not support EmissiveMap assignment.",
                        "Material",
                        materialIndex
                    )
                );
            }
        }
    }

    /// <summary>
    /// Converts an IOR value to Fresnel F0 reflectance using Schlick's approximation.
    /// Clamps IOR to minimum 1.0 and emits a diagnostic if clamping occurs.
    /// </summary>
    private float IorToF0(float ior, int materialIndex)
    {
        if (ior < 1.0f)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Material {materialIndex} KHR_materials_ior: IOR value {ior} is below 1.0, clamped to 1.0.",
                    "Material",
                    materialIndex
                )
            );
            ior = 1.0f;
        }
        float ratio = (ior - 1.0f) / (ior + 1.0f);
        return ratio * ratio;
    }
}
