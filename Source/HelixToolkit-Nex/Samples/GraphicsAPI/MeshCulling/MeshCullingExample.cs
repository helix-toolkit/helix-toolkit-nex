using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;

namespace MeshCulling;

/// <summary>
/// This example demonstrates Compute-Shader-based frustum culling for multiple distinct draw commands.
///
/// Key Concepts:
/// 1. **Multi-Draw Indirect**: Unlike instancing where one command draws many copies, here we have many unique draw commands (different meshes/parameters).
/// 2. **Indirect Count**: The number of draw commands to execute is non-deterministic (depends on visibility). We use `DrawIndexedIndirectCount`
///    which reads the actual draw count from a GPU buffer (`_visibleCountBuffer`).
/// 3. **Compute Culling**: The CS checks visibility for each object and, if visible, appends the corresponding `DrawIndexedIndirectCommand`
///    to the output buffer (`_culledDrawCmdsBuffer`) and increments the count.
///
/// Functional steps:
/// 1. Setup scene: Create many objects, each with its own Draw Command.
/// 2. Compute Shader:
///    - For each object, check bounds vs frustum.
///    - If visible, copy its Draw Command to the append buffer.
///    - Atomically increment the visible count.
/// 3. Render Pass:
///    - Execute `DrawIndexedIndirectCount`.
///    - GPU reads the count from `_visibleCountBuffer` and executes that many commands from `_culledDrawCmdsBuffer`.
/// </summary>
internal class MeshCullingExample : IDisposable
{
    #region 1. Fields and Resources
    private readonly IContext _context;
    private bool _disposedValue;

    // -- Scene Data --
    private Geometry? _boxMesh;
    private Geometry? _sphereMesh;

    // CPU-side data
    private readonly FastList<PBRProperties> _pBRProperties = [];
    private readonly FastList<uint> _meshIds = [];

    // Arrays for data management
    private readonly int _instanceCount = 10000;
    private readonly FastList<MeshDraw> _meshDraws = [];
    private readonly FastList<Geometry> _meshes = [];
    private readonly FastList<MeshInfo> _meshInfos = [];

    // -- GPU Buffers --
    private BufferResource _cullConstBuffer = BufferResource.Null; // Uniforms for culling
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null; // Materials
    private BufferResource _meshInfoBuffer = BufferResource.Null; // MeshInfos
    private BufferResource _meshDrawBuffer = BufferResource.Null; // MeshDraw structs
    private BufferResource _indexBuffer = BufferResource.Null; // Geometry indices

    // Rendering resources
    private BufferResource _fpConstBuffer = BufferResource.Null;
    private TextureResource _depthBuffer = TextureResource.Null;
    private BufferResource _directionalLightBuffer = BufferResource.Null;

    // -- Pipelines --
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private RenderPipelineResource _unlitRenderPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _pbrRenderPipeline = RenderPipelineResource.Null;

    // -- State & Helpers --
    private DepthState _depthState = DepthState.DefaultReversedZ;
    private CullingConstants _cullConst = new();
    private readonly Camera _camera = new()
    {
        Position = new Vector3(0, 0, -50),
        Up = Vector3.UnitY,
    };
    private Vector3 _initialCameraPosition;
    private readonly long _startTimestamp;
    private readonly RenderPass _renderPass = new RenderPass();
    private readonly Framebuffer _frameBuffer = new Framebuffer();
    private readonly Dependencies _renderDependencies = new Dependencies();
    #endregion

    #region 2. Constructor & Initialization
    public MeshCullingExample(IContext context)
    {
        _context = context;
        _initialCameraPosition = _camera.Position;
        _startTimestamp = Stopwatch.GetTimestamp();
        CreateMeshes();
    }

