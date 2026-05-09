using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;
using HelixToolkit.Nex.Scene;

namespace SceneSamples;

/// <summary>
/// Solar system scene that uses large numbers of billboards with various
/// SDF material variants to stress-test and demonstrate the billboard system.
///
/// Each body has:
///  - A name label (one of three fonts, randomised per body)
///  - A "ring" of fact labels orbiting it at a fixed radius
///  - Moons (minor bodies) with their own billboard labels
///
/// Material variants used:
///  - "SDFFont"          – plain white SDF text
///  - "SDFFont_Outlined" – red-outlined SDF text (must be pre-registered by the caller)
///  - "SDFFont_Shadow"   – drop-shadow SDF text  (must be pre-registered by the caller)
/// </summary>
public sealed class SolarSystemScene : IScene
{
    // -----------------------------------------------------------------------
    // IScene – unused terrain properties (solar system has no terrain)
    // -----------------------------------------------------------------------
    public int WorldSizeX => 200;
    public int WorldSizeZ => 200;
    public int MaxTerrainHeight => 0;
    public int MinTerrainHeight => 0;

    // -----------------------------------------------------------------------
    // Configuration
    // -----------------------------------------------------------------------

    /// <summary>Font size used for planet name labels.</summary>
    public float PlanetLabelFontSize { get; set; } = 0.48f;

    /// <summary>Font size used for fact-ring labels around each planet.</summary>
    public float FactLabelFontSize { get; set; } = 0.35f;

    /// <summary>Font size used for moon labels.</summary>
    public float MoonLabelFontSize { get; set; } = 0.25f;

    // -----------------------------------------------------------------------
    // Planet data
    // -----------------------------------------------------------------------

    private sealed record PlanetDef(
        string Name,
        float OrbitRadius,      // from sun (world units)
        Color4 LabelColor,
        string MaterialVariant, // SDF material name
        BuildinFontAtlas Font,
        string[] Facts,         // shown in a ring around the planet
        MoonDef[] Moons,
        // ---- sphere mesh ------------------------------------------------
        float SphereRadius,
        Vector3 SphereAlbedo,
        float SphereMetallic = 0.0f,
        float SphereRoughness = 0.8f,
        Vector3 SphereEmissive = default,
        // ---- orbit ------------------------------------------------------
        float OrbitSpeed = 0.5f  // radians per second
    );

    private sealed record MoonDef(
        string Name,
        float OrbitRadius,      // from parent planet
        Color4 Color,
        BuildinFontAtlas Font,
        // ---- sphere mesh ------------------------------------------------
        float SphereRadius = 0.5f,
        Vector3 SphereAlbedo = default,
        // ---- orbit ------------------------------------------------------
        float OrbitSpeed = 2.0f  // radians per second
    );
    // -----------------------------------------------------------------------
    // Orbit runtime state
    // -----------------------------------------------------------------------

    private sealed class OrbitState(Node node, float orbitRadius, float orbitSpeed)
    {
        public Node Node { get; } = node;
        public float OrbitRadius { get; } = orbitRadius;
        public float OrbitSpeed { get; } = orbitSpeed;
        public float Angle { get; set; } = 0f;
    }

    private readonly List<OrbitState> _planetOrbits = [];
    private readonly List<(OrbitState moon, OrbitState planet)> _moonOrbits = [];

