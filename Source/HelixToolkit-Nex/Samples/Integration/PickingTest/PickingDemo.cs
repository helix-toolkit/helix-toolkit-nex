using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using Microsoft.Extensions.Logging;

/// <summary>
/// Picking demo: creates a large mesh (~1 million triangles), picks a triangle on click,
/// and displays the selected triangle as a dynamic yellow overlay mesh.
/// </summary>
internal sealed class PickingDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<PickingDemo>();

    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private Node? _root;

    // Camera
    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;

    // The large mesh geometry (kept to read triangle vertices on pick)
    private Geometry? _largeMeshGeometry;

    // Dynamic highlight triangle
    private MeshNode? _highlightNode;
    private Geometry? _highlightGeometry;
    private Entity _pickedEntity = Entity.Null;

    // Point cloud
    private Geometry? _pointCloudGeometry;

    // Dynamic highlight point (single red point)
    private PointCloudNode? _highlightPointNode;
    private Geometry? _highlightPointGeometry;

    private Node? _lightNode;

    public PickingDemo(IContext context)
    {
        _context = context;
    }

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera
        {
            Position = new Vector3(0, 5, -30),
            Target = Vector3.Zero,
            FarPlane = 500,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes()
            .WithPostEffects(effects =>
            {
                effects.AddEffect(new Smaa());
                effects.AddEffect(new WireframePostEffect());
                effects.AddEffect(new ToneMapping());
                effects.AddEffect(new ShowFPS());
            })
            .AddRenderToFinal()
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        BuildScene();
    }

    private void BuildScene()
    {
        var geometryManager = _engine!.ResourceManager.Geometries;
        var materialPool = _engine.ResourceManager.PBRPropertyManager;
        var world = _worldDataProvider!.World;

        _root = new Node(world, "PickingRoot");

        // --- Build a large mesh with ~1 million triangles ---
        // Use a high-res sphere: thetaDiv=1002, phiDiv=1002 gives ~1M triangles
        // Each sphere ring: thetaDiv * 2 triangles, phiDiv rings => ~thetaDiv * phiDiv * 2 triangles
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 10f, 128, 128);
        meshBuilder.AddTorus(20, 2, 64, 64);
        var meshGeom3D = meshBuilder.ToMesh();
        _logger.LogInformation(
            "Large mesh: {Positions} vertices, {Triangles} triangles",
            meshGeom3D.Positions.Count,
            meshGeom3D.TriangleIndices.Count / 3
        );

        _largeMeshGeometry = meshGeom3D.ToGeometry();
        bool succ = geometryManager.Add(_largeMeshGeometry, out _);
        Debug.Assert(succ, "Failed to add large mesh geometry");

        // Material: grey PBR
        var greyMaterial = materialPool.Create("PBR");
        greyMaterial.Properties.Albedo = new Vector3(0.6f, 0.6f, 0.6f);
        greyMaterial.Properties.Metallic = 0.3f;
        greyMaterial.Properties.Roughness = 0.5f;
        greyMaterial.Properties.Ao = 1.0f;
        greyMaterial.Properties.Opacity = 1.0f;

        var meshNode = new MeshNode(world, "LargeSphere")
        {
            Geometry = _largeMeshGeometry,
            MaterialProperties = greyMaterial,
        };
        meshNode.Entity.Set(new WireframePostEffect.WireframeComponent
        {
            Color = Color.Blue,
        });
        _root.AddChild(meshNode);

        // --- Highlight triangle (dynamic, initially empty) ---
        _highlightGeometry = new Geometry(isDynamic: true);
        // Initialize with a degenerate triangle so the geometry is valid
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.Indices.Add(0);
        _highlightGeometry.Indices.Add(1);
        _highlightGeometry.Indices.Add(2);
        _highlightGeometry.UpdateBounds();
        succ = geometryManager.Add(_highlightGeometry, out _);
        Debug.Assert(succ, "Failed to add highlight geometry");

        // Yellow unlit material for highlight
        var selectedMaterial = materialPool.Create("Unlit");
        selectedMaterial.Properties.Albedo = new Vector3(1.0f, 0.0f, 0.0f);
        selectedMaterial.Properties.Opacity = 1.0f;

        _highlightNode = new MeshNode(world, "HighlightTriangle")
        {
            Geometry = _highlightGeometry,
            MaterialProperties = selectedMaterial,
            Cullable = false,
        };
        _root.AddChild(_highlightNode);

        // --- Point cloud: random sphere of points ---
        _pointCloudGeometry = GeneratePointCloudSphere(10_000, 12f, new Vector3(25, 0, 0));
        succ = geometryManager.Add(_pointCloudGeometry, out _);
        Debug.Assert(succ, "Failed to add point cloud geometry");

        var pointCloudNode = new PointCloudNode(world, "PointCloud")
        {
            Geometry = _pointCloudGeometry,
            Size = 0.15f,
            Hitable = true,
            Color = new Color4(0.2f, 0.6f, 1.0f, 1.0f),
        };
        _root.AddChild(pointCloudNode);

        // --- Highlight point (dynamic single red point, initially hidden at origin) ---
        _highlightPointGeometry = new Geometry(Topology.Point, isDynamic: true);
        _highlightPointGeometry.Vertices.Add(new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, 1));
        _highlightPointGeometry.VertexColors.Add(new Vector4(1, 0, 0, 1));
        _highlightPointGeometry.UpdateBounds();
        succ = geometryManager.Add(_highlightPointGeometry, out _);
        Debug.Assert(succ, "Failed to add highlight point geometry");

        _highlightPointNode = new PointCloudNode(world, "HighlightPoint")
        {
            Geometry = _highlightPointGeometry,
            Size = 0.4f,
            Color = new Color4(1f, 0f, 0f, 1f),
        };
        _root.AddChild(_highlightPointNode);

        // --- Add a directional light ---
        _lightNode = new Node(world, "Sun");
        _lightNode.Entity.Set(new DirectionalLightComponent
        {
            Color = new Color(1.0f, 1.0f, 1.0f),
            Intensity = 2.0f,
            Direction = Vector3.Normalize(new Vector3(0.3f, -1.0f, 0.5f)),
        });
        _root.AddChild(_lightNode);
    }

    public void Render(int width, int height)
    {
        _lightNode!.Entity.Update<DirectionalLightComponent>(light =>
        {
            light.Direction = _camera.LookDir;
            return light;
        });
        var aspectRatio = (float)width / height;
        _renderContext!.WindowSize = new Size(width, height);
        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _engine!.Render(_renderContext, _worldDataProvider!);
    }

    public void OnMouseDown(int button, float x, float y)
    {
        if (_orbitController is null)
            return;
        if (button == 1) // right = rotate
            _orbitController.OnRotateBegin(x, y);
        else if (button == 2) // middle = pan
            _orbitController.OnPanBegin(x, y);
    }

    public void OnMouseMove(float x, float y, bool isRotating, bool isPanning)
    {
        if (_orbitController is null)
            return;
        if (isRotating)
            _orbitController.OnRotateDelta(x, y);
        if (isPanning)
            _orbitController.OnPanDelta(x, y);
    }

    public void OnMouseWheel(float delta)
    {
        _orbitController?.OnZoomDelta(delta);
    }

    public void Pick(int x, int y)
    {
        if (
            !_renderContext!.TryPick(
                x,
                y,
                out var worldId,
                out var entityId,
                out var instanceIdx,
                out var primitiveId
            )
        )
        {
            _logger.LogInformation("No entity picked at ({X}, {Y})", x, y);
            return;
        }

        _logger.LogInformation(
            "Picked entity {EntityId}, instance {Instance}, primitive {Primitive}",
            entityId,
            instanceIdx,
            primitiveId
        );

        // Get the picked entity to find its geometry
        var pickedEntity = _worldDataProvider!.World.GetEntity((int)entityId);

        // --- Point cloud picking ---
        if (pickedEntity.Has<PointCloudComponent>())
        {
            var pointComp = pickedEntity.Get<PointCloudComponent>();
            var pointGeo = pointComp.Geometry;
            if (pointGeo is not null && primitiveId < pointGeo.Vertices.Count)
            {
                var pointPos = pointGeo.Vertices[(int)primitiveId];
                _logger.LogInformation("Picked point {Id} at ({Pos})", primitiveId, pointPos);

                // Update highlight point position
                _highlightPointGeometry!.Vertices[0] = pointPos;
                _highlightPointGeometry.VertexColors[0] = new Vector4(1, 0, 0, 1);
                _highlightPointGeometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
                _highlightPointGeometry.UpdateBounds();
            }
            return;
        }

        // --- Mesh picking ---
        if (pickedEntity.Has<MeshComponent>())
        {
            var meshComp = pickedEntity.Get<MeshComponent>();
            var geometry = meshComp.Geometry;
            if (geometry is null)
            {
                return;
            }

            // primitiveId is the triangle index; each triangle has 3 indices
            uint baseIndex = primitiveId * 3;
            if (baseIndex + 2 >= geometry.Indices.Count)
            {
                _logger.LogWarning("Primitive ID {Id} out of range", primitiveId);
                return;
            }

            uint i0 = geometry.Indices[(int)baseIndex];
            uint i1 = geometry.Indices[(int)baseIndex + 1];
            uint i2 = geometry.Indices[(int)baseIndex + 2];

            var v0 = geometry.Vertices[(int)i0];
            var v1 = geometry.Vertices[(int)i1];
            var v2 = geometry.Vertices[(int)i2];

            // Compute triangle normal
            var p0 = new Vector3(v0.X, v0.Y, v0.Z);
            var p1 = new Vector3(v1.X, v1.Y, v1.Z);
            var p2 = new Vector3(v2.X, v2.Y, v2.Z);
            var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

            // Offset slightly along normal to prevent z-fighting
            var offset = normal * 0.002f;
            var ov0 = p0 + offset;
            var ov1 = p1 + offset;
            var ov2 = p2 + offset;

            // Update the highlight geometry
            _highlightGeometry!.Vertices[0] = new Vector4(ov0, 1);
            _highlightGeometry.Vertices[1] = new Vector4(ov1, 1);
            _highlightGeometry.Vertices[2] = new Vector4(ov2, 1);
            _highlightGeometry.VertexProps[0] = new VertexProperties { Normal = normal };
            _highlightGeometry.VertexProps[1] = new VertexProperties { Normal = normal };
            _highlightGeometry.VertexProps[2] = new VertexProperties { Normal = normal };
            _highlightGeometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexProp);
            _highlightGeometry.UpdateBounds();

            _logger.LogInformation(
                "Highlighted triangle: ({V0}), ({V1}), ({V2})",
                ov0, ov1, ov2
            );
        }
    }

    private static Geometry GeneratePointCloudSphere(int count, float radius, Vector3 center)
    {
        var geo = new Geometry(Topology.Point);
        geo.Vertices.Capacity = count;
        geo.VertexColors.Capacity = count;
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            Vector3 p;
            do
            {
                p = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)
                );
            } while (p.LengthSquared() > 1f || p.LengthSquared() < 0.001f);

            p = Vector3.Normalize(p) * radius;
            float brightness = 0.5f + 0.5f * (p.Y / radius);
            geo.Vertices.Add((center + p).ToVector4(1));
            geo.VertexColors.Add(new Vector4(brightness, brightness, 1f, 1f));
        }
        geo.UpdateBounds();
        return geo;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _worldDataProvider?.Dispose();
        _renderContext?.Teardown();
        _engine?.Dispose();
    }
}
