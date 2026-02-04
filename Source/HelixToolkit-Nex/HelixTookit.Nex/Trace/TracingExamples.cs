using HelixToolkit.Nex.Trace;

namespace HelixToolkit.Nex.Examples;

/// <summary>
/// Example usage of the high-performance tracing library.
/// </summary>
public static class TracingExamples
{
    /// <summary>
    /// Basic usage example showing how to use tracing.
    /// </summary>
    public static void BasicUsageExample()
    {
        // Enable tracing globally
        TracerFactory.Enable();

        // Get a tracer instance
        var tracer = TracerFactory.GetTracer("MyComponent");

        // Use a scope - automatically measures duration
        using (tracer.BeginScope("MyOperation"))
        {
            // Your code here
            System.Threading.Thread.Sleep(10);
        }

        // Record an event with a value
        tracer.TraceEvent("ItemsProcessed", 150);

        // Record a marker
        tracer.TraceMarker("CheckpointReached");

        // Get and display results
        var entries = tracer.GetTraceEntries();
        foreach (var entry in entries)
        {
            Console.WriteLine(entry);
        }
    }

    /// <summary>
    /// Example showing zero-allocation when disabled.
    /// </summary>
    public static void ZeroAllocationExample()
    {
        // Disable tracing - all operations become no-ops with zero allocation
        TracerFactory.Disable();

        var tracer = TracerFactory.GetTracer("Performance");

        // This creates no allocations when tracing is disabled
        using (tracer.BeginScope("HotPath"))
        {
            // Critical performance code
        }

        // This also has zero overhead
        tracer.TraceEvent("Counter", 42);
    }

    /// <summary>
    /// Example showing categorized tracing.
    /// </summary>
    public static void CategorizedTracingExample()
    {
        TracerFactory.Enable();
        var tracer = TracerFactory.GetTracer("Rendering");

        using (tracer.BeginScope("RenderFrame", "Graphics"))
        {
            using (tracer.BeginScope("UpdateGeometry", "Graphics"))
            {
                System.Threading.Thread.Sleep(5);
            }

            using (tracer.BeginScope("DrawCalls", "Graphics"))
            {
                System.Threading.Thread.Sleep(3);
            }
        }

        tracer.TraceEvent("TrianglesRendered", "Graphics", 15000);
    }

    /// <summary>
    /// Example showing statistics and reporting.
    /// </summary>
    public static void StatisticsExample()
    {
        TracerFactory.Enable();
        var tracer = TracerFactory.GetTracer("Performance");

        // Simulate multiple operations
        for (int i = 0; i < 100; i++)
        {
            using (tracer.BeginScope("ProcessItem"))
            {
                System.Threading.Thread.Sleep(new Random().Next(1, 10));
            }
        }

        // Get statistics for a specific operation
        if (tracer is PerformanceTracer perfTracer)
        {
            var stats = perfTracer.GetStatistics("ProcessItem");
            Console.WriteLine(stats);
            // Output: ProcessItem: Count=100, Avg=X.XXXms, Min=X.XXXms, Max=X.XXXms, ...
        }

        // Aggregate all trace data
        var entries = tracer.GetTraceEntries();
        var aggregated = TraceAggregator.AggregateByName(entries);
        foreach (var stat in aggregated.Values)
        {
            Console.WriteLine(stat);
        }

        // Generate a full report
        var report = TraceAggregator.GenerateReport(entries);
        Console.WriteLine(report);
    }