    private static readonly PlanetDef[] Planets =
    [
        new PlanetDef(
            "Sun",
            0f,
            new Color4(1.0f, 0.95f, 0.2f, 1f),
            "SDFFont",
            BuildinFontAtlas.GoogleSansRegular,
            ["G-type main-sequence star", "Age: 4.6 Gyr", "Radius: 695,700 km", "Mass: 1.989e30 kg", "Surface temp: 5,778 K"],
            [],
            SphereRadius: 5.0f,
            SphereAlbedo: new Vector3(1.0f, 0.95f, 0.2f),
            SphereMetallic: 0.0f,
            SphereRoughness: 1.0f,
            SphereEmissive: new Vector3(1.5f, 1.0f, 0.1f)
        ),
        new PlanetDef(
            "Mercury",
            20f,
            new Color4(0.75f, 0.70f, 0.65f, 1f),
            "SDFFont_Outlined",
            BuildinFontAtlas.RobotoSlabRegular,
            ["Closest to Sun", "No atmosphere", "Day: 59 Earth days", "Radius: 2,439 km"],
            [],
            SphereRadius: 0.4f,
            SphereAlbedo: new Vector3(0.5f, 0.47f, 0.44f),
            SphereMetallic: 0.1f,
            SphereRoughness: 0.9f,
            OrbitSpeed: 1.60f
        ),
        new PlanetDef(
            "Venus",
            35f,
            new Color4(1.0f, 0.85f, 0.5f, 1f),
            "SDFFont",
            BuildinFontAtlas.MichromaRegular,
            ["Hottest planet", "Retrograde rotation", "Thick CO₂ atm.", "Surface: 465 °C"],
            [],
            SphereRadius: 0.95f,
            SphereAlbedo: new Vector3(0.9f, 0.8f, 0.5f),
            SphereRoughness: 0.95f,
            OrbitSpeed: 1.17f
        ),
        new PlanetDef(
            "Earth",
            52f,
            new Color4(0.3f, 0.7f, 1.0f, 1f),
            "SDFFont_Shadow",
            BuildinFontAtlas.GoogleSansRegular,
            ["Home world", "One large moon", "Liquid water oceans", "Magnetic field", "Radius: 6,371 km"],
            [
                new MoonDef("Moon", 6f, new Color4(0.9f, 0.9f, 0.9f, 1f), BuildinFontAtlas.GoogleSansRegular),
            ],
            SphereRadius: 1.0f,
            SphereAlbedo: new Vector3(0.1f, 0.3f, 0.8f),
            OrbitSpeed: 1.00f
        ),
        new PlanetDef(
            "Mars",
            70f,
            new Color4(0.9f, 0.4f, 0.2f, 1f),
            "SDFFont_Outlined",
            BuildinFontAtlas.RobotoSlabRegular,
            ["The Red Planet", "Olympus Mons: tallest volcano", "Two small moons", "Thin CO₂ atm.", "Day: 24h 37m"],
            [
                new MoonDef("Phobos", 5f, new Color4(0.7f, 0.65f, 0.6f, 1f), BuildinFontAtlas.GoogleSansRegular),
                new MoonDef("Deimos", 8f, new Color4(0.65f, 0.60f, 0.55f, 1f), BuildinFontAtlas.RobotoSlabRegular),
            ],
            SphereRadius: 0.53f,
            SphereAlbedo: new Vector3(0.7f, 0.2f, 0.1f),
            SphereRoughness: 0.9f,
            OrbitSpeed: 0.80f
        ),
        new PlanetDef(
            "Jupiter",
            100f,
            new Color4(1.0f, 0.75f, 0.55f, 1f),
            "SDFFont",
            BuildinFontAtlas.MichromaRegular,
            ["Largest planet", "Great Red Spot", "95 known moons", "Mass: 318 Earths", "Radius: 69,911 km"],
            [
                new MoonDef("Io",       7f,  new Color4(1.0f, 0.9f, 0.3f,  1f), BuildinFontAtlas.GoogleSansRegular),
                new MoonDef("Europa",   10f, new Color4(0.8f, 0.8f, 1.0f,  1f), BuildinFontAtlas.RobotoSlabRegular),
                new MoonDef("Ganymede", 13f, new Color4(0.7f, 0.65f, 0.6f, 1f), BuildinFontAtlas.MichromaRegular),
                new MoonDef("Callisto", 17f, new Color4(0.55f, 0.5f, 0.5f, 1f), BuildinFontAtlas.GoogleSansRegular),
            ],
            SphereRadius: 3.0f,
            SphereAlbedo: new Vector3(0.9f, 0.65f, 0.45f),
            SphereRoughness: 0.85f,
            OrbitSpeed: 0.43f
        ),
        new PlanetDef(
            "Saturn",
            140f,
            new Color4(0.95f, 0.90f, 0.65f, 1f),
            "SDFFont_Shadow",
            BuildinFontAtlas.GoogleSansRegular,
            ["Has iconic rings", "Least dense planet", "145 known moons", "Radius: 58,232 km", "Wind speed: 1,800 km/h"],
            [
                new MoonDef("Titan",     9f,  new Color4(1.0f, 0.8f, 0.4f, 1f),  BuildinFontAtlas.GoogleSansRegular),
                new MoonDef("Enceladus", 12f, new Color4(0.9f, 0.95f, 1.0f, 1f), BuildinFontAtlas.RobotoSlabRegular),
                new MoonDef("Mimas",     6f,  new Color4(0.75f, 0.75f, 0.75f, 1f), BuildinFontAtlas.MichromaRegular),
            ],
            SphereRadius: 2.5f,
            SphereAlbedo: new Vector3(0.9f, 0.85f, 0.6f),
            SphereRoughness: 0.85f,
            OrbitSpeed: 0.32f
        ),
        new PlanetDef(
            "Uranus",
            175f,
            new Color4(0.5f, 0.85f, 0.95f, 1f),
            "SDFFont_Outlined",
            BuildinFontAtlas.MichromaRegular,
            ["Rotates on its side", "Ice giant", "27 known moons", "Coldest planet: -224 °C"],
            [
                new MoonDef("Miranda",  5f, new Color4(0.8f, 0.8f, 0.85f, 1f),  BuildinFontAtlas.GoogleSansRegular),
                new MoonDef("Ariel",    7f, new Color4(0.85f, 0.82f, 0.80f, 1f), BuildinFontAtlas.RobotoSlabRegular),
            ],
            SphereRadius: 1.5f,
            SphereAlbedo: new Vector3(0.4f, 0.8f, 0.9f),
            OrbitSpeed: 0.22f
        ),
        new PlanetDef(
            "Neptune",
            210f,
            new Color4(0.2f, 0.35f, 1.0f, 1f),
            "SDFFont",
            BuildinFontAtlas.RobotoSlabRegular,
            ["Strongest winds: 2,100 km/h", "Ice giant", "16 known moons", "Radius: 24,622 km"],
            [
                new MoonDef("Triton", 7f, new Color4(0.65f, 0.80f, 0.85f, 1f), BuildinFontAtlas.GoogleSansRegular),
            ],
            SphereRadius: 1.4f,
            SphereAlbedo: new Vector3(0.15f, 0.25f, 0.9f),
            OrbitSpeed: 0.18f
        ),
    ];

