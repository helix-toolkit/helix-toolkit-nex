using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using SDL3;
using System.Diagnostics;
using Vortice.Vulkan;
using static SDL3.SDL3;

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
        void main() {
            gl_Position = vec4(pos[gl_VertexIndex], 0.0, 1.0);
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

        protected override void Initialize()
        {
            base.Initialize();
            vkContext = VulkanBuilder.Create(new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = (instance) =>
                {
                    unsafe
                    {
                        VkSurfaceKHR surface;
                        if (!SDL_Vulkan_CreateSurface(MainWindow.Instance, instance, 0, (ulong**)&surface))
                        {
                            throw new Exception("SDL: failed to create vulkan surface");
                        }
                        return surface;
                    }
                },
            }, MainWindow.Instance, 0);
            var windowSize = MainWindow.Size;
            vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
            unsafe
            {
                var vsBytes = vs.ToArray();
                var psBytes = ps.ToArray();
                using var pVsBytes = vsBytes.Pin();
                using var pPsBytes = psBytes.Pin();
                vkContext.CreateShaderModule(new ShaderModuleDesc() { Data = (nint)pVsBytes.Pointer, DataSize = (uint)vs.Length, Stage = ShaderStage.Vertex, DataType = ShaderDataType.Glsl, DebugName = "vs" }, out var vsModule).CheckResult();
                vkContext.CreateShaderModule(new ShaderModuleDesc() { Data = (nint)pPsBytes.Pointer, DataSize = (uint)ps.Length, Stage = ShaderStage.Fragment, DataType = ShaderDataType.Glsl, DebugName = "ps" }, out var psModule).CheckResult();
                var pipelineDesc = new RenderPipelineDesc
                {
                    SmVert = vsModule,
                    SmFrag = psModule,
                    Topology = Topology.Triangle,
                };
                pipelineDesc.Color[0].Format = vkContext.GetSwapchainFormat();
                vkContext.CreateRenderPipeline(pipelineDesc, out renderPipeline).CheckResult();
            }
        }

        public override void Dispose()
        {
            vkContext?.Dispose();
            base.Dispose();
        }

        protected override void OnTick()
        {
            Debug.Assert(vkContext != null, "Vulkan context should not be null at this point.");

            var tex = vkContext.GetCurrentSwapchainTexture();
            if (tex.Empty)
            {
                return; // No swapchain texture available, nothing to render to.
            }
            var cmdBuffer = vkContext!.AcquireCommandBuffer();
            var pass = new RenderPass();
            var frameBuffer = new Framebuffer();
            frameBuffer.color[0].Texture = tex;
            pass.color[0] = new RenderPass.AttachmentDesc
            {
                clearColor = new Color4(0.1f, 0.2f, 0.3f, 1.0f),
                loadOp = LoadOp.Clear,
            };          
            cmdBuffer.BeginRendering(pass, frameBuffer, Dependencies.Empty);
            cmdBuffer.BindRenderPipeline(renderPipeline);
            cmdBuffer.Draw(3); // Draw 3 vertices (a triangle)
            cmdBuffer.EndRendering();
            vkContext!.Submit(cmdBuffer, tex);
        }

        protected override void HandleResize(in SDL_Event evt)
        {
            vkContext!.RecreateSwapchain(evt.window.data1, evt.window.data2);
        }
    }
}