    /// <summary>
    /// Example showing thread-safe tracing from multiple threads.
    /// </summary>
    public static void MultithreadedExample()
    {
        TracerFactory.Enable();
        var tracer = TracerFactory.GetTracer("Multithreaded");

        // Spawn multiple threads
        var tasks = new System.Threading.Tasks.Task[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            int threadIndex = i;
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                using (tracer.BeginScope($"Worker{threadIndex}"))
                {
                    System.Threading.Thread.Sleep(10);
                }
            });
        }

        System.Threading.Tasks.Task.WaitAll(tasks);

        // Group results by thread
        var entries = tracer.GetTraceEntries();
        var byThread = TraceAggregator.GroupByThread(entries);
        Console.WriteLine($"Traces from {byThread.Count} different threads");
    }

    /// <summary>
    /// Example showing event subscriptions.
    /// </summary>
    public static void EventSubscriptionExample()
    {
        TracerFactory.Enable();
        var tracer = TracerFactory.CreateTracer() as PerformanceTracer;

        if (tracer != null)
        {
            // Subscribe to trace events in real-time
            tracer.TraceRecorded += (sender, args) =>
            {
                Console.WriteLine($"Trace recorded: {args.Entry}");
            };

            using (tracer.BeginScope("MonitoredOperation"))
            {
                System.Threading.Thread.Sleep(5);
            }
        }
    }

    /// <summary>
    /// Example showing custom configuration.
    /// </summary>
    public static void CustomConfigurationExample()
    {
        // Create custom configuration
        var config = new TracerConfiguration
        {
            Enabled = true,
            InitialCapacity = 2048,
            MaxEntries = 10000, // Keep only last 10,000 entries
            IncludeThreadId = true,
            UseHighResolutionTimestamps = true,
        };

        TracerFactory.Configuration = config;
        TracerFactory.Enable();

        var tracer = TracerFactory.GetTracer("CustomConfig");

        // Use the tracer with custom configuration
        using (tracer.BeginScope("Operation"))
        {
            // Your code
        }
    }

    /// <summary>
    /// Example showing performance analysis workflow.
    /// </summary>
    public static void PerformanceAnalysisWorkflow()
    {
        TracerFactory.Enable();
        var tracer = TracerFactory.GetTracer("Analysis");

        // Simulate a complex workflow
        using (tracer.BeginScope("TotalOperation", "Workflow"))
        {
            tracer.TraceMarker("StartProcessing", "Workflow");

            using (tracer.BeginScope("LoadData", "IO"))
            {
                System.Threading.Thread.Sleep(15);
            }

            tracer.TraceEvent("RecordsLoaded", "IO", 1000);

            using (tracer.BeginScope("ProcessData", "Compute"))
            {
                System.Threading.Thread.Sleep(25);
            }

            using (tracer.BeginScope("SaveResults", "IO"))
            {
                System.Threading.Thread.Sleep(10);
            }

            tracer.TraceMarker("CompletedProcessing", "Workflow");
        }

        // Analyze results
        var entries = tracer.GetTraceEntries();

        // Find bottlenecks
        var slowest = TraceAggregator.GetSlowestEntries(entries, 3);
        Console.WriteLine("=== Bottlenecks ===");
        foreach (var entry in slowest)
        {
            Console.WriteLine($"{entry.Name}: {entry.DurationMs:F2}ms");
        }

        // Analyze by category
        var byCategory = TraceAggregator.AggregateByCategory(entries);
        Console.WriteLine("\n=== Time by Category ===");
        foreach (var stat in byCategory.Values)
        {
            Console.WriteLine($"{stat.Name}: {stat.Total:F2}ms total");
        }
    }

    /// <summary>
    /// Example showing best practices for production use.
    /// </summary>
    public static void ProductionBestPractices()
    {
        // 1. Use compile-time flags for development-only tracing
#if DEBUG
        TracerFactory.Enable();
#else
        TracerFactory.Disable();
#endif

        // 2. Use named tracers for different components
        var renderTracer = TracerFactory.GetTracer("Rendering");
        var physicsTracer = TracerFactory.GetTracer("Physics");

        // 3. Use categories to group related operations
        using (renderTracer.BeginScope("UpdateScene", "SceneManagement"))
        {
            // Scene update code
        }

        // 4. Clear old data periodically to prevent memory buildup
        if (renderTracer is PerformanceTracer perfTracer && perfTracer.Count > 100000)
        {
            perfTracer.Clear();
        }

        // 5. Generate reports periodically for monitoring
        var entries = renderTracer.GetTraceEntries();
        if (entries.Count > 0)
        {
            var report = TraceAggregator.GenerateReport(entries);
            // Log or save the report
        }
    }
}
