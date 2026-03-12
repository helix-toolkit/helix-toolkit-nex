using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
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
/// Includes animated animals that wander across the terrain surface.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterMaterials"/> once before calling
/// <see cref="IMaterialManager.CreatePBRMaterialsFromRegistry"/>, then call <see cref="Build"/>
/// to populate the ECS world with scene nodes.
/// Call <see cref="UpdateAnimals"/> each frame to animate the animals.
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
    // Animal configuration
    // -----------------------------------------------------------------------
    public const int NumCows = 40;
    public const int NumPigs = 50;
    public const int NumChickens = 60;
    public const int NumSheep = 45;

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
    // Animal type
    // -----------------------------------------------------------------------
    public enum AnimalType
    {
        Cow = 0,
        Pig = 1,
        Chicken = 2,
        Sheep = 3,
    }

    // -----------------------------------------------------------------------
    // Per-animal runtime state for wandering AI
    // -----------------------------------------------------------------------
    private class AnimalState
    {
        public AnimalType Type;
        public Node Node = null!; // scene node that owns this animal's mesh
        public Vector3 Position;
        public float Heading; // radians, 0 = +X direction
        public float Speed;
        public float WanderTimer; // seconds until next direction change
        public float BobPhase; // for walk bobbing animation
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
    // Per-animal material definition: name, albedo, metallic, roughness, ao
    // -----------------------------------------------------------------------
    private static readonly (
        string Name,
        Vector3 Albedo,
        float Metallic,
        float Roughness,
        float Ao
    )[] AnimalMaterialDefs = new (string, Vector3, float, float, float)[]
    {
        ("PBR", new Vector3(0.35f, 0.20f, 0.10f), 0.0f, 0.95f, 1.0f), // Cow (brown)
        ("PBR", new Vector3(0.90f, 0.70f, 0.60f), 0.0f, 0.95f, 1.0f), // Pig (pink)
        ("PBR", new Vector3(0.95f, 0.95f, 0.90f), 0.0f, 0.95f, 1.0f), // Chicken (white)
        ("PBR", new Vector3(0.88f, 0.88f, 0.85f), 0.0f, 0.90f, 1.0f), // Sheep (light grey wool)
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
    // Animal runtime data (populated in Build, updated in UpdateAnimals)
    // -----------------------------------------------------------------------
    private readonly List<AnimalState> _animals = [];
    private int[,]? _heightMap;
    private readonly Random _animalRng = new(123);

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
    /// point lights, animals, and the directional sun.
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
        // _context field removed – no longer needed for animal instancing
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
        _heightMap = GenerateHeightMap(WorldSizeX, WorldSizeZ);

        for (int x = 0; x < WorldSizeX; x++)
        {
            for (int z = 0; z < WorldSizeZ; z++)
            {
                int terrainHeight = _heightMap[x, z];
                BiomeType biome = biomeMap[x, z];
                for (int y = 0; y <= terrainHeight; y++)
                {
                    int blockIdx = (int)GetBlockType(x, y, z, terrainHeight, _heightMap, biome);
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
            blockNode.Entity.Set(new MeshComponent(cube, matProps[b], instancings[b]));
            root.AddChild(blockNode);
        }

        // ------------------------------------------------------------------
        // Build one emissive Unlit MaterialProperties per unique light colour
        // so spheres of the same colour share a single draw call via instancing
        // ------------------------------------------------------------------
        var lightSphereInstancings =
            new Dictionary<Color, (MaterialProperties Mat, Instancing Inst)>();
        foreach (var color in _lightColors)
        {
            var mat = materialPool.Create("Unlit");
            mat.Albedo = color;
            mat.Emissive = color * 2.0f; // bright self-illuminated glow
            mat.Opacity = 1.0f;
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
            float ly = _heightMap[lx, lz] + 2.5f;
            var pos = new Vector3(lx, ly, lz);
            var col = _lightColors[i % _lightColors.Length];

            var lightNode = new Node(worldDataProvider.World, $"PointLight_{i}");
            lightNode.Transform = new Transform { Translation = pos };
            lightNode.Entity.Set(
                new RangeLightComponent(RangeLightType.Point)
                {
                    Position = Vector3.Zero, // local position is zero since it's defined in the node's world transform
                    Color = col,
                    Intensity = 3.0f,
                    Range = 4.0f,
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
            sphereNode.Entity.Set(new MeshComponent(lightSphere, mat, inst));
            root.AddChild(sphereNode);
        }

        // ------------------------------------------------------------------
        // Build animals: create meshes, materials, spawn on terrain
        // ------------------------------------------------------------------
        BuildAnimals(context, geometryManager, materialPool, worldDataProvider, root);

        // ------------------------------------------------------------------
        // Sun-like directional light
        // ------------------------------------------------------------------
        var sunNode = new Node(worldDataProvider.World, "Sun");
        sunNode.Entity.Set(
            new DirectionalLightComponent()
            {
                Color = new Color(1.0f, 0.95f, 0.8f),
                Intensity = 0.1f,
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
    // Animal building
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates animal meshes, materials, spawns animals on the terrain, and adds scene nodes.
    /// Each animal is its own <see cref="Node"/> — movement is driven by
    /// <see cref="Node.Transform"/> rather than GPU instancing.
    /// </summary>
    private void BuildAnimals(
        IContext context,
        IGeometryManager geometryManager,
        IMaterialPropertyManager materialPool,
        WorldDataProvider worldDataProvider,
        Node root
    )
    {
        // Build one shared mesh per animal type using voxel-style boxes
        var animalMeshes = new Geometry[4];
        animalMeshes[(int)AnimalType.Cow] = BuildCowMesh(geometryManager);
        animalMeshes[(int)AnimalType.Pig] = BuildPigMesh(geometryManager);
        animalMeshes[(int)AnimalType.Chicken] = BuildChickenMesh(geometryManager);
        animalMeshes[(int)AnimalType.Sheep] = BuildSheepMesh(geometryManager);

        // Create one shared material per animal type
        var animalMatProps = new MaterialProperties[4];
        for (int a = 0; a < 4; a++)
        {
            var (name, albedo, metallic, roughness, ao) = AnimalMaterialDefs[a];
            var props = materialPool.Create(name);
            props.Properties.Albedo = albedo;
            props.Properties.Metallic = metallic;
            props.Properties.Roughness = roughness;
            props.Properties.Ao = ao;
            props.Properties.Opacity = 1.0f;
            props.NotifyUpdated();
            animalMatProps[a] = props;
        }

        // Spawn animals on suitable terrain (not water, not lava)
        var spawnCounts = new[] { NumCows, NumPigs, NumChickens, NumSheep };
        var animalTypes = new[]
        {
            AnimalType.Cow,
            AnimalType.Pig,
            AnimalType.Chicken,
            AnimalType.Sheep,
        };
        var animalSpeeds = new[] { 1.2f, 1.5f, 2.0f, 1.0f }; // blocks per second

        _animals.Clear();

        for (int a = 0; a < 4; a++)
        {
            for (int i = 0; i < spawnCounts[a]; i++)
            {
                // Find a valid spawn position (avoid water/lava surface blocks)
                int attempts = 0;
                int sx,
                    sz;
                do
                {
                    sx = _animalRng.Next(2, WorldSizeX - 2);
                    sz = _animalRng.Next(2, WorldSizeZ - 2);
                    attempts++;
                } while (attempts < 100 && IsWaterOrLavaAt(sx, sz));

                float spawnY = _heightMap![sx, sz] + 1f;
                var spawnPos = new Vector3(sx, spawnY, sz);
                float heading = (float)(_animalRng.NextDouble() * MathF.PI * 2);
                float speed = animalSpeeds[a] * (0.7f + (float)_animalRng.NextDouble() * 0.6f);

                // One dedicated node per animal — Transform drives position + facing
                var animalNode = new Node(worldDataProvider.World, $"Animal_{animalTypes[a]}_{i}");
                animalNode.Transform.Translation = spawnPos;
                animalNode.Transform.Rotation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    heading
                );
                // Mesh component with no Instancing — the node's WorldTransform is used directly
                animalNode.Entity.Set(new MeshComponent(animalMeshes[a], animalMatProps[a]));
                root.AddChild(animalNode);

                _animals.Add(
                    new AnimalState
                    {
                        Type = animalTypes[a],
                        Node = animalNode,
                        Position = spawnPos,
                        Heading = heading,
                        Speed = speed,
                        WanderTimer = (float)(_animalRng.NextDouble() * 3.0 + 1.0),
                        BobPhase = (float)(_animalRng.NextDouble() * MathF.PI * 2),
                    }
                );
            }
        }
    }

    public void Tick(float deltaTime)
    {
        UpdateAnimals(deltaTime);
    }

    /// <summary>
    /// Updates all animals each frame by writing directly to each animal's
    /// <see cref="Node.Transform"/> and then re-computing world transforms.
    /// No GPU instancing buffers are touched.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds since the last frame.</param>
    public void UpdateAnimals(float deltaTime)
    {
        if (_heightMap == null || _animals.Count == 0)
            return;

        foreach (var animal in _animals)
        {
            // ── Wander AI ──────────────────────────────────────────────
            animal.WanderTimer -= deltaTime;
            if (animal.WanderTimer <= 0)
            {
                animal.Heading += (float)(_animalRng.NextDouble() - 0.5) * MathF.PI * 1.5f;
                animal.WanderTimer = (float)(_animalRng.NextDouble() * 4.0 + 1.5);
            }

            // ── Candidate movement ─────────────────────────────────────
            float dx = MathF.Cos(animal.Heading) * animal.Speed * deltaTime;
            float dz = MathF.Sin(animal.Heading) * animal.Speed * deltaTime;

            float newX = Math.Clamp(animal.Position.X + dx, 1f, WorldSizeX - 2f);
            float newZ = Math.Clamp(animal.Position.Z + dz, 1f, WorldSizeZ - 2f);

            // Bounce off world boundary
            if (newX <= 1f || newX >= WorldSizeX - 2f || newZ <= 1f || newZ >= WorldSizeZ - 2f)
            {
                animal.Heading += MathF.PI;
                animal.WanderTimer = 0.5f;
            }

            int ix = Math.Clamp((int)newX, 0, WorldSizeX - 1);
            int iz = Math.Clamp((int)newZ, 0, WorldSizeZ - 1);

            // Stay away from water / lava
            //if (IsWaterOrLavaAt(ix, iz))
            //{
            //    animal.Heading += MathF.PI * 0.75f;
            //    animal.WanderTimer = 0.3f;
            //    continue;
            //}

            // ── Terrain-following Y with walk bob ──────────────────────
            float targetY = _heightMap[ix, iz] + 3f;
            float newY =
                animal.Position.Y + (targetY - animal.Position.Y) * Math.Min(1f, deltaTime * 5f);

            animal.BobPhase += deltaTime * animal.Speed * 8f;
            float bob = MathF.Sin(animal.BobPhase) * 0.05f;

            animal.Position = new Vector3(newX, newY + bob, newZ);

            // ── Write directly to the node's Transform ─────────────────
            ref var t = ref animal.Node.Transform;
            t.Translation = animal.Position;
            t.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, animal.Heading);
            animal.Node.Entity.NotifyComponentChanged<Transform>();
        }
    }

    // -----------------------------------------------------------------------
    // Animal mesh builders (Minecraft-style blocky animals)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a cow mesh: large rectangular body, head, 4 legs.
    /// All geometry is centered at origin so instancing transforms position it.
    /// </summary>
    private static Geometry BuildCowMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        // Body (wide, long box)
        mb.AddBox(new Vector3(0, 0.5f, 0), 0.8f, 0.7f, 1.4f);
        // Head
        mb.AddBox(new Vector3(0, 0.7f, 0.85f), 0.5f, 0.5f, 0.4f);
        // Legs (4 thin boxes)
        mb.AddBox(new Vector3(-0.25f, 0.0f, 0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(0.25f, 0.0f, 0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(-0.25f, 0.0f, -0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(0.25f, 0.0f, -0.4f), 0.2f, 0.5f, 0.2f);
        var geo = mb.ToMesh().ToGeometry();
        bool ok = geometryManager.Add(geo, out _);
        Debug.Assert(ok, "Failed to add cow geometry");
        return geo;
    }

    /// <summary>
    /// Builds a pig mesh: stout rounded body, snout, 4 short legs.
    /// </summary>
    private static Geometry BuildPigMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        // Body (shorter and rounder than cow)
        mb.AddBox(new Vector3(0, 0.35f, 0), 0.6f, 0.5f, 0.9f);
        // Head / snout
        mb.AddBox(new Vector3(0, 0.45f, 0.55f), 0.45f, 0.4f, 0.35f);
        mb.AddBox(new Vector3(0, 0.4f, 0.78f), 0.2f, 0.15f, 0.12f); // snout
        // Legs (short)
        mb.AddBox(new Vector3(-0.18f, 0.0f, 0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(0.18f, 0.0f, 0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(-0.18f, 0.0f, -0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(0.18f, 0.0f, -0.25f), 0.15f, 0.3f, 0.15f);
        var geo = mb.ToMesh().ToGeometry();
        bool ok = geometryManager.Add(geo, out _);
        Debug.Assert(ok, "Failed to add pig geometry");
        return geo;
    }

    /// <summary>
    /// Builds a chicken mesh: small body, head, 2 thin legs.
    /// </summary>
    private static Geometry BuildChickenMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        // Body (small, round-ish)
        mb.AddBox(new Vector3(0, 0.25f, 0), 0.3f, 0.3f, 0.4f);
        // Head (smaller box on top)
        mb.AddBox(new Vector3(0, 0.5f, 0.2f), 0.2f, 0.2f, 0.2f);
        // Beak
        mb.AddBox(new Vector3(0, 0.48f, 0.35f), 0.08f, 0.06f, 0.1f);
        // Legs (2 thin sticks)
        mb.AddBox(new Vector3(-0.06f, 0.0f, 0.0f), 0.05f, 0.2f, 0.05f);
        mb.AddBox(new Vector3(0.06f, 0.0f, 0.0f), 0.05f, 0.2f, 0.05f);
        // Tail feathers
        mb.AddBox(new Vector3(0, 0.35f, -0.25f), 0.15f, 0.2f, 0.1f);
        var geo = mb.ToMesh().ToGeometry();
        bool ok = geometryManager.Add(geo, out _);
        Debug.Assert(ok, "Failed to add chicken geometry");
        return geo;
    }

    /// <summary>
    /// Builds a sheep mesh: woolly body (bigger box), head, 4 legs.
    /// </summary>
    private static Geometry BuildSheepMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        // Woolly body (larger than actual body to simulate wool)
        mb.AddBox(new Vector3(0, 0.5f, 0), 0.75f, 0.65f, 1.1f);
        // Head (smaller, darker)
        mb.AddBox(new Vector3(0, 0.6f, 0.65f), 0.35f, 0.35f, 0.3f);
        // Legs
        mb.AddBox(new Vector3(-0.22f, 0.0f, 0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(0.22f, 0.0f, 0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(-0.22f, 0.0f, -0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(0.22f, 0.0f, -0.3f), 0.15f, 0.45f, 0.15f);
        var geo = mb.ToMesh().ToGeometry();
        bool ok = geometryManager.Add(geo, out _);
        Debug.Assert(ok, "Failed to add sheep geometry");
        return geo;
    }

    // -----------------------------------------------------------------------
    // Animal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Checks whether the surface block at (x, z) is water or lava (not suitable for animals).
    /// </summary>
    private bool IsWaterOrLavaAt(int x, int z)
    {
        if (_heightMap == null)
            return true;
        int h = _heightMap[x, z];
        var biomeMap = GenerateBiomeMap(WorldSizeX, WorldSizeZ);
        var blockType = GetBlockType(x, h, z, h, _heightMap, biomeMap[x, z]);
        return blockType == BlockType.Water || blockType == BlockType.Lava;
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
