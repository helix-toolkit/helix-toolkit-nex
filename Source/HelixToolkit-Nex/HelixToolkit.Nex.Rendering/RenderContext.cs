namespace HelixToolkit.Nex.Rendering;

public sealed class RenderContext(World world)
{
    private static readonly ILogger _logger = LogManager.Create<RenderContext>();

    private readonly Dictionary<uint, System.Range> _materialIdToRange = new();

    public World World { get; } = world;

    /// <summary>
    /// Gets or sets the command buffer for the current frame. 
    /// Set by the renderer manager at the beginning of each frame.
    /// </summary>
    public ICommandBuffer? CommandBuffer { get; internal set; }

    public System.Range OpaqueObjects { get; private set; }

    public System.Range TransparentObjects { get; private set; }

    public System.Range Points { get; private set; }

    public System.Range Lines { get; private set; }

    public System.Range Billboard { get; private set; }

    public System.Range GetRangeByMaterialId(uint materialId)
    {
        return _materialIdToRange.TryGetValue(materialId, out var range) ? range : default;
    }

    public void SetOpaqueObjectsRange(ref System.Range range)
    {
        OpaqueObjects = range;
    }

    public void SetTransparentObjectsRange(ref System.Range range)
    {
        TransparentObjects = range;
    }
}
