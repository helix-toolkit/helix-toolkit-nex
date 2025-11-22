using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using SDL3;
using Vortice.Vulkan;

public class Program
{
    public static unsafe void Main()
    {
        using var app = new App();
        app.Run();
    }

    private const string vs = """
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

    private const string ps = """
        #version 460
        layout (location=0) in vec3 color;
        layout (location=0) out vec4 out_FragColor;

        void main() {
        	out_FragColor = vec4(color, 1.0);
        };
        """;

    private class App : Application
    {
        private IContext? _ctx;
        public override string Name => "HelloTriangle";

        private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null;
        private readonly RenderPass _pass = new();
        private readonly Framebuffer _frameBuffer = new();
        private uint _frameCount = 0;

        protected override void Initialize()
        {
            base.Initialize();
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
            _ctx.CreateShaderModuleGlsl(vs, ShaderStage.Vertex, out var vsModule).CheckResult();
            _ctx.CreateShaderModuleGlsl(ps, ShaderStage.Fragment, out var psModule).CheckResult();
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = vsModule,
                FragementShader = psModule,
                Topology = Topology.Triangle,
            };
            pipelineDesc.Colors[0].Format = _ctx.GetSwapchainFormat();
            _ctx.CreateRenderPipeline(pipelineDesc, out _renderPipeline).CheckResult();

            _pass.Colors[0] = new RenderPass.AttachmentDesc
            {
                ClearColor = new Color4(0.1f, 0.2f, 0.3f, 1.0f),
                LoadOp = LoadOp.Clear,
            };
        }

        protected override void OnTick()
        {
            ++_frameCount;
            Debug.Assert(_ctx != null, "Vulkan context should not be null at this point.");

            var tex = _ctx.GetCurrentSwapchainTexture();
            if (tex.Empty)
            {
                return; // No swapchain texture available, nothing to render to.
            }
            var cmdBuffer = _ctx!.AcquireCommandBuffer();
            _frameBuffer.Colors[0].Texture = tex;
            _pass.Colors[0].ClearColor = new Color4((_frameCount % 1000) / 1000f, 0.2f, 0.3f, 1.0f);
            cmdBuffer.BeginRendering(_pass, _frameBuffer, Dependencies.Empty);
            cmdBuffer.BindRenderPipeline(_renderPipeline);
            var aspect = MainWindow.Size.Width / (float)MainWindow.Size.Height;
            var transform = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, _frameCount / 1000f);
            var cam =
                Matrix4x4.CreateLookAt(
                    (-Vector3.UnitZ * 10).TransformCoordinate(transform),
                    Vector3.Zero,
                    Vector3.UnitY
                ) * Matrix4x4.CreateOrthographic(2, 2 * aspect, 0.1f, 100f);

            cmdBuffer.PushConstants(cam);
            cmdBuffer.Draw(3); // Draw 3 vertices (a triangle)
            cmdBuffer.EndRendering();
            _ctx!.Submit(cmdBuffer, tex);
        }

        protected override void HandleResize(int width, int height)
        {
            _ctx!.RecreateSwapchain(width, height);
        }

        protected override void OnDisposing()
        {
            base.OnDisposing();
            _renderPipeline.Dispose();
            _ctx?.Dispose();
        }
    }
}
