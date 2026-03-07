using System.Diagnostics;
using System.Numerics;
using Arch.Core.Extensions;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;

namespace SceneSamples;

/// <summary>
/// Builds a Minecraft-style voxel world scene using instanced cubes with distinct PBR block
/// materials, procedurally generated terrain, scattered point lights, and a directional sun light.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterMaterials"/> once before calling
/// <see cref="IMaterialManager.CreatePBRMaterialsFromRegistry"/>, then call <see cref="Build"/>
/// to populate the ECS world with scene nodes.
/// </remarks>
public class MinecraftLargeScene : IScene
{
    // -----------------------------------------------------------------------
    // World configuration constants
    // -----------------------------------------------------------------------
    public int WorldSizeX { get; } = 256; // blocks along X
    public int WorldSizeZ { get; } = 256; // blocks along Z
    public int MaxTerrainHeight { get; } = 16; // maximum terrain height in blocks
    public int MinTerrainHeight { get; } = 4; // minimum terrain base height
    public const int NumPointLights = 500; // scattered point lights for Forward+

    // -----------------------------------------------------------------------
    // Block type – each value maps to an index in BlockMaterialDefs
    // -----------------------------------------------------------------------
    public enum BlockType
    {
        Bedrock = 0,
        Stone = 1,
        Dirt = 2,
        Grass = 3,
        Sand = 4,
        Gravel = 5,
        Wood = 6,
        GoldOre = 7,
        Lava = 8,
        Water = 9,
        Snow = 10,
        Sandstone = 11,
    }

    // -----------------------------------------------------------------------
    // Biome type – drives surface block selection and height scaling
    // -----------------------------------------------------------------------
    public enum BiomeType
    {
        Plains = 0, // grass / dirt / gravel
        Desert = 1, // sand / sandstone, flat terrain
        Snowy = 2, // snow cap, tall peaks, ice over water
        Swamp = 3, // mud (dirt) surface, low + wet, mossy gravel
    }

    // -----------------------------------------------------------------------
    // Per-block material definition: shading-mode name, albedo, metallic, roughness, ao
    // -----------------------------------------------------------------------
    private static readonly (
        string Name,
        Vector3 Albedo,
        float Metallic,
        float Roughness,
        float Ao
    )[] BlockMaterialDefs = new (string, Vector3, float, float, float)[]
    {
        ("Unlit", new Vector3(0.08f, 0.08f, 0.08f), 0.0f, 1.0f, 1.0f), // Bedrock
        ("PBR", new Vector3(0.45f, 0.45f, 0.45f), 0.0f, 0.9f, 1.0f), // Stone
        ("PBR", new Vector3(0.55f, 0.35f, 0.15f), 0.0f, 1.0f, 1.0f), // Dirt
        ("PBR", new Vector3(0.25f, 0.65f, 0.15f), 0.0f, 0.95f, 1.0f), // Grass
        ("PBR", new Vector3(0.90f, 0.85f, 0.55f), 0.0f, 1.0f, 1.0f), // Sand
        ("PBR", new Vector3(0.50f, 0.50f, 0.50f), 0.0f, 0.95f, 1.0f), // Gravel
        ("PBR", new Vector3(0.60f, 0.35f, 0.10f), 0.0f, 0.85f, 1.0f), // Wood
        ("GoldOre", new Vector3(0.45f, 0.45f, 0.45f), 0.8f, 0.3f, 1.0f), // Gold Ore
        ("Lava", new Vector3(1.0f, 0.35f, 0.05f), 0.0f, 1.0f, 1.0f), // Lava
        ("Water", new Vector3(0.05f, 0.35f, 0.85f), 0.0f, 0.1f, 1.0f), // Water
        ("Snow", new Vector3(0.92f, 0.95f, 1.00f), 0.0f, 0.85f, 1.0f), // Snow
        ("PBR", new Vector3(0.82f, 0.72f, 0.48f), 0.0f, 0.95f, 1.0f), // Sandstone
    };

