using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.SDF;
using HelixToolkit.Nex.Scene;

namespace SceneSamples;

/// <summary>
/// Billboard showcase scene demonstrating every supported billboard variant:
///
///  Row 0 – Plain SDF text (no effects), all three built-in fonts
///  Row 1 – Outlined text, varying outline colours and widths
///  Row 2 – Drop-shadow text, varying shadow offsets and softness
///  Row 3 – Outline + shadow combined
///  Row 4 – Background quads with padding, anchoring variants
///  Row 5 – Fixed-size (screen-space) billboards
///  Row 6 – Axis-constrained billboards (Y-axis billboard pole)
///  Row 7 – Cull-distance demo (labels at increasing distances)
///  Row 8 – Multi-line Text
/// </summary>
public sealed class BillboardShowcaseScene : IScene
{
    // -----------------------------------------------------------------------
    // IScene
    // -----------------------------------------------------------------------
    public int WorldSizeX => 100;
    public int WorldSizeZ => 100;
    public int MaxTerrainHeight => 0;
    public int MinTerrainHeight => 0;

    // -----------------------------------------------------------------------
    // Material names
    // -----------------------------------------------------------------------
    private const string MatPlain = "SDFFont";
    private const string MatOutlineRed = "BBC_Outline_Red";
    private const string MatOutlineBlue = "BBC_Outline_Blue";
    private const string MatOutlineWide = "BBC_Outline_Wide";
    private const string MatShadow = "BBC_Shadow";
    private const string MatShadowSoft = "BBC_Shadow_Soft";
    private const string MatOutlineShadow = "BBC_Outline_Shadow";

    // Column spacing (world units)
    private const float ColSpacing = 8f;
    // Row spacing
    private const float RowSpacing = 4f;

    // -----------------------------------------------------------------------
    // RegisterMaterials – called once before BuildAsync
    // -----------------------------------------------------------------------
    public void RegisterMaterials()
    {
        // Outline – red, medium width
        SDFFontMaterialConfig.RegisterVariant(MatOutlineRed, new SDFFontMaterialConfig
        {
            OutlineColor = new Color4(1f, 0.1f, 0.1f, 1f),
            OutlineWidth = 0.12f,
        });

        // Outline – blue, medium width
        SDFFontMaterialConfig.RegisterVariant(MatOutlineBlue, new SDFFontMaterialConfig
        {
            OutlineColor = new Color4(0.1f, 0.4f, 1f, 1f),
            OutlineWidth = 0.10f,
        });

        // Outline – yellow, wide
        SDFFontMaterialConfig.RegisterVariant(MatOutlineWide, new SDFFontMaterialConfig
        {
            OutlineColor = new Color4(1f, 0.85f, 0f, 1f),
            OutlineWidth = 0.20f,
        });

        // Shadow – hard, south-east
        SDFFontMaterialConfig.RegisterVariant(MatShadow, new SDFFontMaterialConfig
        {
            ShadowColor = new Color4(0f, 0f, 0f, 0.75f),
            ShadowOffset = new Vector2(0.015f, -0.015f),
            ShadowSoftness = 0f,
        });

        // Shadow – soft, south
        SDFFontMaterialConfig.RegisterVariant(MatShadowSoft, new SDFFontMaterialConfig
        {
            ShadowColor = new Color4(0f, 0f, 0f, 0.5f),
            ShadowOffset = new Vector2(0f, -0.02f),
            ShadowSoftness = 0.08f,
        });

        // Outline + shadow combined
        SDFFontMaterialConfig.RegisterVariant(MatOutlineShadow, new SDFFontMaterialConfig
        {
            OutlineColor = new Color4(0.1f, 0.6f, 0.1f, 1f),
            OutlineWidth = 0.10f,
            ShadowColor = new Color4(0f, 0f, 0f, 0.6f),
            ShadowOffset = new Vector2(0.012f, -0.012f),
            ShadowSoftness = 0.04f,
        });
    }

    // -----------------------------------------------------------------------
    // No per-frame animation
    // -----------------------------------------------------------------------
    public void Tick(float deltaTime) { }

