using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Examples;

/// <summary>
/// Complete example demonstrating Forward+ rendering with bindless buffers.
/// </summary>
public class ForwardPlusExample
{
    private readonly IContext _context;
    private BufferResource _lightBuffer = BufferResource.Null;
    private BufferResource _lightGridBuffer = BufferResource.Null;
    private BufferResource _lightIndexBuffer = BufferResource.Null;
    private BufferResource _counterBuffer = BufferResource.Null;
    private BufferResource _modelMatrixBuffer = BufferResource.Null;
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null;
    private BufferResource _idxBuffer = BufferResource.Null;
    private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null;
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private SamplerResource _depthBufferSampler = SamplerResource.Null;
    private ForwardPlusConfig _config;
    private Light[] _lights = Array.Empty<Light>();
    private Matrix4x4[] _modelMatrices = new[] { Matrix4x4.Identity };
    private readonly Geometry _box;
    private readonly Geometry _sphere;
    private readonly PBRProperties _pbrProperties = new()
    {
        Albedo = new Vector3(1f, 1f, 1f),
        Metallic = 0.9f,
        Roughness = 0.4f,
        Ao = 1f,
    };

    private readonly Dependencies _dependencies = Dependencies.Empty;
    private readonly RenderPass _renderPass = new();
    private readonly Framebuffer _framebuffer = new();
    private TextureResource _depthBuffer = TextureResource.Null;
    private DepthState _depthState = DepthState.Default;

    public ForwardPlusExample(IContext context)
    {
        HxDebug.EnableDebugAssertions = true;
        _context = context;
        _config = ForwardPlusConfig.Default;
        var builder = new MeshBuilder(true, true, true);
        builder.AddBox(Vector3.Zero, 2, 2, 2);
        _box = builder.ToMesh().ToGeometry();
        builder = new MeshBuilder(true, true, true);
        builder.AddBox(Vector3.Zero, 2, 2, 2);
        builder.AddSphere(Vector3.Zero, 1.5f);
        _sphere = builder.ToMesh().ToGeometry();

        _renderPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
        _renderPass.Depth = new RenderPass.AttachmentDesc
        {
            ClearDepth = 1.0f,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
    }

    public void Initialize(int screenWidth, int screenHeight)
    {
        _box.UpdateBuffers(_context);
        _sphere.UpdateBuffers(_context);

        // 2. Create lights
        _lights = CreateTestLights(20); // 100 point lights
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

        _modelMatrixBuffer = _context.CreateBuffer(
            _modelMatrices,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_ModelMatrices"
        );

        _pbrPropertiesBuffer = _context.CreateBuffer(
            new[] { _pbrProperties },
            BufferUsageBits.Uniform,
            StorageType.Device,
            "ForwardPlus_PBRProperties"
        );

        _idxBuffer = _context.CreateBuffer(
            new ModelParams(),
            BufferUsageBits.Uniform,
            StorageType.Device,
            "ForwardPlus_ModelParams"
        );

        _depthBufferSampler = _context.CreateSampler(
            new SamplerStateDesc
            {
                MinFilter = SamplerFilter.Nearest,
                MagFilter = SamplerFilter.Nearest,
            }
        );
        _depthBuffer = _context.CreateTexture(
            new TextureDesc()
            {
                Type = TextureType.Texture2D,
                Format = Format.Z_F32,
                Dimensions = new Dimensions((uint)screenWidth, (uint)screenHeight, 1),
                NumLayers = 1,
                NumSamples = 1,
                Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                NumMipLevels = 1,
                Storage = StorageType.Device,
            }
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
            .WithSimpleLighting(false)
            .ConfigForwardPlus(_config);

        var shaderResult = builder.BuildMaterialPipeline(_context, "ForwardPlus_Render");

        // Create the actual render pipeline
        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = shaderResult.VertexShader,
            FragementShader = shaderResult.FragmentShader,
            DebugName = "ForwardPlus_RenderPipeline",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
        };
        pipelineDesc.Colors[0].Format = Format.BGRA_UN8;
        pipelineDesc.DepthFormat = Format.Z_F32;

        _renderPipeline = _context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_renderPipeline.Valid);
    }

