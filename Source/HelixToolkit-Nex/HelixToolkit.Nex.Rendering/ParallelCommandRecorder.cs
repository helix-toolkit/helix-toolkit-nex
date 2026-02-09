using System.Collections.Concurrent;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Provides utilities for recording commands in parallel using secondary command buffers.
/// </summary>
/// <remarks>
/// This class enables multi-threaded command recording by creating secondary command buffers
/// that can be recorded in parallel and then executed by a primary command buffer.
/// </remarks>
public class ParallelCommandRecorder : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<ParallelCommandRecorder>();
    private readonly IContext _context;
    private readonly ConcurrentBag<ICommandBuffer> _secondaryBuffers = new();
    private bool _disposedValue;

    public ParallelCommandRecorder(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Records commands in parallel across multiple threads using secondary command buffers.
    /// </summary>
    /// <param name="renderPass">The render pass information for compatibility.</param>
    /// <param name="workItems">The collection of work items to execute in parallel.</param>
    /// <param name="recordAction">The action to execute for each work item, receiving the secondary command buffer.</param>
    /// <typeparam name="T">The type of work items.</typeparam>
    /// <returns>An array of recorded secondary command buffers ready for execution.</returns>
    public ICommandBuffer[] RecordParallel<T>(
        in RenderPass renderPass,
        IEnumerable<T> workItems,
        Action<ICommandBuffer, T> recordAction)
    {
        var items = workItems.ToArray();
        var secondaryBuffers = new ICommandBuffer[items.Length];
        var exceptions = new ConcurrentBag<Exception>();

        // Copy to local variable to avoid capture of 'in' parameter
        var localRenderPass = renderPass;

        // Record commands in parallel
        Parallel.For(0, items.Length, i =>
        {
            try
            {
                var secondaryBuffer = _context.CreateSecondaryCommandBuffer(localRenderPass);
                _secondaryBuffers.Add(secondaryBuffer);

                // Record commands for this work item
                recordAction(secondaryBuffer, items[i]);

                secondaryBuffers[i] = secondaryBuffer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording secondary command buffer for work item {INDEX}", i);
                exceptions.Add(ex);
            }
        });

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException("Errors occurred during parallel command recording", exceptions);
        }

        return secondaryBuffers;
    }

    /// <summary>
    /// Records commands in parallel with a specified degree of parallelism.
    /// </summary>
    /// <param name="renderPass">The render pass information for compatibility.</param>
    /// <param name="workItems">The collection of work items to execute in parallel.</param>
    /// <param name="recordAction">The action to execute for each work item.</param>
    /// <param name="maxDegreeOfParallelism">Maximum number of concurrent tasks.</param>
    /// <typeparam name="T">The type of work items.</typeparam>
    /// <returns>An array of recorded secondary command buffers ready for execution.</returns>
    public ICommandBuffer[] RecordParallel<T>(
        in RenderPass renderPass,
        IEnumerable<T> workItems,
        Action<ICommandBuffer, T> recordAction,
        int maxDegreeOfParallelism)
    {
        var items = workItems.ToArray();
        var secondaryBuffers = new ICommandBuffer[items.Length];
        var exceptions = new ConcurrentBag<Exception>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

        // Copy to local variable to avoid capture of 'in' parameter
        var localRenderPass = renderPass;

        Parallel.For(0, items.Length, options, i =>
        {
            try
            {
                var secondaryBuffer = _context.CreateSecondaryCommandBuffer(localRenderPass);
                _secondaryBuffers.Add(secondaryBuffer);

                recordAction(secondaryBuffer, items[i]);
                secondaryBuffers[i] = secondaryBuffer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording secondary command buffer for work item {INDEX}", i);
                exceptions.Add(ex);
            }
        });

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException("Errors occurred during parallel command recording", exceptions);
        }

        return secondaryBuffers;
    }

    /// <summary>
    /// Records commands for batches of work items in parallel.
    /// </summary>
    /// <param name="renderPass">The render pass information for compatibility.</param>
    /// <param name="workItems">The collection of work items to batch.</param>
    /// <param name="batchSize">Number of items per batch.</param>
    /// <param name="recordBatchAction">Action to execute for each batch.</param>
    /// <typeparam name="T">The type of work items.</typeparam>
    /// <returns>An array of recorded secondary command buffers ready for execution.</returns>
    public ICommandBuffer[] RecordBatched<T>(
        in RenderPass renderPass,
        IEnumerable<T> workItems,
        int batchSize,
        Action<ICommandBuffer, IEnumerable<T>> recordBatchAction)
    {
        var items = workItems.ToArray();
        var batchCount = (items.Length + batchSize - 1) / batchSize;
        var secondaryBuffers = new ICommandBuffer[batchCount];
        var exceptions = new ConcurrentBag<Exception>();

        // Copy to local variable to avoid capture of 'in' parameter
        var localRenderPass = renderPass;

        Parallel.For(0, batchCount, i =>
        {
            try
            {
                var start = i * batchSize;
                var count = Math.Min(batchSize, items.Length - start);
                var batch = items.Skip(start).Take(count);

                var secondaryBuffer = _context.CreateSecondaryCommandBuffer(localRenderPass);
                _secondaryBuffers.Add(secondaryBuffer);

                recordBatchAction(secondaryBuffer, batch);
                secondaryBuffers[i] = secondaryBuffer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording secondary command buffer for batch {INDEX}", i);
                exceptions.Add(ex);
            }
        });

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException("Errors occurred during batched command recording", exceptions);
        }

        return secondaryBuffers;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Dispose all secondary buffers
                foreach (var buffer in _secondaryBuffers)
                {
                    if (buffer is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _secondaryBuffers.Clear();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
