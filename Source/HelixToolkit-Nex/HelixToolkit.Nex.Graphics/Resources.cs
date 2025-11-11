namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Marker struct representing a shader module resource type.
/// </summary>
public struct ShaderModule { }

/// <summary>
/// Marker struct representing a texture resource type.
/// </summary>
public struct Texture { }

/// <summary>
/// Marker struct representing a buffer resource type.
/// </summary>
public struct Buffer { }

/// <summary>
/// Marker struct representing a compute pipeline resource type.
/// </summary>
public struct ComputePipeline { }

/// <summary>
/// Marker struct representing a render pipeline resource type.
/// </summary>
public struct RenderPipeline { }

/// <summary>
/// Marker struct representing a sampler resource type.
/// </summary>
public struct Sampler { }

/// <summary>
/// Marker struct representing a query pool resource type.
/// </summary>
public struct QueryPool { }

/// <summary>
/// Reference-counted resource wrapper for shader module handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a shader module resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class ShaderModuleResource : Resource<ShaderModule>
{
    /// <summary>
    /// Initializes a new null shader module resource.
    /// </summary>
    public ShaderModuleResource() { }

    /// <summary>
    /// Initializes a new shader module resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
    /// <param name="handle">The shader module handle.</param>
    public ShaderModuleResource(IContext context, in ShaderModuleHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the shader module handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null shader module resource for convenience.
    /// </summary>
    public static readonly ShaderModuleResource Null = new(); // A null shader module holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for texture handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a texture resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class TextureResource : Resource<Texture>
{
    /// <summary>
    /// Initializes a new null texture resource.
    /// </summary>
    public TextureResource() { }

    /// <summary>
    /// Initializes a new texture resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
    /// <param name="handle">The texture handle.</param>
    public TextureResource(IContext context, in TextureHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the texture handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null texture resource for convenience.
    /// </summary>
    public static readonly TextureResource Null = new(); // A null texture holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for buffer handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a buffer resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class BufferResource : Resource<Buffer>
{
    /// <summary>
    /// Initializes a new null buffer resource.
    /// </summary>
    public BufferResource() { }

    /// <summary>
    /// Initializes a new buffer resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
  /// <param name="handle">The buffer handle.</param>
    public BufferResource(IContext context, in BufferHandle handle) : base(context, handle)
    {
}

    /// <summary>
    /// Destroys the buffer handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null buffer resource for convenience.
    /// </summary>
    public static readonly BufferResource Null = new(); // A null buffer holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for compute pipeline handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a compute pipeline resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class ComputePipelineResource : Resource<ComputePipeline>
{
    /// <summary>
    /// Initializes a new null compute pipeline resource.
    /// </summary>
    public ComputePipelineResource() { }

    /// <summary>
    /// Initializes a new compute pipeline resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
    /// <param name="handle">The compute pipeline handle.</param>
    public ComputePipelineResource(IContext context, in ComputePipelineHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the compute pipeline handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
    {
 ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null compute pipeline resource for convenience.
    /// </summary>
    public static readonly ComputePipelineResource Null = new(); // A null compute pipeline holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for render pipeline handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a render pipeline resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class RenderPipelineResource : Resource<RenderPipeline>
{
    /// <summary>
    /// Initializes a new null render pipeline resource.
    /// </summary>
    public RenderPipelineResource() { }

  /// <summary>
    /// Initializes a new render pipeline resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
 /// <param name="handle">The render pipeline handle.</param>
    public RenderPipelineResource(IContext context, in RenderPipelineHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the render pipeline handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null render pipeline resource for convenience.
    /// </summary>
    public static readonly RenderPipelineResource Null = new(); // A null render pipeline holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for sampler handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a sampler resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class SamplerResource : Resource<Sampler>
{
    /// <summary>
    /// Initializes a new null sampler resource.
    /// </summary>
    public SamplerResource() { }

    /// <summary>
 /// Initializes a new sampler resource with the specified context and handle.
    /// </summary>
    /// <param name="context">The graphics context that owns this resource.</param>
    /// <param name="handle">The sampler handle.</param>
    public SamplerResource(IContext context, in SamplerHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the sampler handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
    {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null sampler resource for convenience.
    /// </summary>
    public static readonly SamplerResource Null = new(); // A null sampler holder for convenience   
}

/// <summary>
/// Reference-counted resource wrapper for query pool handles.
/// </summary>
/// <remarks>
/// This class manages the lifetime of a query pool resource and automatically
/// destroys it when no longer referenced.
/// </remarks>
public sealed class QueryPoolResource : Resource<QueryPool>
{
    /// <summary>
    /// Initializes a new null query pool resource.
    /// </summary>
    public QueryPoolResource() { }

    /// <summary>
    /// Initializes a new query pool resource with the specified context and handle.
    /// </summary>
  /// <param name="context">The graphics context that owns this resource.</param>
    /// <param name="handle">The query pool handle.</param>
    public QueryPoolResource(IContext context, in QueryPoolHandle handle) : base(context, handle)
    {
    }

    /// <summary>
    /// Destroys the query pool handle using the graphics context.
    /// </summary>
    /// <param name="ctx">The graphics context.</param>
    protected override void OnDestroyHandle(IContext ctx)
  {
        ctx.Destroy(handle);
    }

    /// <summary>
    /// A predefined null query pool resource for convenience.
    /// </summary>
    public static readonly QueryPoolResource Null = new(); // A null query pool holder for convenience   
}