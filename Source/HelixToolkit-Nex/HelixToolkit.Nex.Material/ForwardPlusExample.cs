using HelixToolkit.Nex.Material;

namespace HelixToolkit.Nex.Examples;

/// <summary>
/// Complete example demonstrating Forward+ rendering with bindless buffers.
/// </summary>
public class ForwardPlusExample
{
    private readonly IContext _context;
    private BufferResource _vertexBuffer = BufferResource.Null;
    private BufferResource _lightBuffer = BufferResource.Null;
    private BufferResource _lightGridBuffer = BufferResource.Null;
    private BufferResource _lightIndexBuffer = BufferResource.Null;
    private BufferResource _counterBuffer = BufferResource.Null;
    private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null;
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private ForwardPlusConfig _config;
    private Light[] _lights = Array.Empty<Light>();

    public ForwardPlusExample(IContext context)
    {
        _context = context;
        _config = ForwardPlusConfig.Default;
    }

    public void Initialize(int screenWidth, int screenHeight)
    {
        // 1. Create test scene with vertices
        var vertices = CreateTestCube();
        _vertexBuffer = _context.CreateBuffer(
            vertices,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_VertexBuffer"
        );

        // 2. Create lights
        _lights = CreateTestLights(100); // 100 point lights
        _lightBuffer = _context.CreateBuffer(
            _lights,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_LightBuffer"
        );

        // 3. Create light grid buffers
        var tileCountX = (screenWidth + _config.TileSize - 1) / _config.TileSize;
        var tileCountY = (screenHeight + _config.TileSize - 1) / _config.TileSize;
        var totalTiles = tileCountX * tileCountY;

        _lightGridBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * LightGridTile.SizeInBytes),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightGrid"
        );

        _lightIndexBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * _config.MaxLightsPerTile * sizeof(uint)),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightIndices"
        );

        _counterBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = sizeof(uint),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_Counter"
        );

        // 4. Create light culling compute pipeline
        var cullingShader = ForwardPlusLightCulling.GenerateLightCullingComputeShader(_config);
        var cullingModule = _context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "ForwardPlus_CullingCompute"
        );
        _cullingPipeline = _context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );

        // 5. Create render pipeline with Forward+
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithBindlessVertices(true)
            .WithForwardPlus(true, _config);

        var shaderResult = builder.BuildMaterialPipeline(_context, "ForwardPlus_Render");

        // Create the actual render pipeline
        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = shaderResult.VertexShader,
            FragementShader = shaderResult.FragmentShader,
            DebugName = "ForwardPlus_RenderPipeline",
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        _renderPipeline = _context.CreateRenderPipeline(pipelineDesc);
    }

    public void Render(ICommandBuffer cmdBuffer, Camera camera, int screenWidth, int screenHeight)
    {
        // Step 1: Reset counter
        cmdBuffer.FillBuffer(_counterBuffer, 0, sizeof(uint), 0);

        // Step 2: Run light culling compute shader
        var tileCountX = (screenWidth + (int)_config.TileSize - 1) / (int)_config.TileSize;
        var tileCountY = (screenHeight + (int)_config.TileSize - 1) / (int)_config.TileSize;

        cmdBuffer.BindComputePipeline(_cullingPipeline);

        // Push constants for culling
        Matrix4x4.Invert(camera.Projection, out var invProj);
        var cullingConstants = new LightCullingConstants
        {
            InverseProjection = invProj,
            ScreenDimensions = new Vector2(screenWidth, screenHeight),
            TileCount = new Vector2(tileCountX, tileCountY),
            LightCount = (uint)_lights.Length,
            ZNear = camera.NearPlane,
            ZFar = camera.FarPlane,
        };
        cmdBuffer.PushConstants(cullingConstants);

        // Dispatch culling (one thread group per tile)
        cmdBuffer.DispatchThreadGroups(
            new Dimensions((uint)tileCountX, (uint)tileCountY, 1),
            Dependencies.Empty
        );

        // Step 3: Render scene with Forward+
        cmdBuffer.BindRenderPipeline(_renderPipeline);

        Matrix4x4.Invert(camera.View * camera.Projection, out var invViewProj);
        var renderConstants = new ForwardPlusConstants
        {
            ViewProjection = camera.View * camera.Projection,
            InverseViewProjection = invViewProj,
            CameraPosition = camera.Position,
            Time = (float)DateTime.Now.TimeOfDay.TotalSeconds,
            VertexBufferAddress = (uint)_context.GpuAddress(_vertexBuffer),
            LightBufferAddress = (uint)_context.GpuAddress(_lightBuffer),
            LightCount = (uint)_lights.Length,
            TileSize = _config.TileSize,
            ScreenDimensions = new Vector2(screenWidth, screenHeight),
            TileCount = new Vector2(tileCountX, tileCountY),
        };

        cmdBuffer.PushConstants(renderConstants);

        // Draw without binding vertex buffers (bindless!)
        cmdBuffer.Draw(36); // Cube has 36 vertices
    }

    private Vertex[] CreateTestCube()
    {
        // Simple cube vertices for demonstration
        var vertices = new[]
        {
            // Front face
            new Vertex
            {
                Position = new(-1, -1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(0, 0),
                Tangent = new(1, 0, 0, 1),
            },
            new Vertex
            {
                Position = new(1, -1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(1, 0),
                Tangent = new(1, 0, 0, 1),
            },
            new Vertex
            {
                Position = new(1, 1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(1, 1),
                Tangent = new(1, 0, 0, 1),
            },
            new Vertex
            {
                Position = new(1, 1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(1, 1),
                Tangent = new(1, 0, 0, 1),
            },
            new Vertex
            {
                Position = new(-1, 1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(0, 1),
                Tangent = new(1, 0, 0, 1),
            },
            new Vertex
            {
                Position = new(-1, -1, 1),
                Normal = new(0, 0, 1),
                TexCoord = new(0, 0),
                Tangent = new(1, 0, 0, 1),
            },
            // ... additional faces omitted for brevity
        };
        return vertices;
    }

    private Light[] CreateTestLights(int count)
    {
        var lights = new Light[count];
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var position = new Vector3(
                (float)(random.NextDouble() * 40 - 20),
                (float)(random.NextDouble() * 20),
                (float)(random.NextDouble() * 40 - 20)
            );

            var color = new Vector3(
                (float)random.NextDouble(),
                (float)random.NextDouble(),
                (float)random.NextDouble()
            );
            color = Vector3.Normalize(color);

            lights[i] = new Light
            {
                Position = position,
                Type = 1, // Point light
                Direction = Vector3.Zero,
                Range = 10.0f,
                Color = color,
                Intensity = 5.0f,
                InnerConeAngle = 0,
                OuterConeAngle = 0,
            };
        }

        return lights;
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _lightBuffer.Dispose();
        _lightGridBuffer.Dispose();
        _lightIndexBuffer.Dispose();
        _counterBuffer.Dispose();
        _renderPipeline.Dispose();
        _cullingPipeline.Dispose();
    }
}

/// <summary>
/// Simple camera structure for the example.
/// </summary>
public struct Camera
{
    public Vector3 Position;
    public Matrix4x4 View;
    public Matrix4x4 Projection;
    public float NearPlane;
    public float FarPlane;
}

public struct LightCullingConstants
{
    public Matrix4x4 InverseProjection;
    public Vector2 ScreenDimensions;
    public Vector2 TileCount;
    public uint LightCount;
    public float ZNear;
    public float ZFar;
}
