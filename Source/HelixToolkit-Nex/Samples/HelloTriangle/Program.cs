using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using SDL3;
using System.Diagnostics;
using System.Numerics;
using Vortice.Vulkan;

public class Program
{

    public unsafe static void Main()
    {
        using var app = new App();
        app.Run();
    }

    const string vs = """
        #version 460
        layout (location=0) out vec3 color;
        const vec2 pos[3] = vec2[3](
            vec2(-0.6, -0.4),
            vec2( 0.6, -0.4),
            vec2( 0.0,  0.6)
        );
        const vec3 col[3] = vec3[3](
            vec3(1.0, 0.0, 0.0),
            vec3(0.0, 1.0, 0.0),
            vec3(0.0, 0.0, 1.0)
        );

        layout (push_constant) uniform constants {
            mat4 transform;
        } PushConstants;

        void main() {
            vec4 pos = vec4(pos[gl_VertexIndex], 0.0, 1) * PushConstants.transform;
            gl_Position = pos;
            color = col[gl_VertexIndex];
        }
        """;

    const string ps = """
        #version 460
        layout (location=0) in vec3 color;
        layout (location=0) out vec4 out_FragColor;

        void main() {
        	out_FragColor = vec4(color, 1.0);
        };
        """;

    class App : Application
    {
        IContext? vkContext;
        public override string Name => "HelloTriangle";

        RenderPipelineHolder renderPipeline = RenderPipelineHolder.Null;
        RenderPass pass = new();
        Framebuffer frameBuffer = new();
        uint frameCount = 0;

        protected override void Initialize()
        {
            base.Initialize();
            vkContext = VulkanBuilder.Create(new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = CreateSurface,
            }, MainWindow.Instance, 0);
            var windowSize = MainWindow.Size;
            vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
            vkContext.CreateShaderModuleGlsl(vs, ShaderStage.Vertex, out var vsModule).CheckResult();
            vkContext.CreateShaderModuleGlsl(ps, ShaderStage.Fragment, out var psModule).CheckResult();
            var pipelineDesc = new RenderPipelineDesc
            {
                SmVert = vsModule,
                SmFrag = psModule,
                Topology = Topology.Triangle,
            };
            pipelineDesc.Color[0].Format = vkContext.GetSwapchainFormat();
            vkContext.CreateRenderPipeline(pipelineDesc, out renderPipeline).CheckResult();

            pass.Colors[0] = new RenderPass.AttachmentDesc
            {
                ClearColor = new Color4(0.1f, 0.2f, 0.3f, 1.0f),
                LoadOp = LoadOp.Clear,
            };
        }

        protected override void OnTick()
        {
            ++frameCount;
            Debug.Assert(vkContext != null, "Vulkan context should not be null at this point.");

            var tex = vkContext.GetCurrentSwapchainTexture();
            if (tex.Empty)
            {
                return; // No swapchain texture available, nothing to render to.
            }
            var cmdBuffer = vkContext!.AcquireCommandBuffer();
            frameBuffer.Colors[0].Texture = tex;
            pass.Colors[0].ClearColor = new Color4((frameCount % 1000) / 1000f, 0.2f, 0.3f, 1.0f);
            cmdBuffer.BeginRendering(pass, frameBuffer, Dependencies.Empty);
            cmdBuffer.BindRenderPipeline(renderPipeline);
            var aspect = MainWindow.Size.Width / (float)MainWindow.Size.Height;
            var transform = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, frameCount / 1000f);
            var cam = Matrix4x4.CreateLookAt((-Vector3.UnitZ * 10).TransformCoordinate(transform), Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreateOrthographic(2, 2 * aspect, 0.1f, 100f);

            cmdBuffer.PushConstants(cam);
            cmdBuffer.Draw(3); // Draw 3 vertices (a triangle)
            cmdBuffer.EndRendering();
            vkContext!.Submit(cmdBuffer, tex);
        }

        protected override void HandleResize(in SDL_Event evt)
        {
            vkContext!.RecreateSwapchain(evt.window.data1, evt.window.data2);
        }

        protected override void OnDisposing()
        {
            base.OnDisposing();
            renderPipeline.Dispose();
            vkContext?.Dispose();
        }
    }
}


