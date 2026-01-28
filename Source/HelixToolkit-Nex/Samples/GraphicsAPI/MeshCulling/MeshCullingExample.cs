using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;

namespace MeshCulling;

/// <summary>
/// This example demonstrates how to implement compute-shader-based frustum culling.
/// The visibility of objects is calculated on the GPU, and the results are read back to the CPU
/// to drive the rendering loop. Functional steps:
/// 1. Setup scene data (instances, bounds).
/// 2. Compute Shader: Check AABB/Sphere against camera frustum.
/// 3. Readback: Get the list of visible instance IDs.
/// 4. Render: Draw only the visible instances.
/// </summary>
internal class MeshCullingExample : IDisposable
{
    #region 1. Fields and Resources
    private readonly IContext _context;
    private bool _disposedValue;

    // -- Scene Data --
    // Meshes used for rendering instances
    private Geometry? _boxMesh = null;
    private Geometry? _sphereMesh = null;

    // CPU-side data for instances
    private FastList<PBRProperties> _pBRProperties = [];
    private FastList<uint> _meshIds = [];
    private FastList<Matrix4x4> _modelMatrices = [];
    private FastList<MeshBoundData> _bounds = [];

    // Arrays for reading back visibility results
    private uint[] _visibilities = new uint[1000];
    private int _instanceCount = 1000;

    // -- GPU Buffers --
    // Structured buffers holding scene data for the GPU
    private BufferResource _cullConstBuffer = BufferResource.Null; // Constants for culling (Camera, Frustum)
    private BufferResource _meshIdBuffer = BufferResource.Null; // ID of the mesh to draw for each instance
    private BufferResource _modelMatrixBuffer = BufferResource.Null; // Transform matrices for instances
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null; // Material properties
    private BufferResource _boundsBuffer = BufferResource.Null; // AABB/Sphere bounds for culling

    // Buffers for culling output
    private BufferResource _visibilityBuffer = BufferResource.Null; // Output: List of visible instance indices
    private BufferResource _visibleCountBuffer = BufferResource.Null; // Output: Atomic counter for visible instances

    // Rendering resources
    private BufferResource _fpConstBuffer = BufferResource.Null; // Forward+ lighting constants
    private TextureResource _depthBuffer = TextureResource.Null; // Depth attachment

    // -- Pipelines --
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null; // The Compute Shader for culling
    private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null; // The Rasterization pipeline

    // -- State & Helpers --
    private DepthState _depthState = DepthState.DefaultReversedZ;
    private CullingConstants _cullConst = new();
    private Camera _camera = new() { Position = new Vector3(0, 0, -50), Up = Vector3.UnitY };
    private Vector3 _initialCameraPosition;
    private long _startTimestamp;
    private RenderPass _renderPass = new RenderPass();
    private Framebuffer _frameBuffer = new Framebuffer();
    #endregion

