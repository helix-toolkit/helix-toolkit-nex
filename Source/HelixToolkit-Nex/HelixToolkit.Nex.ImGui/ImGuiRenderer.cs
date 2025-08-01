﻿using HelixToolkit.Nex.Graphics;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using Gui = ImGuiNET.ImGui;

namespace HelixToolkit.Nex.ImGui;

public class ImGuiRenderer(IContext context, ImGuiConfig config) : IDisposable
{
    const string codeVS = """
        layout(location = 0) out vec4 out_color;
        layout(location = 1) out vec2 out_uv;

        struct Vertex
        {
            float x, y;
            float u, v;
            uint rgba;
        };

        layout(std430, buffer_reference) readonly buffer VertexBuffer
        {
            Vertex vertices [];
        };

        layout(push_constant) uniform PushConstants
        {
            vec4 LRTB;
            VertexBuffer vb;
            uint textureId;
            uint samplerId;
        } pc;

        void main()
        {
            float L = pc.LRTB.x;
            float R = pc.LRTB.y;
            float T = pc.LRTB.z;
            float B = pc.LRTB.w;
            mat4 proj = mat4(
              2.0 / (R - L), 0.0, 0.0, 0.0,
              0.0, 2.0 / (T - B), 0.0, 0.0,
              0.0, 0.0, -1.0, 0.0,
              (R + L) / (L - R), (T + B) / (B - T), 0.0, 1.0);
            Vertex v = pc.vb.vertices[gl_VertexIndex];
            out_color = unpackUnorm4x8(v.rgba);
            out_uv = vec2(v.u, v.v);
            gl_Position = proj * vec4(v.x, v.y, 0, 1);
        }
        """;

    const string codeFS = """
        layout(location = 0) in vec4 in_color;
        layout(location = 1) in vec2 in_uv;

        layout(location = 0) out vec4 out_color;

        layout(constant_id = 0) const bool kNonLinearColorSpace = false;

        layout(push_constant) uniform PushConstants
        {
          vec4 LRTB;
          vec2 vb;
          uint textureId;
          uint samplerId;
        } pc;

        void main()
        {
            vec4 c = in_color * texture(nonuniformEXT(sampler2D(kTextures2D[pc.textureId], kSamplers[pc.samplerId])), in_uv);
            // Render UI in linear color space to sRGB framebuffer.
            out_color = kNonLinearColorSpace ? vec4(pow(c.rgb, vec3(2.2)), c.a) : c;
        }
        """;

    static readonly ILogger logger = LogManager.Create<ImGuiRenderer>();

    struct Drawable()
    {
        public BufferResource VertexBuffer = BufferResource.Null;
        public BufferResource IndexBuffer = BufferResource.Null;
        public uint VertexCount = 0;
        public uint IndexCount = 0;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PushConstants
    {
        public Vector4 LRTB; // Left, Right, Top, Bottom
        public ulong vb; // Vertex buffer offset and index buffer offset
        public uint TextureId; // Texture ID
        public uint SamplerId; // Sampler ID
    }

    readonly IContext context = context ?? throw new ArgumentNullException(nameof(context));
    readonly ImGuiConfig config = config ?? throw new ArgumentNullException(nameof(config));
    ShaderModuleResource vertexShaderModule = context.CreateShaderModuleGlsl(codeVS, ShaderStage.Vertex, nameof(ImGuiRenderer));
    ShaderModuleResource fragmentShaderModule = context.CreateShaderModuleGlsl(codeFS, ShaderStage.Fragment, nameof(ImGuiRenderer));
    RenderPipelineResource pipeline = RenderPipelineResource.Null;
    SamplerResource sampler = context.CreateSampler(new SamplerStateDesc
    {
        WrapU = SamplerWrap.Clamp,
        WrapV = SamplerWrap.Clamp,
        WrapW = SamplerWrap.Clamp,
        DebugName = nameof(ImGuiRenderer)
    });

    readonly Drawable[] drawables = [new(), new(), new()];
    int frameCount = 0;
    nint imguiContext = 0;
    private bool disposedValue;

    public IContext Context => context;

    public float DisplayScale { set; get; } = 1.0f;

    public TextureResource FontTexture { get; private set; } = TextureResource.Null;

    public nint ImGuiContext => imguiContext;

    public bool Initialize()
    {
        imguiContext = Gui.CreateContext();
        var io = Gui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        SetFont(config.FontPath);
        return true;
    }