    // -----------------------------------------------------------------------
    // Point-light colors that cycle across all spawned lights
    // -----------------------------------------------------------------------
    private static readonly Vector3[] LightColors = new Vector3[]
    {
        new Vector3(1.0f, 0.6f, 0.2f), // warm orange (torchlight)
        new Vector3(0.3f, 0.6f, 1.0f), // cool blue
        new Vector3(0.2f, 1.0f, 0.4f), // green
        new Vector3(1.0f, 0.2f, 0.2f), // red
        new Vector3(1.0f, 1.0f, 0.3f), // yellow
        new Vector3(0.8f, 0.2f, 1.0f), // purple
    };

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Registers the custom GLSL material types (Lava, GoldOre, Water) required by the scene.
    /// Must be called before <see cref="IMaterialManager.CreatePBRMaterialsFromRegistry"/>.
    /// </summary>
    public void RegisterMaterials()
    {
        // Lava: pulsing emissive orange-red glow
        MaterialTypeRegistry.Register(
            "Lava",
            """
            PBRMaterial material = createPBRMaterial();
            float pulse = 0.7 + 0.3 * sin(getTime() * 2.0 + fragWorldPos.x + fragWorldPos.z);
            material.emissive = material.albedo * pulse * 3.0;
            material.albedo *= 0.2;
            return vec4(material.albedo + material.emissive, 1.0);
            """
        );

        // Gold ore: metallic PBR with a view-angle sparkle emissive highlight
        MaterialTypeRegistry.Register(
            "GoldOre",
            """
            PBRMaterial material = createPBRMaterial();
            float sparkle = pow(max(dot(fragNormal, normalize(getCameraPosition() - fragWorldPos)), 0.0), 32.0);
            material.emissive = vec3(1.0, 0.85, 0.1) * sparkle * 1.5;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        );

        // Water: time-based wave shimmer blended with PBR lighting
        MaterialTypeRegistry.Register(
            "Water",
            """
            PBRMaterial material = createPBRMaterial();
            float wave = 0.5 + 0.5 * sin(getTime() * 3.0 + fragWorldPos.x * 0.8 + fragWorldPos.z * 0.6);
            material.albedo = mix(material.albedo, vec3(0.1, 1, 1.0), wave);
            material.emissive = material.albedo * 0.15;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        );

        // Snow: bright diffuse white with a subtle sparkle specular
        MaterialTypeRegistry.Register(
            "Snow",
            """
            PBRMaterial material = createPBRMaterial();
            float sparkle = pow(max(dot(fragNormal, normalize(getCameraPosition() - fragWorldPos)), 0.0), 16.0);
            material.emissive = vec3(0.9, 0.95, 1.0) * sparkle * 0.3;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        );
    }

    /// <summary>
    /// Builds the Minecraft world and returns the root <see cref="Node"/> containing all blocks,
    /// point lights, and the directional sun.
    /// </summary>
    /// <param name="context">Graphics context used to upload instancing buffers to the GPU.</param>
    /// <param name="resourceManager">Provides geometry and material property pools.</param>
    /// <param name="worldDataProvider">ECS world used to create scene nodes.</param>
    public Node Build(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider
    )
    {
        var geometryManager = resourceManager.Geometries;
        var materialPool = resourceManager.MaterialProperties;

        // Single 1×1×1 cube mesh shared by all block types via GPU instancing
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddCube();
        var cube = meshBuilder.ToMesh().ToGeometry();
        bool succ = geometryManager.Add(cube, out _);
        Debug.Assert(succ, "Failed to add cube geometry");

        // Small sphere mesh used to visualise each point light source
        meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 0.3f, 12, 12);
        var lightSphere = meshBuilder.ToMesh().ToGeometry();
        succ = geometryManager.Add(lightSphere, out _);
        Debug.Assert(succ, "Failed to add light sphere geometry");

        var root = new Node(worldDataProvider.World, "MinecraftRoot");

        // ------------------------------------------------------------------
        // Create one MaterialProperties + one Instancing per block type
        // ------------------------------------------------------------------
        int blockCount = BlockMaterialDefs.Length;
        var instancings = new Instancing[blockCount];
        var matProps = new MaterialProperties[blockCount];

        for (int b = 0; b < blockCount; b++)
        {
            var (name, albedo, metallic, roughness, ao) = BlockMaterialDefs[b];
            var props = materialPool.Create(name);
            props.Properties.Albedo = albedo;
            props.Properties.Metallic = metallic;
            props.Properties.Roughness = roughness;
            props.Properties.Ao = ao;
            props.Properties.Opacity = 1.0f;
            props.NotifyUpdated();
            matProps[b] = props;
            instancings[b] = new Instancing(false);
        }

        // ------------------------------------------------------------------
        // Generate terrain heightmap and populate per-block-type instance transforms
        // ------------------------------------------------------------------
        var biomeMap = GenerateBiomeMap(WorldSizeX, WorldSizeZ);
        int[,] heightMap = GenerateHeightMap(WorldSizeX, WorldSizeZ);

        for (int x = 0; x < WorldSizeX; x++)
        {
            for (int z = 0; z < WorldSizeZ; z++)
            {
                int terrainHeight = heightMap[x, z];
                BiomeType biome = biomeMap[x, z];
                for (int y = 0; y <= terrainHeight; y++)
                {
                    int blockIdx = (int)GetBlockType(x, y, z, terrainHeight, heightMap, biome);
                    instancings[blockIdx]
                        .Transforms.Add(Matrix4x4.CreateTranslation(new Vector3(x, y, z)));
                }
            }
        }

        // ------------------------------------------------------------------
        // Upload instancing buffers and create one scene node per block type
        // ------------------------------------------------------------------
        for (int b = 0; b < blockCount; b++)
        {
            if (instancings[b].Transforms.Count == 0)
                continue;

            instancings[b].UpdateBuffer(context);
            var blockNode = new Node(worldDataProvider.World, $"Block_{(BlockType)b}");
            blockNode.Entity.Add(new MeshComponent(cube, matProps[b], instancings[b]));
            root.AddChild(blockNode);
        }

        // ------------------------------------------------------------------
        // Build one emissive Unlit MaterialProperties per unique light colour
        // so spheres of the same colour share a single draw call via instancing
        // ------------------------------------------------------------------
        var lightSphereInstancings =
            new Dictionary<Vector3, (MaterialProperties Mat, Instancing Inst)>();
        foreach (var color in LightColors)
        {
            var mat = materialPool.Create("Unlit");
            mat.Properties.Albedo = color;
            mat.Properties.Emissive = color * 2.0f; // bright self-illuminated glow
            mat.Properties.Opacity = 1.0f;
            mat.NotifyUpdated();
            lightSphereInstancings[color] = (mat, new Instancing(false));
        }

        // ------------------------------------------------------------------
        // Scatter point lights above the terrain surface
        // ------------------------------------------------------------------
        var rand = new Random(42);
        for (int i = 0; i < NumPointLights; i++)
        {
            int lx = rand.Next(0, WorldSizeX);
            int lz = rand.Next(0, WorldSizeZ);
            float ly = heightMap[lx, lz] + 2.5f;
            var pos = new Vector3(lx, ly, lz);
            var col = LightColors[i % LightColors.Length];

            var lightNode = new Node(worldDataProvider.World, $"PointLight_{i}");
            lightNode.Transform = new Transform { Translation = pos };
            lightNode.Entity.Add(
                new Light
                {
                    Position = pos,
                    Type = 1, // point light
                    Color = col,
                    Intensity = 3.0f,
                    Range = 3.0f,
                    Direction = Vector3.Zero,
                }
            );
            root.AddChild(lightNode);

            // Accumulate a sphere instance at this position for the matching colour group
            lightSphereInstancings[col].Inst.Transforms.Add(Matrix4x4.CreateTranslation(pos));
        }

        // Upload sphere instancing buffers and add one node per colour group
        foreach (var (color, (mat, inst)) in lightSphereInstancings)
        {
            if (inst.Transforms.Count == 0)
                continue;

            inst.UpdateBuffer(context);
            var sphereNode = new Node(worldDataProvider.World, $"LightSpheres_{color}");
            sphereNode.Entity.Add(new MeshComponent(lightSphere, mat, inst));
            root.AddChild(sphereNode);
        }

        // ------------------------------------------------------------------
        // Sun-like directional light
        // ------------------------------------------------------------------
        var sunNode = new Node(worldDataProvider.World, "Sun");
        sunNode.Entity.Add(
            new DirectionalLight
            {
                Color = new Vector3(1.0f, 0.95f, 0.8f),
                Intensity = 0.05f,
                Direction = Vector3.Normalize(new Vector3(0.4f, -1.0f, 0.5f)),
            }
        );
        root.AddChild(sunNode);

        // ------------------------------------------------------------------
        // Flatten hierarchy and synchronise world transforms
        // ------------------------------------------------------------------
        var allNodes = new FastList<Node>();
        root.Flatten(node => node.Enabled, allNodes);
        allNodes.UpdateTransforms();

        return root;
    }

    // -----------------------------------------------------------------------
    // Terrain generation helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generates a 2D heightmap using layered sine/cosine waves for natural terrain variation.
    /// Heights are additionally shaped by the biome: deserts are flattened, snowy biomes
    /// amplified, swamps clamped low.
    /// </summary>
    public int[,] GenerateHeightMap(int sizeX, int sizeZ)
    {
        var biomeMap = GenerateBiomeMap(sizeX, sizeZ);
        var map = new int[sizeX, sizeZ];

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float fx = x / (float)sizeX;
                float fz = z / (float)sizeZ;

                // Three octaves of sinusoidal noise for the base elevation
                float n =
                    0.50f * MathF.Sin(fx * MathF.PI * 2.5f + 0.3f) * MathF.Cos(fz * MathF.PI * 2.0f)
                    + 0.25f
                        * MathF.Sin(fx * MathF.PI * 5.0f + 1.1f)
                        * MathF.Cos(fz * MathF.PI * 4.5f + 0.7f)
                    + 0.15f
                        * MathF.Sin(fx * MathF.PI * 10.0f)
                        * MathF.Cos(fz * MathF.PI * 9.0f + 1.3f);

                // Normalise from ~[-0.9, 0.9] to [0, 1]
                float normalized = Math.Clamp((n + 0.9f) / 1.8f, 0f, 1f);

                // Biome-specific height shaping
                normalized = biomeMap[x, z] switch
                {
                    BiomeType.Desert => normalized * 0.45f, // flat desert
                    BiomeType.Snowy => 0.35f + normalized * 0.65f, // tall snowy peaks
                    BiomeType.Swamp => normalized * 0.30f, // low, wet swamp
                    _ => normalized, // plains: unchanged
                };

                map[x, z] =
                    MinTerrainHeight + (int)(normalized * (MaxTerrainHeight - MinTerrainHeight));
            }
        }
        return map;
    }

    /// <summary>
    /// Generates a 2D biome map using an independent low-frequency noise field so that
    /// biome boundaries are gradual and completely independent of terrain elevation.
    /// </summary>
    public static BiomeType[,] GenerateBiomeMap(int sizeX, int sizeZ)
    {
        var map = new BiomeType[sizeX, sizeZ];
        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float fx = x / (float)sizeX;
                float fz = z / (float)sizeZ;

                // Two independent low-frequency channels – temperature (T) and humidity (H)
                float T =
                    0.60f
                        * MathF.Sin(fx * MathF.PI * 1.3f + 0.7f)
                        * MathF.Cos(fz * MathF.PI * 1.1f + 0.4f)
                    + 0.40f
                        * MathF.Sin(fx * MathF.PI * 2.7f + 1.9f)
                        * MathF.Cos(fz * MathF.PI * 2.3f + 1.2f);

                float H =
                    0.60f
                        * MathF.Sin(fx * MathF.PI * 1.7f + 2.3f)
                        * MathF.Cos(fz * MathF.PI * 1.5f + 0.9f)
                    + 0.40f
                        * MathF.Sin(fx * MathF.PI * 3.1f + 0.5f)
                        * MathF.Cos(fz * MathF.PI * 2.9f + 1.7f);

                // Map both channels to [0, 1]
                float t = Math.Clamp((T + 1f) * 0.5f, 0f, 1f); // 0 = cold, 1 = hot
                float h = Math.Clamp((H + 1f) * 0.5f, 0f, 1f); // 0 = dry,  1 = wet

                map[x, z] = (t, h) switch
                {
                    ( > 0.55f, _) => BiomeType.Desert, // hot            → desert
                    ( < 0.30f, _) => BiomeType.Snowy, // cold           → snowy
                    (_, > 0.60f) => BiomeType.Swamp, // mild + wet     → swamp
                    _ => BiomeType.Plains, // mild + dry/mid → plains
                };
            }
        }
        return map;
    }

    /// <summary>
    /// Returns the <see cref="BlockType"/> for the voxel at <c>(x, y, z)</c> given the terrain
    /// height and the biome at that column.
    /// </summary>
    public BlockType GetBlockType(
        int x,
        int y,
        int z,
        int terrainHeight,
        int[,] heightMap,
        BiomeType biome
    )
    {
        int depth = terrainHeight - y; // 0 = surface, increases downward

        // ── Bedrock ──────────────────────────────────────────────────────
        if (y == 0)
            return BlockType.Bedrock;

        // ── Lava pockets at y==1 under tall columns ──────────────────────
        if (y == 1 && terrainHeight >= MinTerrainHeight + 4)
            return BlockType.Lava;

        // ── Water / ice at very low surface columns ───────────────────────
        if (depth == 0 && terrainHeight <= MinTerrainHeight)
            return biome == BiomeType.Snowy ? BlockType.Snow : BlockType.Water;

        // ── Sparse gold ore deep underground (all biomes) ─────────────────
        if (depth >= 4 && (x * 7 + z * 13 + y * 3) % 17 == 0)
            return BlockType.GoldOre;

        // ── Deep sub-surface: stone (all biomes) ─────────────────────────
        if (depth >= 4)
            return BlockType.Stone;

        // ── Shallow sub-surface (depth 1-3): biome variations ────────────
        if (depth >= 2)
        {
            return biome switch
            {
                BiomeType.Desert => BlockType.Sandstone,
                BiomeType.Swamp => (x * 3 + z * 7 + y) % 4 == 0 ? BlockType.Gravel : BlockType.Dirt,
                _ => (x + z) % 5 == 0 ? BlockType.Gravel : BlockType.Stone,
            };
        }

        if (depth == 1)
        {
            return biome switch
            {
                BiomeType.Desert => BlockType.Sand,
                BiomeType.Snowy => BlockType.Dirt,
                BiomeType.Swamp => BlockType.Dirt,
                _ => BlockType.Dirt,
            };
        }

        // ── Surface block (depth == 0) ────────────────────────────────────
        return biome switch
        {
            BiomeType.Desert => BlockType.Sand,
            BiomeType.Snowy => terrainHeight
            >= MinTerrainHeight + (MaxTerrainHeight - MinTerrainHeight) / 2
                ? BlockType.Snow
                : BlockType.Stone,
            BiomeType.Swamp => (x * 5 + z * 11) % 6 == 0 ? BlockType.Gravel : BlockType.Grass,
            _ => terrainHeight >= MinTerrainHeight + (MaxTerrainHeight - MinTerrainHeight) - 1
                ? BlockType.Wood // mountain peak (wood trunk tops)
                : BlockType.Grass,
        };
    }

    /// <summary>
    /// Overload kept for API compatibility — delegates to the biome-aware overload
    /// using a Plains biome.
    /// </summary>
    public BlockType GetBlockType(int x, int y, int z, int terrainHeight, int[,] heightMap) =>
        GetBlockType(x, y, z, terrainHeight, heightMap, BiomeType.Plains);
}
