using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using HxColor = HelixToolkit.Nex.Maths.Color;
namespace ImGuiTest;

internal class ShaderRenderer : IDisposable
{
    const string vertexShaderCode = """
        #version 460
        // Vertex shader for rendering a full-screen quad using gl_VertexIndex
        layout(location = 0) out vec2 fragCoord;

        void main()
        {
            // Define quad vertices in NDC
            vec2 positions[4] = vec2[](
                vec2(-1.0, -1.0),
                vec2( 1.0, -1.0),
                vec2(-1.0,  1.0),
                vec2( 1.0,  1.0)
            );
            vec2 texCoords[4] = vec2[](
                vec2(0.0, 0.0),
                vec2(1.0, 0.0),
                vec2(0.0, 1.0),
                vec2(1.0, 1.0)
            );

            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = texCoords[gl_VertexIndex];
        }
        """;

    const string fragmentShaderCode = """

        layout(push_constant) uniform constants
        {
            vec3 iResolution;
            uint iChannel0;
            uint iSampler;
            float iTime;
        } pc;

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 fragColor;
        // Begin ShaderToy Shader.
        // https://www.shadertoy.com/view/w3KGRK
        float sdf(in vec3 pos){
            pos = mod(pos, 10.);
            return length(pos - vec3(5.)) - 1.;
        }

        void mainImage( out vec4 fragColor, in vec2 fragCoord )
        {
            vec2 uv = (fragCoord * 2. - pc.iResolution.xy)/max(pc.iResolution.x, pc.iResolution.y);

            // Move and rotate camera over time
            vec3 origin = vec3(0., 5., 0.) * pc.iTime;
            float angle = radians(pc.iTime*3.);
            uv *= mat2(cos(angle), -sin(angle), sin(angle), cos(angle));

            // Use spherical projection for ray direction
            vec3 ray_dir = vec3(sin(uv.x), cos(uv.x)*cos(uv.y), sin(uv.y));
            vec3 ray_pos = vec3(origin);

            float ray_length = 0.;

            for(float i = 0.; i < 7.; i++){
                float dist = sdf(ray_pos);
                ray_length += dist;
                ray_pos += ray_dir * dist;
                // Push rays outward with increasing distance
                ray_dir = normalize(ray_dir + vec3(uv.x, 0., uv.y) * dist * .3);
            }

            vec3 o = vec3(sdf(ray_pos));
            o = cos(o + vec3(6.,0,.5));
            o *= smoothstep(38., 20., ray_length);

            fragColor = vec4(o, 1.);
        }
        
        // End ShaderToy Shader.
        void main()
        {	
            mainImage(fragColor, fragCoord * pc.iResolution.xy);
        }
        """;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PushConstants
    {
        public Vector3 iResolution;
        public uint iChannel0;
        public uint iSampler;
        public float iTime;
    };

    readonly IContext context;
    RenderPipelineResource pipeline = RenderPipelineResource.Null;
    readonly RenderPass pass = new(new RenderPass.AttachmentDesc() { ClearColor = new Color4(0, 0, 0, 1), LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store });
    readonly Framebuffer fb = new();
    ShaderModuleResource vertexShader = ShaderModuleResource.Null;
    ShaderModuleResource fragmentShader = ShaderModuleResource.Null;
    SamplerResource sampler = SamplerResource.Null;
    long startTime = Stopwatch.GetTimestamp();
    public ShaderRenderer(IContext context)
    {
        this.context = context;
    }

    public bool Initialize()
    {
        vertexShader = context.CreateShaderModuleGlsl(vertexShaderCode, ShaderStage.Vertex, "ShaderRenderer: Vertex");
        fragmentShader = context.CreateShaderModuleGlsl(fragmentShaderCode, ShaderStage.Fragment, "ShaderRenderer: Fragment");
        var pipelineDesc = new RenderPipelineDesc()
        {
            VertexShader = vertexShader,
            FragementShader = fragmentShader,
            DebugName = "ShaderRenderer: Pipeline",
            Topology = Topology.TriangleStrip,
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
        pipeline = context.CreateRenderPipeline(pipelineDesc);

        sampler = context.CreateSampler(new SamplerStateDesc() { DebugName = "ShaderRenderer: Sampler" });

        //using var image = Image.Load<Rgba32>("Assets/noise.png");
        //if (image == null || image.Width == 0 || image.Height == 0)
        //{
        //    throw new Exception("Failed to load noise texture.");
        //}
        //if (image!.DangerousTryGetSinglePixelMemory(out var pixels))
        //{
        //    using var data = pixels.Pin();
        //    unsafe
        //    {
        //        var textureDesc = new TextureDesc()
        //        {
        //            Type = TextureType.Texture2D,
        //            Dimensions = new((uint)image.Width, (uint)image.Height, 1),
        //            Format = Format.RGBA_SRGB8,
        //            Usage = TextureUsageBits.Sampled,
        //            Data = (nint)data.Pointer,
        //            DataSize = (uint)(image.Width * image.Height * Marshal.SizeOf<Rgba32>()),
        //        };
        //    }
        //}

        return true;
    }

    public void Render(ICommandBuffer cmdBuf, Vector2 windowSize, TextureResource target)
    {
        fb.Colors[0].Texture = target;
        cmdBuf.BeginRendering(pass, fb, Dependencies.Empty);
        cmdBuf.PushDebugGroupLabel("ShaderRenderer: Render", HxColor.Blue);
        cmdBuf.BindDepthState(new());
        cmdBuf.BindViewport(new() { X = 0, Y = 0, Width = (uint)windowSize.X, Height = (uint)windowSize.Y });
        cmdBuf.BindRenderPipeline(pipeline);
        var pc = new PushConstants()
        {
            iResolution = new(windowSize, windowSize.X / windowSize.Y),
            iChannel0 = 0,
            iSampler = sampler.Index, // Assuming sampler is bound to channel 0
            iTime = (float)Stopwatch.GetElapsedTime(startTime).TotalSeconds, // Use high-resolution timer for time
        };
        cmdBuf.PushConstants(pc);
        cmdBuf.Draw(4);
        cmdBuf.PopDebugGroupLabel();
        cmdBuf.EndRendering();
    }

    private bool disposedValue;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~ShaderRenderer()
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
}