    /// <summary>
    /// Initializes GPU resources, buffers, and pipelines.
    /// </summary>
    public void Initialize(int width, int height)
    {
        // 2.1 Create GPU Buffers to hold scene data
        _cullConstBuffer = _context.CreateBuffer(
            _cullConst,
            BufferUsageBits.Storage,
            StorageType.Device,
            "CullingConstantBuffer"
        );
        _pbrPropertiesBuffer = _context.CreateBuffer(
            _pBRProperties,
            BufferUsageBits.Storage,
            StorageType.Device,
            "PBRPropertiesBuffer"
        );
        _meshInfoBuffer = _context.CreateBuffer(
            _meshInfos,
            BufferUsageBits.Storage,
            StorageType.Device,
            "MeshInfoBuffer"
        );

        _meshDrawBuffer = _context.CreateBuffer(
            _meshDraws,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            StorageType.Device,
            "MeshDrawBuffer"
        );

        var indices = new FastList<uint>(_meshes[0].Indices);
        indices.AddRange(_meshes[1].Indices);
        _indexBuffer = _context.CreateBuffer(
            indices,
            BufferUsageBits.Index,
            StorageType.Device,
            "StaticGeoIndexBuf"
        );

        // 2.3 Create Rendering Resources
        _fpConstBuffer = _context.CreateBuffer(
            new FPConstants(),
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlusConstantBuffer"
        );

        // Depth buffer for depth testing (Z-buffering)
        _depthBuffer = _context.CreateTexture(
            new TextureDesc()
            {
                Type = TextureType.Texture2D,
                Format = Format.Z_F32,
                Dimensions = new Dimensions((uint)width, (uint)height, 1),
                NumLayers = 1,
                NumSamples = 1,
                Usage = TextureUsageBits.Attachment,
                NumMipLevels = 1,
                Storage = StorageType.Device,
            }
        );

        _directionalLightBuffer = _context.CreateBuffer(
            new DirectionalLights()
            {
                Lights_0 = new DirectionalLight
                {
                    Direction = new Vector3(0, -1, 0),
                    Color = Vector3.One,
                    Intensity = 1,
                },
                LightCount = 1,
            },
            BufferUsageBits.Storage,
            StorageType.Device,
            "DirectionalLightBuffer"
        );

        _cullConst.MeshInfoBufferAddress = _meshInfoBuffer.GpuAddress;
        _cullConst.MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress;

        // 2.4 Build Pipelines
        CreateCullingPipeline();
        CreateRenderPipeline();
    }

