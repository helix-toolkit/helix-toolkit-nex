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
/// Includes animated animals that wander across the terrain surface, and animated spot lights that
/// sweep their beams across the terrain to test dynamic light handling.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterMaterials"/> once before calling
/// <see cref="IPBRMaterialManager.CreatePBRMaterialsFromRegistry"/>, then call <see cref="Build"/>
/// to populate the ECS world with scene nodes.
/// Call <see cref="Tick"/> each frame to animate animals and spot lights.
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
    // Spot-light configuration
    // -----------------------------------------------------------------------
    public const int NumSpotLights = 8; // sweeping spot lights above the terrain
    private const float SpotInnerDeg = 10f; // inner cone half-angle (degrees)
    private const float SpotOuterDeg = 15f; // outer cone half-angle (degrees)
    private const float SpotRange = 80f; // world-unit reach
    private const float SpotIntensity = 100f;
    private const float SpotHeight = 20f; // height above terrain surface

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
    // Per-spot-light runtime state
    // -----------------------------------------------------------------------
    private class SpotLightState
    {
        public Node Node = null!; // single node: carries RangeLightComponent + MeshComponent
        public Vector3 BasePosition; // fixed world-space anchor (x, height, z)
        public float SwingPhase; // current sweep phase (radians)
        public float SwingSpeed; // radians per second
        public float SwingAmplitude; // max swing angle from vertical (radians)
        public Vector3 SwingAxis; // horizontal axis the beam swings around
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
    // Spot-light colors (one per light; wraps if NumSpotLights > array length)
    // -----------------------------------------------------------------------
    private static readonly Color[] _spotLightColors =
    [
        new Color(1.0f, 0.95f, 0.8f), // warm white
        new Color(0.3f, 0.8f, 1.0f), // sky blue
        new Color(1.0f, 0.3f, 0.3f), // red
        new Color(0.3f, 1.0f, 0.4f), // green
        new Color(1.0f, 0.9f, 0.2f), // yellow
        new Color(0.9f, 0.3f, 1.0f), // purple
        new Color(0.2f, 1.0f, 0.9f), // cyan
        new Color(1.0f, 0.55f, 0.1f), // orange
    ];

    // -----------------------------------------------------------------------
    // Runtime data (populated in Build, updated in Tick)
    // -----------------------------------------------------------------------
    private readonly List<AnimalState> _animals = [];
    private readonly List<SpotLightState> _spotLights = [];
    private int[,]? _heightMap;
    private readonly Random _animalRng = new(123);

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Registers the custom GLSL material types (Lava, GoldOre, Water, Snow) required by the
    /// scene. Must be called before <see cref="IPBRMaterialManager.CreatePBRMaterialsFromRegistry"/>.
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
        );

        // Gold ore: metallic PBR with a view-angle sparkle emissive highlight
        PBRMaterialTypeRegistry.Register(
            "GoldOre",
            """
            PBRMaterial material = createPBRMaterial();
            float sparkle = pow(max(dot(fragNormal, normalize(getCameraPosition() - fragWorldPos)), 0.0), 32.0);
            material.emissive = vec3(1.0, 0.85, 0.1) * sparkle * 1.5;
            return forwardPlusLighting(material) + vec4(material.emissive, 0.0);
            """
        );

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
        );

        // Snow: bright diffuse white with a subtle sparkle specular
        PBRMaterialTypeRegistry.Register(
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
    /// point lights, spot lights, animals, and the directional sun.
    /// </summary>
    public Node Build(
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
        bool succ = geometryManager.AddAsync(cube, out _);
        Debug.Assert(succ, "Failed to add cube geometry");

        // Small sphere mesh used to visualise each point light source
        meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 0.3f, 12, 12);
        var lightSphere = meshBuilder.ToMesh().ToGeometry();
        succ = geometryManager.AddAsync(lightSphere, out _);
        Debug.Assert(succ, "Failed to add light sphere geometry");

        var root = new Node(worldDataProvider.World, "MinecraftRoot");

        // ------------------------------------------------------------------
        // Category nodes
        // ------------------------------------------------------------------
        var blocksNode = new Node(worldDataProvider.World, "Blocks");
        var pointLightsNode = new Node(worldDataProvider.World, "PointLights");
        var lightSpheresNode = new Node(worldDataProvider.World, "LightSpheres");
        var animalsNode = new Node(worldDataProvider.World, "Animals");
        var spotLightsNode = new Node(worldDataProvider.World, "SpotLights");
        var sunNode = new Node(worldDataProvider.World, "Sun");

        root.AddChild(blocksNode);
        root.AddChild(pointLightsNode);
        root.AddChild(lightSpheresNode);
        root.AddChild(animalsNode);
        root.AddChild(spotLightsNode);
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
            mat.Emissive = color * 2.0f;
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
            float ly = _heightMap[lx, lz] + 2.5f;
            var pos = new Vector3(lx, ly, lz);
            var col = _lightColors[i % _lightColors.Length];

            var lightNode = worldDataProvider.World.CreateNode($"PointLight_{i}");
            lightNode.Transform = new Transform { Translation = pos };
            lightNode.Entity.Set(
                new RangeLightComponent(RangeLightType.Point)
                {
                    Position = Vector3.Zero,
                    Color = col,
                    Intensity = 3.0f,
                    Range = 4.0f,
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
        // Build animals: create meshes, materials, spawn on terrain
        // ------------------------------------------------------------------
        BuildAnimals(context, geometryManager, materialPool, worldDataProvider, animalsNode);

        // ------------------------------------------------------------------
        // Build spot lights: elevated positions, sweeping beams, cone visualisers
        // ------------------------------------------------------------------
        BuildSpotLights(
            context,
            geometryManager,
            materialPool,
            worldDataProvider,
            spotLightsNode,
            rand
        );

        // ------------------------------------------------------------------
        // Sun-like directional light
        // ------------------------------------------------------------------
        sunNode.Entity.Set(
            new DirectionalLightComponent()
            {
                Color = new Color(1.0f, 0.95f, 0.8f),
                Intensity = 0.1f,
                Direction = Vector3.Normalize(new Vector3(0.4f, -1.0f, 0.5f)),
            }
        );
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
        IPBRMaterialPropertyManager materialPool,
        WorldDataProvider worldDataProvider,
        Node root
    )
    {
        var animalMeshes = new Geometry[4];
        animalMeshes[(int)AnimalType.Cow] = BuildCowMesh(geometryManager);
        animalMeshes[(int)AnimalType.Pig] = BuildPigMesh(geometryManager);
        animalMeshes[(int)AnimalType.Chicken] = BuildChickenMesh(geometryManager);
        animalMeshes[(int)AnimalType.Sheep] = BuildSheepMesh(geometryManager);

        var animalMatProps = new PBRMaterialProperties[4];
        for (int a = 0; a < 4; a++)
        {
            var (name, albedo, metallic, roughness, ao) = AnimalMaterialDefs[a];
            var props = materialPool.Create(name);
            props.Properties.Albedo = albedo;
            props.Properties.Metallic = metallic;
            props.Properties.Roughness = roughness;
            props.Properties.Ao = ao;
            props.Properties.Opacity = 1.0f;
            animalMatProps[a] = props;
        }

        // ------------------------------------------------------------------
        // Per-animal-type sub-category nodes
        // ------------------------------------------------------------------
        var animalTypeNodes = new Node[4];
        animalTypeNodes[(int)AnimalType.Cow] = new Node(worldDataProvider.World, "Cows");
        animalTypeNodes[(int)AnimalType.Pig] = new Node(worldDataProvider.World, "Pigs");
        animalTypeNodes[(int)AnimalType.Chicken] = new Node(worldDataProvider.World, "Chickens");
        animalTypeNodes[(int)AnimalType.Sheep] = new Node(worldDataProvider.World, "Sheep");

        foreach (var typeNode in animalTypeNodes)
            root.AddChild(typeNode);

        var spawnCounts = new[] { NumCows, NumPigs, NumChickens, NumSheep };
        var animalTypes = new[]
        {
            AnimalType.Cow,
            AnimalType.Pig,
            AnimalType.Chicken,
            AnimalType.Sheep,
        };
        var animalSpeeds = new[] { 1.2f, 1.5f, 2.0f, 1.0f };

        _animals.Clear();

        for (int a = 0; a < 4; a++)
        {
            for (int i = 0; i < spawnCounts[a]; i++)
            {
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

                var animalNode = worldDataProvider.World.CreateMeshNode(
                    $"Animal_{animalTypes[a]}_{i}",
                    new MeshComponent(animalMeshes[a], animalMatProps[a])
                );
                animalNode.Transform.Translation = spawnPos;
                animalNode.Transform.Rotation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    heading
                );
                animalTypeNodes[a].AddChild(animalNode);

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

    // -----------------------------------------------------------------------
    // Spot-light building
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates <see cref="NumSpotLights"/> spot lights placed at evenly-spaced positions above the
    /// terrain, each paired with a small emissive cone mesh so the source is visible. All lights
    /// are given random sweep parameters; their beams are animated each frame via
    /// <see cref="UpdateSpotLights"/>.
    /// </summary>
    private void BuildSpotLights(
        IContext context,
        IGeometryManager geometryManager,
        IPBRMaterialPropertyManager materialPool,
        WorldDataProvider worldDataProvider,
        Node root,
        Random rand
    )
    {
        float cosInner = MathF.Cos(new AngleSingle(SpotInnerDeg, AngleType.Degree).Radians);
        float cosOuter = MathF.Cos(new AngleSingle(SpotOuterDeg, AngleType.Degree).Radians);
        var spotAngles = new Vector2(cosInner, cosOuter);

        var mb = new MeshBuilder(true, true, true);
        mb.AddCone(Vector3.Zero, -Vector3.UnitY, 0f, 0.8f, 2.5f, false, true, 12);
        var coneMesh = mb.ToMesh().ToGeometry();
        bool ok = geometryManager.AddAsync(coneMesh, out _);
        Debug.Assert(ok, "Failed to add spot-light cone geometry");

        _spotLights.Clear();

        float stepX = WorldSizeX / (float)(NumSpotLights + 1);

        for (int i = 0; i < NumSpotLights; i++)
        {
            float worldX = stepX * (i + 1);
            float worldZ = WorldSizeZ * 0.5f + (float)(rand.NextDouble() - 0.5) * WorldSizeZ * 0.3f;
            int ix = Math.Clamp((int)worldX, 0, WorldSizeX - 1);
            int iz = Math.Clamp((int)worldZ, 0, WorldSizeZ - 1);
            float worldY = (_heightMap?[ix, iz] ?? MinTerrainHeight) + SpotHeight;
            var basePos = new Vector3(worldX, worldY, worldZ);

            var col = _spotLightColors[i % _spotLightColors.Length];

            var coneMat = materialPool.Create("Unlit");
            coneMat.Albedo = col;
            coneMat.Emissive = col * 2.0f;
            coneMat.Opacity = 1.0f;

            var lightNode = worldDataProvider.World.CreateMeshNode(
                $"SpotLight_{i}",
                new MeshComponent(coneMesh, coneMat)
            );
            lightNode.Transform = new Transform { Translation = basePos };

            // Direction is stored in local space (-Y = straight down).
            // The engine applies the node's world transform via TransformNormal,
            // so the actual world-space beam direction is driven entirely by the node rotation.
            lightNode.Entity.Set(
                new RangeLightComponent(RangeLightType.Spot)
                {
                    Position = Vector3.Zero,
                    Direction = -Vector3.UnitY, // local-space: straight down
                    Color = col,
                    Intensity = SpotIntensity,
                    Range = SpotRange,
                    SpotAngles = spotAngles,
                }
            );

            root.AddChild(lightNode);

            float axisAngle = (float)(rand.NextDouble() * MathF.PI * 2);
            var swingAxis = new Vector3(MathF.Cos(axisAngle), 0f, MathF.Sin(axisAngle));

            _spotLights.Add(
                new SpotLightState
                {
                    Node = lightNode,
                    BasePosition = basePos,
                    SwingPhase = (float)(rand.NextDouble() * MathF.PI * 2),
                    SwingSpeed = 0.4f + (float)rand.NextDouble() * 0.8f,
                    SwingAmplitude = new AngleSingle(
                        20f + (float)rand.NextDouble() * 35f,
                        AngleType.Degree
                    ).Radians,
                    SwingAxis = swingAxis,
                }
            );
        }
    }

    // -----------------------------------------------------------------------
    // Frame update
    // -----------------------------------------------------------------------

    public void Tick(float deltaTime)
    {
        UpdateAnimals(deltaTime);
        UpdateSpotLights(deltaTime);
    }

    // -----------------------------------------------------------------------
    // Spot-light animation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Advances the sweep phase of every spot light and writes the new direction directly into
    /// each light's <see cref="RangeLightComponent"/>, plus rotates the matching cone mesh.
    /// </summary>
    private void UpdateSpotLights(float deltaTime)
    {
        if (_spotLights.Count == 0)
            return;

        foreach (var sl in _spotLights)
        {
            sl.SwingPhase += sl.SwingSpeed * deltaTime;

            // Desired world-space beam direction: starts straight down, swept by SwingAxis
            float sweep = MathF.Sin(sl.SwingPhase) * sl.SwingAmplitude;
            var sweepRot = Quaternion.CreateFromAxisAngle(sl.SwingAxis, sweep);
            var worldDir = Vector3.Normalize(Vector3.Transform(-Vector3.UnitY, sweepRot));

            // Build a rotation that maps local -Y to worldDir.
            // RotationFromTo(from, to) = rotation taking 'from' onto 'to'.
            // We want: rotation * (-UnitY) == worldDir, so from=-UnitY, to=worldDir.
            var nodeRotation = RotationFromTo(-Vector3.UnitY, worldDir);

            ref var t = ref sl.Node.Transform;
            t.Translation = sl.BasePosition;
            t.Rotation = nodeRotation;

            // A single transform notification is enough: the engine re-derives the
            // world-space direction as TransformNormal(localDir=-UnitY, worldMatrix).
            sl.Node.NotifyTransformChanged();
        }
    }

    /// <summary>
    /// Returns the shortest-arc quaternion that rotates <paramref name="from"/> onto
    /// <paramref name="to"/>. Both vectors are assumed to be unit length.
    /// </summary>
    private static Quaternion RotationFromTo(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);

        // Vectors already aligned
        if (dot >= 1.0f - 1e-6f)
            return Quaternion.Identity;

        // Vectors are opposite: pick an arbitrary perpendicular axis
        if (dot <= -1.0f + 1e-6f)
        {
            Vector3 perp = MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            return Quaternion.CreateFromAxisAngle(
                Vector3.Normalize(Vector3.Cross(from, perp)),
                MathF.PI
            );
        }

        Vector3 axis = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(axis.X, axis.Y, axis.Z, 1.0f + dot));
    }

    // -----------------------------------------------------------------------
    // Animal animation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Updates all animals each frame by writing directly to each animal's
    /// <see cref="Node.Transform"/>.
    /// </summary>
    public void UpdateAnimals(float deltaTime)
    {
        if (_heightMap == null || _animals.Count == 0)
            return;

        foreach (var animal in _animals)
        {
            animal.WanderTimer -= deltaTime;
            if (animal.WanderTimer <= 0)
            {
                animal.Heading += (float)(_animalRng.NextDouble() - 0.5) * MathF.PI * 1.5f;
                animal.WanderTimer = (float)(_animalRng.NextDouble() * 4.0 + 1.5);
            }

            float dx = MathF.Cos(animal.Heading) * animal.Speed * deltaTime;
            float dz = MathF.Sin(animal.Heading) * animal.Speed * deltaTime;
            float newX = Math.Clamp(animal.Position.X + dx, 1f, WorldSizeX - 2f);
            float newZ = Math.Clamp(animal.Position.Z + dz, 1f, WorldSizeZ - 2f);

            if (newX <= 1f || newX >= WorldSizeX - 2f || newZ <= 1f || newZ >= WorldSizeZ - 2f)
            {
                animal.Heading += MathF.PI;
                animal.WanderTimer = 0.5f;
            }

            int ix = Math.Clamp((int)newX, 0, WorldSizeX - 1);
            int iz = Math.Clamp((int)newZ, 0, WorldSizeZ - 1);

            float targetY = _heightMap[ix, iz] + 3f;
            float newY =
                animal.Position.Y + (targetY - animal.Position.Y) * Math.Min(1f, deltaTime * 5f);

            animal.BobPhase += deltaTime * animal.Speed * 8f;
            float bob = MathF.Sin(animal.BobPhase) * 0.05f;

            animal.Position = new Vector3(newX, newY + bob, newZ);

            ref var t = ref animal.Node.Transform;
            t.Translation = animal.Position;
            t.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, animal.Heading);
            animal.Node.NotifyTransformChanged();
        }
    }

    // -----------------------------------------------------------------------
    // Animal mesh builders (Minecraft-style blocky animals)
    // -----------------------------------------------------------------------

    private static Geometry BuildCowMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        mb.AddBox(new Vector3(0, 0.5f, 0), 0.8f, 0.7f, 1.4f);
        mb.AddBox(new Vector3(0, 0.7f, 0.85f), 0.5f, 0.5f, 0.4f);
        mb.AddBox(new Vector3(-0.25f, 0.0f, 0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(0.25f, 0.0f, 0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(-0.25f, 0.0f, -0.4f), 0.2f, 0.5f, 0.2f);
        mb.AddBox(new Vector3(0.25f, 0.0f, -0.4f), 0.2f, 0.5f, 0.2f);
        var geo = mb.ToMesh().ToGeometry();
        Debug.Assert(geometryManager.AddAsync(geo, out _), "Failed to add cow geometry");
        return geo;
    }

    private static Geometry BuildPigMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        mb.AddBox(new Vector3(0, 0.35f, 0), 0.6f, 0.5f, 0.9f);
        mb.AddBox(new Vector3(0, 0.45f, 0.55f), 0.45f, 0.4f, 0.35f);
        mb.AddBox(new Vector3(0, 0.4f, 0.78f), 0.2f, 0.15f, 0.12f);
        mb.AddBox(new Vector3(-0.18f, 0.0f, 0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(0.18f, 0.0f, 0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(-0.18f, 0.0f, -0.25f), 0.15f, 0.3f, 0.15f);
        mb.AddBox(new Vector3(0.18f, 0.0f, -0.25f), 0.15f, 0.3f, 0.15f);
        var geo = mb.ToMesh().ToGeometry();
        Debug.Assert(geometryManager.AddAsync(geo, out _), "Failed to add pig geometry");
        return geo;
    }

    private static Geometry BuildChickenMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        mb.AddBox(new Vector3(0, 0.25f, 0), 0.3f, 0.3f, 0.4f);
        mb.AddBox(new Vector3(0, 0.5f, 0.2f), 0.2f, 0.2f, 0.2f);
        mb.AddBox(new Vector3(0, 0.48f, 0.35f), 0.08f, 0.06f, 0.1f);
        mb.AddBox(new Vector3(-0.06f, 0.0f, 0.0f), 0.05f, 0.2f, 0.05f);
        mb.AddBox(new Vector3(0.06f, 0.0f, 0.0f), 0.05f, 0.2f, 0.05f);
        mb.AddBox(new Vector3(0, 0.35f, -0.25f), 0.15f, 0.2f, 0.1f);
        var geo = mb.ToMesh().ToGeometry();
        Debug.Assert(geometryManager.AddAsync(geo, out _), "Failed to add chicken geometry");
        return geo;
    }

    private static Geometry BuildSheepMesh(IGeometryManager geometryManager)
    {
        var mb = new MeshBuilder(true, true, true);
        mb.AddBox(new Vector3(0, 0.5f, 0), 0.75f, 0.65f, 1.1f);
        mb.AddBox(new Vector3(0, 0.6f, 0.65f), 0.35f, 0.35f, 0.3f);
        mb.AddBox(new Vector3(-0.22f, 0.0f, 0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(0.22f, 0.0f, 0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(-0.22f, 0.0f, -0.3f), 0.15f, 0.45f, 0.15f);
        mb.AddBox(new Vector3(0.22f, 0.0f, -0.3f), 0.15f, 0.45f, 0.15f);
        var geo = mb.ToMesh().ToGeometry();
        Debug.Assert(geometryManager.AddAsync(geo, out _), "Failed to add sheep geometry");
        return geo;
    }

    // -----------------------------------------------------------------------
    // Animal helpers
    // -----------------------------------------------------------------------

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

                float n =
                    0.50f * MathF.Sin(fx * MathF.PI * 2.5f + 0.3f) * MathF.Cos(fz * MathF.PI * 2.0f)
                    + 0.25f
                        * MathF.Sin(fx * MathF.PI * 5.0f + 1.1f)
                        * MathF.Cos(fz * MathF.PI * 4.5f + 0.7f)
                    + 0.15f
                        * MathF.Sin(fx * MathF.PI * 10.0f)
                        * MathF.Cos(fz * MathF.PI * 9.0f + 1.3f);

                float normalized = Math.Clamp((n + 0.9f) / 1.8f, 0f, 1f);

                normalized = biomeMap[x, z] switch
                {
                    BiomeType.Desert => normalized * 0.45f,
                    BiomeType.Snowy => 0.35f + normalized * 0.65f,
                    BiomeType.Swamp => normalized * 0.30f,
                    _ => normalized,
                };

                map[x, z] =
                    MinTerrainHeight + (int)(normalized * (MaxTerrainHeight - MinTerrainHeight));
            }
        }
        return map;
    }

    /// <summary>
    /// Generates a 2D biome map using an independent low-frequency noise field.
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

                float t = Math.Clamp((T + 1f) * 0.5f, 0f, 1f);
                float h = Math.Clamp((H + 1f) * 0.5f, 0f, 1f);

                map[x, z] = (t, h) switch
                {
                    ( > 0.55f, _) => BiomeType.Desert,
                    ( < 0.30f, _) => BiomeType.Snowy,
                    (_, > 0.60f) => BiomeType.Swamp,
                    _ => BiomeType.Plains,
                };
            }
        }
        return map;
    }

    /// <summary>
    /// Returns the <see cref="BlockType"/> for the voxel at <c>(x, y, z)</c>.
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
        int depth = terrainHeight - y;

        if (y == 0)
            return BlockType.Bedrock;

        if (y == 1 && terrainHeight >= MinTerrainHeight + 4)
            return BlockType.Lava;

        if (depth == 0 && terrainHeight <= MinTerrainHeight)
            return biome == BiomeType.Snowy ? BlockType.Snow : BlockType.Water;

        if (depth >= 4 && (x * 7 + z * 13 + y * 3) % 17 == 0)
            return BlockType.GoldOre;

        if (depth >= 4)
            return BlockType.Stone;

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
                _ => BlockType.Dirt,
            };
        }

        // Surface (depth == 0)
        return biome switch
        {
            BiomeType.Desert => BlockType.Sand,
            BiomeType.Snowy => terrainHeight
            >= MinTerrainHeight + (MaxTerrainHeight - MinTerrainHeight) / 2
                ? BlockType.Snow
                : BlockType.Stone,
            BiomeType.Swamp => (x * 5 + z * 11) % 6 == 0 ? BlockType.Gravel : BlockType.Grass,
            _ => terrainHeight >= MinTerrainHeight + (MaxTerrainHeight - MinTerrainHeight) - 1
                ? BlockType.Wood
                : BlockType.Grass,
        };
    }

    /// <summary>Overload for API compatibility — uses Plains biome.</summary>
    public BlockType GetBlockType(int x, int y, int z, int terrainHeight, int[,] heightMap) =>
        GetBlockType(x, y, z, terrainHeight, heightMap, BiomeType.Plains);
}