    // -----------------------------------------------------------------------
    // IScene
    // -----------------------------------------------------------------------

    public void RegisterMaterials() { /* No custom PBR materials needed */ }

    public void Tick(float deltaTime)
    {
        deltaTime /= 10;
        // Advance planet orbits around the Sun
        foreach (var orbit in _planetOrbits)
        {
            orbit.Angle += orbit.OrbitSpeed * deltaTime;
            orbit.Node.Transform.Translation = new Vector3(
                MathF.Cos(orbit.Angle) * orbit.OrbitRadius,
                0f,
                MathF.Sin(orbit.Angle) * orbit.OrbitRadius
            );
            orbit.Node.NotifyTransformChanged();
        }

        // Advance moon orbits around their parent planet
        foreach (var (moon, _) in _moonOrbits)
        {
            moon.Angle += moon.OrbitSpeed * deltaTime;
            moon.Node.Transform.Translation = new Vector3(
                MathF.Cos(moon.Angle) * moon.OrbitRadius,
                0.5f,
                MathF.Sin(moon.Angle) * moon.OrbitRadius
            );
            moon.Node.NotifyTransformChanged();
        }
    }

    public async Task<Node> BuildAsync(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider
    )
    {
        await Task.Yield(); // allow the task infrastructure to treat this as async

        var world = worldDataProvider.World;
        var root = new Node(world, "SolarSystem");
        var materialPool = resourceManager.PBRPropertyManager;
        var geometryManager = resourceManager.Geometries;

        // Build a single unit sphere (radius=1) shared by all bodies; each
        // node is scaled to the body's actual radius via Transform.Scale.
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 1.0f, 32, 16);
        var sphereGeo = meshBuilder.ToMesh().ToGeometry();
        var geoResult = await geometryManager.AddAsync(sphereGeo);
        System.Diagnostics.Debug.Assert(geoResult.Success, "Failed to add unit sphere geometry");