    private void CreateCullingPipeline()
    {
        // Generates the compute shader code for frustum checking.
        // Mode 'MultiMeshSingleInstance' means each thread processes one unique object/DrawCommand.
        var cullingShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.MultiMeshSingleInstance
        );
        var cullingModule = _context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "FrustumCullingCompute"
        );
        _cullingPipeline = _context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );
        Debug.Assert(_cullingPipeline.Valid);
    }

    private void CreateRenderPipeline()
    {
        // Setup PBR material pipeline (though we use simplified lighting/unlit for this demo)
        var builder = new MaterialShaderBuilder().ConfigForwardPlus(
            ForwardPlusLightCulling.Config.Default
        );

        var shaderResult = builder.BuildMaterialPipeline(_context, "Unlit");
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragementShader = shaderResult.FragmentShader,
                DebugName = "UnlitPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };

            // Configure blending and depth formats
            pipelineDesc.Colors[0].Format = Format.BGRA_SRGB8;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.WriteSpecInfo(0, PBRShadingMode.Unlit);

            _unlitRenderPipeline = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_unlitRenderPipeline.Valid);
        }
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragementShader = shaderResult.FragmentShader,
                DebugName = "PbrPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };

            // Configure blending and depth formats
            pipelineDesc.Colors[0].Format = Format.BGRA_SRGB8;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.WriteSpecInfo(0, PBRShadingMode.PBR);
            _pbrRenderPipeline = _context.CreateRenderPipeline(pipelineDesc);
        }
        Debug.Assert(_pbrRenderPipeline.Valid);

        // Configure Render Pass (how to clear screen, store results)
        _renderPass.Colors[0].ClearColor = Color.Black;
        _renderPass.Colors[0].LoadOp = LoadOp.Clear;
        _renderPass.Colors[0].StoreOp = StoreOp.Store;
        _renderPass.Depth.ClearDepth = 0.0f; // 0.0f for Reverse-Z
        _renderPass.Depth.LoadOp = LoadOp.Clear;

        _renderDependencies.Buffers[0] = _meshDrawBuffer;
    }
    #endregion

    #region 3. Main Render Loop
    public void Render(int width, int height)
    {
        // 3.1 Update Camera & Time
        var time = (float)(
            (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency
        );
        RotateCamera(time);

        // Calculate View/Projection matrices
        float aspect = (float)width / height;
        var proj = _camera.CreatePerspective(aspect);
        var view = _camera.CreateView();
        var viewProj = view * proj;
        var frustum = BoundingFrustum.FromViewProjectInversedZ(viewProj);

        // 3.2 Update Culling Constants
        _cullConst.CullingEnabled = 1;
        _cullConst.InstanceCount = (uint)_instanceCount;
        _cullConst.ViewMatrix = view;
        _cullConst.ViewProjectionMatrix = viewProj;
        _cullConst.ProjectionMatrix = proj;
        _cullConst.PlaneCount = frustum.Far.Normal == Vector3.Zero ? 5u : 6u;

        // Pack frustum planes for the shader
        _cullConst.FrustumPlanes_0 = frustum.Left.ToVector4();
        _cullConst.FrustumPlanes_1 = frustum.Right.ToVector4();
        _cullConst.FrustumPlanes_2 = frustum.Top.ToVector4();
        _cullConst.FrustumPlanes_3 = frustum.Bottom.ToVector4();
        _cullConst.FrustumPlanes_4 = frustum.Near.ToVector4();
        _cullConst.FrustumPlanes_5 = frustum.Far.ToVector4();

        _cullConst.MaxDrawDistance = 150;
        // Reducing MinScreenSize from 0.1f to 0.005f prevents aggressive culling of small/distant objects.
        _cullConst.MinScreenSize = 0.005f;

        // 3.3 Dispatch Culling Shader (GPU)
        var cmdBuffer = _context!.AcquireCommandBuffer();
        cmdBuffer.UpdateBuffer(_cullConstBuffer, _cullConst);

        cmdBuffer.BindComputePipeline(_cullingPipeline);
        cmdBuffer.PushConstants(_cullConstBuffer.GpuAddress);

        // Run one thread per object to check visibility
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize((uint)_instanceCount), 1, 1),
            Dependencies.Empty
        );

        // 3.5 Render Visible Objects
        var target = _context.GetCurrentSwapchainTexture();
        var invView = MatrixHelper.PsudoInvert(view);
        var invPersp = _camera.CreateInversePerspective(aspect);
        var invViewProj = invPersp * invView;

        // Update global scene constants
        cmdBuffer.UpdateBuffer(
            _fpConstBuffer,
            new FPConstants()
            {
                ViewProjection = view * proj,
                InverseViewProjection = invViewProj,
                CameraPosition = _camera.Position,
                Time = (float)DateTime.Now.TimeOfDay.TotalSeconds,
                MeshInfoBufferAddress = _meshInfoBuffer.GpuAddress,
                MaterialBufferAddress = _pbrPropertiesBuffer.GpuAddress,
                MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress,
                DirectionalLightsBufferAddress = _directionalLightBuffer.GpuAddress,
                LightCount = 0, // No lights in this unlit demo
                TileSize = 0,
                ScreenDimensions = new Vector2(width, height),
                TileCountX = 0,
                TileCountY = 0,
            }
        );

        // Begin Rendering to screen
        _frameBuffer.Colors[0].Texture = target;
        _frameBuffer.DepthStencil.Texture = _depthBuffer;

        // Wait for buffer writes from Compute Shader
        cmdBuffer.BeginRendering(_renderPass, _frameBuffer, _renderDependencies);
        cmdBuffer.BindDepthState(_depthState);
        cmdBuffer.BindRenderPipeline(_unlitRenderPipeline);
        cmdBuffer.PushConstants(
            new MeshDrawPushConstant() { FpConstAddress = _fpConstBuffer.GpuAddress }
        );
        cmdBuffer.BindIndexBuffer(_indexBuffer, IndexFormat.UI32);
        cmdBuffer.DrawIndexedIndirect(
            _meshDrawBuffer,
            0,
            (uint)_instanceCount / 2,
            MeshDraw.SizeInBytes
        );

        cmdBuffer.BindRenderPipeline(_pbrRenderPipeline);
        cmdBuffer.PushConstants(
            new MeshDrawPushConstant()
            {
                FpConstAddress = _fpConstBuffer.GpuAddress,
                DrawCommandIdxOffset = (uint)(_instanceCount / 2),
            }
        );
        cmdBuffer.DrawIndexedIndirect(
            _meshDrawBuffer,
            (uint)_instanceCount / 2 * MeshDraw.SizeInBytes,
            (uint)_instanceCount / 2,
            MeshDraw.SizeInBytes
        );
        cmdBuffer.EndRendering();
        _context.Submit(cmdBuffer, target);
    }
    #endregion

    #region 4. Scene & Helper Methods
    private void CreateMeshes()
    {
        // Create basic geometry
        var meshBuilder = new MeshBuilder();
        meshBuilder.AddBox(new Vector3(0, 0, 0), 1, 1, 1);
        _boxMesh = meshBuilder.ToMesh().ToGeometry();
        _boxMesh.UpdateBuffers(_context);
        _meshes.Add(_boxMesh);

        meshBuilder.Reset();
        meshBuilder.AddSphere(new Vector3(2, 0, 0), 0.5f, 16, 16);
        _sphereMesh = meshBuilder.ToMesh().ToGeometry();
        _sphereMesh.UpdateBuffers(_context);
        _meshes.Add(_sphereMesh);
        _sphereMesh.FirstIndex = (uint)_boxMesh.Indices.Count;

        _meshInfos.Add(
            new MeshInfo()
            {
                VertexBufferAddress = _boxMesh.VertexBuffer.GpuAddress,
                VertexPropsBufferAddress = _boxMesh.VertexPropsBuffer.GpuAddress,
                VertexColorBufferAddress = _boxMesh.VertexColorBuffer.GpuAddress,
                BoxMax = _boxMesh.BoundingBoxLocal.Maximum,
                BoxMin = _boxMesh.BoundingBoxLocal.Minimum,
                SphereCenter = _boxMesh.BoundingSphereLocal.Center,
                SphereRadius = _boxMesh.BoundingSphereLocal.Radius,
            }
        );

        _meshInfos.Add(
            new MeshInfo()
            {
                VertexBufferAddress = _sphereMesh.VertexBuffer.GpuAddress,
                VertexPropsBufferAddress = _sphereMesh.VertexPropsBuffer.GpuAddress,
                VertexColorBufferAddress = _sphereMesh.VertexColorBuffer.GpuAddress,
                BoxMax = _sphereMesh.BoundingBoxLocal.Maximum,
                BoxMin = _sphereMesh.BoundingBoxLocal.Minimum,
                SphereCenter = _sphereMesh.BoundingSphereLocal.Center,
                SphereRadius = _sphereMesh.BoundingSphereLocal.Radius,
            }
        );

        // Generate random instances
        _pBRProperties.Resize(_instanceCount);
        _meshIds.Resize(_instanceCount);
        _meshDraws.Resize(_instanceCount);
        var rnd = new Random((int)Stopwatch.GetTimestamp());
        int i = 0;
        // First half: Unlit objects
        for (; i < _instanceCount / 2; ++i)
        {
            _meshIds[i] = (uint)rnd.Next(0, 2); // Randomly choose Box or Sphere
            var position = new Vector3(
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0)
            );

            _pBRProperties[i] = new PBRProperties()
            {
                Albedo = new Vector3(
                    (float)rnd.NextDouble(),
                    (float)rnd.NextDouble(),
                    (float)rnd.NextDouble()
                ),
            };
            var mesh = _meshes[(int)_meshIds[i]];
            _meshDraws[i] = new MeshDraw()
            {
                MaterialId = (uint)i,
                MeshId = _meshIds[i],
                MaterialType = (uint)PBRShadingMode.Unlit,
                Transform =
                    Matrix4x4.CreateRotationX(rnd.NextFloat(0, 180) * MathF.PI / 180)
                    * Matrix4x4.CreateRotationY(rnd.NextFloat(0, 180) * MathF.PI / 180)
                    * Matrix4x4.CreateTranslation(position),
                IndexCount = (uint)mesh.Indices.Count,
                InstanceCount = 1,
                FirstIndex = mesh.FirstIndex,
            };
        }
        // Second half: PBR objects
        for (i = _instanceCount / 2; i < _instanceCount; ++i)
        {
            _meshIds[i] = (uint)rnd.Next(0, 2); // Randomly choose Box or Sphere
            var position = new Vector3(
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0)
            );
            _pBRProperties[i] = new PBRProperties()
            {
                Albedo = new Vector3(rnd.NextFloat(0, 1), rnd.NextFloat(0, 1), rnd.NextFloat(0, 1)),
                Metallic = (float)rnd.NextDouble(),
                Roughness = (float)rnd.NextDouble(),
                Ambient = new Vector3(
                    rnd.NextFloat(0.01f, 0.2f),
                    rnd.NextFloat(0.01f, 0.2f),
                    rnd.NextFloat(0.01f, 0.2f)
                ),
            };
            var mesh = _meshes[(int)_meshIds[i]];
            _meshDraws[i] = new MeshDraw()
            {
                MaterialId = (uint)i,
                MeshId = _meshIds[i],
                MaterialType = (uint)PBRShadingMode.PBR,
                Transform =
                    Matrix4x4.CreateRotationX(rnd.NextFloat(0, 180) * MathF.PI / 180)
                    * Matrix4x4.CreateRotationY(rnd.NextFloat(0, 180) * MathF.PI / 180)
                    * Matrix4x4.CreateTranslation(position),
                IndexCount = (uint)mesh.Indices.Count,
                InstanceCount = 1,
                FirstIndex = mesh.FirstIndex,
            };
        }
    }

    private void RotateCamera(float totalTime)
    {
        float speed = 0.1f;
        var rotation = Matrix4x4.CreateRotationY(totalTime * speed);
        _camera.Position = Vector3.Transform(_initialCameraPosition, rotation);
        _camera.Target = Vector3.Zero;
    }
    #endregion

    #region 5. Cleanup
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _boxMesh?.Dispose();
                _sphereMesh?.Dispose();
                // Dispose all GPU buffers
                _cullConstBuffer.Dispose();
                _pbrPropertiesBuffer.Dispose();
                _meshInfoBuffer.Dispose();
                _fpConstBuffer.Dispose();
                _depthBuffer.Dispose();
                _meshDrawBuffer.Dispose();
                _indexBuffer.Dispose();
                _directionalLightBuffer.Dispose();

                // Dispose pipelines
                _cullingPipeline.Dispose();
                _unlitRenderPipeline.Dispose();
                _pbrRenderPipeline.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

