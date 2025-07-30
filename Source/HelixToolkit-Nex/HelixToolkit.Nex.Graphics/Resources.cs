namespace HelixToolkit.Nex.Graphics;

public struct ShaderModule { }
public struct Texture { }
public struct Buffer { }
public struct ComputePipeline { }
public struct RenderPipeline { }
public struct Sampler { }
public struct QueryPool { }

public sealed class ShaderModuleResource : Resource<ShaderModule>
{
    public ShaderModuleResource() { }

    public ShaderModuleResource(IContext context, in ShaderModuleHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }
    public static readonly ShaderModuleResource Null = new(); // A null shader module holder for convenience   
}

public sealed class TextureResource : Resource<Texture>
{
    public TextureResource() { }

    public TextureResource(IContext context, in TextureHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly TextureResource Null = new(); // A null texture holder for convenience   
}

public sealed class BufferResource : Resource<Buffer>
{
    public BufferResource() { }

    public BufferResource(IContext context, in BufferHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly BufferResource Null = new(); // A null buffer holder for convenience   
}

public sealed class ComputePipelineResource : Resource<ComputePipeline>
{
    public ComputePipelineResource() { }

    public ComputePipelineResource(IContext context, in ComputePipelineHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly ComputePipelineResource Null = new(); // A null compute pipeline holder for convenience   
}

public sealed class RenderPipelineResource : Resource<RenderPipeline>
{
    public RenderPipelineResource() { }

    public RenderPipelineResource(IContext context, in RenderPipelineHandle handle) : base(context, handle)
    {
    }
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly RenderPipelineResource Null = new(); // A null render pipeline holder for convenience   
}

public sealed class SamplerResource : Resource<Sampler>
{
    public SamplerResource() { }

    public SamplerResource(IContext context, in SamplerHandle handle) : base(context, handle)
    {
    }

    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly SamplerResource Null = new(); // A null sampler holder for convenience   
}

public sealed class QueryPoolResource : Resource<QueryPool>
{
    public QueryPoolResource() { }

    public QueryPoolResource(IContext context, in QueryPoolHandle handle) : base(context, handle)
    {
    }

    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    public static readonly QueryPoolResource Null = new(); // A null query pool holder for convenience   
}