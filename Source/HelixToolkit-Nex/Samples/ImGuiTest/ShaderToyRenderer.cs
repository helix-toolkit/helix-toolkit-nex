using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HxColor = HelixToolkit.Nex.Maths.Color;

namespace ImGuiTest;

internal class ShaderToyRenderer : IDisposable
{
    private const string vertexShaderCode = """
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

    private const string fragmentShaderCode = """

        layout(push_constant) uniform constants
        {
            vec3 iResolution;
            uint iChannel0;
            uint iSampler;
            float iTime;
            uint shaderType; // 0 for mainImage, 1 for mainImage1
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

        void mainImage1(out vec4 O, vec2 F)
        {
            //Iterator and attenuation (distance-squared)
            float i = .2, a;
            //Resolution for scaling and centering
            vec2 r = pc.iResolution.xy,
                 //Centered ratio-corrected coordinates
                 p = ( F+F - r ) / r.y / .7,
                 //Diagonal vector for skewing
                 d = vec2(-1,1),
                 //Blackhole center
                 b = p - i*d,
                 //Rotate and apply perspective
                 c = p * mat2(1, 1, d/(.1 + i/dot(b,b))),
                 //Rotate into spiraling coordinates
                 v = c * mat2(cos(.5*log(a=dot(c,c)) + pc.iTime*i + vec4(0,33,11,0)))/i,
                 //Waves cumulative total for coloring
                 w;

            //Loop through waves
            for(; i++<9.; w += 1.+sin(v) )
                //Distort coordinates
                v += .7* sin(v.yx*i+pc.iTime) / i + .5;
            //Acretion disk radius
            i = length( sin(v/.3)*.4 + c*(3.+d) );
            //Red/blue gradient
            O = 1. - exp( -exp( c.x * vec4(.6,-.4,-1,0) )
                           //Wave coloring
                           /  w.xyyx
                           //Acretion disk brightness
                           / ( 2. + i*i/4. - i )
                           //Center darkness
                           / ( .5 + 1. / a )
                           //Rim highlight
                           / ( .03 + abs( length(p)-.7 ) )
                     );
        }

        // End ShaderToy Shader.
        void main()
        {	
            if (pc.shaderType == 0)
            {
                mainImage(fragColor, fragCoord * pc.iResolution.xy);
            }
            else if (pc.shaderType == 1)
            {
                mainImage1(fragColor, fragCoord * pc.iResolution.xy);
            }
            else
            {
                // Default shader if no type matches
                float color = mod(pc.iTime, 10.0) * 0.1;
                fragColor = vec4(color, 0.0, 0.0, 1.0); // Default color if no shader type matches
            }
        }
        """;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PushConstants
    {
        public Vector3 Resolution;
        public uint Channel0;
        public uint Sampler;
        public float Time;
        public uint ShaderType; // 0 for mainImage, 1 for mainImage1
    };

    private readonly IContext _context;
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private readonly RenderPass _pass = new(
        new RenderPass.AttachmentDesc()
        {
            ClearColor = new Color4(0, 0, 0, 1),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        }
    );
    private readonly Framebuffer _fb = new();
    private ShaderModuleResource _vertexShader = ShaderModuleResource.Null;
    private ShaderModuleResource _fragmentShader = ShaderModuleResource.Null;
    private SamplerResource _sampler = SamplerResource.Null;
    private readonly long _startTime = Stopwatch.GetTimestamp();

    public string[] ToyTypes { get; } = ["Option 1", "Option 2", "None"];

    public ShaderToyRenderer(IContext context)
    {
        _context = context;
    }

    public bool Initialize()
    {
        _vertexShader = _context.CreateShaderModuleGlsl(
            vertexShaderCode,
            ShaderStage.Vertex,
            "ShaderRenderer: Vertex"
        );
        _fragmentShader = _context.CreateShaderModuleGlsl(
            fragmentShaderCode,
            ShaderStage.Fragment,
            "ShaderRenderer: Fragment"
        );
        var pipelineDesc = new RenderPipelineDesc()
        {
            VertexShader = _vertexShader,
            FragementShader = _fragmentShader,
            DebugName = "ShaderRenderer: Pipeline",
            Topology = Topology.TriangleStrip,
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
        _pipeline = _context.CreateRenderPipeline(pipelineDesc);

        _sampler = _context.CreateSampler(
            new SamplerStateDesc() { DebugName = "ShaderRenderer: Sampler" }
        );

        return true;
    }

    public void Render(ICommandBuffer cmdBuf, uint type, Vector2 size, TextureResource target)
    {
        _fb.Colors[0].Texture = target;
        cmdBuf.BeginRendering(_pass, _fb, Dependencies.Empty);
        cmdBuf.PushDebugGroupLabel("ShaderRenderer: Render", HxColor.Blue);
        cmdBuf.BindDepthState(new());
        cmdBuf.BindViewport(
            new()
            {
                X = 0,
                Y = 0,
                Width = (uint)size.X,
                Height = (uint)size.Y,
            }
        );
        cmdBuf.BindScissorRect(
            new()
            {
                X = 0,
                Y = 0,
                Width = (uint)size.X,
                Height = (uint)size.Y,
            }
        );
        cmdBuf.BindRenderPipeline(_pipeline);
        var pc = new PushConstants()
        {
            Resolution = new(size, size.X / size.Y),
            Channel0 = 0,
            Sampler = _sampler.Index, // Assuming sampler is bound to channel 0
            Time = (float)Stopwatch.GetElapsedTime(_startTime).TotalSeconds, // Use high-resolution timer for time
            ShaderType = type, // 0 for mainImage, 1 for mainImage1
        };
        cmdBuf.PushConstants(pc);
        cmdBuf.Draw(4);
        cmdBuf.PopDebugGroupLabel();
        cmdBuf.EndRendering();
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
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