#region 6. Helper Classes

/// <summary>
/// Simple camera structure for the example.
/// Supports perspective projection with reverse-Z depth buffer.
/// </summary>
public sealed class Camera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up = Vector3.UnitY;
    public float NearPlane = 0.01f;
    public float FarPlane = 1000;
    public float Fov = 45 * MathF.PI / 180;

    /// <summary>
    /// Creates a right-handed view matrix looking from Position to Target.
    /// </summary>
    public Matrix4x4 CreateView()
    {
        return MatrixHelper.LookAtRH(Position, Target, Up);
    }

    /// <summary>
    /// Creates a right-handed perspective projection matrix with reverse-Z depth.
    /// Reverse-Z provides better depth precision for distant objects.
    /// </summary>
    public Matrix4x4 CreatePerspective(float aspect)
    {
        return MatrixHelper.PerspectiveFovRHReverseZ(Fov, aspect, NearPlane, FarPlane);
    }

    /// <summary>
    /// Creates the inverse of the perspective projection matrix.
    /// Used for reconstructing world positions from screen space.
    /// </summary>
    public Matrix4x4 CreateInversePerspective(float aspect)
    {
        return MatrixHelper.InversePerspectiveFovRHReverseZ(Fov, aspect, NearPlane, FarPlane);
    }
}

#endregion
