using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Shaders;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Examples;

/// <summary>
/// Complete example demonstrating Forward+ rendering with bindless buffers.
/// </summary>
public class ForwardPlusExample
{
    private static readonly int NumPointLights = 20;
    private readonly IContext _context;
    private BufferResource _lightCullingBuffer = BufferResource.Null;
    private BufferResource _lightBuffer = BufferResource.Null;
    private BufferResource _lightGridBuffer = BufferResource.Null;
    private BufferResource _lightIndexBuffer = BufferResource.Null;
    private BufferResource _counterBuffer = BufferResource.Null;
    private BufferResource _modelMatrixBuffer = BufferResource.Null;
    private BufferResource _pbrPropertiesBuffer = BufferResource.Null;
    private BufferResource _fpConstBuffer = BufferResource.Null;
    private TextureResource _f16Framebuffer = TextureResource.Null;
    private TextureResource _depthBuffer = TextureResource.Null;
    private RenderPipelineResource _depthPassPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _renderPipelinePBR = RenderPipelineResource.Null;
    private RenderPipelineResource _renderPipelineUnlit = RenderPipelineResource.Null;
    private RenderPipelineResource _toneGammePipeline = RenderPipelineResource.Null;
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private SamplerResource _depthBufferSampler = SamplerResource.Null;
    private SamplerResource _toneMappingSampler = SamplerResource.Null;

    private ForwardPlusLightCulling.Config _config = ForwardPlusLightCulling.Config.Default;
    private Light[] _lights = Array.Empty<Light>();
    private Matrix4x4[] _modelMatrices = new Matrix4x4[NumPointLights + 1];
    private readonly Geometry _lightMesh;
    private readonly Geometry _mesh;
    private readonly PBRProperties[] _pbrProperties = new PBRProperties[NumPointLights + 1];

    private readonly Dependencies _dependencies = Dependencies.Empty;
    private readonly RenderPass _depthPass = new();
    private readonly RenderPass _renderPass = new();
    private readonly RenderPass _toneMappingPass = new();
    private readonly Framebuffer _framebuffer = new();

    private DepthState _depthState = DepthState.DefaultInvZ;
    private DepthState _depthStateNoWrite = new DepthState
    {
        CompareOp = CompareOp.GreaterEqual,
        IsDepthWriteEnabled = false,
    };
    private MeshDraw _drawParams = new();

    public ForwardPlusExample(IContext context)
    {
        HxDebug.EnableDebugAssertions = true;
        _context = context;
        var builder = new MeshBuilder(true, true, true);
        builder.AddSphere(Vector3.Zero, 0.1f);
        _lightMesh = builder.ToMesh().ToGeometry();
        builder = new MeshBuilder(true, true, true);
        builder.AddBox(new Vector3(0, 0, 0), 10, 10, 2);
        _mesh = builder.ToMesh().ToGeometry();

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
        _lights = CreateTestLights(NumPointLights); // 100 point lights
        _modelMatrices[0] = Matrix4x4.Identity;
        _pbrProperties[0] = new()
        {
            Albedo = new Vector3(1f, 1f, 1f),
            Metallic = 0.8f,
            Roughness = 0.2f,
            Ao = 1f,
        };
        for (int i = 0; i < NumPointLights; i++)
        {
            _modelMatrices[i + 1] = Matrix4x4.CreateTranslation(_lights[i + 1].Position);
            _pbrProperties[i + 1] = new()
            {
                Ao = 1f,
                Emissive = _lights[i + 1].Color * _lights[i + 1].Intensity,
                Opacity = 1,
            };
        }
    }

    public void Initialize(int screenWidth, int screenHeight)
    {
        _lightMesh.UpdateBuffers(_context);
        _mesh.UpdateBuffers(_context);

        // 2. Create lights
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

        _lightCullingBuffer = _context.CreateBuffer(
            new BufferDesc
            {
                DataSize = LightCullingConstants.SizeInBytes,
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightCulling"
        );

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
        // 4. Create light culling compute pipeline
        var cullingShader = ForwardPlusLightCulling.GenerateComputeShader(_config);
        var cullingModule = _context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "ForwardPlus_CullingCompute"
        );
        _cullingPipeline = _context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );

        _toneMappingSampler = _context.CreateSampler(
            new SamplerStateDesc
            {
                MinFilter = SamplerFilter.Nearest,
                MagFilter = SamplerFilter.Nearest,
            }
        );

