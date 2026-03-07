using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;
using SDL3;

namespace InstancingMeshCulling;

/// <summary>
/// This example demonstrates how to implement compute-shader-based frustum culling with GPU Instancing.
///
/// Key Concepts:
/// 1. **GPU-Driven Rendering**: The CPU does not know which objects are visible. It simply dispatches a Compute Shader.
/// 2. **Compute Shader Culling**: The CS checks each instance's bounding volume against the camera frustum.
/// 3. **Indirect Drawing**: The CS populates a buffer of visible instance indices and updates the Indirect Draw Command buffer directly on the GPU.
/// 4. **No CPU Readback**: The rendering loop does not stall waiting for the GPU to tell the CPU what to draw.
///
/// Functional steps:
/// 1. Setup scene data (instances, transforms, boundings).
/// 2. Compute Shader:
///    - Check instance bounds against frustum.
///    - If visible, append instance ID to `_culledInstanceIdxBuffer`.
///    - Atomically increment the instance count in `_drawCmdsBuffer`.
/// 3. Render Pass:
///    - Execute `DrawIndexedIndirect` using the buffer modified by the Compute Shader.
///    - The Vertex Shader accesses the visible instances via `_culledInstanceIdxBuffer`.
/// </summary>
internal class InstancingMeshCullingExample : IDisposable
{
    #region 1. Fields and Resources
    private readonly IContext _context;
    private bool _disposedValue;

    // -- Scene Data --
    // Meshes used for rendering instances
    private Geometry? _boxMesh;

    // CPU-side data for instances
    private readonly FastList<PBRProperties> _pBRProperties = [];
    private readonly FastList<Matrix4x4> _instanceMatrices = [];

    // Arrays for data management
    private readonly int _instanceCount = 10000;
    private readonly FastList<MeshDraw> _meshDraws = [];
    private readonly FastList<Geometry> _meshes = [];
    private readonly FastList<MeshInfo> _meshInfos = [];

    // -- GPU Buffers --
    // Constant Data
    private BufferResource _cullConstBuffer = BufferResource.Null; // Uniforms for culling (View, Proj, Frustum planes)
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null; // Material properties
    private BufferResource _indexBuffer = BufferResource.Null; // Geometry indices
    private BufferResource _meshInfoBuffer = BufferResource.Null;

    // Instance Data
    private BufferResource _instancingBuffer = BufferResource.Null; // Stores the local-to-world matrix for every instance (Visible or not)

    // Indirect Draw Data (Modified by GPU)
    private BufferResource _meshDrawBuffer = BufferResource.Null; // Contains MeshDraw structs (pointers to buffers, material IDs)

    // Culling Output (Written by CS, Read by VS)
    private BufferResource _culledInstanceIdxBuffer = BufferResource.Null; // List of indices into _instancingBuffer that survived culling

    // Rendering resources
    private BufferResource _fpConstBuffer = BufferResource.Null; // Forward+ lighting constants (lighting, camera pos)
    private TextureResource _depthBuffer = TextureResource.Null; // Depth attachment for Z-culling/testing

    // -- Pipelines --
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null; // The Compute Shader that performs the culling
    private ComputePipelineResource _resetInstanceCountPipeline = ComputePipelineResource.Null; // The Compute Shader to reset instance count in mesh draw buffer.
    private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null; // The Graphics Pipeline for drawing

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
    private readonly Dependencies _cullDeps = new Dependencies();
    private readonly Dependencies _renderDependencies = new Dependencies();
    #endregion

    #region 2. Constructor & Initialization
    public InstancingMeshCullingExample(IContext context)
    {
        _context = context;
        _initialCameraPosition = _camera.Position;
        _startTimestamp = Stopwatch.GetTimestamp();
        // Generate random scene objects and populate CPU lists
        CreateMeshes();
    }

