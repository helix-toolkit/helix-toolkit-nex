# Quick Start Guide: Secondary Command Buffers

## What are Secondary Command Buffers?

Secondary command buffers allow you to record GPU commands in parallel across multiple CPU cores, significantly improving performance when rendering large numbers of objects (1000+).

## When to Use

✅ **Use secondary buffers when:**
- Rendering 1000+ objects per frame
- CPU command recording is a bottleneck
- Objects can be batched by material/pipeline
- You have multi-core CPU available

❌ **Don't use when:**
- Rendering < 500 objects
- CPU is not the bottleneck
- Frequent pipeline switches required

## Quick Start

### 1. Basic Usage in a Renderer

```csharp
using HelixToolkit.Nex.Rendering;

public class MyRenderer : Renderer
{
    protected override void OnRender(RenderContext context)
    {
        // Get the primary command buffer
        var cmd = context.CommandBuffer;
        if (cmd == null) return;

        var renderPass = new RenderPass(
            new RenderPass.AttachmentDesc 
            { 
                LoadOp = LoadOp.Load 
            }
        );

        // One-liner: record in parallel and execute
        context.RecordAndExecuteParallel(
            renderPass,
            GetObjectsToRender(),
            (secondaryCmd, obj) =>
            {
                // This code runs in parallel!
                secondaryCmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
                secondaryCmd.DrawIndexed(obj.IndexCount);
            }
        );
    }
}
```

### 2. Using ParallelCommandRecorder

```csharp
public class OptimizedRenderer : Renderer
{
    private ParallelCommandRecorder _parallelRecorder;

    protected override bool OnSetup()
    {
        _parallelRecorder = new ParallelCommandRecorder(Context!);
        return true;
    }

    protected override void OnRender(RenderContext context)
    {
        var cmd = context.CommandBuffer;
        var renderPass = new RenderPass(/*...*/);
        
        // Record 500 objects per secondary buffer
        var secondaryBuffers = _parallelRecorder.RecordBatched(
            renderPass,
            GetObjects(),
            batchSize: 500,
            (secondaryCmd, batch) =>
            {
                foreach (var obj in batch)
                {
                    // Record commands for this batch
                    secondaryCmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
                    secondaryCmd.DrawIndexed(obj.IndexCount);
                }
            }
        );

        // Execute all secondary buffers
        cmd.ExecuteCommands(secondaryBuffers);
    }
}
```

### 3. Adaptive Rendering (Recommended)

```csharp
public class SmartRenderer : Renderer
{
    private ParallelCommandRecorder _parallelRecorder;
    private const int PARALLEL_THRESHOLD = 1000;

    protected override void OnRender(RenderContext context)
    {
        var objects = GetObjects();
        
        if (objects.Count >= PARALLEL_THRESHOLD)
        {
            // Use parallel for large scenes
            RenderParallel(context, objects);
        }
        else
        {
            // Use sequential for small scenes
            RenderSequential(context, objects);
        }
    }

    private void RenderParallel(RenderContext context, List<Object> objects)
    {
        var renderPass = new RenderPass(/*...*/);
        var secondaryBuffers = _parallelRecorder.RecordBatched(
            renderPass, objects, 500,
            (cmd, batch) => { /* record commands */ }
        );
        context.CommandBuffer.ExecuteCommands(secondaryBuffers);
    }

    private void RenderSequential(RenderContext context, List<Object> objects)
    {
        var cmd = context.CommandBuffer;
        foreach (var obj in objects)
        {
            // Record commands directly
        }
    }
}
```

## Performance Tips

### 1. Batch Size
```csharp
// ❌ Too small - overhead dominates
batchSize: 10

// ✅ Good for most cases
batchSize: 250-500

// ✅ Good for uniform objects
batchSize: 500-1000

// ❌ Too large - reduces parallelism
batchSize: 10000
```

### 2. Group by Material
```csharp
var materialGroups = objects.GroupBy(o => o.MaterialId);

foreach (var group in materialGroups)
{
    var secondaryBuffers = _parallelRecorder.RecordBatched(
        renderPass,
        group.ToArray(),
        500,
        (cmd, batch) =>
        {
            cmd.BindRenderPipeline(group.Key.Pipeline);
            foreach (var obj in batch)
            {
                // Only bind buffers, not pipeline
                cmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
                cmd.DrawIndexed(obj.IndexCount);
            }
        }
    );
    
    primaryCmd.ExecuteCommands(secondaryBuffers);
}
```

### 3. Control Thread Count
```csharp
// Use all CPU cores
var secondaryBuffers = _parallelRecorder.RecordParallel(
    renderPass,
    objects,
    (cmd, obj) => { /* ... */ },
    maxDegreeOfParallelism: Environment.ProcessorCount
);

// Limit to 4 threads
var secondaryBuffers = _parallelRecorder.RecordParallel(
    renderPass,
    objects,
    (cmd, obj) => { /* ... */ },
    maxDegreeOfParallelism: 4
);
```

## Common Patterns