        // 5. Create render pipeline with Forward+
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithSimpleLighting(false)
            .ConfigForwardPlus(_config);

        var shaderResult = builder.BuildMaterialPipeline(_context, "ForwardPlus_Render");
        // Create the actual render pipeline
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragementShader = shaderResult.FragmentShader,
                DebugName = "ForwardPlus_RenderPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_F16;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.SpecInfo.Entries[0].ConstantId = 0;
            pipelineDesc.SpecInfo.Entries[0].Size = sizeof(uint);
            pipelineDesc.SpecInfo.Data = new byte[sizeof(uint)];
            using var pData = pipelineDesc.SpecInfo.Data.Pin();
            unsafe
            {
                NativeHelper.Write((nint)pData.Pointer, 0u);
            }
            _renderPipelinePBR = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_renderPipelinePBR.Valid);
        }

        {
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = shaderResult.VertexShader,
                FragementShader = shaderResult.FragmentShader,
                DebugName = "ForwardPlus_UnlitPipeline",
                CullMode = CullMode.Back,
                FrontFaceWinding = WindingMode.CCW,
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_F16;
            pipelineDesc.DepthFormat = Format.Z_F32;
            pipelineDesc.SpecInfo.Entries[0].ConstantId = 0;
            pipelineDesc.SpecInfo.Entries[0].Size = sizeof(uint);
            pipelineDesc.SpecInfo.Data = new byte[sizeof(uint)];
            using var pData = pipelineDesc.SpecInfo.Data.Pin();
            unsafe
            {
                NativeHelper.Write((nint)pData.Pointer, 1u);
            }
            _renderPipelineUnlit = _context.CreateRenderPipeline(pipelineDesc);
            Debug.Assert(_renderPipelineUnlit.Valid);
        }

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
            pipelineDesc.FragementShader = _context.CreateShaderModuleGlsl(
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
    }

    public void Render(
        ICommandBuffer cmdBuffer,
        TextureHandle target,
        Camera camera,
        int screenWidth,
        int screenHeight
    )
    {
        MoveLights(_lights, (float)DateTime.Now.TimeOfDay.TotalSeconds);
        cmdBuffer.UpdateBuffer(_lightBuffer, _lights, (uint)_lights.Length);
        // Step 1: Reset _counter
        cmdBuffer.FillBuffer(_counterBuffer, 0, sizeof(uint), 0);
        // Update model matrices if needed
        cmdBuffer.UpdateBuffer(_modelMatrixBuffer, _modelMatrices, (uint)_modelMatrices.Length);

        float aspect = (float)screenWidth / screenHeight;
        var view = camera.CreateView();
        var proj = camera.CreatePerspective(aspect);
        var invView = MatrixHelper.PsudoInvert(view);
        var invPersp = camera.CreateInversePerspective(aspect);
        var invViewProj = invPersp * invView;
        var tileCountX = (screenWidth + (int)_config.TileSize - 1) / (int)_config.TileSize;
        var tileCountY = (screenHeight + (int)_config.TileSize - 1) / (int)_config.TileSize;

        cmdBuffer.UpdateBuffer(
            _fpConstBuffer,
            new FPConstants()
            {
                ViewProjection = view * proj,
                InverseViewProjection = invViewProj,
                CameraPosition = camera.Position,
                Time = (float)DateTime.Now.TimeOfDay.TotalSeconds,
                LightBufferAddress = _context.GpuAddress(_lightBuffer),
                LightGridBufferAddress = _context.GpuAddress(_lightGridBuffer),
                LightIndexBufferAddress = _context.GpuAddress(_lightIndexBuffer),
                ModelMatrixBufferAddress = _context.GpuAddress(_modelMatrixBuffer),
                MaterialBufferAddress = _context.GpuAddress(_pbrPropertiesBuffer),
                PerModelParamsBufferAddress = _context.GpuAddress(_fpConstBuffer),
                LightCount = (uint)_lights.Length,
                TileSize = _config.TileSize,
                ScreenDimensions = new Vector2(screenWidth, screenHeight),
                TileCountX = (uint)tileCountX,
                TileCountY = (uint)tileCountY,
            }
        );

        cmdBuffer.UpdateBuffer(
            _lightCullingBuffer,
            new LightCullingConstants
            {
                ViewMatrix = view,
                InverseProjection = invPersp,
                ScreenDimensions = new Vector2(screenWidth, screenHeight),
                TileCountX = (uint)tileCountX,
                TileCountY = (uint)tileCountY,
                LightCount = (uint)_lights.Length,
                ZNear = camera.NearPlane,
                ZFar = camera.FarPlane,
                DepthTextureIndex = _depthBuffer.Index,
                SamplerIndex = _depthBufferSampler.Index,
                LightBufferAddress = _context.GpuAddress(_lightBuffer),
                LightGridBufferAddress = _context.GpuAddress(_lightGridBuffer),
                LightIndexBufferAddress = _context.GpuAddress(_lightIndexBuffer),
                GlobalCounterBufferAddress = _context.GpuAddress(_counterBuffer),
            }
        );

        DepthPrepass(cmdBuffer);
        LightCulling(cmdBuffer, (uint)tileCountX, (uint)tileCountY);
        RenderPass(cmdBuffer);
        ToneGamma(cmdBuffer, target);
    }

    private void DepthPrepass(ICommandBuffer cmdBuffer)
    {
        _framebuffer.Colors[0].Texture = TextureResource.Null;
        _framebuffer.DepthStencil.Texture = _depthBuffer;
        cmdBuffer.BeginRendering(_depthPass, _framebuffer, _dependencies);
        cmdBuffer.BindDepthState(_depthState);
        cmdBuffer.BindRenderPipeline(_depthPassPipeline);
        cmdBuffer.BindIndexBuffer(_mesh.IndexBuffer, IndexFormat.UI32);

        _drawParams.ForwardPlusConstantsAddress = _context.GpuAddress(_fpConstBuffer);
        _drawParams.VertexBufferAddress = _context.GpuAddress(_mesh.VertexBuffer);
        _drawParams.ModelId = 0;
        _drawParams.MaterialId = 0;
        cmdBuffer.PushConstants(_drawParams);

        // Draw without binding vertex buffers (bindless!)
        cmdBuffer.DrawIndexed((uint)_mesh.Indices.Count);

        // Draw light spheres
        cmdBuffer.BindIndexBuffer(_lightMesh.IndexBuffer, IndexFormat.UI32);
        _drawParams.VertexBufferAddress = _context.GpuAddress(_lightMesh.VertexBuffer);
        for (uint i = 1; i < _lights.Length; ++i)
        {
            _drawParams.ModelId = i;
            _drawParams.MaterialId = i;
            cmdBuffer.PushConstants(_drawParams);
            cmdBuffer.DrawIndexed((uint)_lightMesh.Indices.Count);
        }
        cmdBuffer.EndRendering();
    }

    private void LightCulling(ICommandBuffer cmdBuffer, uint tileX, uint tileY)
    {
        // Step 2: Run light culling compute shader
        cmdBuffer.BindComputePipeline(_cullingPipeline);
        // Push constants for culling
        cmdBuffer.PushConstants(_context.GpuAddress(_lightCullingBuffer));
        // Dispatch culling (one thread group per tile)
        cmdBuffer.DispatchThreadGroups(new Dimensions(tileX, tileY), Dependencies.Empty);
    }

    private void RenderPass(ICommandBuffer cmdBuffer)
    {
        // Step 3: Render scene with Forward+
        _framebuffer.Colors[0].Texture = _f16Framebuffer;
        cmdBuffer.BeginRendering(_renderPass, _framebuffer, _dependencies);
        cmdBuffer.BindDepthState(_depthStateNoWrite);
        cmdBuffer.BindRenderPipeline(_renderPipelinePBR);
        cmdBuffer.BindIndexBuffer(_mesh.IndexBuffer, IndexFormat.UI32);

        _drawParams.ForwardPlusConstantsAddress = _context.GpuAddress(_fpConstBuffer);
        _drawParams.VertexBufferAddress = _context.GpuAddress(_mesh.VertexBuffer);
        _drawParams.ModelId = 0;
        _drawParams.MaterialId = 0;
        cmdBuffer.PushConstants(_drawParams);

        // Draw without binding vertex buffers (bindless!)
        cmdBuffer.DrawIndexed((uint)_mesh.Indices.Count);

        // Draw light spheres
        cmdBuffer.BindRenderPipeline(_renderPipelineUnlit);
        cmdBuffer.BindIndexBuffer(_lightMesh.IndexBuffer, IndexFormat.UI32);
        _drawParams.VertexBufferAddress = _context.GpuAddress(_lightMesh.VertexBuffer);
        for (uint i = 1; i < _lights.Length; ++i)
        {
            _drawParams.ModelId = i;
            _drawParams.MaterialId = i;
            cmdBuffer.PushConstants(_drawParams);
            cmdBuffer.DrawIndexed((uint)_lightMesh.Indices.Count);
        }

        cmdBuffer.EndRendering();
    }

    private void ToneGamma(ICommandBuffer cmdBuffer, TextureHandle target)
    {
        // Step 4: Tone mapping pass
        _framebuffer.Colors[0].Texture = target;
        _framebuffer.DepthStencil.Texture = TextureResource.Null;
        cmdBuffer.BeginRendering(_toneMappingPass, _framebuffer, _dependencies);
        cmdBuffer.BindRenderPipeline(_toneGammePipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new ToneGammaPushConstants()
            {
                Exposure = 1f,
                HdrTextureId = _f16Framebuffer.Index,
                SamplerId = _toneMappingSampler.Index,
                TonemapMode = 0,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
        cmdBuffer.EndRendering();
    }

    private Light[] CreateTestLights(int count)
    {
        var lights = new Light[count + 1];
        var random = new Random((int)Stopwatch.GetTimestamp());
        lights[0] = new Light
        {
            Position = new Vector3(0, 0, -10),
            Type = 0, // Directional light
            Direction = Vector3.Normalize(new Vector3(0, 0, 1)),
            Range = 1000.0f,
            Color = new Vector3(1),
            Intensity = 0.01f,
            SpotAngles = Vector2.Zero,
        };
        var colors = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        for (int i = 0; i < count; i++)
        {
            var position = new Vector3(i - count / 2, i - count / 2, -2);

            var color = colors[i % colors.Length];

            lights[i + 1] = new Light
            {
                Position = position,
                Type = 1, // Point light
                Direction = Vector3.Zero,
                Range = 10f,
                Color = color,
                Intensity = 1.0f,
                SpotAngles = Vector2.Zero,
            };
        }

        return lights;
    }

    private int _counter = 0;
    private float _offset = 1;

    private void MoveLights(Light[] lights, float time)
    {
        _counter = (_counter + 1) % 10000;
        if (_counter < 5000)
        {
            _offset = 0.001f;
        }
        else
        {
            _offset = -0.001f;
        }
        for (int i = 1; i < lights.Length; i++)
        {
            _lights[i].Position += new Vector3(_offset, 0, 0);
            _modelMatrices[i] = Matrix4x4.CreateTranslation(_lights[i].Position);
        }
    }

    public void Dispose()
    {
        _lightMesh.Dispose();
        _mesh.Dispose();
        _lightBuffer.Dispose();
        _lightGridBuffer.Dispose();
        _lightIndexBuffer.Dispose();
        _counterBuffer.Dispose();
        _renderPipelinePBR.Dispose();
        _cullingPipeline.Dispose();
        _depthBufferSampler.Dispose();
        _toneMappingSampler.Dispose();
        _depthBuffer.Dispose();
        _f16Framebuffer.Dispose();
        _depthPassPipeline.Dispose();
        _modelMatrixBuffer.Dispose();
        _pbrPropertiesBuffer.Dispose();
        _fpConstBuffer.Dispose();
        _renderPipelineUnlit.Dispose();
        _lightCullingBuffer.Dispose();
        _toneGammePipeline.Dispose();
    }
}

/// <summary>
/// Simple camera structure for the example.
/// </summary>
public sealed class Camera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up = Vector3.UnitY;
    public float NearPlane = 0.01f;
    public float FarPlane = 1000;
    public float Fov = 45 * MathF.PI / 180;

    public Matrix4x4 CreateView()
    {
        return MatrixHelper.LookAtRH(Position, Target, Up);
    }

    public Matrix4x4 CreatePerspective(float aspect)
    {
        return MatrixHelper.PerspectiveFovRHReverseZ(Fov, aspect, NearPlane, FarPlane);
    }

    public Matrix4x4 CreateInversePerspective(float aspect)
    {
        return MatrixHelper.InversedPerspectiveFovRHReverseZ(Fov, aspect, NearPlane, FarPlane);
    }
}