    /// <summary>
    /// Initializes GPU resources, buffers, and pipelines.
    /// </summary>
    public void Initialize(int width, int height)
    {
        // 2.1 Create GPU Buffers
        // Storage buffers are used because they are large and accessed via indices in shaders.
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

        // 2.2 Create Output Buffers for Culling Results

        // This buffer will be filled by the Compute Shader with the indices of visible instances.
        _culledInstanceIdxBuffer = _context.CreateBuffer(
            new uint[_instanceMatrices.Count],
            BufferUsageBits.Storage,
            StorageType.Device,
            "CulledInstanceIdx"
        );

        // The raw instance data (all 10000 matrices).
        _instancingBuffer = _context.CreateBuffer(
            _instanceMatrices,
            BufferUsageBits.Storage,
            StorageType.Device,
            "InstancingBuffer"
        );

        // Bind the GPU addresses of our instance buffers to the MeshDraw structure.
        // This tells the shader system where to find instance data during rendering.
        _meshDraws.GetInternalArray()[0].InstancingBufferAddress = _instancingBuffer.GpuAddress;
        _meshDraws.GetInternalArray()[0].InstancingIndexBufferAddress =
            _culledInstanceIdxBuffer.GpuAddress;

        _meshDrawBuffer = _context.CreateBuffer(
            _meshDraws,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            StorageType.Device,
            "MeshDrawBuffer"
        );

        _meshInfoBuffer = _context.CreateBuffer(
            _meshInfos,
            BufferUsageBits.Storage,
            StorageType.Device,
            "MeshInfoBuffer"
        );

        var indices = new FastList<uint>(_boxMesh!.Indices);
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

        _cullConst.MeshInfoBufferAddress = _meshInfoBuffer.GpuAddress;
        _cullConst.MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress;

        // 2.4 Build Pipelines
        CreateCullingPipeline();
        CreateRenderPipeline();
    }