    // -----------------------------------------------------------------------
    // BuildAsync
    // -----------------------------------------------------------------------
    public async Task<Node> BuildAsync(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider)
    {
        await Task.Yield();

        var world = worldDataProvider.World;
        var root = new Node(world, "BillboardShowcase");

        var fontRepo = resourceManager.FontAtlasRepository;
        var texRepo = resourceManager.TextureRepository;
        var samplerRepo = resourceManager.SamplerRepository;

        var google = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.GoogleSansRegular, texRepo, samplerRepo);
        var roboto = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.RobotoSlabRegular, texRepo, samplerRepo);
        var michroma = fontRepo.GetOrCreateBuiltIn(BuildinFontAtlas.MichromaRegular, texRepo, samplerRepo);

        float z = 0f;

        // -------------------------------------------------------------------
        // Row 0 – Plain SDF, three fonts
        // -------------------------------------------------------------------
        z = 0f;
        AddRow(world, root, "Row 0 – Plain SDF", z, google);
        AddBillboard(world, root, "Google Sans", new Vector3(-ColSpacing, 2f, z), new Color4(1f, 1f, 1f, 1f), null, 1.8f, google, MatPlain, false);
        AddBillboard(world, root, "Roboto Slab", new Vector3(0f, 2f, z), new Color4(0.9f, 0.85f, 1f, 1f), null, 1.8f, roboto, MatPlain, false);
        AddBillboard(world, root, "Michroma", new Vector3(ColSpacing, 2f, z), new Color4(0.6f, 1f, 0.8f, 1f), null, 1.8f, michroma, MatPlain, false);

        // -------------------------------------------------------------------
        // Row 1 – Outlined variants
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 1 – Outlined", z, roboto);
        AddBillboard(world, root, "Red Outline", new Vector3(-ColSpacing, 2f, z), new Color4(1f, 1f, 1f, 1f), null, 1.8f, google, MatOutlineRed, false);
        AddBillboard(world, root, "Blue Outline", new Vector3(0f, 2f, z), new Color4(1f, 1f, 1f, 1f), null, 1.8f, roboto, MatOutlineBlue, false);
        AddBillboard(world, root, "Wide Yellow", new Vector3(ColSpacing, 2f, z), new Color4(0.2f, 0.2f, 0.2f, 1f), null, 1.8f, michroma, MatOutlineWide, false);

        // -------------------------------------------------------------------
        // Row 2 – Drop-shadow variants
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 2 – Drop Shadow", z, google);
        AddBillboard(world, root, "Hard Shadow", new Vector3(-ColSpacing, 2f, z), new Color4(1f, 0.9f, 0.5f, 1f), null, 1.8f, google, MatShadow, false);
        AddBillboard(world, root, "Soft Shadow", new Vector3(0f, 2f, z), new Color4(0.5f, 0.9f, 1f, 1f), null, 1.8f, roboto, MatShadowSoft, false);
        AddBillboard(world, root, "Shadow Michroma", new Vector3(ColSpacing, 2f, z), new Color4(1f, 0.7f, 0.3f, 1f), null, 1.8f, michroma, MatShadow, false);

        // -------------------------------------------------------------------
        // Row 3 – Outline + shadow combined
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 3 – Outline + Shadow", z, michroma);
        AddBillboard(world, root, "Green Outline\n+ Shadow", new Vector3(-ColSpacing, 2f, z), new Color4(1f, 1f, 1f, 1f), null, 1.8f, google, MatOutlineShadow, false);
        AddBillboard(world, root, "Combined\nRoboto", new Vector3(0f, 2f, z), new Color4(0.9f, 0.95f, 1f, 1f), null, 1.8f, roboto, MatOutlineShadow, false);
        AddBillboard(world, root, "Michroma\nCombined", new Vector3(ColSpacing, 2f, z), new Color4(1f, 0.95f, 0.8f, 1f), null, 1.8f, michroma, MatOutlineShadow, false);

        // -------------------------------------------------------------------
        // Row 4 – Background quads, anchor variants
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 4 – Backgrounds & Anchors", z, roboto);
        // Center anchor, blue background
        AddBillboardWithBackground(world, root, "Center\nAnchor",
            new Vector3(-ColSpacing, 2f, z),
            new Color4(1f, 1f, 1f, 1f), new Color4(0.1f, 0.2f, 0.6f, 0.85f),
            1.6f, google, MatPlain, BillboardAnchor.Center, padding: new Vector4(0.3f, 0.15f, 0.3f, 0.15f));
        // TopLeft anchor, dark green background
        AddBillboardWithBackground(world, root, "TopLeft\nAnchor",
            new Vector3(0f, 2f, z),
            new Color4(0.9f, 1f, 0.8f, 1f), new Color4(0.05f, 0.3f, 0.05f, 0.85f),
            1.6f, roboto, MatPlain, BillboardAnchor.TopLeft, padding: new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
        // BottomRight anchor, dark background
        AddBillboardWithBackground(world, root, "BottomRight\nAnchor",
            new Vector3(ColSpacing, 2f, z),
            new Color4(1f, 0.9f, 0.7f, 1f), new Color4(0.4f, 0.1f, 0f, 0.85f),
            1.6f, michroma, MatPlain, BillboardAnchor.BottomRight, padding: new Vector4(0.2f, 0.2f, 0.2f, 0.2f));

        // -------------------------------------------------------------------
        // Row 5 – Fixed-size (screen-space pixels) billboards
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 5 – Fixed Screen-Space Size", z, google);
        AddBillboard(world, root, "Fixed 24px", new Vector3(-ColSpacing, 2f, z), new Color4(1f, 0.8f, 0.8f, 1f), null, 24f, google, MatPlain, fixedSize: true);
        AddBillboard(world, root, "Fixed 32px", new Vector3(0f, 2f, z), new Color4(0.8f, 1f, 0.8f, 1f), null, 32f, roboto, MatPlain, fixedSize: true);
        AddBillboard(world, root, "Fixed 40px", new Vector3(ColSpacing, 2f, z), new Color4(0.8f, 0.8f, 1f, 1f), null, 40f, michroma, MatPlain, fixedSize: true);

        // -------------------------------------------------------------------
        // Row 6 – Axis-constrained billboards
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 6 – Axis-Constrained (Y-axis)", z, google);
        for (int i = 0; i < 5; i++)
        {
            float x = (i - 2) * ColSpacing * 0.5f;
            var comp = TextLayoutHelper.CreateTextBillboard(
                $"Pole {i + 1}",
                google,
                1.4f,
                new Color4(0.9f, 0.7f + i * 0.05f, 0.3f, 1f),
                anchor: BillboardAnchor.Center,
                materialName: MatPlain,
                fixedSize: false);
            comp.AxisConstrained = true;
            comp.ConstraintAxis = Vector3.UnitY;
            var node = new BillboardNode(world, $"AxisBB_{i}", ref comp);
            node.Transform.Translation = new Vector3(x, 2.5f, z);
            root.AddChild(node);
        }

        // -------------------------------------------------------------------
        // Row 7 – Cull-distance demo (labels disappear as camera moves away)
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 7 – Cull Distance", z, roboto);
        float[] cullDists = [0f, 20f, 40f, 60f];
        for (int i = 0; i < cullDists.Length; i++)
        {
            float x = (i - 1.5f) * ColSpacing * 0.75f;
            string lbl = cullDists[i] == 0f ? "No Cull" : $"Cull @ {cullDists[i]:0}u";
            AddBillboard(world, root, lbl,
                new Vector3(x, 2f, z),
                new Color4(0.7f + i * 0.1f, 0.9f, 0.6f, 1f),
                null, 1.5f, roboto, MatPlain, false,
                cullDistance: cullDists[i]);
        }

        // -------------------------------------------------------------------
        // Row 8 – Multi-line text with various fonts
        // -------------------------------------------------------------------
        z -= RowSpacing;
        AddRow(world, root, "Row 8 – Multi-line Text", z, michroma);
        AddBillboard(world, root, "Line One\nLine Two\nLine Three",
            new Vector3(-ColSpacing, 2f, z), new Color4(1f, 1f, 0.7f, 1f), null, 1.4f, google, MatPlain, false);
        AddBillboard(world, root, "Hello\nWorld\n!",
            new Vector3(0f, 2f, z), new Color4(0.7f, 1f, 1f, 1f), null, 1.4f, roboto, MatShadow, false);
        AddBillboard(world, root, "HELIX\nTOOLKIT\nNEX",
            new Vector3(ColSpacing, 2f, z), new Color4(1f, 0.6f, 0.9f, 1f), null, 1.4f, michroma, MatOutlineRed, false);

        return root;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Adds a small row-header label.</summary>
    private static void AddRow(World world, Node root, string title, float z, SDFFontAtlas atlas)
    {
        var comp = TextLayoutHelper.CreateTextBillboard(
            title,
            atlas,
            1.0f,
            new Color4(0.55f, 0.55f, 0.55f, 1f),
            anchor: BillboardAnchor.CenterLeft,
            materialName: MatPlain,
            fixedSize: false);
        var node = new BillboardNode(world, $"Header_{title}", ref comp);
        node.Transform.Translation = new Vector3(-ColSpacing * 2f, 0.5f, z);
        root.AddChild(node);
    }

    private static void AddBillboard(
        World world,
        Node root,
        string text,
        Vector3 position,
        Color4 color,
        Color4? background,
        float fontSize,
        SDFFontAtlas atlas,
        string materialName,
        bool fixedSize,
        float cullDistance = 0f)
    {
        var comp = TextLayoutHelper.CreateTextBillboard(
            text,
            atlas,
            fontSize,
            color,
            background,
            BillboardAnchor.Center,
            materialName: materialName,
            fixedSize: fixedSize,
            cullDistance: cullDistance);
        var node = new BillboardNode(world, $"BB_{text}", ref comp);
        node.Transform.Translation = position;
        root.AddChild(node);
    }

    private static void AddBillboardWithBackground(
        World world,
        Node root,
        string text,
        Vector3 position,
        Color4 color,
        Color4 background,
        float fontSize,
        SDFFontAtlas atlas,
        string materialName,
        BillboardAnchor anchor,
        Vector4 padding = default)
    {
        var comp = TextLayoutHelper.CreateTextBillboard(
            text,
            atlas,
            fontSize,
            color,
            background,
            anchor,
            padding: padding,
            materialName: materialName,
            fixedSize: false);
        var node = new BillboardNode(world, $"BB_BG_{text}", ref comp);
        node.Transform.Translation = position;
        root.AddChild(node);
    }
}