    public void PreRender(ICommandBuffer cmdBuffer)
    {
        // Step 1: Reset counter
        cmdBuffer.FillBuffer(_counterBuffer, 0, sizeof(uint), 0);
        // Update model matrices if needed
        _modelMatrices[0] = Matrix4x4.CreateRotationY(
            (float)DateTime.Now.TimeOfDay.TotalSeconds * 0.5f
        );
        cmdBuffer.UpdateBuffer(_modelMatrixBuffer, _modelMatrices, (uint)_modelMatrices.Length);
    }

    public void Render(ICommandBuffer cmdBuffer, Camera camera, int screenWidth, int screenHeight)
    {
        _framebuffer.Colors[0].Texture = _context!.GetCurrentSwapchainTexture();
        _framebuffer.DepthStencil.Texture = _depthBuffer;
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
            DepthTextureIndex = _depthBuffer.Index,
            SamplerIndex = _depthBufferSampler.Index,
            LightBufferAddress = _context.GpuAddress(_lightBuffer),
            LightGridBufferAddress = _context.GpuAddress(_lightGridBuffer),
            LightIndexBufferAddress = _context.GpuAddress(_lightIndexBuffer),
            GlobalCounterBufferAddress = _context.GpuAddress(_counterBuffer),
        };
        cmdBuffer.PushConstants(cullingConstants);

        // Dispatch culling (one thread group per tile)
        cmdBuffer.DispatchThreadGroups(
            new Dimensions((uint)tileCountX, (uint)tileCountY, 1),
            Dependencies.Empty
        );

        // Step 3: Render scene with Forward+
        cmdBuffer.BeginRendering(_renderPass, _framebuffer, _dependencies);
        cmdBuffer.BindDepthState(_depthState);
        cmdBuffer.BindRenderPipeline(_renderPipeline);
        cmdBuffer.BindIndexBuffer(_sphere.IndexBuffer, IndexFormat.UI32);

        Matrix4x4.Invert(camera.View * camera.Projection, out var invViewProj);
        var renderConstants = new ForwardPlusConstants
        {
            ViewProjection = camera.View * camera.Projection,
            InverseViewProjection = invViewProj,
            CameraPosition = camera.Position,
            Time = (float)DateTime.Now.TimeOfDay.TotalSeconds,
            VertexBufferAddress = _context.GpuAddress(_sphere.VertexBuffer),
            LightBufferAddress = _context.GpuAddress(_lightBuffer),
            LightGridBufferAddress = _context.GpuAddress(_lightGridBuffer),
            LightIndexBufferAddress = _context.GpuAddress(_lightIndexBuffer),
            ModelMatrixBufferAddress = _context.GpuAddress(_modelMatrixBuffer),
            MaterialBufferAddress = _context.GpuAddress(_pbrPropertiesBuffer),
            PerModelParamsBufferAddress = _context.GpuAddress(_idxBuffer),
            LightCount = (uint)_lights.Length,
            TileSize = _config.TileSize,
            ScreenDimensions = new Vector2(screenWidth, screenHeight),
            TileCount = new Vector2(tileCountX, tileCountY),
        };

        cmdBuffer.PushConstants(renderConstants);

        // Draw without binding vertex buffers (bindless!)
        cmdBuffer.DrawIndexed((uint)_sphere.Indices.Count);
        cmdBuffer.EndRendering();
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
                Range = 40.0f,
                Color = color,
                Intensity = 5.0f,
                SpotAngles = Vector2.Zero,
            };
        }

        return lights;
    }

    public void Dispose()
    {
        _box.Dispose();
        _sphere.Dispose();
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