    private void CreateCullingPipeline()
    {
        // Generates the compute shader code for frustum checking.
        // Mode 'SingleMeshInstancing' implies: One mesh type, many instances.
        var cullingShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.SingleMeshInstancing
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

        var resetShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.ResetInstanceCount
        );
        var resetModule = _context.CreateShaderModuleGlsl(
            resetShader,
            ShaderStage.Compute,
            "ResetDrawInstanceCount"
        );
        _resetInstanceCountPipeline = _context.CreateComputePipeline(
            resetModule,
            "ResetDrawInstanceCount"
        );
        Debug.Assert(_resetInstanceCountPipeline.Valid);
        _cullDeps.Buffers[0] = _meshDrawBuffer;
    }

    private void CreateRenderPipeline()
    {
        // Setup PBR material pipeline (using simplified lighting/unlit for this demo for clarity).
        var builder = new MaterialShaderBuilder().ConfigForwardPlus(
            ForwardPlusLightCulling.Config.Default
        );

        var shaderResult = builder.BuildMaterialPipeline(_context, "Unlit");

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = shaderResult.VertexShader,
            FragmentShader = shaderResult.FragmentShader,
            DebugName = "UnlitPipeline",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
        };

        // Configure blending and depth formats
        pipelineDesc.Colors[0].Format = Format.BGRA_SRGB8;
        pipelineDesc.DepthFormat = Format.Z_F32;
        pipelineDesc.WriteSpecInfo(0, PBRShadingMode.Unlit);

        _renderPipeline = _context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_renderPipeline.Valid);

        // Configure Render Pass (LoadOp.Clear ensures fresh frame)
        _renderPass.Colors[0].ClearColor = Color.Black;
        _renderPass.Colors[0].LoadOp = LoadOp.Clear;
        _renderPass.Colors[0].StoreOp = StoreOp.Store;
        _renderPass.Depth.ClearDepth = 0.0f; // 0.0f for Reverse-Z
        _renderPass.Depth.LoadOp = LoadOp.Clear;

        // Dependencies ensure memory barriers are placed correctly.
        // We need to wait for the Compute Shader to finish writing to these buffers before Rendering reads them.
        _renderDependencies.Buffers[0] = _culledInstanceIdxBuffer;
        _renderDependencies.Buffers[1] = _meshDrawBuffer;
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
        // Upload the new camera matrices and frustum planes to the GPU.
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
        // Reducing MinScreenSize from 0.1f to 0.001f prevents aggressive culling of small/distant objects.
        _cullConst.MinScreenSize = 0.001f;

        // 3.3 Dispatch Culling Shader (GPU)
        var cmdBuffer = _context!.AcquireCommandBuffer();
        cmdBuffer.UpdateBuffer(_cullConstBuffer, _cullConst);

        cmdBuffer.BindComputePipeline(_resetInstanceCountPipeline);
        cmdBuffer.PushConstants(
            new ResetMeshDrawInstanceCountPC
            {
                MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress,
                MeshDrawCount = (uint)_meshDraws.Count,
            }
        );

        // Reset all instance count value in mesh draw buffer to 0.
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize((uint)_meshDraws.Count), 1, 1),
            Dependencies.Empty
        );

        cmdBuffer.BindComputePipeline(_cullingPipeline);
        cmdBuffer.PushConstants(
            new FrustumCullInstancingPC()
            {
                CullingConstAddress = _cullConstBuffer.GpuAddress,
                DrawCommandIdx = 0,
                InstanceCount = (uint)_instanceMatrices.Count,
            }
        );

        // Run one thread per instance to check visibility
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize((uint)_instanceCount), 1, 1),
            _cullDeps
        );

        // 3.4 Render Visible Objects
        // Uses the output from step 3.3 as input for drawing.
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
                MaterialBufferAddress = _pbrPropertiesBuffer.GpuAddress,
                MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress,
                MeshInfoBufferAddress = _meshInfoBuffer.GpuAddress,
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
        // _renderDependencies ensures the Compute Shader finishes before we draw.
        cmdBuffer.BeginRendering(_renderPass, _frameBuffer, _renderDependencies);
        cmdBuffer.BindRenderPipeline(_renderPipeline);
        cmdBuffer.BindDepthState(_depthState);
        cmdBuffer.PushConstants(_fpConstBuffer.GpuAddress);
        cmdBuffer.BindIndexBuffer(_indexBuffer, IndexFormat.UI32);

        // Indirect Draw:
        // The GPU reads arguments (InstanceCount, etc.) from '_meshDrawBuffer'.
        // This count was populated by the Compute Shader in the previous step.
        cmdBuffer.DrawIndexedIndirect(_meshDrawBuffer, 0, 1, MeshDraw.SizeInBytes);
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

        // Generate random instances
        _pBRProperties.Resize(1);
        _meshDraws.Resize(1);
        _meshInfos.Resize(1);
        var rnd = new Random((int)Stopwatch.GetTimestamp());
        _pBRProperties[0] = new PBRProperties()
        {
            Albedo = new Vector3(
                (float)rnd.NextDouble(),
                (float)rnd.NextDouble(),
                (float)rnd.NextDouble()
            ),
            Metallic = (float)rnd.NextDouble(),
            Roughness = (float)rnd.NextDouble(),
        };
        _meshDraws[0] = new MeshDraw()
        {
            IndexCount = (uint)_boxMesh.Indices.Count,
            InstanceCount = 0,
            FirstIndex = 0,
            MaterialId = 0,
            Transform = Matrix4x4.Identity,
            Cullable = 1,
        };
        _meshInfos[0] = new MeshInfo()
        {
            VertexBufferAddress = _boxMesh.VertexBuffer.GpuAddress,
            VertexPropsBufferAddress = _boxMesh.VertexPropsBuffer.GpuAddress,
            VertexColorBufferAddress = _boxMesh.VertexColorBuffer.GpuAddress,
            BoxMax = _boxMesh.BoundingBoxLocal.Maximum,
            BoxMin = _boxMesh.BoundingBoxLocal.Minimum,
            SphereCenter = _boxMesh.BoundingSphereLocal.Center,
            SphereRadius = _boxMesh.BoundingSphereLocal.Radius,
        };

        _instanceMatrices.Resize(_instanceCount);

        for (int i = 0; i < _instanceCount; ++i)
        {
            _instanceMatrices[i] = Matrix4x4.CreateTranslation(
                new Vector3(
                    (float)(rnd.NextDouble() * 200.0 - 100.0),
                    (float)(rnd.NextDouble() * 200.0 - 100.0),
                    (float)(rnd.NextDouble() * 200.0 - 100.0)
                )
            );
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
                // Dispose all GPU buffers
                _cullConstBuffer.Dispose();
                _pbrPropertiesBuffer.Dispose();
                _fpConstBuffer.Dispose();
                _depthBuffer.Dispose();
                _meshDrawBuffer.Dispose();
                _indexBuffer.Dispose();
                _instancingBuffer.Dispose();
                _culledInstanceIdxBuffer.Dispose();
                _meshInfoBuffer.Dispose();

                // Dispose pipelines
                _cullingPipeline.Dispose();
                _renderPipeline.Dispose();
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
    public float FarPlane = float.PositiveInfinity;
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
