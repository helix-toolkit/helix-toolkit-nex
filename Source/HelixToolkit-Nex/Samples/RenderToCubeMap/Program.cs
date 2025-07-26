// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
Console.WriteLine("Hello, World!");
using var app = new App();
app.Run();


class App : Application
{
    [StructLayout(LayoutKind.Sequential)]
    struct PushCube
    {
        public uint Face;
        public float Time;
    }

    const string codeGenCubeMapVS = """
        #version 460
        layout(location= 0) out vec3 color;
        const vec2 pos[3] = vec2[3](
            vec2(-0.6, -0.6),
            vec2(0.6, -0.6),
            vec2(0.0, 0.6)
        );
        const vec3 col[6] = vec3[6](
            vec3(1.0, 0.0, 0.0),
            vec3(0.0, 1.0, 0.0),
            vec3(0.0, 0.0, 1.0),
            vec3(1.0, 0.0, 1.0),
            vec3(1.0, 1.0, 0.0),
            vec3(0.0, 1.0, 1.0)
        );
        layout(push_constant) uniform constants
        {
            uint face;
            float time;
        } pc;

        void main()
        {
            gl_Position = vec4(pos[gl_VertexIndex] * (1.5 + sin(pc.time)) * 0.5, 0.0, 1.0);
            color = col[pc.face];
        }
        """;

    const string codeGenCubeMapFS = """
        #version 460
        layout (location=0) in vec3 color;
        layout(location = 0) out vec4 out_FragColor;

        void main()
        {
            out_FragColor = vec4(color, 1.0);
        }
        ;
        """;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PushRender
    {
        public Matrix4x4 MVP;
        public uint Texture0;
    }

    const string codeVS = """
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

    const string codeFS = """
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

    static readonly ushort[] indexData = [0, 1, 2, 2, 3, 0, 1, 5, 6, 6, 2, 1, 7, 6, 5, 5, 4, 7,
                                    4, 0, 3, 3, 7, 4, 4, 5, 1, 1, 0, 4, 3, 2, 6, 6, 7, 3 ];

    static ILogger logger = LogManager.Create<App>();
    public override string Name => "Render to Cube Map Sample";
    IContext? vkContext;
    TextureHolder cubeMap = TextureHolder.Null;
    BufferHolder indexBuffer = BufferHolder.Null;
    RenderPipelineHolder cubeRenderPipeline = RenderPipelineHolder.Null;
    RenderPipelineHolder genCubeMapPipeline = RenderPipelineHolder.Null;
    RenderPass genCubeMapRenderPass = new RenderPass();
    Framebuffer genCubeMapFramebuffer = new Framebuffer();
    RenderPass renderCubeRenderPass = new RenderPass();
    Framebuffer renderCubeFramebuffer = new Framebuffer();
    Dependencies dependencies = new Dependencies();

    Color4[] clearColors = new Color4[6]
        {
            new (0.3f, 0.1f, 0.1f, 1.0f),
            new (0.1f, 0.3f, 0.1f, 1.0f),
            new (0.1f, 0.1f, 0.3f, 1.0f),
            new (0.3f, 0.1f, 0.3f, 1.0f),
            new (0.3f, 0.3f, 0.1f, 1.0f),
            new(0.1f, 0.3f, 0.3f, 1.0f)
        };