    private unsafe bool CreatePipeline(Framebuffer fb)
    {
        uint nonLinearColorSpace = context.GetSwapchainColorSpace() == ColorSpace.SRGB_NONLINEAR ? 1u : 0u;
        var desc = new RenderPipelineDesc();
        desc.VertexShader = vertexShaderModule;
        desc.FragementShader = fragmentShaderModule;
        desc.SpecInfo.Entries[0].ConstantId = 0;
        desc.SpecInfo.Entries[0].Size = sizeof(uint);
        desc.SpecInfo.Data = new byte[sizeof(uint)];
        using var pData = desc.SpecInfo.Data.Pin();
        NativeHelper.Write((nint)pData.Pointer, ref nonLinearColorSpace);
        desc.Colors[0].Format = context.GetFormat(fb.Colors[0].Texture);
        desc.Colors[0].BlendEnabled = true;
        desc.Colors[0].SrcRGBBlendFactor = BlendFactor.SrcAlpha;
        desc.Colors[0].DstRGBBlendFactor = BlendFactor.OneMinusSrcAlpha;
        desc.DepthFormat = fb.DepthStencil.Texture ? context.GetFormat(fb.DepthStencil.Texture) : Format.Invalid;
        desc.CullMode = CullMode.None;
        desc.DebugName = nameof(ImGuiRenderer);
        var ret = context.CreateRenderPipeline(desc, out pipeline);
        return ret == ResultCode.Ok;
    }

    public void SetFont(string fontPath)
    {
        var io = Gui.GetIO();
        io.Fonts.Clear();
        if (string.IsNullOrEmpty(fontPath))
        {
            io.Fonts.AddFontDefault();
        }
        else
        {
            if (!File.Exists(fontPath))
            {
                logger.LogError($"Font file not found: {fontPath}");
                return;
            }
            io.Fonts.AddFontFromFileTTF(fontPath, config.FontSizeInPixel);
        }
        if (!io.Fonts.Build())
        {
            logger.LogError("Failed to build ImGui fonts.");
        }
        else
        {
            unsafe
            {
                // Get the font texture and upload it to the GPU.
                io.Fonts.GetTexDataAsRGBA32(out nint data, out var width, out var height);
                var textureDesc = new TextureDesc
                {
                    Dimensions = new Dimensions((uint)width, (uint)height, 1),
                    Format = Format.RGBA_UN8,
                    Usage = TextureUsageBits.Sampled,
                    Data = data,
                    DataSize = Format.RGBA_UN8.GetBytesPerBlock() * (uint)(width * height),
                    Type = TextureType.Texture2D,
                };
                FontTexture = context.CreateTexture(textureDesc, "ImGui Font Texture");
                io.Fonts.SetTexID((nint)FontTexture.Index);
            }
        }
    }

    public bool BeginFrame(Framebuffer fb)
    {
        if (pipeline == RenderPipelineResource.Null)
        {
            if (!CreatePipeline(fb))
            {
                logger.LogError("Failed to create ImGui render pipeline.");
                return false;
            }
        }
        var dim = context.GetDimensions(fb.Colors[0].Texture);
        Gui.SetCurrentContext(imguiContext);
        var io = Gui.GetIO();
        io.DisplaySize = new Vector2(dim.Width / DisplayScale, dim.Height / DisplayScale);
        io.DisplayFramebufferScale = new Vector2(DisplayScale);
        Gui.NewFrame();
        return true;
    }

