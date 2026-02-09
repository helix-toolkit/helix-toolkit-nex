using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Example renderer demonstrating parallel command recording using secondary command buffers.
/// This can significantly improve performance when rendering large numbers of objects.
/// </summary>
internal class ParallelOpaqueRenderer : Renderer
{
    private static readonly ILogger _logger = LogManager.Create<ParallelOpaqueRenderer>();
    private ParallelCommandRecorder? _parallelRecorder;
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public override RenderStages Stage => RenderStages.Opaque;
    public override string Name => nameof(ParallelOpaqueRenderer);

    /// <summary>
    /// Gets or sets the minimum number of objects required to use parallel recording.
    /// Below this threshold, sequential recording is used.
    /// </summary>
    public int ParallelThreshold { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the batch size for parallel recording.
    /// Objects are grouped into batches, and each batch is recorded on a separate thread.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    protected override bool OnSetup()
    {
        if (Context == null || RenderManager == null)
            return false;

        _parallelRecorder = new ParallelCommandRecorder(Context);

        // Create pipeline (simplified example)
        // In real implementation, you'd use proper shaders
        return true;
    }

    protected override void OnTearDown()
    {
        _parallelRecorder?.Dispose();
        _parallelRecorder = null;
        _pipeline.Dispose();
        base.OnTearDown();
    }

    protected override void OnRender(RenderContext context)
    {
        if (!Enabled || context.CommandBuffer == null)
            return;

        var cmd = context.CommandBuffer;

        // Get opaque objects to render (example query)
        var opaqueObjects = GetOpaqueRenderables(context);
        int objectCount = opaqueObjects.Count();

        // Use parallel recording for large object counts
        if (objectCount >= ParallelThreshold)
        {
            RenderParallel(cmd, context, opaqueObjects);
        }
        else
        {
            RenderSequential(cmd, context, opaqueObjects);
        }
    }

    private void RenderParallel(ICommandBuffer primaryCmd, RenderContext context, IEnumerable<object> objects)
    {
        _logger.LogDebug("Using parallel command recording for {COUNT} objects", objects.Count());

        // Create render pass info for secondary buffers
        var renderPass = new RenderPass(
            new RenderPass.AttachmentDesc
            {
                LoadOp = LoadOp.Load,  // Don't clear - we're continuing rendering
                StoreOp = StoreOp.Store
            }
        );

        try
        {
            // Record commands in parallel batches
            var secondaryBuffers = _parallelRecorder!.RecordBatched(
                renderPass,
                objects,
                BatchSize,
                (secondaryCmd, batch) =>
                {
                    // Bind pipeline (each secondary buffer needs to bind state)
                    if (_pipeline.Valid)
                    {
                        secondaryCmd.BindRenderPipeline(_pipeline.Handle);
                    }

                    // Record draw calls for this batch
                    foreach (var obj in batch)
                    {
                        RecordDrawCall(secondaryCmd, obj);
                    }
                }
            );

            // Execute all secondary buffers on the primary command buffer
            primaryCmd.ExecuteCommands(secondaryBuffers);

            _logger.LogDebug("Executed {COUNT} secondary command buffers", secondaryBuffers.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during parallel command recording");
            // Fallback to sequential rendering
            RenderSequential(primaryCmd, context, objects);
        }
    }

    private void RenderSequential(ICommandBuffer cmd, RenderContext context, IEnumerable<object> objects)
    {
        if (_pipeline.Valid)
        {
            cmd.BindRenderPipeline(_pipeline.Handle);
        }

        foreach (var obj in objects)
        {
            RecordDrawCall(cmd, obj);
        }
    }

    private void RecordDrawCall(ICommandBuffer cmd, object obj)
    {
        // Example draw call recording
        // In real implementation, you'd bind vertex/index buffers and issue draw calls
        // cmd.BindVertexBuffer(0, vertexBuffer);
        // cmd.BindIndexBuffer(indexBuffer, IndexFormat.UI32);
        // cmd.DrawIndexed(indexCount, 1, 0, 0, 0);
    }

    private IEnumerable<object> GetOpaqueRenderables(RenderContext context)
    {
        // Example: Get renderables from ECS world
        // In real implementation, you'd query the ECS for opaque objects
        return Enumerable.Empty<object>();
    }
}