        // Build font atlases
        var fontRepo = resourceManager.FontAtlasRepository;
        var texRepo = resourceManager.TextureRepository;
        var samplerRepo = resourceManager.SamplerRepository;

        var atlases = new Dictionary<BuildinFontAtlas, SDFFontAtlas>
        {
            [BuildinFontAtlas.GoogleSansRegular] = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.GoogleSansRegular, texRepo, samplerRepo),
            [BuildinFontAtlas.RobotoSlabRegular] = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.RobotoSlabRegular, texRepo, samplerRepo),
            [BuildinFontAtlas.MichromaRegular] = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.MichromaRegular, texRepo, samplerRepo),
        };

        foreach (var planet in Planets)
        {
            var atlas = atlases[planet.Font];

            // ---- Planet group node ----------------------------------------
            var planetNode = new Node(world, planet.Name);
            var planetOrbit = new OrbitState(planetNode, planet.OrbitRadius, planet.OrbitSpeed);
            if (planet.OrbitRadius > 0f)
                _planetOrbits.Add(planetOrbit);
            planetNode.Transform.Translation = new Vector3(planet.OrbitRadius, 0f, 0f);
            root.AddChild(planetNode);

            // ---- Planet sphere mesh ---------------------------------------
            var planetMat = materialPool.Create("PBR");
            planetMat.Albedo = new Color4(planet.SphereAlbedo, 1f);
            planetMat.Metallic = planet.SphereMetallic;
            planetMat.Roughness = planet.SphereRoughness;
            planetMat.Opacity = 1.0f;
            if (planet.SphereEmissive != default)
                planetMat.Emissive = new Color4(planet.SphereEmissive, 1f);

            var sphereNode = world.CreateMeshNode(
                $"{planet.Name}_Sphere",
                new MeshComponent(sphereGeo, planetMat)
            );
            // Scale the unit sphere to the planet's visual radius
            sphereNode.Transform.Scale = new Vector3(planet.SphereRadius);
            planetNode.AddChild(sphereNode);

            // ---- Planet name label (large, above the body) ----------------
            AddLabel(
                world, planetNode,
                planet.Name,
                new Vector3(0f, 3f, 0f),
                planet.LabelColor,
                PlanetLabelFontSize,
                atlas,
                planet.MaterialVariant,
                true
            );

            // ---- Fact-ring labels (smaller, arranged in a circle) ---------
            int factCount = planet.Facts.Length;
            for (int i = 0; i < factCount; i++)
            {
                float angle = MathF.Tau * i / factCount;
                float ringR = PlanetLabelFontSize * 6f + 1f;
                var offset = new Vector3(
                    MathF.Cos(angle) * ringR,
                    MathF.Sin(angle) * ringR * 0.5f,  // slightly flatten vertically
                    MathF.Sin(angle) * ringR * 0.4f
                );
                // Alternate between the three material variants for visual variety
                string factMaterial = (i % 3) switch
                {
                    0 => "SDFFont",
                    1 => "SDFFont_Outlined",
                    _ => "SDFFont_Shadow",
                };
                // Alternate between fonts for variety
                var factAtlas = atlases[(BuildinFontAtlas)(i % 3)];
                AddLabel(
                    world, planetNode,
                    planet.Facts[i],
                    offset,
                    ColorWithAlpha(planet.LabelColor, 0.85f),
                    FactLabelFontSize,
                    factAtlas,
                    factMaterial,
                    false
                );
            }

            // ---- Moon labels ----------------------------------------------
            foreach (var moon in planet.Moons)
            {
                var moonNode = new Node(world, $"{planet.Name}_{moon.Name}");
                var moonOrbit = new OrbitState(moonNode, moon.OrbitRadius, moon.OrbitSpeed);
                _moonOrbits.Add((moonOrbit, planetOrbit));
                moonNode.Transform.Translation = new Vector3(moon.OrbitRadius, 0.5f, 0f);
                planetNode.AddChild(moonNode);

                var moonAtlas = atlases[moon.Font];

                // Moon sphere mesh
                var moonAlbedo = moon.SphereAlbedo != default
                    ? moon.SphereAlbedo
                    : new Vector3(moon.Color.Red, moon.Color.Green, moon.Color.Blue);
                var moonMat = materialPool.Create("PBR");
                moonMat.Albedo = new Color4(moonAlbedo, 1f);
                moonMat.Metallic = 0.0f;
                moonMat.Roughness = 0.9f;
                moonMat.Opacity = 1.0f;

                var moonSphereNode = world.CreateMeshNode(
                    $"{moon.Name}_Sphere",
                    new MeshComponent(sphereGeo, moonMat)
                );
                moonSphereNode.Transform.Scale = new Vector3(moon.SphereRadius);
                moonNode.AddChild(moonSphereNode);

                AddLabel(
                    world, moonNode,
                    moon.Name,
                    new Vector3(0f, 1.5f, 0f),
                    moon.Color,
                    MoonLabelFontSize,
                    moonAtlas,
                    "SDFFont",
                    true
                );

                // One extra fact label per moon (distance from parent)
                AddLabel(
                    world, moonNode,
                    $"Moon of {planet.Name}",
                    new Vector3(0f, 0.8f, 0f),
                    ColorWithAlpha(moon.Color, 0.7f),
                    MoonLabelFontSize * 0.8f,
                    moonAtlas,
                    "SDFFont_Outlined",
                    false
                );
            }
        }

        // ---- Extra stress-test: asteroid belt labels (many small ones) ----
        AddAsteroidBelt(world, root, atlases);

        return root;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void AddLabel(
        World world,
        Node parent,
        string text,
        Vector3 localOffset,
        Color4 color,
        float fontSize,
        SDFFontAtlas atlas,
        string materialName,
        bool fixedSize
    )
    {
        var comp = TextLayoutHelper.CreateTextBillboard(
            text,
            atlas,
            fontSize,
            Vector3.Zero,
            color,
            BillboardAnchor.Center,
            materialName,
            fixedSize: fixedSize
        );

        var node = new Node(world, $"Label_{text}");
        node.Transform.Translation = localOffset;
        node.Entity.Set(comp);
        parent.AddChild(node);
    }

    /// <summary>
    /// Adds ~120 asteroid labels arranged in a torus between Mars and Jupiter.
    /// Uses all three material variants and all three fonts to maximise billboard variety.
    /// </summary>
    private void AddAsteroidBelt(
        World world,
        Node root,
        Dictionary<BuildinFontAtlas, SDFFontAtlas> atlases
    )
    {
        const int count = 120;
        const float innerR = 82f;
        const float outerR = 96f;
        var rng = new Random(42);
        var fontKeys = new[] { BuildinFontAtlas.GoogleSansRegular, BuildinFontAtlas.RobotoSlabRegular, BuildinFontAtlas.MichromaRegular };
        var materials = new[] { "SDFFont", "SDFFont_Outlined", "SDFFont_Shadow" };
        var colors = new Color4[]
        {
            new(0.8f, 0.75f, 0.65f, 1f),
            new(0.7f, 0.65f, 0.55f, 1f),
            new(0.65f, 0.70f, 0.75f, 1f),
        };

        var beltNode = new Node(world, "AsteroidBelt");
        root.AddChild(beltNode);

        for (int i = 0; i < count; i++)
        {
            float angle = MathF.Tau * i / count + (float)(rng.NextDouble() * 0.15);
            float r = innerR + (float)(rng.NextDouble() * (outerR - innerR));
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            var pos = new Vector3(MathF.Cos(angle) * r, y, MathF.Sin(angle) * r);

            string name = $"Asteroid-{i + 1:D3}";
            var font = fontKeys[i % 3];
            var material = materials[i % 3];
            var color = colors[i % 3];

            AddLabel(world, beltNode, name, pos, color, MoonLabelFontSize * 0.75f, atlases[font], material, false);
        }
    }

    private static Color4 ColorWithAlpha(Color4 c, float alpha) =>
        new(c.Red, c.Green, c.Blue, alpha);
}