    #region 2. Constructor & Initialization
    public MeshCullingExample(IContext context)
    {
        _context = context;
        _initialCameraPosition = _camera.Position;
        _startTimestamp = Stopwatch.GetTimestamp();
        // Generate random scene objects
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
        _meshIdBuffer = _context.CreateBuffer(
            _meshIds,
            BufferUsageBits.Storage,
            StorageType.Device,
            "MeshIdBuffer"
        );
        _modelMatrixBuffer = _context.CreateBuffer(
            _modelMatrices,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ModelMatrixBuffer"
        );
        _pbrPropertiesBuffer = _context.CreateBuffer(
            _pBRProperties,
            BufferUsageBits.Storage,
            StorageType.Device,
            "PBRPropertiesBuffer"
        );
        _boundsBuffer = _context.CreateBuffer(
            _bounds,
            BufferUsageBits.Storage,
            StorageType.Device,
            "BoundsBuffer"
        );

        // 2.2 Create Output Buffers for Culling Results
        _visibilityBuffer = _context.CreateBuffer(
            _visibilities,
            BufferUsageBits.Storage,
            StorageType.Device,
            "VisibilityBuffer"
        );

        _visibleCountBuffer = _context.CreateBuffer(
            0u,
            BufferUsageBits.Storage,
            StorageType.Device,
            "VisibleCountBuffer"
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

        // Link buffer addresses to the culling constants so the shader can access them
        _cullConst.ModelMatrixBufferAddress = _modelMatrixBuffer.GpuAddress;
        _cullConst.MeshIdBufferAddress = _meshIdBuffer.GpuAddress;
        _cullConst.MeshBoundBufferAddress = _boundsBuffer.GpuAddress;
        _cullConst.VisibilityBufferAddress = _visibilityBuffer.GpuAddress;
        _cullConst.DrawCountBufferAddress = _visibleCountBuffer.GpuAddress;

        // 2.4 Build Pipelines
        CreateCullingPipeline();
        CreateRenderPipeline();
    }

    private void CreateCullingPipeline()
    {
        // Generates the compute shader code for frustum checking
        var cullingShader = GpuFrustumCulling.GenerateComputeShader();
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
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithSimpleLighting(false) // Disable complex lighting for performance in this sample
            .ConfigForwardPlus(ForwardPlusLightCulling.Config.Default);

        var shaderResult = builder.BuildMaterialPipeline(_context, "Unlit");

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

        // Specialization constants setup (if needed by the shader)
        pipelineDesc.SpecInfo.Entries[0].ConstantId = 0;
        pipelineDesc.SpecInfo.Entries[0].Size = sizeof(uint);
        pipelineDesc.SpecInfo.Data = new byte[sizeof(uint)];
        using var pData = pipelineDesc.SpecInfo.Data.Pin();
        unsafe
        {
            NativeHelper.Write((nint)pData.Pointer, 1u);
        }

        _renderPipeline = _context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_renderPipeline.Valid);

        // Configure Render Pass (how to clear screen, store results)
        _renderPass.Colors[0].ClearColor = Color.Black;
        _renderPass.Colors[0].LoadOp = LoadOp.Clear;
        _renderPass.Colors[0].StoreOp = StoreOp.Store;
        _renderPass.Depth.ClearDepth = 0.0f; // 0.0f for Reverse-Z
        _renderPass.Depth.LoadOp = LoadOp.Clear;
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

        // Reset the count buffer to 0 before dispatch
        cmdBuffer.FillBuffer(_visibleCountBuffer, 0, sizeof(uint), 0);

        cmdBuffer.BindComputePipeline(_cullingPipeline);
        cmdBuffer.PushConstants(_cullConstBuffer.GpuAddress);

        // Run one thread per instance
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize((uint)_instanceCount), 1, 1),
            Dependencies.Empty
        );

        // Submit culling work
        var handle = _context.Submit(cmdBuffer);

        // 3.4 Readback Results (Sync point)
        // Wait for GPU to finish culling before reading back.
        // NOTE: In a more advanced "GPU Driven" pipeline, we would use DrawIndirect
        // and keep the count on the GPU to avoid this stall.
        _context.Wait(handle);
        cmdBuffer = _context!.AcquireCommandBuffer();
        var count = GetVisibleIndices(); // Download the count and list of visible IDs

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
                ModelMatrixBufferAddress = _modelMatrixBuffer.GpuAddress,
                MaterialBufferAddress = _pbrPropertiesBuffer.GpuAddress,
                PerModelParamsBufferAddress = _fpConstBuffer.GpuAddress,
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
        cmdBuffer.BeginRendering(_renderPass, _frameBuffer, Dependencies.Empty);
        cmdBuffer.BindRenderPipeline(_renderPipeline);
        cmdBuffer.BindDepthState(_depthState);

        // Loop through ONLY the visible instances
        for (var i = 0; i < count; ++i)
        {
            var idx = _visibilities[i];
            var mesh = _meshIds[(int)idx] == 0 ? _boxMesh! : _sphereMesh!;
            cmdBuffer.BindIndexBuffer(mesh.IndexBuffer, IndexFormat.UI32);

            // Set per-object draw constants using index
            var drawParams = new MeshDraw();
            drawParams.ForwardPlusConstantsAddress = _fpConstBuffer.GpuAddress;
            drawParams.VertexBufferAddress = mesh.VertexBuffer.GpuAddress;
            drawParams.ModelId = idx;
            drawParams.MaterialId = idx;
            cmdBuffer.PushConstants(drawParams);

            cmdBuffer.DrawIndexed((uint)mesh.Indices.Count);
        }

        cmdBuffer.EndRendering();
        _context.Submit(cmdBuffer, target);
    }

    /// <summary>
    /// Reads back the number of visible items and their indices from GPU buffers.
    /// </summary>
    public uint GetVisibleIndices()
    {
        unsafe
        {
            uint count = 0;
            void* countPtr = &count;
            // Download the atomic counter value
            _context.Download(_visibleCountBuffer, (nint)countPtr, sizeof(uint));

            // If anything is visible, download the list of IDs
            if (count > 0)
            {
                using var ptr = _visibilities.Pin();
                _context.Download(_visibilityBuffer, (nint)ptr.Pointer, count * sizeof(uint));
            }
            return count;
        }
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

        meshBuilder.Reset();
        meshBuilder.AddSphere(new Vector3(2, 0, 0), 0.5f, 16, 16);
        _sphereMesh = meshBuilder.ToMesh().ToGeometry();
        _sphereMesh.UpdateBuffers(_context);

        // Register bounds for geometry types (Box=0, Sphere=1)
        _bounds.Add(
            new MeshBoundData()
            {
                BoxMax = _boxMesh.BoundingBoxLocal.Maximum,
                BoxMin = _boxMesh.BoundingBoxLocal.Minimum,
                SphereCenter = _boxMesh.BoundingSphereLocal.Center,
                SphereRadius = _boxMesh.BoundingSphereLocal.Radius,
            }
        );
        _bounds.Add(
            new MeshBoundData()
            {
                BoxMax = _sphereMesh.BoundingBoxLocal.Maximum,
                BoxMin = _sphereMesh.BoundingBoxLocal.Minimum,
                SphereCenter = _sphereMesh.BoundingSphereLocal.Center,
                SphereRadius = _sphereMesh.BoundingSphereLocal.Radius,
            }
        );

        // Generate random instances
        _pBRProperties.Resize(_instanceCount);
        _meshIds.Resize(_instanceCount);
        _modelMatrices.Resize(_instanceCount);
        var rnd = new Random((int)Stopwatch.GetTimestamp());
        for (int i = 0; i < _instanceCount; ++i)
        {
            _meshIds[i] = (uint)rnd.Next(0, 2); // Randomly choose Box or Sphere
            var position = new Vector3(
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0),
                (float)(rnd.NextDouble() * 200.0 - 100.0)
            );
            _modelMatrices[i] =
                Matrix4x4.CreateRotationX(rnd.NextFloat(0, 180) * MathF.PI / 180)
                * Matrix4x4.CreateRotationY(rnd.NextFloat(0, 180) * MathF.PI / 180)
                * Matrix4x4.CreateTranslation(position);
            _pBRProperties[i] = new PBRProperties()
            {
                Albedo = new Vector3(
                    (float)rnd.NextDouble(),
                    (float)rnd.NextDouble(),
                    (float)rnd.NextDouble()
                ),
                Metallic = (float)rnd.NextDouble(),
                Roughness = (float)rnd.NextDouble(),
            };
        }

        // Setup initial culling parms
        _cullConst.CullingEnabled = 1;
        _cullConst.InstanceCount = (uint)_instanceCount;
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
                _meshIdBuffer.Dispose();
                _modelMatrixBuffer.Dispose();
                _pbrPropertiesBuffer.Dispose();
                _boundsBuffer.Dispose();
                _visibilityBuffer.Dispose();
                _visibleCountBuffer.Dispose();
                _fpConstBuffer.Dispose();
                _depthBuffer.Dispose();

                // Dispose pipelines
                _cullingPipeline.Dispose();
                _renderPipeline.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MeshCullingExample()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
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
