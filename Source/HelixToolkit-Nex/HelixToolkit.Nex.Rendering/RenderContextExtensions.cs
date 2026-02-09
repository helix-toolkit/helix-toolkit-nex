using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Extension methods for <see cref="RenderContext"/> to support secondary command buffers.
/// </summary>
public static class RenderContextExtensions
{
    /// <summary>
    /// Creates a secondary command buffer compatible with the specified render pass.
    /// </summary>
    /// <param name="context">The render context.</param>
    /// <param name="renderPass">The render pass this secondary buffer will be used with.</param>
    /// <returns>A new secondary command buffer.</returns>
    /// <remarks>
    /// The caller is responsible for finalizing the secondary buffer before execution.
    /// </remarks>
    public static ICommandBuffer CreateSecondaryCommandBuffer(
        this RenderContext context,
        in RenderPass renderPass)
    {
        if (context.CommandBuffer?.Context == null)
        {
            throw new InvalidOperationException("RenderContext does not have an active command buffer or context");
        }

        return context.CommandBuffer.Context.CreateSecondaryCommandBuffer(renderPass);
    }

    /// <summary>
    /// Executes secondary command buffers on the primary command buffer in the render context.
    /// </summary>
    /// <param name="context">The render context.</param>
    /// <param name="secondaryBuffers">The secondary buffers to execute.</param>
    public static void ExecuteSecondaryBuffers(
        this RenderContext context,
        params ICommandBuffer[] secondaryBuffers)
    {
        if (context.CommandBuffer == null)
        {
            throw new InvalidOperationException("RenderContext does not have an active command buffer");
        }

        context.CommandBuffer.ExecuteCommands(secondaryBuffers);
    }

    /// <summary>
    /// Records a batch of work items using secondary command buffers and executes them.
    /// </summary>
    /// <typeparam name="T">The type of work items.</typeparam>
    /// <param name="context">The render context.</param>
    /// <param name="renderPass">The render pass for secondary buffer compatibility.</param>
    /// <param name="workItems">The items to process in parallel.</param>
    /// <param name="recordAction">The action to record for each work item.</param>
    public static void RecordAndExecuteParallel<T>(
        this RenderContext context,
        in RenderPass renderPass,
        IEnumerable<T> workItems,
        Action<ICommandBuffer, T> recordAction)
    {
        if (context.CommandBuffer == null)
        {
            throw new InvalidOperationException("RenderContext does not have an active command buffer");
        }

        var items = workItems.ToArray();
        if (items.Length == 0)
            return;

        var secondaryBuffers = new ICommandBuffer[items.Length];

        // Copy to local variable to avoid capture of 'in' parameter
        var localRenderPass = renderPass;

        // Record in parallel
        Parallel.For(0, items.Length, i =>
        {
            var secondaryBuffer = context.CreateSecondaryCommandBuffer(localRenderPass);
            recordAction(secondaryBuffer, items[i]);
            secondaryBuffers[i] = secondaryBuffer;
        });

        // Execute all at once
        context.ExecuteSecondaryBuffers(secondaryBuffers);
    }
}