    public bool EndFrame(ICommandBuffer cmdBuf)
    {
        Gui.EndFrame();
        Gui.Render();
        var drawData = Gui.GetDrawData();
        var size = drawData.DisplaySize * drawData.FramebufferScale;
        if (size.X <= 0 || size.Y <= 0)
        {
            logger.LogWarning("ImGui draw data has zero size, skipping rendering.");
            return false;
        }
        ref var drawable = ref drawables[frameCount];
        frameCount = (frameCount + 1) % drawables.Length;
        unsafe
        {
            if (drawable.VertexBuffer == BufferResource.Null || drawable.VertexCount < drawData.TotalVtxCount)
            {
                if (drawable.VertexBuffer != BufferResource.Null)
                {
                    context.Destroy(drawable.VertexBuffer);
                }
                if (drawData.TotalVtxCount == 0)
                {
                    return false; // No vertex data to render.
                }
                drawable.VertexBuffer = context.CreateBuffer(new BufferDesc
                {
                    Usage = BufferUsageBits.Storage,
                    Storage = StorageType.HostVisible,
                    DataSize = (uint)(drawData.TotalVtxCount * sizeof(ImDrawVert)),
                    DebugName = "ImGui Vertex Buffer"
                });
                drawable.VertexCount = (uint)drawData.TotalVtxCount;
            }
            if (drawable.IndexBuffer == BufferResource.Null || drawable.IndexCount < drawData.TotalIdxCount)
            {
                if (drawable.IndexBuffer != BufferResource.Null)
                {
                    context.Destroy(drawable.IndexBuffer);
                }
                if (drawData.TotalIdxCount <= 0)
                {
                    return false; // No index data to render.
                }
                drawable.IndexBuffer = context.CreateBuffer(new BufferDesc
                {
                    Usage = BufferUsageBits.Index,
                    Storage = StorageType.HostVisible,
                    DataSize = (uint)(drawData.TotalIdxCount * sizeof(ushort)),
                    DebugName = "ImGui Index Buffer"
                });
                drawable.IndexCount = (uint)drawData.TotalIdxCount;
            }

            {
                var vertexPtr = context.GetMappedPtr(drawable.VertexBuffer);
                var indexPtr = context.GetMappedPtr(drawable.IndexBuffer);
                for (int i = 0; i < drawData.CmdListsCount; ++i)
                {
                    var drawList = drawData.CmdLists[i];
                    var vPtr = drawList.VtxBuffer.Data;
                    System.Buffer.MemoryCopy(vPtr.ToPointer(), vertexPtr.ToPointer(), drawable.VertexCount * sizeof(ImDrawVert), drawList.VtxBuffer.Size * sizeof(ImDrawVert));
                    vertexPtr += drawList.VtxBuffer.Size * sizeof(ImDrawVert);

                    var iPtr = drawList.IdxBuffer.Data;
                    System.Buffer.MemoryCopy(iPtr.ToPointer(), indexPtr.ToPointer(), drawable.IndexCount * sizeof(ushort), drawList.IdxBuffer.Size * sizeof(ushort));
                    indexPtr += drawList.IdxBuffer.Size * sizeof(ushort);
                }
                context.FlushMappedMemory(drawable.VertexBuffer, 0, (uint)(drawData.TotalVtxCount * sizeof(ImDrawVert)));
                context.FlushMappedMemory(drawable.IndexBuffer, 0, (uint)(drawData.TotalIdxCount * sizeof(ushort)));
            }
        }

        cmdBuf.PushDebugGroupLabel("ImGui", new Maths.Color4(1, 0, 0, 1));
        cmdBuf.BindDepthState(new());
        cmdBuf.BindViewport(new() { Width = (uint)size.X, Height = (uint)size.Y });
        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        var clipOff = drawData.DisplayPos;
        var clipScale = drawData.FramebufferScale;

        cmdBuf.BindRenderPipeline(pipeline);

        uint idxOffset = 0;
        uint vtxOffset = 0;

        cmdBuf.BindIndexBuffer(drawable.IndexBuffer, IndexFormat.UI16);

        for (int i = 0; i < drawData.CmdListsCount; ++i)
        {
            var cmdList = drawData.CmdLists[i];
            for (int j = 0; j < cmdList.CmdBuffer.Size; ++j)
            {
                var cmd = cmdList.CmdBuffer[j];
                Vector2 clipMin = new((cmd.ClipRect.X - clipOff.X) * clipScale.X, (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y);
                Vector2 clipMax = new((cmd.ClipRect.Z - clipOff.X) * clipScale.X, (cmd.ClipRect.W - clipOff.Y) * clipScale.Y);
                // clang-format off
                if (clipMin.X < 0.0f) clipMin.X = 0.0f;
                if (clipMin.Y < 0.0f) clipMin.Y = 0.0f;
                if (clipMax.X > size.X) clipMax.X = size.X;
                if (clipMax.Y > size.Y) clipMax.Y = size.Y;
                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                    continue;
                var pushConstants = new PushConstants
                {
                    LRTB = new Vector4(L, R, T, B),
                    vb = context.GpuAddress(drawable.VertexBuffer),
                    TextureId = (uint)cmd.GetTexID(),
                    SamplerId = sampler.Index
                };
                cmdBuf.PushConstants(pushConstants);
                cmdBuf.BindScissorRect(new((uint)clipMin.X, (uint)clipMin.Y, (uint)(clipMax.X - clipMin.X), (uint)(clipMax.Y - clipMin.Y)));
                cmdBuf.DrawIndexed(cmd.ElemCount, 1, idxOffset + cmd.IdxOffset, (int)(vtxOffset + cmd.VtxOffset), 0);
            }
            idxOffset += (uint)cmdList.IdxBuffer.Size;
            vtxOffset += (uint)cmdList.VtxBuffer.Size;
        }

        cmdBuf.PopDebugGroupLabel();
        return true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                sampler.Dispose();
                vertexShaderModule.Dispose();
                fragmentShaderModule.Dispose();
                pipeline.Dispose();
                FontTexture.Dispose();
                foreach (var drawable in drawables)
                {
                    drawable.VertexBuffer.Dispose();
                    drawable.IndexBuffer.Dispose();
                }
                if (imguiContext != 0)
                {
                    Gui.DestroyContext(imguiContext);
                    imguiContext = 0;
                }
            }
            disposedValue = true;
        }
    }


    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