    protected override void Initialize()
    {
        // Initialize the application with a specific scene or settings
        // For example, you might want to load a 3D model or set up a camera
        vkContext = VulkanBuilder.Create(new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            OnCreateSurface = CreateSurface,
        }, MainWindow.Instance, 0);
        var windowSize = MainWindow.Size;
        vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);

        vkContext.CreateBuffer(indexData, BufferUsageBits.Index, StorageType.Device, out indexBuffer, "Buffer: Index").CheckResult();

        vkContext.CreateTexture(new()
        {
            Type = TextureType.TextureCube,
            Dimensions = new() { Width = 512, Height = 512, Depth = 1 },
            Format = Format.BGRA_UN8,
            Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment
        }, out cubeMap, "Cube Map").CheckResult();

        { // Create render pass for generate cube map
            vkContext.CreateShaderModuleGlsl(codeGenCubeMapVS, ShaderStage.Vertex, out var genCubeMapVsModule, "Shader: gen cube map (vert)").CheckResult();
            vkContext.CreateShaderModuleGlsl(codeGenCubeMapFS, ShaderStage.Fragment, out var genCubeMapFsModule, "Shader: gen cube map (frag)").CheckResult();

            var renderToCubeMapPipelineDesc = new RenderPipelineDesc
            {
                SmVert = genCubeMapVsModule,
                SmFrag = genCubeMapFsModule,
                DebugName = "Pipeline: Gen Cube Map"
            };

            renderToCubeMapPipelineDesc.Color[0].Format = vkContext.GetFormat(cubeMap);

            vkContext.CreateRenderPipeline(renderToCubeMapPipelineDesc, out genCubeMapPipeline).CheckResult();
        }


        { // Create render pass for cube map rendering
            vkContext.CreateShaderModuleGlsl(codeVS, ShaderStage.Vertex, out var vsModule, "Shader: main (vert)").CheckResult();
            vkContext.CreateShaderModuleGlsl(codeFS, ShaderStage.Fragment, out var fsModule, "Shader: main (frag)").CheckResult();
            var pipelineDesc = new RenderPipelineDesc
            {
                SmVert = vsModule,
                SmFrag = fsModule,
                DebugName = "Pipeline: Render Box"
            };
            pipelineDesc.Color[0].Format = vkContext.GetSwapchainFormat();

            vkContext.CreateRenderPipeline(pipelineDesc, out cubeRenderPipeline).CheckResult();
        }

        dependencies.Textures[0] = cubeMap;
    }

    protected override void OnTick()
    {
        float fov = 45f * MathF.PI / 180f;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, MainWindow.Size.Width / (float)MainWindow.Size.Height, 0.1f, 100f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 model = Matrix4x4.CreateRotationY((float)(DateTime.Now.TimeOfDay.TotalSeconds % (2 * Math.PI)));
        Matrix4x4 mvp = model * view * proj;

        var cmdBuffer = vkContext!.AcquireCommandBuffer();
        for (int i = 0; i < 6; i++)
        {
            genCubeMapRenderPass.Colors[0] = new RenderPass.AttachmentDesc
            {
                ClearColor = clearColors[i],
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                Layer = (byte)i
            };
            genCubeMapFramebuffer.Colors[0].Texture = cubeMap!;
            cmdBuffer.BeginRendering(genCubeMapRenderPass, genCubeMapFramebuffer, Dependencies.Empty);
            cmdBuffer.BindRenderPipeline(genCubeMapPipeline);
            cmdBuffer.PushConstants(new PushCube
            {
                Face = (uint)i,
                Time = (float)DateTime.Now.TimeOfDay.TotalSeconds
            });
            cmdBuffer.Draw(3); // Draw a triangle for each face
            cmdBuffer.EndRendering();
        }


        renderCubeRenderPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0.1f, 0.2f, 0.3f, 1.0f),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store
        };
        renderCubeFramebuffer.Colors[0].Texture = vkContext.GetCurrentSwapchainTexture();

        var size = MainWindow.Size;
        cmdBuffer.BeginRendering(renderCubeRenderPass, renderCubeFramebuffer, dependencies);
        cmdBuffer.BindRenderPipeline(cubeRenderPipeline);
        cmdBuffer.BindViewport(new ViewportF(0, 0, size.Width, size.Height));
        cmdBuffer.BindScissorRect(new ScissorRect(0, 0, (uint)size.Width, (uint)size.Height));
        cmdBuffer.BindDepthState(new DepthState());
        cmdBuffer.BindIndexBuffer(indexBuffer, IndexFormat.UI16);
        cmdBuffer.PushConstants(new PushRender
        {
            MVP = mvp,
            Texture0 = cubeMap.Index
        });
        cmdBuffer.DrawIndexed((uint)indexData.Length);
        cmdBuffer.EndRendering();

        vkContext.Submit(cmdBuffer, vkContext.GetCurrentSwapchainTexture());
    }
}