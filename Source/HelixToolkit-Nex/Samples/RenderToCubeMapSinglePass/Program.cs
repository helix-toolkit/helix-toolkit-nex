// See https://aka.ms/new-console-template for more information
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;
using Vortice.Vulkan;

Console.WriteLine("Hello, World!");
using var app = new App();
app.Run();

internal class App : Application
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PushCube
    {
        public float Time;
    }

    private const string codeGenCubeMapVS = """
        #version 460
        const vec2 pos[3] = vec2[3](
            vec2(-0.6, -0.6),
            vec2(0.6, -0.6),
            vec2(0.0, 0.6)
        );

        layout(push_constant) uniform constants
        {
            float time;
        } pc;

        void main()
        {
            gl_Position = vec4(pos[gl_VertexIndex] * (1.5 + sin(pc.time)) * 0.5, 0.0, 1.0);
        }
        """;

    private const string codeGenCubeMapFS = """
        #version 460
        layout (location=0) in vec3 color;
        layout (location=0) out vec4 out_FragColor0;
        layout (location=1) out vec4 out_FragColor1;
        layout (location=2) out vec4 out_FragColor2;
        layout (location=3) out vec4 out_FragColor3;
        layout (location=4) out vec4 out_FragColor4;
        layout (location=5) out vec4 out_FragColor5;

        layout(push_constant) uniform constants
        {
            float time;
        } pc;

        void main()
        {
            float t = cos(pc.time);
            out_FragColor0 = vec4(t, 0.0, 0.0, 1.0);
            out_FragColor1 = vec4(0.0, t, 0.0, 1.0);
            out_FragColor2 = vec4(0.0, 0.0, t, 1.0);
            out_FragColor3 = vec4(1.0 - t, 0.0, t, 1.0);
            out_FragColor4 = vec4(1.0 - t, t, 0.0, 1.0);
            out_FragColor5 = vec4(0.0, t, 1.0 - t, 1.0);
        };
        """;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PushRender
    {
        public Matrix4x4 MVP;
        public uint Texture0;
    }

    private const string codeVS = """
        layout (location=0) out vec3 dir;

        const vec3 vertices[8] = vec3[8](
            vec3(-1.0, -1.0, 1.0), vec3(1.0, -1.0, 1.0), vec3(1.0, 1.0, 1.0), vec3(-1.0, 1.0, 1.0),
            vec3(-1.0, -1.0, -1.0), vec3(1.0, -1.0, -1.0), vec3(1.0, 1.0, -1.0), vec3(-1.0, 1.0, -1.0)
        );

        layout(push_constant) uniform constants
        {
            mat4 mvp;
            uint texture0;
        } pc;

        void main()
        {
            vec3 v = vertices[gl_VertexIndex];
            gl_Position = pc.mvp * vec4(v, 1.0);
            dir = v;
        }
        """;

    private const string codeFS = """
        layout (location=0) in vec3 dir;
        layout(location = 1) in flat uint textureId;
        layout(location = 0) out vec4 out_FragColor;
        layout(push_constant) uniform constants
        {
            mat4 mvp;
            uint texture0;
        } pc;
        void main()
        {
            out_FragColor = textureBindlessCube(pc.texture0, 0, normalize(dir));
        }
        ;
        """;

    private static readonly ushort[] indexData =
    [
        0,
        1,
        2,
        2,
        3,
        0,
        1,
        5,
        6,
        6,
        2,
        1,
        7,
        6,
        5,
        5,
        4,
        7,
        4,
        0,
        3,
        3,
        7,
        4,
        4,
        5,
        1,
        1,
        0,
        4,
        3,
        2,
        6,
        6,
        7,
        3,
    ];

    private static readonly ILogger logger = LogManager.Create<App>();
    public override string Name => "Render to Cube Map Sample";
    private IContext? _ctx;
    private TextureResource _cubeMap = TextureResource.Null;
    private BufferResource _indexBuffer = BufferResource.Null;
    private RenderPipelineResource _cubeRenderPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _genCubeMapPipeline = RenderPipelineResource.Null;
    private readonly RenderPass _genCubeMapRenderPass = new();
    private readonly Framebuffer _genCubeMapFramebuffer = new();
    private readonly RenderPass _renderCubeRenderPass = new();
    private readonly Framebuffer _renderCubeFramebuffer = new();
    private readonly Dependencies _dependencies = new();

    private readonly Color4[] _clearColors =
    [
        new(0.3f, 0.1f, 0.1f, 1.0f),
        new(0.1f, 0.3f, 0.1f, 1.0f),
        new(0.1f, 0.1f, 0.3f, 1.0f),
        new(0.3f, 0.1f, 0.3f, 1.0f),
        new(0.3f, 0.3f, 0.1f, 1.0f),
        new(0.1f, 0.3f, 0.3f, 1.0f),
    ];

    protected override void Initialize()
    {
        // Initialize the application with a specific scene or settings
        // For example, you might want to load a 3D model or set up a camera
        _ctx = VulkanBuilder.Create(
            new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = CreateSurface,
            },
            MainWindow.Instance,
            0
        );
        var windowSize = MainWindow.Size;
        _ctx.RecreateSwapchain(windowSize.Width, windowSize.Height);

        _ctx.CreateBuffer(
                indexData,
                BufferUsageBits.Index,
                StorageType.Device,
                out _indexBuffer,
                "Buffer: Index"
            )
            .CheckResult();

        _ctx.CreateTexture(
                new()
                {
                    Type = TextureType.TextureCube,
                    Dimensions = new()
                    {
                        Width = 512,
                        Height = 512,
                        Depth = 1,
                    },
                    Format = Format.BGRA_UN8,
                    Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                },
                out _cubeMap,
                "Cube Map"
            )
            .CheckResult();

        { // Create render pass for generate cube map
            _ctx.CreateShaderModuleGlsl(
                    codeGenCubeMapVS,
                    ShaderStage.Vertex,
                    out var genCubeMapVsModule,
                    "Shader: gen cube map (vert)"
                )
                .CheckResult();
            _ctx.CreateShaderModuleGlsl(
                    codeGenCubeMapFS,
                    ShaderStage.Fragment,
                    out var genCubeMapFsModule,
                    "Shader: gen cube map (frag)"
                )
                .CheckResult();

            var renderToCubeMapPipelineDesc = new RenderPipelineDesc
            {
                VertexShader = genCubeMapVsModule,
                FragementShader = genCubeMapFsModule,
                DebugName = "Pipeline: Gen Cube Map",
            };
            for (int i = 0; i < 6; i++)
            {
                renderToCubeMapPipelineDesc.Colors[i].Format = _ctx.GetFormat(_cubeMap);
            }

            _ctx.CreateRenderPipeline(renderToCubeMapPipelineDesc, out _genCubeMapPipeline)
                .CheckResult();
            for (int i = 0; i < 6; i++)
            {
                _genCubeMapRenderPass.Colors[i] = new RenderPass.AttachmentDesc
                {
                    ClearColor = _clearColors[i],
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    Layer = (byte)i,
                };
                _genCubeMapFramebuffer.Colors[i].Texture = _cubeMap!;
            }
        }

        { // Create render pass for cube map rendering
            _ctx.CreateShaderModuleGlsl(
                    codeVS,
                    ShaderStage.Vertex,
                    out var vsModule,
                    "Shader: main (vert)"
                )
                .CheckResult();
            _ctx.CreateShaderModuleGlsl(
                    codeFS,
                    ShaderStage.Fragment,
                    out var fsModule,
                    "Shader: main (frag)"
                )
                .CheckResult();
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = vsModule,
                FragementShader = fsModule,
                DebugName = "Pipeline: Render Box",
            };
            pipelineDesc.Colors[0].Format = _ctx.GetSwapchainFormat();

            _ctx.CreateRenderPipeline(pipelineDesc, out _cubeRenderPipeline).CheckResult();
        }

        _dependencies.Textures[0] = _cubeMap;
    }

    protected override void OnTick()
    {
        float fov = 45f * MathF.PI / 180f;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            fov,
            MainWindow.Size.Width / (float)MainWindow.Size.Height,
            0.1f,
            100f
        );
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 model = Matrix4x4.CreateRotationY(
            (float)(DateTime.Now.TimeOfDay.TotalSeconds % (2 * Math.PI))
        );
        model *= Matrix4x4.CreateRotationX(
            (float)(DateTime.Now.TimeOfDay.TotalSeconds % (4 * Math.PI))
        );
        Matrix4x4 mvp = model * view * proj;

        var cmdBuffer = _ctx!.AcquireCommandBuffer();

        cmdBuffer.BeginRendering(_genCubeMapRenderPass, _genCubeMapFramebuffer, Dependencies.Empty);
        cmdBuffer.BindRenderPipeline(_genCubeMapPipeline);
        cmdBuffer.PushConstants(new PushCube { Time = (float)DateTime.Now.TimeOfDay.TotalSeconds });
        cmdBuffer.Draw(3); // Draw a triangle for each face
        cmdBuffer.EndRendering();

        _renderCubeRenderPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0.1f, 0.2f, 0.3f, 1.0f),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
        _renderCubeFramebuffer.Colors[0].Texture = _ctx.GetCurrentSwapchainTexture();

        var size = MainWindow.Size;
        cmdBuffer.BeginRendering(_renderCubeRenderPass, _renderCubeFramebuffer, _dependencies);
        cmdBuffer.BindRenderPipeline(_cubeRenderPipeline);
        cmdBuffer.BindViewport(new ViewportF(0, 0, size.Width, size.Height));
        cmdBuffer.BindScissorRect(new ScissorRect(0, 0, (uint)size.Width, (uint)size.Height));
        cmdBuffer.BindDepthState(new DepthState());
        cmdBuffer.BindIndexBuffer(_indexBuffer, IndexFormat.UI16);
        cmdBuffer.PushConstants(new PushRender { MVP = mvp, Texture0 = _cubeMap.Index });
        cmdBuffer.DrawIndexed((uint)indexData.Length);
        cmdBuffer.EndRendering();

        _ctx.Submit(cmdBuffer, _ctx.GetCurrentSwapchainTexture());
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        if (_ctx != null)
        {
            _ctx.Destroy(_genCubeMapPipeline);
            _ctx.Destroy(_cubeRenderPipeline);
            _ctx.Destroy(_cubeMap);
            _ctx.Destroy(_indexBuffer);
            _ctx.Dispose();
        }
    }
}
