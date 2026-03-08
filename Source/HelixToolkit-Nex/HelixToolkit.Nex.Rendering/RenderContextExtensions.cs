using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Extension methods for <see cref="RenderContext"/> to support secondary command buffers.
/// </summary>
public static class RenderContextExtensions
{
    private static readonly ILogger _logger = LogManager.Create("RenderContextExtensions");

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
        in RenderPass renderPass
    )
    {
        if (context.Context == null)
        {
            throw new InvalidOperationException(
                "RenderContext does not have an active command buffer or context"
            );
        }

        return context.Context.CreateSecondaryCommandBuffer(renderPass);
    }

    /// <summary>
    /// Executes secondary command buffers on the primary command buffer.
    /// </summary>
    /// <param name="cmdBuf">The command buffer.</param>
    /// <param name="secondaryBuffers">The secondary buffers to execute.</param>
    public static void ExecuteSecondaryBuffers(
        this ICommandBuffer cmdBuf,
        params ICommandBuffer[] secondaryBuffers
    )
    {
        cmdBuf.ExecuteCommands(secondaryBuffers);
    }

    /// <summary>
    /// Records a batch of work items using secondary command buffers and executes them.
    /// </summary>
    /// <typeparam name="T">The type of work items.</typeparam>
    /// <param name="cmdBuf">The main command buffer.</param>
    /// <param name="renderPass">The render pass for secondary buffer compatibility.</param>
    /// <param name="workItems">The items to process in parallel.</param>
    /// <param name="recordAction">The action to record for each work item.</param>
    public static void RecordAndExecuteParallel<T>(
        this ICommandBuffer cmdBuf,
        in RenderPass renderPass,
        IEnumerable<T> workItems,
        Action<ICommandBuffer, T> recordAction
    )
    {
        var items = workItems.ToArray();
        if (items.Length == 0)
            return;

        var secondaryBuffers = new ICommandBuffer[items.Length];

        // Copy to local variable to avoid capture of 'in' parameter
        var localRenderPass = renderPass;

        // Record in parallel
        Parallel.For(
            0,
            items.Length,
            i =>
            {
                try
                { // Create a secondary command buffer for this item}
                    var secondaryBuffer = cmdBuf.Context.CreateSecondaryCommandBuffer(localRenderPass);
                    recordAction(secondaryBuffer, items[i]);
                    secondaryBuffers[i] = secondaryBuffer;
                }
                catch (Exception ex)
                {
                    // Handle exceptions from recording
                    _logger.LogError($"Error recording command buffer for item {i}: {ex}");
                }
            }
        );

        // Execute all at once
        cmdBuf.ExecuteSecondaryBuffers(secondaryBuffers);
    }
}
