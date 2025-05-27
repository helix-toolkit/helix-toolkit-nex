namespace HelixToolkit.Nex.Graphics;

public struct ShaderModule { }
public struct Texture { }
public struct Buffer { }
public struct ComputePipeline { }
public struct RenderPipeline { }
public struct Sampler { }
public struct QueryPool { }

public sealed class ShaderModuleHolder : Holder<ShaderModule>
{
    public ShaderModuleHolder() { }

    public ShaderModuleHolder(IContext context, in ShaderModuleHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }
    public static readonly ShaderModuleHolder Null = new(); // A null shader module holder for convenience   
}

public sealed class TextureHolder : Holder<Texture>
{
    public TextureHolder() { }

    public TextureHolder(IContext context, in TextureHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly TextureHolder Null = new (); // A null texture holder for convenience   
}

public sealed class BufferHolder : Holder<Buffer>
{
    public BufferHolder() { }

    public BufferHolder(IContext context, in BufferHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly BufferHolder Null = new(); // A null buffer holder for convenience   
}

public sealed class ComputePipelineHolder : Holder<ComputePipeline>
{
    public ComputePipelineHolder() { }

    public ComputePipelineHolder(IContext context, in ComputePipelineHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly ComputePipelineHolder Null = new(); // A null compute pipeline holder for convenience   
}

public sealed class RenderPipelineHolder : Holder<RenderPipeline>
{
    public RenderPipelineHolder() { }

    public RenderPipelineHolder(IContext context, in RenderPipelineHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly RenderPipelineHolder Null = new(); // A null render pipeline holder for convenience   
}

public sealed class SamplerHolder : Holder<Sampler>
{
    public SamplerHolder() { }

    public SamplerHolder(IContext context, in SamplerHandle handle) : base(context, handle)
    {
    }

    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly SamplerHolder Null = new(); // A null sampler holder for convenience   
}

public sealed class QueryPoolHolder : Holder<QueryPool>
{
    public QueryPoolHolder() { }

    public QueryPoolHolder(IContext context, in QueryPoolHandle handle) : base(context, handle)
    {
    }

    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle_);
    }

    public static readonly QueryPoolHolder Null = new(); // A null query pool holder for convenience   
}