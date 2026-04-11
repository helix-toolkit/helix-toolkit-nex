using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Examples;

/// <summary>
/// Complete example demonstrating Forward+ rendering with bindless buffers.
///
/// Forward+ (Tiled Forward Rendering) is an advanced rendering technique that:
/// 1. Divides the screen into tiles (e.g., 16x16 pixels)
/// 2. Performs light culling per tile in a compute shader
/// 3. Only processes lights that affect each tile during fragment shading
///
/// This significantly improves performance with many dynamic lights compared to traditional forward rendering.
/// </summary>
public class ForwardPlusExample
{
    #region Constants and Configuration

    private static readonly int NumLights = 100;
    private static readonly int NumSpotLights = 4;
    private const PBRShadingMode ShadingMode = PBRShadingMode.PBR;
    private const float LightRange = 6.0f;
    #endregion

    #region GPU Resources - Buffers

    // Light data and culling buffers
    private BufferResource _lightBuffer = BufferResource.Null;
    private BufferResource _lightCullingBuffer = BufferResource.Null;
    private BufferResource _lightGridBuffer = BufferResource.Null;
    private BufferResource _lightIndexBuffer = BufferResource.Null;
    private BufferResource _directionalLightBuffer = BufferResource.Null;
    private BufferResource _meshInfoBuffer = BufferResource.Null;

    // Per-object data buffers
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null;
    private BufferResource _fpConstBuffer = BufferResource.Null;
    private BufferResource _meshDrawBuffer = BufferResource.Null;
    private BufferResource _indexBuffer = BufferResource.Null;

    #endregion

    #region GPU Resources - Textures

    private TextureResource _f16Framebuffer = TextureResource.Null;
    private TextureResource _depthBuffer = TextureResource.Null;

    #endregion

    #region GPU Resources - Pipelines

    private RenderPipelineResource _depthPassPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _renderPipelinePBR = RenderPipelineResource.Null;
    private RenderPipelineResource _renderPipelineUnlit = RenderPipelineResource.Null;
    private RenderPipelineResource _toneGammePipeline = RenderPipelineResource.Null;
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;

    #endregion

    #region GPU Resources - Samplers

    private SamplerResource _depthBufferSampler = SamplerResource.Null;
    private SamplerResource _toneMappingSampler = SamplerResource.Null;

    #endregion

    #region Render State

    private readonly IContext _context;
    private readonly RenderPass _depthPass = new();
    private readonly RenderPass _renderPass = new();
    private readonly RenderPass _toneMappingPass = new();
    private readonly Framebuffer _framebuffer = new();

    private DepthState _depthState = DepthState.DefaultReversedZ;
    private FastList<MeshDraw> _drawParams = new();

    #endregion

    #region Scene Data

    private ForwardPlusLightCulling.Config _config = ForwardPlusLightCulling.Config.Default;
    private FastList<Light> _lights = new();
    private DirectionalLights _dirLights = new();
    private readonly PBRProperties[] _pbrProperties = new PBRProperties[NumLights + 1];
    private readonly Geometry _lightMesh;
    private readonly Geometry _mesh;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the ForwardPlusExample class.
    /// Sets up geometry, materials, and render pass configurations.
    /// </summary>
    /// <param name="context">The graphics context for resource creation.</param>
    public ForwardPlusExample(IContext context)
    {
        HxDebug.EnableDebugAssertions = true;
        _context = context;

        // Create light representation mesh (small sphere)
        var builder = new MeshBuilder(true, true, true);
        builder.AddSphere(Vector3.Zero, 0.1f);
        _lightMesh = builder.ToMesh().ToGeometry();

        // Create main scene mesh (box + sphere)
        builder = new MeshBuilder(true, true, true);
        builder.AddBox(new Vector3(0, 0, 0), 40, 40, 2);
        builder.AddSphere(new Vector3(0, 0, 0), 3f);
        builder.AddSphere(new Vector3(-5, 5, 0), 3f);
        builder.AddBox(new Vector3(-10, 0, 0), 5, 5, 4);
        builder.AddBox(new Vector3(10, 10, 0), 5, 5, 4);
        builder.AddBox(new Vector3(10, -10, 0), 5, 5, 4);
        _mesh = builder.ToMesh().ToGeometry();
        _lightMesh.UpdateBuffers(_context);
        _mesh.UpdateBuffers(_context);

        // Configure depth prepass: clear depth, don't care about color
        _depthPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            LoadOp = LoadOp.Invalid,
            StoreOp = StoreOp.DontCare,
        };
        _depthPass.Depth = new RenderPass.AttachmentDesc
        {
            ClearDepth = 0.0f,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };

        // Configure main render pass: clear color, load existing depth
        _renderPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
        _renderPass.Depth = new RenderPass.AttachmentDesc
        {
            ClearDepth = 0.0f,
            LoadOp = LoadOp.Load,
            StoreOp = StoreOp.DontCare,
        };

        // Configure tone mapping pass: clear color, no depth
        _toneMappingPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
        _toneMappingPass.Depth = new RenderPass.AttachmentDesc
        {
            LoadOp = LoadOp.Invalid,
            StoreOp = StoreOp.DontCare,
        };

        // Initialize scene data
        _lights = CreateTestLights(NumLights);
        _pbrProperties[0] = new()
        {
            Albedo = new Vector3(1f, 1f, 1f),
            Metallic = 0.8f,
            Roughness = 0.2f,
            Ao = 1f,
        };
        _drawParams.Add(
            new()
            {
                MaterialId = 0,
                MeshId = 0,
                Transform = Matrix4x4.Identity,
                IndexCount = _mesh.IndexCount,
                InstanceCount = 1,
                MaterialType = (uint)ShadingMode,
            }
        );
        // Setup light sphere transforms and emissive materials
        for (int i = 0; i < NumLights; i++)
        {
            _pbrProperties[i + 1] = new()
            {
                Ao = 1f,
                Emissive = _lights[i].Color * _lights[i].Intensity,
                Opacity = 1,
            };
            _drawParams.Add(
                new()
                {
                    MaterialId = (uint)i + 1,
                    MeshId = 1, // Light mesh
                    Transform = Matrix4x4.CreateTranslation(_lights[i].Position),
                    IndexCount = _lightMesh.IndexCount,
                    InstanceCount = 1,
                    MaterialType = (uint)PBRShadingMode.Unlit,
                }
            );
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes all GPU resources including buffers, textures, and pipelines.
    /// Must be called before rendering.
    /// </summary>
    /// <param name="screenWidth">Width of the rendering viewport.</param>
    /// <param name="screenHeight">Height of the rendering viewport.</param>
    public void Initialize(int screenWidth, int screenHeight)
    {
        InitializeLightingBuffers(screenWidth, screenHeight);
        InitializeRenderTargets(screenWidth, screenHeight);
        InitializePipelines();
    }

    /// <summary>
    /// Creates all buffers required for Forward+ light culling.
    /// </summary>
    /// <param name="screenWidth">Width of the screen for tile calculation.</param>
    /// <param name="screenHeight">Height of the screen for tile calculation.</param>
    private void InitializeLightingBuffers(int screenWidth, int screenHeight)
    {
        // Create light data buffer
        _lightBuffer = _context.CreateBuffer(
            _lights,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_LightBuffer"
        );

        // Calculate tile grid dimensions
        var tileCountX = (screenWidth + _config.TileSize - 1) / _config.TileSize;
        var tileCountY = (screenHeight + _config.TileSize - 1) / _config.TileSize;
        var totalTiles = tileCountX * tileCountY;

        // Light culling constants buffer
        _lightCullingBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = LightCullingConstants.SizeInBytes,
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightCulling"
        );

        // Light grid buffer: stores light count and index offset per tile
        _lightGridBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * LightGridTile.SizeInBytes),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightGrid"
        );

        // Light index list buffer: stores light indices for all tiles
        _lightIndexBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * _config.MaxLightsPerTile * sizeof(uint)),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightIndices"
        );

        _directionalLightBuffer = _context.CreateBuffer(
            _dirLights,
            BufferUsageBits.Storage,
            StorageType.Device,
            "DirectionalLightBuffer"
        );

        _meshInfoBuffer = _context.CreateBuffer(
            new MeshInfo[]
            {
                new MeshInfo
                {
                    VertexBufferAddress = _mesh.VertexBuffer.GpuAddress,
                    VertexPropsBufferAddress = _mesh.VertexPropsBuffer.GpuAddress,
                    VertexColorBufferAddress = _mesh.VertexColorBuffer.GpuAddress,
                },
                new MeshInfo
                {
                    VertexBufferAddress = _lightMesh.VertexBuffer.GpuAddress,
                    VertexPropsBufferAddress = _lightMesh.VertexPropsBuffer.GpuAddress,
                    VertexColorBufferAddress = _lightMesh.VertexColorBuffer.GpuAddress,
                },
            },
            BufferUsageBits.Storage,
            StorageType.Device,
            "MeshInfo"
        );

        _pbrPropertiesBuffer = _context.CreateBuffer(
            _pbrProperties,
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_PBRProperties"
        );

        _fpConstBuffer = _context.CreateBuffer(
            new FPConstants(),
            BufferUsageBits.Storage,
            StorageType.Device,
            "ForwardPlus_Constants"
        );

        _meshDrawBuffer = _context.CreateBuffer(
            _drawParams,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            StorageType.Device,
            "MeshDrawBuf"
        );

        var indices = new FastList<uint>(_mesh.Indices);
        indices.AddRange(_lightMesh.Indices);
        _indexBuffer = _context.CreateBuffer(
            indices,
            BufferUsageBits.Index | BufferUsageBits.Storage,
            StorageType.Device,
            "IndexBuffer"
        );
    }

    /// <summary>
    /// Creates render targets and samplers.
    /// </summary>
    /// <param name="screenWidth">Width of the render targets.</param>
    /// <param name="screenHeight">Height of the render targets.</param>
    private void InitializeRenderTargets(int screenWidth, int screenHeight)
    {
        // Depth buffer for depth prepass and culling
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

        // HDR framebuffer (floating point for HDR rendering)
        _f16Framebuffer = _context.CreateTexture(
            new TextureDesc()
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_F16,
                Dimensions = new Dimensions((uint)screenWidth, (uint)screenHeight, 1),
                NumLayers = 1,
                NumSamples = 1,
                Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                NumMipLevels = 1,
                Storage = StorageType.Device,
            }
        );

        // Tone mapping sampler
        _toneMappingSampler = _context.CreateSampler(
            new SamplerStateDesc
            {
                MinFilter = SamplerFilter.Nearest,
                MagFilter = SamplerFilter.Nearest,
            }
        );
    }

    /// <summary>
    /// Creates all rendering and compute pipelines.
    /// </summary>
    private void InitializePipelines()
    {
        CreateLightCullingPipeline();
        CreateRenderPipelines();
        CreateToneMappingPipeline();
    }

    /// <summary>
    /// Creates the compute pipeline for Forward+ light culling.
    /// </summary>
    private void CreateLightCullingPipeline()
    {
        var cullingShader = ForwardPlusLightCulling.GenerateComputeShader(_config);
        var cullingModule = _context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "ForwardPlus_CullingCompute"
        );
        _cullingPipeline = _context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );
    }

    /// <summary>
    /// Creates the render pipelines for depth prepass, PBR shading, and unlit rendering.
    /// </summary>
    private void CreateRenderPipelines()
    {
        var builder = new PBRMaterialShaderBuilder().ConfigForwardPlus(_config);

        var shaderResult = builder.BuildMaterialPipeline(_context, "ForwardPlus_Render");

        // PBR pipeline with lighting
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragmentShader = shaderResult.FragmentShader,
                DebugName = "ForwardPlus_RenderPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_F16;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.WriteSpecInfo(0, ShadingMode);
            _renderPipelinePBR = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_renderPipelinePBR.Valid);
        }

        // Unlit pipeline for light visualization
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragmentShader = shaderResult.FragmentShader,
                DebugName = "ForwardPlus_UnlitPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_F16;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.WriteSpecInfo(0, PBRShadingMode.Unlit);
            _renderPipelineUnlit = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_renderPipelineUnlit.Valid);
        }

        // Depth-only prepass pipeline
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                DebugName = "DepthPass",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
                DepthFormat = Format.Z_F32,
            };
            _depthPassPipeline = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_depthPassPipeline.Valid);
        }
    }

    /// <summary>
    /// Creates the tone mapping and gamma correction pipeline.
    /// </summary>
    private void CreateToneMappingPipeline()
    {
        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = "ToneMapping",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
        };
        var shaderCompiler = new ShaderCompiler();
        pipelineDesc.Colors[0].Format = Format.BGRA_SRGB8;

        var toneGammaShader = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psToneGamma.glsl")
        );
        if (!toneGammaShader.Success || toneGammaShader.Source == null)
        {
            throw new InvalidOperationException(
                "Failed to compile tone mapping shader: "
                    + string.Join("\n", toneGammaShader.Errors)
            );
        }
        pipelineDesc.FragmentShader = _context.CreateShaderModuleGlsl(
            toneGammaShader.Source,
            ShaderStage.Fragment,
            "ToneMapping_Fragment"
        );

        var vsQuad = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
        );

        if (!vsQuad.Success || vsQuad.Source == null)
        {
            throw new InvalidOperationException(
                "Failed to compile full-screen quad vertex shader: "
                    + string.Join("\n", vsQuad.Errors)
            );
        }
        pipelineDesc.VertexShader = _context.CreateShaderModuleGlsl(
            vsQuad.Source,
            ShaderStage.Vertex,
            "FullScreenQuad_Vertex"
        );
        _toneGammePipeline = _context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_toneGammePipeline.Valid);
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Renders a complete frame using Forward+ rendering.
    ///
    /// Rendering pipeline:
    /// 1. Update dynamic data (lights, transforms)
    /// 2. Depth prepass
    /// 3. Light culling compute shader
    /// 4. Main render pass with Forward+ lighting
    /// 5. Tone mapping to final output
    /// </summary>
    /// <param name="cmdBuffer">Command buffer to record rendering commands.</param>
    /// <param name="target">Target texture to render to.</param>
    /// <param name="camera">Camera for view and projection matrices.</param>
    /// <param name="screenWidth">Width of the rendering viewport.</param>
    /// <param name="screenHeight">Height of the rendering viewport.</param>
    public void Render(
        ICommandBuffer cmdBuffer,
        TextureHandle target,
        Camera camera,
        int screenWidth,
        int screenHeight
    )
    {
        UpdateSceneData(cmdBuffer, camera, screenWidth, screenHeight);
        DepthPrepass(cmdBuffer);
        LightCulling(cmdBuffer, screenWidth, screenHeight);
        RenderPass(cmdBuffer);
        ToneGamma(cmdBuffer, target);
    }

    /// <summary>
    /// Updates all dynamic scene data (lights, transforms, constants).
    /// </summary>
    private void UpdateSceneData(
        ICommandBuffer cmdBuffer,
        Camera camera,
        int screenWidth,
        int screenHeight
    )
    {
        // Animate lights
        MoveLights((float)DateTime.Now.TimeOfDay.TotalSeconds);
        cmdBuffer.UpdateBuffer(_lightBuffer, _lights);

        // Calculate matrices
        float aspect = (float)screenWidth / screenHeight;
        var view = camera.CreateView();
        var proj = camera.CreatePerspective(aspect);
        var invView = MatrixHelper.PsudoInvert(view);
        var invPersp = camera.CreateInversePerspective(aspect);
        var invViewProj = invPersp * invView;
        var tileCountX = (screenWidth + (int)_config.TileSize - 1) / (int)_config.TileSize;
        var tileCountY = (screenHeight + (int)_config.TileSize - 1) / (int)_config.TileSize;

        // Update Forward+ constants
        cmdBuffer.UpdateBuffer(
            _fpConstBuffer,
            new FPConstants()
            {
                Enabled = 1,
                ViewProjection = view * proj,
                InverseViewProjection = invViewProj,
                CameraPosition = camera.Position,
                TimeMs = Time.GetMonoTimeMs(),
                MeshInfoBufferAddress = _meshInfoBuffer.GpuAddress,
                LightBufferAddress = _lightBuffer.GpuAddress,
                LightGridBufferAddress = _lightGridBuffer.GpuAddress,
                LightIndexBufferAddress = _lightIndexBuffer.GpuAddress,
                MaterialBufferAddress = _pbrPropertiesBuffer.GpuAddress,
                MeshDrawBufferAddress = _meshDrawBuffer.GpuAddress,
                DirectionalLightsBufferAddress = _directionalLightBuffer.GpuAddress,
                LightCount = (uint)_lights.Count,
                TileSize = _config.TileSize,
                MaxLightsPerTile = _config.MaxLightsPerTile,
                ScreenDimensions = new Vector2(screenWidth, screenHeight),
                TileCountX = (uint)tileCountX,
                TileCountY = (uint)tileCountY,
            }
        );

        // Update light culling constants
        cmdBuffer.UpdateBuffer(
            _lightCullingBuffer,
            new LightCullingConstants
            {
                ViewMatrix = view,
                Projection = proj,
                InverseProjection = invPersp,
                ScreenDimensions = new Vector2(screenWidth, screenHeight),
                TileCountX = (uint)tileCountX,
                TileCountY = (uint)tileCountY,
                LightCount = (uint)_lights.Count,
                ZNear = camera.NearPlane,
                ZFar = camera.FarPlane,
                DepthTextureIndex = _depthBuffer.Index,
                SamplerIndex = _depthBufferSampler.Index,
                MaxLightsPerTile = _config.MaxLightsPerTile,
                EnableAABBCulling = 1,
                EnableDepthMaskCulling = 1,
                LightBufferAddress = _context.GpuAddress(_lightBuffer),
                LightGridBufferAddress = _context.GpuAddress(_lightGridBuffer),
                LightIndexBufferAddress = _context.GpuAddress(_lightIndexBuffer),
            }
        );

        cmdBuffer.UpdateBuffer(_meshDrawBuffer, _drawParams);
    }

    /// <summary>
    /// Performs depth prepass to populate the depth buffer.
    /// This is required for accurate light culling in screen space.
    /// </summary>
    private void DepthPrepass(ICommandBuffer cmdBuffer)
    {
        _framebuffer.Colors[0].Texture = TextureResource.Null;
        _framebuffer.DepthStencil.Texture = _depthBuffer;
        cmdBuffer.BeginRendering(_depthPass, _framebuffer, Dependencies.Empty);
        cmdBuffer.BindDepthState(_depthState);
        cmdBuffer.BindRenderPipeline(_depthPassPipeline);
        cmdBuffer.BindIndexBuffer(_indexBuffer, IndexFormat.UI32);

        // Draw main scene geometry
        cmdBuffer.PushConstants(
            new MeshDrawPushConstant()
            {
                FpConstAddress = _fpConstBuffer.GpuAddress,
                MeshDrawId = 0,
            }
        );
        cmdBuffer.DrawIndexed((uint)_mesh.Indices.Count);

        cmdBuffer.EndRendering();
    }

    private Dependencies _lightCullDeps = new();

    /// <summary>
    /// Runs the Forward+ light culling compute shader.
    /// Calculates which lights affect each screen tile based on depth buffer.
    /// </summary>
    private void LightCulling(ICommandBuffer cmdBuffer, int screenWidth, int screenHeight)
    {
        var tileCountX = (uint)((screenWidth + (int)_config.TileSize - 1) / (int)_config.TileSize);
        var tileCountY = (uint)((screenHeight + (int)_config.TileSize - 1) / (int)_config.TileSize);

        cmdBuffer.BindComputePipeline(_cullingPipeline);
        cmdBuffer.PushConstants(_context.GpuAddress(_lightCullingBuffer));
        _lightCullDeps.Textures[0] = _depthBuffer;
        cmdBuffer.DispatchThreadGroups(new Dimensions(tileCountX, tileCountY), _lightCullDeps);
    }

    private readonly Dependencies _renderDeps = new();

    /// <summary>
    /// Main render pass: renders scene geometry with PBR shading using Forward+ lighting.
    /// Only processes lights that were culled for each tile.
    /// </summary>
    private void RenderPass(ICommandBuffer cmdBuffer)
    {
        _framebuffer.Colors[0].Texture = _f16Framebuffer;
        _renderDeps.Buffers[0] = _lightIndexBuffer;
        cmdBuffer.BeginRendering(_renderPass, _framebuffer, _renderDeps);
        cmdBuffer.BindDepthState(DepthState.DefaultReversedZ);
        cmdBuffer.BindRenderPipeline(_renderPipelinePBR);

        // Draw main scene with PBR lighting
        cmdBuffer.PushConstants(
            new MeshDrawPushConstant()
            {
                FpConstAddress = _fpConstBuffer.GpuAddress,
                MeshDrawId = 0,
            }
        );

        cmdBuffer.BindIndexBuffer(_indexBuffer, IndexFormat.UI32);
        cmdBuffer.DrawIndexedIndirect(_meshDrawBuffer, 0, 1, MeshDraw.SizeInBytes);
        //cmdBuffer.DrawIndexed((uint)_mesh.Indices.Count);
        cmdBuffer.BindRenderPipeline(_renderPipelineUnlit);
        cmdBuffer.PushConstants(
            new MeshDrawPushConstant()
            {
                FpConstAddress = _fpConstBuffer.GpuAddress,
                DrawCommandIdxOffset = 1,
            }
        );

        cmdBuffer.DrawIndexedIndirect(
            _meshDrawBuffer,
            MeshDraw.SizeInBytes,
            (uint)_lights.Count,
            MeshDraw.SizeInBytes
        );
        //for (int i = 0; i < _lights.Count; i++)
        //{
        //    // Draw light spheres
        //    cmdBuffer.PushConstants(
        //        new MeshDrawPushConstant()
        //        {
        //            FpConstAddress = _fpConstBuffer.GpuAddress,
        //            MeshDrawId = (uint)i + 1,
        //            DrawCommandIdxOffset = 1,
        //        }
        //    );
        //    cmdBuffer.DrawIndexed((uint)_lightMesh.Indices.Count, 1, (uint)_mesh.Indices.Count);
        //}
        cmdBuffer.EndRendering();
    }

    private Dependencies _toneMappingDeps = new();

    /// <summary>
    /// Applies tone mapping and gamma correction to convert HDR framebuffer to LDR output.
    /// </summary>
    private void ToneGamma(ICommandBuffer cmdBuffer, TextureHandle target)
    {
        _framebuffer.Colors[0].Texture = target;
        _framebuffer.DepthStencil.Texture = TextureResource.Null;
        _toneMappingDeps.Textures[0] = _f16Framebuffer;
        cmdBuffer.BeginRendering(_toneMappingPass, _framebuffer, _toneMappingDeps);
        cmdBuffer.BindRenderPipeline(_toneGammePipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new ToneGammaPushConstants()
            {
                Enabled = 1,
                Exposure = 1f,
                HdrTextureId = _f16Framebuffer.Index,
                SamplerId = _toneMappingSampler.Index,
                TonemapMode = (uint)ToneMappingMode.Uncharted2,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
        cmdBuffer.EndRendering();
    }

    #endregion

    #region Scene Setup

    /// <summary>
    /// Creates a test scene with one directional light and multiple colored point lights.
    /// </summary>
    /// <param name="count">Number of point lights to create.</param>
    /// <returns>Array of lights including one directional light and the specified number of point lights.</returns>
    private FastList<Light> CreateTestLights(int count)
    {
        var lights = new FastList<Light>(count);
        var random = new Random((int)Stopwatch.GetTimestamp());

        // Directional light (sun)
        _dirLights.Lights_0 = new DirectionalLight
        {
            Direction = Vector3.Normalize(new Vector3(0, 0, 1)),
            Color = new Vector3(1),
            Intensity = 0.01f,
        };
        _dirLights.LightCount = 1;

        // Point lights with alternating colors
        var colors = new[]
        {
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(1, 1, 0),
            new Vector3(1, 0, 1),
            new Vector3(0, 1, 1),
        };

        int gridSize = (int)MathF.Ceiling(MathF.Sqrt(count));
        float spacing = 8.0f;
        float offset = (gridSize - 1) * spacing * 0.5f;

        for (int i = 0; i < count - NumSpotLights; i++)
        {
            int x = i % gridSize;
            int y = i / gridSize;

            var position = new Vector3(x * spacing - offset, y * spacing - offset, -5);
            var color = colors[i % colors.Length];

            lights.Add(
                new Light
                {
                    Position = position,
                    Type = 1, // Point light
                    Direction = Vector3.Zero,
                    Range = LightRange,
                    Color = color,
                    Intensity = 10.0f,
                    SpotAngles = Vector2.Zero,
                }
            );
        }
        for (int i = 0; i < NumSpotLights; ++i)
        {
            lights.Add(
                new Light
                {
                    Position = new Vector3((i - NumSpotLights / 2) * 4, 0, -15),
                    Type = 2, // Spot light
                    Direction = Vector3.Normalize(new Vector3(0, 0, 1)),
                    Range = 40,
                    Color = colors[i % colors.Length],
                    Intensity = 100.0f,
                    SpotAngles = new Vector2(
                        MathF.Cos(new AngleSingle(15, AngleType.Degree).Radians),
                        MathF.Cos(new AngleSingle(30, AngleType.Degree).Radians)
                    ),
                }
            );
        }
        return lights;
    }

    #endregion

    #region Animation

    private int _counter = 0;
    private float _offset = 1;

    /// <summary>
    /// Animates lights by moving them horizontally back and forth.
    /// </summary>
    /// <param name="lights">Array of lights to animate.</param>
    /// <param name="time">Current time in seconds.</param>
    private void MoveLights(float time)
    {
        _counter = (_counter + 1) % 10000;
        if (_counter < 5000)
        {
            _offset = 0.004f;
        }
        else
        {
            _offset = -0.004f;
        }

        // Skip first light (directional), only move point lights
        for (int i = 0; i < NumLights - NumSpotLights; i++)
        {
            _lights.GetInternalArray()[i].Position += new Vector3(_offset, 0, 0);
            _drawParams.GetInternalArray()[i + 1].Transform = Matrix4x4.CreateTranslation(
                _lights[i].Position
            );
        }
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Releases all GPU resources.
    /// </summary>
    public void Dispose()
    {
        // Dispose geometry
        _lightMesh.Dispose();
        _mesh.Dispose();

        // Dispose buffers
        _lightBuffer.Dispose();
        _lightGridBuffer.Dispose();
        _lightIndexBuffer.Dispose();
        _pbrPropertiesBuffer.Dispose();
        _fpConstBuffer.Dispose();
        _lightCullingBuffer.Dispose();
        _directionalLightBuffer.Dispose();
        _meshInfoBuffer.Dispose();

        // Dispose pipelines
        _renderPipelinePBR.Dispose();
        _renderPipelineUnlit.Dispose();
        _cullingPipeline.Dispose();
        _depthPassPipeline.Dispose();
        _toneGammePipeline.Dispose();

        // Dispose samplers
        _depthBufferSampler.Dispose();
        _toneMappingSampler.Dispose();

        // Dispose textures
        _depthBuffer.Dispose();
        _f16Framebuffer.Dispose();
    }

    #endregion
}

#region Camera

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