### Pattern 1: BIM Rendering
```csharp
// Group by floor level for logical organization
var floorGroups = bimElements.GroupBy(e => e.FloorLevel);

var secondaryBuffers = new List<ICommandBuffer>();

Parallel.ForEach(floorGroups, floor =>
{
    var cmd = context.CreateSecondaryCommandBuffer(renderPass);
    
    foreach (var element in floor)
    {
        cmd.BindVertexBuffer(0, element.VertexBuffer.Handle);
        cmd.DrawIndexed(element.IndexCount);
    }
    
    lock (secondaryBuffers) 
    {
        secondaryBuffers.Add(cmd);
    }
});

context.CommandBuffer.ExecuteCommands(secondaryBuffers.ToArray());
```

### Pattern 2: LOD Rendering
```csharp
var lodGroups = objects.GroupBy(o => o.LODLevel);

foreach (var group in lodGroups)
{
    var buffers = _parallelRecorder.RecordBatched(
        renderPass, group.ToArray(), 500,
        (cmd, batch) =>
        {
            cmd.BindRenderPipeline(GetPipelineForLOD(group.Key));
            foreach (var obj in batch)
            {
                cmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
                cmd.DrawIndexed(obj.IndexCount);
            }
        }
    );
    
    primaryCmd.ExecuteCommands(buffers);
}
```

## Troubleshooting

### "CommandBuffer is null"
```csharp
// ❌ Wrong - accessing before RendererManager sets it
protected override void OnSetup()
{
    var cmd = Context.AcquireCommandBuffer(); // Wrong timing
}

// ✅ Correct - access during render
protected override void OnRender(RenderContext context)
{
    var cmd = context.CommandBuffer; // Set by RendererManager
}
```

### "Cannot execute secondary buffers from secondary buffer"
```csharp
// ❌ Wrong - nesting not allowed
secondaryCmd.ExecuteCommands(otherSecondaryBuffers);

// ✅ Correct - only primary can execute secondary
primaryCmd.ExecuteCommands(secondaryBuffers);
```

### "ExecuteCommands must be called within render pass"
```csharp
// ❌ Wrong - outside render pass
primaryCmd.ExecuteCommands(secondaryBuffers);
primaryCmd.BeginRendering(...);

// ✅ Correct - inside render pass
primaryCmd.BeginRendering(...);
primaryCmd.ExecuteCommands(secondaryBuffers);
primaryCmd.EndRendering();
```

## Performance Checklist

- [ ] Only use for 1000+ objects
- [ ] Batch size between 250-1000
- [ ] Group by material/pipeline first
- [ ] Limit thread count to physical cores
- [ ] Profile before and after
- [ ] Consider CPU vs GPU bottleneck

## Next Steps

1. **Read the full docs**: `SECONDARY_COMMAND_BUFFERS.md`
2. **Study examples**: `Examples/SecondaryCommandBufferExample.cs`
3. **Profile your app**: Measure actual performance gain
4. **Start simple**: Begin with `RecordAndExecuteParallel()`
5. **Optimize gradually**: Move to `ParallelCommandRecorder` when needed

## Getting Help

- Check `SECONDARY_COMMAND_BUFFERS.md` for detailed documentation
- Review `Examples/SecondaryCommandBufferExample.cs` for working code
- Profile with tools to identify bottlenecks
- Remember: Not every scene benefits from parallel recording!

## Example: Complete Renderer

```csharp
public class ProductionRenderer : Renderer
{
    private ParallelCommandRecorder _recorder;
    private RenderPipelineResource _pipeline;
    
    public override RenderStages Stage => RenderStages.Opaque;
    public override string Name => "ProductionRenderer";

    protected override bool OnSetup()
    {
        _recorder = new ParallelCommandRecorder(Context!);
        // Create pipeline...
        return true;
    }

    protected override void OnRender(RenderContext context)
    {
        var objects = GetVisibleObjects(context);
        
        if (objects.Count < 500)
        {
            RenderDirect(context.CommandBuffer!, objects);
            return;
        }

        var renderPass = new RenderPass(
            new RenderPass.AttachmentDesc { LoadOp = LoadOp.Load }
        );

        var buffers = _recorder.RecordBatched(
            renderPass, objects, 500,
            (cmd, batch) =>
            {
                cmd.BindRenderPipeline(_pipeline.Handle);
                foreach (var obj in batch)
                {
                    cmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
                    cmd.BindIndexBuffer(obj.IndexBuffer.Handle, IndexFormat.UI32);
                    cmd.PushConstants(obj.Transform);
                    cmd.DrawIndexed(obj.IndexCount);
                }
            }
        );

        context.CommandBuffer!.ExecuteCommands(buffers);
    }

    private void RenderDirect(ICommandBuffer cmd, List<Object> objects)
    {
        cmd.BindRenderPipeline(_pipeline.Handle);
        foreach (var obj in objects)
        {
            cmd.BindVertexBuffer(0, obj.VertexBuffer.Handle);
            cmd.DrawIndexed(obj.IndexCount);
        }
    }

    protected override void OnTearDown()
    {
        _recorder?.Dispose();
        _pipeline?.Dispose();
    }
}
```

That's it! You're ready to use secondary command buffers for better performance. 🚀
