using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace SceneSamples;

/// <summary>
/// Builds a Minecraft-style voxel world scene using instanced cubes with distinct PBR block
/// materials, procedurally generated terrain, scattered point lights, and a directional sun light.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterMaterials"/> once before calling
/// <see cref="IPBRMaterialManager.CreatePBRMaterialsFromRegistry"/>, then call <see cref="Build"/>
/// to populate the ECS world with scene nodes.
/// </remarks>
public class MinecraftScene : IScene
{
    // -----------------------------------------------------------------------
    // World configuration constants
    // -----------------------------------------------------------------------
    public int WorldSizeX { get; } = 32; // blocks along X
    public int WorldSizeZ { get; } = 32; // blocks along Z
    public int MaxTerrainHeight { get; } = 8; // maximum terrain height in blocks
    public int MinTerrainHeight { get; } = 3; // minimum terrain base height
    public const int NumPointLights = 60; // scattered point lights for Forward+

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
    };

    // -----------------------------------------------------------------------
    // Point-light colors that cycle across all spawned lights
    // -----------------------------------------------------------------------
    private static readonly Color[] _lightColors =
    [
        new Color(1.0f, 0.6f, 0.2f), // warm orange (torchlight)
        new Color(0.3f, 0.6f, 1.0f), // cool blue
        new Color(0.2f, 1.0f, 0.4f), // green
        new Color(1.0f, 0.2f, 0.2f), // red
        new Color(1.0f, 1.0f, 0.3f), // yellow
        new Color(0.8f, 0.2f, 1.0f), // purple
    ];

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Registers the custom GLSL material types (Lava, GoldOre, Water) required by the scene.
    /// Must be called before <see cref="IPBRMaterialManager.CreatePBRMaterialsFromRegistry"/>.
    /// </summary>
    public void RegisterMaterials()
    {
        // Lava: pulsing emissive orange-red glow
        PBRMaterialTypeRegistry.Register(
            "Lava",
            """
            PBRMaterial material = createPBRMaterial();
            float pulse = 0.7 + 0.3 * sin(float(getTimeMs() % 100000) / 1000 * 2.0 + fragWorldPos.x + fragWorldPos.z);
            material.emissive = material.albedo * pulse * 3.0;
            material.albedo *= 0.2;
            return vec4(material.albedo + material.emissive, 1.0);
            """
        ).WithPointerRingSupport();

        // Gold ore: metallic PBR with a view-angle sparkle emissive highlight
        PBRMaterialTypeRegistry.Register(
            "GoldOre",
            """
            PBRMaterial material = createPBRMaterial();
            float sparkle = pow(max(dot(fragNormal, normalize(getCameraPosition() - fragWorldPos)), 0.0), 32.0);
            material.emissive = vec3(1.0, 0.85, 0.1) * sparkle * 1.5;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        ).WithPointerRingSupport();

        // Water: time-based wave shimmer blended with PBR lighting
        PBRMaterialTypeRegistry.Register(
            "Water",
            """
            PBRMaterial material = createPBRMaterial();
            float wave = 0.5 + 0.5 * sin(float(getTimeMs() % 100000) / 1000 * 3.0 + fragWorldPos.x * 0.8 + fragWorldPos.z * 0.6);
            material.albedo = mix(material.albedo, vec3(0.1, 1, 1.0), wave);
            material.emissive = material.albedo * 0.15;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        ).WithPointerRingSupport();
    }

    /// <summary>
    /// Builds the Minecraft world and returns the root <see cref="Node"/> containing all blocks,
    /// point lights, and the directional sun.
    /// </summary>
    /// <param name="context">Graphics context used to upload instancing buffers to the GPU.</param>
    /// <param name="resourceManager">Provides geometry and material property pools.</param>
    /// <param name="worldDataProvider">ECS world used to create scene nodes.</param>
    public async Task<Node> BuildAsync(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider
    )
    {
        var geometryManager = resourceManager.Geometries;
        var materialPool = resourceManager.PBRPropertyManager;

        // Single 1×1×1 cube mesh shared by all block types via GPU instancing
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddCube();
        var cube = meshBuilder.ToMesh().ToGeometry();
        var (succ, _) = await geometryManager.AddAsync(cube);
        Debug.Assert(succ, "Failed to add cube geometry");

        // Small sphere mesh used to visualise each point light source
        meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 0.3f, 12, 12);
        var lightSphere = meshBuilder.ToMesh().ToGeometry();
        (succ, _) = await geometryManager.AddAsync(lightSphere);
        Debug.Assert(succ, "Failed to add light sphere geometry");

        var root = new Node(worldDataProvider.World, "MinecraftRoot");

        // ------------------------------------------------------------------
        // Category nodes
        // ------------------------------------------------------------------
        var blocksNode = new Node(worldDataProvider.World, "Blocks");
        var pointLightsNode = new Node(worldDataProvider.World, "PointLights");
        var lightSpheresNode = new Node(worldDataProvider.World, "LightSpheres");
        var sunNode = new Node(worldDataProvider.World, "Sun");

        root.AddChild(blocksNode);
        root.AddChild(pointLightsNode);
        root.AddChild(lightSpheresNode);
        root.AddChild(sunNode);

        // ------------------------------------------------------------------
        // Create one MaterialProperties + one Instancing per block type
        // ------------------------------------------------------------------
        int blockCount = BlockMaterialDefs.Length;
        var instancings = new Instancing[blockCount];
        var matProps = new PBRMaterialProperties[blockCount];

        for (int b = 0; b < blockCount; b++)
        {
            var (name, albedo, metallic, roughness, ao) = BlockMaterialDefs[b];
            var props = materialPool.Create(name);
            props.Properties.Albedo = albedo;
            props.Properties.Metallic = metallic;
            props.Properties.Roughness = roughness;
            props.Properties.Ao = ao;
            props.Properties.Opacity = 1.0f;
            matProps[b] = props;
            instancings[b] = new Instancing(false);
        }

        // ------------------------------------------------------------------
        // Generate terrain heightmap and populate per-block-type instance transforms
        // ------------------------------------------------------------------
        int[,] heightMap = await GenerateHeightMap(WorldSizeX, WorldSizeZ);

        for (int x = 0; x < WorldSizeX; x++)
        {
            for (int z = 0; z < WorldSizeZ; z++)
            {
                int terrainHeight = heightMap[x, z];
                for (int y = 0; y <= terrainHeight; y++)
                {
                    int blockIdx = (int)GetBlockType(x, y, z, terrainHeight, heightMap);
                    instancings[blockIdx]
                        .Transforms.Add(
                            InstanceTransformExts.Identity.SetTranslation(new Vector3(x, y, z))
                        );
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
            var blockNode = worldDataProvider.World.CreateMeshNode(
                $"Block_{(BlockType)b}",
                new MeshComponent(cube, matProps[b], instancings[b])
            );
            blocksNode.AddChild(blockNode);
        }

        // ------------------------------------------------------------------
        // Build one emissive Unlit MaterialProperties per unique light colour
        // so spheres of the same colour share a single draw call via instancing
        // ------------------------------------------------------------------
        var lightSphereInstancings =
            new Dictionary<Color, (PBRMaterialProperties Mat, Instancing Inst)>();
        foreach (var color in _lightColors)
        {
            var mat = materialPool.Create("Unlit");
            mat.Albedo = color;
            mat.Emissive = color * 2.0f; // bright self-illuminated glow
            mat.Opacity = 1.0f;
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
            var col = _lightColors[i % _lightColors.Length];

            var lightNode = worldDataProvider.World.CreateNode($"PointLight_{i}");
            lightNode.Transform = new Transform { Translation = pos };
            lightNode.Entity.Set(
                new RangeLightComponent(RangeLightType.Point)
                {
                    Position = Vector3.Zero, // Use node's world transform for light position
                    Color = col,
                    Intensity = 3.0f,
                    Range = 3.0f,
                    Direction = Vector3.Zero,
                }
            );
            pointLightsNode.AddChild(lightNode);

            // Accumulate a sphere instance at this position for the matching colour group
            lightSphereInstancings[col]
                .Inst.Transforms.Add(InstanceTransformExts.Identity.SetTranslation(pos));
        }

        // Upload sphere instancing buffers and add one node per colour group
        foreach (var (color, (mat, inst)) in lightSphereInstancings)
        {
            if (inst.Transforms.Count == 0)
                continue;

            inst.UpdateBuffer(context);
            var sphereNode = worldDataProvider.World.CreateMeshNode(
                $"LightSpheres_{color}",
                new MeshComponent(lightSphere, mat, inst)
            );
            lightSpheresNode.AddChild(sphereNode);
        }

        // ------------------------------------------------------------------
        // Sun-like directional light
        // ------------------------------------------------------------------
        sunNode.Entity.Set(
            new DirectionalLightComponent
            {
                Color = new Color(1.0f, 0.95f, 0.8f),
                Intensity = 0.05f,
                Direction = Vector3.Normalize(new Vector3(0.4f, -1.0f, 0.5f)),
            }
        );

        return root;
    }

    // -----------------------------------------------------------------------
    // Terrain generation helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generates a 2D heightmap using layered sine/cosine waves for natural terrain variation.
    /// </summary>
    public async Task<int[,]> GenerateHeightMap(int sizeX, int sizeZ)
    {
        var map = new int[sizeX, sizeZ];
        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float fx = x / (float)sizeX;
                float fz = z / (float)sizeZ;

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
                map[x, z] =
                    MinTerrainHeight + (int)(normalized * (MaxTerrainHeight - MinTerrainHeight));
            }
        }
        return map;
    }

    /// <summary>
    /// Returns the <see cref="BlockType"/> for the voxel at <c>(x, y, z)</c> given the terrain height.
    /// </summary>
    public BlockType GetBlockType(int x, int y, int z, int terrainHeight, int[,] heightMap)
    {
        int depth = terrainHeight - y; // 0 = surface, increases downward

        if (y == 0)
            return BlockType.Bedrock;

        // Lava pools at y == 1 underneath tall terrain columns
        if (y == 1 && terrainHeight >= MinTerrainHeight + 3)
            return BlockType.Lava;

        // Water covers the surface of very flat, low-lying areas
        if (depth == 0 && terrainHeight <= MinTerrainHeight)
            return BlockType.Water;

        // Sparse gold ore veins deep underground
        if (depth >= 3 && (x * 7 + z * 13 + y * 3) % 17 == 0)
            return BlockType.GoldOre;

        if (depth >= 3)
            return BlockType.Stone;

        if (depth == 2)
            return (x + z) % 5 == 0 ? BlockType.Gravel : BlockType.Stone;

        if (depth == 1)
            return BlockType.Dirt;

        // Surface: wood on peaks, sand on low flat ground, grass everywhere else
        if (terrainHeight >= MinTerrainHeight + (MaxTerrainHeight - MinTerrainHeight) - 1)
            return BlockType.Wood;

        if (terrainHeight <= MinTerrainHeight + 1)
            return BlockType.Sand;

        return BlockType.Grass;
    }

    public void Tick(float deltaTime) { }
}
